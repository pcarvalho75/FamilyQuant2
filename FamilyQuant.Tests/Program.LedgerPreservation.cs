using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record LedgerProofCase(string ObservationId, string Operation, string Expected, string Actual,
        string Assertion, string Path, string Symbol, bool Passed);

    private static int LedgerPreservationCommand(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Supply a fresh output directory and execution timestamp from date.");
        string root = FindLedgerRoot()!;
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Proof output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        List<LedgerProofCase> cases;
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            cases = LedgerPreservationTests(root, output, inventory);
            Check("native preservation proof has at least fifty observation-specific checks", cases.Count >= 50);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"), values = Path.Combine(output, "actual-values.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(values, cases);
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        string checker = Path.Combine(archive, "Program.LedgerPreservation.cs");
        File.Copy(Path.Combine(root, "FamilyQuant.Tests/Program.LedgerPreservation.cs"), checker);
        string testAssembly = Path.Combine(archive, "FamilyQuant.Tests.dll"), coreAssembly = Path.Combine(archive, "FamilyQuant.Core.dll");
        File.Copy(typeof(Program).Assembly.Location, testAssembly);
        File.Copy(typeof(ProjectStore).Assembly.Location, coreAssembly);
        string runId = "N5-native-preservation";
        var rows = new Dictionary<string, FeatureLedgerEvidence.Row>(StringComparer.Ordinal);
        // A receipt refresh must not discard OPEN mappings merely because a later step moved their
        // source again. Only unproved mappings carry forward; every proof below is freshly executed.
        string previousRegister = Path.Combine(root, "docs/feature-ledger-evidence/N5.json");
        if (File.Exists(previousRegister))
        {
            var previous = JsonSerializer.Deserialize<FeatureLedgerEvidence.Register>(
                File.ReadAllText(previousRegister), FeatureLedgerEvidence.JsonOptions);
            foreach (var row in previous?.Rows.Where(row => row.Proofs.Count == 0)
                ?? Enumerable.Empty<FeatureLedgerEvidence.Row>()) rows[row.ObservationId] = row;
        }
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        FeatureLedgerEvidence.Implementation Mapping(string path, string symbol)
        {
            if (!hashes.TryGetValue(path, out string? hash)) hashes[path] = hash = FeatureLedgerEvidence.Hash(Path.Combine(root, path));
            return new(path, symbol, hash);
        }
        // Source relocation is recorded for the complete first-pass cohort, without claiming coverage.
        foreach (var observation in inventory.Observations.Where(o => !FeatureLedger.IsApprovedDrop(o)))
        {
            string current = FeatureLedgerEvidence.CurrentPath(root, observation.Source);
            if (!current.StartsWith("FamilyQuant.Core/", StringComparison.Ordinal)) continue;
            rows[observation.Id] = new(observation.Id, new() { Mapping(current, "source file: " + Path.GetFileName(current)) }, new(),
                "Source relocation only. No behavior is verified by this mapping.");
        }
        foreach (var mapping in DisplacedLedgerHandlers())
            rows[mapping.Id] = new(mapping.Id, new() { Mapping(mapping.Path, mapping.Symbol) }, new(),
                "Retained H1 handler replacement mapped. Executed UI interaction evidence is still required.");
        foreach (var proof in cases)
            rows[proof.ObservationId] = new(proof.ObservationId, new() { Mapping(proof.Path, proof.Symbol) },
                proof.Passed ? new() { new(runId, "test", nameof(LedgerPreservationTests), proof.Assertion, log, "") } : new(),
                proof.Passed ? null : "Executed assertion failed; row remains OPEN.");
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record source commit.");
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, args[2], commit,
            new() { new(log, FeatureLedgerEvidence.Hash(log)), new(values, FeatureLedgerEvidence.Hash(values)),
                new(checker, FeatureLedgerEvidence.Hash(checker)), new(testAssembly, FeatureLedgerEvidence.Hash(testAssembly)),
                new(coreAssembly, FeatureLedgerEvidence.Hash(coreAssembly)) }, root);
        var register = new FeatureLedgerEvidence.Register(1, "N5", FeatureLedger.ReferenceCommit, new() { run },
            rows.Values.OrderBy(r => r.ObservationId, StringComparer.Ordinal).ToList());
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "N5.json"), register);
        return failed == 0 ? 0 : 1;
    }

    private static List<LedgerProofCase> LedgerPreservationTests(string root, string output, FeatureLedger.Inventory inventory)
    {
        Section("Native per-observation preservation proofs");
        var cases = new List<LedgerProofCase>();
        string Literal(FeatureLedger.Observation row) => JsonSerializer.Deserialize<string>(row.Detail)!;
        void Add(FeatureLedger.Observation row, string operation, string actual, bool matches, string path, string symbol)
        {
            string assertion = "N5 " + row.Id + ": " + operation;
            Check(assertion, matches, "Actual: " + actual);
            cases.Add(new(row.Id, operation, Literal(row), actual, assertion, path, symbol, matches));
        }
        const string storagePath = "FamilyQuant.Core/ProjectStore.cs", sourcePath = "FamilyQuant.Core/DeNovo/DeNovoSource.cs";
        var curation = new FamilyClusterFile { MyExtendedFastaItems = new() { new()
            { SequenceIdentifier = "synthetic-ledger-fixture", Description = "Synthetic storage fixture", Sequence = "PEPTIDEK", UserID = 4, USearchID = 4 } } };
        var quant = new QuantRow { Side = "identified", Run = "synthetic", Scan = 7, PrecursorMz = 500.125, Charge = 2,
            Sequence = "PEPTIDEK", PatternLabXScore = 42.25, IsotopeAreas = new[] { 1d, 2d, 3d, 4d, 5d, 6d }, EnvelopeSum = 21,
            UnderSampledArea = 0.25, UnderSampledIsEstimate = true, FamilyUserId = 4 };
        var deNovo = new QuantRow { Side = "denovo", Run = "synthetic", Scan = 8, PrecursorMz = 500.125, Charge = 2,
            Sequence = "PEPTIDEK", IsotopeAreas = new[] { 1d, 2d, 3d, 4d, 5d, 6d }, EnvelopeSum = 21, FamilyUserId = 4,
            DeNovoEvidence = new() { new() { SourceId = DeNovoSource.DeepNovo.Id, SourceDisplayName = DeNovoSource.DeepNovo.DisplayName,
                ConfidenceField = DeNovoSource.DeepNovo.ConfidenceField, ConfidenceValue = 96.5, Sequence = "PEPTIDEK" } } };
        var manifest = new QuantManifest { IsotopeCount = 6, Provenance = QuantManifest.DefaultProvenance(),
            SpectrumComparisonRows = new() { new() { FileName = "synthetic.raw", NormalizedRunName = "synthetic", ScanNumber = 7, Charge = 2, PrecursorMz = 500.125 } } };
        string project = Path.Combine(output, "synthetic-storage.fqproj");
        string provenanceProject = Path.Combine(output, "synthetic-default-provenance.fqproj");
        ProjectStore.WriteFqproj(provenanceProject, curation, Array.Empty<QuantRow>(), new QuantManifest { Provenance = QuantManifest.DefaultProvenance() });
        var defaultProvenance = ProjectStore.ReadFqproj(provenanceProject).manifest.Provenance;
        manifest.Provenance["family_userid"] = "derived";
        ProjectStore.WriteFqproj(project, curation, new[] { quant, deNovo }, manifest);
        var reopened = ProjectStore.ReadFqproj(project);
        foreach (var group in inventory.Observations.Where(o => o.Source == "FamilyQuant/ProjectStore.cs" && o.Line is >= 527 and <= 539).GroupBy(o => o.Line))
        {
            var pair = group.ToArray();
            if (pair.Length != 2) throw new InvalidDataException("Frozen provenance pair changed.");
            string key = Literal(pair[0]);
            bool present = defaultProvenance.TryGetValue(key, out string? value);
            Add(pair[0], "stored provenance key " + key, present ? key : "missing", present, storagePath, "QuantManifest.DefaultProvenance / ProjectStore round trip");
            Add(pair[1], "stored provenance value for " + key, value ?? "missing", present && value == Literal(pair[1]), storagePath, "QuantManifest.DefaultProvenance / ProjectStore round trip");
        }
        using (var zip = ZipFile.OpenRead(project))
        {
            foreach (var row in inventory.Observations.Where(o => o.Source == "FamilyQuant/ProjectStore.cs" && o.Line is >= 579 and <= 593))
            {
                string expected = Literal(row);
                bool extension = expected == ".fqproj";
                string actual = extension ? ProjectStore.Extension : zip.Entries.FirstOrDefault(e => e.FullName == expected)?.FullName ?? "missing";
                bool correct = actual == expected && (!extension || ProjectStore.IsProjectPath(project) && !ProjectStore.IsProjectPath(project + ".bak"));
                Add(row, extension ? "project extension recognition" : "native ZIP entry " + expected, actual, correct, storagePath, "ProjectStore.WriteFqproj / ReadFqproj");
            }
            using var compressed = new GZipStream(zip.GetEntry(ProjectStore.QuantEntryName)!.Open(), CompressionMode.Decompress);
            using var reader = new StreamReader(compressed);
            string header = reader.ReadLine()!;
            File.WriteAllText(Path.Combine(output, "native-quant-header.tsv"), header + "\n", new UTF8Encoding(false));
            string[] columns = header.Split('\t');
            var stored = reopened.quant.Single(q => q.Side == "identified");
            var storedDeNovo = reopened.quant.Single(q => q.Side == "denovo");
            bool valuesMatch = stored.Side == quant.Side && stored.Run == quant.Run && stored.Scan == quant.Scan && stored.PrecursorMz == quant.PrecursorMz
                && stored.Charge == quant.Charge && stored.Sequence == quant.Sequence && stored.PatternLabXScore == quant.PatternLabXScore
                && stored.IsotopeAreas.SequenceEqual(quant.IsotopeAreas) && stored.EnvelopeSum == quant.EnvelopeSum
                && stored.UnderSampledArea == quant.UnderSampledArea && stored.UnderSampledIsEstimate == quant.UnderSampledIsEstimate
                && stored.FamilyUserId == quant.FamilyUserId && stored.DeNovoEvidence.Count == 0
                && storedDeNovo.DeNovoEvidence.Count == 1 && storedDeNovo.PatternLabXScore == null
                && storedDeNovo.DeNovoEvidence[0].ConfidenceValue == deNovo.DeNovoEvidence[0].ConfidenceValue
                && storedDeNovo.DeNovoEvidence[0].SourceId == deNovo.DeNovoEvidence[0].SourceId;
            var observations = inventory.Observations.Where(o => o.Source == "FamilyQuant/ProjectStore.cs" && o.Line is >= 600 and <= 603).ToArray();
            for (int i = 0; i < observations.Length; i++)
            {
                string actual = i < columns.Length ? columns[i] : "missing";
                Add(observations[i], "native quant column " + i.ToString(CultureInfo.InvariantCulture), actual,
                    valuesMatch && columns.Length == observations.Length && actual == Literal(observations[i]), storagePath, "ProjectStore quant writer/reader");
            }
        }
        var sourceRows = inventory.Observations.Where(o => o.Source == "FamilyQuant/DeNovo/DeNovoSource.cs").ToArray();
        string Failure(Action work)
        {
            try { work(); return "NO REFUSAL"; }
            catch (Exception ex) when (ex is ArgumentException or IOException) { return ex.Message; }
        }
        foreach (var row in sourceRows.Where(o => o.Line is 32 or 34 or 36 or 38))
        {
            string actual = Failure(() => _ = row.Line switch
            {
                32 => new DeNovoSource("", "fixture", "*.csv", "score", false),
                34 => new DeNovoSource("fixture", "", "*.csv", "score", false),
                36 => new DeNovoSource("fixture", "fixture", "", "score", false),
                _ => new DeNovoSource("fixture", "fixture", "*.csv", "", false)
            });
            Add(row, "invalid source descriptor refusal at frozen line " + row.Line.ToString(CultureInfo.InvariantCulture), actual,
                actual.Contains(Literal(row), StringComparison.Ordinal), sourcePath, "DeNovoSource constructor");
        }
        foreach (var group in sourceRows.Where(o => o.Line is 74 or 79 or 84).GroupBy(o => o.Line))
        {
            var source = group.Key == 74 ? DeNovoSource.DenovoOnly : group.Key == 79 ? DeNovoSource.DeNovoAll : DeNovoSource.DeepNovo;
            string folder = Path.Combine(output, source.Id); Directory.CreateDirectory(folder);
            string matched = Path.Combine(folder, "synthetic" + source.FileGlob.TrimStart('*'));
            File.WriteAllText(matched, "Synthetic file-discovery marker; no peptide results.");
            File.WriteAllText(Path.Combine(folder, "ignored.txt"), "Not a configured caller export.");
            var found = DeNovoSourceFileDiscovery.RequireFiles(folder, source);
            string[] actual = { source.Id, source.DisplayName, source.FileGlob, source.ConfidenceColumn };
            var expected = group.ToArray();
            for (int i = 0; i < expected.Length; i++)
                Add(expected[i], "source descriptor " + source.Id + " field " + i.ToString(CultureInfo.InvariantCulture), actual[i],
                    actual[i] == Literal(expected[i]) && found.SequenceEqual(new[] { matched }) && ReferenceEquals(DeNovoSource.FromId(source.Id), source),
                    sourcePath, "DeNovoSource presets / DeNovoSourceFileDiscovery.RequireFiles");
        }
        foreach (var row in sourceRows.Where(o => o.Line == 115))
        {
            string[] actual = new[] { -1d, 101d, double.NaN }.Select(value =>
                Failure(() => _ = new DeNovoSourceSelection(DeNovoSource.DenovoOnly, value))).ToArray();
            Add(row, "invalid source cutoff refusal", string.Join("; ", actual), actual.All(message => message.Contains(Literal(row), StringComparison.Ordinal)),
                sourcePath, "DeNovoSourceSelection constructor");
        }
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            foreach (var row in sourceRows.Where(o => o.Line is 126 or 127 or 143 or 144))
            {
                string label = DeNovoSource.DenovoOnly.ConfidenceLabel;
                Func<double, string> render = row.Line < 140 ? n => new DeNovoSourceSelection(DeNovoSource.DenovoOnly, n).CutoffLabel
                    : n => new DeNovoInputManifest { ConfidenceField = label, MinimumConfidence = n }.CutoffDescription;
                string[] actual = { render(81), render(81.25), render(81.1234) };
                bool correct = actual.SequenceEqual(new[] { label + " >= 81", label + " >= 81.25", label + " >= 81.123" });
                Add(row, "invariant cutoff format at frozen line " + row.Line.ToString(CultureInfo.InvariantCulture), string.Join("; ", actual), correct,
                    sourcePath, row.Line < 140 ? "DeNovoSourceSelection.CutoffLabel" : "DeNovoInputManifest.CutoffDescription");
            }
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        string empty = Path.Combine(output, "empty-source"); Directory.CreateDirectory(empty);
        foreach (var row in sourceRows.Where(o => o.Line is 158 or 163 || o.Line is >= 171 and <= 173))
        {
            string actual = Failure(() => _ = DeNovoSourceFileDiscovery.RequireFiles(row.Line == 158 ? "" : row.Line == 163
                ? Path.Combine(output, "missing-source") : empty, DeNovoSource.DenovoOnly));
            Add(row, "fail-closed source discovery at frozen line " + row.Line.ToString(CultureInfo.InvariantCulture), actual,
                actual.Contains(Literal(row), StringComparison.Ordinal), sourcePath, "DeNovoSourceFileDiscovery.RequireFiles");
        }
        return cases;
    }

    private sealed class LedgerProofWriter(TextWriter original, TextWriter copy) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) { original.WriteLine(value); copy.WriteLine(value); }
        public override void Write(char value) { original.Write(value); copy.Write(value); }
        public override void Flush() { original.Flush(); copy.Flush(); }
    }

    private static (string Id, string Path, string Symbol)[] DisplacedLedgerHandlers() => new[]
    {
        ("FQ15-523631E1F20E", "FamilyQuant/AnalyticsControl.xaml.cs", "ExportClicked / ResultsExportKind.Peptides"),
        ("FQ15-9AB13C0AE2A4", "FamilyQuant/AnalyticsControl.xaml.cs", "ExportClicked / ResultsExportKind.Figures"),
        ("FQ15-D56E685BBF02", "FamilyQuant/AnalyticsControl.xaml.cs", "ExportClicked / ResultsExportKind.EvidencePack"),
        ("FQ15-DF754B3A82A5", "FamilyQuant/AnalyticsControl.xaml.cs", "ChartChoiceChanged"),
        ("FQ15-A338D170136B", "FamilyQuant/AnalyticsControl.xaml.cs", "ChartChoiceChanged"),
        ("FQ15-C213AEB5CD1C", "FamilyQuant/AnalyticsControl.xaml.cs", "ChartChoiceChanged"),
        ("FQ15-8DE16886CEFC", "FamilyQuant/QuantifyControl.xaml.cs", "BrowseRaw"),
        ("FQ15-172FF81C3D7A", "FamilyQuant/QuantifyControl.xaml.cs", "BrowseSepro"),
        ("FQ15-28762255F78D", "FamilyQuant/QuantifyControl.xaml.cs", "BrowseDeNovo"),
        ("FQ15-804F299CF330", "FamilyQuant/ComputeXicViewModel.cs", "ComputeCommand / ComputeAsync"),
        ("FQ15-68E49CE81764", "FamilyQuant/QuantifyControl.xaml.cs", "ViewSpectrum"),
        ("FQ15-E6FC69103914", "FamilyQuant/ComputeXicWindow.xaml.cs", "native Closing handler"),
        ("FQ15-5CC29C895FD3", "FamilyQuant.Core/HomologyCsv.cs", "HomologyCsv.Write / ResultsExportKind.Homology")
    };
}
