using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record DeNovoWorkflowFact(FeatureLedger.Observation Row, string Member, string Target,
        string Property, string[] Phases);
    private static readonly JsonSerializerOptions DeNovoBridgeJson = new()
    { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private static int DeNovoLedgerWorkflowCommand(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Supply frozen source root, frozen app folder, fresh output and a timestamp from date.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row)
            && row.Source is "FamilyQuant/DeNovoControl.xaml" or "FamilyQuant/DeNovoControl.xaml.cs").ToArray();
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
            if (FeatureLedgerEvidence.Hash(Path.Combine(frozenRoot, file.Path)) != file.Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + file.Path);
        Directory.CreateDirectory(output);
        string oldSource = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/DeNovoControl.xaml.cs"));
        var oldXaml = XDocument.Load(Path.Combine(frozenRoot, "FamilyQuant/DeNovoControl.xaml"), LoadOptions.SetLineInfo);
        string legacyPath = Path.Combine(Path.GetFullPath(args[2]), "FamilyQuant.dll");
        var legacyAssembly = Assembly.LoadFrom(legacyPath);
        var legacyType = legacyAssembly.GetType("FamilyQuant.DeNovoControl", true)!;
        var phases = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ApplyImport"] = ["import-positive", "import-empty"],
            ["ApplyReconciliation"] = ["reconcile-positive", "reconcile-empty"],
            ["ApplyHomology"] = ["homology-positive", "homology-empty"],
            ["ClearReconciliation"] = ["reset-by-import"], ["ClearHomology"] = ["reset-by-reconcile"],
            ["UpdateSourceUi"] = ["source-change"], ["CriteriaSentence"] = ["criteria"],
        };
        var assignments = Regex.Matches(oldSource, @"(?m)^\s*(?<field>(?:Label|TextBlock)\w+)\.(?<property>Content|Text)\s*=[\s\S]*?;|^\s*(?<status>SetStatus)\([\s\S]*?\);")
            .Cast<Match>().Select(match => new
            {
                Target = match.Groups["status"].Success ? "TextBlockDeNovoStatus" : match.Groups["field"].Value,
                Property = match.Groups["status"].Success ? "Text" : match.Groups["property"].Value,
                First = 1 + oldSource.AsSpan(0, match.Index).Count('\n'),
                Last = 1 + oldSource.AsSpan(0, match.Index + match.Length).Count('\n'),
            }).ToArray();
        var facts = new List<DeNovoWorkflowFact>();
        foreach (var row in cohort)
        {
            if (row.Source.EndsWith(".cs", StringComparison.Ordinal) && row.Kind == "code fact")
            {
                string member = LedgerMemberFactComparison.Compare(row, oldSource, new Dictionary<string, string>()).FrozenMember;
                string name = phases.Keys.SingleOrDefault(name => member.Contains(" " + name + " (", StringComparison.Ordinal)) ?? "";
                if (name == "CriteriaSentence") facts.Add(new(row, name, name, "return", phases[name]));
                else if (name.Length > 0)
                {
                    var targets = assignments.Where(statement => row.Line >= statement.First && row.Line <= statement.Last).ToArray();
                    if (targets.Length == 1) facts.Add(new(row, name, targets[0].Target, targets[0].Property, phases[name]));
                }
            }
            else if (row.Kind == "column" && row.Detail.Contains(".Binding = ", StringComparison.Ordinal))
            {
                string name = row.Detail[..row.Detail.IndexOf(".Binding", StringComparison.Ordinal)];
                var nodes = oldXaml.Descendants().Where(node => LedgerXmlName(node) == name).ToArray();
                string owner = nodes.Length == 1 ? LedgerXmlName(nodes[0].Ancestors().First(node => node.Name.LocalName == "DataGrid")) ?? "" : "";
                if (owner is "DataGridReviewQueue" or "DataGridHomologyFamilies")
                    facts.Add(new(row, "binding", name, "rendered cells", [owner == "DataGridReviewQueue" ? "reconcile-positive" : "homology-positive"]));
            }
        }
        var before = facts.ToDictionary(fact => fact.Row.Id, _ => new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var after = facts.ToDictionary(fact => fact.Row.Id, _ => new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var original = Console.Out; var originalCulture = CultureInfo.CurrentCulture;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var results = new List<DeNovoDeclarationResult>(); var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        var extra = new List<string> { legacyPath };
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript)); EnsureApplication();
            Check("De novo workflow baseline identifies the exact frozen commit", (legacyAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
                .EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal));
            foreach (string culture in new[] { "en-US", "fr-FR" })
            foreach (var source in new[] { DeNovoSource.DenovoOnly, DeNovoSource.DeNovoAll, DeNovoSource.DeepNovo })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); Themes.ThemeManager.Apply(Themes.AppTheme.Dark);
                string caseId = culture + "/" + source.Id;
                var legacy = (UserControl)Activator.CreateInstance(legacyType)!; var current = new DeNovoControl();
                var oldWindow = LedgerDeNovoWindow(legacy); var newWindow = LedgerDeNovoWindow(current);
                try
                {
                    oldWindow.Show(); newWindow.Show(); PumpDispatcher();
                    foreach (var expander in WalkTree(legacy).Concat(WalkTree(current)).OfType<Expander>()) expander.IsExpanded = true;
                    foreach (var control in new[] { legacy, current })
                    {
                        var combo = (ComboBox)control.FindName("ComboBoxDeNovoSource");
                        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == source.Id);
                        foreach (var box in new[] { ("TextBoxMinLength", "8"), ("TextBoxMinAlc", "80"), ("TextBoxStretchLength", "6"), ("TextBoxStretchFloor", "95.5") })
                            ((TextBox)control.FindName(box.Item1)).Text = box.Item2;
                    }
                    PumpDispatcher();
                    bool oldCriteria = (bool)legacyType.GetMethod("ReadCriteriaFromBoxes")!.Invoke(legacy, null)!;
                    Check("De novo workflow criteria accepted " + caseId, oldCriteria && current.ReadCriteriaFromBoxes());
                    Capture("criteria");
                    var imported = LedgerDeNovoImport(source);
                    var psms = imported.Kept.Take(4).Select((row, index) => new PatternLabPsm
                    {
                        FileName = row.RunName, ScanNumber = row.Scan, ChargeState = row.Charge,
                        PrecursorMz = index == 3 ? 600 : row.Mz + 0.001,
                        Sequence = index == 2 ? "AAAAAAAA" : row.Peptide, XCorr = index == 1 ? 1 : 4,
                    }).ToList();
                    var raw = DeNovoScreenOperations.Reconcile(imported.Kept, psms, "fixture.sepr2", imported.RowsRead, current.Criteria);
                    var oldReconcile = legacyType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Single(method => method.Name == "Reconcile" && method.GetParameters().Length == 5);
                    object?[] oldArguments = { imported.Kept, psms, "fixture.sepr2", imported.RowsRead, current.Criteria };
                    var parameters = oldReconcile.GetParameters();
                    for (int index = 0; index < oldArguments.Length; index++) oldArguments[index] = LedgerLegacyValue(oldArguments[index], parameters[index].ParameterType);
                    object oldOutcome = oldReconcile.Invoke(null, oldArguments)!;
                    Check("De novo workflow all frozen reconciliation members agree " + caseId,
                        LedgerLegacyJsonAgrees(JsonSerializer.SerializeToElement(oldOutcome, oldOutcome.GetType(), DeNovoBridgeJson),
                            JsonSerializer.SerializeToElement(raw, DeNovoBridgeJson)));
                    Check("De novo workflow fixture exercises every verdict " + caseId, raw.Counts.Count == 5 && raw.Counts.Values.All(count => count == 1) && raw.ReviewQueue.Count == 2);
                    object oldImport = Apply("ApplyImport", imported); Capture("import-positive");
                    legacyType.GetMethod("ApplyReconciliation")!.Invoke(legacy, [oldOutcome]);
                    current.ApplyReconciliation(DeNovoInspectionAudit.AttachSources(raw, imported)); PumpDispatcher(); Capture("reconcile-positive");
                    var homology = LedgerDeNovoHomology(); object oldHomology = Apply("ApplyHomology", homology); Capture("homology-positive");

                    // Exercise the preserved legacy-data export branch, separately from the new source-aware columns.
                    current.ApplyReconciliation(DeNovoScreenOperations.Reconcile(imported.Kept, psms, "fixture.sepr2", imported.RowsRead, current.Criteria));
                    current.ApplyHomology(homology);
                    string files = Path.Combine(output, "exports", culture, source.Id); Directory.CreateDirectory(files);
                    foreach (string method in new[] { "ExportSummaryCsv", "ExportReviewQueueCsv", "ExportOverlapStudyCsv", "ExportHomologyCsv" })
                    {
                        string oldPath = Path.Combine(files, "frozen-" + method + ".csv"), newPath = Path.Combine(files, "current-" + method + ".csv");
                        legacyType.GetMethod(method)!.Invoke(legacy, [oldPath]); typeof(DeNovoControl).GetMethod(method)!.Invoke(current, [newPath]);
                        Check("De novo workflow native CSV bytes " + caseId + "/" + method, File.ReadAllBytes(oldPath).SequenceEqual(File.ReadAllBytes(newPath)));
                        extra.Add(oldPath); extra.Add(newPath);
                    }
                    legacyType.GetMethod("ApplyReconciliation")!.Invoke(legacy, [oldOutcome]); current.ApplyReconciliation(raw); PumpDispatcher(); Capture("reset-by-reconcile");
                    Apply("ApplyImport", imported); Capture("reset-by-import");
                    Apply("ApplyImport", new DeNovoImport()); Capture("import-empty");
                    Apply("ApplyReconciliation", DeNovoScreenOperations.Reconcile(new List<PeaksDeNovoRow>(), new List<PatternLabPsm>(), "empty.sepr2", 0, current.Criteria)); Capture("reconcile-empty");
                    Apply("ApplyHomology", new DeNovoHomology()); Capture("homology-empty");
                    foreach (var control in new[] { legacy, current })
                    {
                        var combo = (ComboBox)control.FindName("ComboBoxDeNovoSource");
                        string nextSource = source == DeNovoSource.DenovoOnly ? DeNovoSource.DeepNovo.Id : DeNovoSource.DenovoOnly.Id;
                        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == nextSource);
                    }
                    PumpDispatcher(); Capture("source-change");

                    object Apply(string method, object dto)
                    {
                        var oldMethod = legacyType.GetMethod(method)!;
                        object oldDto = LedgerLegacyValue(dto, oldMethod.GetParameters()[0].ParameterType)!;
                        if (method == "ApplyHomology" && ((DeNovoHomology)dto).PeptidesSearched > 0)
                        { LedgerDeNovoAssignments(oldDto); LedgerDeNovoAssignments(dto); }
                        oldMethod.Invoke(legacy, [oldDto]); typeof(DeNovoControl).GetMethod(method)!.Invoke(current, [dto]); PumpDispatcher(); return oldDto;
                    }
                    void Capture(string phase)
                    {
                        foreach (var fact in facts.Where(fact => fact.Phases.Contains(phase, StringComparer.Ordinal)))
                        {
                            string key = caseId + "/" + phase;
                            string? Read(UserControl control) => fact.Property == "return"
                                ? (string?)(ReferenceEquals(control, legacy) ? legacyType : typeof(DeNovoSession)).GetMethod("CriteriaSentence", BindingFlags.Instance | BindingFlags.NonPublic)!
                                    .Invoke(ReferenceEquals(control, legacy) ? legacy : current.ViewModel, null)
                                : fact.Property == "rendered cells" ? LedgerDeNovoCells(control, fact.Target) : LedgerDeNovoValue(control, fact.Target, fact.Property);
                            if (Read(legacy) is string oldValue) before[fact.Row.Id][key] = oldValue;
                            if (Read(current) is string newValue) after[fact.Row.Id][key] = newValue;
                        }
                    }
                }
                finally { newWindow.Close(); oldWindow.Close(); }
            }
            foreach (var fact in facts)
            {
                string oldValue = JsonSerializer.Serialize(before[fact.Row.Id]), newValue = JsonSerializer.Serialize(after[fact.Row.Id]);
                bool matched = before[fact.Row.Id].Count == fact.Phases.Length * 6 && after[fact.Row.Id].Count == fact.Phases.Length * 6 && oldValue == newValue;
                string path = fact.Property == "rendered cells" ? "FamilyQuant/DeNovoControl.xaml" : "FamilyQuant/DeNovoSession.cs";
                results.Add(new(fact.Row.Id, fact.Row.Kind, matched, oldValue, newValue, path, "FamilyQuant.DeNovoSession." + fact.Member,
                    fact.Row.Source + ":" + fact.Row.Line.ToString(CultureInfo.InvariantCulture) + " " + fact.Row.Detail + " -> executed " + fact.Member + "/" + fact.Target + "." + fact.Property
                    + "; frozen=" + oldValue + "; current=" + newValue, matched ? "Native production projection/formatting agrees in every listed fixture, source and culture." : "A listed projected value is missing or differs; no closure."));
                if (matched) Check("De novo workflow row " + fact.Row.Id + ": " + fact.Member + "/" + fact.Target, true);
                else { skipped++; Console.WriteLine("  SKIP  " + fact.Row.Id + ": projected values missing or different"); }
            }
            foreach (var group in results.Where(result => result.Matched).GroupBy(result => result.Kind))
            {
                string assertion = "De novo workflow kind " + group.Key + ": all listed frozen/current projected values agree for every recorded source, culture and stage";
                Check(assertion, group.All(result => result.Before == result.Actual)); assertions[group.Key] = assertion;
            }
            string styles = LedgerDeNovoStyleHandoff(root, oldXaml, cohort, output); extra.Add(styles);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); CultureInfo.CurrentCulture = originalCulture; Themes.ThemeManager.Apply(Themes.AppTheme.Dark); }
        var sources = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "DeNovo*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*DeNovo*.xaml"))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core"), "DeNovo*.cs"))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/DeNovo"), "*.cs", SearchOption.AllDirectories))
            .Concat(new[] { Path.Combine(root, "FamilyQuant.Core/Homology/HomologyResult.cs") })
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        return CompleteDeNovoLedgerProof(root, frozenRoot, output, args[4], results, assertions, transcript.ToString(), sources,
            "EV-denovo-workflows", nameof(DeNovoLedgerWorkflowCommand), "Synthetic workflow and legacy-data export fixtures only; no fresh RAW or real-sample accuracy claim.", extra, sources);
    }

    private static object? LedgerLegacyValue(object? value, Type type) => value == null ? null
        : JsonSerializer.Deserialize(JsonSerializer.Serialize(value, value.GetType(), DeNovoBridgeJson), type, DeNovoBridgeJson);

    private static bool LedgerLegacyJsonAgrees(JsonElement before, JsonElement after) => before.ValueKind == after.ValueKind && (before.ValueKind switch
    {
        JsonValueKind.Object => before.EnumerateObject().All(property => after.TryGetProperty(property.Name, out var value) && LedgerLegacyJsonAgrees(property.Value, value)),
        JsonValueKind.Array => before.GetArrayLength() == after.GetArrayLength() && before.EnumerateArray().Zip(after.EnumerateArray()).All(pair => LedgerLegacyJsonAgrees(pair.First, pair.Second)),
        _ => before.GetRawText() == after.GetRawText(),
    });

    private static DeNovoImport LedgerDeNovoImport(DeNovoSource source)
    {
        var rows = Enumerable.Range(0, 5).Select(index => new PeaksDeNovoRow { RunName = index < 3 ? "runA.raw" : "runB.raw", Scan = 101 + index,
            Peptide = "PEPTIDEK", Length = 8, Charge = 2, Mz = 500.25, Alc = 88.5, LocalConfidence = [99, 99, 99, 99, 99, 99, 99, 99],
            SourceId = source.Id, ConfidenceField = source.ConfidenceColumn }).ToList();
        return new DeNovoImport { FilesFound = 2, RowsRead = 1205, Kept = rows, SourceId = source.Id, SourceDisplayName = source.DisplayName,
            ConfidenceField = source.ConfidenceColumn, PerRun = [new() { Run = "runA.raw", RowsRead = 1002, RowsKept = 3 }, new() { Run = "runB.raw", RowsRead = 203, RowsKept = 2 }] };
    }

    private static DeNovoHomology LedgerDeNovoHomology() => new() { FcfPath = "fixture.fcf", PeptidesSearched = 5, MappedToTargets = 2,
        DecoyOnly = 1, Unmatched = 2, ElapsedSeconds = 1.25,
        Families = [new() { FamilyId = 11, PeptideCount = 2, Description = "Fixture one" }, new() { FamilyId = 22, PeptideCount = 1, Description = "Fixture two" }] };

    private static void LedgerDeNovoAssignments(object homology)
    {
        var property = homology.GetType().GetProperty("Assignments")!; var list = (IList)property.GetValue(homology)!;
        if (list.Count != 0) return;
        var type = property.PropertyType.GetGenericArguments().Single();
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 4);
        list.Add(constructor.Invoke(["PEPTIDEK", new[] { 11 }, new[] { "P,one" }, Array.Empty<string>()]));
        list.Add(constructor.Invoke(["AAAAAAAA", new[] { 11, 22 }, new[] { "P\"two" }, Array.Empty<string>()]));
    }

    private static string? LedgerDeNovoCells(UserControl control, string name)
    {
        if (control.FindName(name) is not DataGridBoundColumn column || column.Binding == null) return null;
        var grid = WalkTree(control).OfType<DataGrid>().SingleOrDefault(grid => grid.Columns.Contains(column));
        if (grid == null || grid.Items.Count == 0) return null;
        var values = new List<string>();
        foreach (object row in grid.Items)
        {
            if (ReferenceEquals(row, CollectionView.NewItemPlaceholder)) continue;
            var text = new TextBlock { DataContext = row };
            BindingOperations.SetBinding(text, TextBlock.TextProperty, column.Binding);
            BindingOperations.GetBindingExpression(text, TextBlock.TextProperty)?.UpdateTarget();
            values.Add(text.Text); BindingOperations.ClearAllBindings(text);
        }
        return values.Count > 0 && values.Any(value => value.Length > 0) ? JsonSerializer.Serialize(values) : null;
    }

    private static string? LedgerXmlName(XElement element) => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value;

    private static string LedgerDeNovoStyleHandoff(string root, XDocument oldXaml, FeatureLedger.Observation[] cohort, string output)
    {
        var rows = new List<object>(); var markdown = File.ReadAllLines(Path.Combine(root, "docs/FEATURE-LEDGER.md"));
        foreach (var row in cohort.Where(row => row.Source.EndsWith(".xaml", StringComparison.Ordinal)))
        {
            var attributes = oldXaml.Descendants().Attributes().Where(attribute => ((IXmlLineInfo)attribute).LineNumber == row.Line).ToArray();
            var styles = attributes.Select(attribute => attribute.Parent!.AncestorsAndSelf().FirstOrDefault(node => node.Name.LocalName == "Style"))
                .Where(node => node != null).Distinct().ToArray();
            if (styles.Length != 1) continue;
            string[] cells = markdown.Single(line => line.StartsWith("| " + row.Id + " |", StringComparison.Ordinal)).Split('|');
            bool open = cells[7].Trim() == "OPEN";
            Check("C13 style handoff remains OPEN " + row.Id, open);
            var style = styles[0]!;
            rows.Add(new { row.Id, row.Source, row.Line, row.Kind, row.Detail,
                frozenStyleKey = style.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value,
                frozenTargetType = style.Attribute("TargetType")?.Value, state = "OPEN", reason = "Current-role mapping reserved for FQLook; none supplied by EV." });
        }
        string path = Path.Combine(output, "c13-style-role-mapping-open.json"); FeatureLedgerEvidence.WriteJson(path, rows);
        Console.WriteLine("C13 style handoff: " + rows.Count.ToString(CultureInfo.InvariantCulture) + " OPEN rows in " + path); return path;
    }
}
