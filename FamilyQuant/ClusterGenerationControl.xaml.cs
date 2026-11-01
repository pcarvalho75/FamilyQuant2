using Microsoft.Win32;
using PatternTools;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// Interaction logic for ClusterGenerationControl
    /// </summary>
    public partial class ClusterGenerationControl : UserControl
    {
        public FamilyClusterFile fcf { get; private set; }
        private string lastShellStatus = string.Empty;

        public string ShellStatusText => DisplayedMode == null
            ? (HasFullXic ? "Family curation. Full XIC available." : "Family curation. No calculated shares.")
            : TextBlockStatusLine.Text.Replace("showing: ", string.Empty, StringComparison.Ordinal);

        public event EventHandler? ShellStatusChanged;

        public bool UseShellStatusBar
        {
            get => BorderCurationStatus.Visibility == Visibility.Collapsed;
            set => BorderCurationStatus.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
        private PatternLabProject? CurrentPLP { get; set; }

        /// <summary>
        /// The mode the numbers ON SCREEN were produced with, which is NOT the mode currently chosen in
        /// the list: those differ between picking a mode and pressing Recalculate. Every label that
        /// names a denominator - the column headers, the status line, the CSV headers - reads this one,
        /// so a stale table can never be labelled with a pending choice. null until the first
        /// calculation.
        /// </summary>
        private NormalizationMode? DisplayedMode { get; set; }

        private SharedPeptidePolicy DisplayedSharedPolicy { get; set; } = SharedPeptidePolicy.FamilyUnique;

        /// <summary>
        /// What the loaded project says about its own quantitation, kept from the load. Describing a
        /// project walks its whole matrix, so recomputing it every time the help panel is opened would
        /// freeze the window for seconds on a real file.
        /// </summary>
        private QuantitationProfile? CurrentProfile { get; set; }

        /// <summary>The column the user last sorted by, re-applied whenever the rows are rebuilt.</summary>
        private string? sortMemberPath;
        private bool sortDescending;

        /// <summary>
        /// One entry per reversible curation step this session, newest at the end. A snapshot is pushed
        /// BEFORE every mutating operation (Recluster by Words, Create Super Cluster, a User ID edit that
        /// changed a number, a lock removal), so Undo can step back without reloading the file. Bounded so
        /// a long session cannot grow it without limit; the oldest step is dropped when the cap is passed.
        /// It is session-only and there is no redo, which is the whole of the ask.
        /// </summary>
        private readonly List<GroupingSnapshot> undoStack = new List<GroupingSnapshot>();

        private const int UndoLimit = 50;

        /// <summary>
        /// The control is meaningless without a Family Cluster File and every handler below works on it,
        /// so it is required here. That makes the invariant part of the type instead of a null check
        /// repeated in each handler - the control is never constructed from XAML, only from MainWindow.
        /// </summary>
        public ClusterGenerationControl(string familyClusterFile)
            : this(FamilyClusterFile.Load(familyClusterFile))
        {
        }

        /// <summary>
        /// Builds the screen over a Family Cluster File already in hand. A plain .fcf reaches this through
        /// the path overload above; a .fqproj container reaches it directly, because its curation was read
        /// from the container's curation.json and there is no file to re-open. Curation is identical
        /// either way: the two share <see cref="FamilyClusterFile.FromJsonText"/>.
        /// </summary>
        public ClusterGenerationControl(FamilyClusterFile familyClusterFile)
        {
            InitializeComponent();

            // The two lists get their text from the enums, so the wording in a column header, a status
            // line or a dialog is guaranteed to be wording the user can also find in the interface.
            FillChoiceLists();

            fcf = familyClusterFile ?? throw new ArgumentNullException(nameof(familyClusterFile));

            GridCopy.Attach(DataGridResultGrid, row => (row as FastaItemExtended)?.SequenceIdentifier,
                            row => (row as FastaItemExtended)?.Description);

            UpdateGUI();
        }

        // ===================================================================== wording

        private void FillChoiceLists()
        {
            SharedItemUnique.Content = SharedPeptidePolicy.FamilyUnique.DisplayName();
            SharedItemSplit.Content = SharedPeptidePolicy.SplitEqually.DisplayName();
            SharedItemEvery.Content = SharedPeptidePolicy.CountInEveryFamily.DisplayName();

            SharedItemUnique.ToolTip = SharedPeptidePolicy.FamilyUnique.ShortExplanation();
            SharedItemSplit.ToolTip = SharedPeptidePolicy.SplitEqually.ShortExplanation();
            SharedItemEvery.ToolTip = SharedPeptidePolicy.CountInEveryFamily.ShortExplanation();

            ComboBoxSharedPeptides.ToolTip =
                "What to do with a peptide found in more than one family. In a redundant database, such as " +
                "a venom, this choice moves the numbers a great deal. Default: " +
                SharedPeptidePolicy.FamilyUnique.DisplayName() + ".";

            ModeItemIdentified.Content = NormalizationMode.IdentifiedSignal.DisplayName();
            ModeItemTic.Content = NormalizationMode.TotalChromatogram.DisplayName();
            ModeItemPeptideSignal.Content = NormalizationMode.IdentifiedPeptideSignal.DisplayName();
            ModeItemChargeRestricted.Content = NormalizationMode.ChargeRestrictedSignal.DisplayName();

            ModeItemIdentified.ToolTip = NormalizationMode.IdentifiedSignal.ShortExplanation();
            ModeItemTic.ToolTip = NormalizationMode.TotalChromatogram.ShortExplanation();
            ModeItemPeptideSignal.ToolTip = NormalizationMode.IdentifiedPeptideSignal.ShortExplanation();
            ModeItemChargeRestricted.ToolTip = NormalizationMode.ChargeRestrictedSignal.ShortExplanation();

            ComboBoxNormalization.ToolTip =
                "What the share column is a percentage OF; the measurements themselves never change. A mode " +
                "is greyed out when the loaded project lacks the totals it needs. Default: " +
                NormalizationMode.IdentifiedSignal.DisplayName() + ".";
        }

        // ===================================================================== wait screen

        /// <summary>
        /// Shows the overlay AND gives WPF a chance to paint it.
        ///
        /// Setting Visibility and then blocking the UI thread - which is what every caller did - queues a
        /// render that only happens after the work has finished, so the overlay flashed at the end or
        /// never appeared at all. Forcing one render pass at Loaded priority makes it actually visible
        /// before the thread is taken.
        ///
        /// The pump is skipped when the control is not on a screen, which is the case in the test
        /// harness: there is no rendering to wait for, and no message loop to pump.
        /// </summary>
        private void BeginWait(string message)
        {
            WaitScreenMessage.Text = message;
            WaitScreen.Visibility = Visibility.Visible;

            if (PresentationSource.FromVisual(this) == null) return;

            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void EndWait()
        {
            WaitScreen.Visibility = Visibility.Collapsed;
        }

        // ===================================================================== filters

        private void CheckBoxShowXICOnly_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateFilteredResults();
        }

        private void CheckBoxExcludeReverse_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (fcf == null) return;

            // Non-destructive view filter only: the decoy exclusion is applied in
            // UpdateFilteredResults(). We must NOT delete decoys from the model here.
            // The previous handler permanently removed "Reverse_" entries and renumbered groups,
            // which could not be undone by un-checking the box, was silently persisted on Save,
            // and destroyed the target/decoy basis that CalculateFDR depends on.
            UpdateFilteredResults();
        }

        private void ComboBoxNormalization_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Changing the mode does not recalculate on its own - the user clicks Recalculate.
            if (CurrentPLP != null && ButtonRecalculateQuantitation != null)
            {
                // The status mark reports pending changes; the action keeps its primary role.
                UpdateLayerStates();
            }

            // The Full XIC column follows the shared-peptide rule at once: nothing there needs Recalculate.
            if (HasFullXic && fcf != null)
            {
                UpdateColumnHeaders();
                UpdateFilteredResults();
            }
        }

        /// <summary>
        /// The reporting mode. SelectedIndex IS the enum value by construction (see NormalizationMode
        /// and the XAML comment on the item list); there is deliberately no lookup table to drift out of
        /// sync. Anything out of range degrades to IdentifiedSignal, the mode that is always available.
        /// </summary>
        private NormalizationMode SelectedMode
        {
            get
            {
                if (ComboBoxNormalization == null) return NormalizationMode.IdentifiedSignal;

                int idx = ComboBoxNormalization.SelectedIndex;
                if (idx < 0 || idx > (int)NormalizationMode.ChargeRestrictedSignal) return NormalizationMode.IdentifiedSignal;

                return (NormalizationMode)idx;
            }
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
            if (ComboBoxNormalization == null) return;

            // "% of identified signal" is a ratio computed WITHIN the file, so it needs no recorded
            // divisor and is always available. The others each need their own per-run total.
            ApplyModeAvailability(ModeItemTic, NormalizationMode.TotalChromatogram,
                                  profile.CanReportTotalChromatogram);

            var d1 = NormalizationPlan.For(CurrentPLP, PatternTools.PLP.NormalizationInfo.TagIdentified);
            ApplyModeAvailability(ModeItemPeptideSignal, NormalizationMode.IdentifiedPeptideSignal,
                                  d1.IsAvailable, d1.UnavailableReason);

            var d3 = NormalizationPlan.For(CurrentPLP, PatternTools.PLP.NormalizationInfo.TagChargeRestricted);
            ApplyModeAvailability(ModeItemChargeRestricted, NormalizationMode.ChargeRestrictedSignal,
                                  d3.IsAvailable, d3.UnavailableReason);

            // WPF keeps a disabled item SELECTED if it already was, so disabling alone is not enough.
            // Fall back to the always-available mode rather than reporting a number the file cannot
            // support.
            var current = ComboBoxNormalization.Items[(int)SelectedMode] as ComboBoxItem;
            if (current != null && !current.IsEnabled)
            {
                ComboBoxNormalization.SelectedIndex = (int)NormalizationMode.IdentifiedSignal;
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

        private SharedPeptidePolicy SelectedSharedPolicy
        {
            get
            {
                switch (ComboBoxSharedPeptides.SelectedIndex)
                {
                    case 1: return SharedPeptidePolicy.SplitEqually;
                    case 2: return SharedPeptidePolicy.CountInEveryFamily;
                    default: return SharedPeptidePolicy.FamilyUnique;
                }
            }
        }

        /// <summary>
        /// "User ID" is editable, and editing it regroups the families - so the XIC User column it is
        /// paired with becomes stale the moment the cell is committed. Recalculating here keeps the two
        /// consistent; without it an export could carry the new grouping beside the old numbers.
        /// </summary>
        private void DataGridResultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            // Compared by reference, not by heading text: headings now name the denominator in use and
            // change when the reporting mode does, so matching on a string would be a latent bug.
            if (!ReferenceEquals(e.Column, ColumnUserId)) return;

            // A hand-typed family number is curation, and curation outranks every automatic
            // reclustering from now on: both the family the rows LEFT and the family they JOINED are
            // locked. The old value is still on the item (the binding writes after this event); the
            // new one is in the editor.
            //
            // Locks are recorded ONLY when the number actually changed: committing a cell untouched
            // (double-click to inspect, Enter to leave) must not lock anything, and the first release
            // of this feature did exactly that, silently. NumberStyles.None admits digits only, so a
            // sign never reaches the lock set; the explicit negative check below also stops the
            // BINDING from accepting a negative family number, which Reanotate Sepro would otherwise
            // stamp into the .sepr2 as a GroupNo no PatternLab tool has ever seen.
            if (e.Row?.Item is FastaItemExtended edited && e.EditingElement is TextBox tb)
            {
                // ONE parse, with the SAME rule the binding applies. WPF's own converter trims
                // whitespace and accepts a sign, and an earlier draft here parsed stricter than
                // that: "500 " pasted from a spreadsheet then renumbered the family through the
                // binding WITHOUT recording the lock, and the next reclustering re-merged it - the
                // original defect resurrected by a trailing space. NumberStyles.Integer mirrors the
                // converter; whatever it rejects is cancelled so the binding cannot disagree.
                if (!int.TryParse(tb.Text, System.Globalization.NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out int typed))
                {
                    e.Cancel = true;
                    return;
                }

                if (typed < 0)
                {
                    e.Cancel = true;

                    // Advisory only, and only when there is a screen to show it on: in the test
                    // harness the control is never rendered, and a modal box with nobody to click OK
                    // hangs the run forever. The cancel above is the protection; this is the courtesy.
                    if (PresentationSource.FromVisual(this) != null)
                    {
                        MessageBox.Show(
                            "Family numbers cannot be negative, so nothing was changed.\n\n" +
                            "Next step: type a whole number, 0 or higher.",
                            "Negative family number", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                if (typed != edited.UserID)
                {
                    // Record the state BEFORE the edit takes effect (the binding writes the new UserID
                    // after this event) so Undo can step this edit back. Only a genuine change gets a
                    // snapshot: an unchanged commit above returns without reaching here.
                    PushUndoSnapshot();

                    fcf.LockedUserIds.Add(edited.UserID);
                    fcf.LockedUserIds.Add(typed);

                    // Straight away, not through the Dispatcher below: the counter is the only thing on
                    // screen that says a lock was just recorded, and an edit that silently protects two
                    // families is what this summary exists to stop.
                    UpdateLockSummary();
                }
            }

            if (CurrentPLP == null)
            {
                // No quantitation to recalculate, but the family counter and the ordering must still
                // follow the edit; without this the screen contradicted the edit until an unrelated
                // click forced a refresh.
                Dispatcher.BeginInvoke(new Action(UpdateFilteredResults),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            // The binding writes the new value after this event, so recalculate once the UI settles.
            Dispatcher.BeginInvoke(new Action(RecalculateQuantitationInternal),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// "Align Selected" needs two rows and did nothing with fewer, so it is disabled until there are
        /// two - a control that is enabled and has no effect is worse than one that is greyed out with a
        /// reason.
        /// </summary>
        private void DataGridResultGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ButtonAlignSelected == null) return;

            ButtonAlignSelected.IsEnabled = DataGridResultGrid.SelectedItems.Count >= 2;
        }

        /// <summary>
        /// Sorts the rows ourselves, so clicking the same header twice on data full of ties gives the
        /// same order every time. See <see cref="ReportView.Sort"/> for why WPF's own sort does not.
        /// </summary>
        private void DataGridResultGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;

            // Toggle from the PERSISTED state, never from e.Column.SortDirection. UpdateFilteredResults
            // re-assigns DataGridResultGrid.ItemsSource, and a WPF DataGrid resets every column's
            // SortDirection adorner to null on that swap, so by the next click e.Column.SortDirection is
            // always null and the direction would never flip to descending. sortMemberPath /
            // sortDescending survive the swap and are the real record of what is sorted and which way.
            var (path, descending) = ReportView.NextSort(e.Column.SortMemberPath, sortMemberPath, sortDescending);
            sortMemberPath = path;
            sortDescending = descending;

            // UpdateFilteredResults re-applies the header arrow after it swaps ItemsSource, so the
            // toggle needs to do nothing more here.
            UpdateFilteredResults();
        }

        private void ButtonRecalculateQuantitation_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentPLP == null)
            {
                MessageBox.Show(
                    "Nothing was recalculated: no quantitation is loaded.\n\n" +
                    "The percentages come from a PatternLab project, and none has been opened yet, so " +
                    "there is nothing to divide.\n\n" +
                    "Next step: go to Layer 2 and press \"Load PLP File\".",
                    "Load a project first", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RecalculateQuantitationInternal();

        }

        private void RecalculateQuantitationInternal()
        {
            if (CurrentPLP == null) return;   // no quantitation loaded yet; nothing to recalculate

            try
            {
                BeginWait("Recalculating family quantitation...");

                NormalizationMode mode = SelectedMode;
                SharedPeptidePolicy shared = SelectedSharedPolicy;

                // One pass, over the curated grouping, which is the only one the table reports.
                fcf.AttributeXICToGroups(CurrentPLP, mode, shared);

                // Only now do the labels change: everything that names a denominator reads these two,
                // never the ComboBoxes, so the headings always describe the numbers on screen.
                DisplayedMode = mode;
                DisplayedSharedPolicy = shared;

                // Diagnostic: Show XIC results summary
                StringBuilder diagMsg = new StringBuilder();
                diagMsg.AppendLine("=== Recalculation Diagnostic ===");
                diagMsg.AppendLine($"Mode: {mode.DisplayName()}");
                diagMsg.AppendLine($"Shared peptides: {shared.DisplayName()}");

                int itemsWithXIC = fcf.MyExtendedFastaItems.Count(x => x.XIC > 0);
                int itemsWithXICUser = fcf.MyExtendedFastaItems.Count(x => x.XICUser > 0);
                double totalXIC = fcf.MyExtendedFastaItems.Sum(x => x.XIC);
                double totalXICUser = fcf.MyExtendedFastaItems.Sum(x => x.XICUser);

                // DefaultIfEmpty: Max() throws on an empty sequence, and an .fcf with no proteins is a
                // bad file rather than a reason to blow up the recalculation.
                double maxXIC = fcf.MyExtendedFastaItems.Select(x => x.XIC).DefaultIfEmpty(0).Max();
                double maxXICUser = fcf.MyExtendedFastaItems.Select(x => x.XICUser).DefaultIfEmpty(0).Max();

                diagMsg.AppendLine($"Items with XIC > 0: {itemsWithXIC}");
                diagMsg.AppendLine($"Items with XICUser > 0: {itemsWithXICUser}");
                diagMsg.AppendLine($"Total XIC sum: {totalXIC:F6}");
                diagMsg.AppendLine($"Total XICUser sum: {totalXICUser:F6}");
                diagMsg.AppendLine($"Max XIC: {maxXIC:F6}");
                diagMsg.AppendLine($"Max XICUser: {maxXICUser:F6}");

                // Show top 5 by XIC
                diagMsg.AppendLine("\nTop 5 by XIC:");
                var top5 = fcf.MyExtendedFastaItems.OrderByDescending(x => x.XIC).Take(5);
                foreach (var item in top5)
                {
                    diagMsg.AppendLine($"  {item.SequenceIdentifier}: XIC={item.XIC:F6}, XICUser={item.XICUser:F6}");
                }

                // Diagnostics go to the console only. This routine runs automatically on PLP load and
                // after every clustering/filter operation, so a modal popup here interrupted normal use.
                Console.WriteLine(diagMsg.ToString());

                // Refresh the DataGrid to show the new values
                DataGridResultGrid.Items.Refresh();
                UpdateColumnHeaders();
                UpdateFilteredResults();
                UpdateLayerStates();

                EndWait();
            }
            catch (Exception ex)
            {
                EndWait();
                MessageBox.Show(
                    "The family percentages could not be recalculated, so the table still shows the " +
                    "previous numbers.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: try a different denominator in \"Report XIC as\", or reload the project " +
                    "in Layer 2. If it keeps happening, send this message and the project file to whoever " +
                    "maintains FamilyQuant.",
                    "Could not recalculate", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===================================================================== loading

        private async void ButtonLoadQuantData_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "PatternLab project file (*.plp)|*.plp",
                Title = "Choose the peptide-level PatternLab project to quantify these families with"
            };

            if (ofd.ShowDialog() != true) return;

            try
            {
                BeginWait("Loading quantitation data from the PatternLab project...");

                Console.WriteLine("Loading Sparse Matrix");

                // Reading a real project takes seconds to minutes. Doing it on the UI thread is what
                // stopped the overlay above from ever being painted, and made the window look hung.
                string path = ofd.FileName;
                PatternLabProject loaded = await Task.Run(() => new PatternLabProject(new FileInfo(path)));

                // Diagnostic: Show NormalizationFactor values
                StringBuilder diagMsg = new StringBuilder();
                diagMsg.AppendLine("=== PLP NormalizationFactor Diagnostic ===");
                diagMsg.AppendLine($"Secondary Labels Count: {loaded.SecondaryLabels?.Count ?? 0}");

                if (loaded.SecondaryLabels != null)
                {
                    foreach (var sl in loaded.SecondaryLabels)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(sl.Name);
                        diagMsg.AppendLine($"  File: {fileName}, NormalizationFactor: {sl.NormalizationFactor}");
                    }
                }

                Console.WriteLine(diagMsg.ToString());

                CurrentPLP = loaded;

                // Work out what the project actually carries BEFORE calculating.
                //
                // PHASE 4: this now READS the denominator the exporter recorded, instead of inferring
                // it from the size of the values. The old inference survives only as a fallback for
                // files written before the record existed, and profile.Evidence always says which of
                // the two produced the answer - an inference must never be shown as if it were a fact.
                QuantitationProfile profile = await Task.Run(() =>
                {
                    fcf.SetLookupDictionary(loaded);
                    return QuantitationProfile.Describe(loaded);
                });

                Console.WriteLine(profile.Summary());

                CurrentProfile = profile;
                TextBlockQuantProfile.Text = profile.Summary();

                // Each mode needs its own per-run total; grey out the ones this file cannot supply.
                UpdateReportingModeAvailability(profile);

                // Perform initial quantitation calculation
                RecalculateQuantitationInternal();

                // Update status
                TextBlockPLPStatus.Text = "✓ Loaded: " + Path.GetFileName(ofd.FileName);
                TextBlockPLPStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                // Enable Layer 3 controls
                ButtonRecalculateQuantitation.IsEnabled = true;
                UpdateLayerStates();

                // Say what the file IS, rather than what the user should go and do about it. The
                // denominator is now a recorded fact for anything exported by a current XQuant2, so
                // "re-export with Normalize XIC by TIC" is only ever the right advice for a legacy
                // file that recorded nothing - and it is only offered in that case.
                //
                // The wording matters: a collaborator read the previous version of this box as "the
                // normalization is not being recognised" and stopped, when in fact the project had
                // loaded correctly and one of four reporting modes was simply not applicable to it.
                // So it now opens by saying that the project loaded, and never uses the bare word
                // "unavailable" as a title.
                if (!profile.CanReportTotalChromatogram)
                {
                    string advice = profile.Evidence == DenominatorEvidence.Recorded
                        ? "Why: the project records which divisor was applied to its values, but it does " +
                          "not carry each run's total ion current, and without that total there is nothing " +
                          "to express the families as a share of."
                        : "Why: the project was exported before PatternLab wrote down which divisor it " +
                          "used, so it carries no per-run totals at all. Re-exporting it from XQuant " +
                          "records the divisor and every run's totals, and this mode then becomes " +
                          "available.";

                    MessageBox.Show(
                        "The project loaded correctly and the table is already showing numbers.\n\n" +
                        "One of the four reporting modes, \"" +
                        NormalizationMode.TotalChromatogram.DisplayName() + "\", is greyed out for it.\n\n" +
                        advice + "\n\n" +
                        "What the project says about itself: " + profile.DenominatorSentence() + "\n\n" +
                        "Next step: nothing is required. The table currently shows \"" +
                        NormalizationMode.IdentifiedSignal.DisplayName() + "\", which needs no recorded " +
                        "totals and is always available. Hover a greyed-out mode to see what it would need.",
                        "Project loaded · one reporting mode is greyed out",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                Console.WriteLine("Done");

                EndWait();
            }
            catch (Exception ex)
            {
                EndWait();

                MessageBox.Show(
                    "The quantitation project could not be opened, so the table still shows whatever it " +
                    "showed before.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the file is a PatternLab project exported at PEPTIDE level and " +
                    "that it is not open in another program, then try again.",
                    "Could not open the project", MessageBoxButton.OK, MessageBoxImage.Error);

                // Reset status
                TextBlockPLPStatus.Text = "Could not open the last project";
                TextBlockPLPStatus.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
                UpdateLayerStates();
            }
        }

        // ===================================================================== export

        private void ButtonSaveReport_Click(object sender, RoutedEventArgs e)
        {
            var rows = VisibleRows();

            if (rows.Count == 0)
            {
                MessageBox.Show(
                    "Nothing was exported: the table has no rows to write.\n\n" +
                    "Every row is currently hidden by a filter, or the Family Cluster File is empty.\n\n" +
                    "Next step: clear the Search box and untick the filters in this panel, then export again.",
                    "Nothing to export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "FamilyQuant_Report.csv",
                Title = "Save the visible rows as CSV"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(saveFileDialog.FileName, CsvReport.Build(rows, DisplayedMode, FullXicCsvHeader()),
                    Encoding.UTF8);

                string what = DisplayedMode == null
                    ? "The share column is empty, because no quantitation was loaded."
                    : "The share column holds " + DisplayedMode.Value.ShortName().Replace("% of", "the share of") +
                      ", computed with " + DisplayedSharedPolicy.ShortName() + ".";

                MessageBox.Show(
                    rows.Count.ToString("N0", CultureInfo.CurrentCulture) + " rows were written to\n" +
                    saveFileDialog.FileName + "\n\n" +
                    what + "\n\n" +
                    "In the file the shares are fractions between 0 and 1, not percentages, and the decimal " +
                    "separator is always a dot so that any program can read them. The table on screen shows " +
                    "the same numbers as percentages in your own language.\n\n" +
                    "Next step: open it in a spreadsheet, or read it straight into R or Python.",
                    "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The file could not be written, so nothing was exported.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the file is not already open in Excel and that you can write to " +
                    "that folder, then export again.",
                    "Could not export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Exactly the rows the grid is showing, i.e. exactly what an export writes.</summary>
        private List<FastaItemExtended> VisibleRows()
        {
            return (DataGridResultGrid.ItemsSource ?? Enumerable.Empty<FastaItemExtended>())
                .Cast<FastaItemExtended>()
                .ToList();
        }

        // ===================================================================== the table

        // ===================================================================== full XIC column

        // The project's own quantitation (Compute / update full XIC), attributed to the CURRENT families.
        // The homology search and the peptide dedup are done once per quantitation, off the interface
        // thread; a regrouping redoes only the join onto the User IDs and one pass over the peptides, and
        // only when the grouping (an exact snapshot, never a hash) or one of the two Layer 3 choices that
        // shape the column has actually changed.
        private List<DeNovo.NativePeptideQuant>? fullXicPeptides;
        private CurationHomologyHits? fullXicHits;
        private FamilyEnvelopeQuantResult? fullXicResult;
        private PeptideFamilyAttribution? fullXicAttribution;
        private double fullXicTic;
        private int fullXicRuns;
        private string? fullXicTicRefusal;
        private string? fullXicProteinFingerprint;
        private int fullXicGeneration;
        private bool fullXicPending;
        // Each item's User ID AND annotation: the annotation names its family (FamilyNaming), so an
        // annotation-only edit has to refresh the family names the export and the spectrum table read.
        private Dictionary<FastaItemExtended, (int UserID, string Annotation)>? fullXicGrouping;
        private SharedPeptidePolicy fullXicPolicyShown;
        private bool fullXicOverTicShown;

        /// <summary>The primary estimate of SCIENTIFIC_METHOD.md, named wherever the column is described.</summary>
        private const string FullXicQuantity =
            "identified observed envelope plus the de novo identified-reference, mass-calibrated estimate";

        /// <summary>The protein-set fingerprint of the attached quantitation's curation, or null.</summary>
        public string? FullXicProteinFingerprint => HasFullXic ? fullXicProteinFingerprint : null;

        /// <summary>True when a full-XIC quantitation is attached to this screen.</summary>
        public bool HasFullXic => fullXicPeptides != null && fullXicPeptides.Count > 0 && fullXicHits != null;

        /// <summary>The family table behind the Full XIC column as last computed, or null.</summary>
        public FamilyEnvelopeQuantResult? FullXicFamilies => fullXicResult;

        /// <summary>The peptide-to-family attribution on the CURRENT grouping, or null without a full XIC.
        /// Cheap: the homology hits are cached; only the join onto the User IDs is redone.</summary>
        public PeptideFamilyAttribution? CurrentFullXicAttribution()
        {
            RefreshFullXicShares();
            return HasFullXic ? fullXicAttribution : null;
        }

        private sealed class FullXicPrepared
        {
            public List<DeNovo.NativePeptideQuant> Peptides = new List<DeNovo.NativePeptideQuant>();
            public CurationHomologyHits Hits = new CurationHomologyHits();
            public double Tic;
            public int Runs;
            public string? TicRefusal;
            public string ProteinFingerprint = string.Empty;
        }

        /// <summary>
        /// The expensive part, safe off the interface thread: it reads only the rows, the TIC map and a
        /// snapshot of the protein list (identifiers and sequences, which curation never changes).
        ///
        /// The TIC basis is every run the computation recorded a TIC for (a run can be processed and
        /// keep no row, and it still belongs in "the TIC of all runs"), keyed by the normalized run name
        /// on both sides (invariant 1). It is refused, never silently narrowed, when a quantified run has
        /// no positive TIC or one run is recorded twice with different values.
        /// </summary>
        private static FullXicPrepared PrepareFullXic(IReadOnlyList<QuantRow> rows,
            IReadOnlyDictionary<string, double>? perRunTic, FamilyClusterFile proteins)
        {
            var prepared = new FullXicPrepared
            {
                Peptides = FamilyEnvelopeQuant.PreparePeptides(rows),
                Hits = FamilyEnvelopeQuant.SearchCuration(rows, proteins),
                // Once per quantitation, here off the interface thread: protein content never changes
                // during curation, so a regrouping does not invalidate it.
                ProteinFingerprint = FamilyEnvelopeQuant.ProteinSetFingerprint(proteins),
            };

            var tic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (perRunTic != null)
            {
                foreach (KeyValuePair<string, double> kv in perRunTic)
                {
                    string run = DeNovo.DeNovoReconciler.NormalizeRunName(kv.Key ?? string.Empty);
                    if (run.Length == 0) continue;
                    if (tic.TryGetValue(run, out double seen) && seen != kv.Value)
                        prepared.TicRefusal = "the TIC of run " + run + " is recorded twice with different values";
                    tic[run] = kv.Value;
                }
            }

            List<string> quantRuns = rows.Select(r => DeNovo.DeNovoReconciler.NormalizeRunName(r.Run ?? string.Empty))
                .Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> missing = quantRuns
                .Where(r => !tic.TryGetValue(r, out double v) || !(v > 0) || double.IsInfinity(v)).ToList();

            if (prepared.TicRefusal == null && tic.Count == 0)
                prepared.TicRefusal = "the project records no total ion current";
            else if (prepared.TicRefusal == null && missing.Count > 0)
                prepared.TicRefusal = "no positive TIC is recorded for run(s) " + string.Join(", ", missing.Take(3)) +
                    (missing.Count > 3 ? " and " + (missing.Count - 3).ToString(CultureInfo.InvariantCulture) + " more" : string.Empty);
            else if (prepared.TicRefusal == null && tic.Values.Any(v => !(v > 0) || double.IsInfinity(v)))
                prepared.TicRefusal = "a recorded TIC is not a positive number";

            prepared.Runs = tic.Count;
            prepared.Tic = prepared.TicRefusal == null ? tic.Values.Sum() : 0;
            return prepared;
        }

        /// <summary>
        /// Attaches the project's full-XIC quant (or detaches it, with null or an empty list) and fills the
        /// Full XIC share column, synchronously. The application uses <see cref="SetFullXicQuantAsync"/>;
        /// this form is for a caller that needs the column filled when it returns (the test harness).
        /// </summary>
        public void SetFullXicQuant(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic)
        {
            int generation = ++fullXicGeneration;
            fullXicDone?.TrySetResult(false);
            fullXicDone = null;
            ClearFullXic();

            FullXicPrepared? prepared = null;
            string? error = null;
            if (fcf != null && rows != null && rows.Count > 0)
            {
                try
                {
                    BeginWait("Attributing the full-XIC quantitation to the families...");
                    prepared = PrepareFullXic(rows.ToList(), perRunTic, ProteinSnapshot());
                }
                catch (Exception ex) { error = ex.Message; }
                finally { EndWait(); }
            }

            ApplyFullXic(prepared, error, generation);
        }

        /// <summary>
        /// The same, with the homology search off the interface thread: the screen stays usable (Save
        /// included) while it runs, the summary line says it is running, and a result that arrives after
        /// a newer quantitation was attached is discarded.
        /// </summary>
        public async Task SetFullXicQuantAsync(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic)
        {
            int generation = ++fullXicGeneration;
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fullXicDone?.TrySetResult(false);
            fullXicDone = done;
            try
            {
                await AttachAsync(rows, perRunTic, generation);
            }
            finally
            {
                done.TrySetResult(true);
            }
        }

        // Completed when the latest background attach has been applied (or replaced): what a caller that
        // needs the attribution awaits, instead of starting a second homology search of its own.
        private TaskCompletionSource<bool>? fullXicDone;

        /// <summary>Completes when no background attach is pending any more.</summary>
        public Task FullXicReady => fullXicDone?.Task ?? Task.CompletedTask;

        private async Task AttachAsync(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic,
                                       int generation)
        {
            ClearFullXic();

            if (fcf == null || rows == null || rows.Count == 0)
            {
                ApplyFullXic(null, null, generation);
                return;
            }

            fullXicPending = true;
            UpdateLayerStates();
            if (TextBlockFullXicSummary != null)
                TextBlockFullXicSummary.Text = "Attributing the full-XIC quantitation to the families (a homology " +
                    "search, which can take a minute on a large database). The Full XIC column fills in when it " +
                    "finishes; you can keep working, and saving now saves the quantitation.";
            UpdateGUI();

            List<QuantRow> rowCopy = rows.ToList();
            Dictionary<string, double>? ticCopy = perRunTic?.ToDictionary(kv => kv.Key, kv => kv.Value);
            FamilyClusterFile proteins = ProteinSnapshot();

            FullXicPrepared? prepared = null;
            string? error = null;
            try { prepared = await Task.Run(() => PrepareFullXic(rowCopy, ticCopy, proteins)); }
            catch (Exception ex) { error = ex.Message; }

            if (generation != fullXicGeneration) return;   // a newer quantitation was attached meanwhile
            ApplyFullXic(prepared, error, generation);
        }

        /// <summary>The proteins as they stand, in a list the interface thread will not reassign under a
        /// background search (UpdateGUI replaces the list when it re-sorts).</summary>
        private FamilyClusterFile ProteinSnapshot() =>
            new FamilyClusterFile { MyExtendedFastaItems = (fcf?.MyExtendedFastaItems ?? new List<FastaItemExtended>()).ToList() };

        private void ClearFullXic()
        {
            fullXicPeptides = null;
            fullXicHits = null;
            fullXicResult = null;
            fullXicAttribution = null;
            fullXicGrouping = null;
            fullXicTic = 0;
            fullXicRuns = 0;
            fullXicTicRefusal = null;
            fullXicProteinFingerprint = null;
            fullXicPending = false;
        }

        private void ApplyFullXic(FullXicPrepared? prepared, string? error, int generation)
        {
            if (generation != fullXicGeneration) return;

            fullXicPending = false;
            if (prepared != null)
            {
                fullXicPeptides = prepared.Peptides;
                fullXicHits = prepared.Hits;
                fullXicTic = prepared.Tic;
                fullXicRuns = prepared.Runs;
                fullXicTicRefusal = prepared.TicRefusal;
                fullXicProteinFingerprint = prepared.ProteinFingerprint;
            }

            if (FullXicBasisTic != null)
            {
                FullXicBasisTic.IsEnabled = fullXicTic > 0;
                FullXicBasisTic.ToolTip = fullXicTic > 0
                    ? "Divides by the summed TIC of every run the computation recorded."
                    : "The TIC basis is unavailable: " + (fullXicTicRefusal ?? "no full XIC has been computed yet") + ".";
            }
            if (ComboBoxFullXicBasis != null && fullXicTic <= 0) ComboBoxFullXicBasis.SelectedIndex = 0;

            if (error != null && TextBlockFullXicSummary != null)
                TextBlockFullXicSummary.Text = "The full-XIC quantitation could not be attributed to the families, so " +
                    "the Full XIC column stays empty. Reason given by the program: " + error;

            UpdateGUI();
            UpdateLayerStates();
        }

        private bool FullXicOverTic => ComboBoxFullXicBasis?.SelectedIndex == 1 && fullXicTic > 0;

        private string FullXicBasisLabel => FullXicOverTic
            ? "the total ion current of " + fullXicRuns.ToString(CultureInfo.InvariantCulture) + " run(s)"
            : "the quantified signal (every quantified peptide counted once)";

        /// <summary>The CSV heading of the Full XIC column: the quantity, what it is a fraction of, and the rule.</summary>
        private string FullXicCsvHeader() => !HasFullXic
            ? CsvReport.NoFullXicHeader
            : "Full XIC family share (" + FullXicQuantity + "), fraction 0-1 of " + FullXicBasisLabel + ", " +
              SelectedSharedPolicy.ShortName() + " (User families)";

        /// <summary>The exact grouping (and annotations) the column was last computed for, still the current one?</summary>
        private bool GroupingUnchanged()
        {
            if (fcf == null || fullXicGrouping == null || fullXicGrouping.Count != fcf.MyExtendedFastaItems.Count) return false;
            foreach (FastaItemExtended item in fcf.MyExtendedFastaItems)
                if (!fullXicGrouping.TryGetValue(item, out var seen) || seen.UserID != item.UserID
                    || !string.Equals(seen.Annotation, item.UserAnnotation ?? string.Empty, StringComparison.Ordinal))
                    return false;
            return true;
        }

        /// <summary>
        /// Recomputes the Full XIC column when the grouping, the shared-peptide rule or the basis changed
        /// since the last time (or when <paramref name="force"/>), and writes the summary line.
        /// </summary>
        private void RefreshFullXicShares(bool force = false)
        {
            if (fcf == null) return;

            if (ButtonExportComposition != null) ButtonExportComposition.IsEnabled = HasFullXic;

            if (!HasFullXic)
            {
                foreach (FastaItemExtended item in fcf.MyExtendedFastaItems) item.FullXicShare = null;
                fullXicResult = null;
                fullXicAttribution = null;
                fullXicGrouping = null;
                if (TextBlockFullXicSummary != null && !fullXicPending
                    && !TextBlockFullXicSummary.Text.StartsWith("The full-XIC quantitation could not", StringComparison.Ordinal))
                    TextBlockFullXicSummary.Text = "No full XIC computed for this project yet: run File · Compute / " +
                        "update full XIC to fill the Full XIC share column.";
                return;
            }

            SharedPeptidePolicy policy = SelectedSharedPolicy;
            bool overTic = FullXicOverTic;
            if (!force && fullXicResult != null && policy == fullXicPolicyShown && overTic == fullXicOverTicShown
                && GroupingUnchanged()) return;

            PeptideFamilyAttribution attribution = FamilyEnvelopeQuant.AttributeHits(fullXicHits!, fcf);
            IReadOnlyList<int> none = Array.Empty<int>();
            fullXicResult = FamilyEnvelopeQuant.BuildFromPeptides(fullXicPeptides!,
                s => attribution.IdentifiedBySequence.TryGetValue(s, out IReadOnlyList<int>? v) ? v : none,
                s => attribution.DeNovoBySequence.TryGetValue(s, out IReadOnlyList<int>? v) ? v : none,
                id => attribution.FamilyDescriptions.TryGetValue(id, out string? d) ? d : string.Empty);
            fullXicResult.IdentityThreshold = attribution.IdentityThreshold;
            fullXicAttribution = attribution;
            fullXicPolicyShown = policy;
            fullXicOverTicShown = overTic;
            fullXicGrouping = new Dictionary<FastaItemExtended, (int, string)>(ReferenceEqualityComparer.Instance);
            foreach (FastaItemExtended item in fcf.MyExtendedFastaItems)
                fullXicGrouping[item] = (item.UserID, item.UserAnnotation ?? string.Empty);

            double denominator = overTic ? fullXicTic : fullXicResult.MeasuredTotalEnvelopeReferenceCalibrated;
            Dictionary<int, double> byFamily = fullXicResult.Families.ToDictionary(f => f.FamilyUserId, f => f.Signal(policy));
            foreach (FastaItemExtended item in fcf.MyExtendedFastaItems)
                item.FullXicShare = denominator > 0
                    ? (byFamily.TryGetValue(item.UserID, out double s) ? s / denominator : 0)
                    : (double?)null;

            if (TextBlockFullXicSummary == null) return;

            string P(double v) => denominator > 0 ? (v / denominator).ToString("P2", CultureInfo.CurrentCulture) : "n/a";
            double families = fullXicResult.Families.Sum(f => f.Signal(policy));
            string text = "Full XIC column (" + FullXicQuantity + "), as a share of " + FullXicBasisLabel +
                ": families " + P(families);
            if (policy == SharedPeptidePolicy.FamilyUnique)
                text += ", peptides shared between families " + P(fullXicResult.SharedTotalEnvelopeReferenceCalibrated);
            text += ", peptides attributed to no family " + P(fullXicResult.UnattributedTotalEnvelopeReferenceCalibrated) + ".";
            if (policy == SharedPeptidePolicy.CountInEveryFamily)
                text += " Shared peptides are counted in every family they reach, so the families overlap and " +
                        "must not be added up.";
            else if (!overTic)
                text += " These add up to 100%.";
            else
                text += " Together they are the share of the TIC the quantified peptides explain.";
            text += " Peptides are attributed to families by homology at " +
                    fullXicResult.IdentityThreshold.ToString("P0", CultureInfo.CurrentCulture) + " identity.";
            if (fullXicTicRefusal != null)
                text += " The TIC basis is unavailable: " + fullXicTicRefusal + ".";
            TextBlockFullXicSummary.Text = text;
        }

        /// <summary>The family composition the Full XIC column stands on, as CSV, or null without a full XIC.
        /// Public so the export is testable without a file dialog.</summary>
        public string? FullXicCompositionCsv()
        {
            RefreshFullXicShares();
            if (!HasFullXic || fullXicResult == null) return null;
            double denominator = FullXicOverTic ? fullXicTic : fullXicResult.MeasuredTotalEnvelopeReferenceCalibrated;
            return CsvReport.BuildFamilyComposition(fullXicResult, SelectedSharedPolicy, denominator,
                "fraction of " + FullXicBasisLabel + "; signal = " + FullXicQuantity);
        }

        private void ButtonExportComposition_Click(object sender, RoutedEventArgs e)
        {
            string? csv = FullXicCompositionCsv();
            if (csv == null || PresentationSource.FromVisual(this) == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "FamilyQuant_family_composition.csv",
                Title = "Save the family composition as CSV"
            };
            if (sfd.ShowDialog() != true) return;

            // The closure stated is the one that holds for the choices in use, never a blanket promise.
            string closure = SelectedSharedPolicy == SharedPeptidePolicy.CountInEveryFamily
                ? "The families are counted with every shared peptide in each, so they overlap and must not be added up."
                : FullXicOverTic
                    ? "The fractions are of the TIC, so together they give the share of the TIC the quantified peptides explain."
                    : "The fractions add up to 1.";

            try
            {
                File.WriteAllText(sfd.FileName, csv, Encoding.UTF8);
                MessageBox.Show(
                    "The family composition was written to\n" + sfd.FileName + "\n\n" +
                    "One row per family, plus the shared-between-families row (with 'Family-unique only') and the " +
                    "not-attributed row. " + closure + " The basis and the shared-peptide rule are named in the last column.",
                    "Composition exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nothing was written. Reason given by the program: " + ex.Message,
                    "Could not export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ComboBoxFullXicBasis_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (fcf == null) return;   // fires once while the XAML selects its default item
            UpdateColumnHeaders();
            UpdateFilteredResults();
        }

        private void UpdateFilteredResults()
        {
            if (fcf == null) return;

            // Recomputes only when the grouping or the column's two choices changed since last time.
            RefreshFullXicShares();

            var finalResult = FamilyFilter.Apply(
                fcf.MyExtendedFastaItems,
                TextBoxSearch.Text,
                CheckBoxShowXICOnly.IsChecked == true,
                CheckBoxExcludeReverse.IsChecked == true,
                CheckBoxExcludeContaminants.IsChecked == true);

            finalResult = ReportView.Sort(finalResult, sortMemberPath, sortDescending);

            // BOTH numbers, always. Reporting only the visible count is what made this label fall when
            // "Hide Reverse" was ticked and disagree with the console, which counts the whole file.
            LabelNumberOfGroups.Content = ReportView.FamilyCounter(
                FamilyFilter.CountFamilies(finalResult),
                FamilyFilter.CountFamilies(fcf.MyExtendedFastaItems));

            DataGridResultGrid.ItemsSource = finalResult;

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

            TextBlockStatusLine.Text = ReportView.StatusLine(
                DisplayedMode,
                DisplayedSharedPolicy,
                ReportView.QuantifiedFamilies(fcf.MyExtendedFastaItems),
                FamilyFilter.CountFamilies(fcf.MyExtendedFastaItems));
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

            if (ColumnFullXic != null)
                ColumnFullXic.Header = !HasFullXic
                    ? "Full XIC share (not computed)"
                    : FullXicOverTic ? "Full XIC, % of TIC" : "Full XIC, % of quantified signal";
        }

        private void UpdateGUI()
        {
            // Ordered by the curated family, so members of one family sit together in the order the
            // user sees. Ordering by USearchID did that only while reclustering still overwrote it;
            // with that field frozen it would scatter a curated family across the grid.
            fcf.MyExtendedFastaItems = fcf.MyExtendedFastaItems.OrderBy(a => a.UserID).ToList();

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

        // ===================================================================== layer states

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

            // Layer 2 is the OPTIONAL PatternLab route. With no .plp it is not blocked, it is unused: the
            // red "BLOCKED · no project loaded" read as an error to a user quantifying by Compute full XIC,
            // and "project" there meant a .plp, not her .fqproj.
            if (haveQuant)
                SetBadge(BadgeLayer2, TextBlockLayer2State, true, null,
                    "A PatternLab project is loaded and its peptides have been matched to these families.");
            else
                SetOptionalBadge(BadgeLayer2, TextBlockLayer2State, "no .plp loaded",
                    "Optional: a PatternLab .plp, exported at peptide level, for the 'Share of signal' " +
                    "column. Not needed when the project has a full XIC (File · Compute / update full XIC).");

            bool pending = haveQuant && DisplayedMode != null &&
                           (DisplayedMode.Value != SelectedMode || DisplayedSharedPolicy != SelectedSharedPolicy);

            if (!haveQuant && HasFullXic)
            {
                SetBadge(BadgeLayer3, TextBlockLayer3State, true, null,
                    "Shared peptides and Full XIC share of apply to the Full XIC column at once. Report XIC " +
                    "as and Recalculate apply only to a .plp loaded in Layer 2.");
            }
            else if (!haveQuant && fullXicPending)
            {
                SetBadge(BadgeLayer3, TextBlockLayer3State, false, null,
                    "The full XIC is being attributed to the families. These options become live when it " +
                    "finishes.");
            }
            else if (!haveQuant)
            {
                SetBadge(BadgeLayer3, TextBlockLayer3State, false, "no quantitation yet",
                    "These options do nothing until there is a quantitation: run File · Compute / update " +
                    "full XIC, or load a PatternLab .plp in Layer 2.");
            }
            else if (pending)
            {
                SetBadge(BadgeLayer3, TextBlockLayer3State, false, "press Recalculate",
                    "The options changed since the table was last calculated. Press Recalculate to apply " +
                    "them.");
            }
            else
            {
                SetBadge(BadgeLayer3, TextBlockLayer3State, true, null,
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

        // ===================================================================== curation locks

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

        /// <summary>
        /// The lock manager over THIS control's file, wired to refresh the summary above AND to record an
        /// undo snapshot before it removes anything, so a lock removal is reversible like every other
        /// curation step.
        ///
        /// Separate from the click handler on purpose: the window can then be built and driven with no
        /// screen and no modal loop, which is the only way the harness can exercise its removal path.
        /// </summary>
        public LockManagerWindow CreateLockManager()
        {
            return new LockManagerWindow(fcf, UpdateLockSummary, PushUndoSnapshot);
        }

        // ===================================================================== undo

        /// <summary>
        /// Records the current grouping state so the next mutating operation can be stepped back. Called
        /// BEFORE the operation mutates anything. Bounded to <see cref="UndoLimit"/> steps.
        /// </summary>
        private void PushUndoSnapshot()
        {
            undoStack.Add(GroupingSnapshot.Capture(fcf));

            // Drop the oldest step once the cap is passed, so the stack cannot grow without limit.
            if (undoStack.Count > UndoLimit) undoStack.RemoveAt(0);

            UpdateUndoButton();
        }

        /// <summary>The same step, also recording the annotations of <paramref name="annotationsOf"/>,
        /// for an operation that is about to rewrite them in bulk. A distinct name, not an overload, so
        /// <see cref="PushUndoSnapshot()"/> stays a single method for the lock manager and the harness.</summary>
        private void PushUndoSnapshotWithAnnotations(IEnumerable<FastaItemExtended> annotationsOf)
        {
            undoStack.Add(GroupingSnapshot.Capture(fcf, annotationsOf));
            if (undoStack.Count > UndoLimit) undoStack.RemoveAt(0);
            UpdateUndoButton();
        }

        private void UpdateUndoButton()
        {
            if (ButtonUndo != null) ButtonUndo.IsEnabled = undoStack.Count > 0;
        }

        /// <summary>
        /// Steps back one curation operation: pops the last snapshot and restores the family numbers, the
        /// lock set and the grouping-provenance flags to what they were just before it. It records NO
        /// snapshot of its own (undo alone is the feature, there is no redo), then refreshes everything a
        /// reclustering would - recalculating the quantitation when a project is loaded.
        /// </summary>
        private void ButtonUndo_Click(object sender, RoutedEventArgs e)
        {
            if (undoStack.Count == 0) return;

            GroupingSnapshot snapshot = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);

            snapshot.Restore(fcf);

            // The share column follows the restored grouping, exactly as it follows an automatic merge.
            if (CurrentPLP != null)
            {
                try
                {
                    RecalculateQuantitationInternal();
                }
                catch (Exception ex)
                {
                    // Guarded like the other advisory boxes: a headless run has no one to click OK, and a
                    // modal with nobody to dismiss it hangs the test harness forever.
                    if (PresentationSource.FromVisual(this) != null)
                    {
                        MessageBox.Show(
                            "The grouping was stepped back, but the percentages could not be recalculated, so " +
                            "the share column still holds the previous numbers and must not be read.\n\n" +
                            "Reason given by the program: " + ex.Message + "\n\n" +
                            "Next step: press Recalculate in Layer 3.",
                            "Undone, but not recalculated", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }

            // UpdateGUI re-sorts by UserID and refreshes the table, the lock summary and the headers.
            UpdateGUI();

            UpdateUndoButton();
        }

        // ===================================================================== batch edit

        /// <summary>
        /// "Apply to selected rows": one User ID and/or one annotation for every selected row, through
        /// <see cref="BatchCuration.Apply"/>. Reported in its own status line rather than a modal box,
        /// so a headless run never blocks; recorded as one undo step and recalculated like an undo.
        /// </summary>
        private void ButtonApplyToSelected_Click(object sender, RoutedEventArgs e)
        {
            if (fcf == null) return;

            List<FastaItemExtended> selected = DataGridResultGrid.SelectedItems
                .OfType<FastaItemExtended>()
                .Distinct()
                .ToList();

            string idText = (TextBoxBatchUserId.Text ?? string.Empty).Trim();
            string note = (TextBoxBatchAnnotation.Text ?? string.Empty).Trim();
            int? userId = null;

            if (idText.Length > 0)
            {
                // The same rule the User ID column applies: an integer, and never negative.
                if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
                {
                    TextBlockBatchStatus.Text = "Nothing was changed: \"" + idText + "\" is not a family number. " +
                        "Next step: type a whole number, 0 or higher, or leave the box empty.";
                    return;
                }
                userId = parsed;
            }

            if (userId == null && note.Length == 0)
            {
                TextBlockBatchStatus.Text = "Nothing was changed: type a User ID, an annotation, or both.";
                return;
            }

            if (selected.Count == 0)
            {
                TextBlockBatchStatus.Text = "Nothing was changed: no row is selected. Next step: click rows " +
                    "in the table (Ctrl+click, Shift+click, or Ctrl+A for every visible row), then apply.";
                return;
            }

            PushUndoSnapshotWithAnnotations(note.Length > 0 ? selected : Enumerable.Empty<FastaItemExtended>());
            BatchCurationResult outcome = BatchCuration.Apply(fcf, selected, userId, note.Length > 0 ? note : null);

            if (outcome.Moved == 0 && outcome.Annotated == 0)
            {
                // Nothing moved, so the step just recorded would undo nothing; take it back off the stack.
                undoStack.RemoveAt(undoStack.Count - 1);
                UpdateUndoButton();
                TextBlockBatchStatus.Text = "Nothing was changed: the " +
                    selected.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " selected row(s) already carry that User ID and annotation.";
                return;
            }

            if (CurrentPLP != null && outcome.Moved > 0)
            {
                try
                {
                    RecalculateQuantitationInternal();
                }
                catch (Exception ex)
                {
                    TextBlockBatchStatus.Text = "The rows were changed, but the percentages could not be " +
                        "recalculated, so the share column must not be read. Reason given by the program: " +
                        ex.Message + " Next step: press Recalculate in Layer 3.";
                    UpdateGUI();
                    return;
                }
            }

            UpdateGUI();

            var parts = new List<string>();
            if (outcome.Moved > 0)
                parts.Add(outcome.Moved.ToString("N0", CultureInfo.CurrentCulture) + " row(s) moved to family " +
                          userId!.Value.ToString(CultureInfo.CurrentCulture) + " (now locked)");
            if (outcome.Annotated > 0)
                parts.Add(outcome.Annotated.ToString("N0", CultureInfo.CurrentCulture) + " row(s) annotated \"" + note + "\"");
            TextBlockBatchStatus.Text = string.Join("; ", parts) + ". Undo steps it back.";
        }

        private void ButtonManageLocks_Click(object sender, RoutedEventArgs e)
        {
            CreateLockManager().ShowFor(Window.GetWindow(this));
        }

        // ===================================================================== headers

        /// <summary>
        /// A tooltip on each column header. DataGridColumn is not a control, so the tooltip has to be
        /// attached through the header's style; a Style that sets one property inherits everything else
        /// from the theme, so the headers keep their normal appearance.
        /// </summary>
        private void ApplyColumnHeaderTooltips()
        {
            ColumnUSearchId.HeaderStyle = HeaderTip(
                "The last automatic grouping, from USEARCH and any word or keyword merge. Read-only; it " +
                "equals User ID except where you renumbered a family by hand.");

            ColumnUserId.HeaderStyle = HeaderTip(
                "Your own family number, the only editable id. Type the same number in two rows to merge " +
                "their families, or an unused one to split a protein off; editing locks the families involved.");

            ColumnUserAnnotation.HeaderStyle = HeaderTip(
                "Free text of your own. It is saved in the .fcf and written to the CSV, and it affects no " +
                "calculation.");

            ColumnLocus.HeaderStyle = HeaderTip(
                "The protein identifier as in the database. \"" + SequenceLabels.DecoyPrefix +
                "\" marks a decoy and \"" + SequenceLabels.ContaminantMarker + "\" a " +
                "laboratory contaminant; both can be hidden in the Display and Export panel. Right-click a " +
                "row to copy its UniProt code or open its UniProt page; Ctrl+C copies the column you clicked.");

            ColumnDomains.HeaderStyle = HeaderTip(
                "The protein's Pfam domains, from UniProt: what it contains, whatever it is called. Empty until " +
                "Fetch Domains is pressed, and for a protein too short for Pfam. The Search box finds them.");

            ColumnMass.HeaderStyle = HeaderTip(
                "The sum of the protein's residue masses in daltons, without the terminal water. Add " +
                "18.011 Da for the neutral protein mass.");

            ColumnFullXic.HeaderStyle = HeaderTip(
                "This family's share of the full XIC (File · Compute / update full XIC), the primary " +
                "estimate: identified peptides measured in the RAW files plus an estimate for de novo " +
                "peptides. Set what it is a share of in Layer 3.");

            ColumnXicUser.HeaderStyle = HeaderTip(
                "The share of signal held by this protein's family, from the .plp loaded in Layer 2. The " +
                "heading names what it is a percentage of; change it in Layer 3 and press Recalculate.");

            ColumnDescription.HeaderStyle = HeaderTip(
                "The FASTA description line of the protein. Recluster by Words and Create Super Cluster " +
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
            style.Setters.Add(new Setter(System.Windows.Controls.ToolTipService.ShowDurationProperty, 60000));
            style.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, WrappingHeaderTemplate));

            return style;
        }

        private static readonly DataTemplate WrappingHeaderTemplate = BuildWrappingHeaderTemplate();

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

        public void SaveFamilyClusterFile(string fileName)
        {
            fcf.Save(fileName);
        }

        private void DataGridResultGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString(CultureInfo.CurrentCulture);
        }

        private void TextBoxSearch_KeyUp(object sender, KeyEventArgs e)
        {
            UpdateFilteredResults();
        }

        private void TextBoxSuperClusterWording_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ButtonSuperCluster == null) return;

            ButtonSuperCluster.IsEnabled = !string.IsNullOrWhiteSpace(TextBoxSuperClusterWording.Text);
        }

        // ===================================================================== help

        private void ButtonHelp_Click(object sender, RoutedEventArgs e)
        {
            ShowHelp();
        }

        /// <summary>Also reachable from the main window's Help menu, which calls this.</summary>
        public void ShowHelp()
        {
            QuantitationProfile? profile = CurrentProfile;

            TextReportWindow.Show(
                "FamilyQuant · How to read these numbers",
                HelpContent.Build(profile, DisplayedMode ?? SelectedMode, DisplayedMode == null ? SelectedSharedPolicy : DisplayedSharedPolicy),
                Window.GetWindow(this));
        }

        // ===================================================================== clustering

        private async void ButtonAlignSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = DataGridResultGrid.SelectedItems.Cast<FastaItemExtended>().ToList();

            if (selectedItems.Count < 2)
            {
                MessageBox.Show(
                    "Nothing was aligned: an alignment needs at least two sequences and " +
                    selectedItems.Count + " row(s) are selected.\n\n" +
                    "Next step: open the Data Browser tab and select two or more rows, holding Ctrl to " +
                    "pick them one by one or Shift to take a range.",
                    "Select at least two proteins", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                BeginWait("Aligning " + selectedItems.Count + " selected sequences...");

                // Run USEARCH off the UI thread so a large alignment can't freeze (or appear to hang) the app.
                string alignment = await Task.Run(() => USearchTools.Align(selectedItems));

                EndWait();

                TextReportWindow.Show(
                    "Multiple sequence alignment · " + selectedItems.Count + " sequences",
                    alignment,
                    Window.GetWindow(this));
            }
            catch (Exception ex)
            {
                EndWait();
                MessageBox.Show(
                    "The selected sequences could not be aligned, and nothing in your file was changed.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: this step runs the bundled USEARCH program, which antivirus software " +
                    "sometimes blocks. Try again with fewer sequences, and if it keeps failing set the " +
                    "FAMILYQUANT_USEARCH environment variable to a copy of usearch.exe you control.",
                    "Could not align", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ButtonWordCluster_Click(object sender, RoutedEventArgs e)
        {
            await PerformClustering((double)(DecimalUpDownWordReCluster.Value ?? 0.95m), ReclusterExcludeWords());
        }

        /// <summary>The words Recluster by Words ignores when comparing descriptions.</summary>
        public List<string> ReclusterExcludeWords() => WordCluster.ParseKeywords(TextBoxWordsToBeExcluded.Text);

        /// <summary>Seam for the suite only: answers in place of UniProt, so no test reaches the network.</summary>
        public System.Net.Http.HttpMessageHandler? FetchDomainsHandlerOverride { get; set; }

        /// <summary>Fills every protein's Pfam domains from UniProt. They reach the file on the next save.</summary>
        private async void ButtonFetchDomains_Click(object sender, RoutedEventArgs e)
        {
            // Layer 1 can be pressed with a cell of the other tab still open, and a table in edit refuses
            // the refresh that follows.
            DataGridResultGrid.CommitEdit(DataGridEditingUnit.Row, true);

            string title = "Fetch domains";
            string message;
            MessageBoxImage icon = MessageBoxImage.Information;
            bool fetched = false;

            BeginWait("Asking UniProt for each protein's Pfam domains...");
            IsEnabled = false;
            try
            {
                Evidence.EvidenceFetchResult result = await Evidence.EvidenceFetcher.FetchAsync(fcf, FetchDomainsHandlerOverride);
                fetched = result.Asked > 0;
                if (fetched) fcf.DomainSource = result.Source;

                int notFound = result.Asked - result.Found;
                message = result.Asked == 0
                    ? "No protein in this file has a UniProt code, so UniProt has nothing to say about them.\n\n" +
                      "Domains come from UniProt's annotation, so a database of another origin stays without them."
                    : result.WithDomains.ToString("N0", CultureInfo.CurrentCulture) + " of " +
                      result.Asked.ToString("N0", CultureInfo.CurrentCulture) + " proteins have Pfam domains in " + result.Source + ".\n\n" +
                      (notFound > 0 ? notFound.ToString("N0", CultureInfo.CurrentCulture) +
                                      " of them were not found in UniProt, probably withdrawn or merged entries, and were left as they were.\n\n" : "") +
                      "They are in the Domains column, the Search box finds them, and the super cluster preview can " +
                      "choose by them. A protein too short for Pfam has none.\n\n" +
                      "Next step: save the file to keep them.";
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException
                                       || ex is System.Text.Json.JsonException || ex is System.IO.InvalidDataException)
            {
                icon = MessageBoxImage.Warning;
                title = "No domains fetched";
                message = "No domains were fetched and the file is as it was: UniProt could not be reached or " +
                          "gave an answer the program could not read.\n\nReason given: " + ex.Message + "\n\n" +
                          "Next step: check the internet connection and press Fetch Domains again.";
            }
            finally
            {
                IsEnabled = true;
                EndWait();
            }

            if (fetched) UpdateFilteredResults();

            // Only with a person to answer it: in the test harness a modal box hangs the run.
            if (FetchDomainsHandlerOverride == null && PresentationSource.FromVisual(this) != null)
                MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }

        private async void ButtonSuperCluster_Click(object sender, RoutedEventArgs e)
        {
            // The wait overlay stops the mouse, not Space or Enter on the focused button, and a second run
            // would stack a second preview whose merge could undo the first one's ticks.
            if (superClusterRunning) return;
            superClusterRunning = true;
            try { await RunSuperCluster(); }
            finally { superClusterRunning = false; }
        }

        private bool superClusterRunning;

        private async Task RunSuperCluster()
        {
            // Single words as before; a "quoted phrase" is one keyword matched as consecutive words.
            List<string> keywords = WordCluster.ParseKeywords(TextBoxSuperClusterWording.Text);

            List<string> wordsToExclude = ReclusterExcludeWords();

            // Default is "match all words" (the first item is selected in XAML): a multi-word phrase then
            // pins one class instead of gathering everything that shares a single common word.
            KeywordMatch match = ComboBoxSuperClusterMatch.SelectedIndex == 1
                ? KeywordMatch.Any
                : KeywordMatch.All;

            if (keywords.Count == 0)
            {
                MessageBox.Show(
                    "No super cluster was created: the keyword box is empty, so there is nothing to " +
                    "match families against.\n\n" +
                    "Next step: type one or more words that appear in the descriptions of the families you " +
                    "want joined, separated by commas or spaces, for example: metalloproteinase, SVMP",
                    "Type at least one keyword", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Worked out on copies, off the interface thread: indexing a large file's descriptions takes
            // a moment, and the grid stays keyboard-reachable meanwhile.
            var (scopeItems, scoped) = ClusteringScope();
            HashSet<int>? searchFamilies = scoped ? new HashSet<int>(scopeItems.Select(i => i.UserID)) : null;
            List<FastaItemExtended> allItems = fcf.MyExtendedFastaItems.ToList();
            var lockedNow = new HashSet<int>(fcf.LockedUserIds);
            string keywordsText = TextBoxSuperClusterWording.Text;
            string excludeText = TextBoxSuperClusterExclude.Text;

            SuperClusterPreview preview;
            BeginWait("Reading the descriptions for the preview...");
            IsEnabled = false;
            try
            {
                preview = await Task.Run(() => new SuperClusterPreview(
                    allItems, searchFamilies, keywordsText, excludeText, match, wordsToExclude, lockedNow));
            }
            finally
            {
                IsEnabled = true;
                EndWait();
            }

            if (!ConfirmSuperCluster(preview)) return;

            // What was settled in the preview stays in the boxes, so the next run starts from it.
            TextBoxSuperClusterWording.Text = preview.KeywordsText;
            TextBoxSuperClusterExclude.Text = preview.ExcludeText;
            ComboBoxSuperClusterMatch.SelectedIndex = preview.Match == KeywordMatch.Any ? 1 : 0;

            PerformClustering(preview);
        }

        /// <summary>Seam for the suite only, like <see cref="DeNovoControl.ImportOverride"/>: stands in for the
        /// modal window. The application never sets it.</summary>
        public Func<SuperClusterPreview, bool>? ConfirmSuperClusterOverride { get; set; }

        private bool ConfirmSuperCluster(SuperClusterPreview preview)
        {
            if (ConfirmSuperClusterOverride != null) return ConfirmSuperClusterOverride(preview);

            // No owner means the user left this screen while the preview was being built.
            Window? owner = Window.GetWindow(this);
            if (owner == null) return false;

            var window = new SuperClusterPreviewWindow(preview) { Owner = owner };
            window.ShowDialog();
            return window.Confirmed;
        }

        /// <summary>
        /// The items the next reclustering should run over, and whether that is a scoped subset.
        ///
        /// With the Search box empty this is the whole file, so a recluster behaves exactly as before.
        /// With text in it, it is every protein of every family the search shows (see
        /// <see cref="WordCluster.ScopeItems"/>): the operation then reorganises just that subgroup and
        /// leaves every hidden family's numbers untouched. Deliberately scoped by the SEARCH TEXT only,
        /// not by the hide-decoys / hide-contaminants checkboxes, so hiding rows for display never
        /// silently drops a family from a recluster.
        /// </summary>
        private (List<FastaItemExtended> items, bool scoped) ClusteringScope()
        {
            string search = TextBoxSearch?.Text ?? string.Empty;
            var items = WordCluster.ScopeItems(fcf.MyExtendedFastaItems, search);
            return (items, !string.IsNullOrWhiteSpace(search));
        }

        private async Task PerformClustering(double threshold, List<string> wordsToExclude)
        {
            // Record the state before anything changes, so this whole operation is one undo step.
            PushUndoSnapshot();

            // When Search has text, work only on the families it shows; otherwise the whole universe.
            var (scopeItems, scoped) = ClusteringScope();

            WordCluster wc = new WordCluster(scopeItems, wordsToExclude);

            int before = FamilyFilter.CountFamilies(scopeItems);

            BeginWait("Merging families whose descriptions are at least " +
                      threshold.ToString("0.##", CultureInfo.CurrentCulture) + " alike...");

            // A COPY, not the live set: the clustering runs on a background task while the grid
            // stays keyboard-reachable, and HashSet is not safe under a concurrent Add.
            var lockedNow = new HashSet<int>(fcf.LockedUserIds);
            List<List<int>> result = await Task.Run(() => wc.GroupByWords(threshold, lockedNow));

            EndWait();

            // Stable ids: an untouched family keeps its exact number, a merge adopts the smallest
            // member's number. Dense renumbering from zero renumbered families the operation never
            // touched, and the bench noticed within one session.
            var newClusterIdMapping = WordCluster.BuildStableMapping(result);

            // Recorded on the file itself so it survives a save/load, then UpdateClusterIds refreshes the
            // header from it.
            fcf.WordSimilarity = threshold;
            fcf.IsKeywordSuperCluster = false;

            UpdateClusterIds(newClusterIdMapping, scoped ? scopeItems : null);

            Console.WriteLine($"Clusters created: {result.Count}");

            string scopeNote = scoped
                ? "Only the families matching the Search box took part; every family the search hid kept " +
                  "its number untouched.\n\n"
                : string.Empty;

            MessageBox.Show(
                "The families were merged by their description words: " +
                before.ToString("N0", CultureInfo.CurrentCulture) +
                (scoped ? " matching families became " : " families became ") +
                result.Count.ToString("N0", CultureInfo.CurrentCulture) + ".\n\n" +
                scopeNote +
                "Any percentages in the table have been recalculated over the new grouping, and the " +
                "\"Word similarity\" label now reads " +
                threshold.ToString("0.##", CultureInfo.CurrentCulture) + ".\n\n" +
                "Next step: the counter above the table reads \"visible · in file\"; the left number is " +
                "lower whenever a filter such as \"Hide Reverse\" is on. Press Undo to step this merge " +
                "back, or save under a new name to keep it.",
                "Reclustering complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Carries out what the preview showed: the listed proteins move, and nothing is matched again,
        /// so the merge is the one that was on screen. The window said everything a message could say
        /// afterwards, so there is none.</summary>
        private void PerformClustering(SuperClusterPreview preview)
        {
            // Record the state before anything changes, so this whole operation is one undo step.
            PushUndoSnapshot();

            SuperClusterJoin join = preview.Join(fcf.MyExtendedFastaItems);
            fcf.IsKeywordSuperCluster = true;

            WordCluster.SyncUnlockedIds(join.Moved, fcf.LockedUserIds);

            // What stayed behind was left out on purpose, and a later Recluster by Words must not put it
            // back. The lock goes on the new family, not on what stayed: the disintegrins left behind are
            // exactly what the next super cluster wants to gather.
            if (join.SplitFamilies)
            {
                fcf.LockedUserIds.Add(join.Family);
                UpdateLockSummary();
            }

            AfterRegrouping();

            Console.WriteLine($"Super cluster created from {preview.JoiningFamilies.Count} families");
        }

        /// <summary>
        /// Applies a reclustering result to the curated grouping.
        ///
        /// The mapping is keyed on <see cref="FastaItemExtended.UserID"/>, never on USearchID, so a family
        /// the user separated by hand stays separated: it arrives at the algorithm as its own curated
        /// group instead of coming back through as one USEARCH group that overwrites the split. Locked
        /// families are held out of the merge upstream, so their UserID never moves here.
        ///
        /// After the new UserID is written, the two columns are put back in step: for every UNLOCKED
        /// family USearchID is set equal to UserID (see <see cref="WordCluster.SyncUnlockedIds"/>). The
        /// invariant is that an automatic operation keeps USearchID == UserID; the two columns differ
        /// ONLY where the user renumbered a family by hand, which is exactly what a lock records. A
        /// locked family is left untouched on both sides: it did not move, and its frozen USearchID is
        /// the pre-manual-edit number the curation is meant to preserve. Freezing USearchID for every
        /// family, as an earlier build did, was the defect: it desynced the columns for the unlocked
        /// families the program merged on its own, so an automatic merge left the old numbers showing.
        ///
        /// <paramref name="scopedItems"/> is null for a whole-universe operation (Search empty) and, for
        /// a scoped one, the exact items the reclustering ran over. The mapping already contains keys ONLY
        /// for the scoped families, so a family outside the scope keeps its UserID untouched here; the
        /// USearchID sync is likewise restricted to the scoped items, so an out-of-scope family keeps both
        /// of its numbers exactly as they were.
        /// </summary>
        private void UpdateClusterIds(Dictionary<int, int> newClusterIdMapping,
                                     IReadOnlyCollection<FastaItemExtended>? scopedItems = null)
        {
            List<int> newUserIDs = new List<int>();

            for (int i = 0; i < fcf.MyExtendedFastaItems.Count; i++)
            {
                var item = fcf.MyExtendedFastaItems[i];

                // Keyed by the protein's current (old) curated family, NOT by its position in the list.
                // Keying by index `i` scattered the members of a single family across multiple new
                // clusters and left every protein past the family count un-reclustered. An out-of-scope
                // family is simply absent from the mapping, so it keeps its number.
                newUserIDs.Add(newClusterIdMapping.TryGetValue(item.UserID, out int newId)
                    ? newId
                    : item.UserID);
            }

            for (int i = 0; i < fcf.MyExtendedFastaItems.Count; i++)
            {
                fcf.MyExtendedFastaItems[i].UserID = newUserIDs[i];
            }

            // Keep the two id columns in step for everything the program just regrouped on its own.
            // A locked family is a hand renumbering and is the only place the columns are allowed to
            // diverge, so it is left untouched; every unlocked family gets USearchID = UserID. On a scoped
            // operation only the scoped items are synced, so an out-of-scope family's USearchID is not
            // touched either.
            WordCluster.SyncUnlockedIds(scopedItems ?? fcf.MyExtendedFastaItems, fcf.LockedUserIds);

            AfterRegrouping();
        }

        /// <summary>What every regrouping ends with: the numbers on screen follow the new families.</summary>
        private void AfterRegrouping()
        {
            // Recalculate XIC if we have quantitation data. The lookups do NOT need rebuilding here:
            // reclustering changes family membership, not the protein-to-peptide map, and rebuilding
            // threw away the memoised per-peptide XICs on every single reclustering.
            if (CurrentPLP != null)
            {
                try
                {
                    RecalculateQuantitationInternal();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "The families were regrouped, but their percentages could not be recalculated, so " +
                        "the share column still holds the numbers from the previous grouping and must " +
                        "not be read.\n\n" +
                        "Reason given by the program: " + ex.Message + "\n\n" +
                        "Next step: press Recalculate in Layer 3. If it fails again, reload the project in " +
                        "Layer 2.",
                        "Regrouped, but not recalculated", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            UpdateGUI();
        }
    }
}
