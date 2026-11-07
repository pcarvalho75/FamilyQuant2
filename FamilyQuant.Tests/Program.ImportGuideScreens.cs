using FamilyQuant.Evidence;
using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

// Z12p2c capture of the Help chapter pictures: the Import family list wizard on the neutral example list with a small
// invented project, 1366 x 768, one picture per page (light by default, --dark for the website's dark set). The Workbook
// box is shown with a generic documents path, because the real path holds this computer's temporary folder.
internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportGuideScreenCommands() =>
    [
        new("--import-guide-screens", 10100, TestCommandPhase.Raw, args => args.Contains("--import-guide-screens", StringComparer.Ordinal), (args, _) =>
        {
            ImportGuideScreens(args[Array.IndexOf(args, "--import-guide-screens") + 1], args.Contains("--dark", StringComparer.Ordinal));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static FastaItemExtended GuideProtein(int id, string identifier, string description, string family, bool reviewed, params string[] keywords) => new()
    {
        SequenceIdentifier = identifier, Description = description, Sequence = "ACDEFGHIKLMNPQRSTVWY", UserID = id, USearchID = id,
        Evidence = new ProteinEvidence { Accession = identifier.Split('|')[1], Family = family, Reviewed = reviewed, Fragment = false, Keywords = keywords.ToList() },
    };

    private static void ImportGuideScreens(string output, bool dark)
    {
        EnsureApplication();
        // Everything this capture writes stays under the output folder: the pictures and the workbook it imports.
        string work = Path.Combine(Path.GetFullPath(output), "example-workbook");
        Directory.CreateDirectory(work);
        string workbook = Path.Combine(work, ExampleFamilyList.DefaultFileName); ExampleFamilyList.Write(workbook);
        var file = new FamilyClusterFile
        {
            SourceSha256 = "invented-guide-source", SourceSequenceCount = 6,
            MyExtendedFastaItems = new List<FastaItemExtended>
            {
                GuideProtein(1, "sp|P90001|TRY1_EXAMPLE", "Trypsin-like serine protease 1", "Peptidase S1 family", true, "Serine protease", "Hydrolase", "Protease"),
                GuideProtein(1, "tr|A0A900A002|A0A900A002_EXAMPLE", "Trypsin-like serine protease 2", "Peptidase S1 family", false, "Serine protease", "Hydrolase"),
                GuideProtein(2, "sp|P90003|PA2_EXAMPLE", "Secreted phospholipase A2", "Phospholipase A2 family", true, "Hydrolase", "Lipid degradation"),
                GuideProtein(3, "sp|P90004|LEC1_EXAMPLE", "C-type lectin 1", "", true, "Lectin", "Calcium"),
                GuideProtein(4, "sp|P90005|KUI1_EXAMPLE", "Kunitz-type protease inhibitor 1", "", true, "Protease inhibitor", "Serine protease inhibitor"),
                GuideProtein(5, "sp|P90006|ACTB_EXAMPLE", "Actin, cytoplasmic", "Actin family", true, "Cytoskeleton"),
            },
        };
        var previousTheme = Themes.ThemeManager.Current;
        Themes.ThemeManager.Apply(dark ? Themes.AppTheme.Dark : Themes.AppTheme.Light);
        var curation = new CurationViewModel(file);
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), matcher: new FamilyListStoredEvidenceMatches(file));
        var window = new FamilyListImportWindow(model, path => ExampleFamilyList.Write(path), PanelImportTemplate.Write)
        { Width = 1366, Height = 768, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false };
        void Shot(string name)
        {
            PumpDispatcher();
            var content = (FrameworkElement)window.Content; content.Measure(new Size(1366, 768)); content.Arrange(new Rect(0, 0, 1366, 768)); content.UpdateLayout();
            PumpDispatcher();
            Directory.CreateDirectory(output);
            ScreenHarness.Save(window, Path.Combine(output, name + ".png"), 1366, 768);
        }
        try
        {
            window.Show(); PumpDispatcher();
            Shot("01-welcome");
            FinishReviewWork(() => model.LoadAsync(workbook)); PumpDispatcher();
            // The real path holds this computer's temporary folder; the picture shows a generic documents path instead.
            var pathBox = (TextBox)window.FindName("WorkbookPathBox");
            pathBox.Text = @"C:\Users\you\Documents\example-family-list.xlsx";
            Shot("02-file-and-sheet");
            Check("guide: the example opens on its sheet with header row 3", model.Page == 1 && model.Preview?.ProposedHeaderRow == 3);
            ((Button)window.FindName("ForwardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            Shot("03-columns");
            var decision = model.Columns.Single(c => c.Role == PanelImportFields.Decision);
            decision.Header = decision.Headers.First(h => h.Contains("Decis", StringComparison.OrdinalIgnoreCase));
            PumpDispatcher();
            Shot("03-columns-decisions");
            Check("guide: eight decision values to review", model.Decisions.Count == 8 && model.ReviewingDecisions);
            model.ConfirmDecisions();
            FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
            model.SelectedSpecialRow = model.SpecialRows.Single(r => r.SeveralNames && r.Parts.Count == 4 && r.Blocking);
            Shot("04-special-rows");
            model.SelectedSpecialRow.Action = FamilyPanelRowAction.Split; model.UseRowChoice(); PumpDispatcher();
            model.SelectedSpecialRow = model.SpecialRows.Single(r => r.Duplicate);
            model.SelectedSpecialRow.Action = FamilyPanelRowAction.Import; PumpDispatcher();
            Shot("04-special-rows-duplicate");
            model.UseRowChoice();
            while (model.SpecialRows.FirstOrDefault(r => r.Blocking) is { } open)
            { model.SelectedSpecialRow = open; open.Action = FamilyPanelRowAction.Import; model.UseRowChoice(); }
            Check("guide: every special row has a choice", model.CanNext);
            FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
            model.SelectedMatchFamily = model.MatchFamilies.First(f => f.Key == "TRP");
            var family = model.SelectedMatchFamily.Suggestions.FirstOrDefault(s => s.Text == "Peptidase S1 family") ?? model.SelectedMatchFamily.Suggestions.First();
            family.Use = true; PumpDispatcher();
            Shot("05-evidence-matches");
            model.ConfirmMatches();
            model.SelectedMatchFamily = model.MatchFamilies.First(f => f.Key == "PLA");
            var lipase = model.SelectedMatchFamily.Suggestions.FirstOrDefault(s => s.Text == "Phospholipase A2 family") ?? model.SelectedMatchFamily.Suggestions.First();
            lipase.Use = true; model.ConfirmMatches();
            Check("guide: two families confirmed", model.MatchFamilies.Count(f => f.Choice == "Confirmed") == 2);
            while (model.MatchFamilies.FirstOrDefault(f => !f.Decided) is { } next) { model.SelectedMatchFamily = next; model.SkipMatch(); }
            FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
            model.PanelName = "Example family list";
            Shot("06-summary");
            FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
            Shot("07-done");
            Check("guide: imported", model.Imported && file.FamilyPanel?.Name == "Example family list");
            Check("guide: the generated workbook is written under the output folder", Path.GetFullPath(workbook)
                .StartsWith(Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), workbook);
        }
        finally { window.Close(); PumpDispatcher(); Themes.ThemeManager.Apply(previousTheme); }
    }
}
