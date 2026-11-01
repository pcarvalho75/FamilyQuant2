using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// The identity sweep, as a curve above the table it used to be reported by alone.
    ///
    /// The table answers "what did 70% give me"; the curve answers "where does the family count stop
    /// falling and the mixing start", which is the question the threshold is actually chosen on and which
    /// no column of numbers makes visible.
    ///
    /// Built in code rather than XAML for the same reason as <see cref="TextReportWindow"/>: it cannot
    /// fail to parse in a harness that never opens a window.
    /// </summary>
    public static class IdentityScanWindow
    {
        static readonly Color Backdrop = Color.FromRgb(0x33, 0x39, 0x49);

        /// <summary>The name the sweep figure is saved under, beside the analytics figures.</summary>
        public const string FigureFile = "identity_sweep.png";

        public static Window Create(string title, List<IdentityScanRow> rows, string report,
                                    int? currentIdentity = null)
        {
            // Written from what the sweep actually contains: on a decoy-free database the chart draws
            // no floor line and no mixing curve, and a caption explaining both would describe a picture
            // that is not on screen.
            bool hasDecoys = rows != null && rows.Count > 0
                             && !rows.All(r => r.Groups == r.TargetGroups && r.MixedGroups == 0);
            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2.1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

            var chart = new IdentityScanChart(rows ?? new List<IdentityScanRow>(), currentIdentity);
            Grid.SetRow(chart, 0);
            grid.Children.Add(chart);

            var caption = new TextBlock
            {
                Text = hasDecoys
                    ? "Each point is one USEARCH run. The floor line marks the loosest identity at which "
                      + "no family mixed a target with a decoy: below it, alignments are no better than "
                      + "noise. It is a floor and not an optimum, because two unrelated target families "
                      + "fusing is invisible to this measurement."
                    : "Each point is one USEARCH run. This database carries no decoy sequences, so there "
                      + "is no mixing to measure and no floor to mark: choose the threshold from the "
                      + "family counts and from the biology, or re-run on a target-decoy database.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA4, 0x9A)),
                FontSize = 12,
                Margin = new Thickness(0, 12, 0, 10)
            };
            Grid.SetRow(caption, 1);
            grid.Children.Add(caption);

            // The families-vs-identity curve is one of the figures a talk needs, so the window offers it
            // as a PNG rather than leaving it to a screenshot of a window.
            var status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA4, 0x9A)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };

            var save = new Button
            {
                Content = "Save figure (PNG)",
                Padding = new Thickness(12, 5, 12, 5),
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x41, 0x52)),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Saves this sweep chart as a PNG, 1920 by 1080 pixels on a light background, " +
                          "ready for a slide. Only the PNG you name is written; the project is not changed.",
            };

            save.Click += (_, __) => SaveFigureFromDialog(save, status, rows ?? new List<IdentityScanRow>(),
                                                          currentIdentity);

            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10),
            };
            bar.Children.Add(save);
            bar.Children.Add(status);
            Grid.SetRow(bar, 2);
            grid.Children.Add(bar);

            var table = new TextBox
            {
                Text = report,
                AcceptsReturn = true,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12.5,
                Padding = new Thickness(10),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3C)),
                Foreground = Brushes.White,
                SelectionBrush = new SolidColorBrush(Color.FromRgb(0x61, 0xAE, 0xEE)),
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                ToolTip = "The same sweep as numbers. Read-only; select any part and copy it with Ctrl+C."
            };
            Grid.SetRow(table, 3);
            grid.Children.Add(table);

            return new Window
            {
                Title = title,
                Width = 980,
                Height = 760,
                MinWidth = 520,
                MinHeight = 380,
                Background = new SolidColorBrush(Backdrop),
                Content = grid
            };
        }

        public static void Show(string title, List<IdentityScanRow> rows, string report,
                                Window? owner = null, int? currentIdentity = null)
        {
            Window window = Create(title, rows, report, currentIdentity);

            if (owner != null && owner.IsLoaded) window.Owner = owner;

            window.Show();
        }

        /// <summary>
        /// The programmatic entry point: the sweep chart as a presentation-quality PNG, with no window.
        /// Uses the same <see cref="ChartImageExporter"/> as the analytics figures, so every figure of a
        /// talk comes out at the same resolution. Returns the path written.
        /// </summary>
        public static string SaveFigure(List<IdentityScanRow>? rows, string path, int? currentIdentity = null)
        {
            // A fresh chart, never the one in the window: measuring and arranging a parented element
            // fights the layout it is parented into.
            return ChartImageExporter.Save(new IdentityScanChart(rows ?? new List<IdentityScanRow>(),
                                                                 currentIdentity), path);
        }

        /// <summary>
        /// Asks for a file name and writes the figure. A file dialog off a display is a window nothing can
        /// close, so a button pressed by a headless harness reports the refusal in the status line instead,
        /// exactly as the analytics exports do.
        /// </summary>
        private static void SaveFigureFromDialog(Button button, TextBlock status,
                                                 List<IdentityScanRow> rows, int? currentIdentity)
        {
            if (PresentationSource.FromVisual(button) == null)
            {
                status.Text = "Nothing was saved, because this window is not on a display and a file " +
                    "dialog there is a window nothing can close. Next step: open the sweep in the " +
                    "application and save the figure from there.";
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "PNG images (*.png)|*.png|All files (*.*)|*.*",
                FileName = FigureFile,
                Title = "Save the identity sweep figure",
                AddExtension = true,
                DefaultExt = ".png",
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                SaveFigure(rows, sfd.FileName, currentIdentity);
                status.Text = "Written to " + Path.GetFileName(sfd.FileName) + ", " +
                    ((int)(ChartImageExporter.DefaultLayoutWidth * ChartImageExporter.DefaultScale))
                        .ToString("N0", CultureInfo.CurrentCulture) + " by " +
                    ((int)(ChartImageExporter.DefaultLayoutHeight * ChartImageExporter.DefaultScale))
                        .ToString("N0", CultureInfo.CurrentCulture) +
                    " device pixels. Nothing on screen changed.";
            }
            catch (Exception ex)
            {
                status.Text = "The figure could not be written, so nothing was saved. Reason given by the " +
                    "program: " + ex.Message + " Next step: check that you can write to that folder and " +
                    "that no image of the same name is open elsewhere, then save again.";
            }
        }
    }
}
