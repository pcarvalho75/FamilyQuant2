using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PatternTools.PLP;

namespace FamilyQuant;

public partial class ClusterGenerationControl
{
        private void FillChoiceLists()
        {
            QuantitationPanel.SharedItemUnique.Content = SharedPeptidePolicy.FamilyUnique.DisplayName();
            QuantitationPanel.SharedItemSplit.Content = SharedPeptidePolicy.SplitEqually.DisplayName();
            QuantitationPanel.SharedItemEvery.Content = SharedPeptidePolicy.CountInEveryFamily.DisplayName();

            QuantitationPanel.SharedItemUnique.ToolTip = SharedPeptidePolicy.FamilyUnique.ShortExplanation();
            QuantitationPanel.SharedItemSplit.ToolTip = SharedPeptidePolicy.SplitEqually.ShortExplanation();
            QuantitationPanel.SharedItemEvery.ToolTip = SharedPeptidePolicy.CountInEveryFamily.ShortExplanation();

            QuantitationPanel.ComboBoxSharedPeptides.ToolTip =
                "What to do with a peptide found in more than one family; in a redundant database, " +
                "this choice moves the numbers a great deal. Default: " +
                SharedPeptidePolicy.FamilyUnique.DisplayName() + ".";

            QuantitationPanel.ModeItemIdentified.Content = NormalizationMode.IdentifiedSignal.DisplayName();
            QuantitationPanel.ModeItemTic.Content = NormalizationMode.TotalChromatogram.DisplayName();
            QuantitationPanel.ModeItemPeptideSignal.Content = NormalizationMode.IdentifiedPeptideSignal.DisplayName();
            QuantitationPanel.ModeItemChargeRestricted.Content = NormalizationMode.ChargeRestrictedSignal.DisplayName();

            QuantitationPanel.ModeItemIdentified.ToolTip = NormalizationMode.IdentifiedSignal.ShortExplanation();
            QuantitationPanel.ModeItemTic.ToolTip = NormalizationMode.TotalChromatogram.ShortExplanation();
            QuantitationPanel.ModeItemPeptideSignal.ToolTip = NormalizationMode.IdentifiedPeptideSignal.ShortExplanation();
            QuantitationPanel.ModeItemChargeRestricted.ToolTip = NormalizationMode.ChargeRestrictedSignal.ShortExplanation();

            QuantitationPanel.ComboBoxNormalization.ToolTip =
                "What the share column is a percentage OF; the measurements themselves never change, and a mode " +
                "is greyed out when the loaded project lacks the totals it needs. Default: " +
                NormalizationMode.IdentifiedSignal.DisplayName() + ".";
        }

        /// <summary>
        /// Greys out the reporting modes the loaded project cannot supply, and explains why in a tooltip.
        ///
        /// Items are DISABLED, never hidden and never removed. Removing one would shift every index above
        /// it, and index 1 would stop meaning TIC - silently changing what an existing selection reports,
        /// which is the one thing this must not do. A greyed item with a reason is also diagnosable,
        /// whereas an absent item is indistinguishable from an old build.
        /// </summary>
        private void UpdateReportingModeAvailability(QuantitationProfile profile)
        {
            if (QuantitationPanel.ComboBoxNormalization == null) return;

            // "% of identified signal" is a ratio computed WITHIN the file, so it needs no recorded
            // divisor and is always available. The others each need their own per-run total.
            ApplyModeAvailability(QuantitationPanel.ModeItemTic, NormalizationMode.TotalChromatogram,
                                  profile.CanReportTotalChromatogram);

            var d1 = NormalizationPlan.For(CurrentPLP, PatternTools.PLP.NormalizationInfo.TagIdentified);
            ApplyModeAvailability(QuantitationPanel.ModeItemPeptideSignal, NormalizationMode.IdentifiedPeptideSignal,
                                  d1.IsAvailable, d1.UnavailableReason);

            var d3 = NormalizationPlan.For(CurrentPLP, PatternTools.PLP.NormalizationInfo.TagChargeRestricted);
            ApplyModeAvailability(QuantitationPanel.ModeItemChargeRestricted, NormalizationMode.ChargeRestrictedSignal,
                                  d3.IsAvailable, d3.UnavailableReason);

            // WPF keeps a disabled item SELECTED if it already was, so disabling alone is not enough.
            // Fall back to the always-available mode rather than reporting a number the file cannot
            // support.
            var current = QuantitationPanel.ComboBoxNormalization.Items[(int)SelectedMode] as ComboBoxItem;
            if (current != null && !current.IsEnabled)
            {
                QuantitationPanel.ComboBoxNormalization.SelectedIndex = (int)NormalizationMode.IdentifiedSignal;
            }
        }

        private void ApplyModeAvailability(ComboBoxItem item, NormalizationMode mode, bool available,
                                           string? unavailableReason = null)
        {
            if (item == null) return;

            item.IsEnabled = available;

            // Set BOTH ways round: this runs on every project load, so a one-way assignment would strand
            // a stale "unavailable" tooltip on a mode the newly loaded file does support.
            //
            // The unavailable wording says what is missing and what would fix it, in that order, because
            // a greyed-out item with no explanation is exactly the dead end this phase exists to remove.
            item.ToolTip = available
                ? mode.ShortExplanation()
                : "Not available for the loaded project. " +
                  (string.IsNullOrEmpty(unavailableReason)
                      ? "It does not record the per-run totals this mode divides by."
                      : unavailableReason) +
                  " Pick another mode.";
        }

        private void UpdateFilteredResults()
        {
            if (fcf == null) return;

            // Recomputes only when the grouping or the column's two choices changed since last time.
            RefreshFullXicShares();

            ViewModel.SearchText = TextBoxSearch.Text;
            ViewModel.OnlyQuantified = CheckBoxShowXICOnly.IsChecked == true;
            ViewModel.HideDecoys = CheckBoxExcludeReverse.IsChecked == true;
            ViewModel.HideContaminants = CheckBoxExcludeContaminants.IsChecked == true;
            ViewModel.SortMemberPath = sortMemberPath; ViewModel.SortDescending = sortDescending;
            ViewModel.RefreshRows();
            var finalResult = ViewModel.Rows;

            // BOTH numbers, always. Reporting only the visible count is what made this label fall when
            // "Hide Reverse" was ticked and disagree with the console, which counts the whole file.
            LabelNumberOfGroups.Content = ReportView.FamilyCounter(
                FamilyFilter.CountFamilies(finalResult),
                FamilyFilter.CountFamilies(fcf.MyExtendedFastaItems));

            DataGridResultGrid.ItemsSource = ViewModel.SelectedFamilyProteins;

            // Re-assigning ItemsSource resets every column's SortDirection to null, so the header arrow
            // is re-applied here, once, for EVERY refresh (filter, edit, recluster, load, header click),
            // not only the header-click path. This sets only the adorner: the data was already ordered
            // by ReportView.Sort above, so it neither re-sorts nor raises the Sorting event.
            foreach (var column in DataGridResultGrid.Columns)
            {
                column.SortDirection = sortMemberPath != null && column.SortMemberPath == sortMemberPath
                    ? (sortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending)
                    : (ListSortDirection?)null;
            }

            if (ButtonSaveReport != null) ButtonSaveReport.IsEnabled = finalResult.Count > 0;

            UpdateStatusLine();
            UpdateLayerStates();
        }

        private void UpdateStatusLine()
        {
            if (TextBlockStatusLine == null || fcf == null) return;
            if (lastRecalculationFailed && saveResultStatus.Length > 0) return;
            saveResultStatus = string.Empty;
            if (statusTooltipBeforeSave != null)
            {
                TextBlockStatusLine.ToolTip = statusTooltipBeforeSave;
                statusTooltipBeforeSave = null;
            }

            string patternLabStatus = ReportView.StatusLine(
                DisplayedMode,
                DisplayedSharedPolicy,
                ReportView.QuantifiedFamilies(fcf.MyExtendedFastaItems),
                FamilyFilter.CountFamilies(fcf.MyExtendedFastaItems));
            TextBlockStatusLine.Text = HasFullXic
                ? ReportView.FullXicStatusLine(FullXicBasisLabel, Quantitation.SharedPolicy, DisplayedMode == null ? null : patternLabStatus)
                : patternLabStatus;
            PublishShellStatus();
        }

        private void PublishShellStatus()
        {
            string current = ShellStatusText;
            if (!string.Equals(lastShellStatus, current, StringComparison.Ordinal))
            {
                lastShellStatus = current;
                ShellStatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// The share column is headed with the denominator it is a percentage of. "XIC User" named
        /// neither the quantity nor the unit, and the values were shown as raw fractions - 0.121109 with
        /// no clue that it meant 12.111%.
        /// </summary>
        private void UpdateColumnHeaders()
        {
            if (ColumnXicUser == null) return;

            ColumnXicUser.Header = DisplayedMode == null
                ? "Share of signal (none calculated yet)"
                : DisplayedMode.Value.ColumnHeader();

            if (ColumnFamilyPatternLab != null)
            {
                ColumnFamilyPatternLab.Header = ColumnXicUser.Header;
                ColumnFamilyPatternLab.Visibility = DisplayedMode == null ? Visibility.Collapsed : Visibility.Visible;
            }

            if (ColumnFullXic != null)
                ColumnFullXic.Header = !HasFullXic
                    ? "Full XIC share (not computed)"
                    : FullXicOverTic ? "Full XIC, % of TIC" : "Full XIC, % of quantified signal";
            if (ColumnFamilyFullXic != null && ColumnFullXic != null) ColumnFamilyFullXic.Header = ColumnFullXic.Header;
        }

        private void UpdateGUI()
        {
            // Ordered by the curated family, so members of one family sit together in the order the
            // user sees. Ordering by USearchID did that only while reclustering still overwrote it;
            // with that field frozen it would scatter a curated family across the grid.
            // Evidence recipes and their Undo snapshots address the original protein positions.
            // Preserve that order; the grid orders its own view above. Legacy files retain their path.
            ViewModel.RefreshDocument();

            LabelClusterThreshold.Content = fcf.ClusteringIdentity.ToString(CultureInfo.CurrentCulture) + "%";
            LabelWordSimilarity.Content = fcf.GroupingDescription();

            // Covers the load and the legacy migration (both reach here through the constructor) and
            // every reclustering (which ends in UpdateGUI).
            UpdateLockSummary();
            UpdateUndoButton();

            ApplyColumnHeaderTooltips();
            UpdateColumnHeaders();
            UpdateFilteredResults();
        }

        /// <summary>
        /// Every Layer says where you are and, when it cannot be used, what would unblock it. The order
        /// FASTA -> .fcf -> PLP -> recalculate -> export was previously discoverable only by trying
        /// things and noticing which buttons did nothing.
        /// </summary>
        private void UpdateLayerStates()
        {
            if (TextBlockLayer1State == null) return;

            SetBadge(BadgeLayer1, TextBlockLayer1State, true, null,
                "The families are loaded and can be merged further here.");

            bool haveQuant = CurrentPLP != null;

            // PatternLab source is the OPTIONAL PatternLab route. With no .plp it is not blocked, it is unused: the
            // red "BLOCKED · no project loaded" read as an error to a user quantifying with Quantify,
            // and "project" there meant a .plp, not her .fqproj.
            if (haveQuant)
                SetBadge(QuantitationPanel.BadgeLayer2, QuantitationPanel.TextBlockLayer2State, true, null,
                    "A PatternLab project is loaded and its peptides have been matched to these families.");
            else
                SetOptionalBadge(QuantitationPanel.BadgeLayer2, QuantitationPanel.TextBlockLayer2State, "no .plp loaded",
                    "Optional: a PatternLab .plp, exported at peptide level, for the 'Share of signal' " +
                    "column. Full XIC is available separately through Quantify.");

            bool pending = haveQuant && DisplayedMode != null &&
                           (DisplayedMode.Value != SelectedMode || DisplayedSharedPolicy != SelectedSharedPolicy);

            if (!haveQuant && HasFullXic)
            {
                SetBadge(QuantitationPanel.BadgeLayer3, QuantitationPanel.TextBlockLayer3State, true, null,
                    "Shared peptides and Share of apply to Full XIC immediately. PatternLab share of " +
                    "and Recalculate apply only to the optional .plp column.");
            }
            else if (!haveQuant && fullXicPending)
            {
                SetBadge(QuantitationPanel.BadgeLayer3, QuantitationPanel.TextBlockLayer3State, false, null,
                    "The full XIC is being attributed to the families. These options become live when it " +
                    "finishes.");
            }
            else if (!haveQuant)
            {
                SetBadge(QuantitationPanel.BadgeLayer3, QuantitationPanel.TextBlockLayer3State, false, "no quantitation yet",
                    "Run Quantify for the full XIC, or use Load PatternLab project " +
                    "under PatternLab (optional). The source's options become available when it is loaded.");
            }
            else if (pending)
            {
                SetBadge(QuantitationPanel.BadgeLayer3, QuantitationPanel.TextBlockLayer3State, false, "press Recalculate",
                    "The options changed since the table was last calculated. Press Recalculate to apply " +
                    "them.");
            }
            else
            {
                SetBadge(QuantitationPanel.BadgeLayer3, QuantitationPanel.TextBlockLayer3State, true, null,
                    "The table matches the options selected here.");
            }

            bool anyRows = DataGridResultGrid?.ItemsSource != null && VisibleRows().Count > 0;

            SetBadge(BadgeExport, TextBlockExportState, anyRows, "no visible rows",
                anyRows
                    ? "The filters below decide which rows are shown, and an export writes exactly those."
                    : "Every row is hidden by a filter, so there is nothing to export. Clear the Search " +
                      "box and untick the filters.");
        }

        private static void SetBadge(System.Windows.Controls.Border badge, TextBlock text,
                                     bool ready, string? blockedBecause, string tooltip)
        {
            if (badge == null || text == null) return;

            text.Text = ReportView.LayerState(ready, blockedBecause);

            badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ClearBrush");
            text.SetResourceReference(TextBlock.ForegroundProperty,
                ready ? "SuccessBrush" : blockedBecause == null ? "WarningBrush" : "ErrorBrush");

            badge.ToolTip = tooltip;
        }

        /// <summary>A Layer that is not in use and does not need to be: neutral grey, never red.</summary>
        private static void SetOptionalBadge(System.Windows.Controls.Border badge, TextBlock text,
                                             string reason, string tooltip)
        {
            if (badge == null || text == null) return;

            text.Text = "OPTIONAL · " + reason;
            badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ClearBrush");
            text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            badge.ToolTip = tooltip;
        }

        /// <summary>
        /// The lock summary in Layer 1.
        ///
        /// Locks are otherwise invisible: they are recorded by editing the User ID column, and until this
        /// counter existed the only evidence of one was the LockedUserIds array inside the saved JSON. It
        /// is refreshed from the LIVE set by every path that can change it - the load and its legacy
        /// migration, a User ID edit, and a removal in the lock manager - rather than being cached.
        /// </summary>
        private void UpdateLockSummary()
        {
            if (LabelLockCount == null) return;

            int locked = fcf == null ? 0 : fcf.LockedUserIds.Count;

            LabelLockCount.Content = locked == 0
                ? "No families locked"
                : locked.ToString("N0", CultureInfo.CurrentCulture) +
                  (locked == 1 ? " family locked" : " families locked");

            if (ButtonManageLocks != null) ButtonManageLocks.IsEnabled = locked > 0;
        }

        private void UpdateUndoButton()
        {
            if (ButtonUndo != null) ButtonUndo.IsEnabled = undoStack.Count > 0;
        }

        /// <summary>
        /// A tooltip on each column header. DataGridColumn is not a control, so the tooltip has to be
        /// attached through the header's style; a Style that sets one property inherits everything else
        /// from the theme, so the headers keep their normal appearance.
        /// </summary>
        private void ApplyColumnHeaderTooltips()
        {
            ApplyFamilyHeaderTooltips();
            ColumnUSearchId.HeaderStyle = HeaderTip(
                "The last automatic family number, from sequence identity and any word or keyword grouping. " +
                "Read only; hand curation changes User ID without changing this number.");

            ColumnUserId.HeaderStyle = HeaderTip(
                "Current family number for this protein: starts at its stored value, accepts whole numbers from 0 to 2147483647, with no unit. " +
                "An existing number joins that family and an unused number splits this protein off; editing locks affected families, and Undo restores the change.");

            ColumnUserAnnotation.HeaderStyle = HeaderTip(
                "Annotation for this protein: starts at its stored text, or empty, and accepts free text with no unit or fixed length limit. " +
                "The edit changes the project and can affect its family name; Undo restores the text, and Save or CSV export keeps it on disk.");

            ColumnLocus.HeaderStyle = HeaderTip(
                "A UniProt accession where available, otherwise the database identifier; hover over a cell for the original; " + SequenceLabels.DecoyPrefix
                + " marks a decoy and " + SequenceLabels.ContaminantMarker + " marks a contaminant. " +
                "Right-click for its UniProt code or page; Ctrl+C copies the clicked column.");

            ColumnDomains.HeaderStyle = HeaderTip(
                "Pfam domains recorded for this protein, searchable through Search, with fetched previews shown while evidence downloads. " +
                "Read only; an empty value can mean no evidence or no domain call and does not prove domain absence.");

            ColumnMass.HeaderStyle = HeaderTip(
                "Sum of the protein's residue masses in daltons, without terminal water; add 18.011 Da for neutral protein mass. " +
                "This value is read only and changes no project data.");

            ColumnFullXic.HeaderStyle = HeaderTip(
                "This family's share of the full XIC (from Quantify), the primary " +
                "estimate: identified peptides measured in the RAW files plus an estimate for de novo " +
                "peptides. Choose its denominator with Share of under Full XIC in Quantitation.");

            ColumnXicUser.HeaderStyle = HeaderTip(
                "The share of signal held by this protein's family, from the .plp under PatternLab (optional). " +
                "The heading names its denominator; change PatternLab share of and press Recalculate.");

            ColumnDescription.HeaderStyle = HeaderTip(
                "The FASTA description line of the protein. Recluster by words and Group by keywords " +
                "read it, so poor descriptions limit how well they work.");
        }

        /// <summary>
        /// A per-column header style carrying that column's tooltip, plus the wrapping heading template.
        ///
        /// The template is what stops a heading from being cut off. A DataGridColumnHeader shows its
        /// content in a plain ContentPresenter, which renders a string on one line and clips whatever
        /// does not fit - and these headings name the denominator, so the longest of them is far wider
        /// than the column. Wrapping trades a taller header row for a heading that can always be read.
        ///
        /// A Style that sets a few properties still inherits its Template and everything else from the
        /// theme, so the headers keep their normal appearance and their sort arrows.
        /// </summary>
        private static Style HeaderTip(string tooltip)
        {
            var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                Application.Current.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as Style);

            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, tooltip));
            style.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, WrappingHeaderTemplate));

            return style;
        }

        private static DataTemplate BuildWrappingHeaderTemplate()
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));

            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            var template = new DataTemplate { VisualTree = text };
            template.Seal();

            return template;
        }

        private void DataGridResultGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(CultureInfo.CurrentCulture);
        }

        private static readonly DataTemplate WrappingHeaderTemplate = BuildWrappingHeaderTemplate();
}
