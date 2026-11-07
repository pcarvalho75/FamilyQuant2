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
// box is shown with a generic documents path, because the real path holds this computer's temporary folder. Z12p2c1:
// the Columns page with its decision-column question and the fills named as colour categories, and the decisions opened
// through Use this decision column. Z16a8: the wizard reads the whole example workbook through the same Core source as
// File > Import family list (FamilyListWorkbookCoreSource), so File and sheet opens on Workbook sheets and the Columns
// page starts the colours and the decision meanings from the example's legend.
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

    // Z16a8: stored Pfam domains for the invented proteins, so the domains written in the example's rows meet stored entries.
    private static FastaItemExtended WithPfam(FastaItemExtended protein, params (string Accession, string Name)[] domains)
    {
        protein.Evidence!.Pfam = domains.Select(d => new EvidenceDomain { Accession = d.Accession, Name = d.Name }).ToList();
        return protein;
    }

    private static void ImportGuideScreens(string output, bool dark)
    {
        EnsureApplication();
        // Everything this capture writes stays under the output folder: the pictures and the workbook it imports.
        string work = Path.Combine(Path.GetFullPath(output), "example-workbook");
        Directory.CreateDirectory(work);
        string workbook = Path.Combine(work, ExampleFamilyList.DefaultFileName); ExampleFamilyList.Write(workbook);
        var file = new FamilyClusterFile
        {
            SourceSha256 = "invented-guide-source", SourceSequenceCount = 7,
            MyExtendedFastaItems = new List<FastaItemExtended>
            {
                WithPfam(GuideProtein(1, "sp|P90001|TRY1_EXAMPLE", "Trypsin-like serine protease 1", "Peptidase S1 family", true, "Serine protease", "Hydrolase", "Protease"), ("PF00089", "Trypsin")),
                WithPfam(GuideProtein(1, "tr|A0A900A002|A0A900A002_EXAMPLE", "Trypsin-like serine protease 2", "Peptidase S1 family", false, "Serine protease", "Hydrolase"), ("PF00089", "Trypsin")),
                WithPfam(GuideProtein(2, "sp|P90003|PA2_EXAMPLE", "Secreted phospholipase A2", "Phospholipase A2 family", true, "Hydrolase", "Lipid degradation"), ("PF00068", "Phospholip_A2_1")),
                WithPfam(GuideProtein(3, "sp|P90004|LEC1_EXAMPLE", "C-type lectin 1", "", true, "Lectin", "Calcium"), ("PF00059", "Lectin_C")),
                WithPfam(GuideProtein(4, "sp|P90005|KUI1_EXAMPLE", "Kunitz-type protease inhibitor 1", "", true, "Protease inhibitor", "Serine protease inhibitor"), ("PF00014", "Kunitz_BPTI")),
                WithPfam(GuideProtein(5, "sp|P90006|ACTB_EXAMPLE", "Actin, cytoplasmic", "Actin family", true, "Cytoskeleton"), ("PF00022", "Actin")),
                WithPfam(GuideProtein(6, "sp|P90007|MMP1_EXAMPLE", "Matrix metalloproteinase 1", "Peptidase M10A family", true, "Metalloprotease", "Hydrolase"),
                    ("PF00413", "Peptidase_M10"), ("PF00045", "Hemopexin"), ("PF01471", "PG_binding_1")),
            },
        };
        var previousTheme = Themes.ThemeManager.Current;
        Themes.ThemeManager.Apply(dark ? Themes.AppTheme.Dark : Themes.AppTheme.Light);
        var curation = new CurationViewModel(file);
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), matcher: new FamilyListStoredEvidenceMatches(file),
            workbookSource: new FamilyListWorkbookCoreSource(), openWorkbookReview: true);
        var window = new FamilyListImportWindow(model, path => ExampleFamilyList.Write(path), PanelImportTemplate.Write)
        { Width = 1366, Height = 768, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false };
        void Shot(string name)
        {
            // Let opening sections (the colour categories) finish their layout before the picture.
            PumpDispatcher(); System.Threading.Thread.Sleep(400); PumpDispatcher();
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
            // Z16a8 (Z16a9): the whole workbook is read at once and the wizard opens on Review your workbook, as File > Import
            // family list does. The four decision labels the legend leaves unclear and the special rows wait for her, so Accept
            // and import is unavailable; nothing is confirmed and the project has no panel yet.
            Check("guide: the example opens on Review your workbook, with its eight parts and her open choices",
                model.ReviewingWorkbook && model.Title == "Review your workbook" && model.ForwardLabel == "Accept and import" && model.WorkbookSections.Count == 8
                && !model.CanAcceptWorkbook && model.WorkbookHasReviewProblems && model.WorkbookSections.Single(s => s.Key == "decisions").NeedsChoice
                && model.WorkbookSections.Single(s => s.Key == "rows").NeedsChoice && model.Decisions.All(d => !d.Confirmed)
                && model.SubclassChoices.Count > 0 && model.SubclassChoices.All(c => !c.Confirmed) && file.FamilyPanel == null,
                string.Join(" | ", model.WorkbookSections.Select(s => s.Title + ": " + s.Summary)));
            Shot("02-workbook");
            FinishReviewWork(() => model.ReviewWorkbookSectionAsync("sheets")); PumpDispatcher();
            // Z16a8: the six sheets of the example, each with its role; the three notes sheets and the legend's text are kept.
            Check("guide: Review on Workbook sheets opens File and sheet on the map of the six example sheets",
                model.Page == 1 && model.HasWorkbookMap && model.WorkbookSheets.Count == 6 && model.WorkbookSheets.Count(s => s.Role == PanelWorkbookSheetRole.Notes) == 3
                && model.WorkbookSheets.Count(s => s.Role == PanelWorkbookSheetRole.FamilyList) == 1 && model.WorkbookSheets.Count(s => s.Role == PanelWorkbookSheetRole.ColorLegend) == 1
                && model.WorkbookSheets.Count(s => s.Role == PanelWorkbookSheetRole.SubclassTable) == 1 && model.WorkbookSheets.All(s => s.Review == "Proposed")
                && model.KeptNotes.Count == 4 && model.KeptNotesSummary.StartsWith("Notes kept: ", StringComparison.Ordinal)
                && ((TabControl)window.FindName("WorkbookTabs")).SelectedItem == window.FindName("WorkbookMapTab"),
                string.Join(", ", model.WorkbookSheets.Select(s => s.Name + "=" + s.Role + "/" + s.Review)) + "; " + model.KeptNotesSummary);
            Shot("02-workbook-map");
            ((TabControl)window.FindName("WorkbookTabs")).SelectedItem = window.FindName("SheetPreviewTab"); PumpDispatcher();
            Shot("02-file-and-sheet");
            Check("guide: the example's family list has its header row 3", model.Preview?.ProposedHeaderRow == 3);
            ((Button)window.FindName("ForwardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            // Z16a8: the legend's labels are the values of Decisão FamilyQuant and its colours those of Sigla, so both columns are
            // proposed, with Subclasses for the subclass names; each colour starts with the legend's name, or with its plain name
            // when the legend gives that colour two names.
            Check("guide: Columns shows the columns proposed from the legend and each fill named from it",
                model.Page == 2 && !model.ReviewingDecisions && model.Columns.Single(c => c.Role == PanelImportFields.Decision).Header == "Decisão FamilyQuant"
                && model.ColorColumn?.Header == "Sigla" && model.Columns.Any(c => c.Header == "Subclasses")
                && model.SourceColorChoices.Count == 6 && model.SourceColorChoices.Count(c => c.Label != c.ColorName) == 4
                && model.SourceColorChoices.Any(c => c.ColorName == "light turquoise" && c.Label == "Núcleo")
                && model.SourceColorChoices.Any(c => c.ColorName == "light grey" && c.Label == "light grey"),
                string.Join(", ", model.Columns.Select(c => c.Label + "=" + c.Header)) + "; " + string.Join(", ", model.SourceColorChoices.Select(c => c.ColorName + "=" + c.Label)));
            Shot("03-columns");
            // The colour categories sit below the column choices; the picture of them is taken with the page scrolled to them.
            var colours = (Expander)window.FindName("ColourCategories");
            colours.BringIntoView(); PumpDispatcher();
            Check("guide: Colour categories is open below the column choices", colours.IsExpanded && colours.IsVisible);
            Shot("03-columns-colours");
            ((Button)WalkTree(window).OfType<Button>().First(b => b.Content as string == "Review decision values")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            // Z16a8: each value with its meaning from the legend; nothing is confirmed, and unclear meanings stay empty.
            Check("guide: Review decision values shows the eight values with their meanings, four proposed, four empty, none confirmed",
                model.ReviewingDecisions && model.Decisions.Count == 8 && model.Decisions.All(d => d.Meaning.Length > 0 && !d.Confirmed)
                && model.Decisions.Count(d => d.Decision == null) == 4
                && model.Decisions.Any(d => d.Decision == FamilyPanelDecision.MergeIntoAnotherFamily && d.MergeTarget == "LEC") && !model.CanNext,
                string.Join("; ", model.Decisions.Select(d => d.Source + " -> " + (d.Decision?.ToString() ?? "(empty)") + " " + d.MergeTarget)));
            Shot("03-columns-decisions");
            // Her choices for the four labels the legend leaves empty, as a reader of this example would make them: the
            // families out of the total stay out, and the optional, conditional and pending ones are kept for now.
            foreach (var open in model.Decisions.Where(d => d.Decision == null))
                open.Decision = open.Meaning.StartsWith("Fora do total", StringComparison.Ordinal) ? FamilyPanelDecision.NotAFamily : FamilyPanelDecision.Quantify;
            model.ConfirmDecisions();
            Check("guide: every decision value is confirmed only by Confirm these actions", model.Decisions.All(d => d.Confirmed) && model.CanNext, model.Status);
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
            // Z16a8 (Z16a4): the Pfam domain written in TRP's row comes first, marked from your sheet and ticked.
            Check("guide: TRP's Pfam domain from the sheet comes first, marked and ticked",
                model.SelectedMatchFamily.Suggestions.First() is { Text: "PF00089", Use: true, IsFromSheet: true, Origin: "from your sheet" } sheet && sheet.Proteins == 2,
                string.Join(", ", model.SelectedMatchFamily.Suggestions.Select(s => s.Text + (s.Use ? "*" : "") + " " + s.Origin + " " + s.Proteins)));
            Shot("05-evidence-matches");
            model.ConfirmMatches(_ => true);
            model.SelectedMatchFamily = model.MatchFamilies.First(f => f.Key == "PLA");
            var lipase = model.SelectedMatchFamily.Suggestions.FirstOrDefault(s => s.Text == "Phospholipase A2 family") ?? model.SelectedMatchFamily.Suggestions.First();
            lipase.Use = true; model.ConfirmMatches(_ => true);
            Check("guide: two families confirmed", model.MatchFamilies.Count(f => f.Choice == "Confirmed") == 2);
            while (model.MatchFamilies.FirstOrDefault(f => !f.Decided) is { } next) { model.SelectedMatchFamily = next; model.SkipMatch(); }
            FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
            model.PanelName = "Example family list";
            Check("guide: Summary shows the colour category of the families that have one, and goes back to the workbook review",
                model.Page == 5 && model.SummaryFamilies.Any(f => !string.IsNullOrEmpty(f.Color)) && model.ForwardLabel == "Review workbook");
            Shot("06-summary");
            FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
            // Z16a8 (Z16a5, Z16a9): the subclasses the table proposes, each with its domain rules, in the draft and unconfirmed.
            FinishReviewWork(() => model.ReviewWorkbookSectionAsync("subclasses")); PumpDispatcher();
            model.SelectedSubclass = model.SubclassChoices.First(c => c.Family == "MMP"); PumpDispatcher();
            Check("guide: Review on Subclass proposals opens the subclasses proposed from 2. Subclasses, none confirmed",
                model.ReviewingSubclasses && model.Title == "Review subclass proposals" && model.SubclassChoices.All(c => !c.Confirmed)
                && model.SelectedSubclass.Rules.Any(r => r.Accession == "PF00413") && model.SelectedSubclass.Rules.Any(r => r.Accession == "PF00045"),
                string.Join("; ", model.SubclassChoices.Select(c => c.Family + "/" + c.Title + " " + string.Join(",", c.Rules.Select(r => r.Accession + " " + r.Kind)))));
            Shot("05-subclasses");
            model.Back(); PumpDispatcher();
            Check("guide: back on Review your workbook, Accept and import is available once her choices are made",
                model.ReviewingWorkbook && model.CanAcceptWorkbook && !model.WorkbookHasReviewProblems && file.FamilyPanel == null, model.WorkbookReviewStatus);
            Shot("06-accept");
            int subclasses = model.SubclassChoices.Count(c => c.Use);
            FinishReviewWork(async () => { await model.NextAsync(_ => true); }); PumpDispatcher();
            Shot("07-done");
            Check("guide: Accept and import imports the panel with the notes kept and the subclasses left in the draft", model.Imported
                && file.FamilyPanel?.Name == "Example family list" && file.FamilyPanel.SourceNotes.Count == 4
                && file.FamilyPanel.Families.Sum(f => f.Subclasses.Count) == subclasses && curation.UndoStack.Count == 1);
            Check("guide: the generated workbook is written under the output folder", Path.GetFullPath(workbook)
                .StartsWith(Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), workbook);
        }
        finally { window.Close(); PumpDispatcher(); Themes.ThemeManager.Apply(previousTheme); }
    }
}
