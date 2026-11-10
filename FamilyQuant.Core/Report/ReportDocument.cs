using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Report;

/// <summary>
/// WPF-free report interchange. Builders supply invariant text and the original Light-export
/// PNG bytes. Sections and blocks have stable ordinal ids for the stored export checkboxes.
/// Order is explicit in Sections and Blocks; captions are supplied by the caption builders.
/// </summary>
public sealed record ReportDocument
{
    public string Title { get; init; } = "Analysis report";
    public string Authors { get; init; } = string.Empty;
    public string ApplicationVersion { get; init; } = string.Empty;
    // Never use the export clock. The builder can use the saved project's creation/update dates.
    public DateTime CreatedUtc { get; init; } = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public DateTime ModifiedUtc { get; init; } = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public IReadOnlyList<ReportSection> Sections { get; init; } = Array.Empty<ReportSection>();
}

public sealed record ReportSection(string Id, string Title, IReadOnlyList<ReportBlock> Blocks);
public abstract record ReportBlock(string Id);
public sealed record ReportParagraph(string Id, string Text) : ReportBlock(Id)
{
    /// <summary>Word keeps this paragraph on the page of the block that follows it.</summary>
    public bool KeepWithNext { get; init; }
    /// <summary>Word writes no space after this paragraph, so consecutive items read as one list.</summary>
    public bool Compact { get; init; }
}

/// <summary>Cells contain the exact invariant strings used by the program's corresponding export.</summary>
public sealed record ReportTable(string Id, string Title, IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows, string Caption) : ReportBlock(Id)
{
    /// <summary>Full detail retained in the companion workbook, with no quantitative Word rendering.</summary>
    public bool SupplementOnly { get; init; }
    /// <summary>Optional native export behind rounded display columns. Only the workbook consumes
    /// this recipe; Word and the existing CSV exports retain their original literal cells.</summary>
    [JsonIgnore] public ReportSupplementSource? SupplementSource { get; init; }
    /// <summary>Structured list cells, retained separately from the unchanged native CSV text.</summary>
    [JsonIgnore] public IReadOnlyList<ReportListColumn> ListColumns { get; init; } = Array.Empty<ReportListColumn>();
}

public sealed record ReportListColumn(int ColumnIndex, string Name, IReadOnlyList<IReadOnlyList<string>> Items,
    IReadOnlyList<string>? RowKeys = null);

public sealed record ReportSupplementSource(string Csv, IReadOnlyList<string> Columns);

/// <summary>Original PNG export, never transcoded by the document writer. Size controls layout only.</summary>
public sealed record ReportFigure(string Id, string Title, byte[] PngBytes, string Caption,
    int PixelWidth, int PixelHeight) : ReportBlock(Id)
{
    public HtmlSignalChart? SignalChart { get; init; } // U3a: native readings retained beside the Word picture.
}

/// <summary>
/// Top-level order: title, contents, summary, summary of findings, methods, results, one section per panel family,
/// quality, sensitivity, Database gap, interpretation, supplements. Dynamic family ids append
/// the panel's stable key to FamilyPrefix, with no display name or position in the id.
/// </summary>
public static class ReportSectionIds
{
    public const string Title = "title";
    public const string Contents = "contents";
    public const string Summary = "summary";
    public const string Findings = "findings";
    public const string Methods = "methods";
    public const string Results = "results";
    public const string AssignmentConflicts = "assignment-conflicts";
    public const string FamilyPrefix = "family:";
    public const string Quality = "quality";
    public const string Sensitivity = "sensitivity";
    public const string DatabaseGap = "database-gap";
    public const string Interpretation = "interpretation";
    public const string Supplements = "supplements";
}

/// <summary>
/// Null ExcludedIds means include everything. Consumers persist unchecked section/block ids;
/// a section excludes all its blocks, while excluding one block removes only that block.
/// Unknown ids are retained by the project so later available figures keep the person's choice.
/// </summary>
public sealed record ReportSelection
{
    public IReadOnlyList<string>? ExcludedIds { get; init; }
}
