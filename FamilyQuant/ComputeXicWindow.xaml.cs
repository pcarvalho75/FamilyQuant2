using FamilyQuant.DeNovo;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant
{
    /// <summary>
    /// The "Compute / update full XIC" action: pick the RAW folder, the identifications and, optionally,
    /// one or more selected de novo exports, then re-extract every peptide from the acquisitions with the full isotope
    /// envelope and hold the result so a project Save persists it.
    ///
    /// It follows the De Novo screen's discipline. The long extraction runs off the UI thread with the
    /// window disabled and re-enabled in a finally; every file dialog is guarded by a display check, so
    /// nothing here hangs a headless run; and a refusal is a status line, never a modal box.
    ///
    /// It computes and stores the raw per-peptide, per-isotope quant, the per-run TIC and the parameters.
    /// When curation is available it also annotates the spectrum audit table with every matching User
    /// family; this does not alter the quant values or precompute the later family rollups.
    /// </summary>
    public partial class ComputeXicWindow : Window
    {
        private readonly string _appVersion;
        private readonly FamilyClusterFile? _familyCuration;

        /// <summary>The rows the last successful computation produced, or null when none has finished.
        /// The caller reads this after the window closes; it is the store's new quant part.</summary>
        public List<QuantRow>? ResultRows { get; private set; }

        /// <summary>The manifest of the last successful computation, paired with <see cref="ResultRows"/>.</summary>
        public QuantManifest? ResultManifest { get; private set; }

        /// <summary>True once a computation has finished and its numbers are held here.</summary>
        public bool Computed => ResultRows != null && ResultManifest != null;

        /// <summary>
        /// The source-resolved spectrum table paired with the last successful computation. It is built
        /// by the core at the same time as <see cref="ResultRows"/>, so the grid never attempts a second,
        /// UI-specific join.
        /// </summary>
        public IReadOnlyList<SpectrumComparisonRow> SpectrumComparisonRows { get; private set; } =
            Array.Empty<SpectrumComparisonRow>();

        /// <summary>The automatic CSV written by the completed computation, when available.</summary>
        public string? SpectrumComparisonCsvPath { get; private set; }

        /// <summary>The automatic-CSV warning, when export failed after quantitation completed.</summary>
        public string? SpectrumComparisonCsvError { get; private set; }

        /// <summary>The share of the TIC explained, per run, from the last successful computation, so the
        /// caller's confirmation can lead with it too.</summary>
        public IReadOnlyList<string> TicShareSummary { get; private set; } = Array.Empty<string>();

        public ComputeXicWindow(string appVersion, QuantManifest? existingManifest = null,
                                FamilyClusterFile? familyCuration = null)
        {
            InitializeComponent();

            _appVersion = appVersion ?? string.Empty;
            _familyCuration = familyCuration;

            // The two settable rules start at their production defaults. They are read back at Compute
            // time, so whatever is on screen is what the stored quant records having been made with.
            IntegerUpDownIsotopes.Value = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount;
            IntegerUpDownMinPoints.Value = DeNovoXicExtractor.DefaultMinPointsForQuant;

            RestoreDeNovoSelection(existingManifest);

            IntegerUpDownIsotopes.ValueChanged += (_, __) => UpdateBasisLine();
            IntegerUpDownMinPoints.ValueChanged += (_, __) => UpdateBasisLine();
            UpdateBasisLine();
            UpdateDeNovoCutoffState();

            SetStatus(
                "Nothing computed yet. Choose the folder holding the .raw acquisitions and the SEPro " +
                "identification package, add a de novo export root if you have one, select its source, then press " +
                "\"Compute / update full XIC\". The acquisitions are read one at a time and this can take " +
                "a while on the first run of each; a second computation is answered from the cache.");
        }

        private string SelectedDeNovoMode =>
            (ComboBoxDeNovoSource?.SelectedItem as ComboBoxItem)?.Tag as string ?? "peaks-only";

        private void RestoreDeNovoSelection(QuantManifest? manifest)
        {
            List<DeNovoInputManifest> inputs = manifest?.DeNovoInputs ?? new List<DeNovoInputManifest>();
            var ids = new HashSet<string>(inputs.Select(i => i.SourceId), StringComparer.OrdinalIgnoreCase);

            bool hasOnly = ids.Contains(DeNovoSource.DenovoOnly.Id);
            bool hasAll = ids.Contains(DeNovoSource.DeNovoAll.Id);
            bool hasDeepNovo = ids.Contains(DeNovoSource.DeepNovo.Id);
            string tag = hasDeepNovo && hasOnly
                ? "union-only-deepnovo"
                : hasDeepNovo && hasAll
                    ? "union-all-deepnovo"
                    : hasDeepNovo
                    ? "deepnovo"
                    : hasAll ? "peaks-all" : "peaks-only";

            foreach (ComboBoxItem item in ComboBoxDeNovoSource.Items)
            {
                if (string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
                {
                    ComboBoxDeNovoSource.SelectedItem = item;
                    break;
                }
            }

            DeNovoInputManifest? peaks = inputs.FirstOrDefault(i =>
                string.Equals(i.SourceId, DeNovoSource.DenovoOnly.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.SourceId, DeNovoSource.DeNovoAll.Id, StringComparison.OrdinalIgnoreCase));
            DeNovoInputManifest? deep = inputs.FirstOrDefault(i =>
                string.Equals(i.SourceId, DeNovoSource.DeepNovo.Id, StringComparison.OrdinalIgnoreCase));
            if (peaks != null)
                TextBoxPeaksMinimumAlc.Text = peaks.MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture);
            if (deep != null)
                TextBoxDeepNovoMinimumScore.Text = deep.MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void ComboBoxDeNovoSource_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateDeNovoCutoffState();

        private void UpdateDeNovoCutoffState()
        {
            if (TextBoxPeaksMinimumAlc == null || TextBoxDeepNovoMinimumScore == null) return;
            string mode = SelectedDeNovoMode;
            bool union = mode == "union-only-deepnovo" || mode == "union-all-deepnovo";
            TextBoxPeaksMinimumAlc.IsEnabled = mode == "peaks-only" || mode == "peaks-all" || union;
            TextBoxDeepNovoMinimumScore.IsEnabled = mode == "deepnovo" || union;
        }

        private bool TryBuildDeNovoSelections(out IReadOnlyList<DeNovoSourceSelection> selections)
        {
            selections = Array.Empty<DeNovoSourceSelection>();

            bool ReadCutoff(TextBox box, string field, out double value)
            {
                if (double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                    || double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    if (value >= 0 && value <= 100) return true;
                }
                SetStatus("Nothing was computed: " + field + " must be a number from 0 to 100. " +
                    "PEAKS ALC and DeepNovo SCORE are separate cutoffs; neither is converted into the other.");
                return false;
            }

            string mode = SelectedDeNovoMode;
            var list = new List<DeNovoSourceSelection>();
            bool unionOnly = mode == "union-only-deepnovo";
            bool unionAll = mode == "union-all-deepnovo";
            if (mode == "peaks-only" || mode == "peaks-all" || unionOnly || unionAll)
            {
                if (!ReadCutoff(TextBoxPeaksMinimumAlc, "PEAKS minimum ALC", out double alc)) return false;
                list.Add(new DeNovoSourceSelection(
                    mode == "peaks-only" || unionOnly
                        ? DeNovoSource.DenovoOnly : DeNovoSource.DeNovoAll,
                    alc, 0));
            }
            if (mode == "deepnovo" || unionOnly || unionAll)
            {
                if (!ReadCutoff(TextBoxDeepNovoMinimumScore, "DeepNovo minimum SCORE", out double score)) return false;
                list.Add(new DeNovoSourceSelection(DeNovoSource.DeepNovo, score, list.Count));
            }

            selections = list;
            return true;
        }

        private void SetStatus(string message)
        {
            if (TextBlockStatus == null) return;
            TextBlockStatus.Text = message;
        }

        /// <summary>How many isotopes the next computation will credit, from the spinner. A cleared box
        /// falls back to the production ceiling rather than to zero, which would credit nothing.</summary>
        public int IsotopeCount =>
            IntegerUpDownIsotopes?.Value is int n && n >= 1 ? n : DeNovoXicExtractor.DefaultEnvelopeIsotopeCount;

        /// <summary>The minimum points per peak the next computation will apply, per isotope. A cleared box
        /// falls back to the production rule rather than to zero, which is not a rule at all.</summary>
        public int MinPointsForQuant =>
            IntegerUpDownMinPoints?.Value is int n && n >= 1 ? n : DeNovoXicExtractor.DefaultMinPointsForQuant;

        /// <summary>The line under the settings, restated whenever one of them changes so the window never
        /// describes a basis other than the one it would compute at.</summary>
        private void UpdateBasisLine()
        {
            if (TextBlockBasis == null) return;

            int isotopes = IsotopeCount;
            int minPoints = MinPointsForQuant;

            TextBlockBasis.Text =
                "Isotope basis: up to " + isotopes.ToString(CultureInfo.CurrentCulture) +
                " isotope(s) (M+0 to M+" + (isotopes - 1).ToString(CultureInfo.CurrentCulture) +
                "), a ceiling: an isotope that is not there is not credited and nothing is filled in for it. " +
                "Peaks are quantified from " + minPoints.ToString(CultureInfo.CurrentCulture) +
                " point(s) up" +
                (minPoints <= DeNovoXicExtractor.LegacyMinPointsForQuant
                    ? " (the LEGACY rule: a single-point peak is credited its raw height, which is not an " +
                      "integrated area and is not comparable with one)"
                    : "; anything thinner is reported as identified but not quantified rather than dropped") +
                ". Window " + DeNovoXicExtractor.ProductionPpm.ToString("0.##", CultureInfo.CurrentCulture) +
                " ppm, MS1 tolerance " + DeNovoXicExtractor.ProductionMs1Tolerance.ToString(CultureInfo.CurrentCulture) + ".";
        }

        /// <summary>Whether a modal dialog may be raised at all: a file dialog opened off a display is a
        /// window nothing can close, which in a harness is a hang rather than an error. Same guard the De
        /// Novo screen uses.</summary>
        private bool CanShowDialog(string refusal, string nextStep)
        {
            if (PresentationSource.FromVisual(this) != null) return true;

            SetStatus(refusal + ", because this window is not on a display and a file dialog there is a " +
                      "window nothing can close. Next step: " + nextStep);
            return false;
        }

        private void ButtonBrowseRaw_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No folder was chosen",
                               "type the folder holding the .raw acquisitions into the box beside the button.")) return;

            var ofd = new OpenFolderDialog { Title = "Choose the folder holding the .raw acquisitions" };
            if (ofd.ShowDialog() != true) return;

            TextBoxRawDirectory.Text = ofd.FolderName;
        }

        private void ButtonBrowseSepro_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No SEPro package was chosen",
                               "type the path of the .sepr2 into the box beside the button.")) return;

            var ofd = new OpenFileDialog
            {
                Filter = "SEPro 2.0 (*.sepr2)|*.sepr2",
                Title = "Choose a TRUSTED SEPro package (legacy unsafe deserialization)",
            };
            if (ofd.ShowDialog() != true) return;

            TextBoxSeproPackage.Text = ofd.FileName;
        }

        private void ButtonBrowseDeNovo_Click(object sender, RoutedEventArgs e)
        {
            if (!CanShowDialog("No folder was chosen",
                               "type the de novo export root into the box beside the button, or leave it empty.")) return;

            var ofd = new OpenFolderDialog { Title = "Choose the de novo export root (optional)" };
            if (ofd.ShowDialog() != true) return;

            TextBoxDeNovoDirectory.Text = ofd.FolderName;
        }

        private async void ButtonCompute_Click(object sender, RoutedEventArgs e)
        {
            string rawDir = TextBoxRawDirectory.Text.Trim();
            string sepr2 = TextBoxSeproPackage.Text.Trim();
            string deNovo = TextBoxDeNovoDirectory.Text.Trim();

            if (rawDir.Length == 0 || !Directory.Exists(rawDir))
            {
                SetStatus("Nothing was computed: " + (rawDir.Length == 0
                    ? "no RAW folder has been chosen yet."
                    : "the folder \"" + rawDir + "\" does not exist or cannot be reached.") +
                    " Next step: choose the folder holding the .raw acquisitions.");
                return;
            }

            if (sepr2.Length == 0 || !File.Exists(sepr2))
            {
                SetStatus("Nothing was computed: " + (sepr2.Length == 0
                    ? "no SEPro identification package has been chosen yet."
                    : "the file \"" + sepr2 + "\" does not exist or cannot be reached.") +
                    " Next step: choose the .sepr2 of the same runs as the acquisitions.");
                return;
            }

            if (deNovo.Length > 0 && !Directory.Exists(deNovo))
            {
                SetStatus("Nothing was computed: the de novo export folder \"" + deNovo +
                          "\" does not exist or cannot be reached. Next step: choose the de novo export " +
                          "folder, or clear the box to store only the identified peptides.");
                return;
            }

            string? deNovoDir = deNovo.Length == 0 ? null : deNovo;

            if (!TryBuildDeNovoSelections(out IReadOnlyList<DeNovoSourceSelection> selections)) return;

            var options = new FullXicOptions
            {
                IsotopeCount = IsotopeCount,
                MinPointsForQuant = MinPointsForQuant,
                Ppm = DeNovoXicExtractor.ProductionPpm,
                Ms1Tolerance = DeNovoXicExtractor.ProductionMs1Tolerance,
                AppVersion = _appVersion,
                DeNovoSelections = selections,
                FamilyCuration = _familyCuration,
            };

            IsEnabled = false;
            SetStatus("Computing... reading the identifications and extracting each run one at a time. The " +
                      "first computation of each acquisition loads its MS1 spectra, which takes tens of " +
                      "seconds; anything already cached is answered without opening the file.");

            try
            {
                Action<string> progress = m => Dispatcher.BeginInvoke(new Action(() => SetStatus("Computing: " + m)));

                FullXicComputeResult outcome = await Task.Run(() =>
                    FullXicComputer.ComputeFullXic(rawDir, sepr2, deNovoDir, options, null, progress));

                ResultRows = outcome.Rows;
                ResultManifest = outcome.Manifest;
                SpectrumComparisonRows = outcome.SpectrumComparisonRows;
                SpectrumComparisonCsvPath = outcome.SpectrumComparisonCsvPath;
                SpectrumComparisonCsvError = outcome.SpectrumComparisonCsvError;
                TicShareSummary = outcome.TicShareSummary;
                ButtonViewSpectrumComparison.IsEnabled = SpectrumComparisonRows.Count > 0;
                TextBlockSpectrumComparisonPath.Text = !string.IsNullOrWhiteSpace(SpectrumComparisonCsvError)
                    ? SpectrumComparisonCsvError
                    : string.IsNullOrWhiteSpace(SpectrumComparisonCsvPath)
                        ? "The spectrum table is available in the grid; no automatic CSV path was returned."
                        : "CSV: " + SpectrumComparisonCsvPath;

                SetStatus(
                    "Computed " + outcome.Rows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " quant row(s): " + outcome.IdentifiedRows.ToString("N0", CultureInfo.CurrentCulture) +
                    " identified and " + outcome.DeNovoRows.ToString("N0", CultureInfo.CurrentCulture) +
                    " de novo, across " + outcome.Manifest.PerRunTic.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " run(s), at the " + outcome.Manifest.IsotopeCount.ToString(CultureInfo.CurrentCulture) +
                    "-isotope envelope ceiling, quantifying peaks of " +
                    outcome.Manifest.MinPointsForQuant.ToString(CultureInfo.CurrentCulture) +
                    " point(s) or more.\n\n" +
                    // The explained share first: the per-run notes below carry the isotope-series
                    // diagnostic, which was being read as the share explained.
                    (outcome.TicShareSummary.Count > 0
                        ? string.Join("\n", outcome.TicShareSummary) + "\n\nDetails:\n"
                        : string.Empty) +
                    string.Join("\n", outcome.Notes) +
                    (outcome.Manifest.DeNovoInputs.Count > 0
                        ? "\n\nDe novo inputs: " + string.Join("; ", outcome.Manifest.DeNovoInputs
                            .GroupBy(i => i.SourceId, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.First().SourceDisplayName + " (" +
                                g.First().CutoffDescription + ", " +
                                g.Count().ToString(CultureInfo.CurrentCulture) + " file(s))"))
                        : string.Empty) +
                    (string.IsNullOrWhiteSpace(outcome.SpectrumComparisonCsvPath)
                        ? string.Empty
                        : "\n\nSpectrum comparison CSV: " + outcome.SpectrumComparisonCsvPath) +
                    "\n\nNext step: close this window. The numbers are now held by the project; a Save " +
                    "writes them into the .fqproj.");
            }
            catch (FullXicNoMatchedRunsException ex)
            {
                // A refusal, not a failure: the message already lists the RAWs and runs on both sides and
                // says what to rename. ResultRows is left as it was, so the project's quant is not replaced.
                SetStatus(ex.Message);
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The computation did not finish, so nothing was stored. Reason given by the program: " +
                    ex.Message + " Next step: check that the RAW folder really holds these runs' " +
                    "acquisitions, that the .sepr2 is of the same runs, and that neither is open in " +
                    "another program, then press Compute again.");
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void ButtonViewSpectrumComparison_Click(object sender, RoutedEventArgs e)
        {
            if (SpectrumComparisonRows.Count == 0)
            {
                SetStatus("There is no spectrum table to show yet. Next step: complete the full XIC " +
                          "computation first; the table is built from that exact result.");
                return;
            }

            SpectrumComparisonWindow.Show(SpectrumComparisonRows, this, SpectrumComparisonCsvPath,
                ResultManifest?.DeNovoAcceptedScoreScatter);
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            // DialogResult can only be set on a window shown with ShowDialog. When it was, this returns
            // true to the caller; otherwise just close. The caller reads ResultRows regardless.
            try { DialogResult = true; }
            catch (InvalidOperationException) { Close(); }
        }
    }
}
