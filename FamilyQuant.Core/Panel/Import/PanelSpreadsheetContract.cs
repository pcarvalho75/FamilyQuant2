using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Panels.Import;

/// <summary>General role keys, never workbook headers. The user maps each role to a real header.
/// Every input column also remains verbatim in the family's named TextFields.</summary>
public static class PanelImportFields
{
    public const string FamilyKey = "family-key";
    public const string DisplayName = "display-name";
    public const string Decision = "decision";
    public const string Pfam = "pfam";
    /// <summary>A name in another language, stored in the family's Names under the actual header.</summary>
    public const string OtherName = "other-name";
    /// <summary>Recorded so the wizard and the template agree; the text stays in TextFields like every column.</summary>
    public const string Notes = "notes";
    /// <summary>The column whose cell fill colours the user names as a category. Optional.</summary>
    public const string ColorCategory = "color-category";

    /// <summary>The family's text in the column mapped to OtherName, or empty. Which language it is, is the user's choice.</summary>
    public static string OtherNameText(FamilyPanel panel, FamilyPanelFamily family) =>
        panel.Import?.ColumnHeaders.GetValueOrDefault(OtherName) is { Length: > 0 } header
            ? family.Names.FirstOrDefault(n => n.Name == header)?.Text ?? string.Empty : string.Empty;
}

public sealed class PanelSpreadsheetTable
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public int HeaderRowNumber { get; set; } = 1;
    /// <summary>The workbook carries the marker of the program's own template (PanelImportTemplate).</summary>
    public bool IsTemplate { get; set; }
    public List<string> Headers { get; set; } = new();
    /// <summary>Excel column letters, parallel to Headers.</summary>
    public List<string> ColumnLetters { get; set; } = new();
    public List<PanelSpreadsheetRow> Rows { get; set; } = new();
    /// <summary>Empty rows between the header row and the last row with text. Trailing formatted rows are not listed.</summary>
    public List<int> EmptyRows { get; set; } = new();
    /// <summary>Reading notes, for example a renamed duplicate header or a formula without a saved result.</summary>
    public List<PanelImportProblem> Problems { get; set; } = new();
}

public sealed class PanelSpreadsheetRow
{
    public int RowNumber { get; set; }
    /// <summary>Actual header to raw cell text, retaining accents, rich-text runs and line breaks.</summary>
    public Dictionary<string, string> Cells { get; set; } = new();
    /// <summary>Actual header to the cell's direct fill colour as #RRGGBB, for filled cells only. Display data, never a rule.</summary>
    public Dictionary<string, string> Fills { get; set; } = new();
    /// <summary>Actual header to the merged range holding the cell, for example A7:C7, for merged cells only.</summary>
    public Dictionary<string, string> Merges { get; set; } = new();
}

public interface IPanelSpreadsheetReader
{
    IReadOnlyList<string> ReadSheetNames(string path);
    /// <summary>Throws only when the file or sheet cannot be read; data issues are listed in Problems.</summary>
    PanelSpreadsheetTable ReadTable(string path, string sheetName, int headerRowNumber = 1);
}

public interface IPanelSheetPreviewReader : IPanelSpreadsheetReader
{
    /// <summary>The raw grid of one sheet (the first visible sheet when sheetName is empty) with fill colours,
    /// sheet names and a proposed header row. Never throws for an unreadable file: Problems says why.</summary>
    PanelSheetPreview ReadPreview(string path, string sheetName = "");
}

public interface IFamilyPanelSpreadsheetImporter
{
    /// <summary>Requires key and name header choices. The decision column is optional: without it every family is
    /// Quantify; with it every distinct label needs an explicit mapping. Pfam is optional. Throws
    /// InvalidDataException with the first blocking sentence from Check. Does not apply the panel to a document.</summary>
    FamilyPanel Import(PanelSpreadsheetTable table, FamilyPanelImport choices,
        IReadOnlyList<FamilyPanelDecisionMapping> decisions, string panelName = "");

    /// <summary>Reads the selected sheet and repeats the stored column, decision, row and colour choices; checks
    /// the source hash before replay. The source workbook is always opened read-only.</summary>
    FamilyPanel Replay(string path, FamilyPanel previous);

    /// <summary>Proposes the columns from the table's headers and values; the user confirms or changes each.
    /// The program's own template is recognised exactly and needs no mapping.</summary>
    PanelImportProposal Propose(PanelSpreadsheetTable table);

    /// <summary>One proposed meaning for each distinct label of the chosen decision column, with its rows and a reason.
    /// Empty when no decision column is chosen. Proposals only: the user confirms or changes each.</summary>
    IReadOnlyList<PanelDecisionProposal> ProposeDecisions(PanelSpreadsheetTable table, FamilyPanelImport choices);

    /// <summary>Never throws for data: lists every problem as a plain sentence naming the row and the column,
    /// every row's outcome, the decision values and colours to name, and the panel when nothing blocks.</summary>
    PanelImportCheck Check(PanelSpreadsheetTable table, FamilyPanelImport choices,
        IReadOnlyList<FamilyPanelDecisionMapping> decisions, string panelName = "");
}
