using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Complete stored census behind the bounded report drawing, without re-attributing sequences.</summary>
public static class ReportFamilySourceSequences
{
    public static ReportTable Build(FamilySourceSequenceSummary summary) => new("family-source-sequences", "Complete family source sequence census",
        new[] { "Family ID", "Full family name", "Search distinct sequences", "PEAKS de-novo-only exclusive", "Shared exact sequence", "DeepNovo exclusive", "De novo union" },
        summary.Families.Select(row => (IReadOnlyList<string>)new[]
        {
            row.FamilyUserId.ToString(CultureInfo.InvariantCulture), row.Description,
            row.SearchDistinctSequences.ToString(CultureInfo.InvariantCulture),
            row.PeaksDeNovoOnlyExclusiveDistinctSequences.ToString(CultureInfo.InvariantCulture),
            row.SharedPeaksDeepNovoDistinctSequences.ToString(CultureInfo.InvariantCulture),
            row.DeepNovoExclusiveDistinctSequences.ToString(CultureInfo.InvariantCulture),
            row.DeNovoUnionDistinctSequences.ToString(CultureInfo.InvariantCulture),
        }).ToArray(), "All stored families, including those omitted from the bounded drawing. Counting grain: "
            + FamilySourceSequenceSummary.CountingGrain + ". Accepted tier: " + FamilySourceSequenceSummary.AcceptedTier)
        { SupplementOnly = true };
}
