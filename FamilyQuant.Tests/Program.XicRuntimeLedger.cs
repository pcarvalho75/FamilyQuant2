using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class XicReplaySettings
    {
        public int IsotopeCount { get; set; }
        public int MinPointsForQuant { get; set; }
        public double Ppm { get; set; }
        public int Ms1Tolerance { get; set; }
        public Dictionary<string, string> ScientificInputFingerprints { get; set; } = new();
        public List<DeNovoInputManifest> DeNovoInputs { get; set; } = new();
        public List<DeNovoSourceScoreDistribution> DeNovoSourceScoreDistributions { get; set; } = new();
    }

    private static int XicRuntimeLedgerCommand(string[] args)
    {
        if (args.Length is not (6 or 7) || args.Length == 7 && args[6] != "--negative-control")
            throw new ArgumentException("Supply frozen source root, fresh output, date, stored project, lane-owned validated cache folder and optional --negative-control.");
        string root = FindLedgerRoot()!, project = Path.GetFullPath(args[4]), cache = Path.GetFullPath(args[5]);
        string[] paths = new[] { "FamilyQuant.Core/FullXicComputer.cs", "FamilyQuant.Core/FamilyEnvelopeQuant.cs" }
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/Quantitation"), "*.cs")
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/DeNovo"), "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))).Distinct(StringComparer.Ordinal).ToArray();
        string[] common = args.Take(4).Concat(args.Length == 7 ? new[] { args[6] } : Array.Empty<string>()).ToArray();
        return RunCoreRuntimeLedger(common, "Core-X5-runtime", new[] { "FamilyQuant/FullXicComputer.cs" }, paths,
            (output, observations, negative) => XicRuntimeLedgerCases(output, observations, negative, project, cache));
    }

    private static List<CoreRuntimeCase> XicRuntimeLedgerCases(string output, FeatureLedger.Observation[] observations,
        bool negative, string project, string cache)
    {
        var cases = new List<CoreRuntimeCase>();
        string At(int line, int occurrence = 0) => JsonSerializer.Deserialize<string>(
            observations.Where(row => row.Line == line).ElementAt(occurrence).Detail)!;
        void Add(int first, int last, string operation, string expected, string actual, bool matches)
        {
            foreach (var row in observations.Where(row => row.Line >= first && row.Line <= last))
                cases.Add(new(row.Id, operation, expected, actual, matches));
        }
        Exception? Capture(Action work) { try { work(); return null; } catch (Exception ex) { return ex; } }
        Type Stage(string name) => typeof(FullXicComputer).Assembly.GetType("FamilyQuant." + name, true)!;
        object New(string name) => Activator.CreateInstance(Stage(name), nonPublic: true)!;
        T Get<T>(object value, string property) => (T)value.GetType().GetProperty(property,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;
        void Set(object value, string property, object? field) => value.GetType().GetProperty(property,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(value, field);
        object? Call(string owner, string method, params object?[] arguments)
        {
            try { return Stage(owner).GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, arguments); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
        object Input(string rawDirectory, string rawPath, string run, List<PatternLabPsm> psms, object? sources = null)
        {
            var value = New("FullXicInput");
            Set(value, "RawDirectory", rawDirectory); Set(value, "Sepr2Path", "unused.sepr2");
            Set(value, "RawByRun", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [run] = rawPath });
            Set(value, "Psms", psms); Set(value, "PsmsByRun", new Dictionary<string, List<PatternLabPsm>>(StringComparer.OrdinalIgnoreCase) { [run] = psms });
            Set(value, "Selections", Array.Empty<DeNovoSourceSelection>()); Set(value, "Sources", sources ?? New("FullXicSourceInput"));
            Set(value, "Runs", new[] { run }); Set(value, "Match", FullXicRunMatch.Build(new[] { run }, new[] { run }, Array.Empty<string>()));
            return value;
        }
        FullXicComputeResult Complete(FullXicComputeResult result, FullXicOptions options) => (FullXicComputeResult)Call(
            "FullXicCompletionStage", "Complete", Input(output, "unused.raw", "fixture", new()), options, result, Array.Empty<RawScanPrecursorMetadata>())!;
        // The replay needs settings, not the stored quantitation or spectrum evidence populations.
        XicReplaySettings stored;
        using (var archive = ZipFile.OpenRead(project))
        using (var manifest = archive.GetEntry(ProjectStore.ManifestEntryName)!.Open())
            stored = JsonSerializer.Deserialize<XicReplaySettings>(manifest) ?? throw new InvalidDataException("Missing replay settings.");
        string package = stored.ScientificInputFingerprints.Keys.Single(path => path.EndsWith(".sepr2", StringComparison.OrdinalIgnoreCase));
        var allPsms = SeproPsmAdapter.Load(package);
        string rawPath = stored.ScientificInputFingerprints.Keys.Where(path => path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal).First();
        string run = DeNovoReconciler.NormalizeRunName(rawPath);
        var selections = stored.DeNovoInputs.GroupBy(value => value.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).OrderBy(value => value.SourcePriority)
            .Select(value => new DeNovoSourceSelection(DeNovoSource.FromId(value.SourceId)!, value.MinimumConfidence, value.SourcePriority)).ToList();
        var distribution = stored.DeNovoSourceScoreDistributions.First();
        var options = new FullXicOptions { IsotopeCount = stored.IsotopeCount, MinPointsForQuant = stored.MinPointsForQuant,
            Ppm = stored.Ppm, Ms1Tolerance = stored.Ms1Tolerance, ExportSpectrumComparisonCsv = false,
            DeNovoSelections = selections, DeNovoCriteria = new() { MinLength = distribution.MinLength,
                StretchLength = distribution.StretchLength, StretchFloor = distribution.StretchFloor } };
        string sourceDirectory = Path.GetDirectoryName(stored.DeNovoInputs[0].FilePath)!;
        while (stored.DeNovoInputs.Any(value => !Path.GetFullPath(value.FilePath).StartsWith(sourceDirectory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))) sourceDirectory = Path.GetDirectoryName(sourceDirectory)!;
        string oldDirectory = Environment.CurrentDirectory;
        CultureInfo oldCulture = CultureInfo.CurrentCulture;
        try
        {
            Environment.CurrentDirectory = output; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            int callbacks = 0;
            Exception? missingRaw = Capture(() => FullXicComputer.ComputeFullXic("missing-raw", "missing.sepr2", null, options,
                (_, _) => { callbacks++; return "unexpected.json"; }));
            Add(379, 379, "public missing-RAW refusal before extraction/cache callbacks", At(379) + "missing-raw", missingRaw?.Message ?? "NO REFUSAL",
                missingRaw is DirectoryNotFoundException && missingRaw.Message == At(379) + "missing-raw" && callbacks == 0);
            Directory.CreateDirectory("glob");
            Exception? missingPackage = Capture(() => FullXicComputer.ComputeFullXic("glob", "missing.sepr2", null, options,
                (_, _) => { callbacks++; return "unexpected.json"; }));
            Add(381, 381, "public missing-package refusal before extraction/cache callbacks", At(381) + "missing.sepr2", missingPackage?.Message ?? "NO REFUSAL",
                missingPackage is FileNotFoundException fileError && fileError.FileName == "missing.sepr2"
                && fileError.Message == At(381) + "missing.sepr2" && callbacks == 0);
            File.WriteAllText(Path.Combine("glob", run + (negative ? ".txt" : ".raw")), "Synthetic marker, never opened as RAW.");
            File.WriteAllText(Path.Combine("glob", run + ".raw.bak"), "Must not be discovered.");
            Directory.CreateDirectory(Path.Combine("glob", "nested"));
            File.WriteAllText(Path.Combine("glob", "nested", "nested.raw"), "Top-level discovery only.");
            var progress = new List<string>(); object? prepared = null;
            Exception? preparation = Capture(() => prepared = Call("FullXicInputStage", "Prepare", "glob", package, null, options,
                (Action<string>)progress.Add, null));
            bool correctGlob = prepared != null && Get<Dictionary<string, string>>(prepared, "RawByRun").Keys.SequenceEqual(new[] { run })
                && Get<IReadOnlyList<string>>(prepared, "Runs").SequenceEqual(new[] { run });
            Add(391, 391, "top-level RAW glob includes .raw and rejects .raw.bak/nested files", At(391) + ": one matched run", correctGlob ? "*.raw: one matched run" : preparation?.GetType().Name ?? "WRONG RUN SET", correctGlob);
            string reading = At(398, 0) + Path.GetFileName(package) + At(398, 1);
            Add(398, 398, "identification import progress before matching", reading, string.Join(";", progress), progress.Contains(reading));

            string defaultPath = (string)Call("FullXicRunStage", "CachePath", null, "identified", "bad:run?", "cache-root")!;
            string expectedDefault = Path.Combine("cache-root", FullXicComputer.CacheFolderName, At(986, 0) + "identified" + At(986, 1) + "bad_run_" + At(986, 2));
            string? callbackSide = null, callbackRun = null;
            Func<string, string, string> overridePath = (side, name) => { callbackSide = side; callbackRun = name; return "explicit-cache.json"; };
            string overridden = (string)Call("FullXicRunStage", "CachePath", overridePath, "denovo", "fixture", "cache-root")!;
            Add(986, 986, "default cache filename and explicit override branch", expectedDefault + ";explicit-cache.json",
                defaultPath + ";" + overridden, defaultPath == expectedDefault && overridden == "explicit-cache.json" && callbackSide == "denovo" && callbackRun == "fixture");

            Directory.CreateDirectory("sources"); Directory.CreateDirectory(Path.Combine("sources", "nested"));
            const string header = "Scan,Peptide,{0},Length,m/z,z,RT,Area,local confidence (%)\n";
            File.WriteAllText(Path.Combine("sources", "fixture.raw.denovo.csv"), string.Format(CultureInfo.InvariantCulture, header, "ALC (%)")
                + "101,ACDEFGHIK,90,9,500.25,2,12.5,1000,99 99 99 99 99 99 99 99 99\n");
            File.WriteAllText(Path.Combine("sources", "fixture.raw.deepnovo.csv"), string.Format(CultureInfo.InvariantCulture, header, "SCORE (%)")
                + "202,VVVVVVVVK,85,9,600.25,2,13.5,2000,99 99 99 99 99 99 99 99 99\n");
            File.WriteAllText(Path.Combine("sources", "nested", "db.psms.csv"),
                "Peptide,-10LgP,Mass,m/z,raw m/z,z,Scan,Source File\nACDEFGHIK,60,998.48545,500.25,500.25,2,303,fixture.raw\n");
            File.WriteAllText(Path.Combine("sources", "ignored-db.psms.csv"), "Not the exact database export filename.");
            var syntheticSelections = new[] { new DeNovoSourceSelection(DeNovoSource.DeNovoAll, 80.125, 1), new DeNovoSourceSelection(DeNovoSource.DeepNovo, 77.375, 0) };
            var criteria = new DeNovoFilterCriteria { MinLength = 9, StretchLength = 4, StretchFloor = 60 };
            var sourceProgress = new List<string>();
            object imported = Call("FullXicSourceStage", "Read", "sources", new List<PatternLabPsm>(), criteria, syntheticSelections, (Action<string>)sourceProgress.Add)!;
            string[] expectedProgress = syntheticSelections.Select(selection => At(774, 0) + selection.Source.DisplayName + At(774, 1) + selection.Source.FileGlob
                + At(775, 0) + "sources" + At(775, 1) + selection.Source.ConfidenceColumn + At(891)
                + selection.MinimumConfidence.ToString(At(892), CultureInfo.InvariantCulture) + At(775, 2)).ToArray();
            Add(774, 775, "source-aware import progress uses each source glob and its own cutoff", string.Join(";", expectedProgress), string.Join(";", sourceProgress), sourceProgress.SequenceEqual(expectedProgress));
            var concordance = Get<IdentificationConcordanceSummary>(imported, "IdentificationConcordance");
            Add(854, 854, "exact recursive db.psms.csv discovery ignores prefixed filename", At(854) + ": one database row", "db.psms.csv: "
                + concordance.PeaksDatabasePsmRows.ToString(CultureInfo.InvariantCulture) + " database row", concordance.PeaksDatabasePsmRows == 1);
            string scope = string.Join(At(887), syntheticSelections.OrderBy(selection => selection.Priority)
                .Select(selection => selection.Source.DisplayName + At(888, 0) + selection.Source.ConfidenceColumn + At(888, 1)));
            string filter = At(889, 0) + string.Join(At(889, 1), syntheticSelections.OrderBy(selection => selection.Priority)
                .Select(selection => selection.Source.DisplayName + " " + selection.Source.ConfidenceColumn + At(891)
                    + selection.MinimumConfidence.ToString(At(892), CultureInfo.InvariantCulture)))
                + At(893) + "9" + At(894) + "4" + At(895) + "60" + At(896);
            var overlap = Get<DeNovoOverlapSummary>(imported, "Overlap");
            Add(887, 896, "priority-ordered source scope and fractional-cutoff/length/local-confidence labels", scope + ";" + filter,
                overlap.SourceScopeLabel + ";" + overlap.SourceFilterDescription, overlap.SourceScopeLabel == scope && overlap.SourceFilterDescription == filter
                && concordance.DeNovoSourceScopeLabel == scope && concordance.DeNovoFilterDescription == filter && !overlap.IsStrictDeNovoOnlyScope
                && Get<List<PeaksDeNovoRow>>(imported, "AcceptedSourceRows").Count == 2);
            string sourceFile = stored.DeNovoInputs.First().FilePath;
            var wrongRows = new Dictionary<string, List<PeaksDeNovoRow>>(StringComparer.OrdinalIgnoreCase)
                { [syntheticSelections[0].Source.Id] = new() { new() { SourceId = "wrong-synthetic-source", InputFilePath = sourceFile } } };
            Exception? wrongSource = Capture(() => Call("FullXicSourceStage", "RebuildObservedInputManifests", New("FullXicSourceInput"), wrongRows, syntheticSelections, criteria));
            string mismatch = At(947) + Path.GetFullPath(sourceFile) + At(948, 0) + syntheticSelections[0].Source.Id + At(948, 1);
            Add(947, 948, "mixed source identity refuses before rebuilding provenance", mismatch, wrongSource?.Message ?? "NO REFUSAL", wrongSource is InvalidDataException && wrongSource.Message == mismatch);
            var manifestKey = XicManifestKeyCase(observations.Single(row => row.Line == 973), sourceFile, negative);
            if (manifestKey != null) cases.Add(manifestKey);

            var baseRows = new List<QuantRow> { new() { Side = "identified", Run = "fixture", Scan = 101, Charge = 2, PrecursorMz = 500.25,
                Sequence = "ACDEFGHIK", IsotopeAreas = new[] { 1d, 2d }, EnvelopeSum = 3 } };
            var curated = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "fixture-a", Sequence = "ACDEFGHIK", UserID = 41 },
                new() { SequenceIdentifier = "fixture-b", Sequence = "ACDEFGHIK", UserID = 42 } } };
            var familyOptions = new FullXicOptions { ExportSpectrumComparisonCsv = false, FamilyCuration = curated };
            var familyResult = Complete(new FullXicComputeResult { Rows = baseRows.ToList() }, familyOptions);
            double identity = FamilyEnvelopeQuant.BuildAttributionFromCuration(baseRows, curated).IdentityThreshold;
            string familyNote = At(617) + identity.ToString(At(618), CultureInfo.InvariantCulture) + At(619);
            Add(617, 619, "completion credits all matching synthetic User families and reports the identity", familyNote, string.Join(";", familyResult.Notes),
                familyResult.Notes.Contains(familyNote) && familyResult.Manifest.FamilyMapping != null
                && FamilyEnvelopeQuant.BuildAttributionFromCuration(baseRows, curated).IdentifiedBySequence.Values
                    .Any(families => families.SequenceEqual(new[] { 41, 42 })));
            var invalidCuration = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "", Sequence = "ACDEFGHIK", UserID = 41 } } };
            Exception? mappingError = Capture(() => FamilyEnvelopeQuant.BuildAttributionFromCuration(baseRows, invalidCuration));
            string beforeRows = JsonSerializer.Serialize(baseRows), beforeCuration = invalidCuration.ToJsonText();
            var failedMapping = Complete(new FullXicComputeResult { Rows = baseRows.ToList() },
                new FullXicOptions { ExportSpectrumComparisonCsv = false, FamilyCuration = invalidCuration });
            string mappingWarning = At(623) + mappingError!.GetType().Name + At(624, 0) + mappingError.Message + At(624, 1) + At(625);
            Add(623, 625, "mapping failure warns while preserving quantitation and curation", mappingWarning, string.Join(";", failedMapping.Notes),
                failedMapping.Notes.Contains(mappingWarning) && JsonSerializer.Serialize(failedMapping.Rows) == beforeRows
                && invalidCuration.ToJsonText() == beforeCuration && failedMapping.Manifest.FamilyMapping == null);
            const string alphabet = "ACDEFGHKNQRTVWY";
            string Sequence(int number) => "AK" + alphabet[number % alphabet.Length] + alphabet[number / alphabet.Length % alphabet.Length]
                + alphabet[number / alphabet.Length / alphabet.Length] + "PEPK";
            var underRows = Enumerable.Range(0, 1024).Select(index => new QuantRow { Side = "identified", Run = "fixture", Scan = index + 1,
                Charge = 2, PrecursorMz = 500.25, Sequence = Sequence(index), IsotopeAreas = new[] { 1d, 2d }, EnvelopeSum = 3,
                UnderSampledArea = 0.125, UnderSampledIsEstimate = true }).ToList();
            var underResult = new FullXicComputeResult { Rows = underRows }; underResult.Manifest.PerRunTic["fixture"] = 10000;
            Complete(underResult, new FullXicOptions { ExportSpectrumComparisonCsv = false, MinPointsForQuant = 3 });
            double underArea = underRows.Count * 0.125;
            string underNote = "fixture" + At(659) + "3" + At(661) + underRows.Count.ToString(At(663, 0), CultureInfo.InvariantCulture)
                + At(663, 1) + underArea.ToString(At(664), CultureInfo.InvariantCulture) + At(666, 0)
                + (underArea / 10000).ToString(At(666, 1), CultureInfo.InvariantCulture) + At(666, 2) + At(668);
            Add(659, 668, "completion reports distinct under-sampled peptides, invariant counts/area/share and estimate qualifier", underNote,
                string.Join(";", underResult.Notes), underResult.Notes.Contains(underNote)
                && underResult.Manifest.DerivedFractions.Single(value => !value.Grouped).IdentifiedUnderSampledPeptides == underRows.Count);

            // A synthetic invalid input exercises the warning; later TIC refusal remains a failure.
            string fixedInput = Path.Combine(Path.GetDirectoryName(output)!, "xic-fixed-input"); Directory.CreateDirectory(fixedInput);
            string invalidRaw = Path.Combine(fixedInput, "invalid.raw");
            if (!File.Exists(invalidRaw)) File.WriteAllText(invalidRaw, "Synthetic invalid acquisition, never a scientific measurement.", new UTF8Encoding(false));
            Exception? metadataError = Capture(() => RawScanChargeReader.ReadMetadata(invalidRaw, new[] { 101 }));
            var invalidResult = new FullXicComputeResult();
            Exception? invalidExtraction = Capture(() => Call("FullXicRunStage", "Extract", Input(fixedInput, invalidRaw, "invalid", new()),
                "invalid", options, invalidResult, new List<RawScanPrecursorMetadata>(), null, null, null));
            string metadataWarning = "invalid" + At(487) + metadataError!.GetType().Name + At(488) + metadataError.Message + At(489);
            var fallback = SpectrumComparisonTable.Build(Array.Empty<PeaksDeNovoRow>(), Array.Empty<PeaksDeNovoRow>(),
                new[] { new PatternLabPsm { FileName = "invalid.raw", ScanNumber = 101, ChargeState = 2, PrecursorMz = 500.25, Sequence = "ACDEFGHIK" } },
                Array.Empty<QuantRow>(), rawPrecursors: Array.Empty<RawScanPrecursorMetadata>()).Single();
            Add(487, 489, "metadata failure warning plus explicit source-only spectrum fallback; invalid acquisition stays refused", metadataWarning,
                string.Join(";", invalidResult.Notes), invalidExtraction != null && invalidResult.Notes.Contains(metadataWarning)
                && !fallback.RawChargeKnown && !fallback.RawPrecursorMzKnown && fallback.CanonicalCharge == 2 && fallback.CanonicalPrecursorMz == 500.25);

            if (!negative)
            {
                object realSources = Call("FullXicSourceStage", "Read", sourceDirectory, allPsms, options.DeNovoCriteria, selections, null)!;
                var pickedPsms = allPsms.Where(psm => DeNovoReconciler.NormalizeRunName(psm.FileName).Equals(run, StringComparison.OrdinalIgnoreCase)).Take(4096).ToList();
                var byRun = Get<Dictionary<string, List<PeaksDeNovoRow>>>(realSources, "ByRun");
                var pickedDeNovo = byRun[run].Take(4096).ToList(); byRun.Clear(); byRun[run] = pickedDeNovo;
                var auditRows = Get<List<PeaksDeNovoRow>>(realSources, "AllSourceRows"); auditRows.Clear(); auditRows.AddRange(pickedDeNovo);
                var accepted = Get<List<PeaksDeNovoRow>>(realSources, "AcceptedSourceRows"); accepted.Clear(); accepted.AddRange(pickedDeNovo);
                string fingerprint = FileFingerprint.Sha256(rawPath);
                if (fingerprint != stored.ScientificInputFingerprints[rawPath]) throw new InvalidDataException("Readonly RAW fingerprint changed; no extraction replay was launched.");
                string cacheOutput = Path.Combine(output, FullXicComputer.CacheFolderName); Directory.CreateDirectory(cacheOutput);
                foreach (string side in new[] { "identified", "denovo" })
                {
                    string target = Path.Combine(cacheOutput, "fullxic_" + side + "_" + run + ".json");
                    File.Copy(Path.Combine(cache, side + "_" + run + ".json"), target, false);
                    var loaded = DeNovoXicCache.Load(target, run, options.Ppm, options.Ms1Tolerance, options.IsotopeCount, options.MinPointsForQuant, fingerprint);
                    var requests = side == "identified" ? IdentifiedQuantExtractor.BuildRequests(pickedPsms) : DeNovoBatchExtractor.BuildRequests(pickedDeNovo);
                    if (requests.Count == 0 || requests.Any(request => !loaded.TryGet(request.ScanNoMS2, request.PrecursorMz, request.Charge, out _)))
                        throw new InvalidDataException("A bounded replay request was not cached; fresh extraction was not launched.");
                }
                string detector = Path.Combine(cacheOutput, At(565, 0) + run + At(565, 1));
                File.Copy(Path.Combine(cache, "detected_" + run + ".json"), detector, false);
                object?[] detectorArguments = { detector, run, fingerprint, null };
                if (!(bool)typeof(Ms1EnvelopeDetector).GetMethod("TryLoadCache", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, detectorArguments)!)
                    throw new InvalidDataException("Detector cache failed production provenance checks; fresh detection was not launched.");
                var expectedDetection = (Ms1EnvelopeDetectionResult)detectorArguments[3]!;
                object input = Input(output, rawPath, run, pickedPsms, realSources);
                FullXicComputeResult Measure(Func<string, string>? detectedPath, List<string> messages)
                {
                    var result = new FullXicComputeResult();
                    Call("FullXicRunStage", "Extract", input, run, options, result, new List<RawScanPrecursorMetadata>(), null, (Action<string>)messages.Add, detectedPath);
                    return result;
                }
                var messages = new List<string>(); FullXicComputeResult measured = Measure(null, messages);
                string fingerprintProgress = run + At(467);
                Add(467, 467, "bounded native stage fingerprints readonly RAW before cache replay", fingerprintProgress + ";" + fingerprint,
                    string.Join(";", messages.Where(message => message == fingerprintProgress)) + ";" + measured.Manifest.ScientificInputFingerprints[Path.GetFullPath(rawPath)],
                    messages.Contains(fingerprintProgress) && measured.Manifest.ScientificInputFingerprints[Path.GetFullPath(rawPath)] == fingerprint);
                string identifiedProgress = run + At(506) + pickedPsms.Count.ToString(At(507, 0), CultureInfo.InvariantCulture) + At(507, 1);
                Add(501, 511, "identified cache side, formatted input count and stored row side", identifiedProgress + ";identified",
                    string.Join(";", messages.Where(message => message == identifiedProgress)) + ";"
                        + string.Join(',', measured.Rows.Where(row => row.Side == "identified").Select(row => row.Side).Distinct()),
                    At(501) == "identified" && At(511) == "identified" && messages.Contains(identifiedProgress)
                    && measured.Rows.Any(row => row.Side == "identified") && measured.Rows.All(row => row.Side is "identified" or "denovo"));
                int identifiedRows = measured.Rows.Count(row => row.Side == "identified");
                string identifiedNote = run + At(518) + identifiedRows.ToString(At(519, 0), CultureInfo.InvariantCulture) + At(519, 1)
                    + IdentifiedQuantExtractor.BuildRequests(pickedPsms).Count.ToString(At(520, 0), CultureInfo.InvariantCulture) + At(520, 1);
                Add(518, 520, "identified row and unique-precursor status fields from bounded replay", identifiedNote, string.Join(";", measured.Notes),
                    identifiedRows >= 1000 && measured.Notes.Contains(identifiedNote));
                string deNovoProgress = run + At(537) + pickedDeNovo.Count.ToString(At(538, 0), CultureInfo.InvariantCulture) + At(538, 1);
                Add(532, 542, "de novo cache side, formatted kept-input count and stored row side", deNovoProgress + ";denovo",
                    string.Join(";", messages.Where(message => message == deNovoProgress)) + ";"
                        + string.Join(',', measured.Rows.Where(row => row.Side == "denovo").Select(row => row.Side).Distinct()),
                    At(532) == "denovo" && At(542) == "denovo" && messages.Contains(deNovoProgress) && measured.Rows.Any(row => row.Side == "denovo"));
                int deNovoRows = measured.Rows.Count(row => row.Side == "denovo");
                string deNovoNote = run + At(546) + deNovoRows.ToString(At(547, 0), CultureInfo.InvariantCulture) + At(547, 1);
                Add(546, 547, "de novo row-count status from bounded replay", deNovoNote, string.Join(";", measured.Notes), deNovoRows >= 1000 && measured.Notes.Contains(deNovoNote));
                Add(565, 565, "default detector cache filename with validated content fingerprint", Path.GetFileName(detector), Path.GetFileName(detector),
                    measured.Detected.ContainsKey(run) && measured.Detected[run].DetectedEnvelopeCount == expectedDetection.DetectedEnvelopeCount
                    && Directory.GetFiles(cacheOutput, "*.json").Length == 3);
                double tic = measured.Manifest.PerRunTic[run];
                string detectedNote = run + At(572) + At(573) + expectedDetection.DetectedEnvelopeCount.ToString(At(574, 0), CultureInfo.InvariantCulture)
                    + At(574, 1) + expectedDetection.DetectedPeptideLikeArea.ToString(At(575), CultureInfo.InvariantCulture) + At(577, 0)
                    + (expectedDetection.DetectedPeptideLikeArea / tic).ToString(At(577, 1), CultureInfo.InvariantCulture) + At(577, 2);
                Add(572, 577, "detector status keeps independent diagnostic wording, count, E4 area and P2 TIC share", detectedNote,
                    string.Join(";", measured.Notes), tic > 0 && measured.Notes.Contains(detectedNote));
                var failedMessages = new List<string>();
                var failedDetection = Measure(_ => throw new IOException("Synthetic diagnostic path refusal."), failedMessages);
                string diagnosticWarning = run + At(582) + "IOException" + At(583, 0) + "Synthetic diagnostic path refusal." + At(583, 1);
                Add(582, 583, "diagnostic failure warns while retaining identical peptide quantitation", diagnosticWarning, string.Join(";", failedDetection.Notes),
                    failedDetection.Notes.Contains(diagnosticWarning) && failedDetection.Detected.Count == 0
                    && JsonSerializer.Serialize(failedDetection.Rows) == JsonSerializer.Serialize(measured.Rows)
                    && failedDetection.Manifest.PerRunTic[run] == tic);
                FeatureLedgerEvidence.WriteJson(Path.Combine(output, "bounded-stage-rows.json"), measured.Rows);
                FeatureLedgerEvidence.WriteJson(Path.Combine(output, "readonly-inputs.json"), new { Project = project, Raw = rawPath,
                    RawSha256 = fingerprint, Package = package, IdentifiedRequests = pickedPsms.Count, DeNovoRequests = pickedDeNovo.Count,
                    Mode = "bounded production cache replay, not fresh RAW extraction" });
            }
        }
        finally { Environment.CurrentDirectory = oldDirectory; CultureInfo.CurrentCulture = oldCulture; }
        return cases;
    }
}
