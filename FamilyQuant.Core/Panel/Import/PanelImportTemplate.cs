using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Panels.Import;

/// <summary>An empty family-list workbook with the recommended columns and an Instructions sheet. A hidden defined
/// name marks it, so the importer maps its columns and the program's own decision names with no user mapping.</summary>
public static class PanelImportTemplate
{
    public const string FamilySheet = "Families";
    public const string InstructionsSheet = "Instructions";
    public const string Abbreviation = "Abbreviation";
    public const string Name = "Name";
    public const string OtherName = "Other-language name";
    public const string Notes = "Notes";
    public const string Pfam = "Pfam domains";
    public const string Decision = "Decision";
    internal const string Marker = "FamilyQuant_family_list_template";
    internal const string MarkerValue = "\"1\"";
    private const int DecisionRows = 2000;

    /// <summary>Template header to importer role, in column order.</summary>
    public static IReadOnlyList<(string Header, string Role)> Columns { get; } = new[]
    {
        (Abbreviation, PanelImportFields.FamilyKey), (Name, PanelImportFields.DisplayName), (OtherName, PanelImportFields.OtherName),
        (Notes, PanelImportFields.Notes), (Pfam, PanelImportFields.Pfam), (Decision, PanelImportFields.Decision),
    };

    /// <summary>The program's decision names, also recognised in any sheet; a merge is written "Merge into" and an abbreviation.</summary>
    public static IReadOnlyList<(string Label, FamilyPanelDecision Decision)> DecisionNames { get; } = new[]
    {
        ("Quantify", FamilyPanelDecision.Quantify), ("Report under others", FamilyPanelDecision.ReportUnderOthers),
        ("Not a family", FamilyPanelDecision.NotAFamily),
    };
    public const string MergePrefix = "Merge into ";

    private static readonly string[] Instructions =
    {
        "How to fill in this family list",
        "",
        "Write one family per row on the Families sheet, starting in row 2. Keep the names in row 1 as they are, so FamilyQuant reads the columns without asking.",
        "",
        "Abbreviation: a short code that names the family, for example PLA2. Each abbreviation must be used once; it is the family's key in FamilyQuant.",
        "Name: the family name used in tables and reports.",
        "Other-language name: the same family in another language, for example English when Name is in Portuguese. Optional.",
        "Notes: anything you want to keep with the family. It is kept as text and never changes a result. Optional.",
        "Pfam domains: optional, written as Name (accession), separated by semicolons, for example Reprolysin (PF01421); Disintegrin (PF00200).",
        "Decision: optional. Leave it empty to quantify the family, or choose Quantify, Report under others or Not a family, or write Merge into followed by another row's abbreviation, for example Merge into CTL.",
        "",
        "A row that lists several families in one name cell, separated by commas, semicolons or line breaks, is offered as one family per name or as one group reported under others when you import it.",
        "You may colour cells and add more columns: colours can be named as a category during the import, and every extra column is kept as text.",
        "Save the workbook as .xlsx, then use File > Import family panel in FamilyQuant.",
        "",
        "Example rows",
    };

    private static readonly string[][] Example =
    {
        new[] { Abbreviation, Name, OtherName, Notes, Pfam, Decision },
        new[] { "PLA2", "Fosfolipase A2", "Phospholipase A2", "Main family", "Phospholip_A2_1 (PF00068)", "Quantify" },
        new[] { "CTL", "Lectina tipo C", "C-type lectin", "", "Lectin_C (PF00059)", "" },
        new[] { "Gal-CTL", "Lectina ligante de galactose", "Galactose-binding lectin", "Reported with CTL", "Lectin_C (PF00059)", "Merge into CTL" },
        new[] { "Rare", "Outras raras", "Endopeptidase, carboxypeptidase, TCTP", "Offered as one family per name or one group", "", "Report under others" },
    };

    public static void Write(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var styles = workbook.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(
            new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 14 }, new FontName { Val = "Calibri" })),
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFD9E1F2" }) { PatternType = PatternValues.Solid })),
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(new CellFormat(),
                new CellFormat { FontId = 1, FillId = 2, ApplyFont = true, ApplyFill = true },
                new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top }) { ApplyAlignment = true },
                new CellFormat { FontId = 2, ApplyFont = true }),
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));
        var sheets = workbook.Workbook.AppendChild(new Sheets());

        var families = new SheetData(Row(1, Columns.Select(c => c.Header).ToArray(), 1));
        var familySheet = new Worksheet(
            new SheetViews(new SheetView(new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen }) { WorkbookViewId = 0 }),
            new Columns(Width(1, 16), Width(2, 32), Width(3, 32), Width(4, 40), Width(5, 44), Width(6, 24)),
            families,
            new AutoFilter { Reference = "A1:F1" },
            new DataValidations(new DataValidation(new Formula1("\"" + string.Join(",", DecisionNames.Select(d => d.Label)) + "\""))
            {
                Type = DataValidationValues.List, AllowBlank = true, ShowErrorMessage = false, ShowInputMessage = true,
                PromptTitle = "Decision", Prompt = "Empty or Quantify, Report under others, Not a family, or type Merge into and an abbreviation.",
                SequenceOfReferences = new ListValue<StringValue> { InnerText = "F2:F" + DecisionRows.ToString(CultureInfo.InvariantCulture) },
            }) { Count = 1 });
        AddSheet(workbook, sheets, FamilySheet, 1, familySheet);

        var instructions = new SheetData();
        uint row = 1;
        foreach (string line in Instructions)
        {
            uint style = row == 1 ? 3u : line == "Example rows" ? 1u : 2u;
            instructions.Append(Row(row++, new[] { line }, style));
        }
        foreach (var example in Example) instructions.Append(Row(row++, example, example[0] == Abbreviation ? 1u : 2u));
        AddSheet(workbook, sheets, InstructionsSheet, 2, new Worksheet(new Columns(Width(1, 120)), instructions));

        workbook.Workbook.Append(new DefinedNames(new DefinedName { Name = Marker, Hidden = true, Text = MarkerValue }));
        workbook.Workbook.Save();
    }

    internal static bool IsMarked(WorkbookPart workbook) => workbook.Workbook?.DefinedNames?.Elements<DefinedName>()
        .Any(d => d.Name?.Value == Marker && d.Text == MarkerValue) == true;

    private static Column Width(uint column, double width) => new() { Min = column, Max = column, Width = width, CustomWidth = true };

    private static void AddSheet(WorkbookPart workbook, Sheets sheets, string name, uint id, Worksheet worksheet)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = worksheet;
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id, Name = name });
    }

    private static Row Row(uint number, string[] values, uint style) => new(values.Select((value, i) =>
        new Cell(new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }))
        {
            CellReference = PanelSpreadsheetReader.ColumnLetter(i + 1) + number.ToString(CultureInfo.InvariantCulture),
            DataType = CellValues.InlineString, StyleIndex = style,
        })) { RowIndex = number };
}
