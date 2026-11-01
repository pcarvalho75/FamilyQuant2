using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        /// <summary>
        /// Runs everything already queued on this thread's dispatcher, then returns.
        ///
        /// The suite has no message loop of its own, so an <c>await</c> inside a handler would never
        /// resume. The frame is ended from a Background-priority item, so every continuation already
        /// posted at Normal priority has run by the time this returns.
        /// </summary>
        private static void PumpDispatcher()
        {
            var frame = new DispatcherFrame();

            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => frame.Continue = false));

            Dispatcher.PushFrame(frame);
        }

        /// <summary>The tooltip a column's header style installs, which is where a DataGridColumn has to
        /// carry one because it is not a control itself.</summary>
        private static string? HeaderTooltipOf(DataGridColumn column)
        {
            if (column.HeaderStyle == null) return null;

            foreach (SetterBase setterBase in column.HeaderStyle.Setters)
            {
                if (setterBase is Setter setter && setter.Property == FrameworkElement.ToolTipProperty)
                    return setter.Value as string;
            }

            return null;
        }

        private static void UiSmokeTests(string? plpPath, string? databasePath, string workDir)
        {
            if (plpPath == null || databasePath == null || !File.Exists(plpPath) || !File.Exists(databasePath))
            {
                Console.WriteLine("  SKIP  UI smoke - pass a .plp and a database to run this section");
                skipped++;
                return;
            }

            EnsureApplication();

            string fcfPath = Path.Combine(workDir, "uismoke.fcf");
            USearchTools.GenerateFCF(new FileInfo(databasePath), 70).Save(fcfPath);

            ClusterGenerationControl control;
            try
            {
                control = new ClusterGenerationControl(fcfPath);
                Check("the cluster view builds from a real Family Cluster File", true);
            }
            catch (Exception ex)
            {
                Check("the cluster view builds from a real Family Cluster File", false,
                    ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var grid = UiField<DataGrid>(control, "DataGridResultGrid");
            var comboMode = UiField<ComboBox>(control, "ComboBoxNormalization");
            var comboShared = UiField<ComboBox>(control, "ComboBoxSharedPeptides");
            var hideContaminants = UiField<CheckBox>(control, "CheckBoxExcludeContaminants");
            var hideDecoys = UiField<CheckBox>(control, "CheckBoxExcludeReverse");
            var onlyQuantified = UiField<CheckBox>(control, "CheckBoxShowXICOnly");
            var familyCount = UiField<Label>(control, "LabelNumberOfGroups");
            var wordLabel = UiField<Label>(control, "LabelWordSimilarity");

            Check("every control the handlers reach exists",
                grid != null && comboMode != null && comboShared != null && hideContaminants != null
                && hideDecoys != null && onlyQuantified != null && familyCount != null);
            Check("the table is filled on load", grid!.ItemsSource != null);
            Check("the family counter is filled", familyCount!.Content?.ToString() != "?");
            Check("a fresh clustering reports no word reclustering", "none".Equals(wordLabel!.Content));

            // Drive the quantitation the way the Load PLP button does.
            var plp = new PatternLabProject(new FileInfo(plpPath));
            control.GetType().GetProperty("CurrentPLP", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(control, plp);
            control.fcf.SetLookupDictionary(plp);

            var profile = QuantitationProfile.Describe(plp);

            // Drive the REAL availability logic rather than hand-setting one item: this is the code the
            // Load PLP button runs, and it is what decides which of the four reporting modes the loaded
            // project can actually support.
            control.GetType().GetMethod("UpdateReportingModeAvailability", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(control, new object[] { profile });

            Check("the identified-signal mode is always available",
                ((ComboBoxItem)comboMode!.Items[0]).IsEnabled);
            Check("the TIC mode is enabled exactly when the profile says it is computable",
                ((ComboBoxItem)comboMode.Items[1]).IsEnabled == profile.CanReportTotalChromatogram);
            Check("every reporting mode carries a tooltip explaining itself",
                comboMode.Items.Cast<ComboBoxItem>().Skip(1).All(i => i.ToolTip != null));

            var recalculate = control.GetType().GetMethod("RecalculateQuantitationInternal",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var refresh = control.GetType().GetMethod("UpdateFilteredResults",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            for (int mode = 0; mode < comboMode.Items.Count; mode++)
            {
                // Skip what the UI itself greys out, instead of asserting a hard-coded mode list.
                if (!((ComboBoxItem)comboMode.Items[mode]).IsEnabled) continue;

                for (int shared = 0; shared <= 2; shared++)
                {
                    comboMode.SelectedIndex = mode;
                    comboShared!.SelectedIndex = shared;

                    try
                    {
                        recalculate.Invoke(control, null);
                        double sum = control.fcf.GroupedFastaItems.Sum(g => g.First().XIC);
                        Check($"recalculates with mode {mode} and shared-peptide policy {shared} (sum {sum:P2})", true);
                    }
                    catch (Exception ex)
                    {
                        Check($"recalculates with mode {mode} and shared-peptide policy {shared}", false,
                            (ex.InnerException ?? ex).Message);
                    }
                }
            }

            int RowCount() => VisibleRows(grid).Count;

            int all = RowCount();
            hideDecoys!.IsChecked = true; refresh.Invoke(control, null);
            int withoutDecoys = RowCount();
            hideContaminants!.IsChecked = true; refresh.Invoke(control, null);
            int withoutEither = RowCount();
            onlyQuantified!.IsChecked = true; refresh.Invoke(control, null);
            int quantifiedOnly = RowCount();

            Console.WriteLine($"        rows: {all} -> {withoutDecoys} without decoys -> " +
                              $"{withoutEither} without contaminants -> {quantifiedOnly} quantified");

            Check("hiding decoys removes rows", withoutDecoys < all && withoutDecoys > 0);
            Check("hiding contaminants removes more", withoutEither < withoutDecoys && withoutEither > 0);
            Check("the quantified filter narrows further", quantifiedOnly <= withoutEither && quantifiedOnly > 0);

            hideDecoys.IsChecked = false;
            hideContaminants.IsChecked = false;
            onlyQuantified.IsChecked = false;
            refresh.Invoke(control, null);

            Check("the export writes one line per visible row plus a header",
                CsvReport.Build(VisibleRows(grid)).Split('\n').Count(l => l.Trim().Length > 0) == all + 1);

            // Editing User ID regroups the families; XIC User must follow, or an export carries the new
            // grouping beside the old numbers.
            comboMode.SelectedIndex = 0;
            comboShared!.SelectedIndex = 0;
            recalculate.Invoke(control, null);

            // The three places that have to name the denominator, checked against a real project rather
            // than a fixture: the column headings, the permanent status line, and the family counter.
            var shareHeader = UiField<DataGridTextColumn>(control, "ColumnXicUser")!.Header as string;
            var statusLine = UiField<TextBlock>(control, "TextBlockStatusLine")!.Text;

            Console.WriteLine("        status line: " + statusLine);
            Console.WriteLine("        heading: " + shareHeader);
            Console.WriteLine("        counter: " + familyCount.Content);

            string expectedMode = NormalizationMode.IdentifiedSignal.ShortName();

            Check("after recalculating, the share column names the denominator that produced it",
                (shareHeader ?? "").Contains(expectedMode), shareHeader);
            Check("and it says which grouping it was computed over",
                (shareHeader ?? "").Contains("User families"), shareHeader);
            Check("the status line names the same denominator",
                statusLine.Contains(expectedMode), statusLine);
            Check("and the shared-peptide rule that was applied",
                statusLine.Contains(SharedPeptidePolicy.FamilyUnique.ShortName()), statusLine);
            Check("the family counter reports visible and in-file together",
                (familyCount.Content?.ToString() ?? "").Contains("visible")
                && (familyCount.Content?.ToString() ?? "").Contains("in file"),
                familyCount.Content?.ToString());

            // Hiding decoys must move the visible half and leave the in-file half alone - the exact
            // confusion this counter caused.
            string counterAll = familyCount.Content?.ToString() ?? "";
            hideDecoys.IsChecked = true; refresh.Invoke(control, null);
            string counterFiltered = familyCount.Content?.ToString() ?? "";
            hideDecoys.IsChecked = false; refresh.Invoke(control, null);

            Check("hiding decoys changes the visible count but not the file count",
                counterAll != counterFiltered
                && counterAll.Split('·')[1].Trim() == counterFiltered.Split('·')[1].Trim()
                && counterAll.Split('·')[0].Trim() != counterFiltered.Split('·')[0].Trim(),
                counterAll + "  ->  " + counterFiltered);

            var items = control.fcf.MyExtendedFastaItems;
            var quantifiedFamilies = items.Where(i => i.XICUser > 0).Select(i => i.UserID).Distinct().Take(2).ToList();

            if (quantifiedFamilies.Count == 2)
            {
                var kept = items.First(i => i.UserID == quantifiedFamilies[0]);
                double before = kept.XICUser;

                foreach (var i in items.Where(i => i.UserID == quantifiedFamilies[1]).ToList())
                {
                    i.UserID = quantifiedFamilies[0];
                }

                recalculate.Invoke(control, null);

                Check("merging two quantified families through User ID updates XIC User",
                    kept.XICUser > before + 1e-12, $"{before:R} -> {kept.XICUser:R}");
                Check("the User column still adds up to 1 after the edit",
                    Math.Abs(items.GroupBy(i => i.UserID).Sum(g => g.First().XICUser) - 1.0) < 1e-9);
            }

            try { Check("the USearch panel builds", new USearchLib() != null); }
            catch (Exception ex) { Check("the USearch panel builds", false, ex.Message); }

            try { Check("the main window builds", new MainWindow() != null); }
            catch (Exception ex) { Check("the main window builds", false, ex.Message); }
        }

        // ============================ every control explains itself, and nothing is clipped

        /// <summary>
        /// The two checks the Definition of Done asks for by name.
        ///
        /// 1. No interactive control may be without a tooltip. A greyed-out control with no explanation
        ///    is a dead end, and several controls used to be enabled while doing nothing at all.
        /// 2. No chrome text may be clipped or run underneath another control, at 1280x800 and at
        ///    1920x1080. This is what happened to the quantitation-profile line, which in a
        ///    collaborator's screenshot ran behind the Recalculate button.
        ///
        /// It builds its own Family Cluster File so it runs with no arguments and without USEARCH.
        /// </summary>
        private static void UiExplanationTests(string? databasePath, string workDir)
        {
            EnsureApplication();

            ClippingDetectorSelfTest();

            // WPF ignores CultureInfo.CurrentCulture for display: FrameworkElement.Language defaults to
            // en-US whatever the machine is set to, so a percentage would be written "12.111%" on a
            // Brazilian machine where everything else writes "12,111%". App overrides that default.
            var language = FrameworkElement.LanguageProperty.GetMetadata(typeof(FrameworkElement)).DefaultValue
                           as System.Windows.Markup.XmlLanguage;

            Console.WriteLine("        display language: " + (language?.IetfLanguageTag ?? "null") +
                              " · machine culture: " + CultureInfo.CurrentCulture.IetfLanguageTag);

            Check("displayed numbers follow the machine's own language rather than always en-US",
                language != null && string.Equals(language.IetfLanguageTag,
                    CultureInfo.CurrentCulture.IetfLanguageTag, StringComparison.OrdinalIgnoreCase),
                (language?.IetfLanguageTag ?? "null") + " vs " + CultureInfo.CurrentCulture.IetfLanguageTag);

            // The check above can only compare two names, and on an en-US machine both are en-US, so it
            // would pass even if the override did nothing. This one proves the mechanism: a binding
            // formats according to the ELEMENT's language, which is the property App overrides.
            var brazilian = new TextBlock { Language = System.Windows.Markup.XmlLanguage.GetLanguage("pt-BR") };
            brazilian.SetBinding(TextBlock.TextProperty,
                new System.Windows.Data.Binding(".") { Source = SampleShare, StringFormat = "P3" });

            Check("a percentage is written the way the element's language writes it, comma and all",
                brazilian.Text == "12,111%", brazilian.Text);

            var american = new TextBlock { Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US") };
            american.SetBinding(TextBlock.TextProperty,
                new System.Windows.Data.Binding(".") { Source = SampleShare, StringFormat = "P3" });

            Check("and the other way round on an English machine, so the two genuinely differ",
                american.Text == "12.111%", american.Text);

            string fcfPath = Path.Combine(workDir, "ui_explanation.fcf");
            BuildSyntheticFcf().Save(fcfPath);

            ClusterGenerationControl cluster;
            USearchLib usearch;
            MainWindow main;
            DeNovoControl deNovo;
            HomeControl home;

            try
            {
                cluster = new ClusterGenerationControl(fcfPath);
                usearch = new USearchLib();
                main = new MainWindow();
                deNovo = new DeNovoControl();
                home = new HomeControl();
            }
            catch (Exception ex)
            {
                Check("the five views build for the tooltip and layout sweep", false,
                    ex.ToString());
                return;
            }

            CheckTooltipCoverage("ClusterGenerationControl", cluster);
            CheckTooltipCoverage("USearchLib", usearch);
            CheckTooltipCoverage("DeNovoControl", deNovo);
            CheckTooltipCoverage("HomeControl", home);
            CheckMenuTooltips("MainWindow", main);

            // The Q7 home is the default view and pure navigation: it opens with no project, so its two
            // project-gated cards (Compute and Analytics) start disabled and their tooltips say what would
            // enable them; opening a project lights them, which is the same rule the File-menu commands
            // they call already use.
            var homeCompute = UiField<Button>(home, "ButtonHomeCompute")!;
            var homeAnalytics = UiField<Button>(home, "ButtonHomeAnalytics")!;
            var homeBuild = UiField<Button>(home, "ButtonHomeBuild")!;
            var homeCurate = UiField<Button>(home, "ButtonHomeCurate")!;

            Check("the home opens with Build and Curate always live, and Compute and Analytics dark",
                homeBuild.IsEnabled && homeCurate.IsEnabled
                && !homeCompute.IsEnabled && !homeAnalytics.IsEnabled);
            Check("and the two dark cards say a project is what lights them",
                (homeCompute.ToolTip as string ?? "").Contains("Disabled until a project is open")
                && (homeAnalytics.ToolTip as string ?? "").Contains("Disabled until a project is open"),
                homeCompute.ToolTip as string);

            home.SetProjectState(true, "demo.fqproj (12 quant rows stored).");
            Check("opening a project lights Compute and Analytics and names the project on the card",
                homeCompute.IsEnabled && homeAnalytics.IsEnabled
                && UiField<TextBlock>(home, "TextBlockHomeProject")!.Text.Contains("demo.fqproj"),
                UiField<TextBlock>(home, "TextBlockHomeProject")!.Text);
            home.SetProjectState(false, "No project open.");
            Check("and closing it darkens them again", !homeCompute.IsEnabled && !homeAnalytics.IsEnabled);

            // The analytics screen (Q4) is a view of ours like the others, built populated so the sweep and
            // the layout checks have real content. Its charts are custom FrameworkElements, not the chrome
            // controls the sweeps examine, so its six chrome controls are the two CSV exports, the screen
            // figure export, the complete talk-plot export and the family chart's two selectors.
            AnalyticsControl analytics;
            try
            {
                analytics = new AnalyticsControl(SyntheticAnalyticsRows(),
                    new QuantManifest { PerRunTic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                        { ["ui_01"] = 5.0e8, ["ui_02"] = 4.0e8 } });
            }
            catch (Exception ex)
            {
                Check("the analytics screen builds for the tooltip and layout sweep", false,
                    ex.GetType().Name + ": " + ex.Message);
                return;
            }
            CheckTooltipCoverage("AnalyticsControl", analytics);

            // A count as well as the sweep, for the same reason as the menu count below: a control added
            // without a tooltip fails above, but a control REMOVED would pass everything in silence. Six
            // chrome controls - export peptides, export summary, save screen figures, save talk plots,
            // the TopN list and the percent-label switch - plus the five list entries the TopN selector offers.
            var analyticsControls = WalkTree(analytics).OfType<FrameworkElement>()
                .Where(e => e.TemplatedParent == null)
                .Where(e => e is Button || e is CheckBox || e is ComboBox || e is TextBox || e is Label)
                .ToList();

            Check("the analytics screen offers exactly its six chrome controls",
                analyticsControls.Count == 6,
                string.Join(", ", analyticsControls.Select(Describe)));

            var topN = UiField<ComboBox>(analytics, "ComboBoxFamilyTopN")!;
            Check("the family TopN list offers its five choices, each with a tooltip of its own",
                topN.Items.Count == 5 && topN.Items.Cast<ComboBoxItem>().All(i => i.ToolTip != null),
                topN.Items.Count.ToString(CultureInfo.CurrentCulture));

            MenuCommandPreservationTests(main);

            // The Q2 compute window is a view of ours like the others, so it is swept for tooltips too.
            ComputeXicWindow computeWindow;
            try { computeWindow = new ComputeXicWindow("test"); }
            catch (Exception ex)
            {
                Check("the compute-XIC window builds for the tooltip sweep", false,
                    ex.GetType().Name + ": " + ex.Message);
                return;
            }
            CheckTooltipCoverage("ComputeXicWindow", computeWindow);

            ComboBox? deNovoMode = UiField<ComboBox>(computeWindow, "ComboBoxDeNovoSource");
            string[] deNovoModeTags = deNovoMode?.Items.Cast<ComboBoxItem>()
                .Select(item => item.Tag as string ?? string.Empty).ToArray()
                ?? Array.Empty<string>();
            Check("the compute-XIC source list exposes both scientifically distinct PEAKS + DeepNovo unions",
                deNovoModeTags.SequenceEqual(new[]
                {
                    "peaks-only", "peaks-all", "deepnovo",
                    "union-only-deepnovo", "union-all-deepnovo",
                }), string.Join(", ", deNovoModeTags));

            var onlyDeepManifest = new QuantManifest
            {
                DeNovoInputs = new List<DeNovoInputManifest>
                {
                    new DeNovoInputManifest
                    {
                        SourceId = DeNovoSource.DenovoOnly.Id, MinimumConfidence = 81.5,
                    },
                    new DeNovoInputManifest
                    {
                        SourceId = DeNovoSource.DeepNovo.Id, MinimumConfidence = 72.25,
                    },
                },
            };
            var allDeepManifest = new QuantManifest
            {
                DeNovoInputs = new List<DeNovoInputManifest>
                {
                    new DeNovoInputManifest
                    {
                        SourceId = DeNovoSource.DeNovoAll.Id, MinimumConfidence = 84.5,
                    },
                    new DeNovoInputManifest
                    {
                        SourceId = DeNovoSource.DeepNovo.Id, MinimumConfidence = 74.25,
                    },
                },
            };
            var restoredOnlyDeep = new ComputeXicWindow("test", onlyDeepManifest);
            var restoredAllDeep = new ComputeXicWindow("test", allDeepManifest);
            string RestoredTag(ComputeXicWindow window) =>
                (UiField<ComboBox>(window, "ComboBoxDeNovoSource")?.SelectedItem as ComboBoxItem)
                    ?.Tag as string ?? string.Empty;
            IReadOnlyList<DeNovoSourceSelection> RestoredSelections(ComputeXicWindow window)
            {
                MethodInfo? method = typeof(ComputeXicWindow).GetMethod("TryBuildDeNovoSelections",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                object?[] arguments = { null };
                bool built = method != null && (bool)(method.Invoke(window, arguments) ?? false);
                return built && arguments[0] is IReadOnlyList<DeNovoSourceSelection> selections
                    ? selections : Array.Empty<DeNovoSourceSelection>();
            }
            IReadOnlyList<DeNovoSourceSelection> onlyDeepSelections =
                RestoredSelections(restoredOnlyDeep);
            IReadOnlyList<DeNovoSourceSelection> allDeepSelections =
                RestoredSelections(restoredAllDeep);
            Check("a saved PEAKS-de-novo-only + DeepNovo union restores without changing source scope",
                RestoredTag(restoredOnlyDeep) == "union-only-deepnovo"
                && onlyDeepSelections.Select(s => s.Source.Id).SequenceEqual(new[]
                {
                    DeNovoSource.DenovoOnly.Id, DeNovoSource.DeepNovo.Id,
                })
                && onlyDeepSelections[0].MinimumConfidence == 81.5
                && onlyDeepSelections[1].MinimumConfidence == 72.25,
                RestoredTag(restoredOnlyDeep) + " / " +
                string.Join(", ", onlyDeepSelections.Select(s => s.Source.Id)));
            Check("a saved PEAKS-all + DeepNovo union restores without degrading to DeepNovo alone",
                RestoredTag(restoredAllDeep) == "union-all-deepnovo"
                && allDeepSelections.Select(s => s.Source.Id).SequenceEqual(new[]
                {
                    DeNovoSource.DeNovoAll.Id, DeNovoSource.DeepNovo.Id,
                })
                && allDeepSelections[0].MinimumConfidence == 84.5
                && allDeepSelections[1].MinimumConfidence == 74.25,
                RestoredTag(restoredAllDeep) + " / " +
                string.Join(", ", allDeepSelections.Select(s => s.Source.Id)));

            // The source-resolved spectrum table is a first-class read-only view. Build it over a row
            // carrying all three callers and both quant bases so its bindings are exercised without a
            // file dialog or a real acquisition.
            var spectrumRows = new List<SpectrumComparisonRow>
            {
                new SpectrumComparisonRow
                {
                    FileName = "ui_01.raw",
                    NormalizedRunName = "ui_01",
                    ScanNumber = 12345,
                    PrecursorHypothesisIndex = 1,
                    Charge = 2,
                    PrecursorMz = 612.34567,
                    RawPrecursorCharge = 2,
                    RawPrecursorMz = 612.34567,
                    RawChargeKnown = true,
                    RawPrecursorMzKnown = true,
                    CanonicalCharge = 2,
                    CanonicalPrecursorMz = 612.34567,
                    PrecursorKeyBasis = "source-reported compatible cluster; RAW diagnostic only",
                    SameScanDifferentPrecursorHypothesis = true,
                    PeaksDeNovoSequence = "C(+57.02)DC(+57.02)YQDLPPK",
                    DeepNovoSequence = "CDCYQDLPPK",
                    PatternLabSequence = "CDCYQDLPPK",
                    PeaksDeNovoScore = 93.2,
                    PeaksDeNovoScoreField = "ALC (%)",
                    PeaksDeNovoAccepted = true,
                    DeepNovoScore = 91.4,
                    DeepNovoScoreField = "SCORE (%)",
                    DeepNovoAccepted = true,
                    PatternLabXScore = 3.21,
                    PeaksDeNovoMatchesRawPrecursor = true,
                    DeepNovoMatchesRawPrecursor = true,
                    PatternLabMatchesRawPrecursor = true,
                    QuantM0Observed = 1.25e8,
                    QuantPrimaryAssigned = 1.78e8,
                    QuantPrimaryBasis = "de novo reference-calibrated envelope",
                    RunTic = 8.5e10,
                    PooledTic = 2.4e11,
                    QuantCreditedInPrimaryTotals = true,
                    QuantCreditReason = "credited as the deterministic precursor representative",
                    FamilyUserId = 7,
                    FamilyAssignment = "snake venom metalloproteinase",
                    PrecursorAgreement = "compatible precursor metadata",
                    JoinStatus = "three-source exact sequence",
                }
            };
            DeNovoAcceptedScoreScatter persistedScoreScatter =
                DeNovoAcceptedScoreScatterStudy.Build(spectrumRows);
            persistedScoreScatter.PeaksMinimumAcceptedScore = 81.5;
            persistedScoreScatter.DeepNovoMinimumAcceptedScore = 72.25;
            persistedScoreScatter.PeaksFilterDescription =
                "PEAKS: ALC (%) >= 81.5; length >= 7; >= 5 consecutive residues at local confidence >= 90%";
            persistedScoreScatter.DeepNovoFilterDescription =
                "DeepNovo: SCORE (%) >= 72.25; length >= 7; >= 5 consecutive residues at local confidence >= 90%";
            Window spectrumWindow = SpectrumComparisonWindow.Create(spectrumRows,
                Path.Combine(Path.GetTempPath(), SpectrumComparisonWindow.SuggestedFileName),
                persistedScoreScatter);

            if (spectrumWindow.Content is FrameworkElement spectrumContent)
            {
                CheckTooltipCoverage("SpectrumComparisonWindow", spectrumContent);

                DataGrid? spectrumGrid = WalkTree(spectrumContent).OfType<DataGrid>()
                    .FirstOrDefault(g => g.Name == "DataGridSpectrumComparison");
                string[] headers = spectrumGrid?.Columns.Select(c => c.Header as string ?? string.Empty)
                    .ToArray() ?? Array.Empty<string>();

                Check("the spectrum grid is read-only and binds one source-resolved row",
                    spectrumGrid != null && spectrumGrid.IsReadOnly
                    && spectrumGrid.ItemsSource is IReadOnlyList<SpectrumComparisonRow> boundRows
                    && boundRows.Count == 1
                    && spectrumGrid.Items.Count == 1,
                    spectrumGrid == null ? "grid not found" : spectrumGrid.Items.Count.ToString());

                Check("the spectrum grid exposes caller sequences, source scores and two quant bases",
                    headers.Contains("Precursor hypothesis z")
                    && headers.Contains("Same scan, other hypothesis?")
                    && headers.Contains("RAW charge known")
                    && headers.Contains("Precursor key basis")
                    && headers.Contains("PEAKS de novo sequence")
                    && headers.Contains("PEAKS passes configured filters?")
                    && headers.Contains("DeepNovo sequence")
                    && headers.Contains("DeepNovo passes configured filters?")
                    && headers.Contains("PatternLab sequence")
                    && headers.Contains("PEAKS ALC (%)")
                    && headers.Contains("DeepNovo SCORE (%)")
                    && headers.Contains("PatternLab XCorr")
                    && headers.Contains("Observed precursor XIC (M+0)")
                    && headers.Contains("Primary assigned quant")
                    && headers.Contains("Primary quant basis")
                    && headers.Contains("Single family ID")
                    && headers.Contains("All family IDs")
                    && headers.Contains("All family assignments")
                    && headers.Contains("Family mapping identity")
                    && headers.Contains("Family mapping status")
                    && headers.Contains("Run TIC")
                    && headers.Contains("Credited in primary totals?")
                    && headers.Contains("Join status"),
                    string.Join(", ", headers));

                Button? scorePlotButton = WalkTree(spectrumContent).OfType<Button>()
                    .FirstOrDefault(button => button.Name == "ButtonSaveAcceptedScoreScatter");
                DeNovoAcceptedScoreScatter? displayedScatter =
                    scorePlotButton?.DataContext as DeNovoAcceptedScoreScatter;
                Check("the spectrum view exports the persisted accepted-score scatter with its configured cutoffs",
                    ReferenceEquals(displayedScatter, persistedScoreScatter)
                    && displayedScatter?.PeaksMinimumAcceptedScore == 81.5
                    && displayedScatter.DeepNovoMinimumAcceptedScore == 72.25
                    && displayedScatter.PeaksFilterDescription.Contains("length >= 7",
                        StringComparison.Ordinal)
                    && displayedScatter.DeepNovoFilterDescription.Contains("SCORE (%) >= 72.25",
                        StringComparison.Ordinal),
                    displayedScatter == null ? "scatter not attached to the command"
                        : Convert.ToString(displayedScatter.PeaksMinimumAcceptedScore,
                            CultureInfo.InvariantCulture) + " / " +
                          Convert.ToString(displayedScatter.DeepNovoMinimumAcceptedScore,
                            CultureInfo.InvariantCulture));
            }

            // The lock manager is a view of ours like the other two, so it is swept like them. Built over
            // a file carrying one lock whose family exists and one whose family does not, so the listing
            // has both kinds of row to lay out rather than an empty table.
            var lockFixture = BuildSyntheticFcf();
            lockFixture.LockedUserIds.Add(0);
            lockFixture.LockedUserIds.Add(777);

            LockManagerWindow lockManager;

            try { lockManager = new LockManagerWindow(lockFixture); }
            catch (Exception ex)
            {
                Check("the lock manager builds for the tooltip and layout sweep", false,
                    ex.GetType().Name + ": " + ex.Message);
                return;
            }

            CheckTooltipCoverage("LockManagerWindow", lockManager);

            // The identity-sweep window is a view of ours too, and it now carries a command of its own
            // (the figure export), so it joins the sweeps rather than being the one window nobody checks.
            Window scanWindow = IdentityScanWindow.Create("Identity scan · sweep", new List<IdentityScanRow>
            {
                new IdentityScanRow { Identity = 90, Groups = 500, TargetGroups = 260, MixedGroups = 0, Score = 0.000 },
                new IdentityScanRow { Identity = 70, Groups = 320, TargetGroups = 168, MixedGroups = 1, Score = 0.003 },
                new IdentityScanRow { Identity = 50, Groups = 170, TargetGroups = 92,  MixedGroups = 8, Score = 0.061 },
            }, "identity  families  mixed\n      90       500      0\n      70       320      1\n", 70);

            if (scanWindow.Content is FrameworkElement scanContent)
            {
                CheckTooltipCoverage("IdentityScanWindow", scanContent);
            }

            // The lists must SHOW the words that the status line, the column headings and the dialogs
            // quote. Anything else and a message names something the user cannot find on screen.
            var modeItems = UiField<ComboBox>(cluster, "ComboBoxNormalization")!;
            var sharedItems = UiField<ComboBox>(cluster, "ComboBoxSharedPeptides")!;

            Check("the reporting list shows exactly the names the rest of the program quotes",
                modeItems.Items.Cast<ComboBoxItem>()
                    .Select(i => i.Content as string)
                    .SequenceEqual(Enum.GetValues<NormalizationMode>().Select(m => m.DisplayName())));

            Check("the shared-peptide list shows exactly the names the rest of the program quotes",
                sharedItems.Items.Cast<ComboBoxItem>()
                    .Select(i => i.Content as string)
                    .SequenceEqual(Enum.GetValues<SharedPeptidePolicy>().Select(p => p.DisplayName())));

            Check("every entry of both lists explains what it does",
                modeItems.Items.Cast<ComboBoxItem>().All(i => i.ToolTip != null)
                && sharedItems.Items.Cast<ComboBoxItem>().All(i => i.ToolTip != null));

            // No tooltip may use a term that no longer exists in the interface.
            var staleTips = AllTooltips(cluster).Concat(AllTooltips(usearch)).Concat(AllTooltips(main))
                .Where(t => t.IndexOf("unbiased", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            Check("no tooltip still speaks of 'unbiased normalization', a term the interface dropped",
                staleTips.Count == 0, string.Join(" | ", staleTips.Take(2)));

            // Controls that cannot do anything yet are disabled, and their tooltip says what enables them.
            var recalculate = UiField<Button>(cluster, "ButtonRecalculateQuantitation")!;
            var align = UiField<Button>(cluster, "ButtonAlignSelected")!;
            var superCluster = UiField<Button>(cluster, "ButtonSuperCluster")!;
            var fdr = UiField<Button>(usearch, "ButtonCalculateFDR")!;

            Check("Recalculate is disabled before a project is loaded", !recalculate.IsEnabled);
            Check("and its tooltip says what would enable it",
                (recalculate.ToolTip as string ?? "").Contains("Layer 2"), recalculate.ToolTip as string);
            Check("Align Selected is disabled until rows are selected", !align.IsEnabled);
            Check("and its tooltip says so",
                (align.ToolTip as string ?? "").Contains("two rows"), align.ToolTip as string);
            Check("Create Super Cluster is disabled until a keyword is typed", !superCluster.IsEnabled);
            Check("Calculate FDR is disabled until a Family Cluster File has been generated", !fdr.IsEnabled);

            var keywords = UiField<TextBox>(cluster, "TextBoxSuperClusterWording")!;
            keywords.Text = "metalloproteinase";
            Check("typing a keyword enables the super-cluster button", superCluster.IsEnabled);
            keywords.Text = "";
            Check("clearing it disables the button again", !superCluster.IsEnabled);

            // The three new Layer-1 controls exist, carry tooltips (also enforced by the sweep above), and
            // start in the right state.
            var undo = UiField<Button>(cluster, "ButtonUndo")!;
            var matchMode = UiField<ComboBox>(cluster, "ComboBoxSuperClusterMatch")!;
            var superExclude = UiField<TextBox>(cluster, "TextBoxSuperClusterExclude")!;

            Check("Undo starts disabled and explains what would enable it",
                !undo.IsEnabled && (undo.ToolTip as string ?? "").Length > 100, undo.ToolTip as string);
            Check("the super-cluster match selector defaults to 'match all words'",
                matchMode.SelectedIndex == 0 &&
                (matchMode.Items[0] as ComboBoxItem)?.Content as string == "match all words",
                (matchMode.Items[matchMode.SelectedIndex] as ComboBoxItem)?.Content as string);
            Check("the match selector offers exactly match-all and match-any, each with a tooltip",
                matchMode.Items.Count == 2 &&
                matchMode.Items.Cast<ComboBoxItem>().All(i => i.ToolTip != null),
                string.Join(",", matchMode.Items.Cast<ComboBoxItem>().Select(i => i.Content)));
            Check("the super-cluster exclude box carries its own tooltip and starts empty",
                superExclude.ToolTip is string se && se.Length > 100 && superExclude.Text.Length == 0);

            // The real boxes, read the way the buttons read them: commas used to survive into the words.
            var reclusterExclude = UiField<TextBox>(cluster, "TextBoxWordsToBeExcluded")!;
            superExclude.Text = "inhibitor, ADAM, matrix";
            reclusterExclude.Text = "domain, Fragment";
            Check("the Recluster by Words exclude box reads commas as separators",
                cluster.ReclusterExcludeWords().SequenceEqual(new[] { "domain", "fragment" }),
                string.Join(" | ", cluster.ReclusterExcludeWords()));
            Check("both tooltips say commas work",
                (superExclude.ToolTip as string ?? "").Contains("commas", StringComparison.Ordinal)
                && (reclusterExclude.ToolTip as string ?? "").Contains("commas", StringComparison.Ordinal));
            superExclude.Text = "";
            reclusterExclude.Text = "";

            // The headings must name the denominator, and must not claim one before anything is computed.
            var xicColumn = UiField<DataGridTextColumn>(cluster, "ColumnXicUser")!;
            Check("before any calculation the share column does not claim a denominator",
                (xicColumn.Header as string ?? "").Contains("none calculated yet"), xicColumn.Header as string);

            // The share columns used to show a raw double - 0.121109, with nothing to say whether that
            // meant 12% or 0.12%. Read what the grid actually renders.
            LayoutAt(cluster, new Size(1280, 800), 1);

            var cellTexts = WalkTree(cluster).OfType<TextBlock>()
                .Where(t => HasAncestor<DataGridCell>(t))
                .Select(t => t.Text)
                .ToList();

            string asPercentage = SampleShare.ToString("P3", CultureInfo.CurrentCulture);

            Check("the share column renders a percentage with three decimals",
                cellTexts.Contains(asPercentage), asPercentage + " not among " + cellTexts.Count + " cells");
            Check("and no longer renders the bare fraction",
                !cellTexts.Contains(SampleShare.ToString(CultureInfo.CurrentCulture)),
                SampleShare.ToString(CultureInfo.CurrentCulture));

            // The headings change with the reporting mode, so the layout has to survive the LONGEST of
            // them and not just the placeholder that is there before anything is calculated.
            string longestHeading = Enum.GetValues<NormalizationMode>()
                .Select(m => m.ColumnHeader())
                .OrderByDescending(h => h.Length)
                .First();

            xicColumn.Header = longestHeading;

            Console.WriteLine("        longest column heading: \"" + longestHeading + "\"");

            // --- layout, at the two sizes the Definition of Done names -----------------------------
            foreach (var size in new[] { new Size(1280, 800), new Size(1920, 1080) })
            {
                CheckLayout($"the controls panel at {size.Width}x{size.Height}", cluster, size, 0);
                CheckLayout($"the data browser at {size.Width}x{size.Height}", cluster, size, 1);
                CheckLayout($"the clustering panel at {size.Width}x{size.Height}", usearch, size, -1);
                CheckLayout($"the de novo screen at {size.Width}x{size.Height}", deNovo, size, -1);
                CheckLayout($"the analytics screen at {size.Width}x{size.Height}", analytics, size, -1);
                CheckLayout($"the home screen at {size.Width}x{size.Height}", home, size, -1);

                if (main.Content is FrameworkElement mainContent)
                {
                    CheckLayout($"the main window at {size.Width}x{size.Height}", mainContent, size, -1);
                }

                // The window's content, not the Window: a Window measures itself against the desktop, so
                // laying one out by hand measures nothing. This is how the main window is checked too.
                if (lockManager.Content is FrameworkElement lockContent)
                {
                    CheckLayout($"the lock manager at {size.Width}x{size.Height}", lockContent, size, -1);
                }

                if (scanWindow.Content is FrameworkElement scanLayout)
                {
                    CheckLayout($"the identity sweep at {size.Width}x{size.Height}", scanLayout, size, -1);
                }

                if (spectrumWindow.Content is FrameworkElement spectrumLayout)
                {
                    CheckLayout($"the spectrum comparison table at {size.Width}x{size.Height}",
                        spectrumLayout, size, -1);
                }
            }
        }

        /// <summary>
        /// A Family Cluster File that looks like a real one: several families, decoys, a contaminant and
        /// descriptions long enough to stress the table, built without USEARCH so this section always runs.
        /// </summary>
        /// <summary>The exact share the real project reports for its largest family, used as a fixture.</summary>
        private const double SampleShare = 0.121109;

        private static FamilyClusterFile BuildSyntheticFcf()
        {
            var items = new List<FastaItemExtended>();

            string[] descriptions =
            {
                "Snake venom metalloproteinase BJ-PI OS=Bothrops jararaca OX=8724 GN=svmp PE=1 SV=2",
                "Zinc metalloproteinase-disintegrin-like jararhagin OS=Bothrops jararaca OX=8724",
                "Basic phospholipase A2 homolog OS=Bothrops jararaca OX=8724 GN=pla2 PE=1 SV=1",
                "L-amino-acid oxidase OS=Bothrops jararaca OX=8724 GN=laao PE=1 SV=1",
                "Snake venom serine protease bothrombin OS=Bothrops jararaca OX=8724",
            };

            for (int family = 0; family < descriptions.Length; family++)
            {
                for (int member = 0; member < 3; member++)
                {
                    items.Add(new FastaItemExtended
                    {
                        SequenceIdentifier = $"sp|P{family}{member}0000|VM{family}_BOTJA",
                        Description = descriptions[family],
                        Sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFR",
                        USearchID = family,
                        UserID = family,
                        XIC = family == 0 ? SampleShare : 0,
                        XICUser = family == 0 ? SampleShare : 0
                    });

                    items.Add(new FastaItemExtended
                    {
                        SequenceIdentifier = $"Reverse_sp|P{family}{member}0000|VM{family}_BOTJA",
                        Description = "",
                        Sequence = "RFVIQLSDGYFDWTMKSTAVIHPSRHEEQ",
                        USearchID = descriptions.Length + family,
                        UserID = descriptions.Length + family
                    });
                }
            }

            items.Add(new FastaItemExtended
            {
                SequenceIdentifier = "contaminant_KRT1_HUMAN",
                Description = "Keratin, type II cytoskeletal 1 OS=Homo sapiens OX=9606 GN=KRT1",
                Sequence = "MSRQFSSRSGYRSGGGFSSGSAGIINYQRRT",
                USearchID = 2 * descriptions.Length,
                UserID = 2 * descriptions.Length
            });

            return new FamilyClusterFile { ClusteringIdentity = 85, MyExtendedFastaItems = items };
        }

        private static void EnsureApplication()
        {
            // The controls reference the shared theme brushes declared in App.xaml, so a real Application
            // must exist first. InitializeComponent only loads resources; StartupUri fires on Run(),
            // which is never called here. Only one Application may ever exist in a process.
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                return;
            }

            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.GetType().GetMethod("InitializeComponent")?.Invoke(app, null);
        }

        /// <summary>
        /// The Definition of Done, item 1: no Button, CheckBox or ComboBox without a tooltip. TextBox and
        /// Label are included as well, because requirement 5 covers them and the same argument applies.
        ///
        /// Only elements DECLARED in the XAML are examined - anything with a TemplatedParent came out of
        /// a control template (the spinner buttons of an up-down, a scrollbar's arrows, the DataGrid's
        /// internals) and is not ours to annotate.
        /// </summary>
        private static void CheckTooltipCoverage(string what, FrameworkElement root)
        {
            var controls = WalkTree(root)
                .OfType<FrameworkElement>()
                .Where(e => e.TemplatedParent == null)
                .Where(e => e is Button || e is CheckBox || e is ComboBox || e is TextBox || e is Label)
                .ToList();

            var silent = controls.Where(c => c.ToolTip == null).ToList();

            Check($"{what}: all {controls.Count} interactive controls carry a tooltip",
                silent.Count == 0,
                string.Join(", ", silent.Select(Describe).Take(6)));

            Check($"{what}: found controls to check at all", controls.Count > 0, controls.Count.ToString());

            // A tooltip has to say something. Three words is a floor, not a standard.
            var terse = controls
                .Where(c => c.ToolTip is string s && s.Split(' ').Length < 6)
                .ToList();

            Check($"{what}: no tooltip is a bare restatement of the label",
                terse.Count == 0, string.Join(", ", terse.Select(Describe).Take(6)));
        }

        private static void MenuCommandPreservationTests(MainWindow main)
        {
            // Named command coverage stays stable when the recent-file submenu changes length.
            string[] commands = {
                "MenuItemHome", "MenuItemRunUSearch", "MenuItemLoadFQC", "MenuItemSaveProject",
                "MenuItemSaveFCF", "MenuItemComputeXic", "MenuItemSpectrumComparison", "MenuItemReanotateSEPro",
                "MenuItemDeNovo", "MenuItemAnalytics", "MenuItemHowToRead", "MenuItemWhereAmI",
                "MenuThemeLight", "MenuThemeDark", "MenuItemSaveAs", "MenuItemUndo", "MenuItemCopy",
                "MenuItemFind", "MenuItemHideDecoys", "MenuItemHideContaminants", "MenuItemExportFamilyTable",
                "MenuItemExportComposition", "MenuItemFamilies"
            };
            string[] missing = commands.Where(name => main.FindName(name) is not MenuItem { HasItems: false }).ToArray();
            Check("all legacy and new shell commands remain reachable menu leaves", missing.Length == 0,
                string.Join(", ", missing));
        }

        private static void CheckMenuTooltips(string what, FrameworkElement root)
        {
            // Submenu headers ("File", "Help") are containers, not actions; only leaves are checked.
            var leaves = WalkTree(root).OfType<MenuItem>()
                .Where(m => m.TemplatedParent == null && m.Items.Count == 0)
                .ToList();

            var silent = leaves.Where(m => m.ToolTip == null).ToList();

            Check($"{what}: all {leaves.Count} menu commands carry a tooltip",
                silent.Count == 0, string.Join(", ", silent.Select(Describe).Take(6)));
        }

        private static IEnumerable<string> AllTooltips(FrameworkElement root)
        {
            return WalkTree(root).OfType<FrameworkElement>()
                .Select(e => e.ToolTip as string)
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t!);
        }

        private static string Describe(FrameworkElement element)
        {
            if (!string.IsNullOrEmpty(element.Name)) return element.Name;
            if (element is ContentControl cc && cc.Content is string s) return element.GetType().Name + " \"" + s + "\"";
            return element.GetType().Name;
        }

        /// <summary>
        /// Lays the control out at a real screen size and checks that no text is cut off and that no text
        /// runs underneath a button.
        ///
        /// Data cells are excluded: a Description longer than its column is clipped by any table ever
        /// written and is not a defect. Everything else - labels, headings, status lines, button captions,
        /// column headers - is chrome we control, and it has to fit.
        /// </summary>
        /// <summary>Lays a control out at a real screen size, on a chosen tab, and lets it settle.</summary>
        private static void LayoutAt(FrameworkElement root, Size size, int tabIndex)
        {
            if (tabIndex >= 0)
            {
                var tabs = WalkTree(root).OfType<TabControl>().FirstOrDefault();
                if (tabs != null && tabIndex < tabs.Items.Count) tabs.SelectedIndex = tabIndex;
            }

            // Three passes, not one. A DataGrid settles over more than one layout pass: the first tells
            // it how wide its headers and cells want to be, and what it computes from that is only
            // honoured on the next one.
            for (int pass = 0; pass < 3; pass++)
            {
                root.InvalidateMeasure();
                root.Measure(size);
                root.Arrange(new Rect(new Point(0, 0), size));
                root.UpdateLayout();
            }
        }

        private static void CheckLayout(string what, FrameworkElement root, Size size, int tabIndex)
        {
            LayoutAt(root, size, tabIndex);

            var elements = WalkTree(root).OfType<FrameworkElement>().ToList();

            // WPF's own answer to "is this element bigger than the space it was given". Comparing
            // ActualWidth against a re-measurement does NOT work: a TextBlock that overflows reports the
            // width of its text, not the width of its slot, so that comparison is always equal and would
            // pass no matter how badly the text was cut off. GetLayoutClip is the geometry the layout
            // system installs precisely when it has to cut an element down to its slot.
            var clipped = elements
                .Where(e => e.ActualWidth > 0 && e.ActualHeight > 0)
                .Where(e => e is TextBlock || e is Label || e is Button || e is CheckBox || e is ComboBox)
                .Where(e => !HasAncestor<DataGridCell>(e))      // data, not chrome: any table clips long values
                .Where(e => LayoutInformation.GetLayoutClip(e) != null)
                .Select(DescribeVisually)
                .ToList();

            Check(what + ": no text is cut off", clipped.Count == 0, string.Join(" · ", clipped.Take(4)));

            // The defect from the screenshot: text rendered underneath a button.
            var buttons = elements.OfType<Button>()
                .Where(b => b.ActualWidth > 1 && b.ActualHeight > 1)
                .ToList();

            var texts = elements.OfType<TextBlock>()
                .Where(t => t.ActualWidth > 1 && t.ActualHeight > 1 && !string.IsNullOrWhiteSpace(t.Text))
                .ToList();

            var overlaps = new List<string>();

            foreach (Button button in buttons)
            {
                Rect buttonBounds = BoundsIn(root, button);
                if (buttonBounds.IsEmpty) continue;

                foreach (TextBlock text in texts)
                {
                    if (IsAncestorOf(button, text)) continue;    // the button's own caption

                    Rect textBounds = BoundsIn(root, text);
                    if (textBounds.IsEmpty) continue;

                    // Half a unit of slack so that merely touching edges is not an overlap.
                    textBounds.Inflate(-0.5, -0.5);
                    if (textBounds.Width <= 0 || textBounds.Height <= 0) continue;

                    if (buttonBounds.IntersectsWith(textBounds))
                    {
                        overlaps.Add(Shorten(text.Text) + " under " + Describe(button));
                    }
                }
            }

            Check(what + ": no text runs underneath a button", overlaps.Count == 0,
                string.Join(" · ", overlaps.Take(4)));
        }

        /// <summary>
        /// Proves the clipping detector is live before it is trusted. A check that cannot fail is not a
        /// check, and the first version of this one could not: it compared a TextBlock's ActualWidth
        /// with a re-measurement of the same text, which are equal by construction even when the text is
        /// being cut in half.
        /// </summary>
        private static void ClippingDetectorSelfTest()
        {
            var tooLong = new TextBlock
            {
                Text = "a caption far longer than the ten pixels it is about to be given",
                TextWrapping = TextWrapping.NoWrap
            };

            var host = new Border { Width = 10, Height = 20, Child = tooLong };
            host.Measure(new Size(10, 20));
            host.Arrange(new Rect(0, 0, 10, 20));
            host.UpdateLayout();

            Check("the clipping detector can actually see a label that does not fit",
                LayoutInformation.GetLayoutClip(tooLong) != null);

            var fits = new TextBlock { Text = "ok", TextWrapping = TextWrapping.NoWrap };
            var roomy = new Border { Width = 400, Height = 40, Child = fits };
            roomy.Measure(new Size(400, 40));
            roomy.Arrange(new Rect(0, 0, 400, 40));
            roomy.UpdateLayout();

            Check("and does not accuse a label that fits",
                LayoutInformation.GetLayoutClip(fits) == null);
        }

        private static string DescribeVisually(FrameworkElement element)
        {
            Rect slot = LayoutInformation.GetLayoutSlot(element);

            string name = element is TextBlock block && !string.IsNullOrWhiteSpace(block.Text) ? Shorten(block.Text)
                        : element is ContentControl content && content.Content is string s ? Shorten(s)
                        : Describe(element);

            return name + " [needs " + element.RenderSize.Width.ToString("F0") + "x" +
                   element.RenderSize.Height.ToString("F0") + ", slot " + slot.Width.ToString("F0") + "x" +
                   slot.Height.ToString("F0") + "]";
        }

        private static string Shorten(string text)
        {
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 42 ? "\"" + flat + "\"" : "\"" + flat.Substring(0, 40) + "...\"";
        }

        /// <summary>Bounds relative to <paramref name="root"/>, or empty when the element is not under it.</summary>
        private static Rect BoundsIn(FrameworkElement root, FrameworkElement element)
        {
            try
            {
                return element.TransformToAncestor(root)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                // Left over from a tab that is no longer realised; nothing to measure.
                return Rect.Empty;
            }
        }

        private static bool HasAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            for (DependencyObject? current = node; current != null; current = VisualParentOf(current))
            {
                if (current is T && !ReferenceEquals(current, node)) return true;
            }

            return false;
        }

        private static bool IsAncestorOf(DependencyObject ancestor, DependencyObject node)
        {
            for (DependencyObject? current = node; current != null; current = VisualParentOf(current))
            {
                if (ReferenceEquals(current, ancestor)) return true;
            }

            return false;
        }

        private static DependencyObject? VisualParentOf(DependencyObject node)
        {
            if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            {
                DependencyObject? parent = VisualTreeHelper.GetParent(node);
                if (parent != null) return parent;
            }

            return LogicalTreeHelper.GetParent(node);
        }

        /// <summary>
        /// Every element of a control, taking the logical tree AND the visual one.
        ///
        /// Neither alone is enough. The logical tree holds everything the XAML declares even when it has
        /// never been rendered, which is what makes the tooltip sweep work on a control that was never put
        /// in a window; the visual tree holds what a template produced, which is what the layout checks
        /// have to measure.
        /// </summary>
        private static List<DependencyObject> WalkTree(DependencyObject root)
        {
            var found = new List<DependencyObject>();
            var seen = new HashSet<DependencyObject>();
            var pending = new Stack<DependencyObject>();

            pending.Push(root);

            while (pending.Count > 0)
            {
                DependencyObject node = pending.Pop();
                if (!seen.Add(node)) continue;

                found.Add(node);

                foreach (object child in LogicalTreeHelper.GetChildren(node))
                {
                    if (child is DependencyObject dependencyChild) pending.Push(dependencyChild);
                }

                // GetChildrenCount throws on anything that is not a Visual, and plenty of what the
                // logical tree returns is not - Run, Inline, DataGridColumn.
                if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
                {
                    int count = VisualTreeHelper.GetChildrenCount(node);
                    for (int i = 0; i < count; i++) pending.Push(VisualTreeHelper.GetChild(node, i));
                }
            }

            return found;
        }

        /// <summary>The rows the grid is currently showing, i.e. exactly what an export would write.</summary>
        private static List<FastaItemExtended> VisibleRows(DataGrid grid)
        {
            var source = grid.ItemsSource;
            if (source == null) return new List<FastaItemExtended>();

            return source.Cast<FastaItemExtended>().ToList();
        }

        private static T? UiField<T>(object control, string name) where T : class
        {
            return control.GetType()
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?
                .GetValue(control) as T;
        }
    }
}
