using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SheetPfamMatchCommands() =>
    [new("--test-sheet-pfam-matches", 74, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-sheet-pfam-matches", (args, _) =>
    {
        SheetPfamMatchTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.SheetPfam"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];

    private static FamilyPanel SheetPfamPanel()
    {
        var table = new PanelSpreadsheetTable { SourceName = "invented-list.tsv", SheetName = "List", Headers = new() { "Key", "Name", "Pfam" }, ColumnLetters = new() { "A", "B", "C" },
            Rows = new() { new() { RowNumber = 2, Cells = new() { ["Key"] = "sheet-family", ["Name"] = "Invented sheet family", ["Pfam"] = "First fold (PF90001); Other fold (PF90002)" } } } };
        var importer = new FamilyPanelSpreadsheetImporter();
        var proposal = importer.ProposeWorkbook(new PanelWorkbookRead { SourceName = table.SourceName,
            Sheets = new() { new() { SheetName = "List", Table = table } } });
        Check("Z16a4 unified workbook proposal exposes exact row Pfam facts", proposal.Pfam is [{ RowNumber: 2, FamilyKey: "sheet-family", Column: "Pfam" }]
            && proposal.Pfam[0].Domains.Select(domain => domain.Accession).SequenceEqual(new[] { "PF90001", "PF90002" })
            && proposal.Pfam[0].Domains.Select(domain => domain.Name).SequenceEqual(new[] { "First fold", "Other fold" }), JsonSerializer.Serialize(proposal.Pfam));
        if (proposal.Problems.Any(problem => problem.Severity == PanelImportSeverity.Blocking)) throw new InvalidDataException("Synthetic workbook proposal is blocked.");
        return importer.Import(proposal.FamilyTable!, proposal.FamilyProposal.Choices, Array.Empty<FamilyPanelDecisionMapping>(), "Invented family list");
    }

    private static void SheetPfamWorkbookChoiceTests()
    {
        var importer = new FamilyPanelSpreadsheetImporter();
        PanelWorkbookSheet Sheet(string sheet, string key, string domain) => new()
        {
            SheetName = sheet,
            Table = new PanelSpreadsheetTable { SourceName = "invented-choice.xlsx", SheetName = sheet,
                Headers = new() { "Key", "Name", "Pfam" }, ColumnLetters = new() { "A", "B", "C" },
                Rows = new() { new() { RowNumber = 2, Cells = new() { ["Key"] = key,
                    ["Name"] = "Invented sheet family", ["Pfam"] = "Invented fold (" + domain + ")" } } } }
        };
        var workbook = new PanelWorkbookRead { SourceName = "invented-choice.xlsx",
            Sheets = new() { Sheet("First", "AA", "PF90001"), Sheet("Second", "BB", "PF90002") } };
        var first = importer.ProposeWorkbook(workbook, "First");
        Check("Z16a4 explicit first sheet publishes its Pfam facts exactly once", first.Pfam is [{ FamilyKey: "AA", Column: "Pfam" }]
            && first.Pfam[0].Domains is [{ Accession: "PF90001" }]);
        var second = importer.ProposeWorkbook(workbook, "Second");
        Check("Z16a4 changing family sheet replaces source Pfam facts", second.Pfam is [{ FamilyKey: "BB", Column: "Pfam" }]
            && second.Pfam[0].Domains is [{ Accession: "PF90002" }]);
        var blocked = importer.ProposeWorkbook(workbook, "Second",
            new Dictionary<string, PanelWorkbookSheetRole> { ["Second"] = PanelWorkbookSheetRole.Notes });
        Check("Z16a4 inconsistent explicit roles block source matching facts", blocked.NeedsFamilySheetChoice && blocked.Pfam.Count == 0
            && blocked.Problems.Any(problem => problem.Severity == PanelImportSeverity.Blocking && problem.SheetName == "Second"));
        var restored = importer.ProposeWorkbook(workbook, "First");
        Check("Z16a4 rerunning a corrected sheet choice retains one source publication", restored.Pfam.Count == 1
            && first.Pfam[0].Domains[0].Accession == "PF90001" && second.Pfam[0].Domains[0].Accession == "PF90002");
    }

    private static void SheetPfamMatchTests(string output)
    {
        Directory.CreateDirectory(output);
        SheetPfamWorkbookChoiceTests();
        var panel = SheetPfamPanel(); var family = panel.Families.Single(); var file = EvidenceMatchingFile();
        foreach (var protein in file.MyExtendedFastaItems.Take(2)) protein.Evidence!.Pfam = new()
            { new() { Accession = "PF90001", Name = "Stored first fold" }, new() { Accession = "PF90003", Name = "Invented sheet fold" } };
        string before = file.ToJsonText(), panelBefore = JsonSerializer.Serialize(panel); var matching = new PanelEvidenceMatching(file);
        var rows = matching.SuggestForPanel(panel)[family.Key]; string[] expected = { "PF90001", "PF90002" };
        Check("Z16a4 synthetic Pfam column parses both named accessions", family.Pfam.Select(domain => domain.Accession).SequenceEqual(expected));
        Check("Z16a4 explicit sheet accessions precede inferred matches", rows.Take(2).Select(row => row.Value).SequenceEqual(expected));
        Check("Z16a4 sheet accessions are marked and kept in the primary list", rows.Count(row => expected.Contains(row.Value) && !row.IsAdditional && row.Reason.Contains("from your sheet", StringComparison.Ordinal)) == 2);
        var model = new PanelEvidenceMatchViewModel(new(matching, family, null, () => matching.IsCurrent(file), panel));
        Check("Z16a4 shared matching defaults tick the two sheet accessions only", model.Rows.Where(row => row.Selected).Select(row => row.Value).SequenceEqual(expected));
        var adapter = new FamilyListStoredEvidenceMatches(file); var wizard = adapter.Suggest(file, panel).Single();
        Check("Z16a4 wizard defaults tick the two sheet accessions only", wizard.Suggestions.Where(row => row.Use).Select(row => row.Text).SequenceEqual(expected));
        Check("Z16a4 matching suggestions change neither project nor imported source", file.ToJsonText() == before && JsonSerializer.Serialize(panel) == panelBefore);
        if (model.Rows.Count(row => expected.Contains(row.Value)) != 2) return;
        int prompts = 0;
        Check("Z16a4 preselected broad sheet label still needs the counted question", !model.TryConfirm(out _, text => { prompts++; return false; })
            && prompts == 1 && adapter.ConfirmationWarning(wizard.Suggestions.Where(row => row.Use).ToArray()).Contains("PF90001 (2)", StringComparison.Ordinal));
        model.Rows.Single(row => row.Value == "PF90001").Selected = false;
        Check("Z16a4 an unticked broad sheet label is not confirmed", model.TryConfirm(out var saved) && saved!.Choices.Single().Value == "PF90002");
        var kept = PanelEvidenceMatching.WithConfirmed(panel, family.Key, saved!);
        var reopened = new PanelEvidenceMatchViewModel(new(matching, kept.Families[0], kept.ConfirmedEvidenceMatches![family.Key], () => true, kept));
        Check("Z16a4 reopening shared matching honors the saved sheet untick", !reopened.Rows.Single(row => row.Value == "PF90001").Selected
            && reopened.Rows.Single(row => row.Value == "PF90002").Selected);
        var restoredFile = FamilyClusterFile.FromJsonText(before); restoredFile.FamilyPanel = kept;
        var keptWizard = new FamilyListStoredEvidenceMatches(restoredFile).Suggest(restoredFile, kept).Single();
        Check("Z16a4 reopening the wizard honors the saved sheet untick", !keptWizard.Suggestions.Single(row => row.Text == "PF90001").Use
            && keptWizard.Suggestions.Single(row => row.Text == "PF90002").Use);
        family.Pfam.Add(new() { Accession = "PF90004", Name = "A later sheet domain" });
        Check("Z16a4 changing only sheet domains invalidates the shared suggestion cache", matching.SuggestForPanel(panel)[family.Key].Any(row => row.Value == "PF90004"));
        File.WriteAllText(Path.Combine(output, "synthetic-panel.json"), JsonSerializer.Serialize(kept));
        SheetPfamMatchUiTests(file, output);
    }

    private static void SheetPfamMatchUiTests(FamilyClusterFile source, string output)
    {
        using var dialogs = new ImportFixtureDialogs();
        EnsureApplication(); var oldTheme = Themes.ThemeManager.Current;
        string workbook = Path.Combine(output, "synthetic-pfam-list.xlsx"); PanelImportTemplate.Write(workbook);
        FillTemplate(workbook, new[] { new[] { "sheet-family", "Invented sheet family", "", "", "First fold (PF90001); Other fold (PF90002)", "Quantify" } });
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme); var file = FamilyClusterFile.FromJsonText(source.ToJsonText());
                file.MyExtendedFastaItems[0].Evidence!.Pfam.Add(new() { Accession = "PF90002", Name = "Stored other fold" });
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)));
                int prompts = 0;
                var window = new FamilyListImportWindow(model) { ShowActivated = false, ShowInTaskbar = false,
                    BroadMatchConfirmationOverride = _ => { prompts++; return false; } };
                string before = file.ToJsonText();
                try
                {
                    window.Show(); FinishReviewWork(() => model.LoadAsync(workbook));
                    FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); }); PumpDispatcher();
                    var family = model.MatchFamilies.Single();
                    Check("IM6 " + theme + " valid sheet codes reach Summary with no matching question", model.Page == 5 && model.PfamQuestions.Count == 0
                        && family.Decided && prompts == 0
                        && family.PrimarySuggestions.Take(2).All(row => row.Use && row.IsFromSheet)
                        && family.PrimarySuggestions.Take(2).Select(row => row.Text).SequenceEqual(new[] { "PF90001", "PF90002" })
                        && file.ToJsonText() == before);
                    ScreenHarness.Save(window, Path.Combine(output, "sheet-pfam-wizard-" + theme + ".png"), 1366, 768);
                    var panel = model.DraftPanel!; var evidence = new PanelEvidenceMatching(file);
                    var shared = new PanelEvidenceMatchViewModel(new(evidence, panel.Families.Single(), null, () => evidence.IsCurrent(file), panel));
                    var dialog = new PanelEvidenceMatchWindow(shared) { ShowActivated = false, ShowInTaskbar = false };
                    try
                    {
                        dialog.Show(); PumpDispatcher(); var grid = (DataGrid)dialog.FindName("MatchLabelsGrid");
                        Check("Z16a4 " + theme + " native shared dialog shows checked source rows and supplied domain names", shared.Rows.Take(2).All(row => row.Selected && row.Reason == "from your sheet")
                            && shared.Rows[1].Name == "Other fold" && (grid.Columns[0].GetCellContent(shared.Rows[1]) as CheckBox)?.IsChecked == true);
                        shared.Rows.Single(row => row.Value == "PF90001").Selected = false; PumpDispatcher();
                        bool confirmed = shared.TryConfirm(out var saved);
                        var edited = confirmed ? PanelEvidenceMatching.WithConfirmed(panel, family.Key, saved!) : panel;
                        var reopened = new PanelEvidenceMatchViewModel(new(evidence, edited.Families.Single(),
                            edited.ConfirmedEvidenceMatches?.GetValueOrDefault(family.Key), () => evidence.IsCurrent(file), edited));
                        Check("Z16a4 " + theme + " the shared editor retains explicit source unticks without changing the project", confirmed
                            && reopened.Rows.Single(row => row.Value == "PF90001").Selected == false
                            && reopened.Rows.Single(row => row.Value == "PF90002").Selected && file.ToJsonText() == before);
                        ScreenHarness.Save(dialog, Path.Combine(output, "sheet-pfam-shared-" + theme + ".png"), 1180, 680);
                    }
                    finally { dialog.Close(); PumpDispatcher(); }
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
    }
}
