using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Evidence;

public sealed partial class PanelEvidenceMatching
{
    /// <summary>Explicit Pfam values in the user's family row precede inferred stored-name matches.</summary>
    public IReadOnlyList<PanelEvidenceSuggestion> SuggestForFamily(FamilyPanelFamily family)
    {
        var inferred = Suggest(family.Names.Select(name => name.Text).Prepend(family.DisplayName));
        var supplied = family.Pfam.Where(domain => !string.IsNullOrWhiteSpace(domain.Accession))
            .GroupBy(domain => Normalize(domain.Accession), StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .Select(domain =>
            {
                var coverage = Coverage(PanelEvidenceMatchKind.Pfam, domain.Accession);
                var ranked = inferred.FirstOrDefault(row => row.Key == coverage.Key);
                return coverage with { Name = string.IsNullOrWhiteSpace(domain.Name) ? coverage.Name : domain.Name,
                    Reason = "from your sheet", IsFromSheet = true, IsAdditional = false, NameCoverage = ranked?.NameCoverage ?? 0 };
            }).ToArray();
        var suppliedKeys = supplied.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        return supplied.Concat(inferred.Where(row => !suppliedKeys.Contains(row.Key))).ToArray();
    }
}
