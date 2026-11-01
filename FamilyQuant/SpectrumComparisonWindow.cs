using FamilyQuant.DeNovo;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// Read-only, spectrum-level view of the table that joins PEAKS de novo, DeepNovo, PatternLab and
    /// the stored primary quantitation. The window does no scientific work: it shows the exact rows
    /// built by <see cref="SpectrumComparisonTable"/> and delegates every CSV write to that same class,
    /// so the grid and the exported file cannot drift into two definitions of a row.
    /// </summary>
    public static class SpectrumComparisonWindow
    {
        private static readonly Color Backdrop = Color.FromRgb(0x33, 0x39, 0x49);

        /// <summary>The stable suggested file name used by both the computation and Save a copy.</summary>
        public const string SuggestedFileName = "FamilyQuant_spectrum_comparison.csv";

        /// <summary>
        /// Creates the view without showing it. Kept public so the UI smoke suite can verify bindings,
        /// column labels, enabled state and tooltip coverage without opening an unclosable window.
        /// </summary>
        public static Window Create(IReadOnlyList<SpectrumComparisonRow>? rows,
                                    string? existingCsvPath = null,
                                    DeNovoAcceptedScoreScatter? persistedScoreScatter = null)
        {
            IReadOnlyList<SpectrumComparisonRow> displayRows =
                rows ?? (IReadOnlyList<SpectrumComparisonRow>)Array.Empty<SpectrumComparisonRow>();

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var explanation = new TextBlock
            {
                Text = "One row per normalized file + MS2 scan + compatible source-reported charge and " +
                       "non-chaining 20-ppm m/z cluster. Alternative sequences share ONE XIC only " +
                       "inside that compatible precursor-hypothesis row; incompatible same-scan " +
                       "metadata stays on separate flagged rows. RAW-selected z/mz remains visible as " +
                       "diagnostic evidence, not a rule that forces all claims in one scan together. " +
                       "Direct M+0 and plot-parity primary quant remain separate.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10),
                ToolTip = "How the rows are built and what a blank cell means. A blank is missing " +
                          "evidence, not a zero score and not a sequence disagreement."
            };
            Grid.SetRow(explanation, 0);
            root.Children.Add(explanation);

            var count = new TextBlock
            {
                Text = displayRows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                       " precursor-hypothesis row(s)",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 18, 0),
                ToolTip = "The number of rows shown. One scan can give several rows, one per precursor hypothesis."
            };

            var save = new Button
            {
                Name = "ButtonSaveSpectrumComparisonCsv",
                Content = "Save a copy (CSV)",
                Padding = new Thickness(12, 5, 12, 5),
                IsEnabled = displayRows.Count > 0,
                ToolTip = "Saves exactly the rows in this table as a CSV. Scores stay in separate columns, " +
                          "missing values stay blank, and the project is not changed."
            };

            // The project-native object carries the configured source cutoffs and complete filter
            // descriptions. Rebuilding from rows alone preserves the points but necessarily loses that
            // provenance, so use the persisted object whenever the loaded/computed project supplies it.
            DeNovoAcceptedScoreScatter scoreScatter = persistedScoreScatter
                ?? DeNovoAcceptedScoreScatterStudy.Build(displayRows);
            scoreScatter.Validate();
            var saveScorePlot = new Button
            {
                Name = "ButtonSaveAcceptedScoreScatter",
                Content = "Save accepted-call score plot",
                DataContext = scoreScatter,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(8, 0, 0, 0),
                IsEnabled = scoreScatter.Total > 0,
                ToolTip = "Saves a 1920 x 1080 PNG and its CSV: one dot per row where both PEAKS and " +
                          "DeepNovo passed their filters. Blue circles are sequence matches; red diamonds " +
                          "are mismatches."
            };

            var path = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(existingCsvPath)
                    ? "No automatic CSV path is available for this view."
                    : "CSV: " + existingCsvPath,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0xC0, 0xCF)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
                ToolTip = "The CSV saved automatically by the latest computation, if any. Saving a copy " +
                          "does not change the project."
            };

            var commandBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(count, Dock.Left);
            DockPanel.SetDock(save, Dock.Left);
            commandBar.Children.Add(count);
            commandBar.Children.Add(save);
            commandBar.Children.Add(saveScorePlot);
            commandBar.Children.Add(path);
            Grid.SetRow(commandBar, 1);
            root.Children.Add(commandBar);

            var grid = BuildGrid(displayRows);
            Grid.SetRow(grid, 2);
            root.Children.Add(grid);

            var status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0xC0, 0xCF)),
                Margin = new Thickness(0, 10, 0, 0),
                ToolTip = "Shows where Save a copy wrote the CSV, or why it could not."
            };
            Grid.SetRow(status, 3);
            root.Children.Add(status);

            var window = new Window
            {
                Title = "FamilyQuant · Spectrum comparison table",
                Width = 1420,
                Height = 760,
                MinWidth = 780,
                MinHeight = 420,
                Background = new SolidColorBrush(Backdrop),
                Content = root,
            };

            save.Click += (_, __) => SaveCopy(save, status, displayRows, existingCsvPath);
            saveScorePlot.Click += (_, __) => SaveAcceptedScoreScatter(
                saveScorePlot, status, scoreScatter, existingCsvPath);
            return window;
        }

        public static void Show(IReadOnlyList<SpectrumComparisonRow>? rows, Window? owner = null,
                                string? existingCsvPath = null,
                                DeNovoAcceptedScoreScatter? persistedScoreScatter = null)
        {
            Window window = Create(rows, existingCsvPath, persistedScoreScatter);
            if (owner != null && owner.IsLoaded) window.Owner = owner;
            window.Show();
        }

        private static DataGrid BuildGrid(IReadOnlyList<SpectrumComparisonRow> rows)
        {
            var grid = new DataGrid
            {
                Name = "DataGridSpectrumComparison",
                ItemsSource = rows,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = true,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                HeadersVisibility = DataGridHeadersVisibility.All,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                ToolTip = "One read-only row per file, scan and precursor hypothesis. Click a column heading to " +
                          "sort; the CSV keeps the file and scan order."
            };

            grid.Columns.Add(Column("Scan", nameof(SpectrumComparisonRow.ScanNumber), 80,
                "The MS2 scan number within the acquisition file."));
            grid.Columns.Add(Column("File name", nameof(SpectrumComparisonRow.FileName), 210,
                "The acquisition file. Rows are joined by file and scan; scans from different files " +
                "are never combined."));
            grid.Columns.Add(Column("Hypothesis #", nameof(SpectrumComparisonRow.PrecursorHypothesisIndex), 90,
                "Numbers the precursor hypotheses within one file and scan. Calls with a different " +
                "charge or m/z get separate rows; that alone does not prove separate precursors."));
            grid.Columns.Add(Column("Same scan, other hypothesis?", nameof(SpectrumComparisonRow.SameScanDifferentPrecursorHypothesis), 180,
                "True when this scan also has another row with a different charge or m/z, which is why " +
                "comparing by scan alone would be unsafe."));
            grid.Columns.Add(Column("Precursor hypothesis z", nameof(SpectrumComparisonRow.Charge), 135,
                "The charge the search tools reported for this row. A different charge in the same scan " +
                "gets its own flagged row."));
            grid.Columns.Add(Column("Precursor hypothesis m/z", nameof(SpectrumComparisonRow.PrecursorMz), 160,
                "The precursor m/z of this row, grouping reported values within 20 ppm. A different m/z " +
                "in the same scan stays on its own row and does not share this quant.",
                "0.#####"));
            grid.Columns.Add(Column("RAW charge", nameof(SpectrumComparisonRow.RawPrecursorCharge), 85,
                "The precursor charge the instrument chose, read from the RAW file. Blank when it was " +
                "not available."));
            grid.Columns.Add(Column("RAW charge known", nameof(SpectrumComparisonRow.RawChargeKnown), 115,
                "True when the precursor charge was read from the RAW file. False means a labelled " +
                "fallback was used."));
            grid.Columns.Add(Column("RAW precursor m/z", nameof(SpectrumComparisonRow.RawPrecursorMz), 120,
                "The precursor m/z the instrument chose, read from the RAW file. Blank when it was " +
                "not available.", "0.#####"));
            grid.Columns.Add(Column("RAW m/z known", nameof(SpectrumComparisonRow.RawPrecursorMzKnown), 110,
                "True when the precursor m/z was read from the RAW file. False means the reported m/z " +
                "could not be checked against the RAW."));
            grid.Columns.Add(Column("Precursor key basis", nameof(SpectrumComparisonRow.PrecursorKeyBasis), 240,
                "How this row's precursor was defined, and how the RAW values were used to check it."));
            grid.Columns.Add(Column("PEAKS de novo sequence", nameof(SpectrumComparisonRow.PeaksDeNovoCleanSequence), 210,
                "The top PEAKS sequence, without modification notation. It may have passed or failed " +
                "the filters (see the next column); blank means PEAKS made no call here."));
            grid.Columns.Add(Column("PEAKS passes configured filters?", nameof(SpectrumComparisonRow.PeaksDeNovoAccepted), 185,
                "True if the PEAKS call passed the score, length and local-confidence filters, false " +
                "if it failed, blank if PEAKS made no call here."));
            grid.Columns.Add(Column("PEAKS z", nameof(SpectrumComparisonRow.PeaksDeNovoCharge), 70,
                "The charge PEAKS reported for this call, shown apart from the RAW charge so any " +
                "disagreement is visible."));
            grid.Columns.Add(Column("PEAKS m/z", nameof(SpectrumComparisonRow.PeaksDeNovoMz), 95,
                "The precursor m/z PEAKS reported for the displayed call.", "0.#####"));
            grid.Columns.Add(Column("PEAKS matches RAW?", nameof(SpectrumComparisonRow.PeaksDeNovoMatchesRawPrecursor), 135,
                "True if the PEAKS charge and m/z agree with the RAW precursor, false if not, blank if " +
                "either is unknown."));
            grid.Columns.Add(Column("DeepNovo sequence", nameof(SpectrumComparisonRow.DeepNovoCleanSequence), 210,
                "The top DeepNovo sequence, without modification notation. It may have passed or " +
                "failed the filters (see the next column); blank means DeepNovo made no call here."));
            grid.Columns.Add(Column("DeepNovo passes configured filters?", nameof(SpectrumComparisonRow.DeepNovoAccepted), 210,
                "True if the DeepNovo call passed the score, length and local-confidence filters, " +
                "false if it failed, blank if DeepNovo made no call here."));
            grid.Columns.Add(Column("DeepNovo z", nameof(SpectrumComparisonRow.DeepNovoCharge), 85,
                "The charge DeepNovo reported for this call, kept apart from the RAW and the other " +
                "tools' values."));
            grid.Columns.Add(Column("DeepNovo m/z", nameof(SpectrumComparisonRow.DeepNovoMz), 105,
                "The precursor m/z DeepNovo reported for the displayed call.", "0.#####"));
            grid.Columns.Add(Column("DeepNovo matches RAW?", nameof(SpectrumComparisonRow.DeepNovoMatchesRawPrecursor), 155,
                "True if the DeepNovo charge and m/z agree with the RAW precursor, false if not, blank " +
                "if either is unknown."));
            grid.Columns.Add(Column("PatternLab sequence", nameof(SpectrumComparisonRow.PatternLabCleanSequence), 210,
                "The PatternLab peptide sequence, without modification notation. Blank means the " +
                "search gave no accepted PSM here."));
            grid.Columns.Add(Column("PatternLab z", nameof(SpectrumComparisonRow.PatternLabCharge), 90,
                "The precursor charge PatternLab reported for this PSM, shown apart from the RAW " +
                "charge."));
            grid.Columns.Add(Column("PatternLab m/z", nameof(SpectrumComparisonRow.PatternLabMz), 110,
                "The precursor m/z PatternLab reported for this PSM.", "0.#####"));
            grid.Columns.Add(Column("PatternLab matches RAW?", nameof(SpectrumComparisonRow.PatternLabMatchesRawPrecursor), 160,
                "True if the PatternLab charge and m/z agree with the RAW precursor, false if not, " +
                "blank if either is unknown."));
            grid.Columns.Add(Column("PEAKS ALC (%)", nameof(SpectrumComparisonRow.PeaksDeNovoScore), 105,
                "PEAKS' own ALC score. It is never treated as a DeepNovo SCORE.", "0.###"));
            grid.Columns.Add(Column("DeepNovo SCORE (%)", nameof(SpectrumComparisonRow.DeepNovoScore), 130,
                "DeepNovo's own SCORE. It is never treated as a PEAKS ALC.", "0.###"));
            grid.Columns.Add(Column("PatternLab XCorr", nameof(SpectrumComparisonRow.PatternLabXScore), 115,
                "PatternLab's search score (XCorr), kept separate from both de novo scores.", "0.###"));
            grid.Columns.Add(Column("Observed precursor XIC (M+0)", nameof(SpectrumComparisonRow.QuantM0Observed), 175,
                "The measured monoisotopic XIC of this row. Alternative sequences in this row share it; " +
                "it is never shared with another row or counted once per tool.",
                "0.######E+0"));
            grid.Columns.Add(Column("Primary assigned quant", nameof(SpectrumComparisonRow.QuantPrimaryAssigned), 155,
                "The main quantity used in the plots. It can differ from the M+0 XIC; the next column " +
                "says how it was obtained. In the CSV it is also called PrimaryQuantValue.", "0.######E+0"));
            grid.Columns.Add(Column("Primary quant basis", nameof(SpectrumComparisonRow.QuantPrimaryBasis), 185,
                "How Primary assigned quant was obtained, so a measured area is not confused with a " +
                "model estimate."));
            grid.Columns.Add(Column("Run TIC", nameof(SpectrumComparisonRow.RunTic), 120,
                "The total ion current of this run, so per-run percentages can be recomputed from the " +
                "table.", "0.######E+0"));
            grid.Columns.Add(Column("Pooled TIC", nameof(SpectrumComparisonRow.PooledTic), 120,
                "The total ion current of all loaded runs together, repeated on each row for pooled " +
                "percentages.", "0.######E+0"));
            grid.Columns.Add(Column("Credited in primary totals?", nameof(SpectrumComparisonRow.QuantCreditedInPrimaryTotals), 165,
                "True when this row's quantity is included in the plot totals. Check the next column " +
                "rather than summing every number."));
            grid.Columns.Add(Column("Primary credit reason", nameof(SpectrumComparisonRow.QuantCreditReason), 220,
                "Why this row is included in, or excluded from, the plot totals."));
            grid.Columns.Add(Column("Single family ID", nameof(SpectrumComparisonRow.FamilyUserId), 95,
                "Filled only when this peptide maps to exactly one family. Blank means no family or " +
                "several families."));
            grid.Columns.Add(Column("All family IDs", nameof(SpectrumComparisonRow.FamilyUserIds), 150,
                "Every curated family this peptide maps to, in ascending order, separated by " +
                "semicolons."));
            grid.Columns.Add(Column("All family assignments", nameof(SpectrumComparisonRow.FamilyAssignments), 260,
                "Each family ID with its description, in the same order as All family IDs. A peptide " +
                "can count in several families without getting an extra XIC."));
            grid.Columns.Add(Column("Family mapping identity", nameof(SpectrumComparisonRow.FamilyMappingMinimumIdentity), 145,
                "The minimum homology identity used to map this peptide to families. Blank means no " +
                "family mapping was done for this table.", "P0"));
            grid.Columns.Add(Column("Family mapping status", nameof(SpectrumComparisonRow.FamilyMappingStatus), 250,
                "Whether family mapping ran, and whether this peptide reached no, one or several " +
                "families. Read an empty family list together with this column."));
            grid.Columns.Add(Column("Precursor agreement", nameof(SpectrumComparisonRow.PrecursorAgreement), 150,
                "Whether the available tools point to a compatible precursor at this row's charge and " +
                "20-ppm m/z."));
            grid.Columns.Add(Column("Join status", nameof(SpectrumComparisonRow.JoinStatus), 190,
                "A short verdict on which tools made a call here and whether they agree. Calls in the " +
                "same scan with a different charge or m/z are kept on separate rows."));

            return grid;
        }

        private static void SaveAcceptedScoreScatter(Button button, TextBlock status,
            DeNovoAcceptedScoreScatter scatter, string? existingCsvPath)
        {
            if (PresentationSource.FromVisual(button) == null)
            {
                status.Text = "Nothing was saved because this comparison window is not on a display. " +
                              "Open it from the loaded project and choose Save accepted-call score plot.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save accepted PEAKS versus DeepNovo score plot and exact CSV",
                Filter = "PNG image (*.png)|*.png",
                FileName = PresentationFigures.DeNovoAcceptedScoreScatterFile,
                AddExtension = true,
                DefaultExt = ".png",
                OverwritePrompt = true,
            };
            string? folder = string.IsNullOrWhiteSpace(existingCsvPath)
                ? null : Path.GetDirectoryName(existingCsvPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                dialog.InitialDirectory = folder;
            if (dialog.ShowDialog() != true) return;

            try
            {
                scatter.Validate();
                ChartImageExporter.Save(new DeNovoAcceptedScoreScatterChart(scatter),
                    dialog.FileName);
                string csvPath = Path.ChangeExtension(dialog.FileName, ".csv");
                File.WriteAllText(csvPath,
                    DeNovoAcceptedScoreScatterStudy.ToCsv(scatter), new UTF8Encoding(false));
                status.Text = "Saved " + dialog.FileName + " and " + csvPath + ". " +
                              scatter.Total.ToString("N0", CultureInfo.CurrentCulture) +
                              " jointly accepted precursor-hypothesis row(s): " +
                              scatter.Matches.ToString("N0", CultureInfo.CurrentCulture) +
                              " strict matches and " +
                              scatter.Mismatches.ToString("N0", CultureInfo.CurrentCulture) +
                              " mismatches.";
            }
            catch (Exception ex)
            {
                status.Text = "The accepted-call score plot was not saved. " + ex.Message;
            }
        }

        private static DataGridTextColumn Column(string header, string property, double width,
                                                 string tooltip, string? format = null)
        {
            var binding = new Binding(property);
            if (format != null) binding.StringFormat = format;

            var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, tooltip));
            style.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 60000));

            return new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                Width = new DataGridLength(width),
                HeaderStyle = style,
            };
        }

        private static void SaveCopy(Button button, TextBlock status,
                                     IReadOnlyList<SpectrumComparisonRow> rows,
                                     string? existingCsvPath)
        {
            if (PresentationSource.FromVisual(button) == null)
            {
                status.Text = "Nothing was saved, because this window is not on a display and a file " +
                              "dialog there is a window nothing can close. Open this table in the " +
                              "application and press Save a copy again.";
                return;
            }

            string initialDirectory = string.Empty;
            if (!string.IsNullOrWhiteSpace(existingCsvPath))
            {
                initialDirectory = Path.GetDirectoryName(existingCsvPath) ?? string.Empty;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = SuggestedFileName,
                Title = "Save the spectrum comparison table",
                AddExtension = true,
                DefaultExt = ".csv",
                InitialDirectory = initialDirectory,
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                SpectrumComparisonTable.SaveCsv(dialog.FileName, rows);
                status.Text = "Written " + rows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                              " spectrum row(s) to " + dialog.FileName +
                              ". The project and its quantitation were not changed.";
            }
            catch (Exception ex)
            {
                status.Text = "The CSV could not be written, so nothing was saved. Reason given by the " +
                              "program: " + ex.Message + " Check that the file is not open elsewhere " +
                              "and that you can write to that folder, then save again.";
            }
        }
    }
}
