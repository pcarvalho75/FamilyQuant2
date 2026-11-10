using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void AddKeywordScopeTests()
    {
        EnsureApplication();
        var file = TaskFile(); string before = file.ToJsonText(); int proposals = 0;
        var facts = new EvidenceTaskScopeInput("Fixture clade", Array.Empty<ReviewTaskTaxon>(), new[]
        {
            new ReviewTaskKeyword("Existing phrase", 3), new ReviewTaskKeyword("New activity", 2), new ReviewTaskKeyword("Other annotation", 1),
        }, 3, Array.Empty<string>(), "fixture");
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
        var window = new SuperClusterPreviewWindow(preview, null, _ => { proposals++; return GroupingReviewScreens.Fixture(12); },
            initialScope: "\"Existing phrase\", simple", evidenceMode: true, discoverTask: _ => Task.FromResult(facts));
        try
        {
            window.ShowAllOptions = true;
            window.Show(); PumpDispatcher();
            var model = (GroupingReviewViewModel)window.DataContext;
            var grid = (DataGrid)window.FindName("taskKeywordsGrid");
            var button = (Button)window.FindName("AddKeywordToScopeButton");
            var command = (RoutedCommand)button.Command;
            Check("Z12c3 Add to scope needs a selected keyword", !command.CanExecute(null, grid));
            grid.SelectedItems.Add(facts.Keywords[1]); grid.SelectedItems.Add(facts.Keywords[2]);
            command.Execute(null, grid);
            Check("Z12c3 Add to scope appends selected keywords as complete quoted phrases", model.ScopeText
                == "\"Existing phrase\", simple, \"New activity\", \"Other annotation\""
                && WordCluster.ParseKeywords(model.ScopeText).SequenceEqual(new[] { "existing phrase", "simple", "new activity", "other annotation" }));
            string once = model.ScopeText;
            command.Execute(null, grid);
            Check("Z12c3 repeated selections do not duplicate effective scope phrases", model.ScopeText == once && proposals == 0);
            model.ScopeText = "\"Existing phrase";
            grid.SelectedItems.Clear(); grid.SelectedItems.Add(facts.Keywords[1]);
            command.Execute(null, grid);
            Check("Z12c3 an unfinished quote retains the previous phrase and the new quoted phrase",
                WordCluster.ParseKeywords(model.ScopeText).SequenceEqual(new[] { "existing phrase", "new activity" }));
            model.ScopeText = "\"Only this\"";
            grid.SelectedItems.Clear(); grid.SelectedItems.Add(facts.Keywords[2]);
            grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = grid });
            Check("Z12c3 double clicking outside a keyword row does not add the selection", model.ScopeText == "\"Only this\"");
            grid.ScrollIntoView(facts.Keywords[2]); window.UpdateLayout(); PumpDispatcher();
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(facts.Keywords[2]);
            grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = row });
            Check("Z12c3 double clicking a keyword row adds that quoted keyword", model.ScopeText == "\"Only this\", \"Other annotation\""
                && proposals == 0 && file.ToJsonText() == before);
            var update = WalkTree(window).OfType<Button>().Single(b => b.Content as string == "Update proposal");
            FinishReviewWork(async () => { update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); while (model.IsBusy) await Task.Delay(10); });
            Check("Z12c3 only the explicit Update proposal press builds a proposal", proposals == 1 && model.CanConfirm);
            model.AddScopeKeywords(new[] { "A \"quoted\" activity", "A quoted activity", "  " });
            Check("Z12c3 quote punctuation is safely represented and editing requires an updated proposal",
                WordCluster.ParseKeywords(model.ScopeText).Contains("a quoted activity") && !model.CanConfirm && proposals == 1);
        }
        finally { window.Close(); PumpDispatcher(); }
        var pending = new TaskCompletionSource<EvidenceTaskScopeInput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = new GroupingReviewViewModel(preview, initialScope: "existing", discoverTask: _ => pending.Task);
        FinishReviewWork(async () =>
        {
            var running = busy.DiscoverTaskAsync();
            busy.AddScopeKeywords(new[] { "New activity" });
            Check("Z12c3 in-progress discovery prevents scope additions", busy.ScopeText == "existing");
            pending.SetResult(facts); await running;
        });
    }
}
