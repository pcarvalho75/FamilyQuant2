using Microsoft.Win32;
using PatternTools.PLP;
using FamilyQuant.DeNovo;
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
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ClusterGenerationControl? cgc;
        USearchLib? usearch;

        /// <summary>The task-oriented home screen, when it is the one on display. It is the fifth swap
        /// state and the default one: the constructor shows it, and Home on the menu brings it back. In
        /// this state all four of the fields below are null, which is exactly what names "the home is on
        /// screen".</summary>
        HomeControl? home;

        /// <summary>The de novo screen, when it is the one on display. Exactly one of the five view
        /// fields (this, <see cref="home"/>, <see cref="cgc"/>, <see cref="usearch"/>,
        /// <see cref="analytics"/>) is non-null at a time: they name what GridApp is showing, and the
        /// guide and the menu enabling below both read them.</summary>
        DeNovoControl? dnc;

        /// <summary>The analytics screen, when it is the one on display. Like the de novo screen it is a
        /// read-only view swapped into GridApp; it reads the loaded project's stored quant and draws the
        /// graphs, and changes no family, User ID or quantitation.</summary>
        AnalyticsControl? analytics;

        /// <summary>The last Family Cluster File that was opened, kept after the view is swapped away.
        /// The de novo screen's homology Layer needs one, and it is nearly always the same file the
        /// session was already working with; the field survives the swap precisely because the control
        /// that held it does not.</summary>
        string? lastFcfPath;

        /// <summary>
        /// The curation of the loaded project, kept after the view is swapped away for the same reason as
        /// <see cref="lastFcfPath"/>. It is the SAME object the cluster control edits, so it is never a
        /// stale copy. The analytics screen needs it to attribute peptides to families; the Home,
        /// Analytics and de novo swaps keep <see cref="cgc"/> too, so the open project stays saveable there,
        /// while the USearch swap, which starts a different database, drops both.
        /// </summary>
        FamilyClusterFile? loadedCuration;

        /// <summary>
        /// The quant part of the loaded project: the per-peptide, per-isotope areas the store carries.
        /// Empty for a project opened from a plain .fcf (which holds no quant), or until a Compute /
        /// update full XIC fills it. Held here, beside the curation the cluster control holds, so a Save
        /// project can write both.
        /// </summary>
        List<QuantRow> loadedQuant = new List<QuantRow>();

        /// <summary>The manifest paired with <see cref="loadedQuant"/>, or null when there is no stored
        /// quantitation yet.</summary>
        QuantManifest? loadedManifest;

        /// <summary>The automatic spectrum-table CSV from the latest computation in this session. A
        /// reloaded project can always rebuild the same table from its quant rows, but an external CSV
        /// path is not part of the project container and therefore remains null after reload.</summary>
        string? lastSpectrumComparisonCsvPath;

        public MainWindow()
        {
            InitializeComponent();

            this.Title += " " + AppVersion();

            // The home screen is the default view. It is shown here rather than declared in XAML because
            // its cards call the existing menu handlers, which is wiring only code can do.
            ShowHome();
        }

        /// <summary>
        /// Returns this executable's own version without asking PatternTools to rediscover it by name.
        /// PatternTools' legacy helper dereferences a missing assembly when FamilyQuant is hosted by the
        /// test runner (and can do the same in other launch hosts).  The version belongs to FamilyQuant:
        /// the ClickOnce deployment version when installed, this assembly's otherwise (see
        /// <see cref="AppVersionInfo"/>).
        /// </summary>
        private static string AppVersion()
        {
            return AppVersionInfo.Current();
        }

        /// <summary>
        /// Shows the task-oriented home screen and makes it the active view. Like the other swaps it drops
        /// the family fields, so File - Save cannot write a Family Cluster File that is no longer on
        /// screen, and it disables the commands that need a loaded project. Its cards are wired to the very
        /// menu handlers the equivalent commands use, so the home adds navigation and no second code path.
        /// </summary>
        private void ShowHome()
        {
            GridApp.Children.Clear();

            // The transient screens are dropped, but the OPEN PROJECT is kept: going to the hub must not
            // throw away a loaded (and possibly unsaved) curation, and the Compute and Analytics cards can
            // only light up if the project they need is still here. The Save commands stay live while it
            // is open, because what they write is that open project (cgc, with the quant beside it), still
            // the one named on the banner. The old hazard was a Save writing a file that was NO LONGER the
            // open one, after a swap to a different database; that cannot happen here. Disabling them
            // left a Compute started from the Home card with no way to be saved: every route back to the
            // curation screen reloaded the file and threw the computed quant away.
            usearch = null;
            dnc = null;
            analytics = null;
            MenuItemSaveFCF.IsEnabled = cgc != null;
            MenuItemSaveProject.IsEnabled = cgc != null;
            MenuItemComputeXic.IsEnabled = cgc != null;
            MenuItemReanotateSEPro.IsEnabled = cgc != null;

            home = new HomeControl();
            home.BuildFamiliesRequested += () => MenuRunUSearch_Click(this, new RoutedEventArgs());
            home.CurateRequested += ShowCurationOrOpen;
            home.ComputeXicRequested += () => MenuItemComputeXic_Click(this, new RoutedEventArgs());
            home.AnalyticsRequested += () => MenuItemAnalytics_Click(this, new RoutedEventArgs());
            home.DeNovoRequested += () => MenuItemDeNovo_Click(this, new RoutedEventArgs());

            // The card state is the honest project state: with a project open, Compute and Analytics are
            // live; with none, they stay dark until step 2 opens one.
            home.SetProjectState(cgc != null, cgc != null ? ProjectStatusText() : "No project open.");

            GridApp.Children.Add(home);

            UpdateGuide();
        }

        private void MenuItemHome_Click(object sender, RoutedEventArgs e)
        {
            ShowHome();
        }

        /// <summary>
        /// The Home "Curate families" card: back to the OPEN project when there is one, exactly as it was
        /// left (curation, undo stack, computed quant), and the file dialog only when none is open. It used
        /// to always open the dialog, so the one way back from the Home or Analytics to the curation
        /// screen reloaded the file and discarded a quantitation computed since. File · Load still opens a
        /// different file on purpose.
        /// </summary>
        private void ShowCurationOrOpen()
        {
            if (cgc == null)
            {
                MenuItemCluster_Click(this, new RoutedEventArgs());
                return;
            }

            GridApp.Children.Clear();
            usearch = null;
            dnc = null;
            analytics = null;
            home = null;
            GridApp.Children.Add(cgc);

            MenuItemSaveFCF.IsEnabled = true;
            MenuItemSaveProject.IsEnabled = true;
            MenuItemComputeXic.IsEnabled = true;
            MenuItemSpectrumComparison.IsEnabled =
                (loadedManifest?.SpectrumComparisonRows?.Count ?? 0) > 0 || loadedQuant.Count > 0;
            MenuItemReanotateSEPro.IsEnabled = true;

            UpdateGuide();
        }

        /// <summary>
        /// The prompt under the menu bar. There is a fixed sequence - database, then Family Cluster
        /// File, then quantitation project, then export - and nothing in the window used to say where
        /// you were in it or what the next step was.
        /// </summary>
        private void UpdateGuide()
        {
            // The home screen is the hub, not a step of the sequence. It names the project so the through
            // line is stated even here, where none is open.
            if (home != null)
            {
                TextBlockGuide.Text =
                    "Home · The whole flow in four steps, plus the optional de novo side-branch. " +
                    ProjectStatusText() + " Pick a step below, or open a project in step 2 to light up " +
                    "Compute full XIC and Analytics.";
                return;
            }

            // The analytics screen is NOT a step of that sequence either: it summarises what the earlier
            // steps produced, reads only, and says how to get back to the families.
            if (analytics != null)
            {
                TextBlockGuide.Text =
                    "Analytics · A read-only view of this project's own full-envelope quantitation: the XIC " +
                    "by mass, the isotopes credited per peptide, the percent of ion current by mass, what " +
                    "each family contributes, and the recovery of the total ion current. Every graph can be " +
                    "saved as a projector-sized PNG. It changes no family and reads no RAW. If it says " +
                    "there is nothing to analyse, run File · Compute / update full XIC first. The open " +
                    "project stays open: File · Save project saves it from here, and Home · Curate families " +
                    "returns to it. File · Load project or Family Cluster File opens a different one.";
                return;
            }

            // The de novo screen is NOT a step of that sequence, so it says what it is instead of a
            // step number, and it says how to get back to the families.
            if (dnc != null)
            {
                TextBlockGuide.Text =
                    "De Novo evidence · A separate workflow: select PEAKS or DeepNovo, apply the three " +
                    "source-aware filters, and reconcile the accepted calls with PatternLab by file, " +
                    "scan, charge and precursor m/z. It needs no Family Cluster File and changes no family. The " +
                    "status line at the bottom of the screen says what to do next, and the Help button " +
                    "beside it explains every number this screen shows. To return to the family " +
                    "workflow, use Home · Curate families (the open project stays open) or File · Load Family " +
                    "Cluster File for another one.";
                return;
            }

            if (cgc != null)
            {
                TextBlockGuide.Text =
                    "Step 2 of 4 · Families loaded. " + ProjectStatusText() + " Curate them here, then " +
                    "File · Compute / update full XIC gives the project its own quantitation, and Analytics " +
                    "reads it. Help · How to read these numbers explains every column.";
                return;
            }

            if (usearch != null)
            {
                TextBlockGuide.Text =
                    "Step 1 of 4 · Pick a FASTA or target-decoy (.T-R) database and press \"Input a FASTA " +
                    "file\". That writes a Family Cluster File (.fcf) next to the database. Then open it " +
                    "with File · Load project or Family Cluster File.";
                return;
            }

            TextBlockGuide.Text =
                "Start here · If you have never run this database before, use \"Run USearch\" to group its " +
                "proteins into families. If you already have a project (.fqproj) or a Family Cluster File " +
                "(.fcf), open it with File · Load project or Family Cluster File.";
        }

        /// <summary>
        /// The project through-line, in one clause: the open project's file name and whether a full XIC
        /// has been computed for it, or that none is open. It is shown on the home card and repeated in
        /// the banner, so the .fqproj is always named in the same words wherever the user is.
        /// </summary>
        private string ProjectStatusText()
        {
            if (cgc == null || lastFcfPath == null)
            {
                return "Project: none open.";
            }

            string name = Path.GetFileName(lastFcfPath);
            string quant = loadedQuant.Count > 0
                ? loadedQuant.Count.ToString("N0", CultureInfo.CurrentCulture) + " quant rows stored"
                : "no full XIC computed yet";

            string sources = loadedManifest?.DeNovoInputs != null && loadedManifest.DeNovoInputs.Count > 0
                ? "; de novo: " + string.Join(" + ", loadedManifest.DeNovoInputs
                    .GroupBy(i => i.SourceId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Min(i => i.SourcePriority))
                    .Select(g => g.First().SourceDisplayName + " " + g.First().CutoffDescription))
                : string.Empty;

            return "Project: " + name + " (" + quant + sources + ").";
        }

        private void MenuItemCluster_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "FamilyQuant project or Family Cluster File (*.fqproj;*.fcf)|*.fqproj;*.fcf|" +
                         "FamilyQuant project (*.fqproj)|*.fqproj|" +
                         "FamilyClusterFile (*.fcf)|*.fcf",
                Title = "Choose the project or Family Cluster File to work on"
            };

            if (ofd.ShowDialog() != true) return;

            try
            {
                // Read the container or the plain file BEFORE touching the layout, so a corrupt project
                // leaves the previous view in place instead of an empty window. A .fqproj restores the
                // stored quant too; an old .fcf loads exactly as it always did, with the quant empty.
                ClusterGenerationControl loaded;
                List<QuantRow> quant;
                QuantManifest? manifest;

                if (ProjectStore.IsProjectPath(ofd.FileName))
                {
                    var (fcf, rows, mf) = ProjectStore.ReadFqproj(ofd.FileName);
                    loaded = new ClusterGenerationControl(fcf) { Name = "MyClusterGenerationControl" };
                    quant = rows;
                    manifest = mf;
                }
                else
                {
                    loaded = new ClusterGenerationControl(ofd.FileName) { Name = "MyClusterGenerationControl" };
                    quant = new List<QuantRow>();
                    manifest = null;
                }

                GridApp.Children.Clear();
                cgc = loaded;
                usearch = null;
                dnc = null;
                analytics = null;
                home = null;
                lastFcfPath = ofd.FileName;
                loadedQuant = quant;
                loadedManifest = manifest;
                loadedCuration = loaded.fcf;
                GridApp.Children.Add(cgc);

                // A .fqproj carries its own quantitation: the curation table shows it at once.
                _ = cgc.SetFullXicQuantAsync(loadedQuant, loadedManifest?.PerRunTic);

                MenuItemSaveFCF.IsEnabled = true;
                MenuItemSaveProject.IsEnabled = true;
                MenuItemComputeXic.IsEnabled = true;
                MenuItemSpectrumComparison.IsEnabled =
                    (loadedManifest?.SpectrumComparisonRows?.Count ?? 0) > 0 || loadedQuant.Count > 0;
                MenuItemReanotateSEPro.IsEnabled = true;
                lastSpectrumComparisonCsvPath = null;

                UpdateGuide();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The project could not be opened, and what was on screen has been left alone.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that this really is a .fqproj or a .fcf written by FamilyQuant and " +
                    "that it is not open in another program. If it was interrupted while being saved it " +
                    "will have to be generated again from the database with \"Run USearch\".",
                    "Could not open the file", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuRunUSearch_Click(object sender, RoutedEventArgs e)
        {
            GridApp.Children.Clear();

            // Children.Clear() only detaches the cluster view; the field kept pointing at it, so
            // File -> Save afterwards wrote the previous .fcf under whatever name the user typed for the
            // database they had just clustered here. Drop it together with the menu items it enables.
            cgc = null;
            dnc = null;
            analytics = null;
            home = null;
            loadedQuant = new List<QuantRow>();
            loadedManifest = null;
            loadedCuration = null;
            lastSpectrumComparisonCsvPath = null;
            MenuItemSaveFCF.IsEnabled = false;
            MenuItemSaveProject.IsEnabled = false;
            MenuItemComputeXic.IsEnabled = false;
            MenuItemSpectrumComparison.IsEnabled = false;
            MenuItemReanotateSEPro.IsEnabled = false;

            usearch = new USearchLib();
            GridApp.Children.Add(usearch);

            UpdateGuide();
        }

        /// <summary>
        /// The de novo screen, an inspection side-branch. It KEEPS the open project off screen, like the
        /// Home and Analytics do: it used to drop it, so opening the review after a Compute threw an
        /// unsaved quantitation away. File · Save writes that open project, which is still the one on the
        /// banner; the USearch swap is the one that drops it, because it starts a different database.
        /// </summary>
        private void MenuItemDeNovo_Click(object sender, RoutedEventArgs e)
        {
            GridApp.Children.Clear();

            usearch = null;
            analytics = null;
            home = null;
            MenuItemSaveFCF.IsEnabled = cgc != null;
            MenuItemSaveProject.IsEnabled = cgc != null;
            MenuItemComputeXic.IsEnabled = false;
            MenuItemReanotateSEPro.IsEnabled = false;

            dnc = new DeNovoControl();

            // Only a suggestion, and only into an empty box: the de novo screen reads nothing until its
            // own Search button is pressed.
            dnc.SuggestFamilyClusterFile(lastFcfPath);

            GridApp.Children.Add(dnc);

            UpdateGuide();
        }

        /// <summary>
        /// The analytics screen. It swaps like the de novo screen and drops the same family fields, so
        /// File · Save cannot write a Family Cluster File that is no longer on screen. It reads the loaded
        /// project's stored quant and manifest, which it captures BEFORE clearing anything; with no quant
        /// loaded it shows its own "compute the full XIC first" message rather than crashing.
        /// </summary>
        private async void MenuItemAnalytics_Click(object sender, RoutedEventArgs e)
        {
            // The stored spectrum table's families first (its evidence pack exports them), then the view,
            // handed the curation screen's attribution so it runs no second homology search. Captured
            // before the swap, because building the view reads them and the field resets below must not
            // race the constructor.
            if (analyticsInProgress) return;
            analyticsInProgress = true;
            AnalyticsControl control;
            try
            {
                var (completed, _) = await RefreshSpectrumFamiliesAsync();
                if (!completed) return;   // the project changed while waiting; the user is elsewhere now
                PeptideFamilyAttribution? held = cgc != null && cgc.HasFullXic && ReferenceEquals(cgc.fcf, loadedCuration)
                    ? cgc.CurrentFullXicAttribution() : null;
                control = new AnalyticsControl(loadedQuant, loadedManifest, loadedCuration, held);
            }
            finally
            {
                analyticsInProgress = false;
            }

            GridApp.Children.Clear();

            // The open project (cgc) is KEPT, off screen, like the Home keeps it: Analytics is the step
            // after Compute, and dropping the project here made a computed quantitation unsaveable and
            // the Home cards go dark. Save writes that open project; Compute stays off on this screen,
            // whose figures were built from the quant as it was when it opened.
            usearch = null;
            dnc = null;
            home = null;
            analytics = control;
            MenuItemSaveFCF.IsEnabled = cgc != null;
            MenuItemSaveProject.IsEnabled = cgc != null;
            MenuItemComputeXic.IsEnabled = false;
            MenuItemReanotateSEPro.IsEnabled = false;

            GridApp.Children.Add(analytics);

            UpdateGuide();
        }

        private void MenuItemSaveFCF_Click(object sender, RoutedEventArgs e)
        {
            // Use the control we created in MenuItemCluster_Click. Reading GridApp.Children[0] and casting
            // it threw an InvalidCastException whenever the USearch panel was the one on screen.
            if (cgc == null || cgc.fcf == null)
            {
                MessageBox.Show(
                    "Nothing was saved: there are no families on screen to save.\n\n" +
                    "Next step: open a Family Cluster File with File · Load Family Cluster File, or build " +
                    "one from a database with Run USearch.",
                    "Nothing to save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "FamilyClusterFile (*.fcf)|*.fcf",
                Title = "Save the current families as a Family Cluster File"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                cgc.SaveFamilyClusterFile(sfd.FileName);

                MessageBox.Show(
                    "The families were saved to\n" + sfd.FileName + "\n\n" +
                    "The file holds the grouping, your User IDs and your annotations. It does not hold the " +
                    "quantitation: reopening it restores the families, and the percentages come back when " +
                    "you load a PatternLab project again.\n\n" +
                    "Next step: nothing. Carry on working, or open this file later to return to exactly " +
                    "this grouping.",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The file could not be written, so nothing was saved.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the file is not open in another program and that you can write " +
                    "to that folder, then save again.",
                    "Could not save", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Saves the whole project - curation AND the stored quantitation - as a .fqproj container. This
        /// is the go-forward Save. Migration from an old .fcf is exactly this path: open the .fcf (its
        /// quant is empty), then Save project writes a container whose curation is carried over unchanged
        /// and whose quant is empty until Compute / update full XIC fills it.
        /// </summary>
        private async void MenuItemSaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (saveInProgress) return;   // a Save already waiting; it will report when it finishes

            if (cgc == null || cgc.fcf == null)
            {
                MessageBox.Show(
                    "Nothing was saved: there is no project on screen to save.\n\n" +
                    "Next step: open a project or a Family Cluster File with File · Load, or build one " +
                    "from a database with Run USearch.",
                    "Nothing to save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "FamilyQuant project (*.fqproj)|*.fqproj",
                Title = "Save the project as a FamilyQuant project",
                DefaultExt = ".fqproj",
                AddExtension = true,
            };

            if (sfd.ShowDialog() != true) return;

            // ONE project bundle, captured now and written as captured: the refresh below can wait for a
            // background attach, and a project opened or recomputed meanwhile must never be mixed with
            // this one in a file (its curation and quant with this manifest).
            ClusterGenerationControl project = cgc;
            FamilyClusterFile curation = cgc.fcf;
            List<QuantRow> quant = loadedQuant;
            QuantManifest? heldManifest = loadedManifest;
            saveInProgress = true;

            try
            {
                // The manifest travels with the quant. A project that has never been computed still gets
                // a manifest, so the container always describes itself; its updated timestamp is refreshed
                // on every save. A project with no computed quant carries the default provenance and the
                // go-forward isotope basis, both describing what a later Compute would fill.
                QuantManifest manifest = heldManifest ?? new QuantManifest
                {
                    FormatVersion = ProjectStore.CurrentFormatVersion,
                    IsotopeCount = DeNovo.DeNovoXicExtractor.DefaultEnvelopeIsotopeCount,
                    MinPointsForQuant = DeNovo.DeNovoXicExtractor.DefaultMinPointsForQuant,
                    Ppm = DeNovo.DeNovoXicExtractor.ProductionPpm,
                    Ms1Tolerance = DeNovo.DeNovoXicExtractor.ProductionMs1Tolerance,
                    AppVersion = AppVersion(),
                    CreatedUtc = DateTime.UtcNow,
                    Provenance = QuantManifest.DefaultProvenance(),
                };
                manifest.UpdatedUtc = DateTime.UtcNow;

                var (completed, familyNote) = await RefreshSpectrumFamiliesAsync();
                if (!completed || !ReferenceEquals(project, cgc) || !ReferenceEquals(curation, cgc?.fcf)
                    || !ReferenceEquals(quant, loadedQuant) || !ReferenceEquals(heldManifest, loadedManifest))
                {
                    MessageBox.Show(
                        "Nothing was saved: the open project changed (another one was opened, or its quantitation " +
                        "was recomputed) while the save was waiting for the family attribution to finish.\n\n" +
                        "Next step: save again.",
                        "Nothing saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ProjectStore.WriteFqproj(sfd.FileName, curation, quant, manifest);
                loadedManifest = manifest;

                MessageBox.Show(
                    "The project was saved to\n" + sfd.FileName + "\n\n" +
                    "The container holds the grouping, your User IDs and annotations, and " +
                    quant.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " stored quant row(s). Reopening it restores both.\n\n" +
                    (familyNote != null ? familyNote + "\n\n" : string.Empty) +
                    "Next step: nothing. Carry on working, or open this file later to return to exactly " +
                    "this project.",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The project could not be written, so nothing was saved.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the file is not open in another program and that you can write " +
                    "to that folder, then save again.",
                    "Could not save", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                saveInProgress = false;
            }
        }

        /// <summary>
        /// Opens the Compute / update full XIC window and, if it produced numbers, stores them as this
        /// project's quant part. The window does the long work off its own thread; this handler only
        /// hands it the app version and takes back the result. A Save project then persists it.
        /// </summary>
        private void MenuItemComputeXic_Click(object sender, RoutedEventArgs e)
        {
            if (cgc == null || cgc.fcf == null)
            {
                MessageBox.Show(
                    "There is no project on screen to compute quantitation for.\n\n" +
                    "Next step: open a project or a Family Cluster File with File · Load first.",
                    "Load a project first", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new ComputeXicWindow(AppVersion(), loadedManifest, loadedCuration)
            {
                Owner = this,
            };

            window.ShowDialog();

            // The window holds its result whether it was closed with the button or the title bar, so it
            // is read after the dialog returns rather than on a dialog result. A computation that finished
            // replaces the quant part; a window closed without computing leaves the project untouched.
            if (window.Computed && window.ResultRows != null && window.ResultManifest != null)
            {
                loadedQuant = window.ResultRows;
                loadedManifest = window.ResultManifest;
                lastSpectrumComparisonCsvPath = window.SpectrumComparisonCsvPath;
                MenuItemSpectrumComparison.IsEnabled =
                    (loadedManifest?.SpectrumComparisonRows?.Count ?? 0) > 0 || loadedQuant.Count > 0;

                // The curation table's Full XIC column follows the new quantitation at once (the users
                // asked for the numbers in the table as well as in Analytics).
                _ = cgc.SetFullXicQuantAsync(loadedQuant, loadedManifest?.PerRunTic);

                // Started from the Home card, the banner and the guide still said "no full XIC computed
                // yet"; they read the quant just stored.
                home?.SetProjectState(true, ProjectStatusText());
                UpdateGuide();

                MessageBox.Show(
                    window.ResultRows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " quant row(s) were computed and are now held by the project.\n\n" +
                    (window.TicShareSummary.Count > 0
                        ? string.Join("\n", window.TicShareSummary) + "\n\n"
                        : string.Empty) +
                     (string.IsNullOrWhiteSpace(window.SpectrumComparisonCsvPath)
                         ? string.Empty
                         : "The spectrum comparison CSV has already been written to\n" +
                           window.SpectrumComparisonCsvPath + "\n\n") +
                    (string.IsNullOrWhiteSpace(window.SpectrumComparisonCsvError)
                        ? string.Empty
                        : window.SpectrumComparisonCsvError + "\n\n") +
                     "Next step: File · Save project (.fqproj) to persist the quant rows and the complete " +
                    "spectrum evidence table inside the project. The project itself has not been saved yet.",
                    "Quantitation computed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Opens the source-resolved spectrum table persisted with the loaded project. Legacy projects
        /// without that section fall back to a core rebuild from their stored quant rows. Neither path is
        /// a UI-side join, so a just-computed and saved/reloaded project show the same scientific grain.
        /// </summary>
        /// <summary>
        /// Brings the stored spectrum table's family columns in line with the CURRENT grouping before
        /// anything reads or writes them (Save project, the spectrum view, Analytics and its evidence pack).
        /// They were built at Compute against the curation of that moment; after a merge or a renumbering a
        /// saved project would otherwise carry the old assignments beside the new families. Only the family
        /// columns change; every measured field stays as computed. The attribution is the curation screen's
        /// cached one when it is ready, or a fresh homology search otherwise.
        /// </summary>
        /// <returns>Completed is false when the open project, its quant or its manifest was replaced while
        /// the refresh waited (then nothing was refreshed and the caller must not go on as if it had);
        /// Note is a sentence for the user, or null.</returns>
        private async Task<(bool Completed, string? Note)> RefreshSpectrumFamiliesAsync()
        {
            ClusterGenerationControl? control = cgc;
            QuantManifest? manifest = loadedManifest;
            List<QuantRow> quant = loadedQuant;
            List<SpectrumComparisonRow>? rows = manifest?.SpectrumComparisonRows;
            if (control?.fcf == null || manifest == null || rows == null || rows.Count == 0 || quant.Count == 0)
                return (true, null);

            // The background attach is awaited, so its homology hits are reused instead of a second search
            // starting on the interface thread. A replaced attach releases its waiters at once, so the wait
            // is repeated until the readiness it waited on is still the current one.
            while (true)
            {
                Task ready = control.FullXicReady;
                await ready;
                if (ReferenceEquals(ready, control.FullXicReady)) break;
            }

            // Everything it is about to touch must still be the open project's.
            if (!ReferenceEquals(control, cgc) || !ReferenceEquals(manifest, loadedManifest)
                || !ReferenceEquals(quant, loadedQuant)) return (false, null);

            string? note = FamilyEnvelopeQuant.RefreshSpectrumFamilies(rows, manifest.FamilyMapping, control.fcf,
                identity =>
                {
                    PeptideFamilyAttribution? cached = control.CurrentFullXicAttribution();
                    return cached != null && cached.IdentityThreshold == identity
                        ? cached
                        : FamilyEnvelopeQuant.BuildAttributionFromCuration(quant, control.fcf,
                            new Homology.HomologyOptions { MinIdentity = identity });
                },
                control.FullXicProteinFingerprint);
            return (true, note);
        }

        // One Save, one Analytics open and one spectrum view at a time: their handlers await, and a second
        // click must not start a second run against a project the first is still working on.
        private bool saveInProgress;
        private bool analyticsInProgress;
        private bool spectrumViewInProgress;

        private async void MenuItemSpectrumComparison_Click(object sender, RoutedEventArgs e)
        {
            if (spectrumViewInProgress) return;
            spectrumViewInProgress = true;
            try
            {
                var (completed, _) = await RefreshSpectrumFamiliesAsync();
                if (!completed) return;
            }
            finally
            {
                spectrumViewInProgress = false;
            }

            bool havePersistedRows = loadedManifest?.SpectrumComparisonRows != null
                && loadedManifest.SpectrumComparisonRows.Count > 0;
            if (!havePersistedRows && loadedQuant.Count == 0)
            {
                MessageBox.Show(
                    "There is no spectrum comparison table to show yet.\n\n" +
                    "Next step: load a .fqproj that contains full quantitation, or run File · Compute / " +
                    "update full XIC first.",
                    "No spectrum table yet", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IReadOnlyList<SpectrumComparisonRow> rows =
                    havePersistedRows
                        ? loadedManifest!.SpectrumComparisonRows
                        : SpectrumComparisonTable.Build(loadedQuant);
                SpectrumComparisonWindow.Show(rows, this, lastSpectrumComparisonCsvPath,
                    loadedManifest?.DeNovoAcceptedScoreScatter);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The spectrum comparison table could not be built, and the project was not changed.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: recompute the full XIC with the PEAKS de-novo-only + DeepNovo union if " +
                    "this is an older project that does not carry source-resolved evidence.",
                    "Could not build spectrum table", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuItemReanotateSEPro_Click(object sender, RoutedEventArgs e)
        {
            if (cgc == null || cgc.fcf == null)
            {
                MessageBox.Show(
                    "Nothing was reannotated: there are no families on screen to write into a SEPro " +
                    "package.\n\n" +
                    "Next step: open a Family Cluster File with File · Load Family Cluster File first.",
                    "Load the families first", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "SEPro 2.0 (*.sepr2)|*.sepr2",
                Title = "Choose a TRUSTED SEPro result package (legacy unsafe deserialization)"
            };

            if (ofd.ShowDialog() != true) return;

            try
            {
                var spckg = SeproPckg2.ResultPackage.Load(ofd.FileName);
                Console.WriteLine("SEPro Package loaded");

                // Map locus -> curated family id (UserID) once for O(1) lookups.
                var userIdByLocus = cgc.fcf.MyExtendedFastaItems
                    .GroupBy(a => a.SequenceIdentifier)
                    .ToDictionary(g => g.Key, g => g.First().UserID);

                // Validate up front. If any protein in the package is missing from the database we abort
                // WITHOUT saving, so we never write a half-reannotated package (the old code broke out of
                // the loop on the first miss but then saved the partially-updated result anyway).
                var missing = spckg.MyProteins.MyProteinList
                    .Where(p => !userIdByLocus.ContainsKey(p.Locus))
                    .Select(p => p.Locus)
                    .Distinct()
                    .ToList();

                if (missing.Count > 0)
                {
                    MessageBox.Show(
                        "Nothing was written: " + missing.Count.ToString("N0", CultureInfo.CurrentCulture) +
                        " protein(s) in the SEPro package are not in the database these families came " +
                        "from, so they have no family number to receive.\n\n" +
                        "This normally means the search and the clustering used different FASTA files, or " +
                        "different versions of the same one.\n\n" +
                        "Next step: recluster the exact database the SEPro search used, with Run USearch, " +
                        "and try again.\n\n" +
                        "Examples of the missing identifiers: " +
                        string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? ", ..." : ""),
                        "The package and the database do not match", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                foreach (var ptn in spckg.MyProteins.MyProteinList)
                {
                    ptn.GroupNo = userIdByLocus[ptn.Locus];
                }

                var newFileName = ofd.FileName.Remove(ofd.FileName.Length - 6, 6) + "_regrouped.sepr2";
                spckg.Save(newFileName);

                MessageBox.Show(
                    spckg.MyProteins.MyProteinList.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " protein(s) were given their family number and written to\n" + newFileName + "\n\n" +
                    "The number written is the User ID column, so any family you merged by hand is what " +
                    "went in. The file you chose was not modified.\n\n" +
                    "Next step: use the _regrouped package in the rest of the PatternLab pipeline.",
                    "SEPro package reannotated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The SEPro package could not be reannotated, and no file was written.\n\n" +
                    "Reason given by the program: " + ex.Message + "\n\n" +
                    "Next step: check that the .sepr2 opens in SEPro and that you can write to its folder, " +
                    "then try again.",
                    "Could not reannotate", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuItemHowToRead_Click(object sender, RoutedEventArgs e)
        {
            // Two workflows, two pages. Neither page's text applies to the other's screen, so the menu
            // shows the one belonging to what is actually on display.
            if (dnc != null)
            {
                dnc.ShowHelp();
                return;
            }

            // When a cluster view is open it can name the denominator actually in use; otherwise the help
            // explains what the choice will be, without claiming a denominator nothing has established.
            if (cgc != null)
            {
                cgc.ShowHelp();
                return;
            }

            TextReportWindow.Show(
                "FamilyQuant · How to read these numbers",
                HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique),
                this);
        }

        private void MenuItemWhereAmI_Click(object sender, RoutedEventArgs e)
        {
            TextReportWindow.Show("FamilyQuant · What do I do first?", WorkflowText(), this, wrap: true);
        }

        /// <summary>
        /// The order of work in one page, with what each step needs and what it produces. Kept here
        /// rather than in a text file so it cannot go missing from an installation.
        /// </summary>
        private string WorkflowText()
        {
            return
                "WHAT DO I DO FIRST?\n" +
                "===================\n\n" +
                "You are at: " + TextBlockGuide.Text + "\n\n" +
                "STEP 1 · Group the proteins into families\n" +
                "   Menu: Run USearch\n" +
                "   Needs: a protein database, either a plain .fasta or a target-decoy .T-R file.\n" +
                "   Produces: a Family Cluster File (.fcf) written next to the database.\n" +
                "   Choose the sequence identity first. 85% is the value the box starts at. \"Scan\n" +
                "   identity range\" shows how the family count and the target/decoy mixing behave\n" +
                "   across the whole range, which is a better basis for the choice than a single\n" +
                "   number. Divide and Conquer finds the lowest identity with no mixing at all; that\n" +
                "   is a floor, not a recommendation, and Help · How to read these numbers explains\n" +
                "   why.\n\n" +
                "STEP 2 · Open the families\n" +
                "   Menu: File · Load Family Cluster File\n" +
                "   Needs: the .fcf from step 1.\n" +
                "   You can now merge families further by their description words, or build one family\n" +
                "   from a keyword list. Both are shown in Layer 1 and neither can be undone, so save\n" +
                "   under a new name if you want to keep the previous grouping.\n\n" +
                "STEP 3 · Attach the quantitation\n" +
                "   Layer 2: Load PLP File\n" +
                "   Needs: a PatternLab project (.plp) exported at PEPTIDE level. A protein-level\n" +
                "   export contains nothing that can be attributed to a family, and every row will\n" +
                "   read 0.\n" +
                "   Then in Layer 3 choose what the percentages are a share of, choose what to do with\n" +
                "   peptides that belong to several families, and press Recalculate. Modes the project\n" +
                "   cannot support are greyed out, each with the reason in its own tooltip.\n\n" +
                "STEP 4 · Take the result away\n" +
                "   Export to CSV writes exactly the rows on screen, with the shares as fractions\n" +
                "   between 0 and 1 and a dot for the decimal separator, so any program can read them.\n" +
                "   File · Save Family Cluster File keeps the grouping for next time.\n" +
                "   File · Reanotate Sepro stamps your User ID family numbers into a copy of a SEPro\n" +
                "   result package, leaving the original untouched.\n\n" +

                // The de novo screen is a separate workflow and is written as one: it is numbered
                // OPTIONAL rather than as a fifth step, because nothing above it needs it and it needs
                // nothing above it except, for its Layer 4, a Family Cluster File.
                "OPTIONAL · Review de novo evidence\n" +
                "   Menu: De Novo evidence\n" +
                "   A separate workflow, not a step of the four above. It changes no family, no User ID\n" +
                "   and no quantitation, and it needs no Family Cluster File to start. Use it to ask\n" +
                "   how the selected de novo calls relate to PatternLab. Its two core steps are:\n" +
                "     1. Select PEAKS or DeepNovo and keep calls passing all three filters: minimum\n" +
                "        length, the source-native score, and a consecutive local-confidence stretch.\n" +
                "     2. Compare them with a SEPro search of the SAME runs. Each kept row comes back\n" +
                "        as new territory, a co-isolated second species, a redundant confirmation, an\n" +
                "        agreement PatternLab itself scored weakly, or a straight disagreement. The\n" +
                "        last two are listed as a review queue, because they are decisions.\n" +
                "   The collapsed Standalone run (advanced) section retains extraction, family\n" +
                "   attribution and ion-current partitioning for work without a saved project.\n" +
                "   That screen has a Help button of its own, and while it is open Help · How to read\n" +
                "   these numbers shows its page instead of this one.\n";
        }
    }
}
