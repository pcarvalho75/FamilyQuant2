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
        args => args[0] == "--test-import-template-workbook", (_, _) =>
        {
            ImportTemplateWorkbookTests();
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
}
