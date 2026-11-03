using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferenceEnvelopeOptimizationCommands() =>
    [new("--test-reference-envelope-optimization", 100, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-reference-envelope-optimization", (_, _) =>
        {
            ReferenceEnvelopeOptimizationTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReferenceEnvelopeOptimizationTests()
    {
        static NativePeptideQuant P(int i, double scale = 1) => new()
        {
            Side = NativeQuantFractions.SideIdentified, Run = "fixture", Sequence = "PEPTIDE" + i,
            Charge = 2, PrecursorMz = 300 + (i % 7) * 260,
            AreaM0 = scale * (1 + i % 19), EnvelopeSum = scale * (1 + i % 19) * (1.01 + i % 13),
        };
        var same = P(7);
        var cases = new List<(string Name, List<NativePeptideQuant> Rows)>
        {
            ("empty", new()), ("single member fallback", new() { P(0) }),
            ("one per bin", Enumerable.Range(0, 7).Select(i => P(i)).ToList()),
            ("duplicate object references", new() { same, P(4), same, P(14), P(19) }),
            ("mixed bins and scales", Enumerable.Range(0, 250).Select(i => P(i, Math.ScaleB(1, i % 81 - 40))).ToList()),
            ("one populated bin", Enumerable.Range(0, 200).Select(i => P(i * 7)).ToList()),
        };
        foreach (var item in cases)
        {
            var expected = LegacyLeaveOneOut(item.Rows);
            var actual = ReferenceEnvelopeEstimator.Evaluate(item.Rows);
            Check("J38 reference diagnostics retain exact old floating results: " + item.Name,
                expected.Count == actual.CrossValidatedPeptides
                && Bits(expected.Weighted, actual.CrossValidatedWeightedAbsolutePercentError)
                && Bits(expected.Bias, actual.CrossValidatedSignedBiasFraction)
                && Bits(expected.Median, actual.CrossValidatedMedianAbsolutePercentError)
                && Bits(expected.P90, actual.CrossValidatedP90AbsolutePercentError));
        }
        // An allocation budget catches the observed full-list-per-held-out cost without a noisy
        // wall-clock threshold on the shared builder machine.
        var large = Enumerable.Range(0, 5000).Select(i => P(i)).ToList();
        ReferenceEnvelopeEstimator.Evaluate(cases[2].Rows);
        long start = GC.GetAllocatedBytesForCurrentThread();
        var result = ReferenceEnvelopeEstimator.Evaluate(large);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Check("J38 leave-one-out diagnostics avoid quadratic full-peer-list allocation",
            result.CrossValidatedPeptides == large.Count && allocated < 16_000_000,
            allocated.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes for 5000 representatives");
        static bool Bits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    }

    // Frozen numerical oracle: original loop/order, including removing every occurrence of the
    // same object reference. It is intentionally straightforward and fixture-only.
    private static (int Count, double Weighted, double Bias, double Median, double P90) LegacyLeaveOneOut(
        IReadOnlyList<NativePeptideQuant> rows)
    {
        var identified = rows.Where(p => p.Side == NativeQuantFractions.SideIdentified
            && p.AreaM0 > 0 && p.EnvelopeSum >= p.AreaM0 && p.MonoisotopicMass > 0).ToList();
        double absolute = 0, signed = 0, observed = 0;
        var errors = new List<double>();
        foreach (var held in identified)
        {
            var peers = identified.Where(p => !ReferenceEquals(p, held)).ToList();
            double m0Total = peers.Sum(p => p.AreaM0), envelopeTotal = peers.Sum(p => p.EnvelopeSum);
            double pooled = Ratio(envelopeTotal, m0Total, 1);
            int bin = ReferenceEnvelopeEstimator.BinOf(held.MonoisotopicMass);
            double m0 = peers.Where(p => ReferenceEnvelopeEstimator.BinOf(p.MonoisotopicMass) == bin).Sum(p => p.AreaM0);
            double envelope = peers.Where(p => ReferenceEnvelopeEstimator.BinOf(p.MonoisotopicMass) == bin).Sum(p => p.EnvelopeSum);
            double error = held.AreaM0 * Ratio(envelope, m0, pooled) - held.EnvelopeSum;
            absolute += Math.Abs(error); signed += error; observed += held.EnvelopeSum;
            if (held.EnvelopeSum > 0) errors.Add(Math.Abs(error) / held.EnvelopeSum);
        }
        errors.Sort();
        return (errors.Count, observed > 0 ? absolute / observed : 0, observed > 0 ? signed / observed : 0,
            Quantile(.5), Quantile(.9));
        static double Ratio(double n, double d, double fallback)
        {
            double r = d > 0 ? n / d : fallback;
            if (double.IsNaN(r) || double.IsInfinity(r)) r = fallback;
            return Math.Max(1, r);
        }
        double Quantile(double fraction)
        {
            if (errors.Count == 0) return 0;
            double position = Math.Max(0, Math.Min(1, fraction)) * (errors.Count - 1);
            int lo = (int)Math.Floor(position), hi = (int)Math.Ceiling(position);
            if (lo == hi) return errors[lo];
            double weight = position - lo;
            return errors[lo] * (1 - weight) + errors[hi] * weight;
        }
    }
}
