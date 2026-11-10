using System;
using System.Collections.Generic;

namespace FamilyQuant.Report;

/// <summary>Program-computed native chart readings. The page only positions marks and displays
/// the supplied text; it never derives a signal share, a pooled total or an uplift.</summary>
public sealed record HtmlSignalChart
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string Legend { get; init; } = "";
    public string Basis { get; init; } = "";
    public double Maximum { get; init; } = 100;
    public string? IsotopeLabel { get; init; }
    public IReadOnlyList<HtmlSignalRow> Rows { get; init; } = Array.Empty<HtmlSignalRow>();
    public IReadOnlyList<HtmlSignalValue> Notes { get; init; } = Array.Empty<HtmlSignalValue>();
}

public sealed record HtmlSignalRow
{
    public string Name { get; init; } = "";
    public bool IsPooled { get; init; }
    public IReadOnlyList<HtmlSignalValue> Parts { get; init; } = Array.Empty<HtmlSignalValue>();
    public IReadOnlyList<HtmlSignalValue> Details { get; init; } = Array.Empty<HtmlSignalValue>();
    public HtmlSignalValue? Total { get; init; }
}

public sealed record HtmlSignalValue
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public double? Value { get; init; }
    public string Text { get; init; } = "not available";
    public string Exact { get; init; } = "not available";
    public string Unit { get; init; } = "";
    public string Basis { get; init; } = "";
    public double? Area { get; init; }
    public string? AreaText { get; init; }
    public double? Reference { get; init; }
    public string? ReferenceText { get; init; }
    public string? UnavailableReason { get; init; }
    public HtmlSignalValue? FamilyShare { get; init; } // U5: an additional, explicitly named family denominator.
}
