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
        Check("the guide explains direct defaults and conditional choices", text.Contains("three pages", StringComparison.Ordinal)
            && text.Contains("Without a Decision column, every family is quantified", StringComparison.Ordinal)
            && !text.Contains("Colour categories (optional)", StringComparison.Ordinal) && text.Contains(FamilyListImportHelp.DecisionsSummary, StringComparison.Ordinal));
        Check("the guide retains grouping prerequisites and the matching boundary", text.Contains(FamilyListImportHelp.DoneNext, StringComparison.Ordinal)
            && text.Contains(FamilyListImportHelp.MatchingEffect, StringComparison.Ordinal));
        Check("the normal pages are numbered once in order", sections.Where(section => char.IsDigit(section.Title[0]))
            .Select(section => section.Title[0]).SequenceEqual(new[] { '1', '2', '3' }));
        Check("optional decisions, repeated rows, subclass proposals and findings remain documented",
            new[] { "decisions", "duplicate", "special", "matches", "subclasses", "workbook", "accept" }.All(key => sections.Any(section => section.Key == key)));
        Check("the guide explains direct PF codes, missing codes and explicit unresolved choices",
            text.Contains("They do not ask for a second confirmation", StringComparison.Ordinal)
            && text.Contains("gets no proteins until it has one", StringComparison.Ordinal)
            && text.Contains("Continue without matches skips unresolved questions", StringComparison.Ordinal));
        Check("source notes and publication boundaries remain explicit", text.Contains("Every original column stays source text", StringComparison.Ordinal)
            && text.Contains("original field for a family", StringComparison.Ordinal) && text.Contains("one panel edit", StringComparison.Ordinal)
            && text.Contains("Undo in the main window restores the previous panel", StringComparison.Ordinal)
            && text.Contains("No grouping or quantitation runs", StringComparison.Ordinal));
        var pictured = FamilyListImportGuide.Pictured.ToList();
        var loaded = pictured.Select(s =>
        {
            try { var image = new BitmapImage(FamilyListImportGuide.ImageUri(s)); return (Section: s, image.PixelWidth, image.PixelHeight); }
            catch (Exception ex) when (ex is IOException or NotSupportedException) { return (Section: s, PixelWidth: 0, PixelHeight: 0); }
        }).ToList();
        Check("the normal pages carry current 1366 x 820 native pictures and the unchanged subclass picture remains",
            pictured.Count == 4 && new[] { "file", "columns", "summary" }.All(key => pictured.Any(section => section.Key == key))
            && loaded.All(p => p.PixelWidth == 1366 && p.PixelHeight == (p.Section.Key == "subclasses" ? 768 : 820)),
            string.Join(", ", loaded.Select(p => p.Section.Key + "=" + p.PixelWidth + "x" + p.PixelHeight)));
        Check("embedded guide pictures match the current native source captures", pictured.All(section =>
        {
            using var stream = Application.GetResourceStream(FamilyListImportGuide.ImageUri(section))?.Stream;
            string source = Path.Combine(GoldenNumbers.FindRepository(), "FamilyQuant", "Help", "ImportFamilyList", section.Image);
            return stream != null && Convert.ToHexString(SHA256.HashData(stream)) == TestFileHash.Upper(source);
        }));
        string[] labels =
        {
            "Use template", "Edit current panel", "Browse", "Read this header row", "Cell details", "Confirm these actions",
            "Quantify", "Report under others", "Not a family", "Merge into family", "Use selected matches", "Skip this family",
            "Continue without matches", "View source notes", "Accept and import", "Source details", "Include subclass", "Include rule",
            "Required Pfam", "Absent Pfam", "Check choices", "Keep subclass choices",
        };
        Check("the chapter uses the live controls' names and keeps the saving instruction", labels.All(text.Contains)
            && text.Contains("File > Save", StringComparison.Ordinal), string.Join(", ", labels.Where(label => !text.Contains(label))));
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
        Check("the chapter keeps the next-step and matching boundary in the program's own words",
            text.Contains(FamilyListImportHelp.DecisionsSummary, StringComparison.Ordinal) && text.Contains(FamilyListImportHelp.DoneNext, StringComparison.Ordinal)
            && text.Contains(FamilyListImportHelp.MatchingEffect, StringComparison.Ordinal)
            && FamilyListImportHelp.DoneNext.Contains("reads the database and scope by itself", StringComparison.Ordinal)
            && FamilyListImportHelp.MatchingEffect.Contains("place them by hand, then Apply grouping", StringComparison.Ordinal));
        Check("the chapter explains exact program labels and does not retain the removed Quantify prompt",
            text.Contains("Exact program action names are accepted directly", StringComparison.Ordinal)
            && !text.Contains("Quantify every family for this import?", StringComparison.Ordinal));
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
