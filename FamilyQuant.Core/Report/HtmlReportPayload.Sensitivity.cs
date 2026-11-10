using System;
using System.Collections.Generic;

namespace FamilyQuant.Report;

/// <summary>Exact native Sensitivity readings, with workbook-bound fields and program-prepared axes.</summary>
public sealed record HtmlSensitivityFigures
{
    public IReadOnlyList<HtmlSensitivityFigure> Figures { get; init; } = Array.Empty<HtmlSensitivityFigure>();
}

public sealed record HtmlSensitivityFigure(string Id, string Kind, string Title, string Legend,
    IReadOnlyList<HtmlSensitivityPanel> Panels);
public sealed record HtmlSensitivityPanel(string Kind, string Title, string Basis, string XLabel,
    string YLabel, double MinimumX, double MaximumX, double MaximumY, bool ReverseX,
    IReadOnlyList<HtmlSensitivityTick> XTicks, IReadOnlyList<HtmlSensitivityTick> YTicks,
    IReadOnlyList<HtmlSensitivityRow> Rows, IReadOnlyList<HtmlSensitivityMarker> Markers);
public sealed record HtmlSensitivityNumber(string Exact, string Display);
public sealed record HtmlSensitivityTick(double Position, string Label);
public sealed record HtmlSensitivityPart(string Label, HtmlSensitivityNumber Value);
public sealed record HtmlSensitivityRow(string Name, HtmlSensitivityNumber? X,
    IReadOnlyList<HtmlSensitivityPart> Parts, HtmlSensitivityNumber? Total, string? Note);
public sealed record HtmlSensitivityMarker(string Label, HtmlSensitivityNumber Identity);
