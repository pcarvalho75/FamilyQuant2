using FamilyQuant.Homology;
using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

/// <summary>Compares scientific results, excluding only search-work diagnostics.</summary>
internal static class HomologySearchEquivalence
{
    internal static string Difference(IReadOnlyList<HomologyResult> expected, IReadOnlyList<HomologyResult> actual)
    {
        if (expected.Count != actual.Count) return "result count";
        for (int i = 0; i < expected.Count; i++)
        {
            HomologyResult a = expected[i], b = actual[i];
            string at = "query " + i + " (" + a.Peptide + ") ";
            if (a.Peptide != b.Peptide || a.NormalizedPeptide != b.NormalizedPeptide || a.Rejection != b.Rejection)
                return at + "query/rejection";
            if (a.DecoysSuppressed != b.DecoysSuppressed ||
                a.TargetsSuppressedByDecoyCompetition != b.TargetsSuppressedByDecoyCompetition)
                return at + "competition flags";
            if (a.Hits.Count != b.Hits.Count) return at + "hit count";
            for (int h = 0; h < a.Hits.Count; h++)
            {
                HomologyHit x = a.Hits[h], y = b.Hits[h];
                if (x.ProteinId != y.ProteinId || x.Offset != y.Offset || x.Score != y.Score ||
                    BitConverter.DoubleToInt64Bits(x.IdentityFraction) != BitConverter.DoubleToInt64Bits(y.IdentityFraction) ||
                    BitConverter.DoubleToInt64Bits(x.GateIdentityFraction) != BitConverter.DoubleToInt64Bits(y.GateIdentityFraction) ||
                    x.MatchedSubsequence != y.MatchedSubsequence || x.IsDecoy != y.IsDecoy)
                    return at + "hit " + h + " fields";
            }
        }
        return string.Empty;
    }
}
