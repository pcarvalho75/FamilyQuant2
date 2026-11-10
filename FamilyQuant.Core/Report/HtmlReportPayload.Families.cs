using System;
using System.Collections.Generic;

namespace FamilyQuant.Report;

/// <summary>Native Families plots. Text labels are final program text; numeric and row-name IDs bind
/// to Payload.Fields and the matching native-figure worksheets.</summary>
public sealed record HtmlFamilyFigures
{
    public IReadOnlyList<HtmlFamilyFigure> Figures { get; init; } = Array.Empty<HtmlFamilyFigure>();
}

public sealed record HtmlFamilyFigure(string Id, string Kind, string Title, bool UsesNormalization,
    IReadOnlyDictionary<string, HtmlFamilyFigureView> Views);
public sealed record HtmlFamilyFigureView(string Basis, string Legend, string XLabel, string YLabel,
    double MinimumX, double MaximumX, double MaximumY,
    IReadOnlyList<HtmlFamilyFigureTick> XTicks, IReadOnlyList<HtmlFamilyFigureTick> YTicks,
    IReadOnlyList<HtmlFamilyFigureRow> Rows);
public sealed record HtmlFamilyFigureNumber(string Exact, string Display);
public sealed record HtmlFamilyFigureTick(double Position, string Label);
public sealed record HtmlFamilyFigurePart(string Name, HtmlFamilyFigureNumber Value, bool Unresolved);
public sealed record HtmlFamilyFigureRow(string Key, string Name, HtmlFamilyFigureNumber? X,
    HtmlFamilyFigureNumber? Y, HtmlFamilyFigureNumber? Lower, HtmlFamilyFigureNumber? Upper,
    string RunText, IReadOnlyList<HtmlFamilyFigurePart> Parts);
