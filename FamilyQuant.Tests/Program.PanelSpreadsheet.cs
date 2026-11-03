using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void PanelSpreadsheetTests(string workDir)
    {
        Directory.CreateDirectory(workDir);
        string path = Path.Combine(workDir, "panel.xlsx");
        using (var book = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
        {
            var main = book.AddWorkbookPart(); main.Workbook = new Workbook();
            var strings = main.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new SharedStringTable(new SharedStringItem(new Run(new Text("linha um\n")), new Run(new Text("linha dois"))));
            var part = main.AddNewPart<WorksheetPart>();
            part.Worksheet = new Worksheet(new SheetData(
                new Row(Cell("A1", "User title")),
                new Row(Cell("A2", "Decisão"), Cell("B2", "Rótulo"), Cell("C2", "Chave"), Cell("D2", "Domínios"), Cell("E2", "Notas")) { RowIndex = 2 },
                new Row(Cell("A3", "include supplied"), Cell("B3", "Primeira família"), Cell("C3", "one"), Cell("D3", "source name (PF12345), PF23456"),
                    new Cell(new CellValue("0")) { CellReference = "E3", DataType = CellValues.SharedString }) { RowIndex = 3 },
                new Row(Cell("A4", "join supplied"), Cell("B4", "Segunda família"), Cell("C4", "two"), Cell("D4", "domain name without an accession")) { RowIndex = 4 }),
                new MergeCells(new MergeCell { Reference = "E3:E4" }));
            main.Workbook.Append(new Sheets(new Sheet { Id = main.GetIdOfPart(part), SheetId = 1, Name = "User panel" }));
        }
        string original = TestFileHash.Upper(path);
        var reader = new PanelSpreadsheetReader(); var importer = new FamilyPanelSpreadsheetImporter();
        var table = reader.ReadTable(path, "User panel", 2);
        Check("J1b: workbook tabs and a chosen header row are read as supplied", reader.ReadSheetNames(path).Single() == "User panel"
            && table.HeaderRowNumber == 2 && table.Headers[0] == "Decisão" && table.Rows.Count == 2);
        Check("J1b: shared rich text, newlines and merged notes are preserved", table.Rows.All(r => r.Cells["Notas"] == "linha um\nlinha dois"));
        var options = new FamilyPanelImport { FamilySheet = table.SheetName, HeaderRowNumber = 2, ColumnHeaders = new()
        {
            [PanelImportFields.FamilyKey] = "Chave", [PanelImportFields.DisplayName] = "Rótulo",
            [PanelImportFields.Decision] = "Decisão", [PanelImportFields.Pfam] = "Domínios",
        }};
        var decisions = new[]
        {
            new FamilyPanelDecisionMapping { SourceValue = "include supplied", Decision = FamilyPanelDecision.Quantify },
            new FamilyPanelDecisionMapping { SourceValue = "join supplied", Decision = FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey = "one" },
        };
        var panel = importer.Import(table, options, decisions, "User panel");
        Check("J1b: roles use chosen headers rather than positions or workbook vocabulary", panel.Families[0] is { Key: "one", DisplayName: "Primeira família" }
            && panel.Families[1].MergeTargetFamilyKey == "one" && panel.Import?.ColumnHeaders[PanelImportFields.FamilyKey] == "Chave");
        Check("J1b: domain accessions are syntax-based and all original text remains available", panel.Families[0].Pfam.Select(d => d.Accession).SequenceEqual(new[] { "PF12345", "PF23456" })
            && panel.Families[0].Pfam[0].Name == "source name" && panel.Families[0].Pfam[1].Name == string.Empty
            && panel.Families[1].Pfam.Count == 0 && panel.Families[0].TextFields.Count == table.Headers.Count
            && panel.Families[0].TextFields.Single(t => t.Name == "Notas").Text == "linha um\nlinha dois");
        Check("J1b: an unmapped decision is refused instead of defaulted", Throws<InvalidDataException>(() => importer.Import(table, options, decisions.Take(1).ToArray())));
        var invalid = new[] { decisions[0], new FamilyPanelDecisionMapping { SourceValue = "join supplied", Decision = FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey = "missing" } };
        Check("J1b: merge targets must name a real panel family", Throws<InvalidDataException>(() => importer.Import(table, options, invalid)));
        var cycle = new[] { new FamilyPanelDecisionMapping { SourceValue = "include supplied", Decision = FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey = "two" }, decisions[1] };
        Check("J1b: merge cycles are refused", Throws<InvalidDataException>(() => importer.Import(table, options, cycle)));
        Check("J1b: stored import choices replay exactly", JsonSerializer.Serialize(panel) == JsonSerializer.Serialize(importer.Replay(path, panel)));
        var file = new FamilyClusterFile { FamilyPanel = panel, EvidenceRecipe = new Evidence.EvidenceRecipe { FamilyPanel = panel } };
        string project = Path.Combine(workDir, "panel.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(project).fcf;
        Check("J1b: project and recipe retain header row, columns, decisions and exact source text", JsonSerializer.Serialize(panel) == JsonSerializer.Serialize(reopened.FamilyPanel)
            && JsonSerializer.Serialize(panel) == JsonSerializer.Serialize(reopened.EvidenceRecipe?.FamilyPanel));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); string portuguese = JsonSerializer.Serialize(importer.Replay(path, panel));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("J1b: import/replay bytes do not depend on display culture", portuguese == JsonSerializer.Serialize(importer.Replay(path, panel)));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Check("J1b: reading and replaying never changes the input workbook", original == TestFileHash.Upper(path));
        table.Rows[1].Cells["Chave"] = "one";
        Check("J1b: duplicate family keys are refused", Throws<InvalidDataException>(() => importer.Import(table, options, decisions)));
        var staleOptions = JsonSerializer.Deserialize<FamilyPanelImport>(JsonSerializer.Serialize(options))!;
        staleOptions.SourceSha256 = new string('0', 64);
        Check("J1b: a changed source hash cannot silently replay old choices", Throws<InvalidDataException>(() => importer.Import(table, staleOptions, decisions)));

        static Cell Cell(string address, string text) => new(new InlineString(new Text(text))) { CellReference = address, DataType = CellValues.InlineString };
    }
}
