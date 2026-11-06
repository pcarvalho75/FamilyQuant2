using System.Collections.Generic;
using System.Linq;
using System;

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
    string Release, string Note = "", int TargetProteins = 0, int IgnoredContaminants = 0)
{
    public const string ScopeSuggestionExplanation = "The suggested scope uses the most frequent Molecular function keyword and the most frequent tissue phrase.";
    public static string ScopeExpansionHint => EvidenceHelp.ScopeExpansionHint;
    public static string DatabaseEntryCountDefinition => EvidenceHelp.DatabaseEntryCountDefinition;
    public static string ContaminantCountDefinition => EvidenceHelp.ContaminantCountDefinition;
    public static string KeywordCountExplanation => EvidenceHelp.KeywordCountExplanation;
    public string TaxaTotals => Taxa.Any(t => t.Entries == null || t.MarkedSp == null || t.MarkedTr == null)
        ? "Taxa totals unavailable"
        : $"Total entries {Taxa.Sum(t => t.Entries.GetValueOrDefault()):N0} | FASTA sp {Taxa.Sum(t => t.MarkedSp.GetValueOrDefault()):N0} | FASTA tr {Taxa.Sum(t => t.MarkedTr.GetValueOrDefault()):N0}";
}
