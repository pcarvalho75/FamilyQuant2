using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CompositionChartCommands() => new[]
    {
        new TestCommand("--test-composition-charts", 10000, TestCommandPhase.Normalized,
            args => args.Contains("--test-composition-charts", StringComparer.Ordinal), (_, work) =>
            {
                CompositionChartTests(Path.Combine(work, "composition-charts"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static FamilyCompositionData CompositionFixture() => new(new[]
    {
        new FamilyCompositionValue("a", "Family A", 60, 5, 3, new[]
        {
            new FamilySubclassValue("a1", "Subclass 1", 35),
            new FamilySubclassValue("", "Subclass not resolved", 25)
        }, 4, 2),
        new FamilyCompositionValue("b", "Family B", 30, 4, 3, new[]
        {
            new FamilySubclassValue("b1", "Subclass 2", 30)
        }, 8, 5),
        new FamilyCompositionValue("c", "Family C", 10, null, 1,
            Array.Empty<FamilySubclassValue>(), null, null)
    }, "Mean of per-run shares of signal credited to families");

    private static void CompositionChartTests(string folder)
    {
        Section("Composition charts - supplied means, SD and subclass closure");
        EnsureApplication();
        Directory.CreateDirectory(folder);
        var data = CompositionFixture();
        Check("composition retains the input mean and SD without renormalizing",
            data.Families[0].MeanPercent == 60 && data.Families[0].SdPercent == 5);
        Check("one run retains unavailable SD rather than zero SD",
            data.Families[2].SdPercent == null && data.Families[2].RunCount == 1);
        Check("subclasses must close to the family share",
            Throws<ArgumentException>(() => new FamilyCompositionData(new[]
            {
                new FamilyCompositionValue("x", "X", 50, null, 1,
                    new[] { new FamilySubclassValue("x1", "Part", 49) }, null, null)
            }, "Fixture")));
        Check("invalid means and SD are refused rather than clamped",
            Throws<ArgumentException>(() => new FamilyCompositionData(new[]
            {
                new FamilyCompositionValue("x", "X", double.NaN, null, 1,
                    Array.Empty<FamilySubclassValue>(), null, null)
            }, "Fixture")));
        var mutableParts = new List<FamilySubclassValue> { new("p", "Part", 60) };
        var mutableRows = new List<FamilyCompositionValue> { data.Families[0] with { Subclasses = mutableParts } };
        var snapshot = new FamilyCompositionData(mutableRows, "Snapshot fixture");
        mutableParts.Clear(); mutableRows.Clear();
        Check("input is snapshotted so exported figures cannot drift after construction",
            snapshot.Families.Count == 1 && snapshot.Families[0].Subclasses.Count == 1);
        var wideSd = new FamilyCompositionData(new[] { data.Families[0] with { MeanPercent = 10, SdPercent = 17,
            Subclasses = Array.Empty<FamilySubclassValue>() } }, "SD fixture");
        Check("SD extending below zero expands the axis instead of entering the label column",
            new FamilyCompositionChart(wideSd).AxisMinimum == -10);

        var figures = FamilyCompositionFigures.Build(data);
        Check("program offers both family means and stacked subclass figures",
            figures.Select(f => f.FileName).SequenceEqual(new[] { "family_composition.png", "family_subclasses.png" }));
        Check("missing subclass breakdown stays explicitly unavailable",
            new FamilySubclassChart(data).Rows[2].Subclasses.Count == 0);
        foreach (var figure in figures)
            CheckChartNative(figure.FileName, () => FamilyCompositionFigures.Build(data)
                .Single(f => f.FileName == figure.FileName).Chart, folder, figure.ScreenHeight);
        CheckChartNative("empty-composition.png", () => new FamilyCompositionChart(
            new FamilyCompositionData(Array.Empty<FamilyCompositionValue>(), "No stored shares")), folder, 540);
    }

    private static void CheckChartNative(string name, Func<FrameworkElement> create, string folder, double height)
    {
        var original = Themes.ThemeManager.Current;
        byte[]? firstExport = null;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var chart = (ChartDrawingBase)create();
                var host = new Window { Content = chart, Width = 1000, Height = height + 40,
                    Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    host.Show(); PumpDispatcher();
                    Check(name + " " + theme + " native chart follows theme tokens", chart.IsLoaded
                        && ChartPalette.ColorOf(chart.Palette.Backdrop)
                        == ChartPalette.ColorOf((Brush)Application.Current.FindResource("CanvasBrush")));
                    var bitmap = new RenderTargetBitmap((int)chart.ActualWidth, (int)chart.ActualHeight,
                        96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(chart);
                    WriteChartPng(bitmap, Path.Combine(folder, theme + "-" + name));
                }
                finally { host.Close(); PumpDispatcher(); }

                var fresh = create();
                var png = ChartImageExporter.Render(fresh, 960, height);
                byte[] bytes = WriteChartPng(png, Path.Combine(folder, "export-" + theme + "-" + name));
                Check(name + " " + theme + " report export uses pinned Light palette",
                    ReferenceEquals(((IPalettedChart)fresh).Palette, ChartPalette.Light));
                if (firstExport != null)
                    Check(name + " Light export is byte-identical from either application theme", bytes.SequenceEqual(firstExport));
                firstExport = bytes;
                Check(name + " PNG has the requested native dimensions", png.PixelWidth == 1920
                    && png.PixelHeight == (int)Math.Round(height * 2) && bytes.Length > 5000);
            }
        }
        finally { Themes.ThemeManager.Apply(original); }
        Console.WriteLine("        native PNGs: " + Path.Combine(folder, "export-Light-" + name));
    }

    private static byte[] WriteChartPng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream);
        byte[] bytes = stream.ToArray(); File.WriteAllBytes(path, bytes); return bytes;
    }
}
