using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;

namespace FamilyQuant;

public static partial class FamilyCompositionStudy
{
    public static void StorePositionsFromCuration(QuantManifest manifest, FamilyPeptideAttribution attribution,
        CurationHomologyHits hits, FamilyClusterFile curation,
        SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(curation);
        var candidates = new CompositionProteinCandidates(hits, curation);
        IReadOnlyList<PeptideProteinPlacement> Resolve(string side, string sequence, int family)
        {
            string query = DeNovoReconciler.NormalizeSequence(sequence);
            var offsets = hits.LocalizationOffsets(side, query);
            var result = new List<PeptideProteinPlacement>();
            foreach (var candidate in candidates.Resolve(side, query, family))
            {
                string protein = candidate.Protein.Sequence ?? string.Empty;
                PeptideProteinPlacement Place(int? start, PeptideLocalizationMatchKind kind) => new()
                {
                    ProteinIdentifier = candidate.Protein.SequenceIdentifier, ProteinSequence = protein,
                    MatchKind = kind, Positions = candidate.Protein.Evidence?.Positions,
                    Intervals = start.HasValue ? new() { new() { Start = start.Value + 1, End = start.Value + query.Length } } : new(),
                };
                if (candidate.AmbiguousIdentifier || query.Length == 0)
                {
                    result.Add(Place(null, PeptideLocalizationMatchKind.Aligned));
                    continue;
                }
                string folded = protein.ToUpperInvariant().Replace('I', 'L');
                bool exact = false;
                for (int start = folded.IndexOf(query, StringComparison.Ordinal); start >= 0;
                    start = folded.IndexOf(query, start + 1, StringComparison.Ordinal))
                {
                    exact = true;
                    result.Add(Place(start, PeptideLocalizationMatchKind.ExactContainment));
                }
                if (!exact)
                    result.Add(Place(offsets.TryGetValue(candidate.Protein.SequenceIdentifier, out int offset)
                        && offset >= 0 && offset <= protein.Length - query.Length ? offset : null,
                        PeptideLocalizationMatchKind.Aligned));
            }
            return result;
        }
        FamilyPositionStudiesResult studies = BuildPositions(attribution, Resolve, policy);
        const string nativeMethod = " Native mapping retains every target hit: all literal I/L-folded containment positions, " +
            "otherwise the kept ungapped homology window marked aligned. Aligned localization is a mapping hypothesis, not exact containment.";
        studies.DomainParts.Method += nativeMethod;
        studies.Localization.Method += nativeMethod;
        manifest.FamilyDomainParts = studies.DomainParts;
        manifest.FamilyPeptideLocalization = studies.Localization;
    }
}
