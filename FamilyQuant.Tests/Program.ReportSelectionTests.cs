using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportSelectionCommandTable() =>
    [
        new("--test-report-selection", 427, TestCommandPhase.Raw,
            args => args.Contains("--test-report-selection", StringComparer.Ordinal), (_, _) => RunReportSelectionTests()),
        new("--report-export-screens", 430, TestCommandPhase.Raw,
            args => args.Length == 2 && args[0] == "--report-export-screens", (args, _) => ReportExportScreens(args[1])),
    ];

    private static int RunReportSelectionTests()
    {
        var document = new ReportDocument { Sections = new[]
        {
            new ReportSection("section-one", "First section", new ReportBlock[]
            {
                new ReportParagraph("text-one", "Text"),
                new ReportFigure("figure-one", "First figure", Array.Empty<byte>(), "Caption", 10, 10),
                new ReportFigure("figure-two", "Second figure", Array.Empty<byte>(), "Caption", 10, 10)
            }),
            new ReportSection("section-two", "Second section", Array.Empty<ReportBlock>())
        } };
        var model = new ReportExportViewModel(document, null);
        Check("every section and figure has one checkbox, selected by default", model.Choices.Count == 4 && model.Choices.All(row => row.IsIncluded));
        if (model.Choices.Count != 4) return 1;
        model.Choices.Single(row => row.Id == "figure-one").IsIncluded = false;
        Check("unticking one figure excludes only its id", model.Selection().ExcludedIds!.SequenceEqual(new[] { "figure-one" }));
        model.Choices.Single(row => row.Id == "section-two").IsIncluded = false;
        Check("section and figure choices are independent", model.Selection().ExcludedIds!.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "figure-one", "section-two" }));
        var reopened = new ReportExportViewModel(document, new ReportSelection { ExcludedIds = new[] { "future-figure", "figure-one" } });
        Check("stored choices restore each checkbox and retain unavailable ids", !reopened.Choices.Single(row => row.Id == "figure-one").IsIncluded
            && reopened.Choices.Where(row => row.Id != "figure-one").All(row => row.IsIncluded)
            && reopened.Selection().ExcludedIds!.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "future-figure", "figure-one" }));
        Check("the default threshold is displayed as 0.1 percent", model.ThresholdPercent == 0.1m
            && model.TryThreshold(out double fraction) && fraction == 0.001);
        model.ThresholdPercent = 0.2m;
        Check("percentage input returns a fraction", model.TryThreshold(out fraction) && fraction == 0.002);
        model.ThresholdPercent = null;
        Check("an empty threshold is refused", !model.TryThreshold(out _));
        model.ThresholdPercent = 101m;
        Check("a threshold outside zero to one is refused", !model.TryThreshold(out _));
        const double precise = 0.0012345678901234567;
        var preciseModel = new ReportExportViewModel(document, null, precise);
        Check("an untouched threshold keeps its original stored double", preciseModel.TryThreshold(out fraction) && fraction == precise);
        var file = new FamilyClusterFile();
        string original = file.ToJsonText();
        var curation = new CurationViewModel(file);
        int changes = 0;
        curation.DocumentChanged += (_, _) => changes++;
        Check("all-selected defaults leave an old project unchanged", !curation.ApplyReportSelection(new()) && file.ToJsonText() == original && changes == 0);
        Check("checkbox changes notify the project once", curation.ApplyReportSelection(reopened.Selection()) && changes == 1);
        Check("unchanged checkbox choices do not mark the project again", !curation.ApplyReportSelection(reopened.Selection()) && changes == 1);
        Check("a threshold change notifies the project once", curation.ApplyReportOptions(reopened.Selection(), 0.002)
            && changes == 2 && file.ReportThreshold == 0.002);
        Check("saving unchanged combined options adds no project change", !curation.ApplyReportOptions(reopened.Selection(), 0.002) && changes == 2);
        string folder = Path.Combine(Path.GetTempPath(), "FamilyQuant.ReportSelection.Tests");
        Directory.CreateDirectory(folder);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "pt-BR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                string path = Path.Combine(folder, culture + ".fqproj");
                ProjectStore.WriteFqproj(path, file, Array.Empty<QuantRow>(), new QuantManifest());
                var loaded = ProjectStore.ReadFqproj(path);
                var restored = new ReportExportViewModel(document, loaded.fcf.ReportSelection);
                Check("stored checkbox choices survive " + culture + " reopen", !restored.Choices.Single(row => row.Id == "figure-one").IsIncluded
                    && restored.Choices.Where(row => row.Id != "figure-one").All(row => row.IsIncluded)
                    && restored.Selection().ExcludedIds!.Contains("future-figure") && loaded.fcf.ReportThreshold == 0.002);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Check("restoring all default options returns the exact old curation", curation.ApplyReportOptions(new(), FamilyReportThreshold.DefaultThreshold)
            && file.ToJsonText() == original);
        Check("resetting recognized choices retains unknown exclusions", reopened.Selection().ExcludedIds!.Contains("future-figure"));
        ReportSelectionNativeTests(document);
        ReportSelectionWriterTests(document);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static IEnumerable<CheckBox> ReportCheckboxes(DependencyObject root)
    {
        if (root is CheckBox checkbox) yield return checkbox;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in ReportCheckboxes(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void ReportSelectionNativeTests(ReportDocument document)
    {
        EnsureApplication();
        ReportSelection? selected = null;
        double threshold = -1;
        var model = new ReportExportViewModel(document, null);
        var window = new ReportExportWindow(model, (value, share) => { selected = value; threshold = share; })
            { Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show(); PumpDispatcher();
            window.ThresholdEditor.ApplyTemplate();
            var thresholdText = (TextBox)window.ThresholdEditor.Template.FindName("PART_Text", window.ThresholdEditor);
            Check("native threshold shows the default percentage without padded zeros", thresholdText.Text == 0.1m.ToString(CultureInfo.CurrentCulture));
            ReportCheckboxes(window).Single(box => Equals(box.Tag, "section-two")).IsChecked = false;
            window.ThresholdEditor.Value = 0.2m;
            window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("native checkbox and threshold controls apply their exact draft", window.Accepted && threshold == 0.002
                && selected?.ExcludedIds is ["section-two"]);
        }
        finally { window.Close(); }
        int applies = 0;
        model = new ReportExportViewModel(document, null) { ThresholdPercent = null };
        window = new ReportExportWindow(model, (_, _) => applies++);
        try
        {
            window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("invalid native threshold leaves the draft unapplied", !window.Accepted && applies == 0);
        }
        finally { window.Close(); }
    }

    private static void ReportSelectionWriterTests(ReportDocument document)
    {
        var writer = typeof(ReportDocument).Assembly.GetType("FamilyQuant.Report.WordReportWriter");
        var build = writer?.GetMethod("BuildBytes");
        if (build == null) { Console.WriteLine("  SKIP  exact selected Word document proof awaits the report writer"); skipped++; return; }
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a8k0AAAAASUVORK5CYII=");
        document = document with { Sections = document.Sections.Select(section => section with
        {
            Blocks = section.Blocks.Select(block => block is ReportFigure figure
                ? (ReportBlock)(figure with { PngBytes = png, PixelWidth = 1, PixelHeight = 1 }) : block).ToArray()
        }).ToArray() };
        byte[] Bytes(ReportDocument value, ReportSelection? choice = null) =>
            (byte[])build.Invoke(null, new object?[] { value, choice })!;
        var sectionChoice = new ReportSelection { ExcludedIds = new[] { "section-two" } };
        var expected = document with { Sections = document.Sections.Where(section => section.Id != "section-two").ToArray() };
        Check("unticking a section removes exactly that section from Word", Bytes(document, sectionChoice).SequenceEqual(Bytes(expected)));
        var figureChoice = new ReportSelection { ExcludedIds = new[] { "figure-one" } };
        expected = document with { Sections = document.Sections.Select(section => section with
        {
            Blocks = section.Blocks.Where(block => block.Id != "figure-one").ToArray()
        }).ToArray() };
        byte[] selected = Bytes(document, figureChoice);
        Check("unticking one figure removes exactly its Word block", selected.SequenceEqual(Bytes(expected)));
        using var archive = new ZipArchive(new MemoryStream(selected));
        var media = archive.Entries.Where(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal)).ToArray();
        using var image = new MemoryStream();
        if (media.Length == 1) { using var input = media[0].Open(); input.CopyTo(image); }
        Check("the remaining selected figure preserves its native PNG bytes", media.Length == 1 && image.ToArray().SequenceEqual(png));
    }

    private static int ReportExportScreens(string output)
    {
        EnsureApplication();
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a fresh report-export capture directory.");
        Directory.CreateDirectory(output);
        var document = new ReportDocument { Sections = Enumerable.Range(1, 8).Select(index =>
            new ReportSection("section-" + index, "Report section " + index, new ReportBlock[]
            { new ReportFigure("figure-" + index, "Figure " + index, Array.Empty<byte>(), "Caption", 1, 1) })).ToArray() };
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        foreach (var size in new[] { (720, 700), (480, 400) })
        {
            Themes.ThemeManager.Apply(theme);
            var window = new ReportExportWindow(new(document, null), (_, _) => { })
                { Width = size.Item1, Height = size.Item2, Left = -30000, Top = -30000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            try
            {
                window.Show(); PumpDispatcher(); window.UpdateLayout();
                ScreenHarness.Save(window, Path.Combine(output, theme + "-" + size.Item1 + "x" + size.Item2 + ".png"), size.Item1, size.Item2);
            }
            finally { window.Close(); }
        }
        Console.WriteLine("Export report: 4 native captures, both themes and default/minimum sizes: " + output);
        return 0;
    }
}
