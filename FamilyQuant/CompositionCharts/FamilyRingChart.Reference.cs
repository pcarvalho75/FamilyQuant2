using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed record FamilyRingReference(int FamilyIndex, int PartCount);

public sealed partial class FamilyRingChart
{
    private static bool IsPartial(IReadOnlyList<FamilyRingGroup> families, IReadOnlyList<FamilyRingReference> reference) =>
        reference.Count < families.Count || reference.Any(r => r.PartCount < families[r.FamilyIndex].Parts.Count);

    private static IReadOnlyList<FamilyRingReference> BuildReference(IReadOnlyList<FamilyRingGroup> families)
    {
        if (families.Sum(f => 33 + 19 * f.Parts.Count) <= 520)
            return Array.AsReadOnly(families.Select((f, i) => new FamilyRingReference(i, f.Parts.Count)).ToArray());
        // Retain explicit Others and unassigned category labels, then the largest ordinary groups.
        var indices = Enumerable.Range(0, families.Count).OrderByDescending(i => families[i].IsOthers || families[i].IsUnassigned)
            .ThenByDescending(i => families[i].Percent).ThenBy(i => i).Take(8).OrderBy(i => i).ToArray();
        var counts = new int[indices.Length]; int used = 33 * indices.Length;
        bool added = true;
        while (added && used + 19 <= 480)
        {
            added = false;
            for (int i = 0; i < indices.Length && used + 19 <= 480; i++)
                if (counts[i] < Math.Min(FigureLayout.SubclassReferences, families[indices[i]].Parts.Count))
                { counts[i]++; used += 19; added = true; }
        }
        return Array.AsReadOnly(indices.Select((index, i) => new FamilyRingReference(index, counts[i])).ToArray());
    }
}
