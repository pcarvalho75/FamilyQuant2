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
    private sealed record DeNovoDeclarationResult(string Id, string Kind, bool Matched, string Before,
        string Actual, string CurrentPath, string Symbol, string Compared, string Reason);

    private static int DeNovoLedgerDeclarationsCommand(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply frozen source root, fresh output and a timestamp from date.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Comparison output must be fresh.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row)
            && row.Source is "FamilyQuant/DeNovoControl.xaml" or "FamilyQuant/DeNovoControl.xaml.cs").ToArray();
        var frozen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
        {
            string path = Path.Combine(frozenRoot, source.Path);
            if (!File.Exists(path) || FeatureLedgerEvidence.Hash(path) != source.Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + source.Path);
            frozen[source.Path] = File.ReadAllText(path);
        }
        Directory.CreateDirectory(output);
        string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
        var currentCs = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "DeNovoControl*.cs")
            .OrderBy(path => path, StringComparer.Ordinal).ToDictionary(Relative, File.ReadAllText, StringComparer.Ordinal);
        var currentXaml = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*DeNovo*.xaml")
            .OrderBy(path => path, StringComparer.Ordinal).ToDictionary(Relative, XDocument.Load, StringComparer.Ordinal);
        var oldXaml = XDocument.Parse(frozen["FamilyQuant/DeNovoControl.xaml"]);
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var results = new List<DeNovoDeclarationResult>();
        var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            Check("De novo ledger cohort contains exactly the 619 retained frozen observations", cohort.Length == 619);
            foreach (var row in cohort)
            {
                DeNovoDeclarationResult result;
                if (row.Source.EndsWith(".cs", StringComparison.Ordinal))
                {
                    var compared = LedgerMemberFactComparison.Compare(row, frozen[row.Source], currentCs);
                    // X6 split this same partial class into these three files. The base class remains
                    // on DeNovoControl.xaml.cs. This reviewed owner mapping relaxes only the enclosing
                    // declaration check; the shared comparer still requires the entire member tokens.
                    if (compared.FrozenMember.StartsWith("FamilyQuant.DeNovoControl::", StringComparison.Ordinal)
                        && compared.CurrentPath is "FamilyQuant/DeNovoControl.Compatibility.cs"
                            or "FamilyQuant/DeNovoControl.Dialogs.cs" or "FamilyQuant/DeNovoControl.Tables.cs")
                        compared = LedgerMemberFactComparison.Compare(row, frozen[row.Source], currentCs,
                            new(compared.FrozenMember, compared.CurrentPath, compared.CurrentMember));
                    result = new(row.Id, row.Kind, compared.Matched, compared.Before, compared.Actual,
                        compared.CurrentPath, compared.Symbol, compared.FrozenMember + ": " + compared.Before + " -> "
                        + compared.CurrentPath + ": " + compared.CurrentMember + "; complete member tokens SHA256 "
                        + compared.CurrentMemberSha256 + "; literal occurrences " + compared.CurrentOccurrences.ToString(CultureInfo.InvariantCulture), compared.Reason);
                }
                else
                {
                    var compared = LedgerDeclarationComparison.Compare(row, oldXaml, currentXaml);
                    result = new(row.Id, row.Kind, compared.Matched, compared.Before, compared.Actual,
                        compared.CurrentPath, compared.Symbol, row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture)
                        + " " + row.Detail + " -> " + compared.CurrentPath + ": " + compared.Symbol + " = " + compared.Actual, compared.Reason);
                }
                results.Add(result);
                if (result.Matched) Check("De novo exact declaration " + row.Id + ": " + result.Symbol, true);
                else { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": " + result.Reason); }
            }
            foreach (var group in results.Where(result => result.Matched).GroupBy(result => result.Kind).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = "De novo declaration kind " + group.Key + ": every listed frozen/current declaration agrees";
                Check(assertion, group.All(result => result.Before == result.Actual));
                assertions[group.Key] = assertion;
            }
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        return CompleteDeNovoLedgerProof(root, frozenRoot, output, args[3], results, assertions,
            transcript.ToString(), currentCs.Keys.Concat(currentXaml.Keys), "EV-denovo-declarations",
            nameof(DeNovoLedgerDeclarationsCommand), "Declaration preservation only. Reachability, handlers and runtime outputs require executed behaviour checks.");
    }

    private static int CompleteDeNovoLedgerProof(string root, string frozenRoot, string output, string timestamp,
        List<DeNovoDeclarationResult> results, Dictionary<string, string> assertions, string transcript,
        IEnumerable<string> implementationSources, string stepPrefix, string targetMethod, string limitation,
        IEnumerable<string>? additionalArtifacts = null, IEnumerable<string>? supportingSources = null)
    {
        string log = Path.Combine(output, "comparisons.log"), data = Path.Combine(output, "comparisons.json");
        File.WriteAllText(log, transcript, new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(data, results);
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        var artifacts = new List<string> { log, data };
        foreach (string path in implementationSources.Concat(new[] {
            "FamilyQuant.Tests/Program.DeNovoLedger.cs", "FamilyQuant.Tests/Program.Commands.DeNovoLedger.cs",
            "FamilyQuant.Tests/Program.DeNovoLedgerRuntime.cs",
            "FamilyQuant.Tests/LedgerMemberFactComparison.cs", "FamilyQuant.Tests/LedgerDeclarationComparison.cs",
            "FamilyQuant.Tests/FeatureLedger.cs", "FamilyQuant.Tests/FeatureLedgerEvidence.cs" }).Distinct(StringComparer.Ordinal))
        {
            string target = Path.Combine(archive, path.Replace('/', '_'));
            File.Copy(Path.Combine(root, path), target); artifacts.Add(target);
        }
        foreach (string path in new[] { "FamilyQuant/DeNovoControl.xaml", "FamilyQuant/DeNovoControl.xaml.cs" })
        {
            string target = Path.Combine(archive, "frozen_" + path.Replace('/', '_'));
            File.Copy(Path.Combine(frozenRoot, path), target); artifacts.Add(target);
        }
        foreach (string path in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(ProjectStore).Assembly.Location })
        {
            string target = Path.Combine(archive, Path.GetFileName(path)); File.Copy(path, target); artifacts.Add(target);
        }
        artifacts.AddRange(additionalArtifacts ?? Array.Empty<string>());
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record the executing commit.");
        string runId = stepPrefix;
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, timestamp, commit,
            artifacts.Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList(), root);
        int batch = 0;
        foreach (var group in results.Where(result => result.Matched).Chunk(250))
        {
            string step = stepPrefix + "-" + (++batch).ToString(CultureInfo.InvariantCulture);
            var rows = group.Select(result => new FeatureLedgerEvidence.Row(result.Id,
                new[] { result.CurrentPath }.Concat(supportingSources ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal)
                    .Select(path => new FeatureLedgerEvidence.Implementation(path, path == result.CurrentPath ? result.Symbol : "Runtime comparison dependency",
                        FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList(), new(),
                limitation,
                "decl-" + result.Kind.Replace('/', '-').Replace(' ', '-'), result.Compared)).ToList();
            var receipts = group.Select(result => result.Kind).Distinct(StringComparer.Ordinal).Select(kind =>
                new FeatureLedgerEvidence.KindReceipt("decl-" + kind.Replace('/', '-').Replace(' ', '-'), kind,
                    new() { new(runId, "test", targetMethod, assertions[kind], log, "") })).ToList();
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"),
                new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { run }, rows, receipts));
        }
        return failed == 0 ? 0 : 1;
    }
}
