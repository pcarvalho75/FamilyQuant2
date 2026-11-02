using FamilyQuant.DeNovo;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant
{
    public static partial class SpectrumComparisonWindow
    {
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
                "The main plot quantity, called PrimaryQuantValue in the CSV. It can differ from M+0 XIC; " +
                "the next column says how it was obtained.", "0.######E+0"));
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

        private static DataGridTextColumn Column(string header, string property, double width,
                                                 string tooltip, string? format = null)
        {
            var binding = new Binding(property);
            if (format != null) binding.StringFormat = format;

            var style = SecondaryWindowView.HeaderTip(tooltip);
            var word = new TextBlock { Text = header.Split(' ').OrderByDescending(w => w.Length).First(),
                FontSize = (double)(Application.Current?.TryFindResource("CaptionSize") ?? 12.0),
                FontFamily = Application.Current?.TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI") };
            word.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            return new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                Width = new DataGridLength(width),
                MinWidth = Math.Max(width, word.DesiredSize.Width + 44),
                HeaderStyle = style,
                ElementStyle = SecondaryWindowView.CellText(),
            };
        }

    }
}
