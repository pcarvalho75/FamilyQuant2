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

namespace FamilyQuant
{
    /// <summary>One acquisition run of the selected de novo export, as the breakdown reports it.</summary>
    public class DeNovoRunTally
    {
        public string Run { get; set; } = string.Empty;
        public int RowsRead { get; set; }
        public int RowsKept { get; set; }
    }

    /// <summary>
    /// The result of one import pass: what was found, what was read, what survived the criteria.
    ///
    /// A value object, produced off the UI thread and applied on it. The screen holds no half-built
    /// state: an import that throws leaves the previous one on screen untouched.
    /// </summary>
    public class DeNovoImport
    {
        public string Directory { get; set; } = string.Empty;
        public string SourceId { get; set; } = DeNovoSource.DenovoOnly.Id;
        public string SourceDisplayName { get; set; } = DeNovoSource.DenovoOnly.DisplayName;
        public string ConfidenceField { get; set; } = DeNovoSource.DenovoOnly.ConfidenceColumn;
        public int FilesFound { get; set; }
        public int RowsRead { get; set; }

        /// <summary>The rows that cleared every criterion, in file order.</summary>
        public List<PeaksDeNovoRow> Kept { get; set; } = new List<PeaksDeNovoRow>();

        /// <summary>One entry per run, ordered by run name so the same export always reports the same
        /// way. Runs are named by file name, never by position: see <see cref="DeNovoReconciler"/>.</summary>
        public List<DeNovoRunTally> PerRun { get; set; } = new List<DeNovoRunTally>();

        public int RowsKept => Kept.Count;
    }

    /// <summary>The result of one reconciliation pass, likewise built off the UI thread.</summary>
    public class DeNovoReconciliation
    {
        public string Sepr2Path { get; set; } = string.Empty;
        public List<PatternLabPsm> Psms { get; set; } = new List<PatternLabPsm>();
        public List<ReconciledRow> Rows { get; set; } = new List<ReconciledRow>();
        public Dictionary<ReconciliationVerdict, int> Counts { get; set; }
            = new Dictionary<ReconciliationVerdict, int>();
        public List<ReviewQueueRow> ReviewQueue { get; set; } = new List<ReviewQueueRow>();
        public DeNovoOverlapSummary? Overlap { get; set; }
    }

    /// <summary>
    /// One kept acquisition run and the .raw that was found for it, or was not.
    ///
    /// Matched BY FILE NAME through <see cref="DeNovoReconciler.NormalizeRunName"/>, never by position
    /// in a folder listing: a de novo export and a directory need not use the same order, and extracting a
    /// precursor against another run's acquisition returns a plausible wrong area.
    /// </summary>
    public class DeNovoRunRawMatch
    {
        public string Run { get; set; } = string.Empty;

        /// <summary>What was looked for, so a missing acquisition can be named rather than merely
        /// counted.</summary>
        public string ExpectedFileName { get; set; } = string.Empty;

        /// <summary>The acquisition found for this run, or null when none was.</summary>
        public string? RawPath { get; set; }

        public bool Found => RawPath != null;
    }

    /// <summary>What one extraction pass produced, built off the UI thread and applied on it.</summary>
    public class DeNovoXicExtraction
    {
        public string RawDirectory { get; set; } = string.Empty;

        /// <summary>One entry per extracted run, in run-name order.</summary>
        public List<DeNovoBatchStats> Stats { get; set; } = new List<DeNovoBatchStats>();

        /// <summary>The same entries keyed by normalized run name, which is how Layer 5 joins them.</summary>
        public Dictionary<string, DeNovoBatchStats> ByRun { get; set; }
            = new Dictionary<string, DeNovoBatchStats>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sum of M+0 over the DISTINCT PEAKS of every run. The de novo numerator.</summary>
        public double TotalAreaM0 => Stats.Sum(s => s.TotalAreaM0);

        public double ElapsedSeconds => Stats.Sum(s => s.ElapsedSeconds);
    }

    /// <summary>What one homology pass produced. Counts partition the searched set: mapped, decoy
    /// only, unmatched and rejected are exclusive and exhaustive.</summary>
    public class DeNovoHomology
    {
        public string FcfPath { get; set; } = string.Empty;
        public int PeptidesSearched { get; set; }
        public int MappedToTargets { get; set; }
        public int DecoyOnly { get; set; }
        public int Unmatched { get; set; }
        public int Rejected { get; set; }
        public double ElapsedSeconds { get; set; }

        public List<HomologyResult> Results { get; set; } = new List<HomologyResult>();

        /// <summary>One per result, positionally joinable with <see cref="Results"/>.</summary>
        public List<FamilyAssignment> Assignments { get; set; } = new List<FamilyAssignment>();

        /// <summary>Target families only, most peptides first.</summary>
        public List<DeNovoFamilyRow> Families { get; set; } = new List<DeNovoFamilyRow>();
    }

    /// <summary>What one partition pass produced, including the case where no source could answer per
    /// run: the refusal is a first-class result and is shown word for word.</summary>
    public class DeNovoPartition
    {
        /// <summary>Which source supplied the PatternLab segment, in <see cref="PatternLabRunQuants"/>'
        /// own words, refusals included. Displayed verbatim.</summary>
        public string BranchNote { get; set; } = string.Empty;

        /// <summary>The per-run rows, or empty when the PatternLab side refused.</summary>
        public List<ThreeWayPartitionRow> Rows { get; set; } = new List<ThreeWayPartitionRow>();

        /// <summary>The aggregate over <see cref="Rows"/>, or null when there are none.</summary>
        public ThreeWayPartitionRow? Grouped { get; set; }

        public bool IsAvailable => Rows.Count > 0;
    }

    /// <summary>
    /// The de novo evidence screen: import one selected source, keep the confident sequences, and ask which
    /// of them PatternLab had already identified.
    ///
    /// It is deliberately separate from the family workspace. Nothing here reads or writes a Family
    /// Cluster File, and nothing here changes a family: this screen answers what is NEW in a run, which
    /// is a different question from what a family's share of the signal is.
    ///
    /// TWO RULES IT INHERITS FROM THE ENGINE, both measured before being written and both invisible on
    /// screen if they are got wrong. Runs are matched by FILE NAME, never by position, because the runs
    /// of a de novo export and of a PatternLab project are not in the same order. And the join key is scan
    /// number AND precursor m/z, never scan alone, because most scan collisions on real data are
    /// co-isolated species rather than duplicates.
    ///
    /// Nothing on this screen interrupts with a dialog for something the status line can say. A refused
    /// parameter, an empty folder and an unreadable package are all reported there, and the previous
    /// state is left alone.
    /// </summary>
    public partial class DeNovoControl : UserControl
    {
        /// <summary>The criteria IN FORCE, which are not what the boxes currently show: they differ
        /// between typing a value and pressing "Load and filter". A refused box never reaches this
        /// object, so what was imported was always imported with what this holds.</summary>
        public DeNovoFilterCriteria Criteria { get; private set; } = new DeNovoFilterCriteria();

        /// <summary>The last import, or null when nothing has been imported.</summary>
        public DeNovoImport? LastImport { get; private set; }

        /// <summary>The last reconciliation, or null when none has been run.</summary>
        public DeNovoReconciliation? LastReconciliation { get; private set; }

        /// <summary>The rows that cleared the criteria. Everything downstream works from these.</summary>
        public IReadOnlyList<PeaksDeNovoRow> FilteredRows =>
            LastImport?.Kept ?? (IReadOnlyList<PeaksDeNovoRow>)Array.Empty<PeaksDeNovoRow>();

        /// <summary>The PatternLab side of the last join.</summary>
        public IReadOnlyList<PatternLabPsm> Psms =>
            LastReconciliation?.Psms ?? (IReadOnlyList<PatternLabPsm>)Array.Empty<PatternLabPsm>();

        /// <summary>Every kept row with the verdict the join gave it.</summary>
        public IReadOnlyList<ReconciledRow> Reconciled =>
            LastReconciliation?.Rows ?? (IReadOnlyList<ReconciledRow>)Array.Empty<ReconciledRow>();

        /// <summary>Exactly the rows of the table, i.e. exactly what the queue export writes.</summary>
        public IReadOnlyList<ReviewQueueRow> ReviewQueue =>
            LastReconciliation?.ReviewQueue ?? (IReadOnlyList<ReviewQueueRow>)Array.Empty<ReviewQueueRow>();

        /// <summary>The homology settings IN FORCE, which are not what the boxes currently show, for
        /// the same reason as <see cref="Criteria"/>.</summary>
        public HomologyOptions HomologySettings { get; private set; } = new HomologyOptions();

        /// <summary>Which kept run found which acquisition, recomputed whenever either side changes.</summary>
        public IReadOnlyList<DeNovoRunRawMatch> RawMatches { get; private set; } =
            Array.Empty<DeNovoRunRawMatch>();

        /// <summary>The last extraction, or null when none has been run.</summary>
        public DeNovoXicExtraction? LastExtraction { get; private set; }

        /// <summary>The last homology pass, or null when none has been run.</summary>
        public DeNovoHomology? LastHomology { get; private set; }

        /// <summary>The last partition, or null when none has been built.</summary>
        public DeNovoPartition? LastPartition { get; private set; }

        public DeNovoControl()
        {
            InitializeComponent();

            ApplyReviewColumnTooltips();
            ApplyXicColumnTooltips();
            ApplyHomologyColumnTooltips();
            ApplyPartitionColumnTooltips(null);
            UpdateSourceUi(clearStaleImport: false);
            WriteCriteriaToBoxes();
            WriteHomologySettingsToBoxes();

            // The two Layer 5 paths are only paths until a file is named in them, so the button that
            // consumes them has to be re-evaluated as they are typed, not only when a pass finishes.
            TextBoxPartitionPlp.TextChanged += (_, __) => UpdateLayerStates();
            TextBoxPartitionXic2.TextChanged += (_, __) => UpdateLayerStates();

            UpdateLayerStates();

            SetStatus(
                "Step 1 of 5 · Nothing imported yet. Choose a de novo source and its export folder, " +
                "check the three confidence filters, and press \"Load and filter\". Layers 2 to 5 stay " +
                "blocked until a filter pass has kept at least one row, because there would be nothing " +
                "to reconcile, extract, search or partition.");
        }

        // ===================================================================== status

        private void SetStatus(string message)
        {
            if (TextBlockDeNovoStatus == null) return;

            TextBlockDeNovoStatus.Text = message;
        }

        /// <summary>
        /// The badges. Layer 1 can always be used; Layer 2 is blocked until a filter pass has produced
        /// rows, and says so rather than offering a button that would do nothing.
        /// </summary>
        private void UpdateLayerStates()
        {
            if (TextBlockDeNovoLayer1State == null) return;

            SetBadge(BadgeDeNovoLayer1, TextBlockDeNovoLayer1State, true, null,
                "Set the source, folder and three filters here, and re-apply them as often as you like.");

            bool haveRows = FilteredRows.Count > 0;

            SetBadge(BadgeDeNovoLayer2, TextBlockDeNovoLayer2State, haveRows, haveRows ? null : "no rows imported",
                haveRows
                    ? "Rows have been kept in Step 1 and can be joined to a SEPro search result."
                    : "Nothing to reconcile yet. Import a de novo export in Step 1 first.");

            if (ButtonReconcile != null) ButtonReconcile.IsEnabled = haveRows;

            bool haveVerdicts = LastReconciliation != null;
            if (ButtonExportSummary != null) ButtonExportSummary.IsEnabled = haveVerdicts;
            if (ButtonExportReviewQueue != null) ButtonExportReviewQueue.IsEnabled = haveVerdicts;
            if (ButtonExportOverlapStudy != null) ButtonExportOverlapStudy.IsEnabled = haveVerdicts;

            // ---- Layer 3 -------------------------------------------------------------------------
            // Three separate states, because "you cannot press this yet" and "you cannot press this
            // because a file is missing" are different sentences and only one of them is your fault.
            int keptRuns = KeptRunNames().Count;
            int matched = RawMatches.Count(m => m.Found);
            bool everyRunMatched = keptRuns > 0 && matched == keptRuns;

            string? layer3Blocked = !haveVerdicts
                ? "no reconciliation yet"
                : (keptRuns == 0 ? "no rows survived the reconciliation" : null);

            SetBadge(BadgeDeNovoLayer3, TextBlockDeNovoLayer3State,
                haveVerdicts && everyRunMatched, layer3Blocked,
                !haveVerdicts
                    ? "Nothing to extract yet. Reconcile in Step 2 first, which decides the rows to extract."
                    : everyRunMatched
                        ? "Every kept run found its RAW file by name and can be extracted."
                        : "Waiting for the folder holding the .raw files. Extract stays disabled until " +
                          "every kept run has found one.");

            if (ButtonExtractXic != null) ButtonExtractXic.IsEnabled = haveVerdicts && everyRunMatched;

            // ---- Layer 4 -------------------------------------------------------------------------
            // Deliberately NOT gated on Layer 3: homology reads peptides, not chromatograms.
            SetBadge(BadgeDeNovoLayer4, TextBlockDeNovoLayer4State, haveRows, haveRows ? null : "no rows imported",
                haveRows
                    ? "Rows were kept in Step 1, so there are peptides to search. Extracted areas are " +
                      "not needed."
                    : "Nothing to search yet. Import a de novo export in Step 1 first.");

            if (ButtonSearchHomology != null) ButtonSearchHomology.IsEnabled = haveRows;
            if (ButtonExportHomologyCsv != null) ButtonExportHomologyCsv.IsEnabled = LastHomology != null;

            // ---- Layer 5 -------------------------------------------------------------------------
            bool everyRunExtracted = LastExtraction != null && keptRuns > 0
                && KeptRunNames().All(r => LastExtraction.ByRun.ContainsKey(r));
            bool havePatternLabSide = PathTyped(TextBoxPartitionPlp) || PathTyped(TextBoxPartitionXic2);

            string? layer5Blocked = !everyRunExtracted ? "no extraction yet" : null;

            SetBadge(BadgeDeNovoLayer5, TextBlockDeNovoLayer5State,
                everyRunExtracted && havePatternLabSide, layer5Blocked,
                !everyRunExtracted
                    ? "Nothing to partition yet. Extract every kept run first, under Extract areas."
                    : havePatternLabSide
                        ? "Every kept run has an area and a PatternLab source is named, so the " +
                          "partition can be built."
                        : "Waiting for a PatternLab project, which supplies the first segment of " +
                          "each bar.");

            if (ButtonBuildPartition != null)
                ButtonBuildPartition.IsEnabled = everyRunExtracted && havePatternLabSide;

            bool havePartition = LastPartition != null && LastPartition.IsAvailable;
            if (ButtonSavePartitionCsv != null) ButtonSavePartitionCsv.IsEnabled = havePartition;
            if (ButtonOpenPartitionWindow != null) ButtonOpenPartitionWindow.IsEnabled = havePartition;
        }

        private static bool PathTyped(TextBox? box) =>
            box != null && box.Text != null && box.Text.Trim().Length > 0;

        private static void SetBadge(Border badge, TextBlock text, bool ready, string? blockedBecause,
                                     string tooltip)
        {
            if (badge == null || text == null) return;

            text.Text = ReportView.LayerState(ready, blockedBecause);

            badge.Background = new SolidColorBrush(ready
                ? Color.FromRgb(0x2F, 0x85, 0x5A)                                    // green: usable now
                : (blockedBecause == null ? Color.FromRgb(0xB7, 0x79, 0x1F)          // amber: waiting on you
                                          : Color.FromRgb(0x9B, 0x2C, 0x2C)));       // red: blocked, with a reason

            badge.ToolTip = tooltip;
        }

        // ===================================================================== the criteria

        private void WriteCriteriaToBoxes()
        {
            // InvariantCulture, and deliberately: what is written here has to parse again through the
            // same reader, and the reader is invariant because the criteria are also carried in files.
            TextBoxMinLength.Text = Criteria.MinLength.ToString(CultureInfo.InvariantCulture);
            TextBoxMinAlc.Text = Criteria.MinAverageLocalConfidence.ToString(CultureInfo.InvariantCulture);
            TextBoxStretchLength.Text = Criteria.StretchLength.ToString(CultureInfo.InvariantCulture);
            TextBoxStretchFloor.Text = Criteria.StretchFloor.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads the four boxes into the criteria in force, all four or none.
        ///
        /// A refused value keeps the previous one, puts it back in the box so the screen never shows a
        /// number that is not in force, and says which box was refused in the status line. There is no
        /// dialog: the same argument as the User ID editor, where a modal box for a typing mistake is
        /// both an interruption and, in a harness with no screen, a hang.
        /// </summary>
        /// <returns>True when all four were accepted and are now in force.</returns>
        public bool ReadCriteriaFromBoxes()
        {
            if (!TryReadInt(TextBoxMinLength, "Minimum length", 1, 100, WriteCriteriaToBoxes, out int minLength)) return false;
            if (!TryReadDouble(TextBoxMinAlc, "Minimum " + ActiveDeNovoSource.ConfidenceColumn, 0, 100,
                               WriteCriteriaToBoxes, out double minAlc)) return false;
            if (!TryReadInt(TextBoxStretchLength, "Stretch length", 1, 100, WriteCriteriaToBoxes, out int stretchLength)) return false;
            if (!TryReadDouble(TextBoxStretchFloor, "Stretch floor", 0, 100, WriteCriteriaToBoxes, out double stretchFloor)) return false;

            Criteria = new DeNovoFilterCriteria
            {
                MinLength = minLength,
                MinAverageLocalConfidence = minAlc,
                StretchLength = stretchLength,
                // PEAKS reports per-residue confidence as whole numbers, so the floor is one too;
                // rounding here rather than truncating keeps 89.6 from silently becoming 89.
                StretchFloor = (int)Math.Round(stretchFloor, MidpointRounding.AwayFromZero),
            };

            WriteCriteriaToBoxes();
            return true;
        }

        /// <summary><paramref name="putBack"/> restores the boxes of the SAME group to what is in
        /// force, so a refusal in one Layer never rewrites another Layer's settings.</summary>
        private bool TryReadInt(TextBox box, string name, int low, int high, Action putBack, out int value)
        {
            if (int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                && value >= low && value <= high)
            {
                return true;
            }

            Refuse(name, box.Text,
                "a whole number between " + low.ToString(CultureInfo.CurrentCulture) + " and " +
                high.ToString(CultureInfo.CurrentCulture), putBack);
            value = 0;
            return false;
        }

        private bool TryReadDouble(TextBox box, string name, double low, double high, Action putBack, out double value)
        {
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && value >= low && value <= high)
            {
                return true;
            }

            Refuse(name, box.Text,
                "a number between " + low.ToString(CultureInfo.CurrentCulture) + " and " +
                high.ToString(CultureInfo.CurrentCulture) + ", written with a dot for the decimal separator",
                putBack);
            value = 0;
            return false;
        }

        private void Refuse(string name, string typed, string expected, Action putBack)
        {
            // Every box goes back to what is actually in force, not just the offending one: the pass is
            // all or none, so leaving edited values on screen would misdescribe the settings.
            putBack();

            SetStatus(
                "Nothing was read: \"" + typed.Trim() + "\" is not " + expected + " for " + name +
                ", so the previous settings are still in force and the boxes have been put back to them. " +
                "Next step: correct that box and press the button again.");
        }

        /// <summary>The criteria in force, in one sentence, for the status line.</summary>
        private string CriteriaSentence()
        {
            return "length " + Criteria.MinLength.ToString("N0", CultureInfo.CurrentCulture) +
                   " or more, " + ActiveDeNovoSource.ConfidenceColumn + " " +
                   Criteria.MinAverageLocalConfidence.ToString("N0", CultureInfo.CurrentCulture) +
                   "% or more, and " + Criteria.StretchLength.ToString("N0", CultureInfo.CurrentCulture) +
                   " consecutive residues at " + Criteria.StretchFloor.ToString("N0", CultureInfo.CurrentCulture) +
                   "% or better";
        }

        // ===================================================================== layer 1

        /// <summary>
        /// Whether a modal dialog may be raised at all: this screen has to be on a display.
        ///
        /// A Win32 file dialog opened off a display is a window nothing can close and a run nothing
        /// can finish, which in a harness is a hang rather than an error. Every path on this screen
        /// that would raise one asks here first and says in the status line why it did not, exactly as
        /// the detached chart window and the help window already do.
        /// </summary>
        private bool CanShowDialog(string refusal, string nextStep)
        {
            if (PresentationSource.FromVisual(this) != null) return true;

            SetStatus(refusal + ", because this screen is not on a display and a file dialog there is " +
                      "a window nothing can close. Next step: " + nextStep);
            return false;
        }

        private void ButtonBrowsePeaksDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No folder was chosen",
                               "type the folder into the box beside the button instead.")) return;

            var ofd = new OpenFolderDialog
            {
                Title = "Choose the folder holding the selected de novo export"
            };

            if (ofd.ShowDialog() != true) return;

            TextBoxPeaksDirectory.Text = ofd.FolderName;

            // Count straight away rather than at import: the commonest mistake is picking a folder one
            // level too high or too low, and a count of zero says so before anything is read.
            DeNovoSource source = ActiveDeNovoSource;
            int files = CountExports(ofd.FolderName, source);
            LabelFilesFound.Content = "Files found: " + files.ToString("N0", CultureInfo.CurrentCulture);

            SetStatus(files == 0
                ? "That folder holds no " + source.DisplayName + " export: nothing under it is named " +
                  source.FileGlob + ". " +
                  "Next step: choose the export root holding the per-run sub-folders and check that the " +
                  "selected source was exported."
                : files.ToString("N0", CultureInfo.CurrentCulture) + " de novo export file(s) found under " +
                  "that folder. Next step: check the three filters and press \"Load and filter\".");
        }

        /// <summary>The single source selected in Step 1. The default is the original PEAKS
        /// de-novo-only behavior. Multi-source unions are handled by the full-XIC workflow, where one XIC
        /// can preserve alternate evidence explicitly.</summary>
        public DeNovoSource ActiveDeNovoSource
        {
            get
            {
                string id = (ComboBoxDeNovoSource?.SelectedItem as ComboBoxItem)?.Tag as string
                    ?? DeNovoSource.DenovoOnly.Id;
                return DeNovoSource.FromId(id) ?? DeNovoSource.DenovoOnly;
            }
        }

        private void ComboBoxDeNovoSource_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateSourceUi(clearStaleImport: true);

        private void UpdateSourceUi(bool clearStaleImport)
        {
            if (LabelMinimumSourceConfidence == null || TextBlockNativeConfidence == null) return;
            DeNovoSource source = ActiveDeNovoSource;
            string shortField = source.ConfidenceColumn.Replace(" (%)", string.Empty,
                StringComparison.OrdinalIgnoreCase);
            LabelMinimumSourceConfidence.Content = "Minimum " + shortField + " (%):";
            LabelMinimumSourceConfidence.ToolTip = "Lowest " + source.ConfidenceColumn +
                " kept for " + source.DisplayName + ". This score belongs to this program and is " +
                "not converted to any other program's scale.";
            TextBoxMinAlc.ToolTip = "A number from 0 to 100, decimals allowed. Spectra with a lower " +
                source.ConfidenceColumn + " are dropped. " +
                (source == DeNovoSource.DeepNovo
                    ? "DeepNovo SCORE is not PEAKS ALC."
                    : "PEAKS ALC is not a calibrated FDR.");
            TextBlockNativeConfidence.Text = "Native confidence: " + source.ConfidenceColumn;
            ButtonLoadAndFilter.ToolTip = "Reads every " + source.FileGlob + " file in the folder and " +
                "keeps the rows that pass the length, " + source.ConfidenceColumn + " and stretch filters.";

            if (ColumnQueueAlc != null) ColumnQueueAlc.Header = source.ConfidenceColumn;
            ApplyReviewColumnTooltips();

            string directory = TextBoxPeaksDirectory?.Text?.Trim() ?? string.Empty;
            if (LabelFilesFound != null && directory.Length > 0 && Directory.Exists(directory))
                LabelFilesFound.Content = "Files found: " + CountExports(directory, source)
                    .ToString("N0", CultureInfo.CurrentCulture);

            if (!clearStaleImport || LastImport == null
                || string.Equals(LastImport.SourceId, source.Id, StringComparison.OrdinalIgnoreCase)) return;

            LastImport = null;
            ClearReconciliation();
            LabelRowsRead.Content = "Rows read: 0";
            LabelRowsKept.Content = "Rows kept: 0";
            TextBlockPerRunBreakdown.Text = "No import yet. The breakdown per run appears here.";
            UpdateLayerStates();
            SetStatus("The source changed to " + source.DisplayName + " (" + source.ConfidenceColumn +
                "). The previous source's rows and every downstream result were cleared. Next step: " +
                "press Load and filter.");
        }

        /// <summary>How many de novo exports there are under a folder, sub-folders included; 0 when the
        /// folder cannot be read, which the caller reports as an empty folder.</summary>
        private static int CountExports(string directory, DeNovoSource? source = null)
        {
            source ??= DeNovoSource.DenovoOnly;
            try
            {
                return Directory.GetFiles(directory, source.FileGlob, SearchOption.AllDirectories).Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Reads every de novo export under a folder and keeps the rows that clear the criteria.
        ///
        /// Static and free of the interface on purpose: this is what runs on the background thread, and
        /// it is what the harness drives. Nothing in here touches a control.
        /// </summary>
        public static DeNovoImport ImportAndFilter(string directory, DeNovoFilterCriteria criteria,
                                                   DeNovoSource? source = null)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            source ??= DeNovoSource.DenovoOnly;

            var import = new DeNovoImport
            {
                Directory = directory,
                SourceId = source.Id,
                SourceDisplayName = source.DisplayName,
                ConfidenceField = source.ConfidenceColumn,
            };

            string[] files = Directory.GetFiles(directory, source.FileGlob, SearchOption.AllDirectories);
            import.FilesFound = files.Length;

            var byRun = new Dictionary<string, DeNovoRunTally>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                var rows = PeaksCsvReader.Read(file, source);
                var kept = DeNovoFilter.Apply(rows, criteria);

                import.RowsRead += rows.Count;
                import.Kept.AddRange(kept);

                // Keyed by the run the READER stamped on each row, so the breakdown is keyed the same
                // way the reconciliation joins. A file holding rows of more than one run is counted
                // under each of them rather than under its own name.
                foreach (var group in rows.GroupBy(r => r.RunName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!byRun.TryGetValue(group.Key, out var tally))
                        byRun[group.Key] = tally = new DeNovoRunTally { Run = group.Key };

                    tally.RowsRead += group.Count();
                }

                foreach (var group in kept.GroupBy(r => r.RunName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!byRun.TryGetValue(group.Key, out var tally))
                        byRun[group.Key] = tally = new DeNovoRunTally { Run = group.Key };

                    tally.RowsKept += group.Count();
                }
            }

            import.PerRun = byRun.Values.OrderBy(t => t.Run, StringComparer.Ordinal).ToList();

            return import;
        }

        /// <summary>
        /// What "Load and filter" runs off the UI thread. Null in the application, where it is
        /// <see cref="ImportAndFilter"/> on a <see cref="Task.Run(Action)"/>.
        ///
        /// A SEAM FOR THE HARNESS, and for nothing else, like <see cref="CachePathForRunOverride"/>.
        /// The suite hands back a task it completes by hand, which is the only way to observe the
        /// screen WHILE a long operation is in flight: sleeping would observe the same screen slowly,
        /// and observing it after the operation proves nothing about what was reachable during it.
        /// </summary>
        public static Func<string, DeNovoFilterCriteria, Task<DeNovoImport>>? ImportOverride;

        private async void ButtonLoadAndFilter_Click(object sender, RoutedEventArgs e)
        {
            // The criteria in force before this pass. A pass that imports nothing puts them back:
            // see RestoreCriteria.
            DeNovoFilterCriteria previousCriteria = Criteria;

            if (!ReadCriteriaFromBoxes()) return;   // Refuse() has already said which box, and why

            string directory = TextBoxPeaksDirectory.Text.Trim();
            DeNovoSource source = ActiveDeNovoSource;

            if (directory.Length == 0 || !Directory.Exists(directory))
            {
                SetStatus(
                    "Nothing was imported: " + (directory.Length == 0
                        ? "no folder has been chosen yet."
                        : "the folder \"" + directory + "\" does not exist or cannot be reached.") +
                    RestoreCriteria(previousCriteria) +
                    " Next step: press Browse and pick the folder holding the selected de novo export.");
                return;
            }

            // Every Layer, not only the two this operation writes: see SetScreenEnabled.
            SetScreenEnabled(false);

            SetStatus("Reading the de novo exports (source: " + source.DisplayName + ") under " + directory + " and applying " +
                      CriteriaSentence() + ". A real export is a few hundred thousand rows, so this " +
                      "takes a moment.");

            try
            {
                var criteria = Criteria;
                Func<string, DeNovoFilterCriteria, Task<DeNovoImport>>? seam = ImportOverride;

                // A real export is about 180,000 rows across three files. On the UI thread that freezes
                // the window; the whole screen is disabled meanwhile so nothing can be pressed twice.
                DeNovoImport import = seam != null
                    ? await seam(directory, criteria)
                    : await Task.Run(() => ImportAndFilter(directory, criteria, source));

                // The test seam predates source selection. Stamp its value object with the source the UI
                // actually showed so ApplyImport/status never mislabels the rows.
                import.SourceId = source.Id;
                import.SourceDisplayName = source.DisplayName;
                import.ConfidenceField = source.ConfidenceColumn;

                // Explicitly back onto the UI thread: everything below writes controls.
                Dispatcher.Invoke(() => ApplyImport(import));
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The export could not be read, so nothing was imported and the screen still shows " +
                    "whatever it showed before. Reason given by the program: " + ex.Message +
                    RestoreCriteria(previousCriteria) +
                    " Next step: check that the folder really holds the selected export and that its files are " +
                    "not open in another program, then press \"Load and filter\" again.");
            }
            finally
            {
                // Re-enable the containers and let the state machine decide what is actually allowed:
                // an enabled group box does not enable a button whose Layer has nothing to work on.
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }

        /// <summary>
        /// Puts back the criteria that were in force before a pass that imported nothing, and says so.
        ///
        /// A missing folder or an unreadable export leaves the rows on screen exactly as they were,
        /// and those rows were kept by the PREVIOUS criteria. Leaving the newly typed ones in force
        /// would make the boxes, the status line and the help page all describe a filter that produced
        /// none of what is on display.
        /// </summary>
        /// <returns>A sentence for the status line, empty when the criteria did not change.</returns>
        private string RestoreCriteria(DeNovoFilterCriteria previous)
        {
            if (previous == null || SameCriteria(previous, Criteria)) return string.Empty;

            Criteria = previous;
            WriteCriteriaToBoxes();

            return " The criteria have been put back to " + CriteriaSentence() +
                   ", which is what the rows now on screen were kept by.";
        }

        private static bool SameCriteria(DeNovoFilterCriteria a, DeNovoFilterCriteria b) =>
            a.MinLength == b.MinLength
            && a.MinAverageLocalConfidence.Equals(b.MinAverageLocalConfidence)
            && a.StretchLength == b.StretchLength
            && a.StretchFloor == b.StretchFloor;

        /// <summary>
        /// Puts an import on screen and makes it the state everything downstream reads.
        ///
        /// It also DROPS any reconciliation: that reconciliation was about a different set of rows, and
        /// a verdict table left standing beside a fresh import would be describing rows that are no
        /// longer there.
        /// </summary>
        public void ApplyImport(DeNovoImport import)
        {
            if (import == null) throw new ArgumentNullException(nameof(import));

            LastImport = import;
            ClearReconciliation();

            LabelFilesFound.Content = "Files found: " +
                import.FilesFound.ToString("N0", CultureInfo.CurrentCulture);
            LabelRowsRead.Content = "Rows read: " +
                import.RowsRead.ToString("N0", CultureInfo.CurrentCulture);

            double keptShare = import.RowsRead > 0 ? (double)import.RowsKept / import.RowsRead : 0;
            LabelRowsKept.Content = "Rows kept: " +
                import.RowsKept.ToString("N0", CultureInfo.CurrentCulture) +
                " (" + keptShare.ToString("P1", CultureInfo.CurrentCulture) + " of rows read)";

            TextBlockPerRunBreakdown.Text = import.PerRun.Count == 0
                ? "No run could be read from that folder."
                : string.Join("\n", import.PerRun.Select(t =>
                    t.Run + " · " + t.RowsRead.ToString("N0", CultureInfo.CurrentCulture) + " read · " +
                    t.RowsKept.ToString("N0", CultureInfo.CurrentCulture) + " kept"));

            SuggestRawDirectory(import.Directory);
            RefreshRawMatchPreview();
            UpdateLayerStates();

            SetStatus(import.RowsKept == 0
                ? "Showing an import of " + import.RowsRead.ToString("N0", CultureInfo.CurrentCulture) +
                  " de novo row(s) from " + import.FilesFound.ToString("N0", CultureInfo.CurrentCulture) +
                  " file(s), of which NONE cleared " + CriteriaSentence() + ". Layer 2 stays blocked, " +
                  "because there is nothing to reconcile. Next step: loosen a criterion and press " +
                  "\"Load and filter\" again, knowing that a weaker de novo sequence is a weaker claim."
                : "Step 2 of 2 · Showing " + import.RowsKept.ToString("N0", CultureInfo.CurrentCulture) +
                  " kept row(s) of " + import.RowsRead.ToString("N0", CultureInfo.CurrentCulture) +
                  " read, from " + import.FilesFound.ToString("N0", CultureInfo.CurrentCulture) +
                  " file(s) across " + import.PerRun.Count.ToString("N0", CultureInfo.CurrentCulture) +
                  " run(s), at " + CriteriaSentence() + ". Next step: choose the SEPro package of the " +
                  "SAME runs in Layer 2 and press Reconcile.");
        }

        private void ClearReconciliation()
        {
            LastReconciliation = null;

            DataGridReviewQueue.ItemsSource = null;

            LabelVerdictDeNovoOnly.Content = "De novo only: -";
            LabelVerdictCoIsolated.Content = "Different precursor context: -";
            LabelVerdictRedundant.Content = "Redundant equal: -";
            LabelVerdictWeakPsm.Content = "Equal but weak PSM: -";
            LabelVerdictConflict.Content = "Overlapping alternative sequence: -";
            LabelReviewQueueCount.Content = "Review queue: -";
            LabelOverlapScan.Content = "Same file + scan: -";
            LabelOverlapPrecursor.Content = "Comparable precursor (same charge, 20 ppm): -";
            LabelOverlapExact.Content = "Same scan + charge exact sequence, PTMs ignored: -";
            LabelOverlapDivergent.Content = "Same scan + charge alternative sequence: -";

            // Layer 3 and Layer 5 describe the rows the reconciliation kept, so they go with it. A
            // stats table left standing beside a fresh reconciliation would report rows that are no
            // longer the ones on screen. Layer 4 goes with it too: PeptidesForHomology reads the
            // filtered rows when there is no reconciliation and the kept verdicts when there is, so
            // both a re-import and a re-reconciliation change the set that was searched, and its
            // counts carry no record of which set produced them.
            ClearHomology();
            ClearExtraction();
        }

        /// <summary>Drops the extraction and everything computed from it.</summary>
        private void ClearExtraction()
        {
            LastExtraction = null;
            RawMatches = Array.Empty<DeNovoRunRawMatch>();

            if (DataGridXicStats != null) DataGridXicStats.ItemsSource = null;
            if (LabelXicRunsExtracted != null) LabelXicRunsExtracted.Content = "Runs extracted: -";
            if (LabelXicTotalM0 != null) LabelXicTotalM0.Content = "Total M+0: -";
            if (LabelXicElapsed != null) LabelXicElapsed.Content = "Elapsed: -";

            ClearPartition();
        }

        private void ClearPartition()
        {
            LastPartition = null;

            if (DataGridPartition != null) DataGridPartition.ItemsSource = null;
            if (BorderPartitionChart != null) BorderPartitionChart.Child = null;
            if (TextBlockBranchNote != null)
            {
                TextBlockBranchNote.Text =
                    "No partition yet. Which source supplied the PatternLab numbers, in its own words, " +
                    "appears here.";
            }

            ApplyPartitionColumnTooltips(null);
        }

        /// <summary>
        /// Drops the homology pass and everything that reports it.
        ///
        /// Layer 4 searches whatever <see cref="PeptidesForHomology"/> yields at the moment Search is
        /// pressed, and nothing on screen records which set that was. A pass left standing beside a
        /// different set of rows is therefore unreadable rather than merely stale, and the export
        /// button follows <see cref="LastHomology"/> through <see cref="UpdateLayerStates"/>.
        /// </summary>
        private void ClearHomology()
        {
            LastHomology = null;

            if (DataGridHomologyFamilies != null) DataGridHomologyFamilies.ItemsSource = null;

            if (LabelHomologyPeptides != null) LabelHomologyPeptides.Content = "Peptides searched: -";
            if (LabelHomologyMapped != null) LabelHomologyMapped.Content = "Mapped to targets: -";
            if (LabelHomologyDecoyOnly != null) LabelHomologyDecoyOnly.Content = "Decoy-only: -";
            if (LabelHomologyUnmatched != null) LabelHomologyUnmatched.Content = "Unmatched: -";
            if (LabelHomologyElapsed != null) LabelHomologyElapsed.Content = "Elapsed: -";
        }

        // ===================================================================== layer 2

        private void ButtonBrowseSeproPackage_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No SEPro package was chosen",
                               "type the path of the .sepr2 into the box beside the button instead.")) return;

            var ofd = new OpenFileDialog
            {
                Filter = "SEPro 2.0 (*.sepr2)|*.sepr2",
                Title = "Choose a TRUSTED SEPro package (legacy unsafe deserialization)"
            };

            if (ofd.ShowDialog() != true) return;

            TextBoxSeproPackage.Text = ofd.FileName;

            SetStatus("Package chosen: " + Path.GetFileName(ofd.FileName) +
                      ". Next step: press Reconcile. Reading a real package takes a while, and the " +
                      "screen is disabled while it is read.");
        }

        /// <summary>
        /// Joins the kept rows to a SEPro package and shapes everything the screen shows.
        ///
        /// Static and free of the interface, for the same reason as <see cref="ImportAndFilter"/>: it
        /// runs on the background thread, and it is what the harness drives.
        /// </summary>
        public static DeNovoReconciliation Reconcile(IReadOnlyList<PeaksDeNovoRow> filtered, string sepr2Path,
                                                      int? rawDeNovoRows = null,
                                                      DeNovoFilterCriteria? criteria = null)
        {
            if (sepr2Path == null) throw new ArgumentNullException(nameof(sepr2Path));

            return Reconcile(filtered, SeproPsmAdapter.Load(sepr2Path), sepr2Path, rawDeNovoRows, criteria);
        }

        /// <summary>The same join over a PSM list that is already in hand, which is the only part of it
        /// that can be exercised without a 48 MB package.</summary>
        public static DeNovoReconciliation Reconcile(IReadOnlyList<PeaksDeNovoRow> filtered,
                                                     List<PatternLabPsm> psms, string source,
                                                     int? rawDeNovoRows = null,
                                                     DeNovoFilterCriteria? criteria = null)
        {
            if (filtered == null) throw new ArgumentNullException(nameof(filtered));
            if (psms == null) throw new ArgumentNullException(nameof(psms));

            var rows = DeNovoReconciler.Reconcile(filtered, psms);

            return new DeNovoReconciliation
            {
                Sepr2Path = source ?? string.Empty,
                Psms = psms,
                Rows = rows,
                Counts = DeNovoReconciliationReport.CountByVerdict(rows),
                ReviewQueue = DeNovoReconciliationReport.BuildReviewQueue(rows),
                Overlap = DeNovoOverlapStudy.Build(rawDeNovoRows ?? filtered.Count, rows, criteria),
            };
        }

        private async void ButtonReconcile_Click(object sender, RoutedEventArgs e)
        {
            if (FilteredRows.Count == 0)
            {
                SetStatus("Nothing was reconciled: no de novo rows have been kept, so there is nothing " +
                          "to join. Next step: import the selected de novo export in Step 1 first.");
                return;
            }

            string path = TextBoxSeproPackage.Text.Trim();

            if (path.Length == 0 || !File.Exists(path))
            {
                SetStatus(
                    "Nothing was reconciled: " + (path.Length == 0
                        ? "no SEPro package has been chosen yet."
                        : "the file \"" + path + "\" does not exist or cannot be reached.") +
                    " Next step: press Browse in Layer 2 and pick the .sepr2 of the same runs.");
                return;
            }

            // Every Layer, not only the two this operation writes: see SetScreenEnabled.
            SetScreenEnabled(false);

            SetStatus("Reading " + Path.GetFileName(path) + " and joining it to " +
                      FilteredRows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                      " kept de novo row(s), by run file name, MS2 scan, reported charge and precursor m/z. A real " +
                      "package is tens of megabytes, so this takes a while.");

            try
            {
                var filtered = FilteredRows;

                int rawRows = LastImport?.RowsRead ?? filtered.Count;
                DeNovoFilterCriteria criteria = Criteria;
                DeNovoReconciliation outcome = await Task.Run(() => Reconcile(filtered, path, rawRows, criteria));

                Dispatcher.Invoke(() => ApplyReconciliation(outcome));
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The SEPro package could not be read, so nothing was reconciled and the screen " +
                    "still shows whatever it showed before. Reason given by the program: " + ex.Message +
                    " Next step: check that the .sepr2 opens in SEPro and that it is not open in " +
                    "another program, then press Reconcile again.");
            }
            finally
            {
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }

        /// <summary>Puts a reconciliation on screen: the five counts, the review queue and the exports.</summary>
        public void ApplyReconciliation(DeNovoReconciliation outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));

            // A second reconciliation kept a different set of rows, so the areas measured for the first
            // one no longer describe what is on screen. The homology pass goes for the same reason: a
            // reconciliation switches PeptidesForHomology from the filtered rows to the kept verdicts,
            // so the very first one already changes the set Layer 4 would search.
            ClearHomology();
            ClearExtraction();

            LastReconciliation = outcome;

            LabelVerdictDeNovoOnly.Content = "De novo only: " + Count(outcome, ReconciliationVerdict.DeNovoOnly);
            LabelVerdictCoIsolated.Content = "Different precursor context: " + Count(outcome, ReconciliationVerdict.CoIsolatedExtra);
            LabelVerdictRedundant.Content = "Redundant equal: " + Count(outcome, ReconciliationVerdict.RedundantEqual);
            LabelVerdictWeakPsm.Content = "Equal but weak PSM: " + Count(outcome, ReconciliationVerdict.EqualButWeakPsm);
            LabelVerdictConflict.Content = "Overlapping alternative sequence: " + Count(outcome, ReconciliationVerdict.Conflict);

            LabelReviewQueueCount.Content = "Review queue: " +
                outcome.ReviewQueue.Count.ToString("N0", CultureInfo.CurrentCulture) + " to decide";

            DeNovoOverlapSummary overlap = outcome.Overlap
                ?? DeNovoOverlapStudy.Build(FilteredRows.Count, outcome.Rows, Criteria);
            LabelOverlapScan.Content = "Same file + scan: " + N(overlap.SharedFileAndScan);
            LabelOverlapPrecursor.Content = "Comparable precursor (same charge, m/z within " +
                overlap.PrecursorTolerancePpm.ToString("0.##", CultureInfo.CurrentCulture) + " ppm): " +
                N(overlap.SamePrecursorWithinTolerance);
            LabelOverlapExact.Content = "Same scan + charge exact sequence, PTMs ignored: " +
                N(overlap.ScanLevelStrictSequenceMatchesIgnoringPtm);
            LabelOverlapDivergent.Content = "Same scan + charge alternative sequence: " +
                N(overlap.ScanLevelSequenceDivergencesIgnoringPtmStrict);

            DataGridReviewQueue.ItemsSource = outcome.ReviewQueue;

            RefreshRawMatchPreview();
            UpdateLayerStates();

            int alternatives = outcome.Counts.TryGetValue(ReconciliationVerdict.Conflict, out int c) ? c : 0;
            int weak = outcome.Counts.TryGetValue(ReconciliationVerdict.EqualButWeakPsm, out int w) ? w : 0;

            SetStatus(
                "Showing " + FilteredRows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " kept de novo row(s) reconciled against " +
                outcome.Psms.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " PatternLab identification(s) from " + Path.GetFileName(outcome.Sepr2Path) + ": " +
                Count(outcome, ReconciliationVerdict.DeNovoOnly) + " new, " +
                Count(outcome, ReconciliationVerdict.CoIsolatedExtra) + " different-precursor-context rows, " +
                Count(outcome, ReconciliationVerdict.RedundantEqual) + " redundant, and " +
                (alternatives + weak).ToString("N0", CultureInfo.CurrentCulture) +
                " in the review queue. Of " + N(overlap.SharedFileAndScan) + " rows sharing file + scan, " +
                N(overlap.DifferentChargeInSharedScan) + " report a different charge and " +
                N(overlap.SameChargeButNoMzMatch) + " share charge but not precursor m/z. Among " +
                N(overlap.SamePrecursorWithinTolerance) + " rows passing file + scan + charge + 20-ppm m/z, " +
                N(overlap.StrictSequenceMatchesIgnoringPtm) + " are exact after PTM removal (" +
                N(overlap.IleLeuEquivalentSequenceMatches) + " after I/L folding) and " +
                N(overlap.SequenceDivergencesAfterIleLeuFold) +
                " remain alternative sequences. For the broader same-file/scan/charge audit, " +
                N(overlap.ScanLevelStrictSequenceMatchesIgnoringPtm) +
                " are exact after PTM removal (" + N(overlap.ScanLevelIleLeuEquivalentSequenceMatches) +
                " when I/L are folded) and " + N(overlap.ScanLevelSequenceDivergencesAfterIleLeuFold) +
                " differ after I/L folding. These are de-novo/search reconciliation rates, not a " +
                "search-vs-search accuracy estimate. Comparable-precursor alternatives are excluded from additive de novo quantification.");
        }

        private static string Count(DeNovoReconciliation outcome, ReconciliationVerdict verdict)
        {
            int n = outcome.Counts.TryGetValue(verdict, out int value) ? value : 0;
            return n.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static string N(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

        // ===================================================================== export

        /// <summary>The per-run verdict summary. The export button's only path.</summary>
        public void ExportSummaryCsv(string path)
        {
            if (LastReconciliation == null) throw new InvalidOperationException(
                "There is no reconciliation to export; run one in Layer 2 first.");

            DeNovoReconciliationReport.WriteSummaryCsv(path, LastReconciliation.Rows);
        }

        /// <summary>The review queue, exactly as the table shows it. The export button's only path.</summary>
        public void ExportReviewQueueCsv(string path)
        {
            if (LastReconciliation == null) throw new InvalidOperationException(
                "There is no reconciliation to export; run one in Layer 2 first.");

            DeNovoReconciliationReport.WriteReviewQueueCsv(path, LastReconciliation.Rows);
        }

        public void ExportOverlapStudyCsv(string path)
        {
            if (LastReconciliation?.Overlap == null) throw new InvalidOperationException(
                "There is no overlap study to export; run a reconciliation in Step 2 first.");
            File.WriteAllText(path, DeNovoOverlapStudy.BuildSummaryCsv(LastReconciliation.Overlap),
                              new UTF8Encoding(false));
        }

        private void ButtonExportSummary_Click(object sender, RoutedEventArgs e)
        {
            if (!HaveReconciliationToExport()) return;

            Export("FamilyQuant_DeNovo_Summary.csv", "Save the per-run verdict summary as CSV",
                   ExportSummaryCsv,
                   "one line per run and verdict, with how many rows fell into each");
        }

        private void ButtonExportReviewQueue_Click(object sender, RoutedEventArgs e)
        {
            if (!HaveReconciliationToExport()) return;

            Export("FamilyQuant_DeNovo_ReviewQueue.csv", "Save the review queue as CSV",
                   ExportReviewQueueCsv,
                   "one line per row of the table, both sequences side by side");
        }

        private void ButtonExportOverlapStudy_Click(object sender, RoutedEventArgs e)
        {
            if (!HaveReconciliationToExport()) return;

            Export("FamilyQuant_DeNovo_OverlapStudy.csv", "Save the de novo/search-engine overlap study as CSV",
                   ExportOverlapStudyCsv,
                   "the nested scan, precursor, exact-sequence and divergence counts with their denominators");
        }

        private bool HaveReconciliationToExport()
        {
            if (LastReconciliation != null) return true;

            SetStatus("Nothing was exported: no reconciliation has been run, so there are no " +
                      "verdicts to write. Next step: press Reconcile in Layer 2 first.");
            return false;
        }

        /// <summary>
        /// The exports differ only in what they write, so the dialog and the reporting are here once.
        /// What is there to write is the CALLER's question, because the Layers have different answers
        /// to it. Success is reported in the status line rather than in a dialog, for the same reason
        /// as everything else on this screen.
        /// </summary>
        private void Export(string suggestedName, string title, Action<string> write, string what)
        {
            if (!CanShowDialog("Nothing was exported", "nothing. The same rows are on screen.")) return;

            var sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = suggestedName,
                Title = title
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                write(sfd.FileName);

                SetStatus("Written to " + sfd.FileName + ": " + what + ". Numbers use a dot for the " +
                          "decimal separator whatever your machine's language is, so any program can " +
                          "read them. Nothing on screen changed. Next step: nothing.");
            }
            catch (Exception ex)
            {
                SetStatus("The file could not be written, so nothing was exported. Reason given by the " +
                          "program: " + ex.Message + " Next step: check that it is not already open in " +
                          "Excel and that you can write to that folder, then export again.");
            }
        }

        // ===================================================================== layer 3

        /// <summary>Name of the folder the per-run extraction caches are written into, beside the RAW
        /// files. Beside them and not in a temporary folder on purpose: the cache belongs to the
        /// acquisitions, and a second session over the same runs is then answered without opening a
        /// 3 GB file again.</summary>
        public const string CacheFolderName = "familyquant_denovo_cache";

        /// <summary>
        /// Where one run's cache lives, given the run name. Null in the application, where
        /// <see cref="CachePathFor"/> puts it beside the RAW files.
        ///
        /// A SEAM FOR THE HARNESS, and for nothing else. A suite run already holds the caches of the
        /// three acquisitions it measures; without this it would re-read three 3 GB files to arrive at
        /// numbers it has already proved, which is minutes of RAW loading per run for no evidence.
        /// </summary>
        public static Func<string, string>? CachePathForRunOverride;

        public static string CachePathFor(string runName, string rawDirectory)
        {
            Func<string, string>? seam = CachePathForRunOverride;
            if (seam != null) return seam(runName);

            return Path.Combine(rawDirectory, CacheFolderName, "denovo_xic_" + SafeFileName(runName) + ".json");
        }

        /// <summary>A run name that a file system will accept. Run names come off a disk and normally
        /// need nothing done to them; this exists so a strange one cannot throw during a save.</summary>
        private static string SafeFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();

            foreach (char c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            return sb.Length == 0 ? "run" : sb.ToString();
        }

        /// <summary>
        /// The runs the reconciliation kept rows for, in name order. Only DeNovoOnly and
        /// CoIsolatedExtra: a redundant equality adds no ion current the PatternLab side does not
        /// already hold, and counting it would put the same signal on both segments of one bar.
        /// </summary>
        public List<string> KeptRunNames() => KeptRowsByRun().Keys
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

        /// <summary>The kept rows of each run, keyed by normalized run name.</summary>
        public Dictionary<string, List<PeaksDeNovoRow>> KeptRowsByRun()
        {
            var byRun = new Dictionary<string, List<PeaksDeNovoRow>>(StringComparer.OrdinalIgnoreCase);
            if (LastReconciliation == null) return byRun;

            foreach (ReconciledRow row in LastReconciliation.Rows)
            {
                if (row.Verdict != ReconciliationVerdict.DeNovoOnly
                    && row.Verdict != ReconciliationVerdict.CoIsolatedExtra) continue;

                string run = DeNovoReconciler.NormalizeRunName(row.DeNovo.RunName);
                if (run.Length == 0) continue;

                if (!byRun.TryGetValue(run, out List<PeaksDeNovoRow>? rows))
                    byRun[run] = rows = new List<PeaksDeNovoRow>();

                rows.Add(row.DeNovo);
            }

            return byRun;
        }

        /// <summary>
        /// Which kept run found which acquisition. BY FILE NAME, through the same normalisation the
        /// reconciliation joins with, so "x.raw", "x.RAW" and "x" denote one run.
        ///
        /// Static and free of the interface, like the other workers: this is what the harness drives.
        /// </summary>
        public static List<DeNovoRunRawMatch> MatchRunsToRaw(IEnumerable<string> runs, string rawDirectory)
        {
            if (runs == null) throw new ArgumentNullException(nameof(runs));

            var byRun = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!string.IsNullOrWhiteSpace(rawDirectory) && Directory.Exists(rawDirectory))
                {
                    foreach (string file in Directory.GetFiles(rawDirectory, "*.raw", SearchOption.TopDirectoryOnly))
                    {
                        string run = DeNovoReconciler.NormalizeRunName(file);
                        if (run.Length > 0 && !byRun.ContainsKey(run)) byRun.Add(run, file);
                    }
                }
            }
            catch (Exception)
            {
                // An unreadable folder is an empty one here. The caller reports it as runs not found,
                // which is the same sentence and the same consequence.
            }

            var matches = new List<DeNovoRunRawMatch>();

            foreach (string run in runs.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            {
                byRun.TryGetValue(run, out string? path);
                matches.Add(new DeNovoRunRawMatch
                {
                    Run = run,
                    ExpectedFileName = run + ".raw",
                    RawPath = path,
                });
            }

            return matches;
        }

        /// <summary>The selected de novo export normally sits one level under the acquisitions, so that folder is
        /// offered. Only when nothing has been typed: a path the user chose is never overwritten.</summary>
        private void SuggestRawDirectory(string peaksDirectory)
        {
            if (TextBoxRawDirectory == null || TextBoxRawDirectory.Text.Trim().Length > 0) return;
            if (string.IsNullOrWhiteSpace(peaksDirectory)) return;

            try
            {
                DirectoryInfo? parent = Directory.GetParent(peaksDirectory);
                if (parent != null && parent.Exists) TextBoxRawDirectory.Text = parent.FullName;
            }
            catch (Exception)
            {
                // A path with no parent is not an error: the box simply stays empty and the preview
                // says no run found its acquisition.
            }
        }

        private void RefreshRawMatchPreview()
        {
            if (TextBlockRawMatchPreview == null) return;

            List<string> runs = KeptRunNames();

            if (runs.Count == 0)
            {
                RawMatches = Array.Empty<DeNovoRunRawMatch>();
                TextBlockRawMatchPreview.Text =
                    "No reconciliation yet. Which kept run found which .raw appears here.";
                return;
            }

            RawMatches = MatchRunsToRaw(runs, TextBoxRawDirectory?.Text.Trim() ?? string.Empty);

            TextBlockRawMatchPreview.Text = string.Join("\n", RawMatches.Select(m =>
                m.Found
                    ? m.Run + " · found " + Path.GetFileName(m.RawPath!)
                    : m.Run + " · NOT FOUND, looked for " + m.ExpectedFileName));
        }

        private void TextBoxRawDirectory_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TextBlockRawMatchPreview == null) return;

            // An extraction is OF the acquisitions in the folder that was named when it ran, and Layer
            // 5 measures each run's total ion current from those same files. Editing this box changes
            // which files those are, so an extraction left standing could put a TIC measured in one
            // folder beside a de novo numerator extracted from another; the bar would still close on
            // 100% and nothing would fail. So the extraction goes, and the partition with it. Layer 4
            // is deliberately untouched: homology reads peptides, and no peptide comes from this box.
            bool dropped = LastExtraction != null || LastPartition != null;
            if (dropped) ClearExtraction();

            RefreshRawMatchPreview();
            UpdateLayerStates();

            if (dropped)
            {
                SetStatus(
                    "The folder holding the acquisitions was changed, so the extraction and any " +
                    "partition built on it were dropped: they measured the .raw files of the previous " +
                    "folder, and mixing the two would draw a bar that closes on 100% out of two " +
                    "different acquisitions. Nothing else was touched. Next step: check that every " +
                    "kept run found its acquisition below, then press Extract again.");
            }
        }

        private void ButtonBrowseRawDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No folder was chosen",
                               "type the folder holding the .raw acquisitions into the box beside the " +
                               "button instead.")) return;

            var ofd = new OpenFolderDialog
            {
                Title = "Choose the folder holding the .raw acquisitions of these runs"
            };

            if (ofd.ShowDialog() != true) return;

            TextBoxRawDirectory.Text = ofd.FolderName;   // the TextChanged handler redoes the preview

            int found = RawMatches.Count(m => m.Found);

            SetStatus(found == RawMatches.Count && RawMatches.Count > 0
                ? "All " + found.ToString("N0", CultureInfo.CurrentCulture) + " kept run(s) found their " +
                  "acquisition in that folder, by file name. Next step: press Extract. Each RAW is opened " +
                  "once and the runs are done strictly one after another."
                : "Of " + RawMatches.Count.ToString("N0", CultureInfo.CurrentCulture) + " kept run(s), " +
                  found.ToString("N0", CultureInfo.CurrentCulture) + " found an acquisition in that " +
                  "folder. Missing: " + MissingRunsSentence() + " Next step: choose the folder those " +
                  "files really are in. Extract stays disabled until every kept run has one, because a " +
                  "partial extraction would draw a de novo slice with a whole run silently missing.");
        }

        private string MissingRunsSentence()
        {
            var missing = RawMatches.Where(m => !m.Found).Select(m => m.ExpectedFileName).ToList();
            return missing.Count == 0 ? "none." : string.Join(", ", missing) + ".";
        }

        /// <summary>
        /// Extracts every kept run, ONE AT A TIME.
        ///
        /// Static and free of the interface, like <see cref="ImportAndFilter"/>: it runs on the
        /// background thread and it is what the harness drives.
        ///
        /// The runs are strictly sequential and the per-run result list is released before the next
        /// acquisition is opened, because an MS1 list of a 3 GB RAW dominates this process's memory and
        /// two resident ones is the difference between a batch that finishes and one that dies.
        /// </summary>
        public static DeNovoXicExtraction ExtractXic(IReadOnlyList<DeNovoRunRawMatch> matches,
                                                     IReadOnlyDictionary<string, List<PeaksDeNovoRow>> keptByRun,
                                                     string rawDirectory,
                                                     Action<string>? progress)
        {
            if (matches == null) throw new ArgumentNullException(nameof(matches));
            if (keptByRun == null) throw new ArgumentNullException(nameof(keptByRun));

            var extraction = new DeNovoXicExtraction { RawDirectory = rawDirectory ?? string.Empty };

            foreach (DeNovoRunRawMatch match in matches)
            {
                if (!match.Found)
                {
                    throw new InvalidOperationException(
                        "Run \"" + match.Run + "\" has no acquisition in the chosen folder (looked for " +
                        match.ExpectedFileName + "). Extracting the other runs and leaving this one out " +
                        "would draw a de novo slice with a whole run silently missing.");
                }

                if (!keptByRun.TryGetValue(match.Run, out List<PeaksDeNovoRow>? rows) || rows.Count == 0)
                    continue;

                var options = new DeNovoXicOptions
                {
                    CachePath = CachePathFor(match.Run, extraction.RawDirectory),
                    Progress = progress,
                };

                DeNovoBatchStats stats = ExtractOneRun(match.RawPath!, rows, options);

                extraction.Stats.Add(stats);
                extraction.ByRun[match.Run] = stats;

                // The MS1 list of the run just finished must be gone before the next RAW is opened.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            return extraction;
        }

        /// <summary>One run, reduced to its totals. A separate method on purpose: the per-precursor
        /// result list is several megabytes and must not stay reachable while the next acquisition is
        /// being loaded.</summary>
        private static DeNovoBatchStats ExtractOneRun(string rawPath, IReadOnlyList<PeaksDeNovoRow> rows,
                                                      DeNovoXicOptions options)
        {
            var (_, stats) = DeNovoBatchExtractor.ExtractForRun(rawPath, rows, options);
            return stats;
        }

        private async void ButtonExtractXic_Click(object sender, RoutedEventArgs e)
        {
            if (LastReconciliation == null)
            {
                SetStatus("Nothing was extracted: no reconciliation has been run, so it is not known " +
                          "which rows are the kept ones. Next step: press Reconcile in Layer 2 first.");
                return;
            }

            RefreshRawMatchPreview();

            if (RawMatches.Count == 0 || RawMatches.Any(m => !m.Found))
            {
                SetStatus("Nothing was extracted: " + MissingRunsSentence() +
                          " was not found in \"" + (TextBoxRawDirectory.Text.Trim()) + "\". Next step: " +
                          "give the folder those acquisitions really are in. Extracting only the runs " +
                          "that were found would draw a de novo slice with a whole run silently missing.");
                UpdateLayerStates();
                return;
            }

            var matches = RawMatches.ToList();
            var keptByRun = KeptRowsByRun();
            string rawDirectory = TextBoxRawDirectory.Text.Trim();

            SetScreenEnabled(false);
            SetStatus("Extracting " + matches.Count.ToString("N0", CultureInfo.CurrentCulture) +
                      " run(s), one at a time. Each acquisition is opened once and its MS1 spectra take " +
                      "tens of seconds to load; anything already in the cache beside the RAW files is " +
                      "answered without opening them at all.");

            try
            {
                // Progress arrives from the background thread, so it is marshalled rather than written
                // straight into a control.
                Action<string> progress = m => Dispatcher.BeginInvoke(new Action(() =>
                    SetStatus("Extracting: " + m)));

                DeNovoXicExtraction outcome = await Task.Run(
                    () => ExtractXic(matches, keptByRun, rawDirectory, progress));

                Dispatcher.Invoke(() => ApplyExtraction(outcome));
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The extraction did not finish, so nothing was measured and the screen still shows " +
                    "whatever it showed before. Reason given by the program: " + ex.Message +
                    " Next step: check that the .raw files are really acquisitions of these runs and " +
                    "that they are not open in another program, then press Extract again.");
            }
            finally
            {
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }

        /// <summary>Puts an extraction on screen: the per-run table, the totals and Layer 5.</summary>
        public void ApplyExtraction(DeNovoXicExtraction outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));

            // A new extraction invalidates any partition built on the previous one.
            ClearPartition();

            LastExtraction = outcome;

            DataGridXicStats.ItemsSource = outcome.Stats;

            LabelXicRunsExtracted.Content = "Runs extracted: " +
                outcome.Stats.Count.ToString("N0", CultureInfo.CurrentCulture) + " of " +
                KeptRunNames().Count.ToString("N0", CultureInfo.CurrentCulture);
            LabelXicTotalM0.Content = "Total M+0: " +
                outcome.TotalAreaM0.ToString("E4", CultureInfo.CurrentCulture);
            LabelXicElapsed.Content = "Elapsed: " +
                outcome.ElapsedSeconds.ToString("N1", CultureInfo.CurrentCulture) + " s";

            UpdateLayerStates();

            int redundant = outcome.Stats.Sum(s => s.RedundantAnchors);

            SetStatus(
                "Step 5 of 5 · Extracted " + outcome.Stats.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " run(s) in " + outcome.ElapsedSeconds.ToString("N1", CultureInfo.CurrentCulture) +
                " s: " + outcome.Stats.Sum(s => s.DistinctPeaks).ToString("N0", CultureInfo.CurrentCulture) +
                " distinct chromatographic peak(s) carrying " +
                outcome.TotalAreaM0.ToString("E4", CultureInfo.CurrentCulture) +
                " of monoisotopic ion current, after collapsing " +
                redundant.ToString("N0", CultureInfo.CurrentCulture) +
                " anchor(s) that were the same peak sampled by another MS2 scan. Next step: choose a " +
                "PatternLab project in Layer 5 and press \"Build partition\".");
        }

        // ===================================================================== layer 4

        private void WriteHomologySettingsToBoxes()
        {
            // InvariantCulture, and deliberately: the identity is read back through an invariant
            // reader, so a comma-decimal machine must not be shown a value its own reader would refuse.
            TextBoxMinIdentity.Text = HomologySettings.MinIdentity.ToString("0.##", CultureInfo.InvariantCulture);
            TextBoxTopHits.Text = HomologySettings.TopHitsPerPeptide.ToString(CultureInfo.InvariantCulture);
            CheckBoxConsiderILSame.IsChecked = HomologySettings.ConsiderILSame;
            CheckBoxKQEquivalent.IsChecked = HomologySettings.KQEquivalent;
            TextBoxDecoyLabel.Text = HomologySettings.DecoyLabel;
        }

        /// <summary>
        /// Reads the five homology boxes into the settings in force, all five or none.
        ///
        /// A refused value keeps the previous one and says which box was refused, exactly as the four
        /// confidence criteria do, and for the same reason: a modal box for a typing mistake is an
        /// interruption on screen and a hang in a harness that has none.
        /// </summary>
        public bool ReadHomologySettingsFromBoxes()
        {
            if (!TryReadDouble(TextBoxMinIdentity, "Minimum identity", 0, 1,
                               WriteHomologySettingsToBoxes, out double minIdentity)) return false;
            if (!TryReadInt(TextBoxTopHits, "Top hits per peptide", 1, 1000,
                            WriteHomologySettingsToBoxes, out int topHits)) return false;

            string decoy = TextBoxDecoyLabel.Text ?? string.Empty;
            if (decoy.Trim().Length == 0)
            {
                Refuse("Decoy prefix", decoy, "a non-empty prefix, because a search with no decoy label " +
                       "has no control to read its mapped fraction against", WriteHomologySettingsToBoxes);
                return false;
            }

            HomologySettings = new HomologyOptions
            {
                MinIdentity = minIdentity,
                TopHitsPerPeptide = topHits,
                ConsiderILSame = CheckBoxConsiderILSame.IsChecked == true,
                KQEquivalent = CheckBoxKQEquivalent.IsChecked == true,
                DecoyLabel = decoy.Trim(),
            };

            WriteHomologySettingsToBoxes();
            return true;
        }

        /// <summary>
        /// The peptides Layer 4 searches: the kept rows of the reconciliation when there is one, and
        /// every filtered row when there is not.
        ///
        /// Deduplicated by normalised sequence, because the same tag read in fifty spectra is one
        /// alignment and not fifty, and ordered so that two runs of this screen search the same list.
        /// </summary>
        public List<string> PeptidesForHomology()
        {
            IEnumerable<PeaksDeNovoRow> rows = LastReconciliation == null
                ? FilteredRows
                : LastReconciliation.Rows
                    .Where(r => r.Verdict == ReconciliationVerdict.DeNovoOnly
                             || r.Verdict == ReconciliationVerdict.CoIsolatedExtra)
                    .Select(r => r.DeNovo);

            return rows
                .Select(r => DeNovoReconciler.NormalizeSequence(r.Peptide))
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        private void ButtonBrowseHomologyFcf_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No Family Cluster File was chosen",
                               "type the path of the .fcf into the box beside the button instead.")) return;

            var ofd = new OpenFileDialog
            {
                Filter = "FamilyQuant project or Family Cluster File (*.fqproj;*.fcf)|*.fqproj;*.fcf",
                Title = "Choose the project or family file to search against and map onto"
            };

            if (ofd.ShowDialog() != true) return;

            TextBoxHomologyFcf.Text = ofd.FileName;

            SetStatus("Family Cluster File chosen: " + Path.GetFileName(ofd.FileName) +
                      ". Next step: check the identity threshold and the two residue options, then press " +
                      "Search. The file supplies both the sequences that are searched and the family " +
                      "each hit is attributed to.");
        }

        /// <summary>
        /// Searches the peptides against a Family Cluster File and attributes the hits to families.
        ///
        /// Static and free of the interface: it runs on the background thread and it is what the
        /// harness drives. The family table counts TARGET hits only, because a decoy family reached by
        /// a decoy-only peptide is the control and not the biology.
        /// </summary>
        public static DeNovoHomology SearchHomology(IReadOnlyList<string> peptides, FamilyClusterFile fcf,
                                                    HomologyOptions options, string fcfPath)
        {
            if (peptides == null) throw new ArgumentNullException(nameof(peptides));
            if (fcf == null) throw new ArgumentNullException(nameof(fcf));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var database = HomologyDatabase.Build(fcf.MyExtendedFastaItems);

            DateTime started = DateTime.UtcNow;
            List<HomologyResult> results = HomologyMapper.Search(peptides, database, options);
            double seconds = (DateTime.UtcNow - started).TotalSeconds;

            List<FamilyAssignment> assignments = HomologyMapper.MapToFamilies(results, fcf.MyExtendedFastaItems);

            // The engine's own aggregation, not a copy of it: the suite pins the family census against
            // this very method, so the number on screen and the number that is pinned cannot diverge.
            List<DeNovoFamilyRow> rows =
                HomologyMapper.CountByFamily(results, fcf.MyExtendedFastaItems, options.DecoyLabel);

            return new DeNovoHomology
            {
                FcfPath = fcfPath ?? string.Empty,
                PeptidesSearched = results.Count,
                MappedToTargets = results.Count(r => r.Hits.Any(h => !h.IsDecoy)),
                DecoyOnly = results.Count(r => r.Hits.Count > 0 && r.Hits.All(h => h.IsDecoy)),
                Unmatched = results.Count(r => !r.IsRejected && r.Hits.Count == 0),
                Rejected = results.Count(r => r.IsRejected),
                ElapsedSeconds = Math.Round(seconds, 1),
                Results = results,
                Assignments = assignments,
                Families = rows,
            };
        }

        private async void ButtonSearchHomology_Click(object sender, RoutedEventArgs e)
        {
            if (!ReadHomologySettingsFromBoxes()) return;   // Refuse() has already said which box, and why

            List<string> peptides = PeptidesForHomology();

            if (peptides.Count == 0)
            {
                SetStatus("Nothing was searched: no kept de novo row carries a sequence. Next step: " +
                          "import the selected de novo export in Step 1 first.");
                return;
            }

            string fcfPath = TextBoxHomologyFcf.Text.Trim();

            if (fcfPath.Length == 0 || !File.Exists(fcfPath))
            {
                SetStatus(
                    "Nothing was searched: " + (fcfPath.Length == 0
                        ? "no Family Cluster File has been chosen yet."
                        : "the file \"" + fcfPath + "\" does not exist or cannot be reached.") +
                    " Next step: press Browse in Layer 4 and pick the .fcf clustered from the database " +
                    "these peptides should be attributed to.");
                return;
            }

            var options = HomologySettings;

            SetScreenEnabled(false);
            SetStatus("Searching " + peptides.Count.ToString("N0", CultureInfo.CurrentCulture) +
                      " distinct peptide(s) against " + Path.GetFileName(fcfPath) + " at " +
                      options.MinIdentity.ToString("P0", CultureInfo.CurrentCulture) +
                      " identity. Tens of thousands of alignments take a couple of minutes.");

            try
            {
                DeNovoHomology outcome = await Task.Run(() =>
                {
                    FamilyClusterFile fcf = LoadHomologyCuration(fcfPath);
                    return SearchHomology(peptides, fcf, options, fcfPath);
                });

                Dispatcher.Invoke(() => ApplyHomology(outcome));
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The search did not finish, so nothing was attributed and the screen still shows " +
                    "whatever it showed before. Reason given by the program: " + ex.Message +
                    " Next step: check that the file really is a .fcf written by FamilyQuant and that " +
                    "it is not open in another program, then press Search again.");
            }
            finally
            {
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }

        /// <summary>Puts a homology pass on screen: the four counts, the family table and the export.</summary>
        public void ApplyHomology(DeNovoHomology outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));

            LastHomology = outcome;

            double share = outcome.PeptidesSearched > 0
                ? (double)outcome.MappedToTargets / outcome.PeptidesSearched
                : 0;

            LabelHomologyPeptides.Content = "Peptides searched: " +
                outcome.PeptidesSearched.ToString("N0", CultureInfo.CurrentCulture);
            LabelHomologyMapped.Content = "Mapped to targets: " +
                outcome.MappedToTargets.ToString("N0", CultureInfo.CurrentCulture) +
                " (" + share.ToString("P2", CultureInfo.CurrentCulture) + ")";
            LabelHomologyDecoyOnly.Content = "Decoy-only: " +
                outcome.DecoyOnly.ToString("N0", CultureInfo.CurrentCulture);
            LabelHomologyUnmatched.Content = "Unmatched: " +
                outcome.Unmatched.ToString("N0", CultureInfo.CurrentCulture);
            LabelHomologyElapsed.Content = "Elapsed: " +
                outcome.ElapsedSeconds.ToString("N1", CultureInfo.CurrentCulture) + " s";

            DataGridHomologyFamilies.ItemsSource = outcome.Families;

            UpdateLayerStates();

            SetStatus(
                "Showing " + outcome.PeptidesSearched.ToString("N0", CultureInfo.CurrentCulture) +
                " distinct peptide(s) searched against " + Path.GetFileName(outcome.FcfPath) + ": " +
                outcome.MappedToTargets.ToString("N0", CultureInfo.CurrentCulture) + " reached a target (" +
                share.ToString("P2", CultureInfo.CurrentCulture) + "), " +
                outcome.DecoyOnly.ToString("N0", CultureInfo.CurrentCulture) +
                " reached decoys only, and " +
                outcome.Unmatched.ToString("N0", CultureInfo.CurrentCulture) +
                " reached nothing, across " +
                outcome.Families.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " target family(ies). Read the mapped count against the decoy-only count beside it: that " +
                "is the rate at which this threshold invents a family. Next step: export the " +
                "assignments, or go on to Layer 5.");
        }

        /// <summary>The per-peptide assignments. The export button's only path.</summary>
        public void ExportHomologyCsv(string path)
        {
            if (LastHomology == null) throw new InvalidOperationException(
                "There is no homology search to export; run one in Layer 4 first.");

            var csv = new StringBuilder();
            csv.AppendLine("peptide,family_ids,protein_ids,ambiguous");

            foreach (FamilyAssignment a in LastHomology.Assignments)
            {
                csv.AppendLine(string.Join(",",
                    CsvField(a.Peptide),
                    CsvField(string.Join(";", a.FamilyIds.Select(f => f.ToString(CultureInfo.InvariantCulture)))),
                    CsvField(string.Join(";", a.ProteinIds)),
                    a.IsAmbiguous ? "true" : "false"));
            }

            File.WriteAllText(path, csv.ToString());
        }

        /// <summary>Quotes and doubles embedded quotes, RFC 4180, as every other export here does.
        /// Protein identifiers come out of a FASTA and may hold anything.</summary>
        private static string CsvField(string? value) =>
            "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private void ButtonExportHomologyCsv_Click(object sender, RoutedEventArgs e)
        {
            if (LastHomology == null)
            {
                SetStatus("Nothing was exported: no homology search has been run, so there are no " +
                          "assignments to write. Next step: press Search in Layer 4 first.");
                return;
            }

            Export("FamilyQuant_DeNovo_Homology.csv", "Save the per-peptide family assignments as CSV",
                   ExportHomologyCsv,
                   "one line per searched peptide, with its family ids, its protein ids and whether it " +
                   "is ambiguous");
        }

        // ===================================================================== layer 5

        private void ButtonBrowsePartitionPlp_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No PatternLab project was chosen",
                               "type the path of the .plp into the box beside the button instead.")) return;

            var ofd = new OpenFileDialog
            {
                Filter = "PatternLab project (*.plp)|*.plp",
                Title = "Choose the PatternLab project holding the quantitation of these runs"
            };

            if (ofd.ShowDialog() != true) return;

            TextBoxPartitionPlp.Text = ofd.FileName;

            SetStatus("PatternLab project chosen: " + Path.GetFileName(ofd.FileName) +
                      ". Next step: press \"Build partition\". If that project merged its technical " +
                      "replicates it cannot answer per run, and it will say so in its own words; give " +
                      "the .xic2 of the same extraction as well, so there is a source that can.");
        }

        private void ButtonBrowsePartitionXic2_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No stored quants file was chosen",
                               "type the path of the .xic2 into the box beside the button instead.")) return;

            var ofd = new OpenFileDialog
            {
                Filter = "Stored quants (*.xic2)|*.xic2",
                Title = "Choose the stored quants of the same extraction"
            };

            if (ofd.ShowDialog() != true) return;

            TextBoxPartitionXic2.Text = ofd.FileName;

            SetStatus("Stored quants chosen: " + Path.GetFileName(ofd.FileName) +
                      ". It is used only if the .plp cannot resolve one column per run. Next step: press " +
                      "\"Build partition\".");
        }

        /// <summary>
        /// Measures each run's total ion current and splits it three ways.
        ///
        /// Static and free of the interface, like every other worker here. The PatternLab side is asked
        /// of the .plp FIRST, because that is what the rest of the program quantifies from, and its
        /// refusal is carried out word for word rather than being reduced to a flag.
        /// </summary>
        public static DeNovoPartition BuildPartition(IReadOnlyList<DeNovoRunRawMatch> matches,
                                                     IReadOnlyDictionary<string, DeNovoBatchStats> xicByRun,
                                                     string? plpPath, string? xic2Path,
                                                     Action<string>? progress)
        {
            if (matches == null) throw new ArgumentNullException(nameof(matches));
            if (xicByRun == null) throw new ArgumentNullException(nameof(xicByRun));

            PatternLabProject? plp = null;
            if (!string.IsNullOrWhiteSpace(plpPath) && File.Exists(plpPath))
            {
                progress?.Invoke("reading " + Path.GetFileName(plpPath!) + "...");
                plp = new PatternLabProject(new FileInfo(plpPath!));
            }

            PatternLabRunQuants patternLab = PatternLabRunQuants.Preferred(plp, xic2Path, out string branchNote);

            if (!patternLab.IsAvailable) return new DeNovoPartition { BranchNote = branchNote };

            var ticByRun = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var deNovoByRun = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var envelopeByRun = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (DeNovoRunRawMatch match in matches)
            {
                if (!match.Found || !xicByRun.TryGetValue(match.Run, out DeNovoBatchStats? stats)) continue;

                progress?.Invoke("measuring the total ion current of " + match.Run + "...");

                // (0,0) is the whole run, both ends: the trim proved correct for this data. Both
                // parameters are required by the extractor precisely so that no divisor is a default
                // nobody chose.
                double tic = DeNovoXicExtractor.GetTotalIonCurrentArea(match.RawPath!, 0, 0);

                ticByRun[match.Run] = PartitionValue.Measured(tic, ThreeWayPartition.SourceRawTic);
                deNovoByRun[match.Run] = PartitionValue.Measured(stats.TotalAreaM0, ThreeWayPartition.SourceDeNovoM0);
                envelopeByRun[match.Run] = stats.TotalEnvelope;
            }

            var patternLabByRun = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in patternLab.AreaByRun)
                patternLabByRun[kv.Key] = PartitionValue.Recorded(kv.Value, patternLab.Source);

            // Only the runs that were extracted: a .xic2 may describe runs this de novo set has no rows
            // for, and Build refuses a run that is present on one side and absent from another rather
            // than filling the gap with a zero that would draw as a component that merely looks small.
            var patternLabForExtracted = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            foreach (string run in ticByRun.Keys)
            {
                if (patternLabByRun.TryGetValue(run, out PartitionValue value)) patternLabForExtracted[run] = value;
            }

            List<ThreeWayPartitionRow> rows =
                ThreeWayPartition.Build(ticByRun, patternLabForExtracted, deNovoByRun, envelopeByRun);

            return new DeNovoPartition
            {
                BranchNote = branchNote,
                Rows = rows,
                Grouped = ThreeWayPartition.Grouped(rows),
            };
        }

        private async void ButtonBuildPartition_Click(object sender, RoutedEventArgs e)
        {
            if (LastExtraction == null)
            {
                SetStatus("Nothing was partitioned: no run has been extracted, so there is no de novo " +
                          "segment to draw. Next step: press Extract in Layer 3 first.");
                return;
            }

            string plpPath = TextBoxPartitionPlp.Text.Trim();
            string xic2Path = TextBoxPartitionXic2.Text.Trim();

            if (plpPath.Length == 0 && xic2Path.Length == 0)
            {
                SetStatus("Nothing was partitioned: neither a PatternLab project nor a .xic2 has been " +
                          "chosen, so the identified segment of each bar has no source. Next step: press " +
                          "Browse in Layer 5 and pick the .plp of these runs.");
                return;
            }

            if (plpPath.Length > 0 && !File.Exists(plpPath))
            {
                SetStatus("Nothing was partitioned: the file \"" + plpPath + "\" does not exist or " +
                          "cannot be reached. Next step: correct that path and press \"Build partition\" " +
                          "again.");
                return;
            }

            var matches = RawMatches.ToList();
            var xicByRun = new Dictionary<string, DeNovoBatchStats>(LastExtraction.ByRun,
                                                                    StringComparer.OrdinalIgnoreCase);

            SetScreenEnabled(false);
            SetStatus("Measuring the whole chromatogram of " +
                      matches.Count.ToString("N0", CultureInfo.CurrentCulture) +
                      " run(s) and asking the PatternLab side for its quants per run. Reading three " +
                      "whole chromatograms takes a while.");

            try
            {
                Action<string> progress = m => Dispatcher.BeginInvoke(new Action(() =>
                    SetStatus("Building the partition: " + m)));

                DeNovoPartition outcome = await Task.Run(
                    () => BuildPartition(matches, xicByRun, plpPath, xic2Path.Length == 0 ? null : xic2Path,
                                         progress));

                Dispatcher.Invoke(() => ApplyPartition(outcome));
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The partition could not be built, so nothing was drawn and the screen still shows " +
                    "whatever it showed before. Reason given by the program: " + ex.Message +
                    " Next step: check that the .plp and the .xic2 belong to these same runs and that " +
                    "neither is open in another program, then press \"Build partition\" again.");
            }
            finally
            {
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }

        /// <summary>Puts a partition on screen: the branch note verbatim, the table, the chart and the
        /// two buttons that reproduce them elsewhere.</summary>
        public void ApplyPartition(DeNovoPartition outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));

            LastPartition = outcome;

            // Verbatim, refusals included. Reducing a refusal to "unavailable" would hide which source
            // could not answer, and that is the fact the reader needs.
            TextBlockBranchNote.Text = outcome.BranchNote;

            if (!outcome.IsAvailable)
            {
                DataGridPartition.ItemsSource = null;
                BorderPartitionChart.Child = null;
                ApplyPartitionColumnTooltips(null);
                UpdateLayerStates();

                SetStatus("No partition was drawn, because no source could give the PatternLab quants " +
                          "one run at a time. What the sources said is shown in Layer 5 word for word. " +
                          "Next step: give the .xic2 of the same extraction, which still holds every " +
                          "run's quants separately.");
                return;
            }

            var display = new List<ThreeWayPartitionRow>(outcome.Rows);
            if (outcome.Grouped != null) display.Add(outcome.Grouped);

            DataGridPartition.ItemsSource = display;
            ApplyPartitionColumnTooltips(outcome.Rows.Count > 0 ? outcome.Rows[0] : null);

            BorderPartitionChart.Child = new ThreeWayChart(outcome.Rows)
            {
                ToolTip = "Each bar is one run's total ion current, so the three segments add up to " +
                          "100%. The PatternLab and de novo areas both use the M+0 peak, so they " +
                          "compare fairly. The bar at the bottom is all runs together."
            };

            UpdateLayerStates();

            ThreeWayPartitionRow summary = outcome.Grouped ?? outcome.Rows[0];
            bool anyNegative = outcome.Rows.Any(r => r.NegativeRemainder);

            SetStatus(
                "Showing the ion current of " + outcome.Rows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " run(s) split three ways. Over all of them together, PatternLab explains " +
                summary.SharePatternLabPercent.ToString("N4", CultureInfo.CurrentCulture) +
                "%, the de novo set adds " +
                summary.ShareDeNovoPercent.ToString("N4", CultureInfo.CurrentCulture) +
                "%, and " + summary.ShareRemainderPercent.ToString("N4", CultureInfo.CurrentCulture) +
                "% is still without an owner. " + (anyNegative
                    ? "At least one run has a NEGATIVE remainder: there the two numerators together " +
                      "claim more signal than the run holds, which means signal is being counted twice " +
                      "or the totals do not belong to the same acquisition. It is reported with its " +
                      "sign and never clamped. "
                    : string.Empty) +
                "Next step: save the CSV, or open the chart in a window of its own.");
        }

        /// <summary>The partition table. The save button's only path.</summary>
        public void SavePartitionCsv(string path)
        {
            if (LastPartition == null || !LastPartition.IsAvailable) throw new InvalidOperationException(
                "There is no partition to save; build one in Layer 5 first.");

            // The per-run rows only: BuildCsv computes the aggregate itself, so a file can never carry
            // one that does not belong to the rows above it.
            ThreeWayPartition.WriteCsv(path, LastPartition.Rows);
        }

        private void ButtonSavePartitionCsv_Click(object sender, RoutedEventArgs e)
        {
            if (LastPartition == null || !LastPartition.IsAvailable)
            {
                SetStatus("Nothing was saved: no partition has been built, so there are no numbers to " +
                          "write. Next step: press \"Build partition\" in Layer 5 first.");
                return;
            }

            Export("three_way_partition.csv", "Save the three-way partition as CSV",
                   SavePartitionCsv,
                   "the three components with the provenance of each, the three shares, the " +
                   "negative-remainder flag and the de novo envelope diagnostic, aggregate line last");
        }

        private void ButtonOpenPartitionWindow_Click(object sender, RoutedEventArgs e)
        {
            if (LastPartition == null || !LastPartition.IsAvailable)
            {
                SetStatus("Nothing was opened: no partition has been built, so there would be nothing " +
                          "to show. Next step: press \"Build partition\" in Layer 5 first.");
                return;
            }

            // A harness has no screen to put a window on, and opening one there would be a window
            // nothing ever closes. The same guard the rest of this program uses for a modal box.
            if (PresentationSource.FromVisual(this) == null)
            {
                SetStatus("The detached window was not opened, because this screen is not on a display. " +
                          "The chart in Layer 5 shows the same rows. Next step: nothing.");
                return;
            }

            ThreeWayWindow.Show("Three-way partition of the ion current", LastPartition.Rows,
                                Window.GetWindow(this), LastPartition.BranchNote);

            SetStatus("The chart is now also in a window of its own, with its caption and its own save " +
                      "button. It is a second view of the same rows: closing it changes nothing here. " +
                      "Next step: nothing.");
        }

        // ===================================================================== the screen as a whole

        /// <summary>
        /// Every Layer at once, for the length of a long operation. THE ONE BUSY DISCIPLINE of this
        /// screen: all five long operations (import, reconcile, extract, search, partition) disable
        /// all five Layers here and re-enable them in a finally, after which
        /// <see cref="UpdateLayerStates"/> decides what is actually allowed.
        ///
        /// All five rather than the ones that are working, for two reasons. Pressing Extract while a
        /// homology search is running would put two RAW-sized jobs and one 3 GB acquisition in this
        /// process at the same time. And an operation of a LATER Layer started against rows an
        /// in-flight import or reconciliation is about to replace would land after them, so its table
        /// would describe rows that are no longer on screen, which is exactly the staleness the
        /// Clear* methods exist to prevent.
        /// </summary>
        private void SetScreenEnabled(bool enabled)
        {
            GroupBoxImportAndFilter.IsEnabled = enabled;
            GroupBoxReconcile.IsEnabled = enabled;
            GroupBoxExtractXic.IsEnabled = enabled;
            GroupBoxHomology.IsEnabled = enabled;
            GroupBoxPartition.IsEnabled = enabled;
        }

        // ===================================================================== headers

        /// <summary>
        /// A tooltip on every column of the review queue. The columns are the evidence a person judges a
        /// overlapping alternative with, and a heading like "XCorr" or "ppm apart" says nothing about how to weigh it.
        /// </summary>
        private void ApplyReviewColumnTooltips()
        {
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

        /// <summary>
        /// A tooltip on every column of the extraction table. The two grains are the whole point: a
        /// heading like "with signal" beside "distinct peaks" reads like two names for one number, and
        /// they are not.
        /// </summary>
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

        /// <summary>A tooltip on every column of the family table. What is counted, and what is
        /// deliberately not counted, are both decisions rather than details.</summary>
        private void ApplyHomologyColumnTooltips()
        {
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

        /// <summary>
        /// A tooltip on every column of the partition table, carrying the PROVENANCE of the column as
        /// the rows actually report it.
        ///
        /// Written from the row rather than from a constant: the PatternLab segment comes from the .plp
        /// on one project and from the .xic2 on another, and a heading that named the wrong one would
        /// be describing a measurement that did not happen.
        /// </summary>
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

        /// <summary>
        /// Offers a Family Cluster File path to Layer 4, without overwriting one already typed there.
        ///
        /// Called by the main window when the de novo screen is opened, so that a session that had a
        /// family file open does not have to find the same file again. It is only a suggestion: nothing
        /// is read until Search is pressed.
        /// </summary>
        public void SuggestFamilyClusterFile(string? path)
        {
            if (TextBoxHomologyFcf == null) return;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (TextBoxHomologyFcf.Text.Trim().Length > 0) return;

            TextBoxHomologyFcf.Text = path;
        }

        // ===================================================================== the help page

        /// <summary>
        /// This screen's own help, built from the criteria IN FORCE rather than from the boxes, so the
        /// numbers it quotes are the ones the rows on screen were kept by.
        /// </summary>
        public string HelpText() => HelpContent.BuildDeNovo(Criteria, ActiveDeNovoSource);

        /// <summary>Also reachable from the main window's Help menu while this screen is on display,
        /// which calls this.</summary>
        public void ShowHelp()
        {
            // A harness has no display to put a window on, and a window opened there is one nothing
            // ever closes. The same guard the detached chart uses.
            if (PresentationSource.FromVisual(this) == null)
            {
                SetStatus("The help window was not opened, because this screen is not on a display. " +
                          "Next step: nothing.");
                return;
            }

            // Not wrapped: the verdict list and the provenance list are aligned columns, and reflowing
            // them at the window's width would take that alignment apart.
            TextReportWindow.Show("FamilyQuant · De novo evidence", HelpText(),
                                  Window.GetWindow(this));
        }

        private void ButtonDeNovoHelp_Click(object sender, RoutedEventArgs e)
        {
            ShowHelp();
        }

        /// <summary>
        /// A per-column header tooltip. DataGridColumn is not a control, so the tooltip has to be
        /// attached through the header's style; a Style that sets two properties inherits its template
        /// and everything else from the theme, so the headers keep their normal appearance.
        /// </summary>
        private static Style HeaderTip(string tooltip)
        {
            var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));

            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, tooltip));
            style.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 60000));

            return style;
        }
    }
}
