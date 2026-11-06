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
            Func<string, string, CancellationToken, Task<EvidenceScopePreview>>? previewScope = null)
        {
            InitializeComponent();
            model = new GroupingReviewViewModel(preview, input, propose, motif, initialScope, evidenceMode, discoverTask, confirmPanelLinks, discoverTaskWithProgress, previewScope);
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
            model.Keyword.Refresh();
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
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            var help = TextReportWindow.Create("Grouping review help", GroupingReviewHelp.Text
                + "\n\n" + EvidenceHelp.Build(panelMode: model.Evidence?.HasPanelRules == true)
                + (model.Evidence == null ? "" : "\n\nCurrent proposal\n" + model.Evidence.Source.RuleSummary), true);
            help.Owner = this; help.WindowStartupLocation = WindowStartupLocation.CenterOwner; help.Show();
        }
        private void InstallKeywordHeaders()
        {
            string[] facts = {
                "Untick to keep this protein in its family; the rest of the family still joins.",
                "The protein identifier as in the database.", "The description exactly as in the database, OS= and all.",
                "The Pfam domains from UniProt. Empty when there are none yet or the protein is too short for Pfam.",
                "Whether this protein carries a keyword itself, in the description before OS=.",
                "The number of the family this protein belongs to now.",
                "How many proteins of its family carry a keyword: 2 of 11 means the family is here because of those 2."
            };
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                    Application.Current.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as Style);
                style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, facts[i]));
                style.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 60000));
                grid.Columns[i].HeaderStyle = style;
            }
        }
    }
}
