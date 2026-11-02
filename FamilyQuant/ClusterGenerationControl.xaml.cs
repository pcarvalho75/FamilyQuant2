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
        private string saveResultStatus = string.Empty;
        private object? statusTooltipBeforeSave;

        public string ShellStatusText => saveResultStatus.Length > 0 ? saveResultStatus : DisplayedMode == null
            ? (HasFullXic ? "Family curation. Full XIC available." : "Family curation. No calculated shares.")
            : TextBlockStatusLine.Text.Replace("showing: ", string.Empty, StringComparison.Ordinal);

        public event EventHandler? ShellStatusChanged;

        public bool UseShellStatusBar
        {
            get => BorderCurationStatus.Visibility == Visibility.Collapsed;
            set => BorderCurationStatus.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
        private PatternLabProject? CurrentPLP
        {
            get => ViewModel?.Quantitation.CurrentPLP;
            set => Quantitation.CurrentPLP = value;
        }

        /// <summary>
        /// The mode the numbers ON SCREEN were produced with, which is NOT the mode currently chosen in
        /// the list: those differ between picking a mode and pressing Recalculate. Every label that
        /// names a denominator - the column headers, the status line, the CSV headers - reads this one,
        /// so a stale table can never be labelled with a pending choice. null until the first
        /// calculation.
        /// </summary>
        private NormalizationMode? DisplayedMode
        {
            get => ViewModel?.Quantitation.DisplayedMode;
            set => Quantitation.DisplayedMode = value;
        }

        private SharedPeptidePolicy DisplayedSharedPolicy
        {
            get => ViewModel?.Quantitation.DisplayedSharedPolicy ?? SharedPeptidePolicy.FamilyUnique;
            set => Quantitation.DisplayedSharedPolicy = value;
        }

        /// <summary>
        /// What the loaded project says about its own quantitation, kept from the load. Describing a
        /// project walks its whole matrix, so recomputing it every time the help panel is opened would
        /// freeze the window for seconds on a real file.
        /// </summary>
        private QuantitationProfile? CurrentProfile
        {
            get => ViewModel?.Quantitation.CurrentProfile;
            set => Quantitation.CurrentProfile = value;
        }

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
            ViewModel = new CurationViewModel(fcf);
            undoStack = ViewModel.UndoStack;
            DataContext = ViewModel;
            InitializeFamilyView();
            QuantitationPanel.DataContext = Quantitation;
            ViewModel.DocumentChanged += (_, _) => DocumentChanged?.Invoke(this, EventArgs.Empty);
            Quantitation.Refreshed += (_, _) => UpdateGUI();
            Loaded += (_, _) => { _ = Operations; };

            GridCopy.Attach(DataGridResultGrid, row => (row as FastaItemExtended)?.SequenceIdentifier,
                            row => (row as FastaItemExtended)?.Description);

            UpdateGUI();
        }

        // ===================================================================== wording

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
            BeginSharedWait(message);
        }

        private void EndWait()
        {
            EndSharedWait();
        }

        // ===================================================================== filters

        private void CheckBoxShowXICOnly_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateFilteredResults();
        }

        private void SearchIdVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (ColumnUSearchId != null)
                ColumnUSearchId.Visibility = CheckBoxShowSearchIds.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
            if (CurrentPLP != null && QuantitationPanel.ButtonRecalculateQuantitation != null)
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
                if (QuantitationPanel.ComboBoxNormalization == null) return NormalizationMode.IdentifiedSignal;

                int idx = QuantitationPanel.ComboBoxNormalization.SelectedIndex;
                if (idx < 0 || idx > (int)NormalizationMode.ChargeRestrictedSignal) return NormalizationMode.IdentifiedSignal;

                return (NormalizationMode)idx;
            }
        }

        private SharedPeptidePolicy SelectedSharedPolicy
        {
            get
            {
                switch (QuantitationPanel.ComboBoxSharedPeptides.SelectedIndex)
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
            if (ReferenceEquals(e.Column, ColumnUserAnnotation))
            {
                if (e.Row.Item is FastaItemExtended item && e.EditingElement is TextBox editor
                    && (item.UserAnnotation ?? string.Empty) != editor.Text)
                {
                    PushUndoSnapshotWithAnnotations(new[] { item });
                    Dispatcher.BeginInvoke(new Action(() => { ViewModel.NotifyDocumentChanged(); UpdateGUI(); }),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
                return;
            }
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
                        ShowCurationMessage(
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
                    ViewModel.RecordFamilyEdit(edited, typed);
                    UpdateUndoButton();
                    Dispatcher.BeginInvoke(new Action(() => ViewModel.NotifyDocumentChanged()),
                        System.Windows.Threading.DispatcherPriority.Background);

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
            if (DataGridResultGrid.SelectedItems.Count > 0) SelectedProteinActions.IsExpanded = true;
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

        private async void ButtonRecalculateQuantitation_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentPLP == null) { ShowCurationOperationResult("Load a PatternLab project before recalculating its shares."); return; }
            await RunCurationAsync("Recalculate family shares", async operation =>
            {
                var prepared = await Quantitation.PrepareRecalculationAsync(SelectedMode, SelectedSharedPolicy, operation);
                operation.Commit(() =>
                {
                    Quantitation.PublishRecalculation(prepared);
                    DataGridResultGrid.Items.Refresh(); UpdateColumnHeaders(); UpdateFilteredResults(); UpdateLayerStates();
                    ViewModel.NotifyDocumentChanged();
                });
                ShowCurationOperationResult("Family shares recalculated using the selected denominator and shared-peptide rule.");
            });
        }

        private void RecalculateQuantitationInternal()
        {
            if (CurrentPLP == null) return;
            try
            {
                Quantitation.Recalculate(SelectedMode, SelectedSharedPolicy);
                DataGridResultGrid.Items.Refresh(); UpdateColumnHeaders(); UpdateFilteredResults(); UpdateLayerStates();
            }
            catch (Exception ex) { ShowCurationOperationResult("The family shares could not be recalculated: " + ex.Message); }
        }

        // ===================================================================== loading

        private async void ButtonLoadQuantData_Click(object sender, RoutedEventArgs e)
        {
            if (Operations.IsBusy) return;
            var dialog = new OpenFileDialog { Filter = "PatternLab project (*.plp)|*.plp", Title = "Choose the peptide-level PatternLab project" };
            if (!ShellDialogs.ShowFile(this, "curation.patternlab", dialog)) return;
            await RunCurationAsync("Load PatternLab quantitation", async operation =>
            {
                operation.Report("load", "Loading the peptide-level PatternLab project");
                var prepared = await Quantitation.PreparePatternLabAsync(dialog.FileName, SelectedMode, SelectedSharedPolicy, operation.CancellationToken);
                operation.Commit(() =>
                {
                    Quantitation.PublishPatternLab(prepared);
                    UpdateReportingModeAvailability(prepared.Profile);
                    QuantitationPanel.TextBlockQuantProfile.Text = prepared.Profile.Summary();
                    QuantitationPanel.TextBlockPLPStatus.Text = "Loaded: " + Path.GetFileName(dialog.FileName);
                    QuantitationPanel.IconPLPStatus.Visibility = Visibility.Visible;
                    QuantitationPanel.TextBlockPLPStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    QuantitationPanel.ButtonRecalculateQuantitation.IsEnabled = true;
                    ViewModel.NotifyDocumentChanged(); UpdateGUI();
                });
                ShowCurationOperationResult("PatternLab project loaded. " + prepared.Profile.DenominatorSentence()
                    + (prepared.Profile.CanReportTotalChromatogram ? "" : " TIC reporting is unavailable; hover the mode for its reason."));
            });
        }

        // ===================================================================== export

        private async void ButtonSaveReport_Click(object sender, RoutedEventArgs e)
        {
            if (Operations.IsBusy) return;
            if (VisibleRows().Count == 0) { ShowCurationOperationResult("Nothing was exported: no protein row is visible. Clear the search or display filters, then export again."); return; }
            var dialog = new SaveFileDialog { Filter = FileDialogFilters.Csv, FileName = "FamilyQuant_Report.csv", Title = "Save the visible rows as CSV" };
            if (!ShellDialogs.ShowFile(this, "curation.proteins-csv", dialog)) return;
            await RunCurationAsync("Export visible protein rows", async operation =>
            {
                var rows = VisibleRows(); var mode = DisplayedMode; string heading = FullXicCsvHeader();
                operation.Report("export", "Preparing the visible protein report");
                await Task.Run(() =>
                {
                    string csv = CsvReport.Build(rows, mode, heading);
                    AppOperationFiles.WriteFile(dialog.FileName, temporary => File.WriteAllText(temporary, csv, Encoding.UTF8), operation);
                }, operation.CancellationToken);
                ShowCurationOperationResult(rows.Count.ToString("N0", CultureInfo.CurrentCulture) + " protein rows exported to " + dialog.FileName
                    + ". Shares are fractional values with an invariant decimal point.");
            });
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
        // only when the grouping (an exact snapshot, never a hash) or one of the two Quantitation options choices that
        // shape the column has actually changed.
        public CurationViewModel ViewModel { get; private set; } = null!;
        private CurationQuantitationViewModel Quantitation => ViewModel.Quantitation;
        public string? FullXicProteinFingerprint => Quantitation.FullXicProteinFingerprint;
        public bool HasFullXic => ViewModel != null && Quantitation.HasFullXic;
        public FamilyEnvelopeQuantResult? FullXicFamilies => Quantitation.FullXicFamilies;
        public Task FullXicReady => projectionDone?.Task ?? Quantitation.FullXicReady;
        private bool fullXicPending => ViewModel != null && Quantitation.IsPending;
        private bool FullXicOverTic => Quantitation.FullXicOverTic;
        private string FullXicBasisLabel => Quantitation.FullXicBasisLabel;
        private void SyncQuantitationChoices()
        {
            Quantitation.SharedPolicy = SelectedSharedPolicy;
            Quantitation.BasisIndex = QuantitationPanel.ComboBoxFullXicBasis.SelectedIndex;
        }
        public PeptideFamilyAttribution? CurrentFullXicAttribution()
        {
            SyncQuantitationChoices(); return Quantitation.CurrentFullXicAttribution();
        }
        public void SetFullXicQuant(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic)
        {
            SyncQuantitationChoices(); Quantitation.SetFullXicQuant(rows, perRunTic);
        }
        public Task SetFullXicQuantAsync(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic)
        {
            return QueueFullXicAsync(rows, perRunTic);
        }
        public string? FullXicCompositionCsv()
        {
            SyncQuantitationChoices(); return Quantitation.FullXicCompositionCsv();
        }
        private string FullXicCsvHeader() { SyncQuantitationChoices(); return Quantitation.FullXicCsvHeader(); }
        private void RefreshFullXicShares(bool force = false)
        {
            if (!Quantitation.CanUseTic && QuantitationPanel.ComboBoxFullXicBasis.SelectedIndex == 1)
                QuantitationPanel.ComboBoxFullXicBasis.SelectedIndex = 0;
            SyncQuantitationChoices(); Quantitation.RefreshFullXicShares(force);
            QuantitationPanel.TextBlockFullXicSummary.Text = Quantitation.Summary;
            QuantitationPanel.FullXicBasisTic.IsEnabled = Quantitation.CanUseTic;
            QuantitationPanel.FullXicBasisTic.ToolTip = Quantitation.TicHelp;
            ButtonExportComposition.IsEnabled = HasFullXic;
        }

        private async void ButtonExportComposition_Click(object sender, RoutedEventArgs e)
        {
            if (Operations.IsBusy || !HasFullXic) return;
            var dialog = new SaveFileDialog { Filter = FileDialogFilters.Csv, FileName = "FamilyQuant_family_composition.csv", Title = "Save the family composition as CSV" };
            if (!ShellDialogs.ShowFile(this, "curation.composition-csv", dialog)) return;
            await RunCurationAsync("Export family composition", async operation =>
            {
                operation.Report("export", "Preparing the full-XIC family composition");
                string csv = FullXicCompositionCsv() ?? throw new InvalidOperationException("Full XIC is unavailable for the current families.");
                await Task.Run(() => AppOperationFiles.WriteFile(dialog.FileName,
                    temporary => File.WriteAllText(temporary, csv, Encoding.UTF8), operation), operation.CancellationToken);
                ShowCurationOperationResult("Family composition exported to " + dialog.FileName
                    + ". The CSV names the denominator and shared-peptide rule; shared and unattributed signal keep their own rows when applicable.");
            });
        }

        private void ComboBoxFullXicBasis_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (fcf == null) return;   // fires once while the XAML selects its default item
            UpdateColumnHeaders();
            UpdateFilteredResults();
        }

        // ===================================================================== layer states

        // ===================================================================== curation locks

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
            return new LockManagerWindow(fcf, () => { ViewModel.NotifyDocumentChanged(); UpdateLockSummary(); }, PushUndoSnapshot);
        }

        // ===================================================================== undo

        /// <summary>
        /// Records the current grouping state so the next mutating operation can be stepped back. Called
        /// BEFORE the operation mutates anything. Bounded to <see cref="UndoLimit"/> steps.
        /// </summary>
        private void PushUndoSnapshot()
        {
            ViewModel.PushUndo(); UpdateUndoButton();
        }

        /// <summary>The same step, also recording the annotations of <paramref name="annotationsOf"/>,
        /// for an operation that is about to rewrite them in bulk. A distinct name, not an overload, so
        /// <see cref="PushUndoSnapshot()"/> stays a single method for the lock manager and the harness.</summary>
        private void PushUndoSnapshotWithAnnotations(IEnumerable<FastaItemExtended> annotationsOf)
        {
            ViewModel.PushUndo(annotationsOf); UpdateUndoButton();
        }

        /// <summary>
        /// Steps back one curation operation: pops the last snapshot and restores the family numbers, the
        /// lock set and the grouping-provenance flags to what they were just before it. It records NO
        /// snapshot of its own (undo alone is the feature, there is no redo), then refreshes everything a
        /// reclustering would - recalculating the quantitation when a project is loaded.
        /// </summary>
        private void ButtonUndo_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.CanUndo) return;
            ViewModel.Undo();
            if (CurrentPLP != null) RecalculateQuantitationInternal();
            UpdateGUI();
        }

        // ===================================================================== batch edit

        /// <summary>
        /// "Apply to selected rows": one User ID and/or one annotation for every selected row, through
        /// <see cref="BatchCuration.Apply"/>. Reported in its own status line rather than a modal box,
        /// so a headless run never blocks; recorded as one undo step and recalculated like an undo.
        /// </summary>
        private void ButtonApplyToSelected_Click(object sender, RoutedEventArgs e)
        {
            var outcome = ViewModel.ApplyBatch(DataGridResultGrid.SelectedItems.OfType<FastaItemExtended>().Distinct().ToList(),
                TextBoxBatchUserId.Text ?? string.Empty, TextBoxBatchAnnotation.Text ?? string.Empty);
            TextBlockBatchStatus.Text = outcome.Status;
            if (outcome.Changed && CurrentPLP != null) RecalculateQuantitationInternal();
            UpdateGUI();
        }

        private void ButtonManageLocks_Click(object sender, RoutedEventArgs e)
        {
            CreateLockManager().ShowFor(Window.GetWindow(this));
        }

        // ===================================================================== headers

        public void SaveFamilyClusterFile(string fileName)
        {
            fcf.Save(fileName);
            ShowCurationOperationResult(fcf.LastSaveCheck?.Message ?? "Saved.");
        }

        private void TextBoxSearch_KeyUp(object sender, KeyEventArgs e)
        {
            UpdateFilteredResults();
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

        private async void ButtonAlignSelected_Click(object sender, RoutedEventArgs e) => await AlignSelectedAsync();

        public Task AlignSelectedAsync() => AppOperationHost.OnUi(this, AlignSelectedAsyncCore);

        private async Task AlignSelectedAsyncCore()
        {
            var selected = DataGridResultGrid.SelectedItems.OfType<FastaItemExtended>().ToList();
            if (selected.Count < 2) { ShowCurationOperationResult("Select at least two proteins before aligning their sequences."); return; }
            string? alignment = null;
            bool completed = await RunCurationAsync("Align selected proteins", async operation =>
            {
                operation.Report("alignment", "Aligning the selected protein sequences");
                alignment = await ViewModel.AlignAsync(selected, operation);
                operation.ThrowIfCancellationRequested();
            });
            if (completed && alignment != null)
                TextReportWindow.Show("Multiple sequence alignment", alignment, Window.GetWindow(this));
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
        private async void ButtonFetchDomains_Click(object sender, RoutedEventArgs e) => await FetchProteinEvidenceAsync();

        public Task FetchProteinEvidenceAsync() => AppOperationHost.OnUi(this, FetchProteinEvidenceAsyncCore);

        private async Task FetchProteinEvidenceAsyncCore()
        {
            if (evidenceFetchRunning) return;
            evidenceFetchRunning = true;
            try
            {
                await RunCurationAsync("Fetch evidence", async operation =>
                {
                    var outcome = await ViewModel.FetchEvidenceAsync(FetchDomainsHandlerOverride, operation);
                    UpdateFilteredResults();
                    string title = "Fetch evidence";
                    ShellMessages.Report(this, CurationViewModel.DescribeEvidenceFetch(outcome), title,
                        image: outcome.PanelFailure == null ? MessageBoxImage.Information : MessageBoxImage.Warning,
                        status: SetCurationOperationStatus, showDialog: false);
                });
            }
            finally { evidenceFetchRunning = false; }
        }

        private async void ButtonSuperCluster_Click(object sender, RoutedEventArgs e)
        {
            // The wait overlay stops the mouse, not Space or Enter on the focused button, and a second run
            // would stack a second preview whose merge could undo the first one's ticks.
            if (superClusterRunning) return;
            superClusterRunning = true;
            try { await RunSuperCluster(); }
            catch (Exception ex) { ShowCurationOperationResult("Keyword grouping could not finish: " + ex.Message); }
            finally { superClusterRunning = false; }
        }

        private bool superClusterRunning;

        private async Task RunSuperCluster()
        {
            SuperClusterPreview? preview = null;
            bool prepared = await RunCurationAsync("Prepare keyword grouping review", async operation =>
            {
                ViewModel.SearchText = TextBoxSearch.Text;
                var ignored = ReclusterExcludeWords();
                operation.Report("review", "Reading descriptions for the grouping review");
                preview = await Task.Run(() => ViewModel.PrepareKeywordReview(ignored), operation.CancellationToken);
                operation.ThrowIfCancellationRequested();
            });
            if (prepared && preview != null && ConfirmSuperCluster(preview)) PerformClustering(preview);
        }

        /// <summary>Seam for the suite only, like <see cref="DeNovoControl.ImportOverride"/>: stands in for the
        /// modal window. The application never sets it.</summary>
        public Func<SuperClusterPreview, bool>? ConfirmSuperClusterOverride { get; set; }

        private bool ConfirmSuperCluster(SuperClusterPreview preview)
        {
            if (ConfirmSuperClusterOverride != null) return ConfirmSuperClusterOverride(preview);

            return OpenGroupingReview(preview, false);
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

        private async Task PerformClustering(double threshold, List<string> wordsToExclude)
        {
            await RunCurationAsync("Group by description words", async operation =>
            {
                ViewModel.SearchText = TextBoxSearch.Text;
                operation.Report("grouping", "Reading the description words");
                string message = await ViewModel.ReclusterByWordsAsync(threshold, wordsToExclude,
                    operation.CancellationToken, operation.Commit);
                operation.Report("grouping", "Refreshing the family shares");
                AfterRegrouping(); ShowCurationOperationResult(message);
            });
        }

        /// <summary>Carries out what the preview showed: the listed proteins move, and nothing is matched again,
        /// so the merge is the one that was on screen. The window said everything a message could say
        /// afterwards, so there is none.</summary>
        private void PerformClustering(SuperClusterPreview preview)
        {
            BeginWait("Apply keyword grouping");
            try
            {
                CurationOperation.Commit(() => { ViewModel.ApplyKeywordReview(preview); AfterRegrouping(); });
                ShowCurationOperationResult("Keyword grouping applied. Undo restores the previous families.");
            }
            finally { EndWait(); }
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
                    ShowCurationMessage(
                        "The families were regrouped, but their percentages could not be recalculated, so " +
                        "the share column still holds the numbers from the previous grouping and must " +
                        "not be read.\n\n" +
                        "Reason given by the program: " + ex.Message + "\n\n" +
                        "Next step: press Recalculate in Quantitation options. If it fails again, reload the project in " +
                        "PatternLab source.",
                        "Regrouped, but not recalculated", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            UpdateGUI();
        }
    }
}
