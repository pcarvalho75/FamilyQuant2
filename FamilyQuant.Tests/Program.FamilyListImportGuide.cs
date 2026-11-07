using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>Z12p2c: the Help chapter "Import your family list", one part per wizard page with its picture, the
    /// routes that save matches later, and the window that shows it.</summary>
    private static void FamilyListImportGuideTests(string? output = null)
    {
        Section("Import guide: the Help chapter for File > Import family panel (Z12p2c)");
        EnsureApplication();
        var sections = FamilyListImportGuide.Sections;
        string text = FamilyListImportGuide.Text();
        Check("one part for each of the seven wizard pages, in order", Enumerable.Range(1, 7).All(n => sections.Any(s => s.Title.StartsWith(n + ".", StringComparison.Ordinal)))
            && sections.Where(s => char.IsDigit(s.Title[0])).Select(s => s.Title[0]).SequenceEqual(sections.Where(s => char.IsDigit(s.Title[0])).Select(s => s.Title[0]).OrderBy(c => c)));
        Check("the Columns page has its decision review and Special rows its repeated abbreviation", sections.Any(s => s.Key == "decisions") && sections.Any(s => s.Key == "duplicate"));
        var pictured = FamilyListImportGuide.Pictured.ToList();
        var loaded = pictured.Select(s =>
        {
            try { var image = new BitmapImage(FamilyListImportGuide.ImageUri(s)); return (Section: s, image.PixelWidth, image.PixelHeight); }
            catch (Exception ex) when (ex is IOException or NotSupportedException) { return (Section: s, PixelWidth: 0, PixelHeight: 0); }
        }).ToList();
        Check("every wizard page has its picture inside the program, at 1366 x 768", pictured.Count == 9
            && loaded.All(p => p.PixelWidth == 1366 && p.PixelHeight == 768), string.Join(", ", loaded.Where(p => p.PixelWidth != 1366).Select(p => p.Section.Image)));
        string[] labels =
        {
            "Try with the example", "Save a template", "Edit current panel", "Browse", "Read this header row", "Cell details", "Keep as source notes",
            "Confirm these actions", "Back to columns", "Review decision values", "Quantify", "Report under others", "Not a family", "Merge into family",
            "Original source row", "Use this row as", "Use this choice", "Needs a choice", "Keep as one family", "One family per name",
            "One group reported under others", "Keep or rename this row", "Skip this row", "Skipped", "Use selected matches", "Skip this family",
            "Continue without matches", "Kept from previous panel", "View source notes", "Import", "Open family checklist", "Undo import", "Close",
            "Panel checklist", "Match stored names", "Apply grouping", "Apply panel", "File > Save",
        };
        Check("the chapter names the wizard's controls and choices with the program's own words", labels.All(text.Contains),
            string.Join(", ", labels.Where(l => !text.Contains(l))));
        Check("both later routes for saving matches are there, with and without grouping proteins",
            text.Contains("Apply grouping and File > Save keep the choices made in that review", StringComparison.Ordinal)
            && text.Contains("press Edit current panel, select the family, use Match stored names, then Apply panel and File > Save", StringComparison.Ordinal));
        Check("D21: nothing in the chapter names an organism, a venom or a sample's numbers", !DatabaseSpecificCopy.IsMatch(text) && !HistoricalSampleCopy.IsMatch(text),
            DatabaseSpecificCopy.Match(text).Value + HistoricalSampleCopy.Match(text).Value);
        Check("no em or en dash in the chapter", !text.Contains('–') && !text.Contains('—'));
        string index = File.ReadAllText(Path.Combine(GoldenNumbers.FindRepository(), "docs", "INDEX.md"));
        Check("docs/INDEX.md lists the command that takes the chapter's pictures", index.Contains("`--import-guide-screens <output-dir> [--dark]`", StringComparison.Ordinal));

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
                    Check(theme + " Next moves to the following part", model.Current.Key == "columns" && model.CanPrevious);
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
