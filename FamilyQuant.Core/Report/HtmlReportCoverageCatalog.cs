using System;
using System.Collections.Generic;

namespace FamilyQuant.Report;

/// <summary>Each protein map is stored once. Family views, including Others, refer to its index.</summary>
public sealed record HtmlCoverageCatalog
{
    public IReadOnlyList<HtmlCoverageCatalogFamily> Families { get; init; } = Array.Empty<HtmlCoverageCatalogFamily>();
    public IReadOnlyList<HtmlCoverageCatalogProtein> Proteins { get; init; } = Array.Empty<HtmlCoverageCatalogProtein>();
}

public sealed record HtmlCoverageCatalogFamily(string Id, string Name, string SelectorLabel,
    IReadOnlyList<int> Proteins, string? ReportFamilyId = null)
{
    public IReadOnlyList<HtmlFamilyPart> Parts { get; init; } = Array.Empty<HtmlFamilyPart>();
}
public sealed record HtmlCoverageCatalogProtein(int FamilyUserId, string Protein, string SelectorLabel, int Length,
    int PeptideCount, double CreditedSignal, IReadOnlyList<HtmlCoverageCatalogSpan> Peptides,
    IReadOnlyList<HtmlCoverageCatalogSpan> Domains);
public sealed record HtmlCoverageCatalogSpan(int Start, int End, string Label, string Kind, string? Side = null);
