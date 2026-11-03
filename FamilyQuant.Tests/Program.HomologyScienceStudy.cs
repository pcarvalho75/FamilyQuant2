using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class HomologyStudyConfig
    {
        public HomologyStudyConfig() { }
        public string Capture { get; set; } = "";
        public string ReferenceValidation { get; set; } = "";
        public string Project { get; set; } = "";
        public string DeNovoDirectory { get; set; } = "";
        public string Output { get; set; } = "";
        public HomologyScienceOptions? Science { get; set; }
        public Dictionary<int, HomologyQueryRole> Roles { get; set; } = new();
        public int CurationIdentifiedCall { get; set; } = 1;
        public int CurationDeNovoCall { get; set; } = 2;
        public int WorldCall { get; set; } = 3;
        public int KnownHolderCall { get; set; } = 4;
        public int KnownCreditCall { get; set; } = 5;
        public string J42Audit { get; set; } = "";
        public string J42Provenance { get; set; } = "";
        public string ReuseResultsDirectory { get; set; } = "";
    }
    private sealed record StudyStoredHit(string ProteinId, int Offset, int Score, double IdentityFraction,
        double GateIdentityFraction, string MatchedSubsequence, bool IsDecoy);
    private sealed record StudyStoredResult(string Peptide, string NormalizedPeptide, string? Rejection,
        bool DecoysSuppressed, bool TargetsSuppressedByDecoyCompetition, List<StudyStoredHit> Hits,
        HomologyScienceAudit? ScienceAudit = null);
    private static readonly JsonSerializerOptions HomologyStudyJson = new()
    { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [TestCommandTable]
    private static IEnumerable<TestCommand> HomologyScienceStudyCommands() =>
    [new("--homology-science-study", 22, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--homology-science-study", (args, _) =>
        {
            if (args.Length != 2) throw new ArgumentException("Use --homology-science-study <config.json>.");
            return RunHomologyScienceStudy(args[1]);
        })];

    private static string StudyHash(string path)
    {
        using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file));
    }
    private static T StudyRead<T>(string path)
    {
        using var file = File.OpenRead(path);
        using Stream input = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : file;
        return JsonSerializer.Deserialize<T>(input, HomologyStudyJson) ?? throw new InvalidDataException("Missing study input: " + path);
    }
    private static void StudyWrite<T>(string path, T value)
    {
        using var file = File.Create(path);
        using Stream output = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionLevel.Optimal) : file;
        JsonSerializer.Serialize(output, value, HomologyStudyJson);
    }
    private static byte[] StudyScientificBytes(IReadOnlyList<HomologyResult> results) => JsonSerializer.SerializeToUtf8Bytes(results.Select(r => new
    {
        r.Peptide, r.NormalizedPeptide, r.Rejection, r.DecoysSuppressed, r.TargetsSuppressedByDecoyCompetition,
        Hits = r.Hits.Select(h => new { h.ProteinId, h.Offset, h.Score, h.IdentityFraction, h.GateIdentityFraction, h.MatchedSubsequence, h.IsDecoy }),
    }));

    private static List<HomologyResult> StudyRestoreResults(string path)
    {
        var rows = StudyRead<List<StudyStoredResult>>(path);
        var hitConstructor = typeof(HomologyHit).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var resultConstructor = typeof(HomologyResult).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return rows.Select(r => (HomologyResult)resultConstructor.Invoke(new object?[]
        {
            r.Peptide, r.NormalizedPeptide, r.Rejection,
            r.Hits.Select(h => (HomologyHit)hitConstructor.Invoke(new object[] { h.ProteinId, h.Offset, h.Score,
                h.IdentityFraction, h.GateIdentityFraction, h.MatchedSubsequence, h.IsDecoy })).ToArray(),
            r.DecoysSuppressed, r.TargetsSuppressedByDecoyCompetition, 0, false, r.ScienceAudit,
        })).ToList();
    }

    private static int RunHomologyScienceStudy(string configuration)
    {
        var config = StudyRead<HomologyStudyConfig>(configuration);
        string output = Path.GetFullPath(config.Output), capture = Path.GetFullPath(config.Capture);
        if (!output.StartsWith(@"D:\FamilyQuant-data\out-assay\", StringComparison.OrdinalIgnoreCase)
            && !output.StartsWith(@"C:\tmp\familyquant\FQAssay\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Study output must stay in the worker-owned proof root.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Fresh study output required.");
        Directory.CreateDirectory(output);
        var certified = StudyRead<JsonElement>(config.ReferenceValidation);
        if (!certified.GetProperty("AllEqual").GetBoolean() || certified.GetProperty("CompletedCalls").GetInt32() != config.Roles.Count)
            throw new InvalidDataException("A complete verified old-reference packet is required.");
        var referenceCalls = certified.GetProperty("Calls").EnumerateArray().ToDictionary(c => c.GetProperty("Call").GetInt32());
        string projectSha = StudyHash(config.Project);
        var saved = ProjectStore.ReadFqproj(config.Project);
        HomologyScienceOptions? science = config.Science?.AnyEnabled == true ? config.Science.Clone() : null;
        Dictionary<int, JsonElement>? measuredCalls = null;
        if (config.ReuseResultsDirectory.Length > 0)
        {
            var prior = StudyRead<JsonElement>(Path.Combine(config.ReuseResultsDirectory, "validation.json"));
            var priorConfig = prior.GetProperty("Config").Deserialize<HomologyStudyConfig>(HomologyStudyJson)!;
            if (!prior.GetProperty("Complete").GetBoolean() || prior.GetProperty("ProjectSha256").GetString() != projectSha
                || JsonSerializer.Serialize(priorConfig.Science, HomologyStudyJson) != JsonSerializer.Serialize(config.Science, HomologyStudyJson))
                throw new InvalidDataException("Measured search reuse requires the same completed project and science settings.");
            measuredCalls = StudyRead<JsonElement>(Path.Combine(config.ReuseResultsDirectory, "search-summary.json"))
                .EnumerateArray().ToDictionary(r => r.GetProperty("Call").GetInt32());
        }
        HomologyConfidenceProfiles? profiles = null;
        if (science?.TagErrors?.Enabled == true && science.TagErrors.WeightByLocalConfidence)
        {
            profiles = StudyConfidenceProfiles(config.DeNovoDirectory, saved.manifest);
            science.TagErrors.LocalConfidenceBySequence = profiles.Profiles;
            StudyWrite(Path.Combine(output, "confidence-summary.json"), new { profiles.UsedRows, profiles.SkippedRows,
                Profiles = profiles.Profiles.Count, ProfilesSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(profiles.Profiles))),
                Method = "Per-position median of valid accepted PEAKS rows for the same normalized sequence; missing/all-zero uses unit weights" });
        }
        var results = new Dictionary<int, List<HomologyResult>>();
        var databases = new Dictionary<int, HomologyDatabase>();
        var databaseCache = new Dictionary<string, HomologyDatabase>(StringComparer.Ordinal);
        var baseOptions = new Dictionary<int, HomologyOptions>();
        var reports = new List<object>();
        foreach (string directory in Directory.GetDirectories(capture).OrderBy(p => p, StringComparer.Ordinal))
        {
            var meta = StudyRead<CapturedSearch>(Path.Combine(directory, "call.json"));
            if (!config.Roles.TryGetValue(meta.Call, out var role) || !referenceCalls.TryGetValue(meta.Call, out var reference))
                throw new InvalidDataException("Unexpected captured call.");
            foreach (var item in new (string Name, string Sha)[] { ("database", meta.DatabaseSha256), ("queries", meta.QueriesSha256),
                ("options", meta.OptionsSha256), ("results", meta.ResultsSha256) })
                if (StudyHash(Path.Combine(directory, item.Name + ".json.gz")) != item.Sha) throw new InvalidDataException("Captured file changed.");
            if (reference.GetProperty("CapturedDatabaseSha256").GetString() != meta.DatabaseSha256
                || reference.GetProperty("CapturedQueriesSha256").GetString() != meta.QueriesSha256
                || reference.GetProperty("CapturedOptionsSha256").GetString() != meta.OptionsSha256)
                throw new InvalidDataException("Capture differs from the verified reference inputs.");
            string dbKey = meta.DatabaseSha256 + ":" + meta.SeedLength + ":" + meta.HasSeedIndex;
            if (!databaseCache.TryGetValue(dbKey, out var database))
            {
                var proteins = StudyRead<List<CapturedProtein>>(Path.Combine(directory, "database.json.gz"));
                databaseCache[dbKey] = database = HomologyDatabase.Build(proteins.Select(p => (p.Id, p.Sequence)), meta.SeedLength, meta.HasSeedIndex);
            }
            databases[meta.Call] = database;
            var queries = StudyRead<string[]>(Path.Combine(directory, "queries.json.gz"));
            var options = StudyRead<HomologyOptions>(Path.Combine(directory, "options.json.gz"));
            baseOptions[meta.Call] = options.Clone();
            options.Science = science?.Clone();
            if (options.Science != null) options.Science.QueryRole = role;
            var timer = Stopwatch.StartNew();
            bool reused = science != null && role == HomologyQueryRole.Identified;
            var found = measuredCalls != null ? StudyRestoreResults(Path.Combine(config.ReuseResultsDirectory, "results-" + meta.Call + ".json.gz"))
                : reused ? StudyRestoreResults(Path.Combine(directory, "results.json.gz")) : HomologySearch.Search(queries, database, options);
            timer.Stop(); results[meta.Call] = found;
            byte[] bytes = StudyScientificBytes(found);
            string digest = Convert.ToHexString(SHA256.HashData(bytes));
            if (measuredCalls != null && (!measuredCalls.TryGetValue(meta.Call, out var measured)
                || measured.GetProperty("ScientificSha256").GetString() != digest))
                throw new InvalidDataException("Reused measured search scientific payload differs from its completed packet.");
            bool equal = digest == reference.GetProperty("BeforeSha256").GetString();
            if ((science == null || role == HomologyQueryRole.Identified) && !equal)
                throw new InvalidDataException("Default-OFF/identified parity failed for call " + meta.Call);
            StudyWrite(Path.Combine(output, "results-" + meta.Call + ".json.gz"), found);
            var report = new { meta.Call, meta.Queries, meta.Proteins, Role = role.ToString(), ReusedCertifiedIdentified = reused,
                ReusedMeasuredResults = measuredCalls != null,
                Seconds = timer.Elapsed.TotalSeconds, ScientificSha256 = digest, EqualToReference = equal,
                TargetGateQueries = found.Count(StudyTargetGate),
                DecoyGateQueries = found.Count(StudyDecoyGate),
                TargetWinningQueries = found.Count(r => r.Hits.Any(h => !h.IsDecoy)), DecoyWinningQueries = found.Count(r => r.Hits.Any(h => h.IsDecoy)),
                UnmatchedQueries = found.Count(r => r.Hits.Count == 0), RejectedQueries = found.Count(r => r.IsRejected),
                CalibrationAbstentions = found.Count(r => r.ScienceAudit?.LengthCalibration is { IdentityGate: null }),
                ConfidenceCoveredQueries = found.Count(r => r.ScienceAudit?.UsedLocalConfidence == true),
                MeanProteinMultiplicity = found.Count == 0 ? 0 : found.Average(r => r.Hits.Count) };
            reports.Add(report); Console.WriteLine(JsonSerializer.Serialize(report));
            StudyWrite(Path.Combine(output, "search-summary.json"), reports);
            StudyWrite(Path.Combine(output, "length-gates-" + meta.Call + ".json"), found.Select(r => r.ScienceAudit?.LengthCalibration)
                .Where(b => b != null).Distinct().OrderBy(b => b!.Length).ToArray());
        }
        if (results.Count != config.Roles.Count) throw new InvalidDataException("A captured call is missing.");
        var curationOptions = baseOptions[config.CurationDeNovoCall].Clone(); curationOptions.Science = science?.Clone();
        if (curationOptions.Science != null) curationOptions.Science.QueryRole = HomologyQueryRole.DeNovo;
        var compact = CurationHomologyCache.FromReplay(saved.quant, saved.fcf, databases[config.CurationIdentifiedCall],
            results[config.CurationIdentifiedCall], results[config.CurationDeNovoCall], Path.Combine(output, "curation-cache"), curationOptions);
        bool hitCache = false;
        compact = CurationHomologyCache.GetOrSearch(saved.quant, saved.fcf, Path.Combine(output, "curation-cache"), curationOptions,
            progress: hit => hitCache = hit);
        if (!hitCache) throw new InvalidDataException("The replayed curation cache did not take the native hot path.");
        var attribution = FamilyEnvelopeQuant.AttributeHits(compact, saved.fcf);
        var prepared = FamilyEnvelopeQuant.PreparePeptides(saved.quant);
        var first = FamilyEnvelopeQuant.AttributePeptides(prepared, attribution);
        var shares = FamilyCompositionStudy.BuildRunShares(first, saved.manifest.PerRunTic.Keys,
            SharedPeptidePolicy.FamilyUnique, saved.manifest.PerRunTic);
        // Native completion appends the current panel's explicit unobserved-family zero rows.
        // Reuse that exact view so the baseline is the complete stored table, not observed rows only.
        var shareView = new QuantManifest { FamilyRunShares = shares };
        FamilyUnobservedStudies.Store(saved.fcf, shareView);
        shares = shareView.FamilyRunShares!;
        StudyWrite(Path.Combine(output, "family-run-shares.json"), shares);
        var effectiveView = new QuantManifest { PerRunTic = saved.manifest.PerRunTic };
        EffectivePanelFamilyStudy.StoreIfNeeded(effectiveView, first, compact, saved.fcf);
        StudyWrite(Path.Combine(output, "effective-panel-families.json"), effectiveView.EffectivePanelFamilies);
        var snapshot = FamilyWorldFetcher.Stored(saved.fcf);
        var world = FamilyWorld.FromSnapshot(snapshot);
        var worldOptions = new FamilyWorldOptions { MinimumTagLength = saved.manifest.FamilyWorldResult?.MinimumTagLength ?? FamilyWorldOptions.DefaultMinimumTagLength,
            Science = science?.Clone() };
        int[] lengths = saved.manifest.FamilyWorldResult?.Lengths?.Select(l => l.MinimumTagLength).ToArray() ?? FamilyWorldOptions.LengthChoices.ToArray();
        var worldFound = results[config.WorldCall].ToDictionary(r => r.NormalizedPeptide, StringComparer.Ordinal);
        string[] needed = first.UnattributedDeNovoPeptides.Select(p => DeNovoReconciler.NormalizeSequence(p.Sequence))
            .Where(s => s.Length >= lengths.Min()).Distinct(StringComparer.Ordinal).ToArray();
        string[] missing = needed.Where(q => !worldFound.ContainsKey(q)).ToArray();
        bool changedCohort = !worldFound.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(needed);
        int supplementalQueries = 0;
        if (changedCohort && science?.DecoyGate?.Enabled == true)
        {
            // A per-search calibration is cohort-dependent. Refit only this affected world search,
            // never reuse gates learned on a different residual set or recompute quantitation.
            worldFound = HomologySearch.Search(needed, world.Database, worldOptions.SearchOptions())
                .ToDictionary(r => r.NormalizedPeptide, StringComparer.Ordinal); supplementalQueries = needed.Length;
        }
        else if (missing.Length > 0)
        {
            foreach (var result in HomologySearch.Search(missing, world.Database, worldOptions.SearchOptions())) worldFound[result.NormalizedPeptide] = result;
            supplementalQueries = missing.Length;
        }
        var actualWorld = needed.Select(q => worldFound[q]).ToArray();
        StudyWrite(Path.Combine(output, "actual-world-results.json.gz"), actualWorld);
        StudyWrite(Path.Combine(output, "actual-world-length-gates.json"), actualWorld.Select(r => r.ScienceAudit?.LengthCalibration)
            .Where(b => b != null).Distinct().OrderBy(b => b!.Length).ToArray());
        StudyWrite(Path.Combine(output, "actual-world-gate-summary.json"), new { Queries = actualWorld.Length,
            TargetGateQueries = actualWorld.Count(StudyTargetGate), DecoyGateQueries = actualWorld.Count(StudyDecoyGate),
            CalibrationAbstentions = actualWorld.Count(r => r.ScienceAudit?.LengthCalibration is { IdentityGate: null }),
            ConfidenceCoveredQueries = actualWorld.Count(r => r.ScienceAudit?.UsedLocalConfidence == true),
            SupplementalQueries = supplementalQueries, CalibrationCohortChanged = changedCohort });
        var passes = FamilyWorldPass.RunLengthsFromResults(first, world, worldOptions, lengths, worldFound);
        var holders = results[config.KnownHolderCall].ToDictionary(r => r.NormalizedPeptide, StringComparer.Ordinal);
        var knownTags = results[config.KnownCreditCall].ToDictionary(r => r.NormalizedPeptide, StringComparer.Ordinal);
        var complete = FamilyWorldAnalysis.CompleteLengthsFromResults(first, passes, saved.fcf, saved.manifest.SpectrumComparisonRows,
            world, snapshot.Release, null, worldOptions, saved.manifest.FamilyWorldResult?.MinimumKnownCredits ?? FamilyWorldErrors.DefaultMinimumKnownCredits,
            holders, knownTags);
        StudyWrite(Path.Combine(output, "family-world.json"), complete.Summary);
        StudyWrite(Path.Combine(output, "known-answer-pairs.json.gz"), complete.KnownAnswers);
        if (science == null && saved.manifest.FamilyWorldResult != null
            && JsonSerializer.Serialize(saved.manifest.FamilyWorldResult, HomologyStudyJson) != JsonSerializer.Serialize(complete.Summary, HomologyStudyJson))
            throw new InvalidDataException("Cached default-OFF world calculation differs from the stored native result.");
        if (science == null && saved.manifest.FamilyRunShares != null
            && JsonSerializer.Serialize(saved.manifest.FamilyRunShares, HomologyStudyJson) != JsonSerializer.Serialize(shares, HomologyStudyJson))
            throw new InvalidDataException("Cached default-OFF family shares differ from the stored native result.");
        if (science == null && saved.manifest.EffectivePanelFamilies != null
            && JsonSerializer.Serialize(saved.manifest.EffectivePanelFamilies, HomologyStudyJson)
                != JsonSerializer.Serialize(effectiveView.EffectivePanelFamilies, HomologyStudyJson))
            throw new InvalidDataException("Cached default-OFF effective panel families differ from the stored native result.");
        StudyWrite(Path.Combine(output, "headline.json"), new { complete.Summary.MeasuredTotalEnvelope, complete.Summary.MeasuredIdentifiedEnvelope,
            complete.Summary.MeasuredDeNovoEnvelope, complete.Summary.ResidualEnvelope, complete.Summary.UnexplainedEnvelope,
            complete.Summary.SharedEnvelope, complete.Summary.KnownPairs, complete.Summary.KnownLanded, complete.Summary.KnownRight,
            KnownWrong = complete.Summary.KnownLanded - complete.Summary.KnownRight, World = complete.Summary.LengthEstimates,
            SupplementaryWorldQueries = supplementalQueries, CalibrationCohortChanged = changedCohort, NativeCurationCacheHit = hitCache });
        if (config.J42Audit.Length > 0) StudyWrite(Path.Combine(output, "j42-reference.json"), new
        {
            Scope = "Structural invariance: protein-family neighbour LOO is outside HomologySearch; not recomputed",
            AuditSha256 = StudyHash(config.J42Audit), ProvenanceSha256 = StudyHash(config.J42Provenance),
            RecordedAudit = StudyRead<JsonElement>(config.J42Audit), RecordedProvenance = StudyRead<JsonElement>(config.J42Provenance),
        });
        if (StudyHash(config.Project) != projectSha) throw new InvalidDataException("The input project changed.");
        StudyWrite(Path.Combine(output, "validation.json"), new { Complete = true, DefaultOff = science == null,
            Calls = results.Count, NativeCurationCacheHit = hitCache, ProjectSha256 = projectSha, Config = config,
            ConfidenceProfiles = profiles?.Profiles.Count, NoFullCompute = true, J42Recomputed = false });
        return 0;
    }

    // An unsupported length bin has no applied gate. Its audit retains the exploration
    // observations for diagnosis, which must not be counted as admitted gate queries.
    private static bool StudyTargetGate(HomologyResult r) => r.ScienceAudit?.LengthCalibration is not { IdentityGate: null }
        && (r.ScienceAudit?.GateTargetProteins > 0 || r.Hits.Any(h => !h.IsDecoy) || r.TargetsSuppressedByDecoyCompetition);
    private static bool StudyDecoyGate(HomologyResult r) => r.ScienceAudit?.LengthCalibration is not { IdentityGate: null }
        && (r.ScienceAudit?.GateDecoyProteins > 0 || r.Hits.Any(h => h.IsDecoy) || r.DecoysSuppressed);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static HomologyConfidenceProfiles StudyConfidenceProfiles(string directory, QuantManifest manifest)
    {
        if (directory.Length == 0) throw new ArgumentException("Accepted PEAKS source directory is required for weighted tag experiments.");
        var filter = manifest.DeNovoSourceScoreDistributions.First();
        var criteria = new DeNovoFilterCriteria { MinLength = filter.MinLength, StretchLength = filter.StretchLength, StretchFloor = filter.StretchFloor };
        var selections = manifest.DeNovoInputs.GroupBy(i => i.SourceId).Select(g => g.First()).OrderBy(i => i.SourcePriority)
            .Select(i => new DeNovoSourceSelection(DeNovoSource.FromId(i.SourceId) ?? throw new InvalidDataException("Unknown source."), i.MinimumConfidence, i.SourcePriority)).ToArray();
        var imported = DeNovoSourceImport.Run(directory, criteria, selections);
        return HomologyTagErrorModel.MedianProfiles(imported.Kept.Where(r => r.SourceId == DeNovoSource.DenovoOnly.Id || r.SourceId == DeNovoSource.DeNovoAll.Id)
            .Select(r => new HomologyConfidenceObservation(DeNovoReconciler.NormalizeSequence(r.Peptide), r.LocalConfidence.Select(v => (double)v).ToArray())));
    }
}
