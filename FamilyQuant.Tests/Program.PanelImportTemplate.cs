using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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
    private static IEnumerable<TestCommand> PanelImportTemplateCommandTable() =>
    [
        new("--test-panel-import-template", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-panel-import-template", (_, _) =>
        {
            PanelImportTemplateTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-import-template"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static void PanelImportTemplateTests(string workDir)
    {
        Section("Z12p2d the family-list template is written by the program and read with no mapping");
        Directory.CreateDirectory(workDir);
        string path = Path.Combine(workDir, "FamilyQuant family list.xlsx");
        if (File.Exists(path)) File.Delete(path);
        PanelImportTemplate.Write(path);
        var reader = new PanelSpreadsheetReader(); var importer = new FamilyPanelSpreadsheetImporter();
        var blank = reader.ReadPreview(path);
        Check("Z12p2d the template opens on its Families sheet, marked, with the header row proposed from its filter",
            blank.IsTemplate && blank.SheetNames.SequenceEqual(new[] { "Families", "Instructions" }) && blank.HiddenSheetNames.Count == 0
            && blank.ProposedHeaderRow == 1 && blank.Rows.Count == 1 && blank.Problems.Count == 0
            && blank.Rows[0].Cells.Select(c => c.Text).SequenceEqual(PanelImportTemplate.Columns.Select(c => c.Header))
            && blank.Rows[0].Cells.All(c => c.Fill == "#D9E1F2"), string.Join("|", blank.Rows.SelectMany(r => r.Cells).Select(c => c.Text + c.Fill)));
        var help = reader.ReadPreview(path, "Instructions");
        string instructions = string.Join("\n", help.Rows.Select(r => r.Cells[0].Text));
        Check("Z12p2d the Instructions sheet explains every current column, lists and saving",
            help.Rows[0].Cells[0].Text == "How to fill in this family list"
            && PanelImportTemplate.Columns.All(c => instructions.Contains(c.Header + ":", StringComparison.Ordinal))
            && new[] { "several families", "File > Import family list", ".xlsx" }
                .All(text => instructions.Contains(text, StringComparison.Ordinal)));
        var emptyTable = reader.ReadTable(path, "Families", 1);
        var emptyCheck = importer.Check(emptyTable, importer.Propose(emptyTable).Choices, Array.Empty<FamilyPanelDecisionMapping>());
        Check("Z12p2d an untouched template imports nothing and says so", !emptyCheck.CanImport
            && emptyCheck.Problems.Single().Message == "No row of sheet \"Families\" below row 1 becomes a family.");

        SetImportHeaders(path, new[] { "Abbreviation", "Name", "Other-language name", "Notes", "Pfam domains", "Decision", "UniProt family" });
        FillTemplate(path, new[]
        {
            new[] { "PLA2", "Fosfolipase A2", "Phospholipase A2", "Família principal", "Phospholip_A2_1 (PF00068)", "Quantify" },
            new[] { "CTL", "Lectina tipo C", "C-type lectin", "", "Lectin_C (PF00059)", "" },
            new[] { "Gal-CTL", "Lectina ligante de galactose", "Galactose-binding lectin", "Com CTL", "Lectin_C (PF00059)", "Merge into CTL" },
            new[] { "Rare", "Outras raras", "Endopeptidase, TCTP", "", "", "Report under others" },
            new[] { "NOT", "Não é família", "", "", "", "not a family" },
        });
        var table = reader.ReadTable(path, "Families", 1);
        var proposal = importer.Propose(table);
        Check("Z12p2d the importer maps the template's columns and the program's decision names with no question",
            table.IsTemplate && proposal.IsTemplate
            && PanelImportTemplate.Columns.All(c => proposal.Choices.ColumnHeaders[c.Role] == c.Header)
            && proposal.Reasons[PanelImportFields.FamilyKey] == "\"Abbreviation\" is a column of the FamilyQuant template."
            && proposal.Decisions.Select(d => (d.SourceValue, d.Decision, d.MergeTargetFamilyKey)).SequenceEqual(new[]
            {
                ("Quantify", FamilyPanelDecision.Quantify, ""), ("Merge into CTL", FamilyPanelDecision.MergeIntoAnotherFamily, "CTL"),
                ("Report under others", FamilyPanelDecision.ReportUnderOthers, ""), ("not a family", FamilyPanelDecision.NotAFamily, ""),
            }), string.Join(" / ", proposal.Decisions.Select(d => d.SourceValue + "=" + d.Decision)));
        var check = importer.Check(table, proposal.Choices, proposal.Decisions, "Template");
        var panel = check.Panel;
        Check("Z12p2d a filled template imports every row with its names, domains and decisions",
            panel != null && panel.Families.Select(f => (f.Key, f.Decision)).SequenceEqual(new[]
            {
                ("PLA2", FamilyPanelDecision.Quantify), ("CTL", FamilyPanelDecision.Quantify), ("Gal-CTL", FamilyPanelDecision.MergeIntoAnotherFamily),
                ("Rare", FamilyPanelDecision.ReportUnderOthers), ("NOT", FamilyPanelDecision.NotAFamily),
            })
            && panel.Families[0] is { DisplayName: "Fosfolipase A2" } pla2 && PanelImportFields.OtherNameText(panel, pla2) == "Phospholipase A2"
            && pla2.Pfam.Single() is { Accession: "PF00068", Name: "Phospholip_A2_1" } && pla2.TextFields.Single(t => t.Name == "Notes").Text == "Família principal"
            && panel.Families[2].MergeTargetFamilyKey == "CTL"
            && check.Problems.All(p => p.Severity == PanelImportSeverity.Note)
            && check.Problems.Any(p => p.Kind == PanelImportProblemKind.SeveralFamilies && p.RowNumber == 5)
            && check.Problems.Any(p => p.Message == "Row 3 has an empty decision in column \"Decision\", so that family is quantified."),
            string.Join(" / ", check.Problems.Select(p => p.Message)));
        string json = JsonSerializer.Serialize(panel);
        Check("Z12p2d the template import replays exactly", json == JsonSerializer.Serialize(importer.Replay(path, panel!)));

        string plainPath = Path.Combine(workDir, "plain.xlsx");
        if (File.Exists(plainPath)) File.Delete(plainPath);
        DuplicateHeaderWorkbook(plainPath);
        var unmarked = reader.ReadTable(plainPath, "Folha", 1);
        var plain = ListTable(("SVMP", "Not a family", ""), ("X", "Y", ""));
        plain.Headers.Add("Decisão"); plain.ColumnLetters.Add("D");
        plain.Rows[0].Cells["Decisão"] = " Not a family "; plain.Rows[1].Cells["Decisão"] = "Merge into SVMP";
        var plainChoices = ListChoices(plain); plainChoices.ColumnHeaders[PanelImportFields.Decision] = "Decisão";
        var plainDecisions = importer.ProposeDecisions(plain, plainChoices);
        Check("Z12p2d the program's decision names are recognised in any sheet, and only the marker makes a template",
            !importer.Propose(plain).IsTemplate && plainDecisions.All(d => d.Exact)
            && plainDecisions[0].Mapping.Decision == FamilyPanelDecision.NotAFamily && plainDecisions[0].Reason == "\"Not a family\" is the FamilyQuant decision Not a family."
            && plainDecisions[1].Mapping is { Decision: FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey: "SVMP" }
            && !unmarked.IsTemplate && !reader.ReadPreview(plainPath).IsTemplate);

        var culture = CultureInfo.CurrentCulture;
        string portuguese = Path.Combine(workDir, "pt.xlsx"), english = Path.Combine(workDir, "en.xlsx");
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); PanelImportTemplate.Write(portuguese);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); PanelImportTemplate.Write(english);
        }
        finally { CultureInfo.CurrentCulture = culture; }
        string Content(string file) => JsonSerializer.Serialize(new[] { "Families", "Instructions" }.Select(sheet => reader.ReadPreview(file, sheet).Rows
            .Select(r => r.Cells.Select(c => c.Text + c.Fill + c.MergeRange))));
        Check("Z12p2d the template's content does not depend on display culture", Content(portuguese) == Content(english));
        using (var document = SpreadsheetDocument.Open(english, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2016).Validate(document).ToList();
            Check("Z12p2d the written workbook passes the Open XML schema validator", errors.Count == 0,
                string.Join(" / ", errors.Take(5).Select(e => e.Path?.XPath + ": " + e.Description)));
            var sheet = (WorksheetPart)document.WorkbookPart!.GetPartById(document.WorkbookPart.Workbook!.Sheets!.Elements<Sheet>().First().Id!);
            Check("IM3 the empty template has no decision dropdown and keeps its frozen header",
                !sheet.Worksheet!.Descendants<DataValidation>().Any()
                && sheet.Worksheet.Descendants<Pane>().Single() is { State.Value: var state } && state == PaneStateValues.Frozen);
        }
    }

    private static void FillTemplate(string path, string[][] rows)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var workbook = document.WorkbookPart!;
        var part = (WorksheetPart)workbook.GetPartById(workbook.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name == PanelImportTemplate.FamilySheet).Id!);
        var data = part.Worksheet!.GetFirstChild<SheetData>()!;
        for (int i = 0; i < rows.Length; i++)
        {
            uint number = (uint)(i + 2);
            data.Append(new Row(rows[i].Select((value, column) => new Cell(new InlineString(new Text(value)))
            { CellReference = ((char)('A' + column)).ToString() + number.ToString(CultureInfo.InvariantCulture), DataType = CellValues.InlineString })) { RowIndex = number });
        }
        part.Worksheet.Save();
    }
}
