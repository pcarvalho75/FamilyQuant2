using System.Collections.Generic;

namespace FamilyQuant.Panels.Import;

/// <summary>Import rule versions recorded in FamilyPanelImport.ImportVersion. Version 0 is the J1b importer and
/// replays panels imported before 2.1.11 unchanged; version 1 trims keys and names and skips section-title and
/// repeated-header rows unless the user keeps them.</summary>
public static class PanelImportRules
{
    public const int Legacy = 0;
    public const int Graceful = 1;
    public const int Current = Graceful;
}

public enum PanelImportSeverity { Note, Blocking }

/// <summary>Append only: values are stored by number.</summary>
public enum PanelImportProblemKind
{
    FileUnreadable, SheetMissing, HeaderRowEmpty, DuplicateHeader, UnnamedColumn, FormulaWithoutResult, CellError,
    EmptyRow, SectionRow, RepeatedHeaderRow, ColumnNotChosen, ColumnMissing, PreviewMismatch, SourceChanged,
    MissingKey, MissingName, DuplicateKey, SeveralFamilies, DecisionNotMapped, DecisionMappingInvalid,
    MergeTargetMissing, MergeCycle, ColorNotNamed, ChoiceRowMissing, ChoiceInvalid, NoFamilies, PanelInvalid,
    PreviewTruncated,
}

/// <summary>One problem as a plain sentence. RowNumber is the worksheet row (0 when not about one row);
/// Column is the actual header (empty when not about one column).</summary>
public sealed class PanelImportProblem
{
    public PanelImportProblemKind Kind { get; set; }
    public PanelImportSeverity Severity { get; set; }
    public int RowNumber { get; set; }
    public List<int> OtherRowNumbers { get; set; } = new();
    public string Column { get; set; } = string.Empty;
    public string ColumnLetter { get; set; } = string.Empty;
    /// <summary>The cell text concerned, for example the repeated abbreviation or the unmapped decision label.</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>A proposal the user may accept or edit, for example a new abbreviation; empty when none.</summary>
    public string Suggestion { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>Append only: values are stored by number.</summary>
public enum FamilyPanelRowAction { Import, Skip, MergeInto, Split, ReportUnderOthers }

/// <summary>A user choice for one worksheet row, replayed with the panel. Key and Name replace the cell text when
/// nonempty. MergeInto keeps the row as a family reported under MergeTargetFamilyKey. Split creates one family per
/// part, using the row outcome's ProposedParts when Parts is empty; each part keeps the row's decision, colour and
/// original cells, and its Pfam text stays text only. ReportUnderOthers keeps one family with that decision.</summary>
public sealed class FamilyPanelRowChoice
{
    public int RowNumber { get; set; }
    public FamilyPanelRowAction Action { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MergeTargetFamilyKey { get; set; } = string.Empty;
    public List<FamilyPanelSplitPart> Parts { get; set; } = new();
}

public sealed class FamilyPanelSplitPart
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>The user's name for one fill colour of the colour-category column.</summary>
public sealed class FamilyPanelColorLabel
{
    public string Color { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>The raw grid of a sheet for the preview page.</summary>
public sealed class PanelSheetPreview
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public List<string> SheetNames { get; set; } = new();
    public List<string> HiddenSheetNames { get; set; } = new();
    public string SheetName { get; set; } = string.Empty;
    public int ColumnCount { get; set; }
    /// <summary>Every row from 1 to the last used row, dense, each with ColumnCount cells.</summary>
    public List<PanelPreviewRow> Rows { get; set; } = new();
    /// <summary>0 when no row looks like a header.</summary>
    public int ProposedHeaderRow { get; set; }
    public string HeaderReason { get; set; } = string.Empty;
    public bool IsTemplate { get; set; }
    public List<PanelImportProblem> Problems { get; set; } = new();
}

public sealed class PanelPreviewRow
{
    public int RowNumber { get; set; }
    public List<PanelPreviewCell> Cells { get; set; } = new();
}

public sealed class PanelPreviewCell
{
    public int Column { get; set; }
    public string ColumnLetter { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>#RRGGBB or empty.</summary>
    public string Fill { get; set; } = string.Empty;
    /// <summary>A plain colour word for screen readers and labels, for example "orange"; empty without a fill.</summary>
    public string FillName { get; set; } = string.Empty;
    public string MergeRange { get; set; } = string.Empty;
}

/// <summary>Proposed choices for a table; every one is shown to the user and can be changed.</summary>
public sealed class PanelImportProposal
{
    /// <summary>Sheet, header row, source hash, ImportVersion = Current and the proposed ColumnHeaders.</summary>
    public FamilyPanelImport Choices { get; set; } = new();
    /// <summary>Decision mappings; filled only for the program's own template, whose labels the program wrote.</summary>
    public List<FamilyPanelDecisionMapping> Decisions { get; set; } = new();
    /// <summary>Role key to a plain sentence explaining the proposal.</summary>
    public Dictionary<string, string> Reasons { get; set; } = new();
    /// <summary>Headers whose cells carry two or more different fill colours, offered as a category; never chosen automatically.</summary>
    public List<string> ColorColumns { get; set; } = new();
    public bool IsTemplate { get; set; }
}

/// <summary>The result of checking a table against the current choices.</summary>
public sealed class PanelImportCheck
{
    /// <summary>The detached panel, present only when no problem is blocking.</summary>
    public FamilyPanel? Panel { get; set; }
    public bool CanImport => Panel != null;
    public List<PanelImportProblem> Problems { get; set; } = new();
    public List<PanelImportRowOutcome> Rows { get; set; } = new();
    /// <summary>Distinct labels of the chosen decision column, in order of first appearance.</summary>
    public List<string> DecisionValues { get; set; } = new();
    /// <summary>Distinct fill colours of the colour-category column, in order of first appearance.</summary>
    public List<PanelImportColorUse> Colors { get; set; } = new();
}

/// <summary>What happens to one table row under the current choices.</summary>
public sealed class PanelImportRowOutcome
{
    public int RowNumber { get; set; }
    public FamilyPanelRowAction Action { get; set; }
    public bool ChosenByUser { get; set; }
    /// <summary>Keys of the families this row creates, empty when skipped or blocked.</summary>
    public List<string> FamilyKeys { get; set; } = new();
    /// <summary>For a row listing several families: the column holding the list and the names found in it.</summary>
    public string ListColumn { get; set; } = string.Empty;
    public List<FamilyPanelSplitPart> ProposedParts { get; set; } = new();
}

public sealed class PanelImportColorUse
{
    public string Color { get; set; } = string.Empty;
    public string ColorName { get; set; } = string.Empty;
    public List<int> RowNumbers { get; set; } = new();
    public string Label { get; set; } = string.Empty;
}

/// <summary>A proposed meaning for one decision label; Mapping is what Check would receive if the user accepts it.</summary>
public sealed class PanelDecisionProposal
{
    public string SourceValue { get; set; } = string.Empty;
    public List<int> RowNumbers { get; set; } = new();
    public FamilyPanelDecisionMapping Mapping { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}
