using System.Collections.Generic;

namespace FamilyQuant;

// Presentation facts supplied by discovery. This type never infers a taxon or a scope.
public sealed record ReviewTaskTaxon(string Name, int TaxonomyId);
public sealed record ReviewTaskKeyword(string Keyword, int ReviewedProteins);
public sealed record EvidenceTaskScopeInput(string Clade, IReadOnlyList<ReviewTaskTaxon> Taxa,
    IReadOnlyList<ReviewTaskKeyword> Keywords, int ReviewedProteins, IReadOnlyList<string> ProposedScopeTerms,
    string Release, string Note = "", int TargetProteins = 0, int IgnoredContaminants = 0);
