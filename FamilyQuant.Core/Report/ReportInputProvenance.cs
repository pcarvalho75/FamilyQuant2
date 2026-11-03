using System;
using System.Collections.Generic;

namespace FamilyQuant.Report;

/// <summary>Read-only source facts captured from chosen input files, never supplied by report prose.
/// Empty names or an absent parameter hash mean unavailable, not an inferred workflow or search setting.</summary>
public sealed record ReportInputProvenance
{
    public string DatabaseFileName { get; init; } = string.Empty;
    public string SeproFileName { get; init; } = string.Empty;
    public string SearchParameterFileName { get; init; } = string.Empty;
    /// <summary>SHA-256 of the original search parameter file bytes, before parsing.</summary>
    public string SearchParameterSha256 { get; init; } = string.Empty;
    /// <summary>Parsed key/value settings in source order; duplicate keys are preserved.</summary>
    public IReadOnlyList<ReportSearchParameter> SearchParameters { get; init; } = Array.Empty<ReportSearchParameter>();
}

public sealed record ReportSearchParameter(string Key, string Value);
