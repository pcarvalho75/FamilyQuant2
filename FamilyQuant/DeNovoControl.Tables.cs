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
                "Precursor mass-to-charge ratio reported by the row's de novo caller. " +
                "The paired PatternLab precursor is within 20 ppm at the same charge.");

            ColumnQueueCharge.HeaderStyle = HeaderTip(
                "The precursor charge reported by the row's de novo caller. The PatternLab " +
                "row always has the same charge here.");

            ColumnQueueDeNovoSequence.HeaderStyle = HeaderTip(
                "Sequence reported by the row's de novo caller, with modifications. " +
                "Inspection compares sequences without modifications and with I and L treated as equivalent.");

            ColumnQueuePatternLabSequence.HeaderStyle = HeaderTip(
                "The sequence PatternLab assigned to the same spectrum. Same as the de novo one for a " +
                "weak PSM row, different for an alternative sequence row.");

            ColumnQueueXCorr.HeaderStyle = HeaderTip(
                "PatternLab's cross-correlation score for the paired identification. " +
                "A same-sequence match below 2.5 enters the weak-PSM review queue.");

            ColumnQueueAlc.HeaderStyle = HeaderTip(
                "Whole-sequence native confidence in percent: PEAKS ALC or DeepNovo SCORE, from 0 to 100. " +
                "These caller-specific scales are retained separately, without conversion or an implied FDR.");

            ColumnQueueMinConfidence.HeaderStyle = HeaderTip(
                "Lowest supplied local confidence across the peptide's residues, in percent. " +
                "It describes the weakest reported residue rather than the average confidence.");

            ColumnQueuePpm.HeaderStyle = HeaderTip(
                "Absolute precursor m/z difference in parts per million, relative to the de novo precursor m/z. " +
                "Comparable precursors pass the 20 ppm inspection tolerance.");

            ColumnQueueVerdict.HeaderStyle = HeaderTip(
                "Why the row is here: an alternative sequence for a comparable precursor, or the same " +
                "sequence with a weak PatternLab XCorr. This inspection table does not change stored quantities.");
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
                "Description of the first target protein retained for this family in the homology result. " +
                "It is one representative description rather than a combined annotation of all members.");
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

            return style;
        }
}
