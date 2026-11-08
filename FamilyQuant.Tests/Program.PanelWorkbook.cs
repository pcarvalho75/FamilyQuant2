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
                && shipped.SubclassTables.Count == 1 && shipped.Workbook.Sheets.Count(sheet => sheet.Role == PanelWorkbookSheetRole.Notes) == 3);
            Check("Z16a1 enriched legend slots are read and their full source text remains kept", shipped.ColorsState == PanelWorkbookReadState.Read
                && shipped.DecisionsState == PanelWorkbookReadState.Read && shipped.Colors.Count > 0 && shipped.Decisions.Count > 0
                && shipped.Notes.Any(note => note.SheetName == shipped.Workbook.Sheets.Single(sheet => sheet.Role == PanelWorkbookSheetRole.ColorLegend).SheetName
                    && note.Text.Length > 0));
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
                    && proposed.SubclassTables.Count == 1 && proposed.Workbook.Sheets.Count(sheet => sheet.Role == PanelWorkbookSheetRole.Notes) == 3);
                Check("Z16a1 neutral " + freeText + " subclass association comes from source keys and domains", proposed.SubclassTables.Count == 1
                    && proposed.SubclassTables[0].FamilyKeys.SequenceEqual(new[] { "F07" })
                    && proposed.SubclassTables[0].DomainColumns.Count >= 1
                    && (freeText ? proposed.SubclassTables[0].FamilyKeyColumn.Length == 0 : proposed.SubclassTables[0].FamilyKeyColumn.Length > 0));
                Check("Z16a1 neutral " + freeText + " summary carries complete per-family domains and ambiguous rows", proposed.Pfam.Count == 40
                    && proposed.Pfam.All(row => row.Domains.Select(domain => domain.Accession).SequenceEqual(new[] { "PF01234", "PF05678" }))
                    && proposed.MultiFamilyRows.Count > 0);
                Check("Z16a1 neutral " + freeText + " source notes and legend retain cell provenance and line breaks", proposed.Notes.Count == 4
                    && proposed.Notes.All(note => note.Text.Contains("A1:", StringComparison.Ordinal))
                    && proposed.Notes.Any(note => note.Text.Contains("First invented line\nSecond invented line", StringComparison.Ordinal)));
                RenameWorkbookSheets(path);
                var renamed = importer.ProposeWorkbook(path);
                Check("Z16a1 reversing and renaming sheets preserves structural family/legend detection", renamed.FamilyTable?.Rows.Count == 40
                    && renamed.Workbook.Sheets.Count(sheet => sheet.Role == PanelWorkbookSheetRole.ColorLegend) == 1);
                if (!freeText) Check("Z16a1 renamed structured subclass table still links by its source column", renamed.SubclassTables.Count == 1);
            }

            string dualRole = Path.Combine(root, "subclass-family-shape.xlsx"); WriteNeutralWorkbook(dualRole);
            using (var document = SpreadsheetDocument.Open(dualRole, true))
            {
                var part = NeutralWorkbookPart(document.WorkbookPart!, "Data 3");
                int index = 1;
                foreach (var row in part.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1))
                    row.Elements<XL.Cell>().First().InlineString = new XL.InlineString(new XL.Text("F" + (index++).ToString("D2", CultureInfo.InvariantCulture)));
                part.Worksheet.Save();
            }
            var dual = importer.ProposeWorkbook(dualRole);
            Check("Z12p5y2 a mapped subclass table is not named as a competing family sheet", dual.FamilySheet == "Data 9"
                && !dual.Problems.Any(problem => problem.Kind == PanelImportProblemKind.FamilySheetAlternative
                    && problem.Message.Contains("Data 3", StringComparison.Ordinal)));
            string familyReason = dual.Workbook.Sheets.Single(sheet => sheet.SheetName == dual.FamilySheet).RoleReason;
            Check("Z12p5y2 the reader explains the proposed family sheet in plain words",
                familyReason.Contains("abbreviation", StringComparison.OrdinalIgnoreCase)
                && !familyReason.Contains("visible-first", StringComparison.Ordinal) && !familyReason.Contains("short-code", StringComparison.Ordinal), familyReason);
            Check("Z16a10 source-linked subclass structure wins over an alternate family-list shape",
                dual.Workbook.Sheets.Single(sheet => sheet.SheetName == "Data 3") is { Role: PanelWorkbookSheetRole.SubclassTable, IsUncertain: false }
                && dual.SubclassTables.SingleOrDefault()?.FamilyKeys.SequenceEqual(Enumerable.Range(1, 9).Select(index => "F" + index.ToString("D2", CultureInfo.InvariantCulture))) == true
                && dual.SubclassTables[0].ClassColumn == "x2" && dual.SubclassTables[0].DomainColumns.Count == 2
                && dual.Notes.All(note => note.SheetName != "Data 3"));
            var explicitSubclass = importer.ProposeWorkbook(reader.ReadWorkbook(dualRole), "Data 9",
                new Dictionary<string, PanelWorkbookSheetRole> { ["Data 3"] = PanelWorkbookSheetRole.SubclassTable });
            Check("Z16a10 automatic subclass facts equal the valid explicit-role proposal",
                explicitSubclass.Workbook.Sheets.Single(sheet => sheet.SheetName == "Data 3") is { Role: PanelWorkbookSheetRole.SubclassTable, IsUncertain: false }
                && JsonSerializer.Serialize(dual.SubclassTables) == JsonSerializer.Serialize(explicitSubclass.SubclassTables));
            var explicitNotes = importer.ProposeWorkbook(reader.ReadWorkbook(dualRole), "Data 9",
                new Dictionary<string, PanelWorkbookSheetRole> { ["Data 3"] = PanelWorkbookSheetRole.Notes });
            Check("Z16a10 an explicit notes role still overrides supported subclass structure", explicitNotes.SubclassTables.Count == 0
                && explicitNotes.Notes.Any(note => note.SheetName == "Data 3"));
            string keyClasses = Path.Combine(root, "family-keys-as-classes.xlsx"); File.Copy(dualRole, keyClasses);
            using (var document = SpreadsheetDocument.Open(keyClasses, true))
            {
                var part = NeutralWorkbookPart(document.WorkbookPart!, "Data 3");
                int index = 11;
                foreach (var row in part.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1))
                    row.Elements<XL.Cell>().ElementAt(1).InlineString = new XL.InlineString(new XL.Text("F" + (index++).ToString("D2", CultureInfo.InvariantCulture)));
                part.Worksheet.Save();
            }
            var keyClassProposal = importer.ProposeWorkbook(keyClasses);
            Check("Z16a10 imported family keys alone are not distinct subclass names", keyClassProposal.SubclassTables.Count == 0
                && keyClassProposal.Workbook.Sheets.Single(sheet => sheet.SheetName == "Data 3") is { Role: PanelWorkbookSheetRole.Notes, IsUncertain: true });

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
            Check("Z16a1 absent legend/decision slots are read-empty rather than pending", noLegend.ColorsState == PanelWorkbookReadState.Read
                && noLegend.DecisionsState == PanelWorkbookReadState.Read && noLegend.Colors.Count == 0 && noLegend.Decisions.Count == 0);
            var incompleteRead = reader.ReadWorkbook(plain);
            incompleteRead.Sheets.Add(new PanelWorkbookSheet { SheetName = "Unreadable source" });
            incompleteRead.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.FileUnreadable,
                Severity = PanelImportSeverity.Blocking, SheetName = "Unreadable source", Message = "Synthetic read failure." });
            var incomplete = importer.ProposeWorkbook(incompleteRead);
            Check("Z16a1 a failed sheet read never turns unknown legend/decision data into a read-empty result",
                incomplete.ColorsState == PanelWorkbookReadState.NotRead && incomplete.DecisionsState == PanelWorkbookReadState.NotRead);

            foreach (bool blankTab in new[] { false, true })
            {
                string path = Path.Combine(root, "legacy-fallback-" + blankTab + ".xlsx");
                WriteNeutralWorkbook(path, onlyFamily: true);
                using (var document = SpreadsheetDocument.Open(path, true))
                {
                    var book = document.WorkbookPart!;
                    var sheet = book.Workbook!.Sheets!.Elements<XL.Sheet>().Single();
                    var data = ((WorksheetPart)book.GetPartById(sheet.Id!)).Worksheet!.GetFirstChild<XL.SheetData>()!;
                    foreach (var row in data.Elements<XL.Row>().Skip(1))
                        row.Elements<XL.Cell>().First().InlineString = new XL.InlineString(new XL.Text("Long source code " + row.RowIndex));
                    if (blankTab)
                    {
                        var blank = book.AddNewPart<WorksheetPart>(); blank.Worksheet = new XL.Worksheet(new XL.SheetData());
                        book.Workbook.Sheets.Append(new XL.Sheet { Id = book.GetIdOfPart(blank), Name = "Blank 2", SheetId = 2 });
                        blank.Worksheet.Save();
                    }
                    ((WorksheetPart)book.GetPartById(sheet.Id!)).Worksheet!.Save(); book.Workbook.Save();
                }
                var preview = reader.ReadPreview(path, "Data 9");
                var legacy = importer.Propose(reader.ReadTable(path, "Data 9", preview.ProposedHeaderRow));
                var fallback = importer.ProposeWorkbook(path);
                Check("Z16a1 non-code key legacy fallback with blank tab " + blankTab + " keeps the exact old proposal",
                    fallback.FamilySheet == "Data 9" && !fallback.NeedsFamilySheetChoice
                    && JsonSerializer.Serialize(fallback.FamilyProposal) == JsonSerializer.Serialize(legacy));
            }

            string merged = Path.Combine(root, "merged-notes.xlsx"); WriteNeutralWorkbook(merged);
            using (var document = SpreadsheetDocument.Open(merged, true))
            {
                var book = document.WorkbookPart!;
                var sheet = book.Workbook!.Sheets!.Elements<XL.Sheet>().Single(sheet => sheet.Name == "Data 4");
                var part = (WorksheetPart)book.GetPartById(sheet.Id!);
                var row = part.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().First();
                row.RemoveAllChildren<XL.Cell>();
                row.Append(new XL.Cell { CellReference = "A1", DataType = XL.CellValues.InlineString,
                    InlineString = new XL.InlineString(new XL.Text("Merged invented title")) });
                part.Worksheet.Append(new XL.MergeCells(new XL.MergeCell { Reference = "A1:C1" })); part.Worksheet.Save();
            }
            var mergedMap = importer.ProposeWorkbook(merged);
            string mergedText = mergedMap.Notes.Single(note => note.SheetName == "Data 4").Text;
            Check("Z16a1 merged notes text is emitted once with its complete range", mergedText.Contains("A1:C1: Merged invented title", StringComparison.Ordinal)
                && mergedText.Split("Merged invented title", StringSplitOptions.None).Length == 2);
            string problemPath = Path.Combine(root, "sheet-problems.xlsx"); WriteNeutralWorkbook(problemPath);
            using (var document = SpreadsheetDocument.Open(problemPath, true))
            {
                var book = document.WorkbookPart!;
                foreach (string name in new[] { "Data 9", "Data 4" })
                {
                    var sheet = book.Workbook!.Sheets!.Elements<XL.Sheet>().Single(sheet => sheet.Name == name);
                    var part = (WorksheetPart)book.GetPartById(sheet.Id!);
                    var rows = part.Worksheet!.GetFirstChild<XL.SheetData>()!;
                    rows.Elements<XL.Row>().First().Elements<XL.Cell>().First().Remove();
                    if (part.Worksheet.GetFirstChild<XL.AutoFilter>() == null)
                        part.Worksheet.Append(new XL.AutoFilter { Reference = "A1:B3" });
                    part.Worksheet.Save();
                }
            }
            var problemMap = importer.ProposeWorkbook(problemPath);
            Check("Z16a1 same-row table problems remain distinct and attributed to both sheets", new[] { "Data 9", "Data 4" }.All(name =>
                problemMap.Problems.Any(problem => problem.Kind == PanelImportProblemKind.UnnamedColumn && problem.RowNumber == 1 && problem.SheetName == name)));
            Check("Z16a1 selected-family table problems are included in the whole proposal", problemMap.FamilyTable != null
                && problemMap.FamilyTable.Problems.Count > 0 && problemMap.FamilyTable.Problems.All(problem => problemMap.Problems.Any(aggregate =>
                    aggregate.Kind == problem.Kind && aggregate.RowNumber == problem.RowNumber && aggregate.SheetName == problemMap.FamilySheet)));

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
            var chosen = importer.ProposeWorkbook(tied.Workbook, "Another 9");
            Check("Z16a1 explicit family choice reruns complete derived facts after a tie", chosen.FamilySheet == "Another 9"
                && !chosen.NeedsFamilySheetChoice && chosen.Pfam.Count == 40 && chosen.MultiFamilyRows.Count > 0);
            Check("Z16a10 an unchosen duplicate family list remains uncertain notes rather than subclasses",
                chosen.SubclassTables.Count == 0
                && chosen.Workbook.Sheets.Single(sheet => sheet.SheetName == "Data 9") is { Role: PanelWorkbookSheetRole.Notes, IsUncertain: true }
                && chosen.Notes.Any(note => note.SheetName == "Data 9" && note.Text.Contains("Invented group 1", StringComparison.Ordinal)));
            string filteredPath = Path.Combine(root, "filtered-family-copy.xlsx"); File.Copy(ambiguous, filteredPath);
            using (var document = SpreadsheetDocument.Open(filteredPath, true))
            {
                var part = NeutralWorkbookPart(document.WorkbookPart!, "Another 9");
                foreach (var row in part.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(11).ToArray()) row.Remove();
                part.Worksheet.Save();
            }
            var filtered = importer.ProposeWorkbook(filteredPath);
            Check("Z16a10 a filtered family copy keeps its full text as an uncertain alternative",
                filtered.FamilySheet == "Data 9" && filtered.SubclassTables.Count == 0
                && filtered.Workbook.Sheets.Single(sheet => sheet.SheetName == "Another 9") is { Role: PanelWorkbookSheetRole.Notes, IsUncertain: true }
                && filtered.Notes.Any(note => note.SheetName == "Another 9" && note.Text.Contains("Invented group 10", StringComparison.Ordinal)));
            var overridden = importer.ProposeWorkbook(shipped.Workbook, shipped.FamilySheet,
                new Dictionary<string, PanelWorkbookSheetRole> { [shipped.Workbook.Sheets.Single(sheet => sheet.Role == PanelWorkbookSheetRole.ColorLegend).SheetName] = PanelWorkbookSheetRole.Notes });
            Check("Z16a1 explicit role overrides survive relationship derivation", overridden.Workbook.Sheets.All(sheet => sheet.Role != PanelWorkbookSheetRole.ColorLegend)
                && overridden.Notes.Count == 4 && overridden.ColorsState == PanelWorkbookReadState.Read);
            var subclassRead = reader.ReadWorkbook(example);
            var beforeOverride = importer.ProposeWorkbook(subclassRead);
            string subclassName = beforeOverride.SubclassTables.Single().SheetName;
            var noSubclass = importer.ProposeWorkbook(subclassRead, beforeOverride.FamilySheet,
                new Dictionary<string, PanelWorkbookSheetRole> { [subclassName] = PanelWorkbookSheetRole.Notes });
            Check("Z16a1 a subclass-to-notes override is retained rather than redetected", noSubclass.SubclassTables.Count == 0
                && noSubclass.Workbook.Sheets.Single(sheet => sheet.SheetName == subclassName).Role == PanelWorkbookSheetRole.Notes
                && noSubclass.Notes.Any(note => note.SheetName == subclassName && note.Text.Length > 0));
            var conflicting = importer.ProposeWorkbook(reader.ReadWorkbook(example), beforeOverride.FamilySheet,
                new Dictionary<string, PanelWorkbookSheetRole> { [beforeOverride.FamilySheet] = PanelWorkbookSheetRole.Notes });
            Check("Z16a1 conflicting explicit family and role choices block without overwriting the role", conflicting.NeedsFamilySheetChoice
                && conflicting.FamilySheet.Length == 0 && conflicting.Workbook.Sheets.Single(sheet => sheet.SheetName == beforeOverride.FamilySheet).Role == PanelWorkbookSheetRole.Notes
                && conflicting.Problems.Any(problem => problem.Kind == PanelImportProblemKind.ChoiceInvalid && problem.Severity == PanelImportSeverity.Blocking));
            using (var document = SpreadsheetDocument.Open(ambiguous, true))
            {
                var book = document.WorkbookPart!;
                var first = book.Workbook!.Sheets!.Elements<XL.Sheet>().First(); first.State = XL.SheetStateValues.Hidden;
                var part = (WorksheetPart)book.GetPartById(first.Id!);
                var data = part.Worksheet!.GetFirstChild<XL.SheetData>()!;
                var extra = (XL.Row)data.Elements<XL.Row>().Last().CloneNode(true); extra.RowIndex = 42;
                foreach (var cell in extra.Elements<XL.Cell>()) cell.CellReference = new string(cell.CellReference!.Value!.TakeWhile(char.IsLetter).ToArray()) + "42";
                data.Append(extra); part.Worksheet.Save(); book.Workbook.Save();
            }
            var visible = importer.ProposeWorkbook(ambiguous);
            Check("Z16a1 visible family candidates rank before a larger hidden candidate and remain uncertain", visible.FamilySheet == "Another 9"
                && visible.Workbook.Sheets.Single(sheet => sheet.SheetName == visible.FamilySheet).IsUncertain
                && visible.Problems.Any(problem => problem.Kind == PanelImportProblemKind.FamilySheetAlternative
                    && problem.Severity == PanelImportSeverity.Note && problem.Message.Contains("Data 9", StringComparison.Ordinal)));
            var missing = importer.ProposeWorkbook(Path.Combine(root, "missing.xlsx"));
            Check("Z16a1 an unreadable workbook is a blocked proposal rather than a crash", missing.NeedsFamilySheetChoice
                && missing.Problems.Any(problem => problem.Kind == PanelImportProblemKind.FileUnreadable));
        }
        catch (Exception error) { Check("Z16a1 workbook map fixtures run without an exception", false, error.ToString()); }
        finally
        {
            string allowed = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("Workbook fixture cleanup escaped its owned parent.");
            Directory.Delete(root, recursive: true);
        }
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
