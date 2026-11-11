using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportSimplificationCommands() =>
    [new("--test-import-template-workbook", 100, TestCommandPhase.Raw,
        args => args is ["--test-import-template-workbook"], (_, _) =>
        {
            ImportTemplateWorkbookTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-import-uniprot-column", 100, TestCommandPhase.Raw,
            args => args is ["--test-import-uniprot-column"], (_, _) =>
            {
                ImportUniProtColumnTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--test-import-simple-template", 100, TestCommandPhase.Raw,
            args => args is ["--test-import-simple-template"], (_, _) =>
            {
                ImportSimpleTemplateTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--test-import-entry-actions", 100, TestCommandPhase.Raw,
            args => args is ["--test-import-entry-actions"], (_, _) =>
            {
                ImportEntryActionTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--test-import-decision-pages", 100, TestCommandPhase.Raw,
            args => args is ["--test-import-decision-pages"], (_, _) =>
            {
                ImportDecisionPageTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--test-import-pfam-direct", 100, TestCommandPhase.Raw,
            args => args is ["--test-import-pfam-direct"], (_, _) =>
            {
                ImportDirectPfamTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            })];

    private static string ImportSimpleWork(string step)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "fq2", "FQImport", step);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void SetImportHeaders(string path, string[] headers)
    {
        using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, true);
        var workbook = document.WorkbookPart!;
        var sheet = workbook.Workbook!.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Single(s => s.Name == "Families");
        var part = (DocumentFormat.OpenXml.Packaging.WorksheetPart)workbook.GetPartById(sheet.Id!);
        var row = part.Worksheet!.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().First();
        row.RemoveAllChildren();
        for (int i = 0; i < headers.Length; i++)
            row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell(new DocumentFormat.OpenXml.Spreadsheet.InlineString(
                new DocumentFormat.OpenXml.Spreadsheet.Text(headers[i])))
            { CellReference = ((char)('A' + i)).ToString() + "1", DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString });
        part.Worksheet.Save();
    }

    private static void ImportEntryActionTests()
    {
        Section("IM4 the import entry actions stay focused on the user's workbook");
        EnsureApplication();
        var file = new FamilyClusterFile();
        string before = file.ToJsonText();
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)));
        var window = new FamilyListImportWindow(model, _ => throw new InvalidOperationException("The example action was removed."), PanelImportTemplate.Write);
        Check("IM4 the example action is absent while Use template remains available without editing the project",
            window.FindName("ExampleButton") == null
            && window.FindName("TemplateButton") is System.Windows.Controls.Button { IsEnabled: true, Content: "Use template" }
            && file.ToJsonText() == before && !FamilyListImportGuide.Text().Contains("Try with the example", StringComparison.Ordinal));
        window.Close();
    }

    private static void ImportDecisionPageTests()
    {
        Section("IM5 decision labels need review only for the sheet's own words");
        EnsureApplication();
        foreach (string shape in new[] { "no-decision", "program", "custom" })
        {
            string path = Path.Combine(ImportSimpleWork("IM5"), shape + ".xlsx");
            PanelImportTemplate.Write(path);
            SetImportHeaders(path, shape == "no-decision" ? new[] { "Abbreviation", "Name" } : new[] { "Abbreviation", "Name", "Decision" });
            FillTemplate(path, new[] { new[] { "A", "First family", shape == "custom" ? "Keep this" : shape == "program" ? "Quantify" : "" } });
            var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(new FamilyClusterFile())),
                workbookSource: new FamilyListWorkbookCoreSource(), openWorkbookReview: true);
            FinishReviewWork(() => model.LoadAsync(path));
            if (model.HasCustomDecisions) FinishReviewWork(() => model.ReviewWorkbookSectionAsync("decisions"));
            var window = new FamilyListImportWindow(model);
            Check("IM5 " + shape + " shows decision review only for custom words and has no colour-category controls",
                model.HasCustomDecisions == (shape == "custom") && model.ReviewingDecisions == (shape == "custom")
                && model.Decisions.All(d => d.Confirmed == (shape != "custom"))
                && model.WorkbookSections.All(s => s.Key != "colours") && window.FindName("ColourCategories") == null);
            window.Close();
        }
    }

    private static void ImportDirectPfamTests()
    {
        Section("IM6 known supplied PF codes do not require another matching decision");
        EnsureApplication();
        var file = ManualChoiceFile();
        file.MyExtendedFastaItems[0].Evidence!.Pfam = new() { new() { Accession = "PF12345", Name = "First domain" } };
        string before = file.ToJsonText();
        string path = Path.Combine(ImportSimpleWork("IM6"), "known.xlsx");
        PanelImportTemplate.Write(path);
        SetImportHeaders(path, new[] { "Abbreviation", "Name", "Pfam domains" });
        FillTemplate(path, new[] { new[] { "A", "First family", "First domain (PF12345)" }, new[] { "B", "Second family", "" } });
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)),
            workbookSource: new FamilyListWorkbookCoreSource());
        FinishReviewWork(() => model.LoadAsync(path));
        FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(_ => true); await model.NextAsync(); });
        Check("IM6 supplied valid codes reach Summary without a Pfam question and missing-code rows are named once",
            model.Page == 5 && model.PfamQuestions.Count == 0 && model.MatchFamilies.All(f => f.Decided)
            && model.MatchFamilies.Single(f => f.Key == "A").Suggestions.Single(s => s.IsFromSheet).Use
            && model.SummaryFamilies.Count(f => f.Matches == "No Pfam code; no proteins until it has one") == 1
            && file.ToJsonText() == before);
        var importer = new FamilyPanelSpreadsheetImporter();
        var matching = new PanelEvidenceMatching(file);
        var summary = WorkbookImportSummaries.Build(importer.ProposeWorkbook(path), matching);
        Check("IM6 the workbook summary accepts known codes without broad-domain confirmation",
            !summary.HasBlockingItems && summary.Sections.SelectMany(s => s.Items).All(i => i.ConfirmationWarning.Length == 0));
        path = Path.Combine(ImportSimpleWork("IM6"), "questions.xlsx");
        PanelImportTemplate.Write(path);
        SetImportHeaders(path, new[] { "Abbreviation", "Name", "Pfam domains" });
        FillTemplate(path, new[] { new[] { "A", "First family", "First domain" }, new[] { "B", "Second family", "PF99999; PF88888" } });
        summary = WorkbookImportSummaries.Build(importer.ProposeWorkbook(path), matching);
        Check("IM6 names and unknown codes require exactly one resolving choice per source row",
            summary.HasBlockingItems && summary.Sections.SelectMany(s => s.Items).Count(i => i.Required
                && i.State == WorkbookImportItemState.Uncertain && i.Page == WorkbookImportPage.EvidenceMatches) == 2);
    }

    private static void ImportSimpleTemplateTests()
    {
        Section("IM3 the empty template is simple and legacy columns keep their meanings");
        string path = Path.Combine(ImportSimpleWork("IM3"), "template.xlsx");
        PanelImportTemplate.Write(path);
        var reader = new PanelSpreadsheetReader();
        var table = reader.ReadTable(path, "Families", 1);
        string instructions = string.Join("\n", reader.ReadPreview(path, "Instructions").Rows.SelectMany(r => r.Cells).Select(c => c.Text));
        Check("IM3 the writer omits Notes, Decision and colour-category guidance",
            !table.Headers.Contains("Notes") && !table.Headers.Contains("Decision")
            && !instructions.Contains("colour", StringComparison.OrdinalIgnoreCase));
        SetImportHeaders(path, new[] { "Abbreviation", "Name", "Other-language name", "Notes", "Pfam domains", "Decision" });
        FillTemplate(path, new[] { new[] { "A", "First family", "", "12.50 kept as text", "PF12345", "Quantify" },
            new[] { "B", "Second family", "", "7.25 kept as text", "PF23456", "Report under others" } });
        var importer = new FamilyPanelSpreadsheetImporter();
        var proposal = importer.ProposeWorkbook(path);
        var panel = importer.CheckConfirmedWorkbook(proposal, proposal.FamilyProposal.Choices, proposal.FamilyProposal.Decisions).Panel;
        Check("IM3 an old marked sheet keeps every family, decision and original numerical text",
            panel?.Families.Count == 2 && panel.Families[0].Decision == FamilyPanelDecision.Quantify
            && panel.Families[1].Decision == FamilyPanelDecision.ReportUnderOthers
            && panel.Families[0].TextFields.Single(t => t.Name == "Notes").Text == "12.50 kept as text"
            && panel.Families[1].TextFields.Single(t => t.Name == "Notes").Text == "7.25 kept as text"
            && panel.Families.SelectMany(f => f.Pfam).Select(d => d.Accession).SequenceEqual(new[] { "PF12345", "PF23456" }));
    }

    private static void ImportTemplateWorkbookTests()
    {
        Section("IM1 the template's Instructions sheet never becomes a family list");
        string path = Path.Combine(ImportSimpleWork("IM1"), "template.xlsx");
        PanelImportTemplate.Write(path);
        var importer = new FamilyPanelSpreadsheetImporter();
        var blank = importer.ProposeWorkbook(path);
        Check("IM1 an empty template selects Families and reports only the missing family rows",
            blank.FamilySheet == PanelImportTemplate.FamilySheet && !blank.NeedsFamilySheetChoice
            && !blank.Problems.Any(p => p.Severity == PanelImportSeverity.Blocking)
            && importer.Check(blank.FamilyTable!, blank.FamilyProposal.Choices, blank.FamilyProposal.Decisions)
                .Problems.Any(p => p.Kind == PanelImportProblemKind.NoFamilies));
        FillTemplate(path, new[]
        {
            new[] { "A", "First family", "", "", "", "" },
            new[] { "B", "Second family", "", "", "", "" },
        });
        var proposal = importer.ProposeWorkbook(path);
        var summary = WorkbookImportSummaries.Build(proposal, new PanelEvidenceMatching(new FamilyClusterFile()));
        var accepted = WorkbookImportSummaries.AcceptAll(summary, importer, summary.ReviewedSubclassSelections, summary.ProposedEvidenceSelections);
        Check("IM1 the populated template imports unchanged with Instructions present and named once as skipped",
            accepted.CanImport && accepted.Panel!.Families.Select(f => f.Key).SequenceEqual(new[] { "A", "B" })
            && summary.Sections.SelectMany(s => s.Items).Count(i => i.Reason == "Sheet Instructions was skipped: it has no family columns") == 1,
            string.Join("; ", accepted.Items.SelectMany(i => i.Problems).Select(p => p.Message)));
    }

    private static void ImportUniProtColumnTests()
    {
        Section("RG4 the UniProt family column survives both template routes");
        string path = Path.Combine(ImportSimpleWork("RG4"), "empty.xlsx");
        PanelImportTemplate.Write(path);
        var cells = PanelImportTemplate.Columns.Select(column => column.Role switch
        {
            PanelImportFields.FamilyKey => "A", PanelImportFields.DisplayName => "First family",
            PanelImportFields.UniProtFamilies => " First family; Second family ; first FAMILY; ", _ => "",
        }).ToArray();
        FillTemplate(path, new[] { cells });
        var importer = new FamilyPanelSpreadsheetImporter();
        var proposal = importer.ProposeWorkbook(path);
        var panel = importer.CheckConfirmedWorkbook(proposal, proposal.FamilyProposal.Choices, proposal.FamilyProposal.Decisions).Panel!;
        Check("RG4 the reader trims and deduplicates exact semicolon separated labels",
            panel != null && panel.Families.Single().UniProtFamilies.SequenceEqual(new[] { "First family", "Second family" }));
        if (panel == null) return;
        panel.EvidenceLinks.AddRange(new[]
        {
            new FamilyPanelEvidenceLink { SourceFamily = "Third family", PanelFamilyKey = "A" },
            new FamilyPanelEvidenceLink { SourceFamily = "third FAMILY", PanelFamilyKey = "A" },
            new FamilyPanelEvidenceLink { SourceFamily = "", ArchitectureKey = "PF12345", PanelFamilyKey = "A" },
            new FamilyPanelEvidenceLink { SourceFamily = "Outside family", PanelFamilyKey = null },
        });
        path = Path.Combine(ImportSimpleWork("RG4"), "saved.xlsx");
        FamilyPanelTemplate.Write(path, panel);
        var restored = FamilyPanelTemplate.Read(path);
        var table = new PanelSpreadsheetReader().ReadTable(path, "Families", 2);
        var choices = importer.Propose(table).Choices;
        choices.ColumnHeaders[PanelImportFields.FamilyKey] = "Key";
        choices.ColumnHeaders[PanelImportFields.DisplayName] = "Name";
        var imported = importer.Check(table, choices, Array.Empty<FamilyPanelDecisionMapping>()).Panel;
        Check("RG4 saved rows carry named chosen links once, while hidden metadata retains every choice",
            restored.Families.Single().UniProtFamilies.SequenceEqual(new[] { "First family", "Second family", "Third family" })
            && restored.EvidenceLinks.Count == 4
            && imported?.Families.Single().UniProtFamilies.SequenceEqual(restored.Families.Single().UniProtFamilies) == true
            && FamilyPanelValidation.Clone(restored).Families.Single().UniProtFamilies.SequenceEqual(restored.Families.Single().UniProtFamilies));
    }
}
