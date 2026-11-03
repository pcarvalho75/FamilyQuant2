using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;

namespace FamilyQuant;

public sealed partial class CurationHomologyHits
{
    // One extra integer per kept hit, sharing the existing compact protein-index array. Full
    // HomologyHit objects and their database subsequences need not be retained for localization.
    private readonly Dictionary<(string Side, string Sequence), (int[] Proteins, int[] Offsets)> localizationOffsets = new();

    internal void RecordLocalizationOffsets(string side, string sequence, int[] proteins, int[] offsets)
    {
        if (proteins.Length != offsets.Length) throw new ArgumentException("Protein and offset counts differ.");
        localizationOffsets[(side, DeNovoReconciler.NormalizeSequence(sequence))] = (proteins, offsets);
    }

    internal IReadOnlyDictionary<string, int> LocalizationOffsets(string side, string sequence)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!localizationOffsets.TryGetValue((side, DeNovoReconciler.NormalizeSequence(sequence)), out var rows)) return result;
        for (int i = 0; i < rows.Proteins.Length; i++) result.TryAdd(ProteinIds[rows.Proteins[i]], rows.Offsets[i]);
        return result;
    }
}
