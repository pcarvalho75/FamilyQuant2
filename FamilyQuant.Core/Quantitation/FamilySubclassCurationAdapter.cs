using System;
using System.Linq;
using FamilyQuant.Panels;

namespace FamilyQuant;

public static partial class FamilyCompositionStudy
{
    /// <summary>Reads EV's complete protein-label list against all production mapping hits.</summary>
    public static bool StoreSubclassesFromCuration(QuantManifest manifest, FamilyPeptideAttribution attribution,
        CurationHomologyHits hits, FamilyClusterFile curation,
        SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique) =>
        StoreSubclassesFromCurationCore(manifest, attribution, hits, curation, policy);

    private static bool StoreSubclassesFromCurationCore(QuantManifest manifest, FamilyPeptideAttribution attribution,
        CurationHomologyHits hits, FamilyClusterFile curation, SharedPeptidePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(hits);
        if (!SubclassAssignment.IsCurrent(curation))
        {
            manifest.FamilySubclassParts = null;
            return false;
        }
        var candidates = new CompositionProteinCandidates(hits, curation);
        var labels = curation.ProteinSubclassLabels!.GroupBy(l => l.ProteinIndex)
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.Single() : null);
        var familyKeys = curation.ProteinSubclassLabels!.Where(l => l.FamilyUserId.HasValue && l.PanelFamilyKey != null)
            .GroupBy(l => l.FamilyUserId!.Value).ToDictionary(g => g.Key,
                g => g.Select(l => l.PanelFamilyKey!).Distinct(StringComparer.Ordinal).ToArray());
        var result = BuildSubclasses(attribution, (side, sequence, family) =>
        {
            var matched = candidates.Resolve(side, sequence, family);
            return matched.Select(candidate =>
            {
                if (candidate.AmbiguousIdentifier || !labels.TryGetValue(candidate.Index, out var label) || label == null
                    || !string.Equals(label.Identifier, candidate.Protein.SequenceIdentifier, StringComparison.Ordinal)
                    || label.FamilyUserId != family || label.Status != ProteinSubclassStatus.Assigned
                    || label.PanelFamilyKey == null || !familyKeys.TryGetValue(family, out var keys) || keys.Length != 1)
                    return null;
                return label.SubclassKey;
            }).ToArray();
        }, policy);
        foreach (var family in result.Families)
        {
            if (!familyKeys.TryGetValue(family.FamilyUserId, out var keys) || keys.Length != 1) continue;
            var definition = curation.FamilyPanel?.Families.FirstOrDefault(f => string.Equals(f.Key, keys[0], StringComparison.Ordinal));
            foreach (var part in family.Parts.Where(p => p.PartKey != null))
            {
                string? name = definition?.Subclasses.FirstOrDefault(s => string.Equals(s.Key, part.PartKey, StringComparison.Ordinal))?.DisplayName;
                if (!string.IsNullOrWhiteSpace(name)) part.Label = name;
            }
        }
        manifest.FamilySubclassParts = result;
        return true;
    }
}
