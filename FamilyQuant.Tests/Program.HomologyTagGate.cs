using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HomologyTagGateCommands() =>
    [new("--test-homology-tag-gate", 21, TestCommandPhase.Raw,
        args => args.Contains("--test-homology-tag-gate", StringComparer.Ordinal), (_, _) =>
        {
            HomologyTagGateTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HomologyTagGateTests()
    {
        var medians = HomologyTagErrorModel.MedianProfiles(new[]
        {
            new HomologyConfidenceObservation("AAAAAA", new double[] { 0, 100, 80, 60, 40, 20 }),
            new HomologyConfidenceObservation("AAAAAA", new double[] { 100, 80, 60, 40, 20, 0 }),
            new HomologyConfidenceObservation("AAAAAA", Array.Empty<double>()),
        });
        Check("J47 confidence is per-position median over valid accepted source rows", medians.UsedRows == 2 && medians.SkippedRows == 1
            && medians.Profiles["AAAAAA"].SequenceEqual(new double[] { 50, 90, 70, 50, 30, 10 }));
        var model = new TagErrorGateOptions { Enabled = true };
        var variants = HomologyTagErrorModel.Variants("NQKAGG", model);
        Check("J47 all gate variants use at most one rewrite", variants.All(v => v.Rewrites is 0 or 1)
            && variants.Any(v => v.Rule == "N to GG") && variants.Any(v => v.Rule == "Q to AG")
            && variants.Any(v => v.Rule == "GG to N") && variants.Any(v => v.Rule == "K/Q ambiguity"));
        Check("J47 rewrites preserve total confidence weight", variants.All(v => v.Weights.Sum() == 600));
        HomologyOptions Options(double gate = 1, TagErrorGateOptions? settings = null) => new()
        {
            MinIdentity = gate, TopHitsPerPeptide = 100, BruteForceProteinLimit = 0,
            Science = new() { QueryRole = HomologyQueryRole.DeNovo, TagErrors = settings ?? model },
        };
        foreach (var pair in new (string Query, string Protein)[] { ("ANAAAA", "AGGAAAA"), ("AQAAAA", "AAGAAAA"),
            ("ANRAAA", "ARNAAA"), ("AKAAAA", "AQAAAA"), ("AGGAAAA", "WANAAAA") })
        {
            var db = HomologyDatabase.Build(new[] { ("target", pair.Protein) });
            var result = HomologySearch.SearchOne(pair.Query, db, Options());
            int offset = HomologyScorer.BestWindow(pair.Query, pair.Protein, out int pam);
            Check("J47 isobaric/adjacent gate can admit " + pair.Query + " against " + pair.Protein,
                result.Hits.Count == 1 && result.Hits[0].GateIdentityFraction == 1);
            Check("J47 PAM and reported span still describe the ORIGINAL tag " + pair.Query,
                result.Hits.Count == 1 && result.Hits[0].Offset == offset && result.Hits[0].Score == pam
                && result.Hits[0].MatchedSubsequence.Length == pair.Query.Length);
            if (pair.Query == "ANRAAA")
                Check("J47 adjacent-swap audit names conserved pair mass without implying equal residue masses",
                    result.ScienceAudit?.GateAlignments?.Any(a => a.Rule == "adjacent order swap (same pair mass)") == true);
        }
        var two = HomologyDatabase.Build(new[] { ("two", "GGGGAAAA") });
        Check("J47 two required rewrites cannot sneak through the one-rewrite model",
            HomologySearch.SearchOne("NNAAAA", two, Options()).Hits.Count == 0);
        var shortDb = HomologyDatabase.Build(new[] { ("short", "ANAAAA") });
        Check("J47 original full-window PAM contract still excludes too-short proteins",
            HomologySearch.SearchOne("AGGAAAA", shortDb, Options()).Hits.Count == 0);
        var weighted = new TagErrorGateOptions { Enabled = true, MaximumRewrites = 0,
            LocalConfidenceBySequence = new() { ["AAAAAA"] = new double[] { 100, 100, 100, 100, 10, 100 } } };
        var mismatch = HomologyDatabase.Build(new[] { ("low", "AAAAGA") });
        var low = HomologySearch.SearchOne("AAAAAA", mismatch, Options(0.95, weighted));
        Check("J47 a low-confidence mismatch carries its measured smaller gate weight", low.Hits.Count == 1
            && low.Hits[0].GateIdentityFraction == 500.0 / 510 && low.ScienceAudit?.UsedLocalConfidence == true);
        Check("J47 missing confidence uses unit weights and reports missing coverage", HomologySearch.SearchOne("AAAAAA", mismatch,
            Options(0.95, new() { Enabled = true, MaximumRewrites = 0 })).Hits.Count == 0);
        var named = Options(); named.Science!.QueryRole = HomologyQueryRole.Identified;
        var idDb = HomologyDatabase.Build(new[] { ("target", "AGGAAAA") });
        Check("J47 tag error option never changes explicitly identified search", HomologySearchEquivalence.Difference(
            HomologySearch.Search(new[] { "ANAAAA" }, idDb, new HomologyOptions { MinIdentity = 1, TopHitsPerPeptide = 100, BruteForceProteinLimit = 0 }),
            HomologySearch.Search(new[] { "ANAAAA" }, idDb, named)) == string.Empty);

        foreach (var profile in new[]
        {
            new double[] { 100, 100, 100, 100, 0, 0, 0, 0 },
            new double[] { 100, 100, 0, 100, 100, 0, 0, 0 },
            new double[] { 100, 0, 100, 0, 100, 0, 100, 0 },
        })
        {
            const string query = "ACDEFGHI";
            var fixture = HomologyDatabase.Build(new[] { ("target", "XX" + query + "YY"), ("Reverse_other", "WVTSRQPN") });
            var indexOptions = Options(0.8, new() { Enabled = true, MaximumRewrites = 0, LocalConfidenceBySequence = new() { [query] = profile } });
            var bruteOptions = indexOptions.Clone(); bruteOptions.Strategy = HomologySearchStrategy.BruteForce;
            var fast = HomologySearch.SearchOne(query, fixture, indexOptions);
            var slow = HomologySearch.SearchOne(query, fixture, bruteOptions);
            Check("J47 sparse weights use a proved exact block when the global q3 count cannot", fast.UsedSeedIndex);
            Check("J47 sparse weighted seed retains every scientific hit and gate alignment", HomologySearchEquivalence.Difference([slow], [fast]).Length == 0
                && System.Text.Json.JsonSerializer.Serialize(slow.ScienceAudit) == System.Text.Json.JsonSerializer.Serialize(fast.ScienceAudit));
        }
        var random = new Random(4702);
        const string alphabet = "ARNDCQEGHILKMFPSTWYV";
        for (int length = 6; length <= 20; length++)
        {
            var entries = Enumerable.Range(0, 12).Select(i => ((i % 3 == 0 ? "Reverse_" : "target-") + i,
                new string(Enumerable.Range(0, 35).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray()))).ToArray();
            string query = entries[1].Item2.Substring(2, length);
            var confidence = Enumerable.Range(0, length).Select(i => i % 4 == 0 ? 0.0 : random.Next(10, 100) + 0.125).ToArray();
            var settings = new TagErrorGateOptions { Enabled = true, LocalConfidenceBySequence = new() { [query] = confidence } };
            var indexOptions = Options(length % 2 == 0 ? 0.8 : Math.BitIncrement(0.8), settings);
            var bruteOptions = indexOptions.Clone(); bruteOptions.Strategy = HomologySearchStrategy.BruteForce;
            var fixture = HomologyDatabase.Build(entries);
            string difference = HomologySearchEquivalence.Difference(HomologySearch.Search([query], fixture, bruteOptions),
                HomologySearch.Search([query], fixture, indexOptions));
            Check($"J47 weighted/variant diagonal prefilter retains exact brute result length={length}", difference.Length == 0, difference);
        }
        for (int trial = 0; trial < 30; trial++)
        {
            int length = 6 + trial % 15;
            var entries = Enumerable.Range(0, 12).Select(i => ((i % 3 == 0 ? "Reverse_" : "target-") + i,
                new string(Enumerable.Range(0, 35).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray()))).ToArray();
            string query = entries[1].Item2.Substring(2, length);
            var profile = Enumerable.Range(0, length).Select(i => i % 2 == 0 ? (trial % 3 == 0 ? 0.000000001 : random.Next(1, 100) + 0.125) : 0).ToArray();
            var fastOptions = Options(trial % 3 == 0 ? 1 : trial % 3 == 1 ? 0.8 : Math.BitIncrement(0.8),
                new() { Enabled = true, LocalConfidenceBySequence = new() { [query] = profile } });
            var slowOptions = fastOptions.Clone(); slowOptions.Strategy = HomologySearchStrategy.BruteForce;
            var fixture = HomologyDatabase.Build(entries);
            var fast = HomologySearch.Search([query], fixture, fastOptions); var slow = HomologySearch.Search([query], fixture, slowOptions);
            string difference = HomologySearchEquivalence.Difference(slow, fast);
            Check($"J47 sparse/rounding weighted seed equals brute trial={trial}", difference.Length == 0
                && System.Text.Json.JsonSerializer.Serialize(slow[0].ScienceAudit) == System.Text.Json.JsonSerializer.Serialize(fast[0].ScienceAudit), difference);
        }
    }
}
