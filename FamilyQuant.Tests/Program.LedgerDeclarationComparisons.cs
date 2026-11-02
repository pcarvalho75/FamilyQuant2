using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void LedgerDeclarationComparisonTests()
    {
        Check("named UI declarations have an observation-specific frozen/current comparer",
            typeof(Program).Assembly.GetType("FamilyQuant.Tests.LedgerDeclarationComparison") != null);
        const string old = "<UserControl xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Class='Fixture.View'><Button x:Name='Keep' ToolTip='Six plain words describe this action.' IsEnabled='False'/></UserControl>";
        XDocument before = XDocument.Parse(old);
        var row = new FeatureLedger.Observation("fixture", "FamilyQuant/fixture.xaml", 1, "tooltip", "Keep.ToolTip = Six plain words describe this action.");
        LedgerDeclarationComparison.Result Compare(string after, FeatureLedger.Observation? supplied = null) =>
            LedgerDeclarationComparison.Compare(supplied ?? row, before, new Dictionary<string, XDocument> { ["FamilyQuant/current.xaml"] = XDocument.Parse(after) });
        Check("the comparer proves one exact named tooltip value", Compare(old).Matched);
        Check("changed tooltip text stays unverified", !Compare(old.Replace("plain", "different", StringComparison.Ordinal)).Matched);
        Check("a renamed element cannot borrow another element's copy", !Compare(old.Replace("x:Name='Keep'", "x:Name='Other'", StringComparison.Ordinal)).Matched);
        Check("an unrelated same-text element cannot satisfy a missing named control", !Compare(old.Replace("x:Name='Keep'", "x:Name='Other'", StringComparison.Ordinal)).Matched);
        Check("a changed element type cannot satisfy the old declaration", !Compare(old.Replace("Button", "TextBox", StringComparison.Ordinal)).Matched);
        Check("an absent explicit property stays unverified", !Compare(old.Replace(" ToolTip='Six plain words describe this action.'", "", StringComparison.Ordinal)).Matched);
        Check("declaration comparison does not claim event behavior", !Compare(old, row with { Kind = "handler" }).Matched);
        Check("markup expressions require their resolved values", !Compare(old, row with { Detail = "Keep.ToolTip = {Binding Explanation}" }).Matched);
        var duplicates = new Dictionary<string, XDocument> { ["first.xaml"] = before, ["second.xaml"] = XDocument.Parse(old) };
        Check("duplicate named destinations stay unverified", !LedgerDeclarationComparison.Compare(row, before, duplicates).Matched);
        Check("unnamed elements never close through a whole-file text search", !Compare(old, row with { Detail = "Button.ToolTip = Six plain words describe this action." }).Matched);
    }

    private static int LedgerDeclarationCommand(string[] args)
    {
        if (args.Length != 7) throw new ArgumentException("Supply frozen root, current source root, fresh output, step, source prefix and date timestamp.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), currentRoot = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Comparison output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row) && row.Source.StartsWith(args[5], StringComparison.Ordinal)).ToArray();
        var oldDocuments = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
        {
            string path = Path.Combine(frozenRoot, file.Path);
            if (!File.Exists(path) || FeatureLedgerEvidence.Hash(path) != file.Sha256) throw new InvalidDataException("Frozen source hash differs: " + file.Path);
            if (file.Path.EndsWith(".xaml", StringComparison.Ordinal)) oldDocuments[file.Path] = XDocument.Load(path);
        }
        string[] files = Directory.GetFiles(Path.Combine(currentRoot, "FamilyQuant"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        // C1 is split across these two views; unrelated windows cannot lend it same-named elements.
        if (args[5].Contains("ClusterGenerationControl", StringComparison.Ordinal))
            files = files.Where(path => Path.GetFileName(path) is "ClusterGenerationControl.xaml" or "CurationQuantitationPanel.xaml").ToArray();
        var documents = files.ToDictionary(path => Path.GetRelativePath(currentRoot, path).Replace('\\', '/'), XDocument.Load, StringComparer.Ordinal);
        var results = new List<LedgerDeclarationComparison.Result>();
        var kindsById = cohort.ToDictionary(row => row.Id, row => row.Kind, StringComparer.Ordinal);
        var kindAssertions = new Dictionary<string, string>(StringComparer.Ordinal);
        var original = Console.Out;
        using var transcript = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            foreach (var row in cohort)
            {
                var result = oldDocuments.TryGetValue(row.Source, out var before)
                    ? LedgerDeclarationComparison.Compare(row, before, documents)
                    : new LedgerDeclarationComparison.Result(row.Id, false, row.Detail, "", "", "", "C# facts require a scoped output/member comparison or specific assertion.");
                results.Add(result);
                if (result.Matched) Check("declaration " + row.Id + ": " + result.Symbol, true);
                else { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": " + result.Reason); }
            }
            foreach (var kind in results.Where(result => result.Matched).GroupBy(result => kindsById[result.Id]).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = "declaration kind " + kind.Key + ": every listed named property agrees";
                Check(assertion, kind.All(result => result.Matched));
                kindAssertions[kind.Key] = assertion;
            }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "comparisons.log"), data = Path.Combine(output, "comparisons.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(data, results);
        var rows = results.Select(result => new FeatureLedgerEvidence.Row(result.Id,
            result.Matched ? new() { new(result.CurrentPath, result.Symbol, FeatureLedgerEvidence.Hash(Path.Combine(currentRoot, result.CurrentPath))) } : new(),
            new(),
            result.Matched ? "Declaration only; reachability/operation observations require their own proof. Target source root: " + currentRoot
                : result.Reason,
            result.Matched ? "decl-" + kindsById[result.Id].Replace('/', '-').Replace(' ', '-') : null,
            result.Matched ? result.Before + " -> " + result.CurrentPath + ": " + result.Symbol + " = " + result.Actual : null)).ToList();
        var kindReceipts = kindAssertions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new FeatureLedgerEvidence.KindReceipt("decl-" + pair.Key.Replace('/', '-').Replace(' ', '-'), pair.Key,
                new() { new("declarations", "test", nameof(LedgerDeclarationCommand), pair.Value, log, "") })).ToList();
        var git = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        git.ArgumentList.Add("rev-parse"); git.ArgumentList.Add("HEAD");
        using var process = System.Diagnostics.Process.Start(git)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record executing commit.");
        var run = new FeatureLedgerEvidence.Run("declarations", Environment.CommandLine, args[6], commit,
            new() { new(log, FeatureLedgerEvidence.Hash(log)), new(data, FeatureLedgerEvidence.Hash(data)) }, root);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, args[4] + ".json"),
            new FeatureLedgerEvidence.Register(1, args[4], FeatureLedger.ReferenceCommit, new() { run }, rows, kindReceipts));
        return failed == 0 ? 0 : 1;
    }
}
