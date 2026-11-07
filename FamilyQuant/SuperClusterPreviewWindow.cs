using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using FamilyQuant.Evidence;

namespace FamilyQuant
{
    public partial class SuperClusterPreviewWindow : Window
    {
        private readonly GroupingReviewViewModel model;
        private List<SortDescription> sorts = new();
        public bool Confirmed { get; private set; }
        public EvidenceReviewSelection? EvidenceSelection { get; private set; }
        public bool IsEvidenceMode => model.ModeIndex == 1;
        public SuperClusterPreviewWindow(SuperClusterPreview preview) : this(preview, null, null) { }
        public SuperClusterPreviewWindow(SuperClusterPreview preview, EvidenceReviewInput? input,
            Func<string, EvidenceReviewInput>? propose, Func<string, string>? motif = null,
            string initialScope = "", bool evidenceMode = false,
            Func<CancellationToken, Task<EvidenceTaskScopeInput>>? discoverTask = null,
            Func<IReadOnlyList<PanelFamilyLinkSelection>, EvidenceReviewInput>? confirmPanelLinks = null,
            Func<IProgress<FetchProgress>?, CancellationToken, Task<EvidenceTaskScopeInput>>? discoverTaskWithProgress = null,
            Func<string, string, CancellationToken, Task<EvidenceScopePreview>>? previewScope = null,
            Func<EvidenceManualRequest, EvidenceReviewInput>? addToFamily = null,
            Func<IReadOnlyList<int>, EvidenceReviewInput>? removeFromFamily = null,
            Func<IReadOnlyList<EvidenceManualChoice>, EvidenceReviewInput>? updateManualChoices = null,
            Func<string, PanelEvidenceMatchContext>? createMatchContext = null,
            Func<string, Panels.PanelConfirmedEvidenceMatches, EvidenceReviewInput>? confirmPanelMatches = null,
            Func<string, FamilyQuant.Operations.OperationContext, Task>? fetchMissingPanel = null, Func<string, string>? panelCoverage = null, Func<string, bool>? panelCovered = null,
            Func<string>? fullPanelRequirement = null)
        {
            InitializeComponent();
            this.createMatchContext = createMatchContext;
            model = new GroupingReviewViewModel(preview, input, propose, motif, initialScope, evidenceMode, discoverTask, confirmPanelLinks, discoverTaskWithProgress,
                previewScope, addToFamily, removeFromFamily, updateManualChoices, confirmPanelMatches, fetchMissingPanel, panelCoverage, panelCovered, fullPanelRequirement);
            Loaded += async (_, _) => await DiscoverInitialTaskAsync();
            Closed += (_, _) => model.CancelPending();
            DataContext = model;
            AppOperationHost.SetScope(this, Operations);
            BusyOverlay.AttachWindow(this, Operations);
            model.Keyword.Refreshing += (_, _) => sorts = grid.Items.SortDescriptions.ToList();
            model.Keyword.Refreshed += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(RestoreSort));
            GridCopy.Attach(grid, row => (row as SuperClusterPreviewProtein)?.Locus, row => (row as SuperClusterPreviewProtein)?.Description);
            GridCopy.Attach(evidenceGrid, row => (row as ReviewProteinRow)?.Identifier, row => (row as ReviewProteinRow)?.Description);
            GridCopy.Attach(leftOutGrid, row => (row as ReviewLeftOutInput)?.Identifier, row => (row as ReviewLeftOutInput)?.Description);
            GridCopy.Attach(unassignedGrid, row => (row as ReviewProteinRow)?.Identifier, row => (row as ReviewProteinRow)?.Description);
            InstallKeywordHeaders();
            model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(GroupingReviewViewModel.CanConfirm) or nameof(GroupingReviewViewModel.ApplyHint))
                    UpdateApplyToolTip();
            };
            model.Keyword.Refresh();
            UpdateApplyToolTip();
        }
        private async Task DiscoverInitialTaskAsync()
        {
            if (model.ModeIndex == 1 && model.TaskDiscoveryAvailable && !model.HasTaskScope)
                await RunReviewAsync("Read grouping task", model.DiscoverTaskAsync, taskDiscovery: true);
        }
        private async void ReviewModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && ReferenceEquals(e.OriginalSource, sender)) await DiscoverInitialTaskAsync();
        }
        private async void DiscoverTask_Click(object sender, RoutedEventArgs e) => await RunReviewAsync("Read grouping task", model.DiscoverTaskAsync, taskDiscovery: true);
        private void RestoreSort()
        {
            grid.Items.SortDescriptions.Clear();
            foreach (var sort in sorts)
            {
                grid.Items.SortDescriptions.Add(sort);
                var column = grid.Columns.FirstOrDefault(c => c.SortMemberPath == sort.PropertyName);
                if (column != null) column.SortDirection = sort.Direction;
            }
        }
        private void KeywordTick_Click(object sender, RoutedEventArgs e) => model.Keyword.FollowTicks();
        private void UntickRed_Click(object sender, RoutedEventArgs e) => model.Keyword.UntickWithoutKeyword();
        private void TickAll_Click(object sender, RoutedEventArgs e) => model.Keyword.TickAll();
        private void UntickSelected_Click(object sender, RoutedEventArgs e) => model.Keyword.SetTicks(grid.SelectedItems.Cast<SuperClusterPreviewProtein>().ToArray(), false);
        private void TickSelected_Click(object sender, RoutedEventArgs e) => model.Keyword.SetTicks(grid.SelectedItems.Cast<SuperClusterPreviewProtein>().ToArray(), true);
        private void Domains_Click(object sender, RoutedEventArgs e) => model.Keyword.KeepDomains();
        private void UntickFlagged_Click(object sender, RoutedEventArgs e) => model.Evidence?.UntickFlagged();
        private void AllEvidenceProteins_Click(object sender, RoutedEventArgs e) { if (model.Evidence != null) model.Evidence.SelectedFamily = null; }
        private void EvidenceTickSelected_Click(object sender, RoutedEventArgs e) => model.Evidence?.SetSelected(evidenceGrid.SelectedItems.Cast<ReviewProteinRow>().ToArray(), true);
        private void EvidenceUntickSelected_Click(object sender, RoutedEventArgs e) => model.Evidence?.SetSelected(evidenceGrid.SelectedItems.Cast<ReviewProteinRow>().ToArray(), false);
        private async void UpdateProposal_Click(object sender, RoutedEventArgs e) => await RunReviewAsync("Update grouping proposal", model.UpdateProposalAsync);
        private async void UpdatePanelLinks_Click(object sender, RoutedEventArgs e) => await RunReviewAsync("Confirm panel links", model.UpdatePanelLinksAsync);
        private async void Motif_Click(object sender, RoutedEventArgs e) => await RunReviewAsync("Count motif", model.CheckMotifAsync);
        private void Create_Click(object sender, RoutedEventArgs e)
        {
            familyGrid.CommitEdit(DataGridEditingUnit.Cell, true); familyGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (!model.CanConfirm) return;
            if (IsEvidenceMode) EvidenceSelection = model.Evidence!.CaptureSelection() with { MotifPattern = model.MotifPattern };
            Confirmed = true; Close();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) { Confirmed = false; Close(); }
        private void UpdateApplyToolTip()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(UpdateApplyToolTip));
                return;
            }
            if (!model.CanConfirm && !model.IsBusy && model.ModeIndex == 0)
            {
                createButton.ToolTip = model.Keyword.Headline;
                return;
            }
            string next = model.IsBusy ? "Wait for the current review operation to finish"
                : model.ApplyHint.TrimEnd('.');
            createButton.ToolTip = model.CanConfirm
                ? "Commit the reviewed ticks, family names and splits as one project grouping change. Undo restores the preceding grouping, and Save writes the project to disk."
                : "Apply grouping is unavailable: " + next + ". The project stays unchanged until Apply grouping, after which Undo restores the preceding grouping and Save writes it to disk.";
        }
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            var help = TextReportWindow.Create("Grouping review help", GroupingReviewHelp.ForMode(model.ModeIndex == 1, model.Evidence?.HasPanelRules == true)
                + (model.Evidence == null ? "" : "\n\nCurrent proposal\n" + model.Evidence.Source.RuleSummary), true);
            help.Owner = this; help.WindowStartupLocation = WindowStartupLocation.CenterOwner; help.Show();
        }
        private void InstallKeywordHeaders()
        {
            string[] facts = {
                "Whether this protein is selected to join the keyword grouping on Apply grouping. Unticking keeps its current family, and Undo restores an applied grouping.",
                "Protein identifier exactly as retained from the database, including its prefixes.",
                "Original database description, including OS= text. Keyword matching reads only the description before OS=.",
                "Stored UniProt Pfam calls for this protein. An empty cell is a missing call, not proof that a domain is absent.",
                "Whether this protein's description before OS= carries the required entered keyword or phrase under the chosen all/any rule.",
                "Current document User ID before the reviewed grouping is applied.",
                "Number of keyword-carrying proteins over the total proteins in this current family. The fraction explains why a family is offered even when this protein carries no keyword."
            };
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                    Application.Current.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as Style);
                style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, facts[i]));
                grid.Columns[i].HeaderStyle = style;
            }
        }
    }
}
