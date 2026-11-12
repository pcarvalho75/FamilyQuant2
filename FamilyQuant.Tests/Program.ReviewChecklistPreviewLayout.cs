using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReviewChecklistPreviewLayoutTests(string? output)
    {
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (int height in new[] { 768, 820 })
            {
                Themes.ThemeManager.Apply(theme);
                var file = ChecklistActionFile();
                for (int i = 0; i < 8; i++) file.FamilyPanel!.Families.Add(new() { Key = "extra" + i, DisplayName = "Additional list family " + i });
                string before = file.ToJsonText();
                var session = new EvidenceGroupingSession(file, "synthetic checklist preview");
                var input = session.Propose("selected");
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds),
                    input, session.Propose, initialScope: "selected", evidenceMode: true, previewScope: session.PreviewScopeAsync,
                    addToFamily: session.AddToFamily, removeFromFamily: session.RemoveFromFamily, updateManualChoices: session.UpdateManualChoices)
                { Width = 1366, Height = height, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false, ShowAllOptions = true };
                try
                {
                    window.Show(); PumpDispatcher();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    ((TabItem)window.FindName("PanelChecklistTab")).IsSelected = true;
                    model.Evidence!.SelectedChecklistFamily = model.Evidence.PanelChecklist.Single(row => row.Key == "missing");
                    PumpDispatcher();
                    ((Button)window.FindName("ChecklistAddScopeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    FinishReviewWork(() => model.ScopePreviewCompletion); PumpDispatcher(); window.UpdateLayout();
                    var preview = (TextBox)window.FindName("ChecklistScopePreview");
                    WaitForChecklistPreviewLayout(window, preview, "RG10b " + theme + " " + height, TimeSpan.FromSeconds(10));
                    string label = "RG10b " + theme + " 1366x" + height;
                    Check(label + " scope preview stays fully visible beside the scrolling actions", preview.ActualHeight >= 48 && preview.ActualWidth >= 300
                        && ReviewLayout3Visible(preview, window) && preview.Text.Contains(model.ScopePreviewSummary, StringComparison.Ordinal));
                    var tools = (ScrollViewer)window.FindName("ChecklistToolsScroller");
                    tools.ScrollToBottom(); PumpDispatcher();
                    var hand = (Button)window.FindName("ChecklistPlaceHandButton"); hand.BringIntoView(); PumpDispatcher();
                    Check(label + " preview stays visible while scrolling to a hand action", ReviewLayout3Visible(preview, window) && ReviewLayout3Visible(hand, window));
                    if (height == 820)
                    {
                        var table = (DataGrid)window.FindName("PanelChecklistGrid");
                        int rows = WalkTree(table).OfType<DataGridRow>().Count(row => row.IsVisible && row.ActualHeight > 0 && ReviewLayout3Visible(row, window));
                        Check(label + " preview retains six complete list rows", rows >= 6, "rows=" + rows + "; table=" + table.ActualHeight);
                    }
                    Check(label + " preview retains the scope gate and unchanged source", !model.CanConfirm && before == file.ToJsonText());
                    if (output != null)
                    {
                        Directory.CreateDirectory(output);
                        ScreenHarness.Save(window, Path.Combine(output, "scope-preview-" + theme + "-1366x" + height + ".png"), 1366, height);
                    }
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }
}
