using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        private static void UiChartPaletteTests(string workDir)
        {
            var exportBrushes = typeof(ChartPalette).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(Brush)).ToArray();
            ChartPalette MutatedExport(string property)
            {
                var clone = (ChartPalette)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(ChartPalette.Light, null)!;
                var target = typeof(ChartPalette).GetProperty(property)!;
                Color original = ChartPalette.ColorOf((Brush)target.GetValue(clone)!);
                target.SetValue(clone, new SolidColorBrush(Color.FromArgb(original.A, (byte)(original.R ^ 1), original.G, original.B)));
                return clone;
            }
            Check("every Light export brush and mass band retains its exact pinned colour",
                PinnedLightPaletteMatches(ChartPalette.Light));
            Check("the export guard detects a one-channel mutation of every brush",
                exportBrushes.All(p => !PinnedLightPaletteMatches(MutatedExport(p.Name))));
            var mutatedBands = (ChartPalette)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(ChartPalette.Light, null)!;
            var bands = ChartPalette.Light.MassBands.ToArray(); bands[0] = ChartPalette.Light.Mixing;
            typeof(ChartPalette).GetProperty(nameof(ChartPalette.MassBands))!.SetValue(mutatedBands, bands);
            Check("the export guard also detects a changed mass-band entry", !PinnedLightPaletteMatches(mutatedBands));
            var rows = new List<IdentityScanRow>
            {
                new() { Identity = 90, Groups = 500, TargetGroups = 260, MixedGroups = 0, Score = 0 },
                new() { Identity = 70, Groups = 320, TargetGroups = 168, MixedGroups = 1, Score = 0.003 },
                new() { Identity = 50, Groups = 170, TargetGroups = 92, MixedGroups = 8, Score = 0.061 }
            };
            byte[] Export(ChartPalette? palette = null)
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(ChartImageExporter.Render(new IdentityScanChart(rows, 70, palette), 900, 500, 1)));
                using var memory = new MemoryStream(); png.Save(memory); return memory.ToArray();
            }
            byte[] before = Export();
            int changed = 0;
            EventHandler handler = (_, _) => changed++;
            FamilyQuant.Themes.ThemeManager.ThemeChanged += handler;
            try
            {
                foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
                {
                    FamilyQuant.Themes.ThemeManager.Apply(theme);
                    var ui = ChartPalette.UiPalette;
                    Check(theme + " UI chart structure follows the current tokens",
                        ChartPalette.ColorOf(ui.Backdrop) == ChartPalette.ColorOf((Brush)Application.Current.FindResource("CanvasBrush"))
                        && ChartPalette.ColorOf(ui.Ink) == ChartPalette.ColorOf((Brush)Application.Current.FindResource("TextBrush"))
                        && ChartPalette.ColorOf(ui.Families) == ChartPalette.ColorOf((Brush)Application.Current.FindResource("ChartIdentifiedBrush")));
                    Check(theme + " UI chart text and scan curves remain readable",
                        ChartPalette.ContrastRatio(ui.Ink, ui.Backdrop) >= 4.5 && ChartPalette.ContrastRatio(ui.Muted, ui.Backdrop) >= 4.5
                        && new[] { ui.Families, ui.Mixing, ui.Floor }.All(b => ChartPalette.ContrastRatio(b, ui.Backdrop) >= 3));
                    Check(theme + " UI snapshots are frozen and exporting them still selects the explicit Light palette",
                        typeof(ChartPalette).GetProperties().Where(p => p.PropertyType == typeof(Brush)).All(p => ((Brush)p.GetValue(ui)!).IsFrozen)
                        && ui.MassBands.All(b => b.IsFrozen) && before.SequenceEqual(Export(ui)));
                    var chart = new IdentityScanChart(rows, 70, ui);
                    chart.Measure(new Size(900, 500)); chart.Arrange(new Rect(0, 0, 900, 500)); chart.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(900, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(chart);
                    var image = new PngBitmapEncoder(); image.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(workDir, "ui-palette-" + theme.ToString().ToLowerInvariant() + ".png")); image.Save(stream);
                }
                Check("theme change notifications let open chart views refresh their snapshot", changed == 2);
            }
            finally
            {
                FamilyQuant.Themes.ThemeManager.ThemeChanged -= handler;
                FamilyQuant.Themes.ThemeManager.Apply(FamilyQuant.Themes.AppTheme.Dark);
            }
        }

    }
}
