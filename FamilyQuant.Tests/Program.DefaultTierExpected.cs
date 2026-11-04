using FamilyQuant.Homology;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DefaultTierExpectedCommands() =>
    [new("--j47d-verify-compute", 22, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--j47d-verify-compute", (args, _) => VerifyDefaultTierCompute(args)),
     new("--j47d-rejoin-headlines", 22, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--j47d-rejoin-headlines", (args, _) =>
        {
            if (args.Length != 4) throw new ArgumentException("Use --j47d-rejoin-headlines <owned-project-copy> <completed-study-root> <fresh-output>.");
            string output = Path.GetFullPath(args[3]);
            if (!output.StartsWith(@"D:\FamilyQuant-data\out-assay\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
                throw new IOException("Fresh owned expected-row output required.");
            Directory.CreateDirectory(output);
            string projectSha = StudyHash(args[1]);
            var saved = ProjectStore.ReadFqproj(args[1]);
            var database = HomologyDatabase.Build(saved.fcf.MyExtendedFastaItems, buildSeedIndex: false);
            var modes = new[] { ("off-04", "OFF"), ("tier-02", "score-tier"), ("length-02", "length") };
            var summaries = new List<FamilyRunSharesResult>();
            var facts = new List<object>();
            foreach (var (folder, label) in modes)
            {
                string packet = Path.Combine(args[2], folder);
                var validation = StudyRead<JsonElement>(Path.Combine(packet, "validation.json"));
                if (!validation.GetProperty("Complete").GetBoolean()) throw new InvalidDataException("Completed measured packet required.");
                var config = validation.GetProperty("Config").Deserialize<HomologyStudyConfig>(HomologyStudyJson)!;
                var sourceProteins = StudyRead<List<CapturedProtein>>(Path.Combine(config.Capture, config.CurationDeNovoCall.ToString("D2", CultureInfo.InvariantCulture), "database.json.gz"));
                var captureFile = new FamilyClusterFile { MyExtendedFastaItems = sourceProteins.Select(p => new FastaItemExtended
                    { SequenceIdentifier = p.Id, Sequence = p.Sequence }).ToList() };
                if (FamilyEnvelopeQuant.ProteinSetFingerprint(captureFile) != FamilyEnvelopeQuant.ProteinSetFingerprint(saved.fcf))
                    throw new InvalidDataException("Target and certified measured protein sets differ.");
                var id = StudyRestoreResults(Path.Combine(packet, "results-" + config.CurationIdentifiedCall + ".json.gz"));
                var dn = StudyRestoreResults(Path.Combine(packet, "results-" + config.CurationDeNovoCall + ".json.gz"));
                var recorded = StudyRead<JsonElement>(Path.Combine(packet, "search-summary.json")).EnumerateArray().ToDictionary(r => r.GetProperty("Call").GetInt32());
                foreach (var pair in new[] { (config.CurationIdentifiedCall, id), (config.CurationDeNovoCall, dn) })
                    if (Convert.ToHexString(SHA256.HashData(StudyScientificBytes(pair.Item2))) != recorded[pair.Item1].GetProperty("ScientificSha256").GetString())
                        throw new InvalidDataException("Measured scientific result payload changed.");
                var options = label == "score-tier" ? HomologyDefaults.ForNewCompute() : new HomologyOptions { Science = config.Science?.Clone() };
                if (options.Science != null) options.Science.QueryRole = HomologyQueryRole.DeNovo;
                if (label == "score-tier" && options.Science?.ScoreTier is not { Enabled: true, MaximumPamScoreDrop: 0 })
                    throw new InvalidDataException("Current Compute default is not the approved score tier.");
                string cache = Path.Combine(output, label + "-cache");
                var compact = CurationHomologyCache.FromReplay(saved.quant, saved.fcf, database, id, dn, cache, options);
                bool hot = false;
                compact = CurationHomologyCache.GetOrSearch(saved.quant, saved.fcf, cache, options,
                    progress: hit => { if (!hit) throw new OperationCanceledException("Rejoined cache missed; no search allowed."); hot = true; });
                var attribution = FamilyEnvelopeQuant.AttributeHits(compact, saved.fcf);
                var prepared = FamilyEnvelopeQuant.PreparePeptides(saved.quant);
                var assigned = FamilyEnvelopeQuant.AttributePeptides(prepared, attribution);
                var view = new QuantManifest { PerRunTic = saved.manifest.PerRunTic,
                    FamilyRunShares = FamilyCompositionStudy.BuildRunShares(assigned, saved.manifest.PerRunTic.Keys,
                        saved.manifest.FamilyRunShares!.Policy, saved.manifest.PerRunTic) };
                FamilyUnobservedStudies.Store(saved.fcf, view);
                EffectivePanelFamilyStudy.StoreIfNeeded(view, assigned, compact, saved.fcf);
                var basis = ReportCompositionBasis.Select(saved.fcf, view);
                var shares = view.FamilyRunShares!; summaries.Add(shares);
                StudyWrite(Path.Combine(output, label + "-shares.json"), shares);
                File.WriteAllText(Path.Combine(output, label + "-headline.csv"), ReportStudyExports.Headline(basis.Grouping!, null, basis.Labels), new UTF8Encoding(false));
                bool baselineEqual = label != "OFF" || JsonSerializer.Serialize(shares, HomologyStudyJson)
                    == JsonSerializer.Serialize(FamilyUnobservedStudies.View(saved.fcf, saved.manifest).FamilyRunShares, HomologyStudyJson);
                if (!baselineEqual) throw new InvalidDataException("Certified OFF rejoin does not reproduce this project's saved shares.");
                facts.Add(new { Mode = label, NativeCacheHot = hot, BaselineEqualStored = baselineEqual, basis.IsIdentity,
                    MembershipSha256 = FamilyStudyMembership.Fingerprint(saved.fcf), Science = options.Science });
            }
            var labels = FamilyNaming.Names(saved.fcf.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)));
            var text = new StringBuilder("# Ana delivered-curation expected headlines\n\nNative certified-hit rejoin, no search or full Compute. Percent of assigned primary family signal, arithmetic per-run mean; signed deltas are percentage points from OFF.\n\n| Current family | OFF % | Score tier % (delta pp) | Length % (delta pp) |\n|---|---:|---:|---:|\n");
            var maps = summaries.Select(s => s.Families.ToDictionary(f => f.FamilyUserId)).ToArray();
            foreach (int family in maps.SelectMany(m => m.Keys).Distinct().OrderByDescending(id => maps[0].GetValueOrDefault(id)?.MeanShare))
            {
                double? before = maps[0].GetValueOrDefault(family)?.MeanShare;
                if (!maps.Any(m => m.GetValueOrDefault(family)?.MeanShare >= (saved.fcf.ReportThreshold ?? FamilyReportThreshold.DefaultThreshold))) continue;
                text.Append("| " + family + ": " + labels.GetValueOrDefault(family, "").Replace("|", "\\|"));
                for (int i = 0; i < maps.Length; i++)
                {
                    double? value = maps[i].GetValueOrDefault(family)?.MeanShare;
                    text.Append(" | " + (value.HasValue ? (value * 100)!.Value.ToString("0.000000", CultureInfo.InvariantCulture) : "unavailable"));
                    if (i > 0) text.Append(" (" + (value.HasValue && before.HasValue ? (100 * (value.Value - before.Value)).ToString("+0.000000;-0.000000;0.000000", CultureInfo.InvariantCulture) : "unavailable") + ")");
                }
                text.AppendLine(" |");
            }
            File.WriteAllText(Path.Combine(output, "expected-headlines.md"), text.ToString(), new UTF8Encoding(false));
            if (StudyHash(args[1]) != projectSha) throw new InvalidDataException("Owned input copy changed.");
            StudyWrite(Path.Combine(output, "validation.json"), new { Complete = true, NoSearch = true, NoFullCompute = true,
                ProjectSha256 = projectSha, Facts = facts });
            Console.WriteLine("Certified measured hits rejoined; OFF exactly equals saved delivered shares; all native caches hot; source unchanged.");
            return 0;
        })];

    private static int VerifyDefaultTierCompute(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Use --j47d-verify-compute <input-copy> <expected-folder> <computed-project> <fresh-output>.");
        string output = Path.GetFullPath(args[4]);
        if (!output.StartsWith(@"D:\FamilyQuant-data\out-assay\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
            throw new IOException("Fresh owned verification output required.");
        var expectedValidation = StudyRead<JsonElement>(Path.Combine(args[2], "validation.json"));
        if (!expectedValidation.GetProperty("Complete").GetBoolean() || StudyHash(args[1]) != expectedValidation.GetProperty("ProjectSha256").GetString())
            throw new InvalidDataException("Expected rows do not match the unchanged input copy.");
        var expected = StudyRead<FamilyRunSharesResult>(Path.Combine(args[2], "score-tier-shares.json"));
        var before = StudyRead<FamilyRunSharesResult>(Path.Combine(args[2], "OFF-shares.json"));
        string computedSha = StudyHash(args[3]);
        var saved = ProjectStore.ReadFqproj(args[3]);
        var manifest = FamilyUnobservedStudies.View(saved.fcf, saved.manifest);
        var science = manifest.FamilyMapping?.Science;
        if (science?.ScoreTier is not { Enabled: true, MaximumPamScoreDrop: 0 }
            || science.DecoyGate?.Enabled == true || science.TagErrors?.Enabled == true)
            throw new InvalidDataException("Full Compute did not record exactly the authorized default policy.");
        var actual = manifest.FamilyRunShares ?? throw new InvalidDataException("Full Compute has no native family shares.");
        bool equal = JsonSerializer.Serialize(expected, HomologyStudyJson) == JsonSerializer.Serialize(actual, HomologyStudyJson);
        var e = expected.Families.ToDictionary(f => f.FamilyUserId);
        var a = actual.Families.ToDictionary(f => f.FamilyUserId);
        var b = before.Families.ToDictionary(f => f.FamilyUserId);
        var basis = ReportCompositionBasis.Select(saved.fcf, manifest);
        var text = new StringBuilder("# J47d ONE full Compute: Ana before and after\n\nNative stored shares; percent of assigned primary family signal, arithmetic run mean. Every family is retained below; expected is the certified-hit rejoin onto this exact input curation.\n\n| Family | Before % | Expected tier % | Computed tier % | Change pp | Expected equals computed |\n|---|---:|---:|---:|---:|---|\n");
        foreach (int id in b.Keys.Union(e.Keys).Union(a.Keys).OrderByDescending(id => b.GetValueOrDefault(id)?.MeanShare))
        {
            double? old = b.GetValueOrDefault(id)?.MeanShare, wanted = e.GetValueOrDefault(id)?.MeanShare, got = a.GetValueOrDefault(id)?.MeanShare;
            text.AppendLine("| " + id + ": " + basis.Labels.GetValueOrDefault(id, "").Replace("|", "\\|") + " | " + Percent(old) + " | " + Percent(wanted)
                + " | " + Percent(got) + " | " + (old.HasValue && got.HasValue ? (100 * (got.Value - old.Value)).ToString("+0.000000;-0.000000;0.000000", CultureInfo.InvariantCulture) : "unavailable")
                + " | " + (wanted == got) + " |");
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "before-after.md"), text.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "computed-headline.csv"), ReportStudyExports.Headline(basis.Grouping!, manifest.FamilyProteinCounts, basis.Labels), new UTF8Encoding(false));
        StudyWrite(Path.Combine(output, "computed-shares.json"), actual);
        if (StudyHash(args[3]) != computedSha) throw new InvalidDataException("Verification changed the computed project.");
        StudyWrite(Path.Combine(output, "validation.json"), new { Complete = true, SharesExactlyEqualExpected = equal,
            InputCopySha256 = StudyHash(args[1]), ComputedProjectSha256 = computedSha, AppVersion = manifest.AppVersion,
            RecordedScience = science, WorldScience = manifest.FamilyWorldResult?.Science, basis.IsIdentity, NoAdditionalCompute = true });
        Console.WriteLine("Native expected/full-Compute shares exactly equal: " + equal);
        return equal ? 0 : 1;
        static string Percent(double? value) => value.HasValue ? (100 * value.Value).ToString("0.000000", CultureInfo.InvariantCulture) : "unavailable";
    }
}
