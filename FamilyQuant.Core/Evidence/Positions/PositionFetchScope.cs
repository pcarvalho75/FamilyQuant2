using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Evidence.Positions;

/// <summary>Bounds remote position lookups to the proteins reached by the existing production
/// peptide mapper. Additional family IDs are an explicit user choice; no family is inferred here.</summary>
public static class PositionFetchScope
{
    public static IReadOnlyList<string> FromMappedPeptides(CurationHomologyHits? hits, FamilyClusterFile file,
        IReadOnlyCollection<int>? additionalFamilyIds = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        var mapped = hits == null ? new HashSet<string>(StringComparer.Ordinal)
            : hits.Identified.Concat(hits.DeNovo).SelectMany(h => h.Proteins).Distinct()
                .Select(i => hits.ProteinIds[i]).ToHashSet(StringComparer.Ordinal);
        return file.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)
                && (mapped.Contains(p.SequenceIdentifier) || additionalFamilyIds?.Contains(p.UserID) == true))
            .Select(p => FamilyNaming.Accession(p.SequenceIdentifier).Split('-')[0])
            .Where(FamilyNaming.IsUniProtAccession).Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal).ToArray();
    }
}
