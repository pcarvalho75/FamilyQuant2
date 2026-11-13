using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportCodesAsWrittenCommands() =>
    [new("--test-import-codes-as-written", 100, TestCommandPhase.Raw,
        args => args is ["--test-import-codes-as-written"], (_, _) =>
        {
            ImportCodesAsWrittenTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ImportCodesAsWrittenTests()
    {
        Section("IM10 supplied Pfam codes before Fetch evidence");
        EnsureApplication();
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "fq2", "FQDeNovo", "v2118", "IM10", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "families.xlsx");
        PanelImportTemplate.Write(path);
        FillTemplate(path, new[] {
            new[] { "A", "First family", "First other name", "First domain (PF12345); PF67890", "First source family" },
            new[] { "B", "Second family", "", "PF54321", "" },
        });
        var file = new FamilyClusterFile();
        string before = file.ToJsonText();
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)),
            workbookSource: new FamilyListWorkbookCoreSource());
        FinishReviewWork(() => model.LoadAsync(path));
        FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); });
        Check("IM10 no stored evidence: supplied codes skip Evidence matches and do not block Next",
            model.Page == 5 && model.PfamQuestions.Count == 0 && model.CanNext
            && model.MatchFamilies.Count == 2 && model.MatchFamilies.All(row => row.Choice == "Confirmed")
            && model.MatchFamilies.Sum(row => row.Suggestions.Count(choice => choice.Use && choice.IsFromSheet)) == 3
            && file.ToJsonText() == before);
        var importer = new FamilyPanelSpreadsheetImporter();
        var summary = WorkbookImportSummaries.Build(importer.ProposeWorkbook(path), new FamilyQuant.Evidence.PanelEvidenceMatching(file));
        Check("IM10 workbook review accepts codes absent from stored evidence without a required matching choice",
            !summary.HasBlockingItems && !summary.Sections.SelectMany(section => section.Items).Any(item => item.Required
                && item.Page == WorkbookImportPage.EvidenceMatches));
        var window = new FamilyListImportWindow(model) { Width = 1366, Height = 768, Left = -30000, Top = -30000,
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); PumpDispatcher();
            ScreenHarness.Save(window, Path.Combine(folder, "summary.png"), 1366, 768);
            Check("IM10 Summary exposes per-row confirmation actions before import",
                model.SummaryFamilies.All(row => row.CanUnconfirm)
                && WalkTree(window).OfType<System.Windows.Controls.Button>().Count(button => Equals(button.Content, "Un-confirm") && button.IsEnabled) == 2);
            model.UnconfirmMatches("A");
            Check("IM10 un-confirm changes only one draft row and keeps Next available",
                model.MatchFamilies.Single(row => row.Key == "A").Choice == "Unconfirmed" && model.CanNext
                && model.MatchFamilies.Single(row => row.Key == "B").Choice == "Confirmed" && file.ToJsonText() == before);
            FinishReviewWork(() => model.NextAsync());
            var reopened = FamilyClusterFile.FromJsonText(file.ToJsonText());
            Check("IM10 accepted un-confirm survives reopening while source codes and other confirmations remain",
                model.Imported && reopened.FamilyPanel!.ConfirmedEvidenceMatches?.ContainsKey("A") != true
                && reopened.FamilyPanel!.ConfirmedEvidenceMatches?.GetValueOrDefault("B")?.UserConfirmed == true
                && reopened.FamilyPanel.Families.Single(row => row.Key == "A").Pfam.Count == 2);
        }
        finally { window.Close(); }
        string namesPath = Path.Combine(folder, "names.xlsx");
        PanelImportTemplate.Write(namesPath);
        FillTemplate(namesPath, new[] { new[] { "C", "Third family", "", "Domain without a code", "" } });
        using var names = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(new FamilyClusterFile())),
            workbookSource: new FamilyListWorkbookCoreSource());
        FinishReviewWork(async () => { await names.LoadAsync(namesPath); await names.NextAsync(); await names.NextAsync(); });
        Check("IM10 a domain name without a code still opens Evidence matches and can be skipped",
            names.Page == 4 && names.PfamQuestions.Count == 1 && !names.CanNext);
        names.SkipAllMatches();
        Check("IM10 skipping the name-only question enables Next", names.CanNext);
        Check("IM10 malformed accessions do not count as supplied codes",
            new[] { "PF1234", "PF123456", "PF12345x", "xPF12345" }.All(FamilyPanelSpreadsheetImporter.NeedsPfamChoice)
            && !FamilyPanelSpreadsheetImporter.NeedsPfamChoice("Named domain (PF12345.2)"));
    }
}
