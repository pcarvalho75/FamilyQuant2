using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int CurationActionsLedger(string[] args)
    {
        if (args.Length is not (6 or 7) || args.Length == 7 && args[6] != "--negative-control"
            || !DateTimeOffset.TryParse(args[5], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
            throw new ArgumentException("Supply frozen root, own frozen host, fixed audit JSON, fresh output, date and optional --negative-control.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[4]), host = Path.GetFullPath(args[2]);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Curation proof output must be fresh.");
        if (!host.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || FeatureLedgerEvidence.Hash(Path.Combine(Path.GetDirectoryName(host)!, "FamilyQuant.dll"))
                != FeatureLedgerEvidence.Hash("D:/FamilyQuant-data/reference-1.5.0.6/app/FamilyQuant.dll"))
            throw new InvalidDataException("Use this worktree's host with the actual frozen application bytes.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        if (FeatureLedger.Fingerprint(inventory) != FeatureLedger.ReferenceInventorySha256) throw new InvalidDataException("Frozen inventory differs.");
        using var audit = JsonDocument.Parse(File.ReadAllText(args[3]));
        var ids = audit.RootElement.EnumerateArray().Where(row => row.GetProperty("state").GetString() == "OPEN")
            .Select(row => row.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var cohort = inventory.Observations.Where(row => ids.Contains(row.Id) && row.Source is
            "FamilyQuant/ClusterGenerationControl.xaml" or "FamilyQuant/ClusterGenerationControl.xaml.cs" or "FamilyQuant/CsvReport.cs")
            .OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        foreach (var source in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
            if (FeatureLedgerEvidence.Hash(Path.Combine(args[1], source.Path)) != source.Sha256) throw new InvalidDataException("Frozen source differs: " + source.Path);
        Directory.CreateDirectory(output);
        string log = Path.Combine(output, "proofs.log");
        var console = Console.Out;
        using var capture = new StringWriter(CultureInfo.InvariantCulture);
        List<CurationActionCase> actions;
        bool csvEqual, reportEqual;
        void Snapshot(string executable, string name)
        {
            var start = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(CurationExportSnapshot.Argument); start.ArgumentList.Add(Path.Combine(output, name));
            using var process = Process.Start(start)!;
            string text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
            File.WriteAllText(Path.Combine(output, name + ".log"), text, new UTF8Encoding(false));
            if (process.ExitCode != 0) throw new InvalidDataException("Native snapshot failed: " + text);
        }
        try
        {
            Console.SetOut(new LedgerProofWriter(console, capture));
            Snapshot(host, "frozen.json"); Snapshot(Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe"), "current.json");
            Snapshot(Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe"), "repeat.json");
            var before = JsonSerializer.Deserialize<List<CurationExportSnapshot.Value>>(File.ReadAllText(Path.Combine(output, "frozen.json")))!;
            var after = JsonSerializer.Deserialize<List<CurationExportSnapshot.Value>>(File.ReadAllText(Path.Combine(output, "current.json")))!;
            bool repeat = File.ReadAllBytes(Path.Combine(output, "current.json")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "repeat.json")));
            Check("curation native export snapshot repeats byte-for-byte", repeat);
            string Select(List<CurationExportSnapshot.Value> values, bool csv) => JsonSerializer.Serialize(values.Where(value => value.Key.StartsWith("csv/", StringComparison.Ordinal) == csv));
            string comparedCsv = Select(after, true);
            if (args.Length == 7) comparedCsv += "\nPLANTED WRONG CSV FIELD";
            File.WriteAllText(Path.Combine(output, "compared-current-csv.json"), comparedCsv, new UTF8Encoding(false));
            csvEqual = repeat && before.Count(value => value.Key.StartsWith("csv/", StringComparison.Ordinal)) == 20
                && Select(before, true) == comparedCsv;
            reportEqual = repeat && Select(before, false) == Select(after, false);
            Check("curation native complete CSV output equals frozen for twenty culture/mode/header fixtures including comma quote CR LF null and fractional fields", csvEqual);
            Check("curation native retained User sorting and counters equal the frozen branches", reportEqual);
            actions = CurationActionProbe(Path.Combine(output, "native"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(console); File.WriteAllText(log, capture.ToString(), new UTF8Encoding(false)); }
        if (failed != 0) return 1;
        string Handler(FeatureLedger.Observation row) => row.Kind == "handler" ? Regex.Match(row.Detail, @" = (\w+)$").Groups[1].Value
            : row.Kind == "handler definition" ? Regex.Match(row.Detail, @"\b(\w+)\(").Groups[1].Value : "";
        CurationPendingList(root, output, cohort.Where(row => !(row.Source == "FamilyQuant/CsvReport.cs" && csvEqual)
            && !actions.Any(action => action.Passed && action.Handlers.Contains(Handler(row), StringComparer.Ordinal))).ToArray());
        var artifacts = new List<FeatureLedgerEvidence.Artifact>();
        void Artifact(string path) => artifacts.Add(new(path, FeatureLedgerEvidence.Hash(path)));
        foreach (string path in Directory.GetFiles(output, "*", SearchOption.AllDirectories)) Artifact(path);
        Artifact(Path.GetFullPath(args[3]));
        string executed = Path.Combine(output, "executed"); Directory.CreateDirectory(executed);
        string[] checks = ["CurationExportSnapshot.cs", "Program.CurationActionProbe.cs", "Program.CurationActionLedger.cs", "Program.Commands.CurationActions.cs", "Program.ShellProofWindows.cs"];
        foreach (string check in checks)
        {
            string target = Path.Combine(executed, check); File.Copy(Path.Combine(root, "FamilyQuant.Tests", check), target); Artifact(target);
        }
        foreach (string assembly in new[] { typeof(Program).Assembly.Location, typeof(MainWindow).Assembly.Location, typeof(FamilyClusterFile).Assembly.Location,
            host, Path.ChangeExtension(host, ".dll"), Path.Combine(Path.GetDirectoryName(host)!, "FamilyQuant.dll") })
        {
            string target = Path.Combine(executed, Path.GetFileName(assembly)); File.Copy(assembly, target); Artifact(target);
        }
        var implementation = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).StartsWith("ClusterGenerationControl", StringComparison.Ordinal)
                || Path.GetFileName(path).StartsWith("Curation", StringComparison.Ordinal))
            .Concat(new[] { "FamilyQuant.Core/CsvReport.cs", "FamilyQuant.Core/ReportView.cs", "FamilyQuant.Core/BatchCuration.cs" }.Select(path => Path.Combine(root, path)))
            .Concat(checks.Select(path => Path.Combine(root, "FamilyQuant.Tests", path))).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FeatureLedgerEvidence.Implementation(Path.GetRelativePath(root, path).Replace('\\', '/'), "executed curation screen action/export", FeatureLedgerEvidence.Hash(path))).ToList();
        var comparisons = cohort.Select(row =>
        {
            string handler = Handler(row);
            var action = handler.Length == 0 ? null : actions.SingleOrDefault(action => action.Passed && action.Handlers.Contains(handler, StringComparer.Ordinal));
            bool csv = row.Source == "FamilyQuant/CsvReport.cs" && csvEqual;
            string assertion = action != null ? "curation native " + action.Id + ": " + action.Compared : csv
                ? "curation native complete CSV output equals frozen for twenty culture/mode/header fixtures including comma quote CR LF null and fractional fields"
                : "No executed matching native action.";
            bool matched = action != null || csv;
            string compared = row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + row.Detail
                + (action != null ? "; current native action " + action.Id + ": " + action.Compared + "; actual=" + action.Actual
                    : csv ? "; complete native CSV before/after values in frozen.json/current.json, csv/* keys"
                    : "; no executed matching native action; plain text assigned to FQOpus, changed wording for Paulo");
            return new FeatureLedgerEvidence.Row(row.Id, matched ? implementation : new(), matched
                ? new() { new("SH-curation-actions", "test", action != null ? nameof(CurationActionProbe) : nameof(CurationActionsLedger), assertion, log, "") } : new(),
                matched ? "Native synthetic screen/export fixture. No real RAW or scientific-policy change; no secondary window shown." : "OPEN; no handler, style or wording proof is inferred from a search or build.", Compared: compared);
        }).ToList();
        var git = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse"); git.ArgumentList.Add("HEAD");
        using var head = Process.Start(git)!; string commit = head.StandardOutput.ReadToEnd().Trim(); head.WaitForExit();
        if (head.ExitCode != 0) throw new IOException("Could not identify execution commit.");
        var run = new FeatureLedgerEvidence.Run("SH-curation-actions", Environment.CommandLine, args[5], commit, artifacts, root);
        var register = new FeatureLedgerEvidence.Register(1, "SH-curation-actions", FeatureLedger.ReferenceCommit, new() { run }, comparisons);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "SH-curation-actions.json"), register);
        var validation = FeatureLedgerEvidence.Load(root, inventory, [register]);
        var problems = validation.FatalProblems.Concat(validation.Entries.Values.Where(entry => entry.Row.Proofs.Count > 0).SelectMany(entry => entry.Problems)).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "candidate-validation.json"), new { valid = problems.Length == 0, problems });
        if (problems.Length > 0) throw new InvalidDataException("Curation receipt is invalid: " + string.Join("; ", problems.Take(4)));
        Console.WriteLine("SH-curation-actions: " + comparisons.Count(row => row.Proofs.Count > 0) + " supported; " + comparisons.Count(row => row.Proofs.Count == 0) + " OPEN.");
        return 0;
    }

    private static void CurationPendingList(string root, string output, FeatureLedger.Observation[] rows)
    {
        var views = new[] { "FamilyQuant/ClusterGenerationControl.xaml", "FamilyQuant/CurationQuantitationPanel.xaml" }
            .ToDictionary(path => path, path => XDocument.Load(Path.Combine(root, path)), StringComparer.Ordinal);
        var native = new ClusterGenerationControl(new FamilyClusterFile { MyExtendedFastaItems = new()
        { new() { SequenceIdentifier = "pending_wording_fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 } } });
        native.Measure(new System.Windows.Size(1280, 900)); native.Arrange(new System.Windows.Rect(0, 0, 1280, 900)); native.UpdateLayout();
        string? NativeValue(string name, string property)
        {
            object? element = ShellMetadataSnapshot.Resolve(native, name);
            if (element == null) return null;
            if (property == "$type") return element.GetType().Name + " " + name;
            var info = ShellMetadataSnapshot.Property(element.GetType(), property);
            if (info == null || info.GetIndexParameters().Length != 0) return null;
            object? value = info.GetValue(element);
            return value == null ? "<null>" : value is string || value is bool || value.GetType().IsEnum || value is IFormattable
                ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }
        var sources = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).StartsWith("ClusterGenerationControl", StringComparison.Ordinal)
                || Path.GetFileName(path).StartsWith("Curation", StringComparison.Ordinal))
            .Concat(new[] { "FamilyQuant.Core/HelpContent.cs", "FamilyQuant.Core/NormalizationMode.cs", "FamilyQuant.Core/ReportView.cs" }
                .Select(path => Path.Combine(root, path)).Where(File.Exists))
            .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
        string Cell(string value) => value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal).Replace("\u2013", "\\u2013", StringComparison.Ordinal).Replace("\u2014", "\\u2014", StringComparison.Ordinal);
        var lines = new List<string> { "lane\trow_id\told_text\tnew_text\treason" };
        const string windows = "FL-secondary-runtime: D:/FamilyQuant-data/out-refresh/20261102-070844-FamilyQuant-gate/secondary-runtime";
        foreach (var row in rows.OrderBy(row => row.Id, StringComparer.Ordinal))
        {
            string current = "<No exact current value captured>";
            string reason = "UNPROVED. Tracking entry for desk/Paulo; no semantic proof or DROP. ";
            if (row.Source.EndsWith(".xaml", StringComparison.Ordinal))
            {
                var property = Regex.Match(row.Detail, @"^(\w+)\.(\w+) = ");
                if (property.Success)
                {
                    var matches = views.Values.SelectMany(view => view.Descendants()).Where(node => node.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Name" && attribute.Value == property.Groups[1].Value)).ToArray();
                    if (matches.Length == 1 && matches[0].Attribute(property.Groups[2].Value) is { } value)
                        current = property.Groups[1].Value + "." + property.Groups[2].Value + " = " + value.Value;
                    if (NativeValue(property.Groups[1].Value, property.Groups[2].Value) is { } nativeValue)
                        current = property.Groups[1].Value + "." + property.Groups[2].Value + " = " + nativeValue;
                }
                else if (row.Kind == "control")
                {
                    string name = row.Detail.Split(' ').Last();
                    var matches = views.Values.SelectMany(view => view.Descendants()).Where(node => node.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Name" && attribute.Value == name)).ToArray();
                    if (matches.Length == 1) current = matches[0].Name.LocalName + " " + name;
                    else current = "<Original inline/unnamed control is absent from the current curation view>";
                    if (NativeValue(name, "$type") is { } nativeType) current = nativeType;
                }
                reason += "Unshown default-fixture native scalar where available, otherwise current XAML declaration/structural capture; runtime equivalence not inferred. ";
            }
            else if (row.Kind == "handler definition")
            {
                string name = Regex.Match(row.Detail, @"\b(\w+)\(").Groups[1].Value;
                var pattern = new Regex(@"(?:private|public)\s+(?:async\s+)?void\s+" + Regex.Escape(name)
                    + @"\s*\([^)]*\)\s*(?:=>[^;]*;|\{[^{}]*\})", RegexOptions.Singleline);
                var match = sources.Select(source => (source.Key, Match: pattern.Match(source.Value))).FirstOrDefault(value => value.Match.Success);
                if (match.Match != null && match.Match.Success) current = match.Key + ": " + match.Match.Value.Trim();
                reason += "Current handler entry source capture; the displayed secondary path was not executed here. ";
            }
            else if (sources.Any(source => source.Value.Contains(row.Detail, StringComparison.Ordinal)))
            {
                current = row.Detail;
                reason += "Exact token remains in current feature source; compiled reachability is assigned to N6a. ";
            }
            else reason += "No exact token at the captured feature sources; inspect as wording/fact gap. ";
            if (row.Detail.Contains("ButtonHelp", StringComparison.Ordinal) || row.Detail.Contains("ButtonAlignSelected", StringComparison.Ordinal)
                || row.Detail.Contains("ButtonManageLocks", StringComparison.Ordinal)) reason += windows + "; handler/window boundary tracked per M6. ";
            lines.Add(string.Join("\t", "FQShell", row.Id, Cell(row.Detail), Cell(current), Cell(row.Source + ":" + row.Line + " " + reason)));
        }
        File.WriteAllLines(Path.Combine(output, "wording-for-paulo.tsv"), lines, new UTF8Encoding(false));
    }
}
