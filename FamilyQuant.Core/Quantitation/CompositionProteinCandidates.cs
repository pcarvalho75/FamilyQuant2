using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

/// <summary>Rejoins the complete compact production hit set to current curation entries. Compact hit
/// indices and curation protein indices are different index spaces and must not be interchanged.</summary>
internal sealed class CompositionProteinCandidates
{
    internal sealed record Candidate(int Index, FastaItemExtended Protein, bool AmbiguousIdentifier);
    private readonly Dictionary<string, List<Candidate>> proteins;
    private readonly Dictionary<string, string[]> identified;
    private readonly Dictionary<string, string[]> deNovo;

    internal CompositionProteinCandidates(CurationHomologyHits hits, FamilyClusterFile curation)
    {
        proteins = (curation.MyExtendedFastaItems ?? new()).Select((p, i) => (Protein: p, Index: i))
            .Where(p => p.Protein != null && !SequenceLabels.IsDecoy(p.Protein.SequenceIdentifier ?? string.Empty))
            .GroupBy(p => p.Protein.SequenceIdentifier ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => new Candidate(p.Index, p.Protein, g.Count() > 1)).ToList(), StringComparer.Ordinal);
        Dictionary<string, string[]> Map(List<(string Key, int[] Proteins)> source) => source
            .GroupBy(p => DeNovoReconciler.NormalizeSequence(p.Key), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.SelectMany(p => p.Proteins).Select(i => hits.ProteinIds[i])
                .Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        identified = Map(hits.Identified);
        deNovo = Map(hits.DeNovo);
    }

    internal IReadOnlyList<Candidate> Resolve(string side, string sequence, int family)
    {
        var map = string.Equals(side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal) ? identified : deNovo;
        if (!map.TryGetValue(DeNovoReconciler.NormalizeSequence(sequence), out var identifiers)) return Array.Empty<Candidate>();
        return identifiers.Where(proteins.ContainsKey).SelectMany(id => proteins[id])
            .Where(p => p.Protein.UserID == family).ToList();
    }
}
