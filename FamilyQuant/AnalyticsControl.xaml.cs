using FamilyQuant.DeNovo;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>
    /// The Analytics screen (Q4): the graphs Ana and the owner asked for, read from FamilyQuant's own
    /// full-envelope quantitation - the Q2 per-peptide store and the Q3 fractions - never the .plp or the
    /// .xic2. Five charts, each a house-style FrameworkElement of its own, plus a CSV export of the table
    /// behind them and a PNG export of the figures themselves.
    ///
    /// IT ONLY READS. The analytics are derived from quant rows already in memory (the loaded project's
    /// store), so no RAW is re-opened and nothing is recomputed here beyond the cheap per-peptide collapse.
    /// The one thing that would recompute - a full XIC - is a different screen (Compute / update full XIC);
    /// this one refuses with a clear message when there is no computed quantitation to read.
    ///
    /// THE ONE EXCEPTION, and it is not a recomputation of any measurement: when a curated Family Cluster
    /// File is handed in, the per-family envelope table behind the family-contribution chart is built by
    /// the homology search (<see cref="FamilyEnvelopeQuant.BuildFromCuration"/>), because attributing a
    /// peptide to a family is an alignment and the store does not carry one. It costs seconds on a real
    /// database, reads no RAW, and changes no family, User ID or quantitation.
    /// </summary>
    public partial class AnalyticsControl : UserControl
    {
        /// <summary>The deduped table and the Q3 fractions, or null in the empty state.</summary>
        private AnalyticsData? data;

        /// <summary>The per-family envelope table, or null when no curation was handed in (or the
        /// homology pass could not run). The chart draws its own empty state in that case.</summary>
        private FamilyEnvelopeQuantResult? families;
        private QuantManifest? loadedManifest;
        private FamilyClusterFile? loadedCuration;

        /// <summary>Set once the XAML is loaded, so the selectors' change handlers do not try to redraw
        /// while InitializeComponent is still installing the default selection.</summary>
        private bool ready;

        public AnalyticsControl(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest)
            : this(rows, manifest, null)
        {
        }

        /// <param name="rows">The loaded project's stored per-peptide quant.</param>
        /// <param name="manifest">The manifest paired with it, for the per-run TIC and the Q6 estimate.</param>
        /// <param name="curation">The curated family file on which the family-contribution chart stands,
        /// or null: with none, that chart draws its own "open a Family Cluster File" state.</param>
        public AnalyticsControl(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest,
                                FamilyClusterFile? curation)
            : this(rows, manifest, curation, null)
        {
        }

        /// <param name="attribution">The peptide-to-family attribution of the same quant on the same
        /// curation, when the caller already holds it (the curation screen's), so this screen does not run
        /// a second homology search. Null to search here.</param>
        public AnalyticsControl(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest,
                                FamilyClusterFile? curation, PeptideFamilyAttribution? attribution)
        {
            InitializeComponent();
            ready = true;
            heldAttribution = attribution;
            Build(rows, manifest, curation);
        }

        private PeptideFamilyAttribution? heldAttribution;

        /// <summary>The analytics as the tests read them, or null when the screen is in its empty state.</summary>
        public AnalyticsData? Data => data;

        /// <summary>The per-family envelope table behind the family chart, as the tests read it.</summary>
        public FamilyEnvelopeQuantResult? Families => families;

        /// <summary>How many families get a bar of their own, from the selector. Zero means "all".</summary>
        public int FamilyTopN
        {
            get
            {
                var item = ComboBoxFamilyTopN.SelectedItem as ComboBoxItem;
                string tag = item?.Tag as string ?? string.Empty;
                return int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                    ? n
                    : FamilyContributionPlot.DefaultTopN;
            }
        }

        /// <summary>Whether the family bars are labelled as shares of the whole measured signal.</summary>
        public bool FamilyAsPercent => CheckBoxFamilyPercent.IsChecked == true;

        private void Build(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest, FamilyClusterFile? curation)
        {
            PanelCharts.Children.Clear();
            loadedManifest = manifest;
            loadedCuration = curation;

            if (rows == null || rows.Count == 0)
            {
                ShowEmptyState();
                return;
            }

            IReadOnlyDictionary<string, double> tic = manifest?.PerRunTic
                ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            data = AnalyticsBuilder.Build(rows, tic);

            // Q6: the detected peptide-like estimate is not recomputed here (this screen only reads); it
            // is injected from the manifest's derived section, which the compute path measured and stored,
            // onto the fractions the recovery chart draws.
            if (manifest?.DerivedFractions != null && manifest.DerivedFractions.Count > 0)
                NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped, manifest.DerivedFractions);

            families = BuildFamilies(rows, curation, heldAttribution);

            BorderEmpty.Visibility = Visibility.Collapsed;
            ScrollAnalytics.Visibility = Visibility.Visible;
            ButtonExportPeptides.IsEnabled = true;
            ButtonExportSummary.IsEnabled = true;
            ButtonSaveFigures.IsEnabled = true;
            ButtonSavePresentationFigures.IsEnabled = manifest != null && curation != null && families != null;
            ComboBoxFamilyTopN.IsEnabled = true;
            CheckBoxFamilyPercent.IsEnabled = true;

            TextBlockStatus.Text = SummaryLine(data, families, curation, manifest);

            DrawCharts();
        }

        /// <summary>
        /// The per-family envelope table, or null. A curation with no proteins, or a homology pass that
        /// throws, leaves the chart in its empty state rather than taking the whole screen down with it:
        /// the other four graphs do not depend on the families at all.
        /// </summary>
        private static FamilyEnvelopeQuantResult? BuildFamilies(IReadOnlyList<QuantRow> rows,
                                                                FamilyClusterFile? curation,
                                                                PeptideFamilyAttribution? attribution)
        {
            if (curation == null) return null;

            try
            {
                return attribution != null
                    ? FamilyEnvelopeQuant.BuildFromAttribution(rows, attribution)
                    : FamilyEnvelopeQuant.BuildFromCuration(rows, curation);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Rebuilds the stacked charts from the analytics already in memory. Cheap: it re-reads
        /// nothing and re-runs no homology, it only draws.</summary>
        private void DrawCharts()
        {
            PanelCharts.Children.Clear();
            if (data == null) return;

            foreach (AnalyticsFigure figure in
                     AnalyticsFigures.Build(data, families, FamilyTopN, FamilyAsPercent))
            {
                AddChart(figure.Chart, figure.ScreenHeight);
            }
        }

        private void ShowEmptyState()
        {
            data = null;
            families = null;
            ScrollAnalytics.Visibility = Visibility.Collapsed;
            BorderEmpty.Visibility = Visibility.Visible;
            ButtonExportPeptides.IsEnabled = false;
            ButtonExportSummary.IsEnabled = false;
            ButtonSaveFigures.IsEnabled = false;
            ButtonSavePresentationFigures.IsEnabled = false;
            ComboBoxFamilyTopN.IsEnabled = false;
            CheckBoxFamilyPercent.IsEnabled = false;

            TextBlockStatus.Text = string.Empty;
            TextBlockEmpty.Text =
                "There is no quantitation to analyse yet. The analytics read FamilyQuant's own full " +
                "isotope-envelope quant, which is produced by File · Compute / update full XIC after a " +
                "project is loaded. Compute the full XIC first, then return here.\n\n" +
                "A project opened from a plain .fcf carries no quantitation, and a .fqproj that was saved " +
                "before it was ever computed carries none either. In both cases the same step applies: run " +
                "Compute / update full XIC, then this screen will draw the graphs.";
        }

        private void AddChart(FrameworkElement chart, double height)
        {
            var host = new Border
            {
                Height = height,
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x39, 0x49)),
                Child = chart,
            };
            PanelCharts.Children.Add(host);
        }

        private static string SummaryLine(AnalyticsData d, FamilyEnvelopeQuantResult? fam,
                                          FamilyClusterFile? curation, QuantManifest? manifest)
        {
            string familyClause = fam != null && fam.Families.Count > 0
                ? " The family chart stands on " +
                  fam.Families.Count.ToString("N0", CultureInfo.CurrentCulture) +
                  " family(ies) of the open grouping, reached by homology; families below the cut are " +
                  "folded into one bar and the signal that reached no family is drawn apart. Its bars count a " +
                  "peptide shared by several families in each of them; counted once instead, the peptides " +
                  "shared between families are " +
                  fam.SharedShare.ToString("P2", CultureInfo.CurrentCulture) + " of the quantified signal and " +
                  fam.UnattributedShare.ToString("P2", CultureInfo.CurrentCulture) + " reached no family (the " +
                  "evidence pack's family_composition.csv uses this basis; the curation screen's Full XIC column " +
                  "can show it too, with 'Family-unique only' and 'the quantified signal' chosen in Layer 3)."
                : curation == null
                    ? " No Family Cluster File is open beside this quantitation, so the family chart has " +
                      "nothing to draw: open the project's .fcf or .fqproj and return here."
                    : " No family of the open grouping was reached by any peptide, so the family chart is " +
                      "empty. Check that the curation and the quantitation come from the same database.";

            string identificationClause = IdentificationCountingClause(d, manifest);
            string sourceClause = DeNovoSourceClause(manifest);

            if (d.Grouped == null || !(d.Grouped.Tic > 0))
            {
                return "Read " + d.Peptides.Count.ToString("N0", CultureInfo.CurrentCulture) +
                       " peptide(s) after collapsing each to one representative peak. No total ion current " +
                       "is recorded, so the recovery shares cannot be taken; the mass and isotope graphs " +
                       "still read from the table." + sourceClause + identificationClause + familyClause;
            }

            NativeRunFraction g = d.Grouped;

            // The identified-but-not-quantified block is named even when it is zero. "Nothing was
            // excluded" and "exclusions were not reported" are different facts, and a reader has to be
            // able to tell them apart from the line alone.
            string underClause = string.Format(CultureInfo.CurrentCulture,
                " A further {0:P2} was identified but NOT quantified: {1:N0} peptide(s) whose peaks carried " +
                "too few points to integrate{2}. It is accounted for as its own block rather than left to " +
                "the remainder.",
                g.UnderSampledFraction,
                g.IdentifiedUnderSampledPeptides + g.DeNovoUnderSampledPeptides,
                g.UnderSampledIsEstimate
                    ? ", and part of that number is an ESTIMATE (a single-point peak has no width, so it is " +
                      "given the run's median scan spacing)"
                    : string.Empty);

            return string.Format(CultureInfo.CurrentCulture,
                "Across {0} run(s): the identified (DDA) envelope explains {1:P2} of the total ion current, " +
                "the de novo envelope adds {2:P2}, and together they recover {3:P2}. " +
                "{4:N0} identified and {5:N0} de novo peptide(s), each counted once. Every number is " +
                "FamilyQuant's own full-envelope quant, deduped to one deterministic largest-M+0 " +
                "representative per run, evidence side, canonical PTM-aware peptidoform and charge, divided " +
                "by the raw TIC; nothing here is read from the .plp or the .xic2.",
                d.Fractions.Count,
                g.IdentifiedFractionEnvelope,
                g.DeNovoFractionEnvelope,
                g.RecoveredFractionEnvelope,
                g.IdentifiedPeptides,
                g.DeNovoPeptides) + sourceClause + identificationClause + underClause + familyClause;
        }

        private static string DeNovoSourceClause(QuantManifest? manifest)
        {
            if (manifest?.DeNovoInputs == null || manifest.DeNovoInputs.Count == 0) return string.Empty;

            string sources = string.Join(" + ", manifest.DeNovoInputs
                .GroupBy(i => i.SourceId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Min(i => i.SourcePriority))
                .Select(g => g.First().SourceDisplayName + " [" + g.First().CutoffDescription + "]"));
            return " De novo source provenance: " + sources + ". " +
                manifest.DeNovoInputs.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " input file(s) are fingerprinted in the project manifest" +
                (manifest.DeNovoUnionResolutionMethod.Length > 0
                    ? "; competing source calls share one XIC and retain all alternate evidence"
                    : string.Empty) + ".";
        }

        private static string IdentificationCountingClause(AnalyticsData d, QuantManifest? manifest)
        {
            DeNovoIdentificationSummary? s = manifest?.DeNovoIdentification;
            if (s == null) return string.Empty;

            return string.Format(CultureInfo.CurrentCulture,
                " Identification census (no XIC): {0:N0} accepted de novo spectrum calls become {1:N0} " +
                "distinct PTM-cleaned sequences carried forward after search reconciliation, pooled across " +
                "runs and charges. Quantification is a separate grain: {2:N0} post-XIC de novo run × sequence " +
                "× charge representatives; this is not a unique-peptide count.",
                s.FilteredSpectrumCalls,
                s.CarriedForwardDistinctSequencesIgnoringPtm,
                d.DeNovoCount);
        }

        // ----------------------------------------------------------------- the family chart's controls

        private void ComboBoxFamilyTopN_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready) return;
            DrawCharts();
        }

        private void CheckBoxFamilyPercent_Changed(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            DrawCharts();
        }

        // ----------------------------------------------------------------- export

        private void ButtonExportPeptides_Click(object sender, RoutedEventArgs e)
        {
            if (data == null) return;
            Export("familyquant_analytics_peptides.csv", "Save the per-peptide analytics table",
                   path => File.WriteAllText(path, AnalyticsCsv.BuildPeptideCsv(data.Peptides)),
                   "the per-peptide analytics table");
        }

        private void ButtonExportSummary_Click(object sender, RoutedEventArgs e)
        {
            if (data == null) return;
            Export("familyquant_analytics_summary.csv", "Save the per-run and grouped fraction summary",
                   path => File.WriteAllText(path, AnalyticsCsv.BuildSummaryCsv(data.Fractions, data.Grouped)),
                   "the per-run and grouped fraction summary");
        }

        /// <summary>
        /// Every graph on this screen, as a PNG in a folder the user picks. Guarded with PresentationSource
        /// for the same reason as the CSV exports: a folder dialog off a display is a window nothing can
        /// close, so the headless harness is refused in the status line instead of being hung.
        /// </summary>
        private void ButtonSaveFigures_Click(object sender, RoutedEventArgs e)
        {
            if (data == null) return;

            if (PresentationSource.FromVisual(this) == null)
            {
                TextBlockStatus.Text = "Nothing was saved, because this screen is not on a display and a " +
                    "folder dialog there is a window nothing can close. Next step: open the project in the " +
                    "application and save the figures from there.";
                return;
            }

            var dialog = new OpenFolderDialog
            {
                Title = "Choose the folder to write the figures into",
                Multiselect = false,
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                List<string> written = AnalyticsFigures.Save(data, families, dialog.FolderName,
                                                             FamilyTopN, FamilyAsPercent);

                TextBlockStatus.Text = "Written to " + dialog.FolderName + ": " +
                    written.Count.ToString("N0", CultureInfo.CurrentCulture) + " figure(s) - " +
                    string.Join(", ", written.Select(Path.GetFileName)) + ". Each is " +
                    ((int)(ChartImageExporter.DefaultLayoutWidth * ChartImageExporter.DefaultScale))
                        .ToString("N0", CultureInfo.CurrentCulture) + " by " +
                    ((int)(ChartImageExporter.DefaultLayoutHeight * ChartImageExporter.DefaultScale))
                        .ToString("N0", CultureInfo.CurrentCulture) +
                    " device pixels, which is crisp on a projector. Nothing on screen changed.";
            }
            catch (Exception ex)
            {
                TextBlockStatus.Text = "The figures could not be written, so nothing was saved. Reason " +
                    "given by the program: " + ex.Message + " Next step: check that you can write to that " +
                    "folder and that no PNG of the same name is open in another program, then save again.";
            }
        }

        private void ButtonSavePresentationFigures_Click(object sender, RoutedEventArgs e)
        {
            if (data == null || loadedManifest == null || loadedCuration == null || families == null) return;

            if (PresentationSource.FromVisual(this) == null)
            {
                TextBlockStatus.Text = "Nothing was saved, because this screen is not on a display and a " +
                    "folder dialog there is a window nothing can close. Open the complete project in the " +
                    "application and use Save presentation evidence pack from Analytics.";
                return;
            }

            var dialog = new OpenFolderDialog
            {
                Title = "Choose the folder for the reproducible presentation evidence pack",
                Multiselect = false,
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                PresentationEvidencePackResult written = PresentationEvidencePack.Save(
                    data, loadedManifest, loadedCuration, families, dialog.FolderName);
                TextBlockStatus.Text = "Written to " + dialog.FolderName + ": " +
                    written.FigurePaths.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " FamilyQuant plot(s) plus " +
                    written.DataPaths.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " numeric/definition file(s). The PNGs are 1920 by 1080 in the light projector palette; " +
                    "the CSV and JSON files contain the exact plotted numbers.";
            }
            catch (Exception ex)
            {
                TextBlockStatus.Text = "The complete presentation plot pack was not written. " + ex.Message +
                    " No partial claim is substituted. Load or recompute the named missing study, then try again.";
            }
        }

        /// <summary>
        /// The shared export path. A file dialog off a display is a window nothing can close, so when this
        /// screen is not on a display the export is refused in the status line instead, exactly as the de
        /// novo screen does. Guarded with PresentationSource so the headless harness never opens a dialog.
        /// </summary>
        private void Export(string suggestedName, string title, Action<string> write, string what)
        {
            if (PresentationSource.FromVisual(this) == null)
            {
                TextBlockStatus.Text = "Nothing was exported, because this screen is not on a display and a " +
                    "file dialog there is a window nothing can close. Next step: open the project in the " +
                    "application and export from there.";
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = suggestedName,
                Title = title,
                AddExtension = true,
                DefaultExt = ".csv",
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                write(sfd.FileName);
                TextBlockStatus.Text = "Written to " + sfd.FileName + ": " + what + ". Numbers use a dot for " +
                    "the decimal separator whatever your machine's language is, so any program can read them. " +
                    "Nothing on screen changed.";
            }
            catch (Exception ex)
            {
                TextBlockStatus.Text = "The file could not be written, so nothing was exported. Reason given " +
                    "by the program: " + ex.Message + " Next step: check that it is not already open in Excel " +
                    "and that you can write to that folder, then export again.";
            }
        }
    }
}
