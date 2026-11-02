using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record ResultsRuntimeFact(string Id, string Operation, string[] FixtureKeys,
        string Expected, string Actual, bool Passed);

    private static int ResultsRuntimeLedgerCommand(string[] args)
    {
        if (args.Length is not (5 or 6) || args.Length == 6 && args[5] != "--negative-control")
            throw new ArgumentException("Supply frozen source root, own frozen host, fresh output, date timestamp, optional --negative-control.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]);
        string referenceHost = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        if (!referenceHost.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Build the frozen host inside this worktree.");
        string referenceApp = Path.Combine(Path.GetDirectoryName(referenceHost)!, "FamilyQuant.dll");
        if (FeatureLedgerEvidence.Hash(referenceApp) != FeatureLedgerEvidence.Hash("D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll"))
            throw new InvalidDataException("The comparison host must load the frozen 1.5.0.6 application bytes.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Proof output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var registry = FeatureLedgerEvidence.Load(root, inventory);
        var proved = registry.Entries.Values.Where(entry => entry.Valid && entry.Step != "RS-runtime")
            .Select(entry => entry.Row.ObservationId).ToHashSet(StringComparer.Ordinal);
        var cohort = inventory.Observations.Where(row => FeatureLedgerEvidence.Owner(row) is "RS" or "RS/C5"
            && !FeatureLedger.IsApprovedDrop(row) && !proved.Contains(row.Id)).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        foreach (var source in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
            if (FeatureLedgerEvidence.Hash(Path.Combine(frozenRoot, source.Path)) != source.Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + source.Path);

        string currentHost = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var cases = new List<ResultsRuntimeFact>();
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            string beforePath = Path.Combine(output, "frozen-values.json"), afterPath = Path.Combine(output, "current-values.json");
            Execute(referenceHost, ResultsRuntimeSnapshot.Argument, beforePath);
            Execute(currentHost, ResultsRuntimeSnapshot.Argument, afterPath);
            string repeatPath = Path.Combine(output, "repeat-values.json");
            Execute(currentHost, ResultsRuntimeSnapshot.Argument, repeatPath);
            var before = JsonSerializer.Deserialize<List<ResultsRuntimeSnapshot.Value>>(File.ReadAllText(beforePath))!
                .ToDictionary(value => value.Key, value => value.Text, StringComparer.Ordinal);
            var after = JsonSerializer.Deserialize<List<ResultsRuntimeSnapshot.Value>>(File.ReadAllText(afterPath))!
                .ToDictionary(value => value.Key, value => value.Text, StringComparer.Ordinal);
            bool repeated = FeatureLedgerEvidence.Hash(afterPath) == FeatureLedgerEvidence.Hash(repeatPath);
            Check("RS runtime frozen and current snapshots contain the same 178 named fixture values",
                before.Count == 178 && before.Keys.OrderBy(key => key, StringComparer.Ordinal)
                    .SequenceEqual(after.Keys.OrderBy(key => key, StringComparer.Ordinal)));
            Check("RS runtime presentation snapshot is byte-identical across two current runs", repeated);
            string chartBefore = Path.Combine(output, "frozen-charts"), chartAfter = Path.Combine(output, "current-charts");
            string chartRepeat = Path.Combine(output, "repeat-charts");
            Execute(referenceHost, ChartExportSnapshot.Argument, chartBefore);
            Execute(currentHost, ChartExportSnapshot.Argument, chartAfter);
            Execute(currentHost, ChartExportSnapshot.Argument, chartRepeat);
            var charts = new Dictionary<string, string>(StringComparer.Ordinal)
            { ["FamilyContributionChart.cs"] = "families", ["IdentityScanChart.cs"] = "identity", ["IsotopeCountChart.cs"] = "isotopes",
                ["PercentIonCurrentChart.cs"] = "percent", ["RecoveryChart.cs"] = "recovery", ["XicMassChart.cs"] = "mass" };
            foreach (var row in cohort)
            {
                string[]? keys = null;
                string operation = "";
                if (row.Source == "FamilyQuant/AnalyticsControl.xaml.cs" && row.Line is >= 212 and <= 261)
                {
                    keys = before.Keys.Where(key => key.StartsWith("summary/", StringComparison.Ordinal) && (row.Line switch
                    {
                        >= 212 and <= 221 => key.Contains("/family=2/", StringComparison.Ordinal),
                        >= 223 and <= 224 => key.Contains("/family=0/", StringComparison.Ordinal),
                        >= 225 and <= 226 => key.Contains("/family=1/", StringComparison.Ordinal),
                        >= 233 and <= 236 => !key.Contains("/tic=2/", StringComparison.Ordinal),
                        >= 245 and <= 247 => key.Contains("/tic=2/", StringComparison.Ordinal),
                        >= 251 and <= 252 => key.Contains("/tic=2/", StringComparison.Ordinal) && key.Contains("/under=2/", StringComparison.Ordinal),
                        >= 256 and <= 261 => key.Contains("/tic=2/", StringComparison.Ordinal),
                        _ => false
                    })).OrderBy(key => key, StringComparer.Ordinal).ToArray();
                    operation = "native SummaryLine branch text and formatted values; the sole reviewed screen-name substitution is Layer 3 -> the quantitation panel";
                }
                else if (row.Source == "FamilyQuant/NormalizationMode.cs" && row.Line is >= 104 and <= 122
                    && !row.Detail.Contains("USearch families", StringComparison.Ordinal))
                {
                    keys = before.Keys.Where(key => key.StartsWith(row.Line <= 105 ? "column/" : "csv-header/", StringComparison.Ordinal))
                        .OrderBy(key => key, StringComparer.Ordinal).ToArray();
                    operation = "native normalization header, frozen User-family branch against current header; four modes, unknown CSV denominator, two cultures";
                }
                else if (row.Source == "FamilyQuant/AnalyticsModel.cs" && row.Line == 444)
                {
                    keys = before.Keys.Where(key => key.StartsWith("csv-number/", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal).ToArray();
                    operation = "native AnalyticsCsv.Num round-trip formatting of ten double cases in two cultures";
                }
                else if (row.Source == "FamilyQuant/AnalyticsModel.cs" && row.Line is 261 or 455)
                {
                    keys = before.Keys.Where(key => key.StartsWith(row.Line == 261 ? "key/" : "csv-field/", StringComparison.Ordinal))
                        .OrderBy(key => key, StringComparer.Ordinal).ToArray();
                    operation = row.Line == 261 ? "native KeyOf separators across two runs, sides, charges and cultures"
                        : "native RFC 4180 Field escaping: null, empty, plain, comma, quote, LF, CR and combined fields in two cultures";
                }
                if (keys is { Length: > 0 })
                {
                    string expected = JsonSerializer.Serialize(keys.Select(key => new { key, text = before[key] }));
                    string actual = JsonSerializer.Serialize(keys.Select(key => new { key, text = after.GetValueOrDefault(key, "<missing>") }));
                    string compared = expected.Replace("chosen in Layer 3).", "chosen in the quantitation panel).", StringComparison.Ordinal);
                    if (args.Length == 6) actual += "negative control";
                    cases.Add(new(row.Id, operation, keys, expected, actual, repeated && compared == actual));
                }
                else if (charts.TryGetValue(Path.GetFileName(row.Source), out string? chart))
                {
                    string[] names = Directory.GetFiles(chartBefore, chart + "-*.png").Select(Path.GetFileName).Cast<string>()
                        .OrderBy(name => name, StringComparer.Ordinal).ToArray();
                    string expected = string.Join(";", names.Select(name => name + ":" + FeatureLedgerEvidence.Hash(Path.Combine(chartBefore, name))));
                    string actual = string.Join(";", names.Select(name => name + ":" + FeatureLedgerEvidence.Hash(Path.Combine(chartAfter, name))));
                    bool equalRepeat = names.All(name => FeatureLedgerEvidence.Hash(Path.Combine(chartAfter, name))
                        == FeatureLedgerEvidence.Hash(Path.Combine(chartRepeat, name)));
                    if (args.Length == 6) actual += "negative control";
                    cases.Add(new(row.Id, "complete native raster bytes: empty and populated branches, historical Light and Dark palettes, two sizes; font and draw labels included",
                        names, expected, actual, names.Length == 8 && equalRepeat && expected == actual));
                }
            }
            foreach (var fact in cases) Check("RS runtime " + fact.Id + ": " + fact.Operation, fact.Passed);
            foreach (var row in cohort.Where(row => cases.All(fact => fact.Id != row.Id)))
            { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": no observation-specific native branch comparison."); }
            foreach (var group in cases.GroupBy(fact => cohort.Single(row => row.Id == fact.Id).Kind).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = "RS runtime kind " + group.Key + ": every listed native branch comparison and deterministic repeat passed";
                Check(assertion, group.All(fact => fact.Passed)); kinds[group.Key] = assertion;
            }
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"), data = Path.Combine(output, "comparisons.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false)); FeatureLedgerEvidence.WriteJson(data, cases);
        void Retain(string from, string relative)
        {
            string to = Path.Combine(output, "executed", relative); Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(from, to, false);
        }
        string[] implementations = cases.Select(fact => cohort.Single(row => row.Id == fact.Id).Source)
            .Select(source => source == "FamilyQuant/AnalyticsControl.xaml.cs" ? "FamilyQuant/AnalyticsViewModel.cs" : FeatureLedgerEvidence.CurrentPath(root, source))
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (string path in implementations.Concat(new[] { "FamilyQuant.Tests/ResultsRuntimeSnapshot.cs", "FamilyQuant.Tests/ChartExportSnapshot.cs",
            "FamilyQuant.Tests/Program.ResultsRuntimeLedger.cs", "FamilyQuant.Tests/Program.Commands.ResultsRuntimeLedger.cs",
            "FamilyQuant.Tests/ReferenceGoldenHost/Program.cs", "FamilyQuant.Tests/ReferenceGoldenHost/ReferenceGoldenHost.csproj" }))
            Retain(Path.Combine(root, path), path);
        foreach (string source in cohort.Select(row => row.Source).Distinct(StringComparer.Ordinal)) Retain(Path.Combine(frozenRoot, source), "frozen/" + source);
        foreach (string path in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(ProjectStore).Assembly.Location })
            Retain(path, "current/" + Path.GetFileName(path));
        foreach (string path in new[] { Path.ChangeExtension(referenceHost, ".dll"), Path.Combine(Path.GetDirectoryName(referenceHost)!, "FamilyQuant.dll") })
            Retain(path, "reference/" + Path.GetFileName(path));
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!; string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record source commit.");
        var run = new FeatureLedgerEvidence.Run("native", Environment.CommandLine, args[4], commit,
            Directory.GetFiles(output, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList(), root);
        var rows = cases.Select(fact =>
        {
            var row = cohort.Single(row => row.Id == fact.Id);
            string current = row.Source == "FamilyQuant/AnalyticsControl.xaml.cs" ? "FamilyQuant/AnalyticsViewModel.cs" : FeatureLedgerEvidence.CurrentPath(root, row.Source);
            return new FeatureLedgerEvidence.Row(row.Id, new() { new(current, fact.Operation, FeatureLedgerEvidence.Hash(Path.Combine(root, current))) }, new(),
                "Frozen executable comparison of synthetic presentation inputs. No scientific inference or USearch removal approval. Textual screen-name change is explicit, not literal equality.",
                fact.Passed ? "native-" + row.Kind.Replace('/', '-').Replace(' ', '-') : null,
                fact.Passed ? row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + row.Detail + "; " + fact.Operation
                    + "; exact fixture keys and before/actual values retained in comparisons.json" : null);
        }).ToList();
        var receipts = kinds.Select(pair => new FeatureLedgerEvidence.KindReceipt("native-" + pair.Key.Replace('/', '-').Replace(' ', '-'), pair.Key,
            new() { new("native", "test", nameof(ResultsRuntimeLedgerCommand), pair.Value, log, "") })).ToList();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "RS-runtime.json"),
            new FeatureLedgerEvidence.Register(1, "RS-runtime", FeatureLedger.ReferenceCommit, new() { run }, rows, receipts));
        return failed == 0 ? 0 : 1;

        void Execute(string executable, string flag, string destination)
        {
            var info = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(flag); info.ArgumentList.Add(destination);
            using var child = Process.Start(info)!;
            var error = child.StandardError.ReadToEndAsync(); string text = child.StandardOutput.ReadToEnd(); child.WaitForExit();
            Console.WriteLine(executable + " " + flag + " " + destination); Console.Write(text); Console.Write(error.GetAwaiter().GetResult());
            if (child.ExitCode != 0) throw new InvalidDataException("Native producer failed: " + child.ExitCode.ToString(CultureInfo.InvariantCulture));
        }
    }
}
