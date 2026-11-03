using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Homology;

public sealed record HomologyConfidenceObservation(string Sequence, IReadOnlyList<double> Percent);
public sealed record HomologyConfidenceProfiles(Dictionary<string, double[]> Profiles, int UsedRows, int SkippedRows);
public sealed record HomologyTagVariant(string Sequence, double[] Weights, string Rule, int Rewrites, bool UsedLocalConfidence);
public sealed record HomologyGateAlignment(string ProteinId, string Variant, string Rule, int Offset, int SpanLength,
    double WeightedIdentity, bool UsedLocalConfidence);

/// <summary>Bounded de novo gate variants and source-confidence aggregation. It never changes
/// PAM30MS or treats source labels as truth. The caller supplies accepted normalized PEAKS rows.</summary>
public static class HomologyTagErrorModel
{
    public static HomologyConfidenceProfiles MedianProfiles(IEnumerable<HomologyConfidenceObservation> acceptedRows)
    {
        ArgumentNullException.ThrowIfNull(acceptedRows);
        var groups = new Dictionary<string, List<double[]>>(StringComparer.Ordinal);
        int used = 0, skipped = 0;
        foreach (var row in acceptedRows)
        {
            if (!HomologyScorer.TryNormalizeQuery(row.Sequence, out string sequence, out _)
                || row.Percent == null || row.Percent.Count != sequence.Length
                || row.Percent.Any(v => !double.IsFinite(v) || v < 0 || v > 100)) { skipped++; continue; }
            if (!groups.TryGetValue(sequence, out var rows)) groups[sequence] = rows = new();
            rows.Add(row.Percent.ToArray()); used++;
        }
        var profiles = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var group in groups.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            double[] profile = new double[group.Key.Length];
            for (int i = 0; i < profile.Length; i++)
            {
                double[] values = group.Value.Select(r => r[i]).Order().ToArray();
                profile[i] = values.Length % 2 == 1 ? values[values.Length / 2]
                    : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2;
            }
            profiles[group.Key] = profile;
        }
        return new(profiles, used, skipped);
    }

    public static IReadOnlyList<HomologyTagVariant> Variants(string normalized, TagErrorGateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options); options.Validate();
        if (!HomologyScorer.TryNormalizeQuery(normalized, out string sequence, out string error)) throw new ArgumentException(error);
        bool confidence = options.WeightByLocalConfidence && options.LocalConfidenceBySequence != null
            && options.LocalConfidenceBySequence.TryGetValue(sequence, out var supplied) && supplied.Any(v => v > 0);
        double[] weights = confidence ? options.LocalConfidenceBySequence![sequence].ToArray() : Enumerable.Repeat(100.0, sequence.Length).ToArray();
        var result = new List<HomologyTagVariant> { new(sequence, weights, "Original", 0, confidence) };
        if (!options.Enabled || options.MaximumRewrites == 0) return result;
        for (int i = 0; i < sequence.Length; i++)
        {
            if (options.AllowNAndGG && sequence[i] == 'N') Replace(i, 1, "GG", [weights[i] / 2, weights[i] / 2], "N to GG");
            if (options.AllowQAndAG && sequence[i] == 'Q')
            {
                Replace(i, 1, "AG", [weights[i] / 2, weights[i] / 2], "Q to AG");
                Replace(i, 1, "GA", [weights[i] / 2, weights[i] / 2], "Q to GA");
            }
            if (options.AllowKAndQ && sequence[i] is 'K' or 'Q') Replace(i, 1, sequence[i] == 'K' ? "Q" : "K", [weights[i]], "K/Q ambiguity");
            if (i + 1 >= sequence.Length) continue;
            string pair = sequence.Substring(i, 2);
            if (options.AllowNAndGG && pair == "GG") Replace(i, 2, "N", [weights[i] + weights[i + 1]], "GG to N");
            if (options.AllowQAndAG && pair is "AG" or "GA") Replace(i, 2, "Q", [weights[i] + weights[i + 1]], "AG/GA to Q");
            if (options.AllowAdjacentOrderSwap && sequence[i] != sequence[i + 1])
                Replace(i, 2, new string([sequence[i + 1], sequence[i]]), [weights[i + 1], weights[i]], "Adjacent equal-mass order");
        }
        return result.GroupBy(v => v.Sequence + "\u001f" + string.Join(",", v.Weights.Select(w => w.ToString("R", System.Globalization.CultureInfo.InvariantCulture))), StringComparer.Ordinal)
            .Select(g => g.First()).OrderBy(v => v.Rewrites).ThenBy(v => v.Sequence, StringComparer.Ordinal).ThenBy(v => v.Rule, StringComparer.Ordinal).ToArray();

        void Replace(int start, int count, string replacement, double[] replacementWeights, string rule)
        {
            string variant = sequence[..start] + replacement + sequence[(start + count)..];
            double[] variantWeights = weights.Take(start).Concat(replacementWeights).Concat(weights.Skip(start + count)).ToArray();
            result.Add(new(variant, variantWeights, rule, 1, confidence));
        }
    }

    internal static int MinimumMatchingResidues(double[] weights, double gate)
    {
        double total = weights.Sum(), largest = 0;
        // Positive-sum roundoff bound conservatively widens the prefilter only. The exact ordered
        // weighted comparison still decides the gate. Native PEAKS medians/splits are half/quarter
        // percentages, but callers may supply other finite percentages too.
        double slack = 4 * (Math.BitIncrement(1.0) - 1.0) * weights.Length * total;
        int count = 0;
        foreach (double weight in weights.OrderByDescending(w => w))
        {
            if ((largest + slack) / total >= gate) break;
            largest += weight; count++;
        }
        return count;
    }
}
