using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ShellExecution3(string[] args)
    {
        if (args.Length is not (7 or 8) || args.Length == 8 && args[7] != "--negative-control"
            || !DateTimeOffset.TryParse(args[6], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
            throw new ArgumentException("Supply frozen source/app, trusted SEPro package, fixed execution JSON, fresh output and date.");
        if (!NativeProofDesktop.IsPrivate) return IsolatedNativeProof(new[] { "--isolated-native-proof" }.Concat(args).ToArray());
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[5]);
        using var scope = JsonDocument.Parse(File.ReadAllText(args[4]));
        var ids = scope.RootElement.EnumerateArray().Select(row => row.GetProperty("row_id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => ids.Contains(row.Id)).ToArray();
        if (ids.Count != 20 || cohort.Length != 20 || cohort.Count(row => row.Kind is "handler" or "handler definition") != 18)
            throw new InvalidDataException("Use the fixed twenty-row execution cohort.");
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
            if (FeatureLedgerEvidence.Hash(Path.Combine(args[1], file.Path)) != file.Sha256)
                throw new InvalidDataException("Frozen source differs: " + file.Path);
        // The existing packet executes both versions' enabled buttons, real native chooser acceptance,
        // actual CSV writes, cancellation and failure paths on an unselected private desktop.
        int result = DeNovoCommandEvidence(["--prove-denovo-commands", args[1], args[2], args[3], args[5], args[6]]);
        if (result != 0) return result;
        var baseRegister = JsonSerializer.Deserialize<FeatureLedgerEvidence.Register>(File.ReadAllText(Path.Combine(output, "EV-denovo-commands-1.json")), FeatureLedgerEvidence.JsonOptions)!;
        var rows = baseRegister.Rows.Where(row => ids.Contains(row.ObservationId)).ToList();
        string extraLog = Path.Combine(output, "execution3-extra.log");
        var original = Console.Out; using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var values = new List<object>();
        var legacy = Assembly.LoadFrom(Path.Combine(args[2], "FamilyQuant.dll"));
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            Check("SH execution3 all eighteen frozen/current handler rows have actual successful button receipts", rows.Count == 18 && rows.All(row => row.Proofs.Count > 0));
            var accession = legacy.GetType("FamilyQuant.FamilyNaming", true)!.GetMethod("Accession", BindingFlags.Public | BindingFlags.Static)!;
            string contaminant = JsonSerializer.Deserialize<string>(cohort.Single(row => row.Id == "FQ15-57DFC76ED641").Detail)!;
            var labels = new[]
            {
                (Input: contaminant + "P12345|owned", Expected: "P12345"),
                (Input: SequenceLabels.DecoyPrefix + contaminant + "P12345|owned", Expected: ""),
                (Input: SequenceLabels.DecoyPrefix + "P12345|owned", Expected: ""),
                (Input: "sp|P12345|owned", Expected: "P12345"),
                (Input: "owned_identifier", Expected: "owned_identifier")
            }.Select(item => new { item.Input, item.Expected,
                Before = (string)accession.Invoke(null, [item.Input])!, Current = FamilyNaming.Accession(item.Input) }).ToArray();
            var comparedLabels = labels.Select((value, index) => new { value.Input, value.Expected, value.Before, value.Current,
                ComparedCurrent = value.Current + (args.Length == 8 && index == 0 ? " PLANTED WRONG ACCESSION" : "") }).ToArray();
            bool labelsEqual = comparedLabels.All(value => value.Before == value.ComparedCurrent && value.Current == value.Expected);
            const string labelAssertion = "SH execution3 contaminant accession stripping and decoy exclusion equal frozen outputs for five native inputs";
            Check(labelAssertion, labelsEqual);
            values.Add(new { kind = "accessions", labels = comparedLabels, plantedMismatch = args.Length == 8 });
            string fasta = Path.Combine(output, "owned-no-decoys.fasta");
            File.WriteAllText(fasta, ">owned_a\nACDEFGHIKLMNPQRSTVWY\n>owned_b\nACDEFGHIKLMNPQASTVWY\n", new UTF8Encoding(false));
            string inputHash = FeatureLedgerEvidence.Hash(fasta);
            var oldMethod = legacy.GetType("FamilyQuant.FamilyClusterFile", true)!.GetMethod("DivideAndConquer", BindingFlags.Public | BindingFlags.Static)!;
            string before = "", current = "";
            try { oldMethod.Invoke(null, [fasta, 0.0, SequenceLabels.DecoyPrefix, true]); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException error) { before = error.Message; }
            try { FamilyClusterFile.DivideAndConquer(fasta); }
            catch (InvalidOperationException error) { current = error.Message; }
            string frozenInstruction = JsonSerializer.Deserialize<string>(cohort.Single(row => row.Id == "FQ15-0DB3E899880B").Detail)!;
            bool refused = before.Length > 0 && current == before && current.Contains(frozenInstruction, StringComparison.Ordinal)
                && FeatureLedgerEvidence.Hash(fasta) == inputHash;
            const string refusalAssertion = "SH execution3 actual frozen/current identity search refuses the target-only FASTA with the same complete target-decoy and scan-range advice, preserving input bytes";
            Check(refusalAssertion, refused);
            values.Add(new { kind = "no-decoy-refusal", before, current, inputHash });
            string valuesPath = Path.Combine(output, "execution3-values.json");
            FeatureLedgerEvidence.WriteJson(valuesPath, values);
            foreach (var item in new[] { (Id: "FQ15-57DFC76ED641", Passed: labelsEqual, Assertion: labelAssertion),
                (Id: "FQ15-0DB3E899880B", Passed: refused, Assertion: refusalAssertion) })
            {
                var observed = cohort.Single(row => row.Id == item.Id);
                var implementation = new[] { "FamilyQuant.Core/FamilyNaming.cs", "FamilyQuant.Core/SequenceLabels.cs", "FamilyQuant.Core/FamilyClusterScoring.cs",
                    "FamilyQuant.Core/USearchTools.cs", "FamilyQuant.Core/FamilyQuantFile.cs", "FamilyQuant.Tests/Program.ShellExecution3.cs",
                    "FamilyQuant.Tests/Program.Commands.ShellExecution3.cs" }
                    .Select(path => new FeatureLedgerEvidence.Implementation(path, item.Assertion, FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList();
                if (item.Passed) rows.Add(new FeatureLedgerEvidence.Row(item.Id, implementation,
                    new() { new("SH-execution3-extra", "test", nameof(ShellExecution3), item.Assertion, extraLog, "") },
                    "Native frozen/current synthetic fixture; no scientific policy change.",
                    Compared: observed.Source + ":" + observed.Line + " " + observed.Detail + "; " + item.Assertion + "; complete native before/current values in " + valuesPath));
            }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); File.WriteAllText(extraLog, transcript.ToString(), new UTF8Encoding(false)); }
        if (failed > 0) return 1;
        using (var probe = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "command-probe.json"))))
        {
            object? Stable(JsonElement element) => element.ValueKind switch
            {
                JsonValueKind.Object => new SortedDictionary<string, object?>(element.EnumerateObject()
                    .Where(property => property.Name != "ElapsedSeconds")
                    .ToDictionary(property => property.Name, property => Stable(property.Value)), StringComparer.Ordinal),
                JsonValueKind.Array => element.EnumerateArray().Select(Stable).ToArray(),
                JsonValueKind.String => element.GetString()!.Replace(output, "{out}", StringComparison.Ordinal)
                    .Replace(output.Replace('\\', '/'), "{out}", StringComparison.Ordinal),
                _ => element.Clone()
            };
            var snapshots = probe.RootElement.GetProperty("snapshots").EnumerateArray().Select(snapshot =>
                new SortedDictionary<string, object?>(snapshot.EnumerateObject().Where(property => property.Name != "status")
                    .ToDictionary(property => property.Name, property => Stable(property.Value)), StringComparer.Ordinal)).ToArray();
            var choices = probe.RootElement.GetProperty("chooserCalls").EnumerateArray().Select(choice =>
                new SortedDictionary<string, object?>(choice.EnumerateObject().Where(property => property.Name != "controls")
                    .ToDictionary(property => property.Name, property => Stable(property.Value)), StringComparer.Ordinal)).ToArray();
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "execution3-stable.json"), new { snapshots, choices });
        }
        string archive = Path.Combine(output, "execution3-producer"); Directory.CreateDirectory(archive);
        foreach (string relative in new[] { "FamilyQuant.Tests/Program.ShellExecution3.cs", "FamilyQuant.Tests/Program.Commands.ShellExecution3.cs", "FamilyQuant.Tests/Program.Commands.DeNovoLedger.cs" })
            File.Copy(Path.Combine(root, relative), Path.Combine(archive, Path.GetFileName(relative)));
        File.Copy(args[4], Path.Combine(archive, "execution20.json"));
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var git = Process.Start(start)!; string commit = git.StandardOutput.ReadToEnd().Trim(); git.WaitForExit();
        if (git.ExitCode != 0) throw new IOException("Cannot identify execution commit.");
        var artifacts = Directory.GetFiles(archive).Append(extraLog).Append(Path.Combine(output, "execution3-values.json")).Append(Path.Combine(output, "execution3-stable.json"))
            .Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList();
        var run = new FeatureLedgerEvidence.Run("SH-execution3-extra", Environment.CommandLine, args[6], commit, artifacts, root);
        var register = new FeatureLedgerEvidence.Register(1, "SH-execution3", FeatureLedger.ReferenceCommit, baseRegister.Runs.Concat(new[] { run }).ToList(), rows.OrderBy(row => row.ObservationId, StringComparer.Ordinal).ToList());
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "SH-execution3.json"), register);
        var validation = FeatureLedgerEvidence.Load(root, inventory, [register]);
        var problems = validation.FatalProblems.Concat(validation.Entries.Values.SelectMany(entry => entry.Problems)).ToArray();
        if (problems.Length > 0) throw new InvalidDataException(LedgerDiagnostics.Capped(problems, 4));
        Console.WriteLine("SH-execution3: " + rows.Count + " native supported rows; zero disposition changes.");
        return 0;
    }
}
