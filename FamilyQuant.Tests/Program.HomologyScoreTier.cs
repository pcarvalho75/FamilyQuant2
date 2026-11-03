using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HomologyScoreTierCommands() =>
    [new("--test-homology-score-tier", 19, TestCommandPhase.Raw,
        args => args.Contains("--test-homology-score-tier", StringComparer.Ordinal), (_, _) =>
        {
            HomologyScoreTierTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HomologyScoreTierTests()
    {
        var db = HomologyDatabase.Build(new[] { ("exact", "AAAAAA"), ("near", "AAAAAG"),
            ("far", "AAAAGG"), ("Reverse_lower", "AAAAAT") });
        HomologyOptions Options(int margin, HomologyQueryRole role = HomologyQueryRole.DeNovo) => new()
        {
            MinIdentity = 0.5, TopHitsPerPeptide = 100,
            Science = new() { QueryRole = role, ScoreTier = new() { Enabled = true, MaximumPamScoreDrop = margin } },
        };
        var plain = new HomologyOptions { MinIdentity = 0.5, TopHitsPerPeptide = 100 };
        var baseline = HomologySearch.SearchOne("AAAAAA", db, plain);
        Check("J47 score tier fixture has three target hits before experimental filtering", baseline.Hits.Count == 3 && baseline.DecoysSuppressed);
        var disabled = plain.Clone(); disabled.Science = new() { ScoreTier = new() { Enabled = false, MaximumPamScoreDrop = 99 } };
        Check("J47 all-disabled clone preserves serialized old options", JsonSerializer.Serialize(disabled.Clone()) == JsonSerializer.Serialize(plain));
        Check("J47 disabled tier preserves every old result field", HomologySearchEquivalence.Difference([baseline],
            [HomologySearch.SearchOne("AAAAAA", db, disabled)]) == string.Empty);
        foreach (var sample in new[] { (Margin: 0, Count: 1), (Margin: 8, Count: 2), (Margin: 16, Count: 3) })
        {
            var result = HomologySearch.SearchOne("AAAAAA", db, Options(sample.Margin));
            Check($"J47 tier keeps integer PAM score drop <= {sample.Margin}", result.Hits.Count == sample.Count
                && result.Hits.All(h => (long)result.Hits[0].Score - h.Score <= sample.Margin));
            Check($"J47 tier reports before/after multiplicity margin={sample.Margin}", result.ScienceAudit is
                { ProteinsBeforeTier: 3, GateTargetProteins: 3, GateDecoyProteins: 1 } audit
                && audit.ProteinsAfterTier == sample.Count && audit.ProteinsAfterTopCut == sample.Count);
            Check($"J47 tier preserves conservative competition flags margin={sample.Margin}", result.DecoysSuppressed && !result.TargetsSuppressedByDecoyCompetition);
        }
        var identified = HomologySearch.SearchOne("AAAAAA", db, Options(0, HomologyQueryRole.Identified));
        Check("J47 tier does not change explicitly identified queries", HomologySearchEquivalence.Difference([baseline], [identified]) == string.Empty
            && identified.ScienceAudit == null);
        var cut = Options(8); cut.TopHitsPerPeptide = 1;
        var top = HomologySearch.SearchOne("AAAAAA", db, cut);
        Check("J47 multiplicity precedes caller top-hit cut", top.Hits.Count == 1 && top.ScienceAudit is
            { ProteinsBeforeTier: 3, ProteinsAfterTier: 2, ProteinsAfterTopCut: 1 });
        var decoyDb = HomologyDatabase.Build(new[] { ("near", "AAAAAG"), ("Reverse_exact", "AAAAAA") });
        var decoy = HomologySearch.SearchOne("AAAAAA", decoyDb, Options(0));
        Check("J47 tier never rescues a target losing target-decoy competition", decoy.Hits.Count == 1 && decoy.Hits[0].IsDecoy
            && decoy.TargetsSuppressedByDecoyCompetition && decoy.ScienceAudit is { GateTargetProteins: 1, GateDecoyProteins: 1 });
        var tied = HomologyDatabase.Build(new[] { ("b", "AAAAAA"), ("a", "AAAAAA") });
        Check("J47 zero tier preserves all exact best ties in ordinal order", HomologySearch.SearchOne("AAAAAA", tied, Options(0))
            .Hits.Select(h => h.ProteinId).SequenceEqual(new[] { "a", "b" }));
        var absent = HomologySearch.SearchOne("WWWWWW", db, Options(0));
        Check("J47 empty tier remains unmatched with zero multiplicity", absent.Hits.Count == 0
            && absent.ScienceAudit is { ProteinsBeforeTier: 0, ProteinsAfterTier: 0 });
        var bad = Options(-1);
        bool refused = false;
        try { HomologySearch.SearchOne("AAAAAA", db, bad); } catch (ArgumentOutOfRangeException) { refused = true; }
        Check("J47 negative PAM tier margin is rejected", refused);

        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "exact", Sequence = "AAAAAA", UserID = 1 },
            new() { SequenceIdentifier = "near", Sequence = "AAAAAG", UserID = 2 },
        } };
        var rows = new[] { Qr("denovo", "tier-cache", 1, 2, "AAAAAA", 10, 10) };
        string cache = Path.Combine(Path.GetTempPath(), "FamilyQuant.TierCache." + Guid.NewGuid().ToString("N"));
        var modes = new List<bool>();
        var oldHits = CurationHomologyCache.GetOrSearch(rows, file, cache, plain, progress: hit => modes.Add(hit));
        _ = CurationHomologyCache.GetOrSearch(rows, file, cache, plain, progress: hit => modes.Add(hit));
        var tierHits = CurationHomologyCache.GetOrSearch(rows, file, cache, Options(0), progress: hit => modes.Add(hit));
        _ = CurationHomologyCache.GetOrSearch(rows, file, cache, Options(0), progress: hit => modes.Add(hit));
        Check("J47 enabled tier has its own verified J28 cache entry", modes.SequenceEqual(new[] { false, true, false, true }));
        Check("J47 cached tier retains narrowed family candidates", FamilyEnvelopeQuant.AttributeHits(oldHits, file).Resolve("denovo", "AAAAAA").Count == 2
            && FamilyEnvelopeQuant.AttributeHits(tierHits, file).Resolve("denovo", "AAAAAA").SequenceEqual(new[] { 1 }));
    }
}
