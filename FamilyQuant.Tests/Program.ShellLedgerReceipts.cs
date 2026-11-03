using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record ShellLedgerComparison(string Id, string Kind, string Source, int Line,
        bool Matched, string Before, string Actual, string CurrentPath, string Symbol, string Reason,
        string FrozenMember = "", string FrozenMemberSha256 = "", string CurrentMemberSha256 = "");

    private static int ShellLedgerComparisonCommand(string[] args)
    {
        if (args.Length is < 4 or > 6 || args.Length == 6 && args[5] != "--negative-control"
            || !DateTimeOffset.TryParse(args[3], CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out _))
            throw new ArgumentException("Supply frozen source root, fresh output and date timestamp, optionally the frozen app folder and --negative-control.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Comparison output must be fresh.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row)
            && (FeatureLedgerEvidence.Owner(row).StartsWith("SH", StringComparison.Ordinal)
                || FeatureLedgerEvidence.Owner(row) == "C1/C5/C13")).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        var frozen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
        {
            string path = Path.Combine(frozenRoot, file.Path);
            if (!File.Exists(path) || FeatureLedgerEvidence.Hash(path) != file.Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + file.Path);
            frozen[file.Path] = File.ReadAllText(path);
        }
        string[] available = new[] { "FamilyQuant", "FamilyQuant.Core" }.SelectMany(folder =>
            Directory.GetFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).ToArray();
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in cohort.SelectMany(row => ShellLedgerSources(row.Source, available)).Distinct(StringComparer.Ordinal))
            current[path] = File.ReadAllText(Path.Combine(root, path));
        Directory.CreateDirectory(output);
        var results = new List<ShellLedgerComparison>();
        var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        var curationRuntime = new Dictionary<string, string>(StringComparer.Ordinal);
        var compositionRuntime = new ShellCompositionEvidence(new(StringComparer.Ordinal), new());
        var ticRuntime = new ShellTicEvidence(new(StringComparer.Ordinal), new());
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            foreach (var row in cohort)
            {
                var sources = ShellLedgerSources(row.Source, available).ToDictionary(path => path, path => current[path], StringComparer.Ordinal);
                ShellLedgerComparison result;
                if (row.Source.EndsWith(".xaml", StringComparison.Ordinal))
                {
                    var compared = LedgerDeclarationComparison.Compare(row, XDocument.Parse(frozen[row.Source]),
                        sources.ToDictionary(file => file.Key, file => XDocument.Parse(file.Value), StringComparer.Ordinal));
                    result = new(row.Id, row.Kind, row.Source, row.Line, compared.Matched, compared.Before,
                        compared.Actual, compared.CurrentPath, compared.Symbol, compared.Reason);
                }
                else
                {
                    var compared = LedgerMemberFactComparison.Compare(row, frozen[row.Source], sources);
                    if (!compared.Matched && ShellPartialRelocation(row.Source, compared, frozen[row.Source], sources) is { } partial)
                        compared = LedgerMemberFactComparison.Compare(row, frozen[row.Source], sources, partial);
                    if (!compared.Matched && row.Source == "FamilyQuant/ClusterGenerationControl.xaml.cs"
                        && CurationLiteralRelocation(compared.FrozenMember) is { } relocation)
                        compared = LedgerMemberFactComparison.Compare(row, frozen[row.Source], sources, relocation);
                    result = new(row.Id, row.Kind, row.Source, row.Line, compared.Matched, compared.Before,
                        compared.Actual, compared.CurrentPath, compared.Symbol, compared.Reason,
                        compared.FrozenMember, compared.FrozenMemberSha256, compared.CurrentMemberSha256);
                }
                results.Add(result);
                if (result.Matched) Check("shell declaration " + row.Id + ": " + result.Symbol, true);
                else { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": " + result.Reason); }
            }
            foreach (var kind in results.Where(result => result.Matched).GroupBy(result => result.Kind).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = "shell declaration kind " + kind.Key + ": every listed matched observation retains its exact declaration";
                Check(assertion, kind.All(result => result.Before == result.Actual));
                assertions[kind.Key] = assertion;
            }
            CurationScreenTests();
            foreach (var fact in new[]
            {
                ("control", "DataGrid DataGridResultGrid", "curation always shows its table without Controls or Data Browser tabs"),
                ("control", "CheckBox CheckBoxExcludeReverse", "curation hides search IDs and decoys by default while keeping all document rows"),
                ("control", "DataGridTextColumn ColumnUSearchId", "curation hides search IDs and decoys by default while keeping all document rows"),
                ("control", "DataGridTextColumn ColumnLocus", "curation shows accession in Locus while retaining the complete protein identifier"),
                ("column", "ColumnLocus.Binding = {Binding Path=SequenceIdentifier}", "curation shows accession in Locus while retaining the complete protein identifier"),
                ("control", "DataGridTextColumn ColumnUserAnnotation", "a native protein annotation edit marks the document and records one Undo step"),
                ("column", "ColumnUserAnnotation.Binding = {Binding Path=UserAnnotation, Mode=TwoWay}", "a native protein annotation edit marks the document and records one Undo step"),
                ("control", "Button ButtonUndo", "Undo restores the edited protein annotation and emits one more document change"),
                ("handler", "ButtonUndo.Click = ButtonUndo_Click", "Undo restores the edited protein annotation and emits one more document change"),
                ("handler definition", "private void ButtonUndo_Click(object sender, RoutedEventArgs e)", "Undo restores the edited protein annotation and emits one more document change")
            })
            {
                var observation = cohort.Single(row => row.Source.StartsWith("FamilyQuant/ClusterGenerationControl.xaml", StringComparison.Ordinal)
                    && row.Kind == fact.Item1 && row.Detail == fact.Item2);
                curationRuntime.Add(observation.Id, fact.Item3);
            }
            if (args.Length >= 5)
            {
                compositionRuntime = ProveShellComposition(Path.Combine(frozenRoot, "FamilyQuant/CsvReport.cs"), args[4], output, cohort, args.Length == 6);
                ticRuntime = ProveShellTic(Path.Combine(frozenRoot, "FamilyQuant/ClusterGenerationControl.xaml.cs"), args[4], output, cohort);
            }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "comparisons.log"), data = Path.Combine(output, "comparisons.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(data, results);
        string runtimeData = Path.Combine(output, "curation-runtime.json");
        FeatureLedgerEvidence.WriteJson(runtimeData, curationRuntime.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new { observationId = pair.Key, assertion = pair.Value }));
        if (failed != 0)
        {
            Console.WriteLine("No ledger register published because an executed proof failed.");
            return 1;
        }
        var artifacts = new List<FeatureLedgerEvidence.Artifact>
        { new(log, FeatureLedgerEvidence.Hash(log)), new(data, FeatureLedgerEvidence.Hash(data)), new(runtimeData, FeatureLedgerEvidence.Hash(runtimeData)) };
        artifacts.AddRange(compositionRuntime.Artifacts.Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))));
        artifacts.AddRange(ticRuntime.Artifacts.Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))));
        string snapshot = Path.Combine(output, "executed");
        Directory.CreateDirectory(snapshot);
        foreach (string source in new[] { "Program.ShellLedgerReceipts.cs", "Program.ShellLedgerComparison.cs", "Program.ShellCompositionProof.cs", "Program.ShellTicProof.cs",
            "Program.Commands.LedgerDeclarations.cs", "Program.CurationScreen.cs", "LedgerDeclarationComparison.cs", "LedgerMemberFactComparison.cs", "FeatureLedgerEvidence.Reporting.cs" })
        {
            string path = Path.Combine(snapshot, source);
            File.Copy(Path.Combine(root, "FamilyQuant.Tests", source), path);
            artifacts.Add(new(path, FeatureLedgerEvidence.Hash(path)));
        }
        string assembly = Path.Combine(snapshot, "FamilyQuant.Tests.dll");
        File.Copy(typeof(Program).Assembly.Location, assembly);
        artifacts.Add(new(assembly, FeatureLedgerEvidence.Hash(assembly)));
        foreach (string compiled in new[] { typeof(MainWindow).Assembly.Location, typeof(FamilyClusterFile).Assembly.Location })
        {
            string path = Path.Combine(snapshot, Path.GetFileName(compiled));
            File.Copy(compiled, path);
            artifacts.Add(new(path, FeatureLedgerEvidence.Hash(path)));
        }
        var git = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        git.ArgumentList.Add("rev-parse"); git.ArgumentList.Add("HEAD");
        using var process = Process.Start(git)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record executing commit.");
        var run = new FeatureLedgerEvidence.Run("shell-declarations", Environment.CommandLine, args[3], commit, artifacts, root);
        foreach (string step in new[] { "SH", "C1" })
        {
            var selected = results.Where(result => (result.Source.Contains("ClusterGenerationControl", StringComparison.Ordinal) ? "C1" : "SH") == step).ToArray();
            var rows = selected.Select(result => ticRuntime.Assertions.TryGetValue(result.Id, out string? ticAssertion)
                ? new FeatureLedgerEvidence.Row(result.Id, ShellTicSources.Select(path => new FeatureLedgerEvidence.Implementation(path,
                    path == "FamilyQuant/CurationQuantitationViewModel.cs" ? "PrepareFullXic; native five-argument production overload"
                        : "Native TIC refusal proof dependency", FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList(),
                    new() { new("shell-declarations", "test", nameof(ProveShellTic), ticAssertion, log, "") },
                    "Executed frozen/current TIC refusal output, run count and denominator in both cultures on synthetic inputs and repeated private-cache calls; declaration equality is not claimed.",
                    Compared: result.Source + ":" + result.Line.ToString(CultureInfo.InvariantCulture) + " " + result.Before
                        + "; frozen member " + result.FrozenMember + "; exact emitted message fragment in equal native results")
                : compositionRuntime.Assertions.TryGetValue(result.Id, out string? compositionAssertion)
                ? new FeatureLedgerEvidence.Row(result.Id, ShellCompositionSources.Select(path => new FeatureLedgerEvidence.Implementation(path,
                    path == "FamilyQuant.Core/CsvReport.cs" ? HistoricalCompositionMember + "; native public historical overload"
                        : "Native historical composition CSV dependency", FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList(),
                    new() { new("shell-declarations", "test", nameof(ProveShellComposition), compositionAssertion, log, "") },
                    "Executed complete frozen/current historical composition CSVs in all shared-peptide policies and two cultures; declaration equality is not claimed.",
                    Compared: result.Source + ":" + result.Line.ToString(CultureInfo.InvariantCulture) + " " + result.Before
                        + "; frozen member " + result.FrozenMember + "; exact native file bytes and observed literal, including empty and nonpositive-denominator cases")
                : curationRuntime.TryGetValue(result.Id, out string? runtimeAssertion)
                ? new FeatureLedgerEvidence.Row(result.Id, new[] { "FamilyQuant/ClusterGenerationControl.xaml", "FamilyQuant/ClusterGenerationControl.xaml.cs",
                    "FamilyQuant/ClusterGenerationControl.Presentation.cs", "FamilyQuant/CurationViewModel.cs" }
                    .Select(path => new FeatureLedgerEvidence.Implementation(path,
                        path.EndsWith("CurationViewModel.cs", StringComparison.Ordinal) ? "FamilyQuant.CurationViewModel" : "FamilyQuant.ClusterGenerationControl",
                        FeatureLedgerEvidence.Hash(Path.Combine(root, path))))
                    .Append(new("FamilyQuant.Tests/Program.CurationScreen.cs", nameof(CurationScreenTests),
                        FeatureLedgerEvidence.Hash(Path.Combine(root, "FamilyQuant.Tests/Program.CurationScreen.cs")))).ToList(),
                    new() { new("shell-declarations", "test", nameof(CurationScreenTests), runtimeAssertion, log, "") },
                    "Native synthetic edit/Undo and retained table-data behavior; no scientific result or export claim.",
                    Compared: result.Source + ":" + result.Line.ToString(CultureInfo.InvariantCulture) + " " + result.Before + " -> " + runtimeAssertion)
                : new FeatureLedgerEvidence.Row(result.Id,
                result.Matched ? new() { new(result.CurrentPath, result.Symbol, FeatureLedgerEvidence.Hash(Path.Combine(root, result.CurrentPath))) } : new(),
                new(), result.Matched ? "Declaration only; no reachability or handler-execution claim." : result.Reason,
                result.Matched ? ReceiptId(result.Kind) : null,
                result.Matched ? result.Source + ":" + result.Line.ToString(CultureInfo.InvariantCulture) + " " + result.Before
                    + " -> " + result.CurrentPath + ": " + result.Symbol + " = " + result.Actual
                    + (result.FrozenMemberSha256.Length == 0 ? "" : "; complete member tokens SHA256=" + result.FrozenMemberSha256) : null)).ToList();
            var receipts = selected.Where(result => result.Matched).Select(result => result.Kind).Distinct(StringComparer.Ordinal)
                .OrderBy(kind => kind, StringComparer.Ordinal).Select(kind => new FeatureLedgerEvidence.KindReceipt(ReceiptId(kind), kind,
                    new() { new("shell-declarations", "test", nameof(ShellLedgerComparisonCommand), assertions[kind], log, "") })).ToList();
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"),
                new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { run }, rows, receipts));
            Console.WriteLine(step + ": " + selected.Count(result => result.Matched).ToString(CultureInfo.InvariantCulture)
                + " matched declarations; " + selected.Count(result => curationRuntime.ContainsKey(result.Id)).ToString(CultureInfo.InvariantCulture)
                + " native interaction proofs; " + selected.Count(result => compositionRuntime.Assertions.ContainsKey(result.Id)).ToString(CultureInfo.InvariantCulture)
                + " native composition facts; " + selected.Count(result => ticRuntime.Assertions.ContainsKey(result.Id)).ToString(CultureInfo.InvariantCulture)
                + " native TIC facts; " + selected.Count(result => !result.Matched && !curationRuntime.ContainsKey(result.Id) && !ticRuntime.Assertions.ContainsKey(result.Id)
                    && !compositionRuntime.Assertions.ContainsKey(result.Id)).ToString(CultureInfo.InvariantCulture) + " remain unproven.");
        }
        return failed == 0 ? 0 : 1;
    }

    private static string ReceiptId(string kind) => "shell-decl-" + kind.Replace('/', '-').Replace(' ', '-');

    // Inspected literal relocations. Exact complete member tokens must still agree.
    private static LedgerMemberFactComparison.MemberMapping? CurationLiteralRelocation(string member)
    {
        const string owner = "FamilyQuant.ClusterGenerationControl::";
        const string quantity = "private const string FullXicQuantity";
        const string prepare = "private static FullXicPrepared PrepareFullXic ( IReadOnlyList < QuantRow > rows , IReadOnlyDictionary < string , double > ? perRunTic , FamilyClusterFile proteins )";
        if (member == owner + quantity || member == owner + prepare)
            return new(member, "FamilyQuant/CurationQuantitationViewModel.cs", member.Replace(owner, "FamilyQuant.CurationQuantitationViewModel::", StringComparison.Ordinal));
        if (member == owner + "private void UpdateLockSummary ( )")
            return new(member, "FamilyQuant/ClusterGenerationControl.Presentation.cs", member);
        return null;
    }
}
