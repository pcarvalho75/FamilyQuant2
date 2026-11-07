using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelImportExampleCommandTable() =>
    [
        new("--test-panel-import-example", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-panel-import-example", (_, _) =>
        {
            PanelImportExampleTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-import-example"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>The rows below the header that hold any text, the empty rows between them, and each text row's first cell,
    /// read straight from the workbook with Open XML.</summary>
    private static (List<int> Rows, List<int> Empty, List<string> Keys) ExampleSheetRows(string path, string sheetName, int headerRow)
    {
        using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart!;
        var strings = workbook.SharedStringTablePart?.SharedStringTable?.Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>().Select(i => i.InnerText).ToArray() ?? Array.Empty<string>();
        var sheet = workbook.Workbook!.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Single(x => x.Name == sheetName);
        var part = (DocumentFormat.OpenXml.Packaging.WorksheetPart)workbook.GetPartById(sheet.Id!);
        string Text(DocumentFormat.OpenXml.Spreadsheet.Cell cell) => cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString
            ? strings[int.Parse(cell.CellValue!.Text, CultureInfo.InvariantCulture)] : cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? string.Empty;
        var texts = part.Worksheet!.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().Where(r => r.RowIndex!.Value > headerRow)
            .Select(r => (Row: (int)r.RowIndex!.Value, Cells: r.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().Select(Text).ToArray())).ToList();
        var filled = texts.Where(r => r.Cells.Any(t => !string.IsNullOrWhiteSpace(t))).ToList();
        int last = filled.Max(r => r.Row);
        var empty = Enumerable.Range(headerRow + 1, last - headerRow).Except(filled.Select(r => r.Row)).ToList();
        return (filled.Select(r => r.Row).ToList(), empty, filled.Select(r => r.Cells[0].Trim()).ToList());
    }

    /// <summary>M7: the shipped example family list goes through the graceful importer as the wizard reads it.</summary>
    private static void PanelImportExampleTests(string workDir)
    {
        Section("Z12p1 the shipped example family list through the graceful importer");
        Directory.CreateDirectory(workDir);
        string path = Path.Combine(workDir, ExampleFamilyList.DefaultFileName), plainPath = Path.Combine(workDir, "no-title-" + ExampleFamilyList.DefaultFileName);
        foreach (string file in new[] { path, plainPath }) if (File.Exists(file)) File.Delete(file);
        ExampleFamilyList.Write(path); ExampleFamilyList.Write(plainPath, titleRow: false);
        var reader = new PanelSpreadsheetReader(); var importer = new FamilyPanelSpreadsheetImporter();
        var preview = reader.ReadPreview(path);
        Check("example: the family sheet opens first and its header row is found below the titles",
            preview.SheetName == ExampleFamilyList.FamilySheetName && preview.ProposedHeaderRow == ExampleFamilyList.FamilyHeaderRow(true)
            && preview.Problems.All(p => p.Severity == PanelImportSeverity.Note) && reader.ReadPreview(plainPath).ProposedHeaderRow == ExampleFamilyList.FamilyHeaderRow(false),
            preview.ProposedHeaderRow + ": " + preview.HeaderReason);
        var table = reader.ReadTable(path, preview.SheetName, preview.ProposedHeaderRow);
        var proposal = importer.Propose(table);
        var roles = proposal.Choices.ColumnHeaders;
        Check("example: abbreviation, both names and Pfam are proposed, and the coloured abbreviations are offered as a category",
            roles.GetValueOrDefault(PanelImportFields.FamilyKey) == "Sigla" && roles.GetValueOrDefault(PanelImportFields.DisplayName) == "Nome (português)"
            && roles.GetValueOrDefault(PanelImportFields.OtherName) == "Nome (inglês)" && roles.GetValueOrDefault(PanelImportFields.Pfam) == "Domínio(s) Pfam"
            && proposal.ColorColumns.Contains("Sigla") && !roles.ContainsKey(PanelImportFields.Decision),
            string.Join("|", roles.Select(r => r.Key + "=" + r.Value)) + " colours: " + string.Join(",", proposal.ColorColumns));
        var choices = proposal.Choices;
        choices.ColumnHeaders[PanelImportFields.Decision] = "Decisão FamilyQuant";
        var decisions = importer.ProposeDecisions(table, choices);
        var byLabel = decisions.ToDictionary(d => d.SourceValue);
        Check("example: the merge label names its family and the excluding labels are proposed as not a family; the rest are Quantify",
            byLabel["Fundir com LEC"].Mapping is { Decision: FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey: "LEC" }
            && byLabel["Excluir"].Mapping.Decision == FamilyPanelDecision.NotAFamily && byLabel["Excluir (ou só varredura)"].Mapping.Decision == FamilyPanelDecision.NotAFamily
            && decisions.Where(d => !d.SourceValue.StartsWith("Excluir", StringComparison.Ordinal) && d.SourceValue != "Fundir com LEC").All(d => d.Mapping.Decision == FamilyPanelDecision.Quantify),
            string.Join(" / ", decisions.Select(d => d.SourceValue + "=" + d.Mapping.Decision)));
        var mappings = decisions.Select(d => d.Mapping).ToList();
        var first = importer.Check(table, choices, mappings, "Exemplo");
        var lec = table.Rows.Where(r => r.Cells["Sigla"].Trim() == "LEC").Select(r => r.RowNumber).ToArray();
        var repeat = first.Problems.SingleOrDefault(p => p.Kind == PanelImportProblemKind.DuplicateKey);
        Check("example: the repeated LEC is reported on its later row with the earlier row and a rename",
            lec.Length == 2 && !first.CanImport && repeat is { Value: "LEC", Suggestion: "LEC-2" } && repeat.RowNumber == lec[1] && repeat.OtherRowNumbers.SequenceEqual(new[] { lec[0] }),
            string.Join(" / ", first.Problems.Where(p => p.Severity == PanelImportSeverity.Blocking).Select(p => p.Message)));
        var outras = first.Rows.Single(r => table.Rows.Single(t => t.RowNumber == r.RowNumber).Cells["Sigla"] == "OUTRAS");
        Check("example: the row listing four families is offered as a list, with paired names in the other language",
            outras.ListColumn == "Nome (português)" && outras.ListLabel == "Outras raras" && outras.ProposedParts.Select(p => p.Key).SequenceEqual(new[] { "HYA", "QTN", "GC", "AP" })
            && first.Problems.Any(p => p.Kind == PanelImportProblemKind.SeveralFamilies && p.RowNumber == outras.RowNumber),
            string.Join(" | ", outras.ProposedParts.Select(p => p.Key)));
        // Expectations from the workbook's own cells, read here without the importer: which rows below the header hold
        // text (every one is a family row in this example) and which are empty between them.
        var (sourceRows, sourceEmpty, sourceKeys) = ExampleSheetRows(path, preview.SheetName, preview.ProposedHeaderRow);
        Check("example: the empty rows between families are exactly the workbook's empty rows, listed and skipped",
            sourceEmpty.Count > 0 && table.EmptyRows.SequenceEqual(sourceEmpty) && first.Problems.Any(p => p.Kind == PanelImportProblemKind.EmptyRow)
            && table.Rows.Select(r => r.RowNumber).SequenceEqual(sourceRows), string.Join(",", table.EmptyRows) + " vs " + string.Join(",", sourceEmpty));

        choices.RowChoices = new()
        {
            new FamilyPanelRowChoice { RowNumber = lec[1], Action = FamilyPanelRowAction.Import, Key = repeat?.Suggestion ?? "LEC-2" },
            new FamilyPanelRowChoice { RowNumber = outras.RowNumber, Action = FamilyPanelRowAction.Split },
        };
        var check = importer.Check(table, choices, mappings, "Exemplo");
        var panel = check.Panel;
        var expectedKeys = sourceKeys.Where((key, i) => key != "OUTRAS" && !(key == "LEC" && sourceKeys.Take(i).Contains("LEC"))).Append("LEC-2")
            .Concat(new[] { "HYA", "QTN", "GC", "AP" }).ToHashSet(StringComparer.Ordinal);
        Check("example: no row with text is skipped; every one of the workbook's family rows becomes a family",
            check.Rows.Select(r => r.RowNumber).SequenceEqual(sourceRows) && check.Rows.All(r => r.Action != FamilyPanelRowAction.Skip)
            && panel != null && panel.Families.Select(f => f.Key).ToHashSet(StringComparer.Ordinal).SetEquals(expectedKeys),
            string.Join(",", check.Rows.Where(r => r.Action == FamilyPanelRowAction.Skip).Select(r => r.RowNumber)));
        Check("example: with the two answers every family row imports, the list as four families with their English names",
            panel != null && panel.Families.Count == sourceRows.Count - 1 + 4
            && panel.Families.Where(f => f.Key is "HYA" or "QTN" or "GC" or "AP").Select(f => (f.DisplayName, PanelImportFields.OtherNameText(panel, f))).SequenceEqual(new[]
                { ("hialuronidase", "hyaluronidase"), ("quitinase", "chitinase"), ("glutaminil ciclase", "glutaminyl cyclase"), ("aminopeptidase", "aminopeptidase") })
            && panel.Families.Single(f => f.Key == "CLP") is { Decision: FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey: "LEC" }
            && panel.Families.Single(f => f.Key == "LEC").Pfam.Single().Accession == "PF00059",
            string.Join(" / ", check.Problems.Where(p => p.Severity == PanelImportSeverity.Blocking).Select(p => p.Message)));
        Check("example: the import replays exactly", panel != null && JsonSerializer.Serialize(panel) == JsonSerializer.Serialize(importer.Replay(path, panel)));
    }
}
