using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelEvidenceMatchUiCommandTable() =>
    [
        new("--test-panel-evidence-match-ui", 74, TestCommandPhase.Raw, args => args.Contains("--test-panel-evidence-match-ui", StringComparer.Ordinal), (args, _) =>
        {
            PanelEvidenceMatchUiTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelEvidenceMatchUi"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static void PanelEvidenceMatchUiTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var oldTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (1366, 768), (1920, 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var file = EvidenceMatchingFile(); string before = file.ToJsonText();
                var evidence = new PanelEvidenceMatching(file);
                var context = new PanelEvidenceMatchContext(evidence, file.FamilyPanel!.Families[0], null, () => evidence.IsCurrent(file));
                var model = new PanelEvidenceMatchViewModel(context);
                var window = new PanelEvidenceMatchWindow(model) { Width = size.Item1, Height = size.Item2, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    var grid = (DataGrid)window.FindName("MatchLabelsGrid");
                    Check("Z12p4 " + theme + " " + size.Item1 + " new suggestions start unticked with original names and exact coverage",
                        model.Rows.Count > 0 && model.Rows.All(row => !row.Selected) && model.NamesText.Contains("Actual other header", StringComparison.Ordinal)
                        && model.Rows.Single(row => row.Kind == PanelEvidenceMatchKind.Pfam) is { Targets: 2, Reviewed: 1, References: 1 }
                        && grid.ActualHeight > 200 && file.ToJsonText() == before);
                    ScreenHarness.Save(window, Path.Combine(output, "matches-unticked-" + theme + "-" + size.Item1 + ".png"), size.Item1, size.Item2);
                    var edited = model.Rows.Single(row => row.Kind == PanelEvidenceMatchKind.Pfam); edited.Selected = true;
                    grid.SelectedItem = edited; grid.CurrentCell = new DataGridCellInfo(edited, grid.Columns[2]); grid.BeginEdit(); PumpDispatcher();
                    var box = (TextBox)grid.Columns[2].GetCellContent(edited);
                    box.Text = "Edited label"; box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); PumpDispatcher();
                    Check("Z12p4 " + theme + " " + size.Item1 + " native value editing updates exact coverage without changing source",
                        edited is { Value: "Edited label", Targets: 0, Reviewed: 0, References: 0 } && file.ToJsonText() == before);
                    Check("Z12p4 " + theme + " " + size.Item1 + " explicit confirmation captures only the edited checked label",
                        model.TryConfirm(out var matches) && matches is { UserConfirmed: true } && matches.Choices.Single() == new PanelEvidenceChoice(PanelEvidenceMatchKind.Pfam, "Edited label")
                        && file.ToJsonText() == before);
                    ScreenHarness.Save(window, Path.Combine(output, "matches-edited-" + theme + "-" + size.Item1 + ".png"), size.Item1, size.Item2);
                }
                finally { window.Close(); PumpDispatcher(); }

                var session = new EvidenceGroupingSession(file, "matching review fixture");
                var input = session.Propose("Action alpha");
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
                var review = new SuperClusterPreviewWindow(preview, input, session.Propose, initialScope: "Action alpha", evidenceMode: true,
                    addToFamily: session.AddToFamily, removeFromFamily: session.RemoveFromFamily, updateManualChoices: session.UpdateManualChoices,
                    createMatchContext: session.CreatePanelMatchContext, confirmPanelMatches: session.ConfirmPanelEvidenceMatches)
                    { Width = size.Item1, Height = size.Item2, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    review.Show(); PumpDispatcher(); ((TabItem)review.FindName("PanelChecklistTab")).IsSelected = true;
                    var state = (GroupingReviewViewModel)review.DataContext;
                    var button = (Button)review.FindName("MatchPanelNamesButton");
                    int opens = 0; review.PanelMatchesDialogOverride = _ => { opens++; return false; };
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check("Z12p4 " + theme + " " + size.Item1 + " checklist opens matching even with no candidates and cancel preserves source",
                        opens == 1 && state.Evidence!.SelectedChecklistFamily?.Matches == 0 && file.ToJsonText() == before);
                    review.PanelMatchesDialogOverride = editor =>
                    {
                        editor.Rows.Single(row => row.Kind == PanelEvidenceMatchKind.UniProtFamily).Selected = true; return true;
                    };
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); review.UpdateLayout(); PumpDispatcher();
                    Check("Z12p4 " + theme + " " + size.Item1 + " confirmed records update checklist candidates and preserve automatic grouping",
                        state.Evidence!.SelectedChecklistFamily is { Matches: 2, Placed: 0, Outside: 2, ExcludedContaminants: 1 }
                        && session.Proposal!.Proteins.All(row => row.Family.Length == 0) && file.ToJsonText() == before);
                    ScreenHarness.Save(review, Path.Combine(output, "confirmed-checklist-" + theme + "-" + size.Item1 + ".png"), size.Item1, size.Item2);
                    state.AddToFamily(new EvidenceManualRequest(new[] { 0, 1 }, "stable-key", "Nome fornecido", false, "Explicit matched choice"));
                    var applied = session.Apply(state.Evidence.CaptureSelection()); string after = file.ToJsonText();
                    applied.Undo!.Restore(file); bool undo = file.ToJsonText() == before;
                    EvidenceApply.ApplyRecipe(file, applied.Recipe);
                    Check("Z12p4 " + theme + " " + size.Item1 + " normal hand Apply persists matched labels with exact Undo and replay",
                        undo && file.ToJsonText() == after && file.FamilyPanel!.ConfirmedEvidenceMatches!["stable-key"].UserConfirmed);
                }
                finally { review.Close(); PumpDispatcher(); }
            }
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = EvidenceMatchingFile(); foreach (var protein in file.MyExtendedFastaItems) file.LockedUserIds.Add(protein.UserID);
                var host = new ClusterGenerationControl(file); string before = file.ToJsonText();
                var editor = new PanelEditorWindow(file.FamilyPanel, evidenceFile: file) { Width = 1180, Height = 720, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    editor.Show(); PumpDispatcher();
                    var row = editor.Draft.Families[0]; ((DataGrid)editor.FindName("FamilyGrid")).SelectedItem = row; PumpDispatcher();
                    editor.PanelMatchesDialogOverride = model =>
                    {
                        var choice = model.AddLabel(); choice.Kind = PanelEvidenceMatchKind.Keyword; choice.Value = "Future keyword"; return true;
                    };
                    ((Button)editor.FindName("MatchStoredNamesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var panel = editor.Draft.Build();
                    Check("Z12p4 " + theme + " panel editor confirms an edited zero-coverage label without grouping or changing source",
                        panel.ConfirmedEvidenceMatches!["stable-key"].Choices.Single().Value == "Future keyword" && file.ToJsonText() == before);
                    row.Key = "renamed-key";
                    Check("Z12p4 " + theme + " newly confirmed draft records follow key edits", editor.Draft.Build().ConfirmedEvidenceMatches!.ContainsKey("renamed-key")
                        && editor.Draft.MatchChangeNotice.Contains("stable-key to renamed-key", StringComparison.Ordinal));
                    row.Key = "stable-key"; panel = editor.Draft.Build();
                    ScreenHarness.Save(editor, Path.Combine(output, "panel-matches-" + theme + ".png"), 1180, 720);
                    bool changed = host.ViewModel.ApplyFamilyPanel(panel);
                    Check("Z12p4 " + theme + " panel Apply saves labels while all protein families remain locked",
                        changed && file.FamilyPanel!.ConfirmedEvidenceMatches!.ContainsKey("stable-key") && file.LockedUserIds.SetEquals(new[] { 0, 1, 2 }));
                    host.ViewModel.Undo();
                    Check("Z12p4 " + theme + " metadata-only panel Apply has byte-exact Undo", file.ToJsonText() == before);
                }
                finally { editor.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
        var staleFile = EvidenceMatchingFile(); var snapshot = new PanelEvidenceMatching(staleFile);
        var stale = new PanelEvidenceMatchViewModel(new(snapshot, staleFile.FamilyPanel!.Families[0], null, () => snapshot.IsCurrent(staleFile)));
        stale.Rows[0].Selected = true; staleFile.MyExtendedFastaItems[0].Evidence!.Keywords.Add("Changed source");
        Check("Z12p4 changed stored evidence refuses confirmation inline", !stale.TryConfirm(out _) && !stale.CanConfirm && stale.Summary.Contains("reopen", StringComparison.Ordinal));
        var partialFile = EvidenceMatchingFile(); partialFile.EvidenceSource!.ReferencePanel!.MissingFamilies.Add("Unavailable group");
        var partial = new PanelEvidenceMatchViewModel(new(new(partialFile), partialFile.FamilyPanel!.Families[0], null, () => true));
        Check("Z12p4 incomplete stored references are plainly identified in matching UI", partial.Summary.Contains("incomplete", StringComparison.Ordinal));
        var pending = new PanelEditorDraft(partialFile.FamilyPanel);
        pending.SetConfirmedMatches(pending.Families[0], PanelEvidenceMatching.Confirm(new[] { "Catalytic violet" },
            new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Keyword, "Action alpha") }));
        pending.Families.RemoveAt(0);
        Check("Z12p4 removing a newly confirmed draft family drops its pending labels with a notice",
            pending.Build().ConfirmedEvidenceMatches == null && pending.MatchChangeNotice.Contains("Dropped", StringComparison.Ordinal));
        var modalFile = EvidenceMatchingFile(); var modalSnapshot = new PanelEvidenceMatching(modalFile);
        var modalModel = new PanelEvidenceMatchViewModel(new(modalSnapshot, modalFile.FamilyPanel!.Families[0], null, () => modalSnapshot.IsCurrent(modalFile)));
        modalModel.Rows[0].Selected = true;
        var modal = new PanelEvidenceMatchWindow(modalModel) { ShowInTaskbar = false, ShowActivated = false };
        modal.Loaded += (_, _) => modal.Dispatcher.BeginInvoke(new Action(() =>
            ((Button)modal.FindName("ConfirmMatchLabelsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        Check("Z12p4 native modal confirmation returns only checked user choices", modal.ShowDialog() == true
            && modal.Matches is { UserConfirmed: true } && modal.Matches.Choices.Count == 1 && modalFile.FamilyPanel.ConfirmedEvidenceMatches == null);
    }
}
