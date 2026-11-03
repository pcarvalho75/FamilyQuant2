using System.Collections.Generic;

namespace FamilyQuant.Panels.Import;

/// <summary>General role keys, never workbook headers. The user maps each role to a real header.
/// Every input column also remains verbatim in the family's named TextFields.</summary>
public static class PanelImportFields
{
    public const string FamilyKey = "family-key";
    public const string DisplayName = "display-name";
    public const string Decision = "decision";
    public const string Pfam = "pfam";
}

public sealed class PanelSpreadsheetTable
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public int HeaderRowNumber { get; set; } = 1;
    public List<string> Headers { get; set; } = new();
    public List<PanelSpreadsheetRow> Rows { get; set; } = new();
}

public sealed class PanelSpreadsheetRow
{
    public int RowNumber { get; set; }
    /// <summary>Actual header to raw cell text, retaining accents, rich-text runs and line breaks.</summary>
    public Dictionary<string, string> Cells { get; set; } = new();
}

public interface IPanelSpreadsheetReader
{
    IReadOnlyList<string> ReadSheetNames(string path);
    PanelSpreadsheetTable ReadTable(string path, string sheetName, int headerRowNumber = 1);
}

public interface IFamilyPanelSpreadsheetImporter
{
    /// <summary>Requires key/name/decision header choices and an explicit mapping for every distinct
    /// decision label. Pfam is optional. Refuses ambiguous headers, duplicate keys, missing decisions,
    /// invalid merge targets and cycles. Does not apply the returned panel to a document.</summary>
    FamilyPanel Import(PanelSpreadsheetTable table, FamilyPanelImport choices,
        IReadOnlyList<FamilyPanelDecisionMapping> decisions, string panelName = "");

    /// <summary>Reads the selected sheet and repeats the stored column and decision choices; checks
    /// the source hash before replay. The source workbook is always opened read-only.</summary>
    FamilyPanel Replay(string path, FamilyPanel previous);
}
