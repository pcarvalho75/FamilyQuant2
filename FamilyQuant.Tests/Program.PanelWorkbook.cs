using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Examples;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelWorkbookCommands() =>
    [new("--test-panel-workbook-map", 21601, TestCommandPhase.Raw,
        args => args is ["--test-panel-workbook-map"], (_, _) =>
        {
            PanelWorkbookMapTests(Path.Combine(Path.GetTempPath(), "panel-workbook-map"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void PanelWorkbookMapTests(string parent)
    {
        Section("Z16a1 whole workbook map from source structure");
        string root = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var importer = new FamilyPanelSpreadsheetImporter();
            var reader = new PanelSpreadsheetReader();
            string example = Path.Combine(root, "shipped.xlsx"); ExampleFamilyList.Write(example);
            string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(example)));
            var shipped = importer.ProposeWorkbook(example);
            Check("Z16a1 shipped six-sheet example chooses the family list structurally", shipped.Workbook.Sheets.Count == 6
                && shipped.FamilySheet == ExampleFamilyList.FamilySheetName && !shipped.NeedsFamilySheetChoice);
            Check("Z16a1 shipped map separates one legend, one subclass table and three notes sheets",
                shipped.Workbook.Sheets.Count(sheet => sheet.Role == PanelWorkbookSheetRole.ColorLegend) == 1
                && shipped.SubclassTables.Count == 1 && shipped.Notes.Count == 3);
            Check("Z16a1 shipped opening proposal carries family columns, multi-family rows and Pfam facts",
                shipped.FamilyProposal.Choices.ColumnHeaders.ContainsKey(PanelImportFields.FamilyKey)
                && shipped.FamilyProposal.Choices.ColumnHeaders.ContainsKey(PanelImportFields.OtherName)
                && shipped.MultiFamilyRows.Count > 0 && shipped.Pfam.Count > 0);
            Check("Z16a1 reading every sheet leaves the workbook bytes unchanged", originalHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(example))));

            foreach (bool freeText in new[] { false, true })
            {
                string path = Path.Combine(root, freeText ? "free-text.xlsx" : "structured.xlsx");
                WriteNeutralWorkbook(path, freeText);
                var proposed = importer.ProposeWorkbook(path);
                Check("Z16a1 neutral " + freeText + " map selects the largest coded/name table", proposed.FamilySheet == "Data 9"
                    && proposed.FamilyTable?.Rows.Count == 40 && !proposed.NeedsFamilySheetChoice);
                Check("Z16a1 neutral " + freeText + " map retains all six source roles", proposed.Workbook.Sheets.Count == 6
                    && proposed.Workbook.Sheets.Single(sheet => sheet.Role == PanelWorkbookSheetRole.ColorLegend).SheetName == "Data 2"
                    && proposed.SubclassTables.Count == 1 && proposed.Notes.Count == 3);
                Check("Z16a1 neutral " + freeText + " subclass association comes from source keys and domains", proposed.SubclassTables.Count == 1
                    && proposed.SubclassTables[0].FamilyKeys.SequenceEqual(new[] { "F07" })
                    && proposed.SubclassTables[0].DomainColumns.Count >= 1
                    && (freeText ? proposed.SubclassTables[0].FamilyKeyColumn.Length == 0 : proposed.SubclassTables[0].FamilyKeyColumn.Length > 0));
                Check("Z16a1 neutral " + freeText + " summary carries complete per-family domains and ambiguous rows", proposed.Pfam.Count == 40
                    && proposed.Pfam.All(row => row.Domains.Select(domain => domain.Accession).SequenceEqual(new[] { "PF01234", "PF05678" }))
                    && proposed.MultiFamilyRows.Count > 0);
                Check("Z16a1 neutral " + freeText + " source notes retain cell provenance and line breaks", proposed.Notes.Count == 3
                    && proposed.Notes.All(note => note.Text.Contains("A1:", StringComparison.Ordinal))
                    && proposed.Notes.Any(note => note.Text.Contains("First invented line\nSecond invented line", StringComparison.Ordinal)));
                RenameWorkbookSheets(path);
                var renamed = importer.ProposeWorkbook(path);
                Check("Z16a1 reversing and renaming sheets preserves structural family/legend detection", renamed.FamilyTable?.Rows.Count == 40
                    && renamed.Workbook.Sheets.Count(sheet => sheet.Role == PanelWorkbookSheetRole.ColorLegend) == 1);
                if (!freeText) Check("Z16a1 renamed structured subclass table still links by its source column", renamed.SubclassTables.Count == 1);
            }

            string single = Path.Combine(root, "single.xlsx"); WriteNeutralWorkbook(single, onlyFamily: true);
            var oldPreview = reader.ReadPreview(single);
            var oldTable = reader.ReadTable(single, oldPreview.SheetName, oldPreview.ProposedHeaderRow);
            var one = importer.ProposeWorkbook(single);
            Check("Z16a1 one-sheet workbook retains the old sheet/header and exact column proposal", one.FamilySheet == oldPreview.SheetName
                && one.FamilyTable?.HeaderRowNumber == oldPreview.ProposedHeaderRow
                && JsonSerializer.Serialize(one.FamilyProposal) == JsonSerializer.Serialize(importer.Propose(oldTable)));
            string plain = Path.Combine(root, "no-legend.xlsx"); WriteNeutralWorkbook(plain, includeLegend: false, includeDecision: false);
            var noLegend = importer.ProposeWorkbook(plain);
            Check("Z16a1 list without legend or decisions remains a supported family list", noLegend.FamilyTable?.Rows.Count == 40
                && !noLegend.FamilyProposal.Choices.ColumnHeaders.ContainsKey(PanelImportFields.Decision)
                && noLegend.Workbook.Sheets.All(sheet => sheet.Role != PanelWorkbookSheetRole.ColorLegend));
            Check("Z16a1 unrelated notes never become family rows", noLegend.Notes.Count == 3
                && noLegend.FamilyTable?.Rows.Count == 40 && noLegend.SubclassTables.Count == 1);

            string ambiguous = Path.Combine(root, "ambiguous.xlsx"); WriteNeutralWorkbook(ambiguous, onlyFamily: true);
            using (var document = SpreadsheetDocument.Open(ambiguous, true))
            {
                var book = document.WorkbookPart!; var sheets = book.Workbook!.Sheets!;
                var first = sheets.Elements<XL.Sheet>().Single();
                var copy = book.AddNewPart<WorksheetPart>();
                copy.Worksheet = (XL.Worksheet)((WorksheetPart)book.GetPartById(first.Id!)).Worksheet!.CloneNode(true);
                sheets.Append(new XL.Sheet { Id = book.GetIdOfPart(copy), Name = "Another 9", SheetId = 2 });
                copy.Worksheet.Save(); book.Workbook.Save();
            }
            var tied = importer.ProposeWorkbook(ambiguous);
            Check("Z16a1 equally supported family sheets require a choice instead of a first-sheet guess", tied.NeedsFamilySheetChoice
                && tied.FamilySheet.Length == 0 && tied.Problems.Any(problem => problem.Severity == PanelImportSeverity.Blocking));
            var missing = importer.ProposeWorkbook(Path.Combine(root, "missing.xlsx"));
            Check("Z16a1 an unreadable workbook is a blocked proposal rather than a crash", missing.NeedsFamilySheetChoice
                && missing.Problems.Any(problem => problem.Kind == PanelImportProblemKind.FileUnreadable));
        }
        catch (Exception error) { Check("Z16a1 workbook map fixtures run without an exception", false, error.ToString()); }
        finally { Directory.Delete(root, recursive: true); }
        Check("Z16a1 owned synthetic workbook folder is removed", !Directory.Exists(root));
    }

    private static void WriteNeutralWorkbook(string path, bool freeTextSubclass = false, bool onlyFamily = false,
        bool includeLegend = true, bool includeDecision = true)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var book = document.AddWorkbookPart(); book.Workbook = new XL.Workbook(new XL.Sheets());
        var fills = new XL.Fills(new XL.Fill(new XL.PatternFill { PatternType = XL.PatternValues.None }),
            new XL.Fill(new XL.PatternFill { PatternType = XL.PatternValues.Gray125 }));
        foreach (string value in new[] { "FFCBDCEA", "FFD8E9DA", "FFEADBE7", "FFD8D8D8", "FFE9D5D8" })
            fills.Append(new XL.Fill(new XL.PatternFill(new XL.ForegroundColor { Rgb = value }, new XL.BackgroundColor { Indexed = 64 })
                { PatternType = XL.PatternValues.Solid }));
        var formats = new XL.CellFormats(new XL.CellFormat());
        for (uint index = 2; index < 7; index++) formats.Append(new XL.CellFormat { FillId = index, ApplyFill = true });
        book.AddNewPart<WorkbookStylesPart>().Stylesheet = new XL.Stylesheet(new XL.Fonts(new XL.Font()), fills,
            new XL.Borders(new XL.Border()), new XL.CellStyleFormats(new XL.CellFormat()), formats);
        uint nextId = 1;
        void Add(string name, IReadOnlyList<string[]> rows, bool family = false, bool legend = false)
        {
            var part = book.AddNewPart<WorksheetPart>(); var data = new XL.SheetData();
            uint number = 1;
            foreach (var values in rows)
            {
                var row = new XL.Row { RowIndex = number };
                for (int column = 0; column < values.Length; column++)
                    row.Append(new XL.Cell { CellReference = PanelTestColumn(column + 1) + number.ToString(CultureInfo.InvariantCulture),
                        DataType = XL.CellValues.InlineString, InlineString = new XL.InlineString(new XL.Text(values[column]) { Space = SpaceProcessingModeValues.Preserve }),
                        StyleIndex = column == 0 && number > 1 && (family || legend) ? 1u + (number - 2) % 5 : 0u });
                data.Append(row); number++;
            }
            part.Worksheet = new XL.Worksheet(data);
            if (family) part.Worksheet.Append(new XL.AutoFilter { Reference = "A1:" + PanelTestColumn(rows[0].Length) + rows.Count.ToString(CultureInfo.InvariantCulture) });
            book.Workbook.Sheets!.Append(new XL.Sheet { Name = name, SheetId = nextId++, Id = book.GetIdOfPart(part) });
            part.Worksheet.Save();
        }
        string[] labels = ["Action alpha", "Action beta", "Action gamma", "Action delta", "Action epsilon"];
        var familyRows = new List<string[]> { Enumerable.Range(1, 15).Select(index => "h" + index.ToString("D2", CultureInfo.InvariantCulture)).ToArray() };
        for (int index = 1; index <= 40; index++)
            familyRows.Add(new[] { "F" + index.ToString("D2", CultureInfo.InvariantCulture),
                index == 39 ? "First invented group; Second invented group" : "Invented group " + index,
                "Alternate invented title " + index, "Level " + index % 3, "Tier " + index % 4,
                index == 6 ? "Merge into F01" : labels[(index - 1) % 5], index % 2 == 0 ? "Yes" : "No", "Crossref " + index,
                "Occurrence " + index % 3, "Presence " + index % 4, "About one invented unit", "DomainOne (PF01234); DomainTwo (PF05678)",
                "A, B (B1, B2), C (C1, C2)", "REF" + index % 7, "Invented source note " + index });
        if (!includeDecision) familyRows = familyRows.Select(row => row.Where((_, index) => index != 5).ToArray()).ToList();
        Add("Data 9", familyRows, family: true);
        if (!onlyFamily)
        {
            var subclass = new List<string[]> { freeTextSubclass ? new[] { "x1", "x2", "x3" } : new[] { "x1", "x2", "x3", "x4", "x5" } };
            for (int index = 0; index < 9; index++) subclass.Add(freeTextSubclass
                ? new[] { ((char)('A' + index)).ToString(), "DomainOne + DomainTwo", "with DomainOne and without DomainTwo" }
                : new[] { "F07", "Class " + index, "DomainOne (PF01234)", "DomainTwo (PF05678)", "Invented class note " + index });
            Add(freeTextSubclass ? "Details F07" : "Data 3", subclass);
            Add("Data 4", new[] { new[] { "n1", "n2" }, new[] { "Q01", "First invented line\nSecond invented line" }, new[] { "Q02", "Another invented answer" } });
            Add("Data 5", new[] { new[] { "n1", "n2" }, new[] { "R01", "An invented rule retained verbatim" }, new[] { "R02", "Another invented rule" } });
            Add("Data 6", new[] { new[] { "n1", "n2", "n3", "n4" }, new[] { "REF1", "Invented reference", "10.0000/invented", "Background" } });
            if (includeLegend) Add("Data 2", new[] { new[] { "z1", "z2" } }.Concat(labels.Select((label, index) => new[]
                { label, new[] { "Quantify", "Report under others", "Not a family", "An unresolved action", "Quantify" }[index] })).ToArray(), legend: true);
        }
        book.Workbook.Save();
    }

    private static string PanelTestColumn(int column)
    {
        string result = "";
        for (; column > 0; column = (column - 1) / 26) result = (char)('A' + (column - 1) % 26) + result;
        return result;
    }

    private static void RenameWorkbookSheets(string path)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var sheets = document.WorkbookPart!.Workbook!.Sheets!;
        var original = sheets.Elements<XL.Sheet>().Reverse().ToArray();
        foreach (var sheet in original) sheet.Remove();
        for (int index = 0; index < original.Length; index++) { original[index].Name = "Tab " + index; sheets.Append(original[index]); }
        document.WorkbookPart.Workbook.Save();
    }
}
