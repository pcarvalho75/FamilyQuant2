using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

// A development inventory, never a producer of scientific results. Scan the frozen source,
// not the moving 2.0 views. Keep observations separate from their acceptance evidence.
internal static class FeatureLedger
{
    internal const string ReferenceCommit = "e9b73064d8ff19b2fde3e3231240b636a64c0a91";
    internal const string ReferenceInventorySha256 = "B7B572E352CB394AD46CB4D7CE04394929A7462F8E0326A48635FC61EA871000";
    internal const string GenerateArgument = "--feature-ledger-generate";
    internal const string DropApproval = "Paulo via orchestrator M4, 2026-11-01 11:20:38: retire the De Novo Extract areas and Partition ion current sections in 2.0; 1.5 keeps them. Attribute to families stays and its CSV moves to Results in C11.";
    internal const string Du7Retirement = "DU7 frozen FamilyQuant 1.5 reader compatibility check retired: Paulo, 11-07: forget familyquant 1.5 (D80). FCF saves now use curation format 2; current-reader preservation checks remain required.";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex Tokens = new(
        "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|@\"(?:\"\"|[^\"])*\"|\\$?\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])'",
        RegexOptions.Compiled);
    private static readonly HashSet<string> Controls = new(StringComparer.Ordinal)
    {
        "MenuItem", "Button", "CheckBox", "RadioButton", "ComboBox", "ComboBoxItem", "TextBox",
        "IntegerUpDown", "DoubleUpDown", "DataGrid", "DataGridTextColumn", "DataGridTemplateColumn",
        "DataGridCheckBoxColumn", "KeyBinding", "MouseBinding", "Expander", "TabItem"
    };
    private static readonly HashSet<string> Facts = new(StringComparer.Ordinal)
    {
        "ToolTip", "Header", "Content", "Text", "InputGestureText", "Command", "CommandParameter",
        "Key", "Modifiers", "IsChecked", "SelectedIndex", "SelectedValue", "Value", "Minimum", "Maximum",
        "IsReadOnly", "IsEnabled", "Binding", "ItemsSource", "SortMemberPath", "ClipboardContentBinding"
    };
    private static readonly HashSet<string> Events = new(StringComparer.Ordinal)
    {
        "Click", "Checked", "Unchecked", "SelectionChanged", "TextChanged", "ValueChanged", "KeyDown",
        "PreviewKeyDown", "MouseDoubleClick", "CellEditEnding", "BeginningEdit", "LoadingRow", "Executed",
        "CanExecute", "Loaded", "Closing", "Drop", "DragOver", "SelectedCellsChanged", "Sorting"
    };

    internal sealed record Observation(string Id, string Source, int Line, string Kind, string Detail);
    internal sealed record SourceFile(string Path, string Sha256, int Observations);
    internal sealed record Inventory(string Reference, string Scanner, List<SourceFile> Files,
        List<Observation> Observations);

    internal static int Generate(string sourceRoot, string repoRoot)
    {
        Inventory inventory = Scan(sourceRoot);
        string docs = Path.Combine(repoRoot, "docs");
        string ledger = Path.Combine(docs, "FEATURE-LEDGER.md");
        string markdown = Markdown(inventory, File.Exists(ledger) ? File.ReadAllText(ledger) : null);
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "feature-ledger-scan.json"),
            JsonSerializer.Serialize(inventory, JsonOptions) + "\n", new UTF8Encoding(false));
        File.WriteAllText(ledger, markdown, new UTF8Encoding(false));
        Console.WriteLine($"Feature ledger: {inventory.Files.Count} source files, {inventory.Observations.Count} observations and rows.");
        return 0;
    }

    internal static Inventory Scan(string sourceRoot)
    {
        string product = Path.Combine(sourceRoot, "FamilyQuant");
        if (!Directory.Exists(product)) throw new DirectoryNotFoundException(product);
        var rows = new List<Observation>();
        var files = new List<SourceFile>();
        foreach (string full in Directory.GetFiles(product, "*", SearchOption.AllDirectories)
            .Where(p => (p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                         p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) &&
                        !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            string path = Path.GetRelativePath(sourceRoot, full).Replace('\\', '/');
            string text = File.ReadAllText(full);
            int before = rows.Count;
            void Add(int offset, string kind, string detail)
            {
                int line = 1 + text.AsSpan(0, Math.Clamp(offset, 0, text.Length)).Count('\n');
                string key = path + "|" + offset + "|" + kind;
                string id = "FQ15-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12];
                rows.Add(new Observation(id, path, line, kind, detail));
            }
            if (path.EndsWith(".xaml", StringComparison.Ordinal))
            {
                var document = XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
                foreach (XElement node in document.Descendants())
                {
                    string type = node.Name.LocalName;
                    int offset = Offset(text, ((IXmlLineInfo)node).LineNumber, ((IXmlLineInfo)node).LinePosition);
                    string name = node.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value ?? type;
                    if (Controls.Contains(type)) Add(offset, "control", type + " " + name);
                    foreach (XAttribute attribute in node.Attributes())
                    {
                        string property = attribute.Name.LocalName;
                        if (!Facts.Contains(property) && !Events.Contains(property)) continue;
                        var info = (IXmlLineInfo)attribute;
                        Add(Offset(text, info.LineNumber, info.LinePosition),
                            Events.Contains(property) ? "handler" : property == "ToolTip" ? "tooltip" :
                            type.Contains("Column", StringComparison.Ordinal) ? "column" : "option/copy",
                            name + "." + property + " = " + attribute.Value);
                    }
                }
            }
            else
            {
                bool ui = text.Contains("using System.Windows", StringComparison.Ordinal) ||
                          path.EndsWith(".xaml.cs", StringComparison.Ordinal) ||
                          Path.GetFileNameWithoutExtension(path) is "HelpContent" or "ReportView" or
                          "NormalizationMode" or "SharedPeptidePolicy" or "QuantitationProfile" or "DeNovoSource";
                // Export and storage builders are included even when they have no WPF dependency.
                bool export = Regex.IsMatch(text, @"\b\w*(?:Csv|CSV)\w*\b|\b(?:SerializeObject|DeserializeObject)\b") ||
                              path.EndsWith("ProjectStore.cs", StringComparison.Ordinal);
                var strings = new List<(int Offset, string Text)>();
                var code = text.ToCharArray();
                foreach (Match token in Tokens.Matches(text))
                {
                    bool literal = token.Value[0] == '"' || token.Value.StartsWith("@\"", StringComparison.Ordinal) ||
                                   token.Value.StartsWith("$\"", StringComparison.Ordinal);
                    if (literal) strings.Add((token.Index, token.Value));
                    // Mask comments and literals before looking for event bindings and control factories.
                    for (int i = token.Index; i < token.Index + token.Length; i++)
                        if (code[i] != '\r' && code[i] != '\n') code[i] = ' ';
                }
                foreach (var literal in strings)
                {
                    bool format = Regex.IsMatch(literal.Text,
                        @"\.(fcf|fqproj|plp|sepr2|xic2|raw|fasta|fa|fas|T-R|csv|tsv|txt|png|json|sqt|uc|aln|yada|ms2|mzml|zip)\b",
                        RegexOptions.IgnoreCase);
                    if (!ui && !export && !format) continue;
                    // Keep the complete source literal, including dynamic placeholders. Never infer its value.
                    if (literal.Text is "\"\"" or "\" \"" or "\"\\n\"" or "\"\\r\\n\"") continue;
                    Add(literal.Offset, format ? "format/export" : path.EndsWith("HelpContent.cs", StringComparison.Ordinal)
                        ? "help fact" : export && !ui ? "export/storage fact" : "code fact", literal.Text);
                }
                string masked = new(code);
                if (ui)
                {
                    foreach (Match match in Regex.Matches(masked,
                        @"\bnew\s+(Button|MenuItem|CheckBox|RadioButton|ComboBox|TextBox|DataGrid\w*Column|DataGrid|KeyBinding)\b"))
                        Add(match.Index, "code control", match.Value);
                    foreach (Match match in Regex.Matches(masked,
                        @"\b(?:\w+\.)?(?:Click|Checked|Unchecked|SelectionChanged|TextChanged|ValueChanged|KeyDown|PreviewKeyDown|Closing|Executed|CanExecute)\s*\+="))
                        Add(match.Index, "handler", Line(text, match.Index));
                    foreach (Match match in Regex.Matches(masked, @"\b(?:Key|ModifierKeys)\.\w+"))
                        Add(match.Index, "shortcut", Line(text, match.Index));
                    foreach (Match match in Regex.Matches(masked,
                        @"\b(?:private|public|internal|protected)\s+(?:static\s+)?(?:async\s+)?(?:void|Task|bool)\s+\w+(?:_Click|_Changed|_KeyDown|_Executed|_Closing)\s*\("))
                        Add(match.Index, "handler definition", Line(text, match.Index));
                }
                if (path.EndsWith("HelpContent.cs", StringComparison.Ordinal))
                    foreach (Match match in Regex.Matches(masked, @"public\s+static\s+string\s+(Build|BuildDeNovo)\s*\("))
                        Add(match.Index, "help page", match.Value);
            }
            files.Add(new SourceFile(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))), rows.Count - before));
        }
        return new Inventory(ReferenceCommit, "FeatureLedger.Scan/v1", files, rows);
    }

    private static int Offset(string text, int line, int column)
    {
        int offset = 0;
        for (int i = 1; i < line; i++) offset = text.IndexOf('\n', offset) + 1;
        return offset + Math.Max(0, column - 1);
    }

    private static string Line(string text, int offset)
    {
        int start = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        int end = text.IndexOf('\n', offset);
        return text[start..(end < 0 ? text.Length : end)].Trim();
    }

    private static string Escape(string value)
    {
        var result = new StringBuilder();
        foreach (char c in value)
            result.Append(c > 126 || c < 32 ? "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) : c.ToString());
        return result.ToString().Replace("|", "&#124;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("`", "&#96;");
    }

    internal static string CheckFor(Observation row)
    {
        string file = Path.GetFileNameWithoutExtension(row.Source).Replace(".xaml", "");
        string? screen = file switch
        {
            "MainWindow" or "HomeControl" or "App" => "01-home",
            "USearch" => "02-build",
            "ClusterGenerationControl" or "GridCopy" or "ReportView" or "NormalizationMode" or
                "SharedPeptidePolicy" or "QuantitationProfile" => "03-curation",
            "DeNovoControl" or "DeNovoSource" => "06-denovo-advanced",
            "AnalyticsControl" => "07-analytics", "ComputeXicWindow" => "08-compute",
            "SuperClusterPreviewWindow" => "09-super-cluster-preview", "LockManagerWindow" => "10-lock-manager",
            "SpectrumComparisonWindow" => "11-spectrum-table", "IdentityScanWindow" => "12-identity-scan",
            "TextReportWindow" => "13-text-report", "HelpContent" => "14-help", _ => null
        };
        if (screen != null) return "render:" + screen;
        string test = file switch
        {
            "ProjectStore" => "ProjectStoreTests", "FamilyQuantFile" => "RoundTripTests",
            "USearchTools" => "DatabaseTests", "FullXicComputer" => "FullXicRunMatchTests",
            "ThreeWayChart" or "ThreeWayWindow" or "ThreeWayPartition" => "ThreeWayPartitionTests",
            "DeNovoReconciler" or "PeaksDeNovoReader" or "SeproPsmAdapter" or "PeaksDatabasePsmReader" => "DeNovoTests",
            "Ms1EnvelopeDetector" => "Ms1EnvelopeUnitTests",
            "SpreadsheetSafeCsv" => "CsvTests",
            "SpectrumComparisonTable" or "DeNovoAcceptedScoreScatterStudy" or "DeNovoSourceVennStudy" or
                "DeNovoTalkComparison" or "DeNovoVariantComparison" or "PsmDeNovoCounterpartStudy" or
                "SetNoveltyStudy" => "ProjectStoreTests",
            "IdentificationConcordanceStudy" => "IdentificationConcordanceUnitTests",
            "PatternLabRunQuants" => "RecordedDenominatorTests",
            "IdentityScanChart" => "IdentityScanPlotTests", "FamilyContributionChart" => "FamilyContributionChartTests",
            "ChartPalette" => "ChartPaletteTests", "ChartImageExporter" => "ChartImageExportTests",
            "AnalyticsFigures" or "PresentationFigures" or "PresentationEvidencePack" or "RecoveryChart" or
                "IsotopeCountChart" or "PercentIonCurrentChart" or "XicMassChart" => "AnalyticsRenderTests",
            _ => "DeNovoAgreementUnitTests"
        };
        return "test:" + test;
    }

    // Frozen source ranges identify the two approved UI subtrees and their exclusive view handlers.
    // Static extraction/partition engines, shared state, test seams and explanatory science stay KEEP.
    internal static bool IsApprovedDrop(Observation row) => row.Source switch
    {
        "FamilyQuant/DeNovoControl.xaml" => row.Line is >= 314 and <= 388 or >= 506 and <= 605,
        "FamilyQuant/DeNovoControl.xaml.cs" => row.Line is >= 798 and <= 834 or >= 1291 and <= 1344
            or >= 1415 and <= 1510 or >= 1788 and <= 1834 or >= 1899 and <= 2083
            or >= 2158 and <= 2200 or >= 2226 and <= 2269,
        _ => false
    };

    internal static string Markdown(Inventory inventory)
    {
        var md = new StringBuilder();
        md.AppendLine("# FamilyQuant 1.5.0.6 feature ledger");
        md.AppendLine();
        md.AppendLine("Contract for 2.0. Only Paulo may authorize DROP. KEEP preserves behavior; IMPROVE preserves the function while its presentation changes.");
        md.AppendLine("Retained rows start OPEN. A planned check is not evidence that behavior passed. Close a retained row only after its owner records an executed test or an inspected render and reviewer approval. Approved DROP rows record Paulo's decision as evidence, without claiming their implementation is removed yet.");
        md.AppendLine("Approved exception: " + DropApproval);
        md.AppendLine("Retired compatibility check: " + Du7Retirement);
        md.AppendLine("DROP covers the two frozen XAML section subtrees and their exclusive view handlers/tooltips. Shared extraction/partition engines, test seams, explanatory science and Attribute to families remain preserved. The guard rejects DROP outside that scope. Actual retirement is C11.");
        md.AppendLine();
        md.AppendLine("Frozen source: `" + inventory.Reference + "` in the old FamilyQuant repository, matching the read-only reference README. The frozen app and tests are in `D:/FamilyQuant-data/reference-1.5.0.6`.");
        md.AppendLine("Source hashes, per-file counts and exact observations: `docs/feature-ledger-scan.json`. Scan includes all product XAML and C# files, including code-only windows, event wiring, key handling, help, option copy, export/storage builders and file-extension literals. Zero-observation files remain in the manifest.");
        md.AppendLine("XAML: one control row plus each behavioral/copy/tooltip attribute. C#: literal tokens in UI/help/option/export code, every file-format literal, code-created controls, event hooks, handler definitions and key references. Literal fragments remain separate and retain source syntax; internal strings are deliberately over-included. This is a static preservation inventory, not a proof that each code branch is reachable.");
        md.AppendLine("Unicode in frozen copy is escaped so old punctuation and icons are recorded without introducing them into new UI. Formatting and color attributes are excluded; they are not preserved appearance requirements.");
        md.AppendLine();
        md.AppendLine("Regenerate from a clean source export of that commit: `FamilyQuant.Tests.exe --feature-ledger-generate <frozen-source-root> <2.0-repo-root>`. Never regenerate from moving 2.0 source. The default suite checks source counts, stable IDs, exact row coverage, dispositions and check targets. When `FAMILYQUANT_LEDGER_REFERENCE_SOURCE` points to the source export, it also repeats the scan and compares every observation and source hash.");
        md.AppendLine("Render targets refer to `ScreenHarness.Screens`, captured with `--screens <output>`, in both themes and sizes. Test targets name existing suite methods in the relevant area. A target identifies where the owner must check that fact; it does not assert that the current method covers every detail. A render checks visible presentation only; interactions, numbers and compatibility still need executable checks before closure. Extend or replace a broad target with the exact assertion before marking a row VERIFIED.");
        md.AppendLine("Release gate: `FamilyQuant.Tests.exe --feature-ledger-release-check` fails until every retained row is VERIFIED with evidence. Authorized DROP rows use APPROVED_DROP, which records a decision, not a completed UI removal. The normal quick suite permits OPEN rows during development. Regeneration resets retained rows to OPEN; preserve reviewed evidence when updating the scanner.");
        md.AppendLine();
        md.AppendLine("Scan count: **" + inventory.Observations.Count + "**. Ledger rows: **" + inventory.Observations.Count + "**. Source files: **" + inventory.Files.Count + "**.");
        md.AppendLine("Authorized DROP rows: **" + inventory.Observations.Count(IsApprovedDrop) + "**; the remaining rows stay OPEN.");
        md.AppendLine();
        md.AppendLine("| ID | Disposition | Kind | Frozen source | Fact or control | Planned check | State | Evidence |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (Observation row in inventory.Observations)
        {
            bool improve = row.Kind is "control" or "code control" or "option/copy" or "code fact" or "column" or "tooltip" or "help fact";
            bool drop = IsApprovedDrop(row);
            md.AppendLine("| " + row.Id + " | " + (drop ? "DROP" : improve ? "IMPROVE" : "KEEP") + " | " + row.Kind + " | " +
                row.Source + ":" + row.Line + " | " + Escape(row.Detail) + " | " + CheckFor(row) +
                (drop ? " | APPROVED_DROP | " + DropApproval + " |" : " | OPEN | pending |"));
        }
        return md.ToString().Replace("\r\n", "\n");
    }

    /// <summary>The existing added-contracts tail is its single source of truth. Preserve its text and line
    /// endings verbatim after the unchanged frozen renderer; never infer additions from frozen observations.</summary>
    internal static string Markdown(Inventory inventory, string? existingMarkdown)
    {
        var sections = Regex.Matches(existingMarkdown ?? "", @"^## Added contracts after the frozen source\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        if (sections.Count > 1)
            throw new InvalidDataException("The ledger has multiple added-contracts sections; regeneration cannot choose one.");
        // Validate before Generate writes either output. Frozen rendering retains its documented OPEN reset.
        return Markdown(inventory) + (sections.Count == 0 ? "" : "\n" + existingMarkdown![sections[0].Index..]);
    }

    /// <summary>Set to <see cref="ReceiptsReleaseOnlyValue"/>, quick gates skip the receipt-currency checks of the
    /// ordinary suite, which hash every receipt's artifacts. The release check and every other guard always run.</summary>
    internal const string ReceiptsVariable = "FAMILYQUANT_LEDGER_RECEIPTS";
    internal const string ReceiptsReleaseOnlyValue = "release-only";
    internal static bool ReceiptsReleaseOnly => string.Equals(Environment.GetEnvironmentVariable(ReceiptsVariable),
        ReceiptsReleaseOnlyValue, StringComparison.Ordinal);

    internal const string DeferredReleaseReason = "not re-proved for 2.1.11; receipts lost after 2.1.10; verify again after release";
    internal const string DeferredReleaseList = "docs/LEDGER-REVERIFY-2.1.11.md";

    internal sealed record ReleaseDeferrals(IReadOnlySet<string> Ids, bool VersionAllowed, IReadOnlyList<string> Errors)
    {
        internal bool Accepts(string id, string reason) => VersionAllowed && Errors.Count == 0
            && string.Equals(reason, DeferredReleaseReason, StringComparison.Ordinal) && Ids.Contains(id);
    }

    internal static ReleaseDeferrals LoadReleaseDeferrals(string? root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        bool versionAllowed = Version.TryParse(AppVersionInfo.Current(), out var version)
            && version.Major == 2 && version.Minor == 1 && version.Build == 11 && version.Revision <= 0;
        string? path = root == null ? null : Path.Combine(root, DeferredReleaseList);
        if (path != null && File.Exists(path))
        {
            foreach (string line in File.ReadLines(path).Where(line => line.StartsWith("| FQ15-", StringComparison.Ordinal)))
            {
                string[] cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
                if (cells.Length != 4 || !Regex.IsMatch(cells[1], @"^FQ15-[0-9A-F]{12}$", RegexOptions.CultureInvariant))
                { errors.Add("Malformed D90 deferred ID row: " + line); continue; }
                if (!ids.Add(cells[1])) errors.Add("Duplicate D90 deferred ID: " + cells[1]);
            }
        }
        return new(ids, versionAllowed, errors);
    }

    private static string[][] LedgerRows(string markdown) => markdown.Split('\n')
        .Where(line => line.StartsWith("| FQ15-", StringComparison.Ordinal))
        .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray()).ToArray();

    internal static string ReleaseSummary(string markdown, ReleaseDeferrals deferrals)
    {
        var rows = LedgerRows(markdown).Where(row => row.Length == 10).ToArray();
        return "Feature ledger release: VERIFIED=" + rows.Count(row => row[7] == "VERIFIED").ToString(CultureInfo.InvariantCulture)
            + "; DEFERRED=" + rows.Count(row => row[7] == "OPEN" && deferrals.Accepts(row[1], row[8])).ToString(CultureInfo.InvariantCulture)
            + "; LIST=" + DeferredReleaseList + "; D90.";
    }

    /// <param name="checkReceipts">False leaves out the receipt-currency check of VERIFIED rows (and the receipt
    /// load it needs); every other check still runs.</param>
    internal static List<string> Validate(Inventory inventory, string markdown, bool requireClosed = false,
        FeatureLedgerEvidence.Registry? evidenceOverride = null, bool checkReceipts = true, ReleaseDeferrals? releaseDeferrals = null)
    {
        var errors = new List<string>();
        string[][] rows = LedgerRows(markdown);
        if (inventory.Reference != ReferenceCommit) errors.Add("Wrong frozen reference.");
        if (Fingerprint(inventory) != ReferenceInventorySha256) errors.Add("Frozen inventory fingerprint differs.");
        if (inventory.Files.Sum(f => f.Observations) != inventory.Observations.Count) errors.Add("Source scan counts differ.");
        if (rows.Length != inventory.Observations.Count) errors.Add("Ledger row count differs from the scan.");
        if (rows.Select(r => r[1]).Distinct().Count() != rows.Length) errors.Add("Duplicate ledger ID.");
        if (inventory.Observations.Select(r => r.Id).Distinct().Count() != inventory.Observations.Count) errors.Add("Duplicate scan ID.");
        var byId = inventory.Observations.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        string? evidenceRoot = FeatureLedgerEvidence.FindRoot();
        var deferrals = requireClosed ? releaseDeferrals ?? LoadReleaseDeferrals(evidenceRoot) : null;
        if (deferrals != null)
        {
            errors.AddRange(deferrals.Errors);
            var openIds = rows.Where(row => row.Length == 10 && row[7] == "OPEN").Select(row => row[1]).ToHashSet(StringComparer.Ordinal);
            foreach (string id in deferrals.Ids.Where(id => !openIds.Contains(id)).Order(StringComparer.Ordinal))
                errors.Add("D90 list entry is not OPEN in the ledger: " + id);
        }
        var evidence = evidenceOverride ?? (checkReceipts && evidenceRoot != null && rows.Any(r => r.Length == 10 && r[7] == "VERIFIED")
            ? FeatureLedgerEvidence.Load(evidenceRoot, inventory) : null);
        foreach (string[] row in rows)
        {
            if (row.Length != 10) { errors.Add("Malformed row."); continue; }
            if (!byId.TryGetValue(row[1], out Observation? observation)) { errors.Add("Unknown ID: " + row[1]); continue; }
            if (row[2] is not ("KEEP" or "IMPROVE" or "DROP") ||
                row[2] == "DROP" && (!IsApprovedDrop(observation) || row[8] != DropApproval))
                errors.Add("Disposition requires Paulo: " + row[1]);
            if (IsApprovedDrop(observation) && row[2] != "DROP") errors.Add("Paulo's retirement decision is missing: " + row[1]);
            if (row[3] != observation.Kind || row[4] != observation.Source + ":" + observation.Line || row[5] != Escape(observation.Detail))
                errors.Add("Frozen fact changed: " + row[1]);
            bool validCheck = row[6].StartsWith("render:", StringComparison.Ordinal)
                ? ScreenHarness.Screens.Contains(row[6][7..], StringComparer.Ordinal)
                : row[6].StartsWith("test:", StringComparison.Ordinal) &&
                  typeof(Program).GetMethod(row[6][5..], System.Reflection.BindingFlags.Static |
                      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) != null;
            if (!validCheck) errors.Add("Unknown check target: " + row[1]);
            if (row[7] is not ("OPEN" or "VERIFIED" or "APPROVED_DROP")) errors.Add("Unknown state: " + row[1]);
            if ((row[7] == "APPROVED_DROP") != (row[2] == "DROP")) errors.Add("Retirement state differs: " + row[1]);
            if (requireClosed && row[7] is not ("VERIFIED" or "APPROVED_DROP")
                && !(row[7] == "OPEN" && deferrals?.Accepts(row[1], row[8]) == true))
                errors.Add("Release has an open row: " + row[1]);
            if (row[7] == "VERIFIED" && (row[8] == "pending" || row[8].Length < 12))
                errors.Add("Verified row lacks behavioral evidence: " + row[1]);
            if (checkReceipts && row[7] == "VERIFIED" && evidence?.IsVerified(row[1], row[8]) != true)
                errors.Add("Verified row lacks a current executed receipt: " + row[1]);
        }
        foreach (SourceFile file in inventory.Files)
            if (file.Observations != inventory.Observations.Count(o => o.Source == file.Path)) errors.Add("File count differs: " + file.Path);
        return errors;
    }

    internal static string Fingerprint(Inventory inventory) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(inventory))));
}
