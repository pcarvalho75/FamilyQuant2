using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using Microsoft.Win32;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using System.Windows.Data;

namespace FamilyQuant;

public partial class DeNovoControl
{
        private void ApplyReviewColumnTooltips()
        {
            RightAlign(ColumnQueueScan, ColumnQueueMz, ColumnQueueCharge, ColumnQueueXCorr,
                ColumnQueueAlc, ColumnQueueMinConfidence, ColumnQueuePpm);
            ColumnQueueSource.HeaderStyle = HeaderTip("The de novo program that supplied this row. Its native confidence field is not converted to another program's scale.");
            ColumnQueueRun.HeaderStyle = HeaderTip(
                "The acquisition run, by file name without extension. Runs are matched to PatternLab " +
                "by this name, never by order.");

            ColumnQueueScan.HeaderStyle = HeaderTip(
                "The scan number within the run. A comparable precursor also needs the same charge " +
                "and a close precursor m/z.");

            ColumnQueueMz.HeaderStyle = HeaderTip(
                "The precursor m/z " + ActiveDeNovoSource.DisplayName + " reported. PatternLab's value " +
                "agrees within tolerance; the ppm column shows by how much.");

            ColumnQueueCharge.HeaderStyle = HeaderTip(
                "The precursor charge " + ActiveDeNovoSource.DisplayName + " reported. The PatternLab " +
                "row always has the same charge here.");

            ColumnQueueDeNovoSequence.HeaderStyle = HeaderTip(
                "The sequence " + ActiveDeNovoSource.DisplayName + " read from the spectrum, with its " +
                "modifications. I and L count as the same, so a row here differs by more than that.");

            ColumnQueuePatternLabSequence.HeaderStyle = HeaderTip(
                "The sequence PatternLab assigned to the same spectrum. Same as the de novo one for a " +
                "weak PSM row, different for an alternative sequence row.");

            ColumnQueueXCorr.HeaderStyle = HeaderTip(
                "PatternLab's main identification score (cross-correlation). A low value is what sends " +
                "a matching row here for review.");

            ColumnQueueAlc.HeaderStyle = HeaderTip(
                "The " + ActiveDeNovoSource.ConfidenceColumn + " that " + ActiveDeNovoSource.DisplayName +
                " gave the whole sequence, 0 to 100. A high score can still hide a poorly read region; " +
                "see the next column.");

            ColumnQueueMinConfidence.HeaderStyle = HeaderTip(
                "Confidence of the weakest residue. A high average with a low minimum means part of " +
                "the sequence is a guess.");

            ColumnQueuePpm.HeaderStyle = HeaderTip(
                "How far apart the two precursor masses are, in parts per million. Closer to zero is " +
                "a stronger pairing.");

            ColumnQueueVerdict.HeaderStyle = HeaderTip(
                "Why the row is here: an alternative sequence for a comparable precursor, or the same " +
                "sequence with a weak PatternLab XCorr. Both are left out of the de novo quantitation.");
        }

        private void ApplyXicColumnTooltips()
        {
            ColumnXicRun.HeaderStyle = HeaderTip(
                "The acquisition run, by file name without extension. Its .raw was found by this " +
                "name, never by order.");

            ColumnXicRowsIn.HeaderStyle = HeaderTip(
                "Kept de novo rows of this run sent for extraction: the De novo only and Different " +
                "precursor context rows. Rows matching PatternLab are left out, so no signal counts twice.");

            ColumnXicUnique.HeaderStyle = HeaderTip(
                "Rows merged by scan, m/z and charge: one request per precursor, even when a spectrum " +
                "has several de novo candidates.");

            ColumnXicWithSignal.HeaderStyle = HeaderTip(
                "Requests whose extraction found a signal. Still counted per MS2 scan: several can " +
                "be the same peak.");

            ColumnXicDistinctPeaks.HeaderStyle = HeaderTip(
                "Distinct chromatographic peaks, the ones the M+0 total sums. A peak sampled by " +
                "several MS2 scans counts once.");

            ColumnXicRedundantAnchors.HeaderStyle = HeaderTip(
                "Requests that landed in a peak another request had already found, so they add " +
                "nothing.");

            ColumnXicRedundantArea.HeaderStyle = HeaderTip(
                "The area those repeated requests would have added a second time. Shown so you can " +
                "see what was avoided.");

            ColumnXicSharedWindow.HeaderStyle = HeaderTip(
                "An upper limit on area that might still be counted twice: separate peaks in the same " +
                "precursor window. Nothing is subtracted for it.");

            ColumnXicTotalM0.HeaderStyle = HeaderTip(
                "Sum of M+0 areas over this run's distinct peaks: the de novo part of the partition. " +
                "M+0 only, to match PatternLab's quants.");

            ColumnXicElapsed.HeaderStyle = HeaderTip(
                "Seconds spent extracting this run, mostly loading the RAW. A run read from the cache " +
                "is much faster.");
        }

        private void ApplyHomologyColumnTooltips()
        {
            RightAlign(ColumnFamilyId, ColumnFamilyPeptides);
            ColumnFamilyId.HeaderStyle = HeaderTip(
                "The family's User ID, the grouping you can edit. Using it keeps every family you " +
                "split by hand.");

            ColumnFamilyPeptides.HeaderStyle = HeaderTip(
                "Distinct de novo peptides assigned to this family, target hits only. A peptide in " +
                "two families counts in both.");

            ColumnFamilyDescription.HeaderStyle = HeaderTip(
                "The description of the family's first target protein. A sample, not a summary of " +
                "all its members.");
        }

        private void ApplyPartitionColumnTooltips(ThreeWayPartitionRow? sample)
        {
            string Provenance(Func<ThreeWayPartitionRow, PartitionValue> pick) =>
                sample == null
                    ? " Source: not known until a partition has been built."
                    : " Source: " + pick(sample).Label;

            ColumnPartitionRun.HeaderStyle = HeaderTip(
                "The acquisition run, or \"" + ThreeWayPartition.GroupedRunName + "\" for the total " +
                "line. Every column was matched by this name, never by order.");

            ColumnPartitionTic.HeaderStyle = HeaderTip(
                "The run's whole total ion current, nothing trimmed at either end." +
                Provenance(r => r.Tic));

            ColumnPartitionPatternLab.HeaderStyle = HeaderTip(
                "The identified part: this run's PatternLab quants, M+0 only." +
                Provenance(r => r.PatternLab));

            ColumnPartitionDeNovo.HeaderStyle = HeaderTip(
                "The de novo part: M+0 summed over this run's distinct peaks, each peak counted once." +
                Provenance(r => r.DeNovo));

            ColumnPartitionRemainder.HeaderStyle = HeaderTip(
                "The total minus the other two: signal nobody identified. It can be negative, and it " +
                "is never set to zero." +
                Provenance(r => r.Remainder));

            ColumnPartitionSharePl.HeaderStyle = HeaderTip(
                "The PatternLab part as a percentage of the run's ion current, to four decimals.");

            ColumnPartitionShareDn.HeaderStyle = HeaderTip(
                "The de novo part as a percentage of the run's ion current. Comparable with PatternLab " +
                "because both use M+0 and count each peak once.");

            ColumnPartitionShareRest.HeaderStyle = HeaderTip(
                "What is left, as a percentage. The three shares add up to 100% by design, and this " +
                "is checked.");
        }

        private static void RightAlign(params DataGridTextColumn[] columns)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            foreach (var column in columns) column.ElementStyle = style;
        }

        private static Style HeaderTip(string tooltip)
        {
            var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                Application.Current?.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as Style);
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 6, 4, 6)));

            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, tooltip));
            style.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 60000));

            return style;
        }
}
