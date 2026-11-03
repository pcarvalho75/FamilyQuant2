using FamilyQuant.Evidence;
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class ClusterGenerationControl
{
    private bool familyWorldFetchRunning;
    public HttpMessageHandler? FamilyWorldHandlerOverride { get; set; }
    public HttpMessageHandler? FamilyWorldTaskHandlerOverride { get; set; }
    public Func<EvidenceTaskProposal, EvidenceTaskChoice?>? ConfirmFamilyWorldScopeOverride { get; set; }
    public Task FamilyWorldFetchCompletion { get; private set; } = Task.CompletedTask;

    private async void ButtonFetchFamilyWorld_Click(object sender, RoutedEventArgs e)
    {
        FamilyWorldFetchCompletion = FetchFamilyWorldAsync();
        await FamilyWorldFetchCompletion;
    }

    public Task FetchFamilyWorldAsync() => AppOperationHost.OnUi(this, FetchFamilyWorldCoreAsync);

    private async Task FetchFamilyWorldCoreAsync()
    {
        if (familyWorldFetchRunning || Operations.IsBusy) return;
        familyWorldFetchRunning = true;
        try
        {
            if (!NearestReviewed.Targets(fcf).Any(p => !SequenceLabels.IsContaminant(p.SequenceIdentifier)
                && p.Evidence != null && ReferencePanelFetcher.FamiliesOf(p.Evidence).Any()))
            {
                SetCurationOperationStatus("Fetch evidence first so the database task and annotated families can be read.");
                return;
            }
            EvidenceTaskChoice? choice = fcf.EvidenceRecipe?.ConfirmedTask ?? fcf.EvidenceSource?.FamilyWorld?.Task;
            if (choice != null)
            {
                try { choice = EvidenceTaskDiscovery.Confirm(fcf, choice.Proposal, choice.ScopeTerms); }
                catch (InvalidOperationException) { choice = null; }
            }
            if (choice == null)
            {
                EvidenceTaskProposal? proposal = null;
                if (!await RunCurationAsync("Read database task", async operation =>
                    proposal = await EvidenceTaskDiscovery.DiscoverAsync(fcf, FamilyWorldTaskHandlerOverride,
                        progress: new FamilyWorldTaskProgress(operation),
                        cancellationToken: operation.CancellationToken))) return;
                choice = ConfirmFamilyWorldScopeOverride != null ? ConfirmFamilyWorldScopeOverride(proposal!)
                    : ConfirmFamilyWorldScope(proposal!);
                if (choice == null) { SetCurationOperationStatus("Family world cancelled. The previous document was kept."); return; }
                // Confirmation is read-only until the completed world is published by Core.
                choice = EvidenceTaskDiscovery.Confirm(fcf, choice.Proposal, choice.ScopeTerms);
            }
            await RunCurationAsync("Fetch family world", async operation =>
            {
                await ViewModel.FetchFamilyWorldAsync(choice, operation, FamilyWorldHandlerOverride);
                SetCurationOperationStatus("Family world stored. Save the project, then Compute to populate Database gap.");
            });
        }
        catch (Exception ex) { SetCurationOperationStatus("Family world could not finish: " + ex.Message); }
        finally { familyWorldFetchRunning = false; }
    }

    private sealed class FamilyWorldTaskProgress(FamilyQuant.Operations.OperationContext operation) : IProgress<FetchProgress>
    {
        public void Report(FetchProgress value) => operation.Report("task", "Reading database task: "
            + value.CompletedBatches.ToString(CultureInfo.CurrentCulture) + " of "
            + value.TotalBatches.ToString(CultureInfo.CurrentCulture) + " taxa.", value.CompletedBatches, value.TotalBatches);
    }

    private EvidenceTaskChoice? ConfirmFamilyWorldScope(EvidenceTaskProposal proposal)
    {
        var dialog = new Window { Title = "Confirm family-world scope", Width = 520, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Database task: " + proposal.Clade.ScientificName + ". UniProt release " + proposal.Release + ".",
            TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Confirm the scope words or quoted phrases. Suggestions can include a reviewed keyword and a tissue annotation phrase. The stored world will contain family references for this scope.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 4) });
        var scope = new TextBox { Text = string.Join(", ", proposal.ProposedScopeTerms.ConvertAll(term => "\"" + term + "\"")),
            ToolTip = "Scope words or quoted phrases, as in Group by evidence." };
        panel.Children.Add(scope);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var fetch = new Button { Content = "Confirm and fetch", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        EvidenceTaskChoice? result = null;
        fetch.Click += (_, _) =>
        {
            var terms = WordCluster.ParseKeywords(scope.Text);
            if (terms.Count == 0) { error.Text = "Type at least one scope word or quoted phrase."; return; }
            try { result = EvidenceTaskDiscovery.Confirm(fcf, proposal, terms); dialog.DialogResult = true; }
            catch (ArgumentException ex) { error.Text = ex.Message; }
            catch (InvalidOperationException ex) { error.Text = ex.Message; }
        };
        buttons.Children.Add(fetch); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true ? result : null;
    }
}
