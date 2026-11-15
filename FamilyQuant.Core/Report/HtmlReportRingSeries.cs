namespace FamilyQuant.Report;

/// <summary>Program-prepared percent fields for the family ring on one chosen denominator.</summary>
public sealed record HtmlRingBasis(string? Total, string? Outside, bool CanDraw, int RunCount, string? Unavailable = null)
{
    public string? OutsidePeptide { get; init; }
    public string? OutsideRest { get; init; }
}
