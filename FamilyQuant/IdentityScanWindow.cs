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
        /// <summary>The name the sweep figure is saved under, beside the analytics figures.</summary>
        public const string FigureFile = "identity_sweep.png";

        public static Window Create(string title, List<IdentityScanRow> rows, string report,
                                    int? currentIdentity = null)
        {
            var model = new IdentityScanViewModel(title, rows, report, currentIdentity);
            var window = new Window { Title = model.Title, DataContext = model, Width = 980, Height = 760,
                MinWidth = 520, MinHeight = 380 };
            SecondaryWindowView.Theme(window);
            var grid = new Grid();
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new() { Height = new GridLength(2.1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(SecondaryWindowView.Text("Compare family counts and target-decoy mixing across the tested identities.", true));
            var chart = new IdentityScanChart(model.Rows, model.CurrentIdentity, ChartPalette.UiPalette);
            EventHandler updatePalette = (_, _) => chart.Palette = ChartPalette.UiPalette;
            Themes.ThemeManager.ThemeChanged += updatePalette;
            window.Closed += (_, _) => Themes.ThemeManager.ThemeChanged -= updatePalette;
            Grid.SetRow(chart, 1); grid.Children.Add(chart);
            var status = SecondaryWindowView.Text("", true);
            status.Margin = new Thickness(12, 0, 0, 0); status.VerticalAlignment = VerticalAlignment.Center;
            var save = new Button { Content = "Save figure (PNG)", HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Saves this sweep chart as a PNG, 1920 by 1080 pixels on a light background, ready for a slide. Only the PNG you name is written; the project is not changed." };
            save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            save.Click += (_, _) => SaveFigureFromDialog(save, status, model.Rows, model.CurrentIdentity);
            var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
            DockPanel.SetDock(save, Dock.Left); bar.Children.Add(save);
            var help = SecondaryWindowView.Help("Identity scan help", model.Explanation, window);
            DockPanel.SetDock(help, Dock.Right); bar.Children.Add(help); bar.Children.Add(status);
            Grid.SetRow(bar, 2); grid.Children.Add(bar);
            var table = SecondaryWindowView.Report(model.Report, false,
                "The same sweep as numbers. Read-only; select any part and copy it with Ctrl+C.");
            Grid.SetRow(table, 3); grid.Children.Add(table); window.Content = SecondaryWindowView.Frame(grid);
            return window;
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
        private static async void SaveFigureFromDialog(Button button, TextBlock status,
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
            using var operation = AppOperationHost.For(button).Begin("Export identity figure");

            try
            {
                await AppOperationFiles.RunSta(() => AppOperationFiles.WriteFile(sfd.FileName,
                    path => SaveFigure(rows, path, currentIdentity), operation.Context));
                status.Text = "Written to " + Path.GetFileName(sfd.FileName) + ", " +
                    ((int)(ChartImageExporter.DefaultLayoutWidth * ChartImageExporter.DefaultScale))
                        .ToString("N0", CultureInfo.CurrentCulture) + " by " +
                    ((int)(ChartImageExporter.DefaultLayoutHeight * ChartImageExporter.DefaultScale))
                        .ToString("N0", CultureInfo.CurrentCulture) +
                    " device pixels. Nothing on screen changed.";
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                status.Text = "Cancelled. The previous figure was kept.";
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
