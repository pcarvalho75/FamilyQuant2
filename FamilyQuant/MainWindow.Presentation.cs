using Microsoft.Win32;
using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class MainWindow
{
    private sealed class ShellViewPresenter
    {
        private readonly MainWindow owner;
        public ShellViewPresenter(MainWindow owner) => this.owner = owner;

        public void PresentHome()
        {
            owner.GridApp.Children.Clear();

            // The transient screens are dropped, but the OPEN PROJECT is kept: going to the hub must not
            // throw away a loaded (and possibly unsaved) curation, and the Compute and Analytics cards can
            // only light up if the project they need is still here. The Save commands stay live while it
            // is open, because what they write is that open project (cgc, with the quant beside it), still
            // the one named on the banner. The old hazard was a Save writing a file that was NO LONGER the
            // open one, after a swap to a different database; that cannot happen here. Disabling them
            // left a Compute started from the Home card with no way to be saved: every route back to the
            // curation screen reloaded the file and threw the computed quant away.
            owner.usearch = null;
            owner.dnc = null;
            owner.analytics = null;


            owner.home = new HomeControl();
            owner.home.OpenRequested += () => owner.MenuItemCluster_Click(owner, new RoutedEventArgs());
            owner.home.OpenPathRequested += async path => await owner.OpenProjectSafelyAsync(path);
            owner.home.RemoveRecentProjectRequested += owner.RemoveRecentProject;
            owner.home.LastResultRequested += owner.ShowLastResult;
            owner.home.BuildFamiliesRequested += () => owner.MenuRunUSearch_Click(owner, new RoutedEventArgs());
            owner.home.CurateRequested += owner.ShowCurationOrOpen;
            owner.home.ComputeXicRequested += () => owner.MenuItemComputeXic_Click(owner, new RoutedEventArgs());
            owner.home.AnalyticsRequested += () => owner.MenuItemAnalytics_Click(owner, new RoutedEventArgs());
            owner.home.DeNovoRequested += () => owner.MenuItemDeNovo_Click(owner, new RoutedEventArgs());

            // The step states are the honest project state: with a project open, Quantify and Results are
            // live; with none, they stay dark until Curate families or Open project opens one.
            owner.home.SetProjectState(owner.cgc != null, owner.cgc != null ? owner.ProjectStatusText() : "No project open.");
            owner.home.SetRecentProjects(owner.shellState.RecentProjects);
            owner.home.SetLastResult(owner.shellState.LastResultProject, owner.cgc != null && owner.loadedQuant.Count > 0);

            owner.GridApp.Children.Add(owner.home);

            owner.UpdateGuide();
        }

        public void MenuItemHome_Click(object sender, RoutedEventArgs e)
        {
            owner.ShowHome();
        }

        public void PresentCurationOrOpen()
        {
            if (owner.cgc == null)
            {
                // Curate families needs a project: ask for one, or offer Build families (Z4d).
                switch (owner.AskCurateWithoutProject())
                {
                    case CurateWithoutProjectChoice.OpenProject: owner.MenuItemCluster_Click(owner, new RoutedEventArgs()); break;
                    case CurateWithoutProjectChoice.BuildFamilies: owner.NavigationModel.RequestNavigate(ShellDestination.Build); break;
                }
                return;
            }

            owner.GridApp.Children.Clear();
            owner.usearch = null;
            owner.dnc = null;
            owner.analytics = null;
            owner.home = null;
            owner.GridApp.Children.Add(owner.cgc);

            owner.RefreshProjectCommandAvailability(resetSpectrum: true);

            owner.UpdateGuide();
        }

        public void PresentBuild()
        {
            owner.DocumentState.Clear();
            owner.GridApp.Children.Clear();

            // Children.Clear() only detaches the cluster view; the field kept pointing at it, so
            // File -> Save afterwards wrote the previous .fcf under whatever name the user typed for the
            // database they had just clustered here. Drop it together with the menu items it enables.
            owner.cgc = null;
            owner.dnc = null;
            owner.analytics = null;
            owner.home = null;
            owner.loadedQuant = new List<QuantRow>();
            owner.loadedManifest = null;
            owner.loadedCuration = null;
            owner.lastSpectrumComparisonCsvPath = null;


            owner.usearch = new USearchLib();
            owner.GridApp.Children.Add(owner.usearch);

            owner.UpdateGuide();
        }

        public void PresentDeNovo()
        {
            owner.GridApp.Children.Clear();

            owner.usearch = null;
            owner.analytics = null;
            owner.home = null;


            owner.dnc = new DeNovoControl(owner.SharedDeNovoFilters());
            owner.dnc.ViewModel.Operations = owner.Operations;
            owner.dnc.HomologyCompleted += owner.KeepHomologyResult;

            // Only a suggestion, and only into an empty box: the de novo screen reads nothing until its
            // own Search button is pressed.
            owner.dnc.SuggestFamilyClusterFile(owner.lastFcfPath);

            owner.GridApp.Children.Add(owner.dnc);

            owner.UpdateGuide();
        }

        public async void PresentResults() => await AppOperationHost.OnUi(owner, PresentResultsCoreAsync);
        private async Task PresentResultsCoreAsync()
        {
            // The stored spectrum table's families first (its evidence pack exports them), then the view,
            // handed the curation screen's attribution so it runs no second homology search. Captured
            // before the swap, because building the view reads them and the field resets below must not
            // race the constructor.
            if (owner.analyticsInProgress || owner.Operations.IsBusy) return;
            owner.analyticsInProgress = true;
            owner.RefreshNavigationState();
            AnalyticsControl control;
            using var operation = owner.Operations.Begin("Prepare results");
            try
            {
                if (owner.loadedQuant.Count > 0)
                {
                    var (completed, _) = await owner.RefreshSpectrumFamiliesAsync();
                    if (!completed) return;   // the project changed while waiting
                }
                PeptideFamilyAttribution? held = owner.cgc != null && owner.cgc.HasFullXic && ReferenceEquals(owner.cgc.fcf, owner.loadedCuration)
                    ? owner.cgc.CurrentFullXicAttribution() : null;
                var rows = owner.loadedQuant;
                var manifest = owner.loadedManifest;
                var curation = owner.loadedCuration;
                var homology = owner.latestHomologyResult;
                var model = rows.Count == 0 ? new AnalyticsViewModel(rows, manifest, curation, held, homology)
                    : await Task.Run(() => new AnalyticsViewModel(rows, manifest, curation, held, homology));
                operation.Context.ThrowIfCancellationRequested();
                control = new AnalyticsControl(model);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                owner.StatusModel.ShowMessage("Cancelled. The previous view was kept.");
                return;
            }
            catch (Exception ex)
            {
                owner.StatusModel.ShowMessage("The results could not be prepared. " + ex.Message);
                return;
            }
            finally
            {
                owner.analyticsInProgress = false;
                owner.RefreshNavigationState();
            }

            owner.GridApp.Children.Clear();

            // The open project (cgc) is KEPT, off screen, like the Home keeps it: Analytics is the step
            // after Compute, and dropping the project here made a computed quantitation unsaveable and
            // the Home cards go dark. Save writes that open project; Compute stays off on this screen,
            // whose figures were built from the quant as it was when it opened.
            owner.usearch = null;
            owner.dnc = null;
            owner.home = null;
            owner.analytics = control;
            var resultManifest = owner.loadedManifest;
            control.ViewModel.ResultsOptionsChanged += (_, _) =>
            {
                if (ReferenceEquals(owner.analytics, control) && ReferenceEquals(owner.loadedManifest, resultManifest))
                    owner.DocumentState.MarkModified();
            };

            owner.GridApp.Children.Add(owner.analytics);

            owner.UpdateGuide();
        }

        public void PresentCompute()
        {
            if (MainWindow.ComputeDialogOverride == null)
            {
                owner.PresentQuantifyScreen();
                return;
            }
            if (owner.cgc == null || owner.cgc.fcf == null)
            {
                ShellMessages.Report(owner,
                    "There is no project on screen to compute quantitation for.\n\n" +
                    "Next step: open a project or a Family Cluster File with File > Open first.",
                    "Load a project first", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool attached = PresentationSource.FromVisual(owner) != null;
            if (!attached && MainWindow.ComputeDialogOverride == null)
            {
                owner.StatusModel.ShowMessage("Quantify needs a displayed application window.");
                return;
            }
            var window = new ComputeXicWindow(MainWindow.AppVersion(), owner.loadedManifest, owner.loadedCuration, owner.SharedDeNovoFilters());
            if (attached) window.Owner = owner;
            if (MainWindow.ComputeDialogOverride != null)
            {
                try { MainWindow.ComputeDialogOverride(window); }
                finally { window.Close(); }
            }
            else window.ShowDialog();

            // The window holds its result whether it was closed with the button or the title bar, so it
            // is read after the dialog returns rather than on a dialog result. A computation that finished
            // replaces the quant part; a window closed without computing leaves the project untouched.
            if (window.Computed && window.ResultRows != null && window.ResultManifest != null)
            {
                owner.loadedQuant = window.ResultRows;
                owner.loadedManifest = window.ResultManifest;
                owner.DocumentState.MarkModified();
                owner.lastSpectrumComparisonCsvPath = window.SpectrumComparisonCsvPath;
                owner.RefreshProjectCommandAvailability(spectrumAvailable: (owner.loadedManifest?.SpectrumComparisonRows?.Count ?? 0) > 0 || owner.loadedQuant.Count > 0);

                // The curation table's Full XIC column follows the new quantitation at once (the users
                // asked for the numbers in the table as well as in Analytics).
                owner.cgc.SetFullXicMappingProvenance(owner.loadedManifest?.FamilyMapping);
                _ = owner.cgc.SetFullXicQuantAsync(owner.loadedQuant, owner.loadedManifest?.PerRunTic);

                // Started from the Home card, the banner and the guide still said "no full XIC computed
                // yet"; they read the quant just stored.
                owner.home?.SetProjectState(true, owner.ProjectStatusText());
                owner.home?.SetLastResult(owner.shellState.LastResultProject, owner.loadedQuant.Count > 0);
                owner.UpdateGuide();

                ShellMessages.Report(owner,
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
                     "Next step: File > Save (.fqproj) to persist the quant rows and the complete " +
                    "spectrum evidence table inside the project. The project itself has not been saved yet.",
                    "Quantitation computed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
