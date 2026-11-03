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
    private static int ApplyPauloRulings(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Supply fresh output and a timestamp from date.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Ruling output must be fresh.");
        Directory.CreateDirectory(output); EnsureApplication();
        if (FeatureLedgerEvidence.Hash(LedgerPauloRulings.RulesPath) != LedgerPauloRulings.RulesSha256)
            throw new InvalidDataException("Paulo's recorded rulings changed; review the producer before applying them.");
        string tsv = Path.Combine(output, "wording-input.tsv"), rules = Path.Combine(output, "wording-rulings.md");
        File.Copy(LedgerPauloRulings.InputPath, tsv); File.Copy(LedgerPauloRulings.RulesPath, rules);
        string factFile = Path.Combine(output, "rulings-fact-check.tsv"); File.Copy(LedgerPauloRulings.FactChecksPath, factFile);
        var factChecks = LedgerPauloRulings.ReadFactChecks(factFile);
        // Paulo's group ruling is optional: until he rules the file is absent or empty, nothing closes under rule 4, and the
        // packet records no group artifact.
        string groupFile = "";
        if (File.Exists(LedgerPauloRulings.GroupRulingPath))
        { groupFile = Path.Combine(output, "paulo-group-ruling.tsv"); File.Copy(LedgerPauloRulings.GroupRulingPath, groupFile); }
        var groupRulings = groupFile.Length > 0 ? LedgerPauloRulings.ReadGroupRulings(groupFile) : new Dictionary<string, LedgerPauloRulings.GroupRuling>(StringComparer.Ordinal);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var byId = inventory.Observations.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var inputs = LedgerPauloRulings.ReadInputs(tsv);
        List<LedgerPauloRulings.Decision> Evaluate() => inputs.GroupBy(input => input.Id, StringComparer.Ordinal).Select(group =>
        {
            byId.TryGetValue(group.Key, out var observation);
            var evaluated = group.OrderBy(input => input.Lane, StringComparer.Ordinal).ThenBy(input => input.OldText, StringComparer.Ordinal)
                .ThenBy(input => input.NewText, StringComparer.Ordinal).Select(input => LedgerPauloRulings.Evaluate(input, observation, LedgerRulingLiveText.Find, factChecks, groupRulings)).ToArray();
            return evaluated.Where(decision => decision.Closed).OrderBy(decision => decision.Rule, StringComparer.Ordinal)
                .ThenBy(decision => decision.NewText, StringComparer.Ordinal).FirstOrDefault() ?? evaluated[0];
        }).OrderBy(decision => decision.Id, StringComparer.Ordinal).ToList();
        var original = Console.Out; using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        List<LedgerPauloRulings.Decision> decisions;
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            PauloRulingGuardTests(root, inventory, output, args[2]);
            decisions = Evaluate(); var repeat = Evaluate();
            Check("ruling decisions are byte-identical on two evaluations of the same pinned input",
                JsonSerializer.Serialize(decisions, FeatureLedgerEvidence.JsonOptions) == JsonSerializer.Serialize(repeat, FeatureLedgerEvidence.JsonOptions));
            Check("an empty new text closes no input row under any ruling", inputs.All(input =>
            { byId.TryGetValue(input.Id, out var row); return !LedgerPauloRulings.Evaluate(input with { NewText = "" }, row, LedgerRulingLiveText.Find, factChecks).Closed; }));
            Check("every accepted wording ruling has an independent kept fact verdict", decisions.Where(decision => decision.Closed && decision.Rule is "1" or "3")
                .All(decision => factChecks.TryGetValue(decision.Id, out var fact) && fact.Verdict == "kept"));
            Check("every rule-4 closure is listed in the pinned group ruling and is no DROP or unlisted execution row", decisions.Where(decision => decision.Closed && decision.Rule == "4")
                .All(decision => groupRulings.TryGetValue(decision.Id, out var listed) && listed.Group.Length > 0 && byId.TryGetValue(decision.Id, out var observed)
                    && !FeatureLedger.IsApprovedDrop(observed) && (observed.Kind is not ("export/storage fact" or "format/export") || listed.Message)));
            Check("every closed export-kind row is listed as message (Rule 5)", decisions.Where(decision => decision.Closed && byId.TryGetValue(decision.Id, out var observed)
                && observed.Kind is "export/storage fact" or "format/export").All(decision => groupRulings.TryGetValue(decision.Id, out var listed) && listed.Message));
            if (groupRulings.Count == 0)
                Check("with no group ruling from Paulo, no row closes under rule 4", decisions.All(decision => decision.Rule != "4"));
            Console.WriteLine("Group ruling: " + (groupFile.Length > 0 ? groupRulings.Count.ToString(CultureInfo.InvariantCulture) + " listed rows, "
                + groupRulings.Keys.Count(id => !inputs.Any(input => input.Id == id)).ToString(CultureInfo.InvariantCulture) + " of them absent from the wording input"
                : "absent; rule 4 closes nothing") + ".");
            foreach (string rule in new[] { "1", "2", "3", "4" })
                Console.WriteLine("Rule " + rule + ": " + decisions.Count(decision => decision.Closed && decision.Rule == rule).ToString(CultureInfo.InvariantCulture) + " accepted rows.");
            foreach (var group in decisions.Where(decision => !decision.Closed).GroupBy(decision => decision.Reason).OrderBy(group => group.Key, StringComparer.Ordinal))
                Console.WriteLine("OPEN " + group.Count().ToString(CultureInfo.InvariantCulture) + ": " + group.Key);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"); File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        if (failed > 0) return 1; // Never publish a register or remaining list from failed guards.
        string values = Path.Combine(output, "decisions.json"); FeatureLedgerEvidence.WriteJson(values, decisions);
        string packetPath = Path.Combine(output, "ruling-packet.json");
        FeatureLedgerEvidence.WriteJson(packetPath, new LedgerPauloRulings.Packet(2, LedgerPauloRulings.Command, tsv, rules,
            FeatureLedgerEvidence.Hash(tsv), LedgerPauloRulings.RulesSha256, decisions, factFile, FeatureLedgerEvidence.Hash(factFile),
            groupFile, groupFile.Length > 0 ? FeatureLedgerEvidence.Hash(groupFile) : ""));
        string left = Path.Combine(output, "wording-left-for-paulo.tsv");
        static string Clean(string text)
        {
            string value = text.Replace("\t", "\\t", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
            return value.Contains('"') ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
        }
        var leftLines = new[] { "lane\trow_id\told_text\tnew_text\treason" }.Concat(decisions.Where(decision => !decision.Closed).Select(decision =>
            string.Join('\t', new[] { decision.Lane, decision.Id, decision.OldText, decision.NewText,
                decision.Reason + (decision.FactCheckNote.Length > 0 ? " " + decision.FactCheckNote : "") }.Select(Clean))));
        File.WriteAllText(left, string.Join('\n', leftLines) + "\n", new UTF8Encoding(false));
        // This is a derived review list, replaced atomically; the shared input is never rewritten.
        Directory.CreateDirectory(Path.GetDirectoryName(LedgerPauloRulings.LeftPath)!);
        string staged = LedgerPauloRulings.LeftPath + ".FQResults.tmp"; File.Copy(left, staged, true); File.Move(staged, LedgerPauloRulings.LeftPath, true);
        void Retain(string source, string name)
        { string destination = Path.Combine(output, "executed", name); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination); }
        foreach (string path in new[] { "FamilyQuant.Tests/LedgerPauloRulings.cs", "FamilyQuant.Tests/LedgerRulingLiveText.cs",
            "FamilyQuant.Tests/FeatureLedgerEvidence.Rulings.cs", "FamilyQuant.Tests/FeatureLedgerEvidence.cs", "FamilyQuant.Tests/Program.PauloRulings.cs",
            "FamilyQuant.Tests/Program.PauloRulingTests.cs", "FamilyQuant.Tests/Program.Commands.PauloRulings.cs" }) Retain(Path.Combine(root, path), path);
        foreach (string assembly in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(FamilyClusterFile).Assembly.Location })
            Retain(assembly, Path.GetFileName(assembly));
        foreach (string source in decisions.Where(decision => decision.Closed && decision.Rule == "2").Select(decision => byId[decision.Id].Source).Distinct(StringComparer.Ordinal))
            Retain(Path.Combine(LedgerPauloRulings.FrozenRoot, source), "frozen/" + source);
        var processInfo = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        processInfo.ArgumentList.Add("rev-parse"); processInfo.ArgumentList.Add("HEAD"); using var process = Process.Start(processInfo)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); if (process.ExitCode != 0) throw new IOException("Cannot record source commit.");
        var run = new FeatureLedgerEvidence.Run("rulings", Environment.CommandLine, args[2], commit,
            Directory.GetFiles(output, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList(), root);
        string[] ruleText = File.ReadAllLines(rules).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        var rows = decisions.Where(decision => decision.Closed).Select(decision =>
        {
            var observed = byId[decision.Id]; string current = FeatureLedgerEvidence.CurrentPath(root, observed.Source);
            if (current.Length == 0) throw new InvalidDataException("No current implementation for ruling: " + decision.Id);
            string symbol = decision.WitnessSymbol.Length > 0 ? decision.WitnessSymbol
                : decision.Rule == "4" ? "Paulo group ruling" : "Current theme; frozen Style/Setter anchor";
            string ruleCited = decision.Rule == "4" ? LedgerPauloRulings.GroupRule(decision) : ruleText[int.Parse(decision.Rule, CultureInfo.InvariantCulture) - 1];
            return new FeatureLedgerEvidence.Row(decision.Id, new() { new(current, symbol, FeatureLedgerEvidence.Hash(Path.Combine(root, current))) },
                new() { new("rulings", "ruling", "Rule " + decision.Rule, FeatureLedgerEvidence.RulingAssertion(ruleCited, decision), packetPath, "Paulo") },
                "Paulo's recorded ruling; not a behavioral/export test. Reviewer samples accepted rows.", Compared: FeatureLedgerEvidence.RulingComparison(decision));
        }).ToList();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, LedgerPauloRulings.Step + ".json"), new FeatureLedgerEvidence.Register(1, LedgerPauloRulings.Step,
            FeatureLedger.ReferenceCommit, new() { run }, rows));
        Console.WriteLine("Ruling register: " + rows.Count.ToString(CultureInfo.InvariantCulture) + "; left: " + decisions.Count(decision => !decision.Closed).ToString(CultureInfo.InvariantCulture));
        return failed == 0 ? 0 : 1;
    }
}
