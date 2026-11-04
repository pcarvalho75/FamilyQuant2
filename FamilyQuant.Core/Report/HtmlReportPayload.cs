using System;
using System.Collections.Generic;

namespace FamilyQuant.Report;

/// <summary>Compact offline view of the same selected report. Text is final display text;
/// Value is for geometry only. Sources bind fields to Word or native workbook cells.</summary>
public sealed record HtmlReportPayload
{
    public string Schema { get; init; } = "familyquant-report-html/v1";
    public string? Title { get; init; }
    public string? Authors { get; init; }
    public HtmlReportLinks Links { get; init; } = new();
    public IReadOnlyList<HtmlReportHighlight> Highlights { get; init; } = Array.Empty<HtmlReportHighlight>();
    public HtmlComposition Composition { get; init; } = new();
    public IReadOnlyList<HtmlRunCredit> CreditedRuns { get; init; } = Array.Empty<HtmlRunCredit>();
    public IReadOnlyList<HtmlGapRow> DatabaseGap { get; init; } = Array.Empty<HtmlGapRow>();
    public IReadOnlyList<HtmlMethodStep> Methods { get; init; } = Array.Empty<HtmlMethodStep>();
    public IReadOnlyList<HtmlReportFigure> Quality { get; init; } = Array.Empty<HtmlReportFigure>();
    public IReadOnlyList<HtmlReportFigure> Sensitivity { get; init; } = Array.Empty<HtmlReportFigure>();
    public string? Interpretation { get; init; }
    public IReadOnlyDictionary<string, HtmlReportField> Fields { get; init; }
        = new SortedDictionary<string, HtmlReportField>(StringComparer.Ordinal);
}

public sealed record HtmlReportLinks
{
    public string Word { get; init; } = "report.docx";
    public string Workbook { get; init; } = "report_supplement.xlsx";
}

public sealed record HtmlReportField(string Text, double? Value, string Unit, HtmlReportReference Source)
{
    /// <summary>Optional field ID for a program-rounded chart label. This field keeps exact text.</summary>
    public string? Short { get; init; }
}

/// <summary>Table/row/column are one-based, including the header row. Word tables count
/// only printed tables. Format and Scale describe existing display conversion, never analysis.</summary>
public sealed record HtmlReportReference
{
    public string Kind { get; init; } = "word-cell";
    public int? Table { get; init; }
    public int? Row { get; init; }
    public int? Column { get; init; }
    public string? Sheet { get; init; }
    public string? Property { get; init; }
    public string? Figure { get; init; }
    public string? Format { get; init; }
    public string? Family { get; init; }
    public string? Study { get; init; }
    public string? Run { get; init; }
    public double Scale { get; init; } = 1;
}

public sealed record HtmlComposition
{
    public string Basis { get; init; } = "Mean of per-run shares of credited family signal";
    public bool IsAdditive { get; init; } = true;
    public IReadOnlyList<HtmlReportFamily> Families { get; init; } = Array.Empty<HtmlReportFamily>();
}

/// <summary>Name, Mean and other field properties are keys into Payload.Fields, not display strings.</summary>
public sealed record HtmlReportFamily
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Mean { get; init; }
    public string? Sd { get; init; }
    public string? Cv { get; init; }
    public string? WholeMeasured { get; init; }
    public string? Proteins { get; init; }
    public string? UniqueProteins { get; init; }
    public string? Peptides { get; init; }
    public bool IsOthers { get; init; }
    public IReadOnlyList<HtmlFamilyRun> Runs { get; init; } = Array.Empty<HtmlFamilyRun>();
    public IReadOnlyList<HtmlFamilyPart> Parts { get; init; } = Array.Empty<HtmlFamilyPart>();
    public IReadOnlyList<HtmlReportCoverage> Coverage { get; init; } = Array.Empty<HtmlReportCoverage>();
}

public sealed record HtmlFamilyRun(string Name, string? Share, string? Measured);
public sealed record HtmlFamilyPart(string Kind, string Name, string? Share);
public sealed record HtmlRunCredit(string Name, string Share);
public sealed record HtmlGapRow(string Name, string Fraction);
public sealed record HtmlReportHighlight(string Name, string Value, string? Description);
/// <summary>Title, fact Name and fact Value are all field IDs.</summary>
public sealed record HtmlMethodStep(string Id, string Title, IReadOnlyList<HtmlReportFact> Facts);
public sealed record HtmlReportFact(string Name, string Value);
public sealed record HtmlReportFigure(string Title, string Caption, string Png, string SourceFigureId);

/// <summary>One-based inclusive stored coordinates, used only for geometry. Every label,
/// including hover text, is a field ID. Png is retained only when live geometry is unavailable.</summary>
public sealed record HtmlReportCoverage
{
    public string Title { get; init; } = "";
    public string Caption { get; init; } = "";
    public string SourceFigureId { get; init; } = "";
    public string? Png { get; init; }
    public string? Protein { get; init; }
    public string? LengthLabel { get; init; }
    public int? Length { get; init; }
    public IReadOnlyList<HtmlCoverageSpan> Peptides { get; init; } = Array.Empty<HtmlCoverageSpan>();
    public IReadOnlyList<HtmlCoverageSpan> Domains { get; init; } = Array.Empty<HtmlCoverageSpan>();
}
public sealed record HtmlCoverageSpan(int Start, int End, string Label, string Kind, string? Range = null);

/// <summary>Existing composition identity, captured by the prepared report producer. Not a
/// replacement for canonical membership or an instruction to calculate a missing study.</summary>
public sealed record HtmlReportContext
{
    public bool UsesPanel { get; init; }
    public bool IsIdentity { get; init; } = true;
    public bool IsAdditive { get; init; } = true;
    public IReadOnlyDictionary<string, string> FamilySections { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> PanelKeys { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
    public QuantManifest? StoredStudies { get; init; }
    public string FigureDetailsCsv { get; init; } = "";
}
