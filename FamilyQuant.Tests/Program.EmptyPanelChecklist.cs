using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> EmptyPanelChecklistCommands() =>
    [new("--test-empty-panel-checklist", 74, TestCommandPhase.Raw, args => args.Contains("--test-empty-panel-checklist", StringComparer.Ordinal), (args, _) =>
    {
        EmptyPanelChecklistTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.EmptyChecklist"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void EmptyPanelChecklistTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output); var oldTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme); var file = EvidenceMatchingFile(); string before = file.ToJsonText();
                var session = new EvidenceGroupingSession(file, "empty checklist fixture"); int updates = 0;
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, null);
                var window = new SuperClusterPreviewWindow(preview, null, scope => { updates++; return session.Propose(scope); }, evidenceMode: true,
                    discoverTask: _ => Task.FromResult(new EvidenceTaskScopeInput("Supplied grouping task", Array.Empty<ReviewTaskTaxon>(),
                        Array.Empty<ReviewTaskKeyword>(), 2, new[] { "Action alpha" }, "fixture-release")))
                    { ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.RequestFamilyChecklist(); window.Show(); PumpDispatcher();
                    ((TabItem)window.FindName("PanelChecklistTab")).IsSelected = true; PumpDispatcher();
                    var hint = (TextBlock)window.FindName("PanelChecklistHintText");
                    Check("Z12p5g " + theme + " empty native checklist names the next two actions in one line", hint.Text == "Press Read grouping task, then Update proposal to fill the panel checklist."
                        && updates == 0 && file.ToJsonText() == before);
                    ScreenHarness.Save(window, Path.Combine(output, "empty-checklist-" + theme + ".png"), 1366, 768);
                    var model = (GroupingReviewViewModel)window.DataContext; model.ScopeText = "Action alpha";
                    FinishReviewWork(model.UpdateProposalAsync); PumpDispatcher();
                    Check("Z12p5g " + theme + " explicit proposal replaces the empty instruction with the checklist count definition", updates == 1
                        && model.Evidence!.PanelChecklist.Count > 0 && hint.Text.StartsWith("Matches are database targets", StringComparison.Ordinal) && file.ToJsonText() == before);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
    }
}
