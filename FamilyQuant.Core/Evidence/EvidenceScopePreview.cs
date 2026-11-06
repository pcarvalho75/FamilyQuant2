using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FamilyQuant.Evidence;

public sealed record ScopePreviewProtein(string Accession, string Name, ProteinEvidence? Evidence);

public sealed record ScopePreviewFamily(string Family, int Proteins, int Reviewed, int Unreviewed, int UnknownStatus,
    int Additional, IReadOnlyList<string> Examples)
{
    public string ExamplesText => string.Join(" | ", Examples);
}

public sealed record EvidenceScopePreview(IReadOnlyList<string> AddedTerms, int Proteins, int Reviewed, int Unreviewed,
    int UnknownStatus, int Additional, IReadOnlyList<ScopePreviewFamily> Families);

public static partial class EvidenceClassifier
{
    public static IReadOnlyList<ScopePreviewProtein> ScopePreviewSnapshot(IEnumerable<FastaItemExtended> proteins) => proteins
        .Where(protein => !NearestReviewed.IsDecoy(protein) && !IsContaminant(protein))
        .Select(protein => new ScopePreviewProtein(FamilyNaming.Accession(protein.SequenceIdentifier), FamilyNaming.ProteinName(protein.Description),
            protein.Evidence == null ? null : new ProteinEvidence
            {
                Accession = protein.Evidence.Accession, Family = protein.Evidence.Family,
                Reviewed = protein.Evidence.Accession == FamilyNaming.Accession(protein.SequenceIdentifier).Split('-')[0] ? protein.Evidence.Reviewed : null,
                Keywords = (protein.Evidence.Keywords ?? new List<string>()).ToList(),
                TissueSpecificity = (protein.Evidence.TissueSpecificity ?? new List<string>()).ToList(),
            })).ToArray();

    public static EvidenceScopePreview PreviewScopeChange(IReadOnlyList<ScopePreviewProtein> proteins, string baseline, string edited,
        CancellationToken cancellationToken = default)
    {
        var oldTerms = WordCluster.ParseKeywords(baseline);
        var oldSet = oldTerms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedTerms = WordCluster.ParseKeywords(edited).Where(term => !oldSet.Contains(term)).ToArray();
        var added = new ScopeMatcher(addedTerms);
        var previous = new ScopeMatcher(oldTerms);
        var matches = new List<(ScopePreviewProtein Protein, bool Additional)>();
        foreach (var protein in proteins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (added.Matches(protein.Evidence)) matches.Add((protein, !previous.Matches(protein.Evidence)));
        }
        var families = matches.GroupBy(match => match.Protein.Evidence?.Family ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ScopePreviewFamily(string.IsNullOrWhiteSpace(group.Key) ? "Family not recorded" : group.Key,
                group.Count(), group.Count(match => match.Protein.Evidence?.Reviewed == true),
                group.Count(match => match.Protein.Evidence?.Reviewed == false), group.Count(match => match.Protein.Evidence?.Reviewed == null),
                group.Count(match => match.Additional), group.Select(match => string.IsNullOrWhiteSpace(match.Protein.Name)
                    ? match.Protein.Accession : match.Protein.Name + " [" + match.Protein.Accession + "]")
                    .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).Take(3).ToArray()))
            .OrderByDescending(family => family.Additional).ThenByDescending(family => family.Proteins)
            .ThenBy(family => family.Family, StringComparer.Ordinal).ToArray();
        return new(addedTerms, matches.Count, matches.Count(match => match.Protein.Evidence?.Reviewed == true),
            matches.Count(match => match.Protein.Evidence?.Reviewed == false), matches.Count(match => match.Protein.Evidence?.Reviewed == null),
            matches.Count(match => match.Additional), families);
    }
}
