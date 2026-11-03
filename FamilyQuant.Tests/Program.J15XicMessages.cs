using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string XicMessagesStep = "J15-xic-runtime";
    private static readonly string[] XicMessageIds =
    [
        "7797D0EC1F17", "B85B65F42CE1", "2E287945BA54", "77672F14EDEE", "BD4FFA89F875",
        "D773CB4DC14D", "4FE5D15AF045", "868453DF20C8", "EDD5C6112828", "CFB0C496B272",
        "EC1ED07E39CC", "D1A591E52DA0", "C28A0B762E27", "D636B492FD40",
        "AEC20A408BF5", "DA304C6BDF34", "071F2EFBA65F", "3DED4AAA299C", "D9FF9427E1F0",
        "DC05EFDCB408", "467B1BA865EB",
    ];

    [TestCommandTable]
    private static IEnumerable<TestCommand> XicMessageCommands() =>
    [
        new("--ledger-xic-messages-runtime", 10145, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-xic-messages-runtime", (args, _) => XicMessagesCommand(args)),
    ];

    private sealed record XicMessageCase(string Id, string Operation, string Expected, string Actual, bool Passed);
    private sealed class XicMessageInputs
    {
        public Dictionary<string, string> ScientificInputFingerprints { get; set; } = new();
    }

    private static int XicMessagesCommand(string[] args)
    {
        if (args.Length is not (5 or 6) || args.Length == 6 && args[5] != "--negative-control")
            throw new ArgumentException("Supply frozen source root, fresh output, date timestamp, stored project and optional --negative-control.");
        if (!DateTimeOffset.TryParse(args[3], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
            throw new ArgumentException("Use a timestamp obtained from date '+%F %T %z'.");
        string root = FeatureLedgerEvidence.FindRoot()!, frozen = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        string project = Path.GetFullPath(args[4]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Native proof output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var observations = XicMessageIds.Select(id => inventory.Observations.Single(row => row.Id == "FQ15-" + id)).ToArray();
        foreach (string source in observations.Select(row => row.Source).Distinct(StringComparer.Ordinal))
            if (FeatureLedgerEvidence.Hash(Path.Combine(frozen, source)) != inventory.Files.Single(file => file.Path == source).Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + source);

        List<XicMessageCase> rawCases;
        TextWriter original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        string Canonical(string value) => value.Replace(output, "<owned-output>", StringComparison.OrdinalIgnoreCase)
            .Replace(output.Replace('\\', '/'), "<owned-output>", StringComparison.OrdinalIgnoreCase);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            rawCases = XicMessagesCases(output, frozen, project, observations, args.Length == 6);
            if (rawCases.Count != observations.Length || rawCases.Select(row => row.Id).Distinct().Count() != observations.Length)
                throw new InvalidDataException("Native cases must cover the exact assigned 21-row cohort once.");
            foreach (var fact in rawCases)
                Check(XicMessagesStep + " " + fact.Id + ": " + fact.Operation, fact.Passed, Canonical(fact.Actual));
            foreach (var group in observations.GroupBy(row => row.Kind).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = XicMessagesStep + " kind " + group.Key + ": every assigned native branch and complete message passed";
                Check(assertion, group.All(row => rawCases.Single(fact => fact.Id == row.Id).Passed));
                assertions[group.Key] = assertion;
            }
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        finally { Console.SetOut(original); }

        string log = Path.Combine(output, "proofs.log");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "native-cases-raw.json"), rawCases);
        var cases = rawCases.Select(fact => fact with { Expected = Canonical(fact.Expected), Actual = Canonical(fact.Actual) }).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "native-cases-canonical.json"), cases);
        string[] implementations =
        [
            "FamilyQuant.Core/DeNovo/Ms1EnvelopeDetector.cs", "FamilyQuant.Core/DeNovo/DeNovoXicExtractor.cs",
            "FamilyQuant.Core/DeNovo/SpectrumComparisonTable.cs", "FamilyQuant.Core/FileFingerprint.cs",
            "FamilyQuant.Core/Quantitation/FullXicModels.cs", "FamilyQuant.Core/Quantitation/FullXicStoragePaths.cs",
            "FamilyQuant.Tests/Program.J15XicMessages.cs",
        ];
        void Retain(string source, string relative)
        {
            string target = Path.Combine(output, "executed", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, false);
        }
        foreach (string source in implementations.Concat(new[] { "FamilyQuant.Tests/FeatureLedgerEvidence.cs",
            "FamilyQuant.Tests/FeatureLedgerEvidence.Reporting.cs", "FamilyQuant.Tests/FeatureLedger.cs",
            "FamilyQuant.Tests/Program.LedgerPreservation.cs", "FamilyQuant.Tests/TestCommandRegistry.cs" }))
            Retain(Path.Combine(root, source), source);
        foreach (string source in observations.Select(row => row.Source).Distinct(StringComparer.Ordinal))
            Retain(Path.Combine(frozen, source), Path.Combine("frozen", source));
        Retain(Path.Combine(root, "docs/feature-ledger-scan.json"), "docs/feature-ledger-scan.json");
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)
            && a.Location.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.Location, StringComparer.Ordinal))
            Retain(assembly.Location, Path.Combine("binaries", Path.GetFileName(assembly.Location)));
        var artifacts = Directory.GetFiles(output, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList();
        var mappings = implementations.Select(path => new FeatureLedgerEvidence.Implementation(path,
            "Ms1EnvelopeDetector.DetectRun / FullXicComputeResult CSV outcome / FullXicStoragePaths.Extraction native execution",
            FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList();
        string Receipt(string kind) => "native-" + kind.Replace('/', '-').Replace(' ', '-');
        var rows = observations.Select(row =>
        {
            var fact = cases.Single(fact => fact.Id == row.Id);
            return new FeatureLedgerEvidence.Row(row.Id, mappings, new(),
                "Native execution of this branch; synthetic cache/CSV fixtures and a read-only uncached detector pass, not scientific quantitation validation.",
                fact.Passed ? Receipt(row.Kind) : null, fact.Passed ? row.Source + ":" + row.Line + " " + row.Detail
                    + "; " + fact.Operation + "; expected " + fact.Expected + "; actual " + fact.Actual : null);
        }).ToList();
        var receipts = assertions.Select(pair => new FeatureLedgerEvidence.KindReceipt(Receipt(pair.Key), pair.Key,
            new() { new("native", "test", nameof(XicMessagesCommand), pair.Value, log, "") })).ToList();
        var git = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        git.ArgumentList.Add("rev-parse"); git.ArgumentList.Add("HEAD");
        using var process = Process.Start(git)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record source commit.");
        var run = new FeatureLedgerEvidence.Run("native", Environment.CommandLine, args[3], commit, artifacts, root);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, XicMessagesStep + ".json"),
            new FeatureLedgerEvidence.Register(1, XicMessagesStep, FeatureLedger.ReferenceCommit, new() { run }, rows, receipts));
        return failed == 0 ? 0 : 1;
    }

    private static List<XicMessageCase> XicMessagesCases(string output, string frozen, string project,
        FeatureLedger.Observation[] observations, bool negative)
    {
        var cases = new List<XicMessageCase>();
        const string detectorSource = "FamilyQuant/DeNovo/Ms1EnvelopeDetector.cs", fullSource = "FamilyQuant/FullXicComputer.cs";
        string At(string source, int line, int occurrence = 0) => JsonSerializer.Deserialize<string>(
            observations.Where(row => row.Source == source && row.Line == line).ElementAt(occurrence).Detail)!;
        void Add(string source, int first, int last, string operation, string expected, string actual, bool passedCase)
        {
            foreach (var row in observations.Where(row => row.Source == source && row.Line >= first && row.Line <= last))
                cases.Add(new(row.Id, operation, expected, actual, passedCase));
        }
        Exception? Capture(Action work) { try { work(); return null; } catch (Exception error) { return error; } }
        string previousDirectory = Environment.CurrentDirectory;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            Environment.CurrentDirectory = output; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Exception? missing = Capture(() => Ms1EnvelopeDetector.DetectRun(""));
            string missingExpected = new ArgumentException(At(detectorSource, 197), "rawPath").Message;
            Add(detectorSource, 197, 197, "empty acquisition path refuses before cache/load", missingExpected,
                missing?.Message ?? "NO REFUSAL", missing is ArgumentException && missing.Message == missingExpected);

            File.WriteAllText("synthetic.raw", "Owned synthetic cache marker; never parsed as acquisition.", new UTF8Encoding(false));
            string syntheticFingerprint = FileFingerprint.Sha256("synthetic.raw");
            var cacheFixture = new Ms1EnvelopeDetectionResult { RunName = "synthetic", DetectedEnvelopeCount = negative ? 1235 : 1234,
                DetectedPeptideLikeArea = 12340, DetectedAreaMultiplyCharged = 12000, DetectedAreaSinglyCharged = 340,
                DetectedCountMultiplyCharged = 1200, DetectedCountSinglyCharged = negative ? 35 : 34, Ms1ScanCount = 5678 };
            cacheFixture.DetectedAreaByCharge[1] = 340; cacheFixture.DetectedAreaByCharge[2] = 12000;
            cacheFixture.DetectedCountByCharge[1] = cacheFixture.DetectedCountSinglyCharged; cacheFixture.DetectedCountByCharge[2] = 1200;
            typeof(Ms1EnvelopeDetector).GetMethod("SaveCache", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { "synthetic-detector.json", cacheFixture, syntheticFingerprint });
            var cacheProgress = new List<string>();
            var cached = Ms1EnvelopeDetector.DetectRun("synthetic.raw", "synthetic-detector.json", cacheProgress.Add);
            string cacheExpected = "synthetic" + At(detectorSource, 207) + 1234.ToString(At(detectorSource, 208), CultureInfo.InvariantCulture)
                + At(detectorSource, 208, 1);
            Add(detectorSource, 207, 208, "validated detector cache emits complete invariant grouped-count status", cacheExpected,
                string.Join("\n", cacheProgress), cacheProgress.SequenceEqual(new[] { cacheExpected }) && cached.DetectedEnvelopeCount == 1234);

            XicMessageInputs inputs;
            using (var archive = ZipFile.OpenRead(project))
            using (var entry = archive.GetEntry(ProjectStore.ManifestEntryName)!.Open())
                inputs = JsonSerializer.Deserialize<XicMessageInputs>(entry) ?? throw new InvalidDataException("Missing stored input fingerprints.");
            var source = inputs.ScientificInputFingerprints.Where(pair => pair.Key.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
            string raw = Path.GetFullPath(source.Key), rawHash = FileFingerprint.Sha256(raw);
            if (!string.Equals(rawHash, source.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Read-only acquisition differs from its stored fingerprint; detection was not launched.");
            var sourceInfo = new FileInfo(raw); long rawLength = sourceInfo.Length, rawModified = sourceInfo.LastWriteTimeUtc.Ticks;
            var detectionProgress = new List<string>();
            Console.WriteLine("Executing one uncached read-only MS1 detector pass; all output is owned by this proof.");
            var detected = Ms1EnvelopeDetector.DetectRun(raw, "uncached-detector.json", detectionProgress.Add, rawHash);
            string run = DeNovoReconciler.NormalizeRunName(raw);
            string loading = run + At(detectorSource, 212);
            string FrozenLabel(string name)
            {
                string pattern = @"\b" + name + "\\s*=\\s*(\"(?:\\\\.|[^\"])*\")\\s*;";
                var match = Regex.Match(File.ReadAllText(Path.Combine(frozen, detectorSource)), pattern);
                return match.Success ? JsonSerializer.Deserialize<string>(match.Groups[1].Value)!
                    : throw new InvalidDataException("Frozen detector label is missing: " + name);
            }
            string completion = run + At(detectorSource, 216)
                + detected.DetectedEnvelopeCount.ToString(At(detectorSource, 217), CultureInfo.InvariantCulture) + At(detectorSource, 218)
                + detected.Ms1ScanCount.ToString(At(detectorSource, 219), CultureInfo.InvariantCulture) + At(detectorSource, 219, 1)
                + detected.DetectedCountMultiplyCharged.ToString(At(detectorSource, 220), CultureInfo.InvariantCulture) + " " + FrozenLabel("StrongEvidenceLabel")
                + At(detectorSource, 221) + detected.DetectedCountSinglyCharged.ToString(At(detectorSource, 222), CultureInfo.InvariantCulture)
                + " " + FrozenLabel("WeakEvidenceLabel") + At(detectorSource, 223);
            bool unchanged = FileFingerprint.Sha256(raw) == rawHash && new FileInfo(raw).Length == rawLength
                && new FileInfo(raw).LastWriteTimeUtc.Ticks == rawModified;
            Add(detectorSource, 212, 212, "uncached native loader emits the complete loading status", loading,
                detectionProgress.FirstOrDefault() ?? "NO PROGRESS", detectionProgress.FirstOrDefault() == loading && unchanged);
            Add(detectorSource, 216, 223, "uncached detector completion retains counts, invariant formats and both evidence labels", completion,
                string.Join("\n", detectionProgress.Skip(1)), detectionProgress.SequenceEqual(new[] { loading, completion }) && unchanged);
            FeatureLedgerEvidence.WriteJson("readonly-input.json", new { Project = project, Raw = raw, RawSha256 = rawHash,
                Length = rawLength, ModifiedUtcTicks = rawModified, Unchanged = unchanged, FreshDetection = true, FreshQuantitation = false });
            FeatureLedgerEvidence.WriteJson("detector-progress.json", new { Cache = cacheProgress, Uncached = detectionProgress });
            GC.Collect();

            var result = new FullXicComputeResult { Rows = new() { new() { Run = "synthetic", Side = "identified", Scan = 7,
                Sequence = "ACDEFGHIK", Charge = 2, PrecursorMz = 500.25, EnvelopeSum = 18, IsotopeAreas = new[] { 10d, 5d, 2d, 1d, 0d, 0d } } } };
            result.SpectrumComparisonRows = SpectrumComparisonTable.Build(Array.Empty<PeaksDeNovoRow>(), Array.Empty<PeaksDeNovoRow>(),
                new[] { new PatternLabPsm { FileName = "synthetic.raw", ScanNumber = 7, ChargeState = 2, PrecursorMz = 500.25, Sequence = "ACDEFGHIK" } }, result.Rows);
            string originalRows = JsonSerializer.Serialize(result.Rows), originalTable = JsonSerializer.Serialize(result.SpectrumComparisonRows);
            bool success = result.TryExportSpectrumComparisonCsvOutsideRaw("spectrum.csv", "acquisitions");
            string successExpected = At(fullSource, 144) + Path.GetFullPath("spectrum.csv");
            Add(fullSource, 144, 144, "native CSV success records the actual written path and full table", successExpected,
                result.Notes.LastOrDefault() ?? "NO STATUS", success && result.Notes.Last() == successExpected
                && File.Exists(result.SpectrumComparisonCsvPath) && result.SpectrumComparisonRows.Count == 1);
            File.WriteAllText("blocked", "Owned file, so its child cannot be a CSV destination.", new UTF8Encoding(false));
            string refusedPath = Path.Combine("blocked", "table.csv");
            Exception? csvError = Capture(() => SpectrumComparisonTable.SaveCsv(refusedPath, result.SpectrumComparisonRows));
            bool refused = !result.TryExportSpectrumComparisonCsvOutsideRaw(refusedPath, "acquisitions");
            string refusalExpected = At(fullSource, 154) + refusedPath + At(fullSource, 155)
                + csvError!.GetType().Name + At(fullSource, 155, 1) + csvError.Message + At(fullSource, 156) + At(fullSource, 157);
            Add(fullSource, 154, 157, "native CSV refusal retains the complete warning, retry instruction and unchanged result/table", refusalExpected,
                result.SpectrumComparisonCsvError, refused && result.SpectrumComparisonCsvError == refusalExpected
                && result.Notes.Last() == refusalExpected && result.SpectrumComparisonCsvPath.Length == 0
                && JsonSerializer.Serialize(result.Rows) == originalRows && JsonSerializer.Serialize(result.SpectrumComparisonRows) == originalTable);
            var storage = new FullXicStoragePaths("acquisitions", "cache");
            string emptyName = Path.GetFileName(storage.Extraction("identified", "").WritePath);
            string expectedName = "fullxic_identified_" + At(fullSource, 994) + ".json";
            Add(fullSource, 994, 994, "public cache-path construction executes the empty-run filename fallback", expectedName, emptyName, emptyName == expectedName);
        }
        finally { Environment.CurrentDirectory = previousDirectory; CultureInfo.CurrentCulture = previousCulture; }
        return cases;
    }
}
