using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Panels.Import;

public enum PanelWorkbookSheetRole { FamilyList, ColorLegend, SubclassTable, Notes }
public enum PanelWorkbookReadState { NotRead, Read }

/// <summary>An additive whole-workbook reader. Existing single-sheet reader implementations remain valid.</summary>
public interface IPanelWorkbookReader : IPanelSheetPreviewReader
{
    PanelWorkbookRead ReadWorkbook(string path);
}

/// <summary>Read-only source facts from one unchanged workbook. Roles and decisions are proposals, never edits.</summary>
public sealed class PanelWorkbookRead
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public List<PanelWorkbookSheet> Sheets { get; set; } = new();
    public List<PanelImportProblem> Problems { get; set; } = new();
}

public sealed class PanelWorkbookSheet
{
    public string SheetName { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    public bool HasFilterHeader { get; set; }
    public PanelSheetPreview Preview { get; set; } = new();
    public PanelSpreadsheetTable? Table { get; set; }
    /// <summary>All nonempty source rows, including text above the proposed header and beyond the preview limit.</summary>
    public List<PanelPreviewRow> SourceRows { get; set; } = new();
    public PanelWorkbookSheetRole Role { get; set; } = PanelWorkbookSheetRole.Notes;
    public string RoleReason { get; set; } = string.Empty;
    public bool IsUncertain { get; set; }
}

public interface IFamilyPanelWorkbookImporter
{
    PanelWorkbookProposal ProposeWorkbook(string path);
    PanelWorkbookProposal ProposeWorkbook(PanelWorkbookRead workbook);
    PanelWorkbookProposal ProposeWorkbook(PanelWorkbookRead workbook, string? familySheet = null,
        IReadOnlyDictionary<string, PanelWorkbookSheetRole>? roleOverrides = null);
}

/// <summary>The single opening proposal consumed by the workbook summary. Unknown meanings stay unknown.</summary>
public sealed class PanelWorkbookProposal
{
    public PanelWorkbookRead Workbook { get; set; } = new();
    public string FamilySheet { get; set; } = string.Empty;
    public PanelImportProposal FamilyProposal { get; set; } = new();
    public List<PanelWorkbookColorHint> Colors { get; set; } = new();
    public PanelWorkbookReadState ColorsState { get; set; }
    public List<PanelWorkbookDecisionHint> Decisions { get; set; } = new();
    public PanelWorkbookReadState DecisionsState { get; set; }
    public List<PanelImportRowOutcome> MultiFamilyRows { get; set; } = new();
    public List<PanelWorkbookPfamHint> Pfam { get; set; } = new();
    public List<PanelWorkbookSubclassTable> SubclassTables { get; set; } = new();
    public List<PanelWorkbookSourceNote> Notes { get; set; } = new();
    public List<PanelImportProblem> Problems { get; set; } = new();
    public bool NeedsFamilySheetChoice { get; set; }
    public PanelSpreadsheetTable? FamilyTable => Workbook.Sheets.FirstOrDefault(sheet => sheet.SheetName == FamilySheet)?.Table;
}

public sealed class PanelWorkbookLegendEntry
{
    public string SheetName { get; set; } = string.Empty;
    public int RowNumber { get; set; }
    public string Color { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
}

public sealed class PanelWorkbookColorHint
{
    public string Color { get; set; } = string.Empty;
    /// <summary>One source name when unambiguous; empty when several legend entries disagree.</summary>
    public string Name { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
    public List<PanelWorkbookLegendEntry> Entries { get; set; } = new();
    public bool IsUncertain { get; set; }
}

public sealed class PanelWorkbookDecisionHint
{
    public string SourceValue { get; set; } = string.Empty;
    public List<int> RowNumbers { get; set; } = new();
    public string LegendMeaning { get; set; } = string.Empty;
    public List<PanelWorkbookLegendEntry> LegendEntries { get; set; } = new();
    /// <summary>Null rather than a guessed Quantify default when the label/meaning is not understood.</summary>
    public FamilyPanelDecisionMapping? ProposedMapping { get; set; }
    public bool IsUncertain => ProposedMapping == null;
    public bool ExactProgramDecision { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PanelWorkbookPfamHint
{
    public int RowNumber { get; set; }
    public string FamilyKey { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
    public List<FamilyPanelDomain> Domains { get; set; } = new();
}

/// <summary>Source facts for the subclass owner. Domain-column presence does not decide required versus absent.</summary>
public sealed class PanelWorkbookSubclassTable
{
    public string SheetName { get; set; } = string.Empty;
    public List<string> FamilyKeys { get; set; } = new();
    public string FamilyKeyColumn { get; set; } = string.Empty;
    public string ClassColumn { get; set; } = string.Empty;
    public List<string> DomainColumns { get; set; } = new();
    public PanelSpreadsheetTable Table { get; set; } = new();
}

public sealed class PanelWorkbookSourceNote
{
    public string SheetName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
