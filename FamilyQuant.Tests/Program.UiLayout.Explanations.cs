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
            CurationWordingTests();

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

            // Export choice/action and Top N. Export kinds stay pinned in the view-model check.
            var analyticsControls = WalkTree(analytics).OfType<FrameworkElement>()
                .Where(e => e.TemplatedParent == null)
                .Where(e => e is Button || e is CheckBox || e is ComboBox || e is TextBox || e is Label)
                .ToList();

            Check("the Results screen offers its three consolidated chrome controls",
                analyticsControls.Count == 3,
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

            ComboBox? deNovoMode = UiField<ComboBox>(UiField<DeNovoFilterControl>(computeWindow.Screen, "FilterControl")!, "ComboBoxDeNovoSource");
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
                (UiField<ComboBox>(UiField<DeNovoFilterControl>(window.Screen, "FilterControl")!, "ComboBoxDeNovoSource")?.SelectedItem as ComboBoxItem)
                    ?.Tag as string ?? string.Empty;
            IReadOnlyList<DeNovoSourceSelection> RestoredSelections(ComputeXicWindow window)
            {
                return window.ViewModel.Filters.TryBuild(out _, out var selections, out _)
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
                (recalculate.ToolTip as string ?? "").Contains("PatternLab"), recalculate.ToolTip as string);
            Check("Align Selected is disabled until rows are selected", !align.IsEnabled);
            Check("and its tooltip says so",
                (align.ToolTip as string ?? "").Contains("two rows"), align.ToolTip as string);
            Check("Group by keywords is available before any keyword is typed", superCluster.IsEnabled
                && superCluster.Content as string == "Group by keywords");
            Check("Calculate FDR is disabled until a Family Cluster File has been generated", !fdr.IsEnabled);

            var keywordPreview = new SuperClusterPreview(Vipers(), null, "", "", KeywordMatch.All, null, null);
            var keywordWindow = new SuperClusterPreviewWindow(keywordPreview);
            LayoutAt((FrameworkElement)keywordWindow.Content, new Size(keywordWindow.Width, keywordWindow.Height), -1);
            var keywordInputs = WalkTree((FrameworkElement)keywordWindow.Content).OfType<TextBox>()
                .Where(t => t.TemplatedParent == null).ToList();
            var keywords = keywordInputs.Single(t => (System.Windows.Data.BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path.Path) == "KeywordsText");
            keywords.Text = "metalloproteinase";
            Check("typing a keyword in review updates the preview", keywordPreview.KeywordsText == "metalloproteinase");
            keywords.Text = "";
            Check("clearing the review keywords disables Apply", !((GroupingReviewViewModel)keywordWindow.DataContext).CanConfirm);

            // The three new Layer-1 controls exist, carry tooltips (also enforced by the sweep above), and
            // start in the right state.
            var undo = UiField<Button>(cluster, "ButtonUndo")!;
            var matchMode = WalkTree((FrameworkElement)keywordWindow.Content).OfType<ComboBox>()
                .Single(t => System.Windows.Data.BindingOperations.GetBinding(t, System.Windows.Controls.Primitives.Selector.SelectedIndexProperty)?.Path.Path == "MatchIndex");
            var superExclude = keywordInputs.Single(t => System.Windows.Data.BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path.Path == "ExcludeText");

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
            keywordWindow.Close();

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

            CheckWordingRules(what, root);
        }

        private static void CheckWordingRules(string what, FrameworkElement root)
        {
            var tips = AllTooltips(root).Concat(WalkTree(root).OfType<DataGrid>()
                .SelectMany(grid => grid.Columns).Select(HeaderTooltipOf).OfType<string>()).Distinct().ToArray();
            var longTips = tips.Where(tip => TooltipSentenceCount(tip) > 2).ToArray();
            Check($"{what}: tooltips use one or two sentences", longTips.Length == 0, string.Join(" | ", longTips.Take(6)));
            var oldNames = ScreenWording(root).Where(text => RetiredUiTerms.IsMatch(text)).Distinct().ToArray();
            Check($"{what}: visible text and tooltips use current screen names", oldNames.Length == 0, string.Join(" | ", oldNames.Take(6)));
        }

        private static void MenuCommandPreservationTests(MainWindow main)
        {
            // Named command coverage stays stable when the recent-file submenu changes length.
            string[] commands = {
                "MenuItemHome", "MenuItemRunUSearch", "MenuItemLoadFQC", "MenuItemSaveProject",
                "MenuItemExport", "MenuItemComputeXic", "MenuItemSpectrumComparison", "MenuItemReanotateSEPro",
                "MenuItemDeNovo", "MenuItemAnalytics", "MenuItemHowToRead", "MenuItemWhereAmI",
                "MenuThemeLight", "MenuThemeDark", "MenuThemeSystem", "MenuItemSaveAs", "MenuItemUndo", "MenuItemCopy",
                "MenuItemFind", "MenuItemHideDecoys", "MenuItemHideContaminants", "MenuItemExport",
                "MenuItemExport", "MenuItemFamilies"
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
            CheckWordingRules(what, root);
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

    }
}
