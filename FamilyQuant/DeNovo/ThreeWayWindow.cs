using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// The three-way partition on screen: the chart, a caption stating what the bar is, and one
    /// button that writes the same numbers to CSV.
    ///
    /// Built in code rather than XAML for the same reason as <see cref="FamilyQuant.TextReportWindow"/>:
    /// it cannot fail to parse in a harness that never opens a window, and the suite constructs it
    /// headlessly.
    ///
    /// No menu wiring lives here. The tab that offers this window is phase F7; this is the window it
    /// will offer.
    /// </summary>
    public static class ThreeWayWindow
    {
        static readonly Color Backdrop = Color.FromRgb(0x33, 0x39, 0x49);

        public static Window Create(string title, IReadOnlyList<ThreeWayPartitionRow> rows,
                                    string? provenanceNote = null)
        {
            var perRun = (rows ?? new List<ThreeWayPartitionRow>()).Where(r => !r.IsGrouped).ToList();

            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var chart = new ThreeWayChart(perRun)
            {
                ToolTip = "Each bar is one run's total ion current, so the three segments add up to " +
                          "100%. The PatternLab and de novo areas both use the M+0 peak, so they " +
                          "compare fairly."
            };
            Grid.SetRow(chart, 0);
            grid.Children.Add(chart);

            // Written from what the partition actually contains: a caption promising a negative-
            // remainder marker on a set that has none would describe a picture that is not on screen.
            bool anyNegative = perRun.Any(r => r.NegativeRemainder);

            var caption = new TextBlock
            {
                Text = Caption(anyNegative) + (string.IsNullOrWhiteSpace(provenanceNote)
                                                   ? string.Empty
                                                   : " " + provenanceNote),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA4, 0x9A)),
                FontSize = 12,
                Margin = new Thickness(0, 12, 0, 10),
                ToolTip = "How to read the chart above, and where each number came from."
            };
            Grid.SetRow(caption, 1);
            grid.Children.Add(caption);

            var save = new Button
            {
                Content = "Save CSV...",
                Padding = new Thickness(14, 5, 14, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = perRun.Count > 0,
                ToolTip = "Saves every number behind this chart as a CSV, with where each one came " +
                          "from. Numbers use a dot as the decimal separator."
            };
            save.Click += (_, __) => SaveCsv(perRun);
            Grid.SetRow(save, 2);
            grid.Children.Add(save);

            return new Window
            {
                Title = title,
                Width = 1020,
                Height = 620,
                MinWidth = 560,
                MinHeight = 340,
                Background = new SolidColorBrush(Backdrop),
                Content = grid
            };
        }

        public static void Show(string title, IReadOnlyList<ThreeWayPartitionRow> rows,
                                Window? owner = null, string? provenanceNote = null)
        {
            Window window = Create(title, rows, provenanceNote);

            if (owner != null && owner.IsLoaded) window.Owner = owner;

            window.Show();
        }

        private static string Caption(bool anyNegative)
        {
            string common =
                "Each bar is one run's whole ion current. The first segment is what PatternLab " +
                "identified and quantified, the second is what the de novo search adds on top, and " +
                "the third is signal that no identification claims. Both numerators are monoisotopic " +
                "areas from the same extraction machinery, and both count each integrated peak once " +
                "rather than once per MS2 scan that sampled it, so the two are on one base and one " +
                "grain and the three shares add up to 100% by construction. The de novo envelope sum " +
                "is larger and is deliberately absent here: it has no counterpart on the PatternLab " +
                "side, and it is in the CSV instead.";

            return anyNegative
                ? common + " One or more runs are marked in red and hatched: there the two numerators " +
                           "together claim MORE than the run's ion current, which means signal is being " +
                           "counted twice or the totals do not belong to the same acquisition. The value " +
                           "is reported with its sign and is never clamped to zero."
                : common;
        }

        private static void SaveCsv(IReadOnlyList<ThreeWayPartitionRow> rows)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save the three-way partition",
                Filter = "Comma-separated values (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "three_way_partition.csv",
                AddExtension = true,
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                ThreeWayPartition.WriteCsv(dialog.FileName, rows);
            }
            catch (Exception ex)
            {
                MessageBox.Show("The file could not be written: " + ex.Message,
                    "Save the three-way partition", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
