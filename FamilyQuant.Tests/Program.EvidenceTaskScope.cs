using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceTaskScopeTests()
    {
        Section("Evidence task scope - supplied facts, editable confirmation and late discovery");
        var input = new EvidenceTaskScopeInput("Supplied clade (456)",
            new[] { new ReviewTaskTaxon("Supplied species", 123), new ReviewTaskTaxon("Other supplied species", 124) },
            new[] { new ReviewTaskKeyword("Supplied keyword", 11), new ReviewTaskKeyword("Other keyword", 4) },
            16, new[] { "Supplied keyword" }, "fixture_release", TargetProteins: 50);
        SuperClusterPreview Keyword() => new(new System.Collections.Generic.List<FastaItemExtended>(), null,
            "", "", KeywordMatch.All, null, null);
        var model = new GroupingReviewViewModel(Keyword(), propose: _ => GroupingReviewScreens.Fixture(12),
            evidenceMode: true, discoverTask: _ => Task.FromResult(input));
        Check("task discovery is available without inventing any initial scope", model.CanDiscoverTask && model.ScopeText.Length == 0);
        FinishReviewWork(model.DiscoverTaskAsync);
        Check("discovery presents the exact supplied taxa, keyword counts, denominator and release",
            ReferenceEquals(model.TaskScope, input) && model.TaskScope.Taxa.Count == 2 && model.TaskScope.TargetProteins == 50
            && model.TaskScope.Keywords[0].ReviewedProteins == 11 && model.TaskScope.ReviewedProteins == 16
            && model.TaskScope.Release == "fixture_release");
        Check("discovery proposes a phrase without splitting it into separate scope terms",
            WordCluster.ParseKeywords(model.ScopeText).SequenceEqual(input.ProposedScopeTerms, StringComparer.OrdinalIgnoreCase));
        model.ScopeText = "\"User edited phrase\"";
        FinishReviewWork(model.UpdateProposalAsync);
        Check("the person may edit the scope before updating the grouping proposal",
            model.ScopeText == "\"User edited phrase\"" && model.Evidence != null && model.CanConfirm);
        model.ScopeText = "manual";
        Check("editing a confirmed scope prevents applying the old grouping proposal", !model.CanConfirm);
        FinishReviewWork(model.DiscoverTaskAsync);
        Check("rediscovery never overwrites a person's manual scope", model.ScopeText == "manual");
        var unavailable = new GroupingReviewViewModel(Keyword(), propose: _ => GroupingReviewScreens.Fixture(12),
            evidenceMode: true, discoverTask: _ => Task.FromException<EvidenceTaskScopeInput>(new InvalidOperationException("Supplied unsupported-header refusal")));
        FinishReviewWork(unavailable.DiscoverTaskAsync);
        unavailable.ScopeText = "manual";
        Check("discovery refusal is plain and preserves the manual scope path", unavailable.TaskScope == null
            && unavailable.Status == "Supplied unsupported-header refusal" && unavailable.CanUpdateProposal);

        var pending = new TaskCompletionSource<EvidenceTaskScopeInput>();
        CancellationToken observed = default;
        var cancelled = new GroupingReviewViewModel(Keyword(), evidenceMode: true,
            discoverTask: token => { observed = token; return pending.Task; });
        Task running = cancelled.DiscoverTaskAsync();
        Check("discovery disables duplicate requests while it is running", cancelled.IsBusy && !cancelled.CanDiscoverTask);
        cancelled.CancelPending(); pending.SetResult(input);
        FinishReviewWork(() => running);
        Check("closing cancels discovery and rejects late scope facts", observed.IsCancellationRequested
            && cancelled.TaskScope == null && cancelled.ScopeText.Length == 0 && !cancelled.IsBusy);
    }
}

internal static partial class Program
{
    private static void EvidenceTaskScopeWindowTests(string output)
    {
        EnsureApplication();
        System.IO.Directory.CreateDirectory(output);
        var previousTheme = Themes.ThemeManager.Current;
        var facts = new EvidenceTaskScopeInput("Supplied clade (456)",
            new[] { new ReviewTaskTaxon("Supplied species", 123), new ReviewTaskTaxon("Other supplied species", 124) },
            new[] { new ReviewTaskKeyword("Supplied keyword", 11), new ReviewTaskKeyword("Other keyword", 4) },
            16, new[] { "Supplied keyword" }, "fixture_release", "Synthetic presentation fixture, no scientific result.", TargetProteins: 50);
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                string? confirmedScope = null;
                var preview = new SuperClusterPreview(new System.Collections.Generic.List<FastaItemExtended>(), null,
                    "", "", KeywordMatch.All, null, null);
                var window = new SuperClusterPreviewWindow(preview, null,
                    scope => { confirmedScope = scope; return GroupingReviewScreens.Fixture(12); },
                    evidenceMode: true, discoverTask: _ => Task.FromResult(facts));
                window.Left = -30000; window.Top = -30000; window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                window.ShowInTaskbar = false; window.ShowActivated = false;
                try
                {
                    window.Show(); PumpDispatcher();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    var taxa = (System.Windows.Controls.DataGrid)window.FindName("taskTaxaGrid");
                    var keywords = (System.Windows.Controls.DataGrid)window.FindName("taskKeywordsGrid");
                    Check(theme + " opening the evidence window discovers and binds the supplied task facts",
                        ReferenceEquals(model.TaskScope, facts) && ReferenceEquals(taxa.ItemsSource, facts.Taxa)
                        && ReferenceEquals(keywords.ItemsSource, facts.Keywords)
                        && taxa.IsReadOnly && keywords.IsReadOnly);
                    Check(theme + " the database clade is present in the rendered view text",
                        WalkTree(window).OfType<System.Windows.Controls.TextBlock>()
                            .Any(t => t.Text == "Database clade: " + facts.Clade));
                    CheckTooltipCoverage(theme + " task scope review", window);
                    foreach (var size in new[] { (1024, 768), (1366, 820) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2; PumpDispatcher();
                        Thread.Sleep(350); PumpDispatcher();
                        ScreenHarness.Save(window, System.IO.Path.Combine(output, $"scope-{theme}-{size.Item1}.png"), size.Item1, size.Item2);
                    }
                    var scopeBox = WalkTree(window).OfType<System.Windows.Controls.TextBox>()
                        .Single(t => System.Windows.Data.BindingOperations.GetBinding(t, System.Windows.Controls.TextBox.TextProperty)?.Path.Path == "ScopeText");
                    scopeBox.Text = "\"Person edited phrase\"";
                    var update = WalkTree(window).OfType<System.Windows.Controls.Button>().Single(b => b.Content as string == "Update proposal");
                    FinishReviewWork(async () =>
                    {
                        update.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        while (model.IsBusy) await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                    });
                    PumpDispatcher();
                    var apply = (System.Windows.Controls.Button)window.FindName("createButton");
                    Check(theme + " native scope editing and confirmation reach the proposal callback", confirmedScope == scopeBox.Text
                        && model.CanConfirm && apply.IsEnabled);
                    Check(theme + " confirming scope frees table space while retaining the expandable facts", !model.TaskScopeExpanded
                        && ReferenceEquals(model.TaskScope, facts));
                    scopeBox.Text = "changed after confirmation"; PumpDispatcher();
                    Check(theme + " native Apply refuses a scope edited after confirmation", !apply.IsEnabled && !window.Confirmed);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}

internal static partial class Program
{
    private static void EvidenceTaskScopeRecipeTests()
    {
        var file = TaskFile();
        string before = file.ToJsonText();
        using var handler = new TaskTaxonomyHandler();
        var session = new EvidenceGroupingSession(file, "B9 fixture",
            discoverTask: (source, token) => Evidence.EvidenceTaskDiscovery.DiscoverAsync(source, handler, cancellationToken: token));
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
        var window = new SuperClusterPreviewWindow(preview, null, session.Propose, session.CountMotif,
            evidenceMode: true, discoverTask: session.DiscoverTaskAsync);
        window.Left = -30000; window.Top = -30000; window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
        window.ShowInTaskbar = false; window.ShowActivated = false;
        try
        {
            window.Show(); PumpDispatcher();
            var model = (GroupingReviewViewModel)window.DataContext;
            FinishReviewWork(async () =>
            {
                while (model.IsBusy) await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            });
            Check("the real B3 session discovery stays read only and supplies exact counted facts",
                file.ToJsonText() == before && handler.Requests == 3 && model.TaskScope?.TargetProteins == 3
                && model.TaskScope.ReviewedProteins == 2 && model.TaskScope.IgnoredContaminants == 1
                && model.TaskScope.Keywords[0].ReviewedProteins == 2);
            var scopeBox = WalkTree(window).OfType<System.Windows.Controls.TextBox>()
                .Single(t => System.Windows.Data.BindingOperations.GetBinding(t, System.Windows.Controls.TextBox.TextProperty)?.Path.Path == "ScopeText");
            scopeBox.Text = "\"Other\"";
            var update = WalkTree(window).OfType<System.Windows.Controls.Button>().Single(b => b.Content as string == "Update proposal");
            FinishReviewWork(async () =>
            {
                update.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                while (model.IsBusy) await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            });
            PumpDispatcher();
            var apply = (System.Windows.Controls.Button)window.FindName("createButton");
            Check("the real session confirms the edited phrase through Core before Apply", model.CanConfirm && apply.IsEnabled
                && session.Proposal!.Options.ScopeTerms.SequenceEqual(new[] { "other" }) && file.ToJsonText() == before);
            apply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (window.EvidenceSelection == null) { Check("the native confirmation returns its selection", false); return; }
            var applied = session.Apply(window.EvidenceSelection);
            var saved = FamilyClusterFile.FromJsonText(file.ToJsonText());
            Check("native confirmation reaches the saved recipe with the real B3 clade and denominator", window.Confirmed
                && applied.Changed && saved.EvidenceRecipe?.ConfirmedTask is { } task
                && task.ScopeTerms.SequenceEqual(new[] { "other" }) && task.Proposal.Clade.TaxonId == 10
                && task.Proposal.ReviewedProteins == 2 && task.Proposal.Keywords[0].ReviewedProteins == 2);
            var replay = FamilyClusterFile.FromJsonText(before);
            Evidence.EvidenceApply.ApplyRecipe(replay, applied.Recipe);
            Check("the scope-confirmed recipe replays byte exactly without another discovery request",
                replay.ToJsonText() == file.ToJsonText() && handler.Requests == 3);
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
