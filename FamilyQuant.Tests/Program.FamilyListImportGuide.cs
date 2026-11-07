using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyListImportGuideCommands() =>
    [
        new("--test-import-guide", 10100, TestCommandPhase.Raw, args => args.Contains("--test-import-guide", StringComparer.Ordinal), (args, _) =>
        {
            int at = Array.IndexOf(args, "--test-import-guide");
            FamilyListImportGuideTests(at + 1 < args.Length && args[at + 1] != "." ? args[at + 1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>Z12p2c: the Help chapter "Import family list", one part per wizard page with its picture, the
    /// routes that save matches later, and the window that shows it.</summary>
    private static void FamilyListImportGuideTests(string? output = null)
    {
        Section("Import guide: the Help chapter for File > Import family list (Z12p2c, Z12p2c1, Z16a8)");
        EnsureApplication();
        var sections = FamilyListImportGuide.Sections;
        string text = FamilyListImportGuide.Text();
        Check("the guide uses the current import and checklist names", FamilyListImportGuide.Title == "Import family list"
            && !text.Contains("Import family panel", StringComparison.Ordinal) && !text.Contains("Panel checklist", StringComparison.Ordinal));
        Check("the guide describes optional source categories and explicit default decisions", text.Contains("Colour categories (optional) opens by itself", StringComparison.Ordinal)
            && text.Contains("Next asks once", StringComparison.Ordinal) && text.Contains(FamilyListImportHelp.DecisionsSummary, StringComparison.Ordinal));
        Check("the guide retains the Done prerequisites and matching boundary", text.Contains(FamilyListImportHelp.DoneNext, StringComparison.Ordinal)
            && text.Contains(FamilyListImportHelp.MatchingEffect, StringComparison.Ordinal));
        Check("one part for each of the seven wizard pages, in order", Enumerable.Range(1, 7).All(n => sections.Any(s => s.Title.StartsWith(n + ".", StringComparison.Ordinal)))
            && sections.Where(s => char.IsDigit(s.Title[0])).Select(s => s.Title[0]).SequenceEqual(sections.Where(s => char.IsDigit(s.Title[0])).Select(s => s.Title[0]).OrderBy(c => c)));
        Check("the Columns page has its decision review and Special rows its repeated abbreviation", sections.Any(s => s.Key == "decisions") && sections.Any(s => s.Key == "duplicate"));
        // Z16a8 (W21, D81): a workbook with several sheets, read at once, with nothing set before her one press.
        Check("the chapter covers a workbook with several sheets: Review your workbook, the map, colours, subclasses and the one press",
            new[] { "workbook", "file", "preview", "colours", "subclasses", "accept" }.All(key => sections.Any(s => s.Key == key))
            && text.Contains("it accepts everything the page shows, exactly as shown", StringComparison.Ordinal)
            && text.Contains("that press is your confirmation, and nothing is set before it", StringComparison.Ordinal)
            && text.Contains("it never guesses", StringComparison.Ordinal) && text.Contains("never by their names", StringComparison.Ordinal));
        Check("the chapter keeps every workbook proposal her choice: legend meanings, her Pfam domains, subclass rules, notes",
            text.Contains("each line is still your choice", StringComparison.Ordinal) && text.Contains("the action is left empty for you to choose", StringComparison.Ordinal)
            && text.Contains("because they are your own statement", StringComparison.Ordinal) && text.Contains("Untick any you do not want", StringComparison.Ordinal)
            && text.Contains("Nothing becomes a rule until you press Accept and import", StringComparison.Ordinal)
            // Z16a12: polarity from the column headings, and her check of each rule still asked for.
            && text.Contains("Whether a domain is required or absent is read from the column headings", StringComparison.Ordinal)
            && text.Contains("Still check each rule before accepting", StringComparison.Ordinal) && text.Contains("never read as families or as rules", StringComparison.Ordinal));
        var pictured = FamilyListImportGuide.Pictured.ToList();
        var loaded = pictured.Select(s =>
        {
            try { var image = new BitmapImage(FamilyListImportGuide.ImageUri(s)); return (Section: s, image.PixelWidth, image.PixelHeight); }
            catch (Exception ex) when (ex is IOException or NotSupportedException) { return (Section: s, PixelWidth: 0, PixelHeight: 0); }
        }).ToList();
        Check("every wizard page has its picture inside the program, at 1366 x 768", pictured.Count == 14
            && loaded.All(p => p.PixelWidth == 1366 && p.PixelHeight == 768), string.Join(", ", loaded.Where(p => p.PixelWidth != 1366).Select(p => p.Section.Image)));
        Check("embedded guide pictures match the current native source captures", pictured.All(section =>
        {
            using var stream = Application.GetResourceStream(FamilyListImportGuide.ImageUri(section))?.Stream;
            string source = Path.Combine(GoldenNumbers.FindRepository(), "FamilyQuant", "Help", "ImportFamilyList", section.Image);
            return stream != null && Convert.ToHexString(SHA256.HashData(stream)) == TestFileHash.Upper(source);
        }));
        string[] labels =
        {
            "Try with the example", "Save a template", "Edit current panel", "Browse", "Read this header row", "Cell details",
            "Confirm these actions", "Back to columns", "Review decision values", "Quantify", "Report under others", "Not a family", "Merge into family",
            "Original source row", "Use this row as", "Use this choice", "Needs a choice", "Keep as one family", "One family per name",
            "One group reported under others", "Keep or rename this row", "Skip this row", "Skipped", "Use selected matches", "Skip this family",
            "Continue without matches", "Kept from previous panel", "View source notes", "Import", "Family checklist", "Undo import", "Close",
            "Use this decision column", "Match stored names", "Apply grouping", "Apply panel", "Review spreadsheet decisions",
            "Colour categories (optional)", "Colour", "Group by evidence", "Read database and scope", "Update proposal", "Place outside matches by hand",
            "Import family list",
            // Z16a8 (Z16a3): the workbook map and the legend meanings.
            "Workbook sheets", "Use as", "Kept as", "Why this role", "Review role", "Proposed", "Family list", "Colour legend", "Subclass table", "Notes",
            "Use selected family sheet", "Read kept notes", "Kept notes preview", "Kept as source notes", "Sheet preview", "Source notes", "Meaning from legend",
            "Use as (proposed)", "Merge into abbreviation", "Source colour", "Source rows", "Category name",
            // Z16a8 (Z16a4): her Pfam domains on Evidence matches.
            "Pfam domain", "from your sheet",
            // Z16a8 (Z16a9, Z16a5): Review your workbook, its parts and the subclass review.
            "Review your workbook", "Accept and import", "Review findings", "Back to workbook review", "Review workbook", "Family list and columns",
            "Source categories", "Decision labels", "Family rows", "Pfam and evidence matches", "Subclass proposals", "All findings", "Could not place",
            "Families", "Special rows", "Subclasses", "Review subclass proposals", "Include subclass", "Include rule", "Leave out", "Required Pfam",
            "Absent Pfam", "Check choices", "Keep subclass choices", "Family key", "Subclass key", "Display name", "Pfam rules", "Column with coloured cells",
        };
        Check("the chapter names the wizard's controls and choices with the program's own words", labels.All(text.Contains) && text.Contains("File > Save", StringComparison.Ordinal),
            string.Join(", ", labels.Where(l => !text.Contains(l))));
        // Z12p2c1: each of those words is still a label of the program, a string of its own sources, so a renamed control fails here.
        string repository = GoldenNumbers.FindRepository();
        string program = string.Concat(new[] { "FamilyListImportWindow.xaml", "FamilyListImportViewModel.cs", "FamilyListImportViewModel.Rows.cs",
                "FamilyListImportViewModel.Commit.cs", "FamilyListImportModels.cs", "SuperClusterPreviewWindow.xaml", "PanelEditorWindow.xaml",
                "MainWindow.xaml", "ClusterGenerationControl.xaml", "FamilyListImportWorkbookModels.cs", "FamilyListImportViewModel.Workbook.cs", "FamilyListStoredEvidenceMatches.cs",
                "FamilyListImportViewModel.Overview.cs", "FamilyListWorkbookOverview.xaml", "FamilyListSubclassReview.xaml", "FamilyListSubclassReviewModels.cs" }
            .Select(name => File.ReadAllText(Path.Combine(repository, "FamilyQuant", name))));
        Check("every control the chapter names is a label in the program's sources", labels.All(l => program.Contains("\"" + l + "\"", StringComparison.Ordinal)
            || program.Contains("\"" + l + "...\"", StringComparison.Ordinal)),
            string.Join(", ", labels.Where(l => !program.Contains("\"" + l + "\"", StringComparison.Ordinal) && !program.Contains("\"" + l + "...\"", StringComparison.Ordinal))));
        Check("the chapter quotes the wizard's own lines: decisions, the Quantify question and the Done page",
            text.Contains(FamilyListImportHelp.DecisionsSummary, StringComparison.Ordinal) && text.Contains(FamilyListImportHelp.DoneNext, StringComparison.Ordinal)
            && text.Contains(FamilyListImportHelp.MatchingEffect, StringComparison.Ordinal)
            // The chapter's own sentences around the Done lines assume this wording (Z12p5g, 7b2cfc75): the read starts by
            // itself, and proteins move only after a scope change or a hand placement plus Apply grouping.
            && FamilyListImportHelp.DoneNext.Contains("reads the database and scope by itself", StringComparison.Ordinal)
            && FamilyListImportHelp.MatchingEffect.Contains("place them by hand, then Apply grouping", StringComparison.Ordinal)
            && program.Contains("Quantify every family for this import?", StringComparison.Ordinal) && text.Contains("Quantify every family for this import?", StringComparison.Ordinal)
            && program.Contains("\"Could \" + DecisionHint + \" hold the family actions?\"", StringComparison.Ordinal) && text.Contains("hold the family actions?", StringComparison.Ordinal));
        Check("the chapter says there is no conditional action and that the program's own labels are confirmed at once",
            text.Contains("There is no conditional action", StringComparison.Ordinal) && text.Contains("is confirmed at once", StringComparison.Ordinal)
            && text.Contains("named for you with its plain name", StringComparison.Ordinal) && text.Contains("never hold up Next", StringComparison.Ordinal));
        Check("both later routes for saving matches are there, with and without grouping proteins",
            text.Contains("Apply grouping and File > Save keep the choices made in that review", StringComparison.Ordinal)
            && text.Contains("press Edit current panel, select the family, use Match stored names, then Apply panel and File > Save", StringComparison.Ordinal));
        Check("D21: nothing in the chapter names an organism, a venom or a sample's numbers", !DatabaseSpecificCopy.IsMatch(text) && !HistoricalSampleCopy.IsMatch(text),
            DatabaseSpecificCopy.Match(text).Value + HistoricalSampleCopy.Match(text).Value);
        Check("no em or en dash in the chapter", !text.Contains('\u2013') && !text.Contains('\u2014'));
        string index = File.ReadAllText(Path.Combine(GoldenNumbers.FindRepository(), "docs", "INDEX.md"));
        Check("docs/INDEX.md lists the command that takes the chapter's pictures", index.Contains("`--import-guide-screens <output-dir> [--dark]`", StringComparison.Ordinal));
        // F1 and Help wiring is FQHome's: --test-import-help-routes runs it in the suite.

        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Light, Themes.AppTheme.Dark })
            {
                Themes.ThemeManager.Apply(theme);
                var window = new FamilyListImportGuideWindow("file") { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    var model = window.Model;
                    Check(theme + " the chapter opens at the part asked for, with its picture", model.Current.Key == "file" && model.HasPicture
                        && ((Image)window.FindName("pagePicture")).Source != null);
                    Check(theme + " it opens at a size that fits a 1024 x 768 screen", window.Width <= 1024 && window.Height <= 728);
                    if (theme == Themes.AppTheme.Light) CheckTooltipCoverage("Import guide window", (FrameworkElement)window.Content);
                    ((Button)window.FindName("nextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    Check(theme + " Next moves to the following part", model.Current.Key == "preview" && model.CanPrevious);
                    model.Current = model.Sections.Single(s => s.Key == "later"); PumpDispatcher();
                    Check(theme + " a part without a picture hides the picture frame", !model.HasPicture);
                    model.Current = model.Sections[^1]; PumpDispatcher();
                    Check(theme + " the last part disables Next", !model.CanNext && !((Button)window.FindName("nextButton")).IsEnabled);
                    if (output != null)
                    {
                        Directory.CreateDirectory(output);
                        foreach (string key in new[] { "special", "later" })
                        {
                            model.Current = model.Sections.Single(s => s.Key == key);
                            var content = (FrameworkElement)window.Content;
                            content.Measure(new Size(1000, 700)); content.Arrange(new Rect(0, 0, 1000, 700)); content.UpdateLayout(); PumpDispatcher();
                            System.Threading.Thread.Sleep(150); PumpDispatcher();
                            ScreenHarness.Save(window, Path.Combine(output, "z12p2c-guide-" + theme.ToString().ToLowerInvariant() + "-" + key + ".png"), 1000, 700);
                        }
                    }
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
