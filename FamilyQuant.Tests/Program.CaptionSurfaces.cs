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
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CaptionSurfaceCommands() => new[]
    {
        new TestCommand("--test-caption-surfaces", 10000, TestCommandPhase.Normalized,
            args => args.Contains("--test-caption-surfaces", StringComparer.Ordinal), (_, work) =>
            {
                CaptionSurfaceTests(Path.Combine(work, "caption-surfaces"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void CaptionSurfaceTests(string folder)
    {
        Section("Generated captions reach native Results and the real Word report export");
        EnsureApplication(); Directory.CreateDirectory(folder);
        var manifest = new QuantManifest();
        var rows = SyntheticAnalyticsRows();
        var control = new AnalyticsControl(rows, manifest);
        var captions = WalkTree(control).OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("figure-caption:", StringComparison.Ordinal)).ToArray();
        Check("every Results chart has its generated caption on the actual screen tree", captions.Length == AnalyticsFigures.FileNames.Count);
        Check("screen captions occupy one line and expose their complete text in a tooltip", captions.Length > 0
            && captions.All(c => c.TextWrapping == TextWrapping.NoWrap && c.TextTrimming == TextTrimming.CharacterEllipsis
                && c.ToolTip is string full && full == c.Text && full.Length > 60));
        Check("Results XIC caption contains facts from its displayed data", captions.Any(c => Equals(c.Tag, "figure-caption:" + AnalyticsFigures.XicByMassFile)
            && c.Text.Contains("Identified points") && c.Text.Contains("De novo points")));
        var originalTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var window = new Window { Content = control, Width = 1100, Height = 820, Left = -30000, Top = -30000,
                    ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    var bitmap = new RenderTargetBitmap((int)control.ActualWidth, (int)control.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(control); WriteChartPng(bitmap, Path.Combine(folder, "Results-" + theme + ".png"));
                    Check(theme + " generated caption ink uses the current theme token", captions.Length > 0
                        && captions.All(c => ChartPalette.ColorOf(c.Foreground) == ChartPalette.ColorOf((Brush)Application.Current.FindResource("MutedBrush"))));
                }
                finally { window.Content = null; window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(originalTheme); }
        var curation = new FamilyClusterFile();
        var families = FamilyFixture(3, unattributedIdentified: 4000);
        var exported = ReportProjectExporter.Build(rows, manifest, curation, Path.Combine(folder, "report-png"), familyResults: families);
        var xic = exported.Figures.Single(f => f.Figure.Id == "figure:" + AnalyticsFigures.XicByMassFile).Figure;
        Check("actual report figure has generated numeric facts rather than only a fixed sentence",
            xic.Caption.Contains("Identified points") && xic.Caption.Contains("De novo points"));
        var tables = exported.Document.Sections.SelectMany(s => s.Blocks).OfType<ReportTable>().ToArray();
        Check("actual report tables receive generated cell-based explanations", tables.Length > 0 && tables.All(t => t.Caption.Contains("The table shows")));
        byte[] bytes = WordReportWriter.BuildBytes(exported.Document);
        File.WriteAllBytes(Path.Combine(folder, "caption-surfaces.docx"), bytes);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var xmlInput = zip.GetEntry("word/document.xml")!.Open();
        var xml = System.Xml.Linq.XDocument.Load(xmlInput);
        System.Xml.Linq.XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        string text = string.Concat(xml.Descendants(w + "t").Select(t => t.Value));
        Check("the program-written Word document contains the generated caption facts", text.Contains("Identified points") && text.Contains("The table shows"));
    }
}
