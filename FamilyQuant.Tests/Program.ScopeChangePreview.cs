using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static FamilyClusterFile ScopePreviewFile()
    {
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            TaskProtein("sp|P10001|FIRST", 21, true, "baseline activity", "narrow activity"),
            TaskProtein("tr|P10002|SECOND", 21, false, "narrow activity"),
            TaskProtein("sp|P10003|THIRD", 21, true, "broad activity"),
            TaskProtein("tr|P10004|FOURTH", 21, false),
            TaskProtein("tr|P10005|FIFTH", 21, false, "narrow activity"),
            TaskProtein("tr|P10006|NAMEONLY", 21, false),
            TaskProtein("contaminant_sp|P10007|IGNORED", 21, true, "narrow activity"),
            TaskProtein("Reverse_sp|P10008|IGNORED", 21, true, "narrow activity"),
        } };
        for (int i = 0; i < file.MyExtendedFastaItems.Count; i++)
        {
            var protein = file.MyExtendedFastaItems[i];
            protein.Description = "Supplied example " + (i + 1) + " OS=Fixture label OX=21";
            protein.Evidence!.Family = i < 2 ? "Fixture family A" : i == 2 ? "Fixture family B" : "Fixture family C";
        }
        file.MyExtendedFastaItems[3].Evidence!.TissueSpecificity.Add("Narrow activity in supporting cells");
        file.MyExtendedFastaItems[4].Evidence!.Reviewed = null;
        file.MyExtendedFastaItems[4].Evidence!.Family = "";
        file.MyExtendedFastaItems[5].Evidence = null;
        file.MyExtendedFastaItems[5].Description = "narrow activity OS=Fixture label OX=21";
        return file;
    }

    private static void ScopeChangePreviewTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var file = ScopePreviewFile(); string before = file.ToJsonText();
        var snapshot = EvidenceClassifier.ScopePreviewSnapshot(file.MyExtendedFastaItems);
        const string baseline = "\"baseline activity\"";
        const string narrow = "\"baseline activity\", \"narrow activity\"";
        const string broad = "\"baseline activity\", \"narrow activity\", \"broad activity\"";
        var result = EvidenceClassifier.PreviewScopeChange(snapshot, baseline, narrow);
        Check("Z12k1 added words match stored keywords and tissue with explicit review status and new-entry counts",
            result is { Proteins: 4, Reviewed: 1, Unreviewed: 2, UnknownStatus: 1, Additional: 3 }
            && result.Families.Single(f => f.Family == "Fixture family A") is { Proteins: 2, Reviewed: 1, Unreviewed: 1, Additional: 1 }
            && result.Families.Any(f => f.Family == "Family not recorded") && file.ToJsonText() == before);
        var wider = EvidenceClassifier.PreviewScopeChange(snapshot, baseline, broad);
        Check("Z12k1 a broader word reveals the extra stored family before proposal building", wider.Proteins == 5
            && wider.Reviewed == 2 && wider.Additional == 4 && wider.Families.Any(f => f.Family == "Fixture family B"));
        Check("Z12k1 names alone, contaminants and decoys do not enter the annotation preview", snapshot.Count == 6
            && result.Families.SelectMany(f => f.Examples).All(name => !name.Contains("P10006", StringComparison.Ordinal)
                && !name.Contains("P10007", StringComparison.Ordinal) && !name.Contains("P10008", StringComparison.Ordinal)));
        var many = file.MyExtendedFastaItems.Concat(Enumerable.Range(20, 5).Select(id =>
        {
            var protein = TaskProtein("sp|P" + id.ToString("D5") + "|EXAMPLE", 21, true, "narrow activity");
            protein.Description = "Extra supplied name " + id; protein.Evidence!.Family = "Fixture family A"; return protein;
        })).ToArray();
        Check("Z12k1 family examples stay short and deterministic", EvidenceClassifier.PreviewScopeChange(EvidenceClassifier.ScopePreviewSnapshot(many), baseline, narrow)
            .Families.Single(f => f.Family == "Fixture family A").Examples.Count == 3);
        file.MyExtendedFastaItems[1].Evidence!.Keywords.Clear();
        Check("Z12k1 the preview metadata snapshot is detached from subsequent source edits",
            EvidenceClassifier.PreviewScopeChange(snapshot, baseline, narrow).Proteins == 4);
        file = ScopePreviewFile(); before = file.ToJsonText();
        int searches = 0, discoveries = 0;
        var session = new EvidenceGroupingSession(file, "fixture", search: (_, _) => { searches++; throw new InvalidOperationException(); },
            discoverTask: (_, _) => { discoveries++; throw new InvalidOperationException(); });
        var stored = session.PreviewScopeAsync(baseline, narrow).GetAwaiter().GetResult();
        Check("Z12k1 stored preview performs neither discovery nor homology search and changes no source state", searches == 0
            && discoveries == 0 && stored.Proteins == 4 && session.Proposal == null && file.ToJsonText() == before);

        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (1366, 820), (1920, 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
                var facts = new EvidenceTaskScopeInput("Fixture clade", Array.Empty<ReviewTaskTaxon>(), new[]
                { new ReviewTaskKeyword("narrow activity", 1), new ReviewTaskKeyword("broad activity", 1) }, 2, new[] { "baseline activity" }, "fixture");
                int proposals = 0;
                var window = new SuperClusterPreviewWindow(preview, null, _ => { proposals++; return GroupingReviewScreens.Fixture(12); },
                    initialScope: baseline, evidenceMode: true, discoverTask: _ => Task.FromResult(facts), previewScope: session.PreviewScopeAsync)
                { Width = size.Item1, Height = size.Item2, ShowActivated = false, ShowInTaskbar = false };
                var model = (GroupingReviewViewModel)window.DataContext;
                try
                {
                    window.Show(); PumpDispatcher();
                    var box = (TextBox)window.FindName("TaskScopeBox");
                    box.Text = narrow; PumpDispatcher(); FinishReviewWork(() => model.ScopePreviewCompletion);
                    Check("Z12k1 " + theme + " " + size.Item1 + " typed scope previews stored evidence before Update proposal",
                        model.ScopeChangePreview?.Proteins == 4 && !model.TaskScopeExpanded && proposals == 0 && model.CanUpdateProposal
                        && ((FrameworkElement)window.FindName("ScopeChangePreviewPanel")).IsVisible);
                    Check("Z12k1 " + theme + " " + size.Item1 + " preview explains the reviewed relative/domain route",
                        model.ScopePreviewRelativeNote.Contains("70%", StringComparison.Ordinal) && model.ScopePreviewFamilies.Count == 3);
                    FinishReviewWork(async () => await Task.Delay(350));
                    window.UpdateLayout(); PumpDispatcher();
                    ScreenHarness.Save(window, Path.Combine(output, "scope-preview-" + theme + "-" + size.Item1 + ".png"), size.Item1, size.Item2);
                    Check("Z12k1 " + theme + " " + size.Item1 + " capture retains the completed preview",
                        !model.IsScopePreviewBusy && model.ScopeChangePreview?.Proteins == 4);
                    model.TaskScopeExpanded = true; PumpDispatcher();
                    var keywords = (DataGrid)window.FindName("taskKeywordsGrid");
                    keywords.SelectedItems.Add(facts.Keywords[1]);
                    SuperClusterPreviewWindow.AddTaskKeywordsToScope.Execute(null, keywords);
                    FinishReviewWork(() => model.ScopePreviewCompletion);
                    Check("Z12k1 " + theme + " " + size.Item1 + " Add to scope refreshes its wider preview", model.ScopeChangePreview?.Proteins == 5 && proposals == 0);
                    model.ScopeText = baseline; PumpDispatcher();
                    model.TaskScopeExpanded = true; PumpDispatcher();
                    keywords.ScrollIntoView(facts.Keywords[0]); window.UpdateLayout();
                    var row = (DataGridRow)keywords.ItemContainerGenerator.ContainerFromItem(facts.Keywords[0]);
                    keywords.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Control.MouseDoubleClickEvent, Source = row });
                    FinishReviewWork(() => model.ScopePreviewCompletion);
                    Check("Z12k1 " + theme + " " + size.Item1 + " double-click refreshes the preview without building a proposal", model.ScopeChangePreview?.Proteins == 4 && proposals == 0);
                    var update = WalkTree(window).OfType<Button>().Single(button => button.Content as string == "Update proposal");
                    FinishReviewWork(async () => { update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); while (model.IsBusy) await Task.Delay(10); });
                    Check("Z12k1 " + theme + " " + size.Item1 + " explicit proposal update advances the baseline and clears its preview", proposals == 1
                        && !model.HasScopeChangePreview && file.ToJsonText() == before);
                }
                finally { window.Close(); FinishReviewWork(() => model.ScopePreviewCompletion); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }

        var keywordPreview = new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null);
        var first = new TaskCompletionSource<EvidenceScopePreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<EvidenceScopePreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var delayed = new GroupingReviewViewModel(keywordPreview, initialScope: baseline,
            previewScope: (_, _, _) => ++calls == 1 ? first.Task : second.Task);
        FinishReviewWork(async () =>
        {
            delayed.ScopeText = narrow;
            while (calls < 1) await Task.Delay(10);
            Task older = delayed.ScopePreviewCompletion;
            delayed.ScopeText = broad;
            while (calls < 2) await Task.Delay(10);
            second.SetResult(wider); await delayed.ScopePreviewCompletion;
            first.SetResult(result); await older;
            Check("Z12k1 late preview completion cannot replace a newer scope result", delayed.ScopeChangePreview?.Proteins == 5 && !delayed.IsScopePreviewBusy);
            delayed.CancelPending();
            Check("Z12k1 cancellation clears the preview without changing scope text", !delayed.HasScopeChangePreview && delayed.ScopeText == broad);
        });
        var failing = new GroupingReviewViewModel(keywordPreview, propose: _ => GroupingReviewScreens.Fixture(12), initialScope: baseline,
            previewScope: (_, _, _) => Task.FromException<EvidenceScopePreview>(new IOException("Fixture preview refusal")));
        FinishReviewWork(async () => { failing.ScopeText = narrow; await failing.ScopePreviewCompletion; });
        Check("Z12k1 a preview failure stays inline with manual proposal work available", failing.ScopePreviewSummary.Contains("Fixture preview refusal", StringComparison.Ordinal)
            && !failing.IsBusy && failing.CanUpdateProposal);
        failing.CancelPending();
    }
}
