using FamilyQuant.DeNovo;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ChartThemeTests(string workDir)
    {
        Section("Chart themes - live token refresh and subscription lifetime");
        EnsureApplication();
        Directory.CreateDirectory(workDir);
        var originalTheme = Themes.ThemeManager.Current;
        var data = AnalyticsBuilder.Build(SyntheticAnalyticsRows(),
            new System.Collections.Generic.Dictionary<string, double> { ["ui_01"] = 5e8, ["ui_02"] = 4e8 });
        var charts = new ChartDrawingBase[]
        {
            new IdentityScanChart(new() { new() { Identity = 90, Groups = 500, Score = 0 },
                new() { Identity = 70, Groups = 320, Score = .003 }, new() { Identity = 50, Groups = 170, Score = .061 } }),
            new RecoveryChart(data.Fractions, data.Grouped), new XicMassChart(data.Peptides),
            new IsotopeCountChart(data.Peptides), new PercentIonCurrentChart(data.Peptides, 1e9),
            new FamilyContributionChart(FamilyFixture(3, unattributedIdentified: 4000)),
            new ThreeWayChart(new[] { new ThreeWayPartitionRow { RunName = "Synthetic overflow diagnostic",
                Tic = PartitionValue.Measured(100, "fixture"), PatternLab = PartitionValue.Recorded(80, "fixture"),
                DeNovo = PartitionValue.Measured(40, "fixture"), Remainder = PartitionValue.Derived(-20, "fixture") } })
        };
        var panel = new StackPanel();
        foreach (var chart in charts) { chart.Height = 240; panel.Children.Add(chart); }
        var window = new Window { Content = new ScrollViewer { Content = panel }, Width = 760, Height = 600,
            Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
            WindowStyle = WindowStyle.None };
        Brush Token(string name) => (Brush)Application.Current.FindResource(name);
        bool Same(Brush a, Brush b) => ChartPalette.ColorOf(a) == ChartPalette.ColorOf(b);
        int Subscriptions() => ((MulticastDelegate?)typeof(Themes.ThemeManager)
            .GetField("ThemeChanged", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null))?
            .GetInvocationList().Count(d => charts.Contains(d.Target)) ?? 0;
        try
        {
            Themes.ThemeManager.Apply(Themes.AppTheme.Dark);
            window.Show(); PumpDispatcher();
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light, Themes.AppTheme.Dark })
            {
                Themes.ThemeManager.Apply(theme); PumpDispatcher();
                var ui = ChartPalette.UiPalette;
                Check(theme + " every visible chart uses the current surface, ink and data tokens",
                    charts.All(c => c.IsLoaded && Same(c.Palette.Backdrop, Token("CanvasBrush"))
                        && Same(c.Palette.Ink, Token("TextBrush"))
                        && Same(c.Palette.Identified, Token("ChartIdentifiedBrush"))));
                Check(theme + " chart text, fills and inside labels meet pinned contrast floors",
                    ChartPalette.ContrastRatio(ui.Ink, ui.Backdrop) >= 4.5
                    && ChartPalette.ContrastRatio(ui.Muted, ui.Backdrop) >= 4.5
                    && new[] { ui.Identified, ui.DeNovo, ui.Quantified, ui.NotQuantified,
                        ui.PeptideLikeUnidentified, ui.Remainder, ui.DetectedMultiplyCharged, ui.DetectedSinglyCharged }
                        .All(fill => ChartPalette.ContrastRatio(fill, ui.Backdrop) >= 3
                            && ChartPalette.ContrastRatio(fill, ui.SegmentLabel) >= 4.5));
                Check(theme + " mass bands use data tokens without an error status colour",
                    Same(ui.MassBands[2], Token("ChartPeptideLikeBrush")));
                foreach (var chart in charts)
                {
                    var bitmap = new RenderTargetBitmap((int)chart.ActualWidth, 240, 96, 96, PixelFormats.Pbgra32);
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                        dc.DrawRectangle(new VisualBrush(chart) { Stretch = Stretch.Fill }, null,
                            new Rect(0, 0, chart.ActualWidth, 240));
                    bitmap.Render(visual);
                    byte[] pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
                    var backdrop = ChartPalette.ColorOf(Token("CanvasBrush"));
                    Check(theme + " " + chart.GetType().Name + " paints the token backdrop",
                        pixel[0] == backdrop.B && pixel[1] == backdrop.G && pixel[2] == backdrop.R && pixel[3] == 255,
                        BitConverter.ToString(pixel));
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(workDir, "hosted-" + chart.GetType().Name + "-" + theme + ".png"));
                    png.Save(output);
                }
            }
            Check("each loaded chart has exactly one theme subscription", Subscriptions() == charts.Length);
            panel.Children.Clear(); PumpDispatcher();
            Check("removing charts detaches all their theme subscriptions", Subscriptions() == 0);
            var detached = charts.Select(c => c.Palette).ToArray();
            Themes.ThemeManager.Apply(Themes.AppTheme.Light); PumpDispatcher();
            Check("unloaded charts do not receive theme changes", charts.Select(c => c.Palette).SequenceEqual(detached));
            foreach (var chart in charts) panel.Children.Add(chart);
            PumpDispatcher();
            Check("reloading charts refreshes their tokens and subscribes once",
                charts.All(c => c.IsLoaded && Same(c.Palette.Backdrop, Token("CanvasBrush"))));
            Check("reloaded charts have one theme subscription each", Subscriptions() == charts.Length);
        }
        finally { window.Close(); PumpDispatcher(); Themes.ThemeManager.Apply(originalTheme); }
        Check("closing a chart host leaves no chart theme subscriptions", Subscriptions() == 0);

        // The real Results path must apply tokens even before loading or after replacing its chart set.
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        {
            Themes.ThemeManager.Apply(theme);
            var control = new AnalyticsControl(SyntheticAnalyticsRows(), new QuantManifest());
            var actual = WalkTree(control).OfType<ChartDrawingBase>().ToArray();
            Check(theme + " Results creates every chart with UI tokens", actual.Length == 5
                && actual.All(c => Same(c.Palette.Backdrop, Token("CanvasBrush"))));
            var host = new Window { Content = control, Width = 1024, Height = 768,
                Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                host.Show(); PumpDispatcher();
                Themes.ThemeManager.Apply(theme == Themes.AppTheme.Dark ? Themes.AppTheme.Light : Themes.AppTheme.Dark);
                PumpDispatcher();
                Check(theme + " open Results refreshes the same chart instances on theme change",
                    WalkTree(control).OfType<ChartDrawingBase>().SequenceEqual(actual)
                    && actual.All(c => Same(c.Palette.Backdrop, Token("CanvasBrush"))));
                control.ViewModel.FamilyTopN = 5; PumpDispatcher();
                var replaced = WalkTree(control).OfType<ChartDrawingBase>().ToArray();
                Check(theme + " Results options replace charts with current tokens and unload the old set",
                    replaced.Length == 5 && !replaced.Intersect(actual).Any()
                    && actual.All(c => !c.IsLoaded)
                    && replaced.All(c => c.IsLoaded && Same(c.Palette.Backdrop, Token("CanvasBrush"))));
            }
            finally { host.Close(); PumpDispatcher(); }
        }
        Themes.ThemeManager.Apply(originalTheme);
    }
}
