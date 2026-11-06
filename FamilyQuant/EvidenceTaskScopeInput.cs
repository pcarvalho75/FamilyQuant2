using System.Collections.Generic;
using System.Linq;
using System;
using System.Globalization;

namespace FamilyQuant;

// Presentation facts supplied by discovery. This type never infers a taxon or a scope.
public sealed record ReviewTaskTaxon(string Name, int TaxonomyId, int? Entries = null, int? MarkedSp = null, int? MarkedTr = null);
public sealed record ReviewTaskKeyword(string Keyword, int ReviewedProteins, string CategoryName = "", string CategoryId = "")
{
    public string CategoryDisplay => string.IsNullOrWhiteSpace(CategoryName) ? "Unavailable (fetch evidence)" : CategoryName;
    public bool IsMolecularFunction => CategoryId == Evidence.EvidenceTaskDiscovery.FunctionalScopeCategoryId
        || string.IsNullOrEmpty(CategoryId) && string.Equals(CategoryName, "Molecular function", StringComparison.OrdinalIgnoreCase);
}
public sealed record EvidenceTaskScopeInput(string Clade, IReadOnlyList<ReviewTaskTaxon> Taxa,
    IReadOnlyList<ReviewTaskKeyword> Keywords, int ReviewedProteins, IReadOnlyList<string> ProposedScopeTerms,
    string Release, string Note = "", int TargetProteins = 0, int IgnoredContaminants = 0,
    int? FastaSpEntries = null, string ReviewStatusRelease = "", IReadOnlyList<string>? FastaTrNowReviewed = null,
    IReadOnlyList<string>? FastaSpNowUnreviewed = null, IReadOnlyList<string>? FastaSpUnresolved = null, IReadOnlyList<string>? RepeatedSpAccessions = null)
{
    public const string ScopeSuggestionExplanation = "The suggested scope uses the most frequent Molecular function keyword and the most frequent tissue phrase.";
    public static string ScopeExpansionHint => EvidenceHelp.ScopeExpansionHint;
    public static string DatabaseEntryCountDefinition => EvidenceHelp.DatabaseEntryCountDefinition;
    public static string ContaminantCountDefinition => EvidenceHelp.ContaminantCountDefinition;
    public static string KeywordCountExplanation => EvidenceHelp.KeywordCountExplanation;
    public string ReviewedCountText => string.Format(CultureInfo.CurrentCulture, "Reviewed in UniProt (release {0}): {1:N0}",
        string.IsNullOrWhiteSpace(ReviewStatusRelease) ? "unavailable" : ReviewStatusRelease, ReviewedProteins);
    public string FastaSpCountText => FastaSpEntries.HasValue
        ? "Marked sp in the FASTA: " + FastaSpEntries.Value.ToString("N0", CultureInfo.CurrentCulture) : "Marked sp in the FASTA: unavailable";
    public string ReviewDifferenceTooltip
    {
        get
        {
            if (!FastaSpEntries.HasValue) return "Read database and scope to compare FASTA prefixes with the stored review status.";
            static string Values(IReadOnlyList<string>? values) => values?.Count > 0 ? string.Join(", ", values) : "none";
            string result = "tr now reviewed: " + Values(FastaTrNowReviewed) + "\nsp no longer reviewed: " + Values(FastaSpNowUnreviewed);
            if (FastaSpUnresolved?.Count > 0) result += "\nsp with unresolved status: " + Values(FastaSpUnresolved);
            if (RepeatedSpAccessions?.Count > 0) result += "\nRepeated or isoform sp accessions: " + Values(RepeatedSpAccessions);
            return result;
        }
    }
    public string TaxaTotals => Taxa.Any(t => t.Entries == null || t.MarkedSp == null || t.MarkedTr == null)
        ? "Taxa totals unavailable"
        : $"Total entries {Taxa.Sum(t => t.Entries.GetValueOrDefault()):N0} | FASTA sp {Taxa.Sum(t => t.MarkedSp.GetValueOrDefault()):N0} | FASTA tr {Taxa.Sum(t => t.MarkedTr.GetValueOrDefault()):N0}";
}
