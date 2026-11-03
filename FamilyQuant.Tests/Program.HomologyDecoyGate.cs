using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HomologyDecoyGateCommands() =>
    [new("--test-homology-decoy-gate", 20, TestCommandPhase.Raw,
        args => args.Contains("--test-homology-decoy-gate", StringComparer.Ordinal), (_, _) =>
        {
            HomologyDecoyGateTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HomologyDecoyGateTests()
    {
        var settings = new DecoyIdentityGateOptions { Enabled = true };
        var tight = Enumerable.Repeat(new HomologyGateObservation(10, 1, null), 100)
            .Concat(Enumerable.Repeat(new HomologyGateObservation(10, 0.7, 0.8), 2)).ToArray();
        var selected = HomologyDecoyCalibration.Fit(tight, 1000, 1000, 0.8, settings).Single();
        Check("J47 length gate tightens only until observed decoy ratio meets the stated level", selected.IdentityGate == 0.9
            && selected.TargetWinners == 100 && selected.DecoyWinners == 0 && selected.EmpiricalDecoyRatio == 0.01);
        var clean = HomologyDecoyCalibration.Fit(tight.Take(100), 1000, 1000, 0.8, settings).Single();
        Check("J47 supported clean length keeps the exploration floor", clean.IdentityGate == 0.8);
        var unsupported = HomologyDecoyCalibration.Fit(tight.Take(99), 1000, 1000, 0.8, settings).Single();
        Check("J47 low-support length explicitly abstains", unsupported.IdentityGate == null && unsupported.Status == "Low support");
        var ties = HomologyDecoyCalibration.Fit(Enumerable.Repeat(new HomologyGateObservation(10, 1, 1), 100), 1000, 1000, 0.8, settings).Single();
        Check("J47 identity ties favor decoys and cannot calibrate themselves", ties.IdentityGate == null && ties.Status == "No supported gate");
        Check("J47 missing decoy database is not a zero-decoy success", HomologyDecoyCalibration.Fit(tight, 1000, 0, 0.8, settings)
            .Single().Status == "No decoy exposure");
        var counts = new DecoyIdentityGateOptions { Enabled = true, MinimumLengthQueries = 2, MaximumDecoyRatio = 0.5 };
        Check("J47 decoy ratio normalizes target/decoy database exposure", HomologyDecoyCalibration.Fit(
            Enumerable.Repeat(new HomologyGateObservation(6, 1, null), 2), 4, 2, 0.8, counts).Single().IdentityGate == null);
        var mixed = HomologyDecoyCalibration.Fit(tight.Concat(tight.Select(o => o with { Length = 11 })), 1000, 1000, 0.8, settings);
        Check("J47 length calibration remains separate and deterministic", mixed.Select(b => b.Length).SequenceEqual(new[] { 10, 11 })
            && mixed.All(b => b.Queries == 102));
        var db = HomologyDatabase.Build(new[] { ("a", "AAAAAA"), ("r", "RRRRRR"), ("Reverse_d", "DDDDDA"), ("Reverse_e", "EEEEEA") });
        var options = new HomologyOptions { MinIdentity = 0.8, TopHitsPerPeptide = 100, Science = new()
        { QueryRole = HomologyQueryRole.DeNovo, DecoyGate = counts } };
        var results = HomologySearch.Search(new[] { "AAAAAA", "RRRRRR", "DDDDDD" }, db, options);
        Check("J47 calibrated gate is applied by the actual search", results.All(r => r.ScienceAudit?.LengthCalibration?.IdentityGate == 1)
            && results[0].Hits.Count == 1 && results[1].Hits.Count == 1 && results[2].Hits.Count == 0);
        var low = options.Clone(); low.Science!.DecoyGate!.MinimumLengthQueries = 4;
        var abstained = HomologySearch.Search(new[] { "AAAAAA", "RRRRRR", "DDDDDD" }, db, low);
        Check("J47 actual low-support search abstains with a reason", abstained.All(r => r.Hits.Count == 0
            && r.ScienceAudit?.LengthCalibration?.Status == "Low support"));
        var identified = options.Clone(); identified.Science!.QueryRole = HomologyQueryRole.Identified;
        var standard = HomologySearch.Search(new[] { "AAAAAA", "RRRRRR", "DDDDDD" }, db, new HomologyOptions { TopHitsPerPeptide = 100 });
        Check("J47 decoy-calibrated option leaves identified search untouched", HomologySearchEquivalence.Difference(standard,
            HomologySearch.Search(new[] { "AAAAAA", "RRRRRR", "DDDDDD" }, db, identified)) == string.Empty);
    }
}
