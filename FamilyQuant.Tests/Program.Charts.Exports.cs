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

        /// <summary>
        /// The shared PNG exporter: that it produces the pixel size that was ASKED for (the default being
        /// a projector-sized 1920x1080 over a 960x540 layout), that it works with no window anywhere, and
        /// that the whole analytics figure set comes out under its documented names.
        /// </summary>
        private static void ChartImageExportTests(string workDir)
        {
            string folder = Path.Combine(workDir, "figures");
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

            // ---- the requested pixel size ------------------------------------------------------------
            Check("the exporter's default layout is the 960x540 slide it documents",
                ChartImageExporter.DefaultLayoutWidth == 960 && ChartImageExporter.DefaultLayoutHeight == 540
                && ChartImageExporter.DefaultScale == 2.0);

            var sweep = new List<IdentityScanRow>
            {
                new IdentityScanRow { Identity = 90, Groups = 500, TargetGroups = 260, MixedGroups = 0, Score = 0.000 },
                new IdentityScanRow { Identity = 70, Groups = 320, TargetGroups = 168, MixedGroups = 1, Score = 0.003 },
                new IdentityScanRow { Identity = 50, Groups = 170, TargetGroups = 92,  MixedGroups = 8, Score = 0.061 },
                new IdentityScanRow { Identity = 30, Groups = 60,  TargetGroups = 35,  MixedGroups = 21, Score = 0.402 },
            };

            string defaultPng = Path.Combine(folder, "exporter_default.png");
            ChartImageExporter.Save(new IdentityScanChart(sweep, currentIdentity: 70), defaultPng);

            var (w, h) = PngPixelSize(defaultPng);
            Check("the default export is 1920 by 1080 device pixels, crisp on a projector",
                w == 1920 && h == 1080, w + "x" + h);

            string customPng = Path.Combine(folder, "exporter_custom.png");
            ChartImageExporter.Save(new IdentityScanChart(sweep, currentIdentity: 70), customPng,
                                    layoutWidth: 640, layoutHeight: 360, scale: 1.5);
            var (cw, ch) = PngPixelSize(customPng);
            Check("and a caller's own layout and scale are honoured exactly",
                cw == 960 && ch == 540, cw + "x" + ch);

            Check("a scale of zero is a caller error and is refused, not silently clamped",
                Throws<ArgumentOutOfRangeException>(() =>
                    ChartImageExporter.Save(new IdentityScanChart(sweep), Path.Combine(folder, "never.png"),
                                            scale: 0)));

            // ---- the sweep figure through the window's own entry point -------------------------------
            string sweepPng = Path.Combine(folder, IdentityScanWindow.FigureFile);
            IdentityScanWindow.SaveFigure(sweep, sweepPng, currentIdentity: 70);
            var (sw, sh) = PngPixelSize(sweepPng);
            Check("the identity sweep saves as a figure of the same size, with no window opened",
                File.Exists(sweepPng) && sw == 1920 && sh == 1080, sw + "x" + sh);

            // The window offers it too, and the button explains itself like every other command.
            Window scanWindow = IdentityScanWindow.Create("Identity scan · test", sweep, "report text", 70);
            Button? saveButton = (scanWindow.Content as FrameworkElement) == null ? null
                : WalkTree((DependencyObject)scanWindow.Content).OfType<Button>()
                    .FirstOrDefault(b => b.Content is string s && s.Contains("PNG"));

            Check("the sweep window offers the figure export, with a tooltip that says what it writes",
                saveButton != null && saveButton.ToolTip is string tip && tip.Length > 100
                && tip.Contains("1920"), saveButton?.ToolTip as string);

            // Pressed off a display it must REFUSE in words, never open a dialog nothing can close.
            saveButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var scanStatus = WalkTree((DependencyObject)scanWindow.Content).OfType<TextBlock>()
                .FirstOrDefault(t => t.Text.Contains("not on a display"));
            Check("and pressed with no display it says so instead of opening a dialog",
                scanStatus != null, scanStatus?.Text);

            // ---- the whole analytics figure set ------------------------------------------------------
            var rows = SyntheticAnalyticsRows();
            var tic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                { ["ui_01"] = 5.0e8, ["ui_02"] = 4.0e8 };

            List<string> written = AnalyticsFigures.SaveFromProject(
                rows, new QuantManifest { PerRunTic = tic }, curation: null, folder: folder);

            Check("every analytics figure is written, under the documented names",
                written.Select(Path.GetFileName).SequenceEqual(AnalyticsFigures.FileNames),
                string.Join(", ", written.Select(Path.GetFileName)));

            Check("and each of them is a real PNG of the projector size, not a stub",
                written.All(p => new FileInfo(p).Length > 3000)
                && written.All(p => PngPixelSize(p) == (1920, 1080)),
                string.Join(", ", written.Select(p => new FileInfo(p).Length.ToString("N0", CultureInfo.InvariantCulture))));

            foreach (string p in written) Console.WriteLine("        figure written to " + p);


        }

    }
}
