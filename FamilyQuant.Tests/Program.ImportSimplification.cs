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
