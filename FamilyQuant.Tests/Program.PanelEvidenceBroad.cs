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
    private static IEnumerable<TestCommand> PanelEvidenceBroadCommands() =>
    [new("--test-panel-evidence-broad", 74, TestCommandPhase.Raw, args => args.Contains("--test-panel-evidence-broad", StringComparer.Ordinal), (args, _) =>
    {
        PanelEvidenceBroadTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.BroadMatches"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void PanelEvidenceBroadTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var file = EvidenceMatchingFile(); string before = file.ToJsonText(); var snapshot = new PanelEvidenceMatching(file);
        var model = new PanelEvidenceMatchViewModel(new(snapshot, file.FamilyPanel!.Families[0], null, () => snapshot.IsCurrent(file)));
        var family = model.Rows.Single(row => row.Kind == PanelEvidenceMatchKind.UniProtFamily); family.Selected = true;
        var keyword = model.Rows.Single(row => row.Value == "Action alpha"); keyword.Selected = true;
        Check("Z12p5i broad labels require an explicit answer", !model.TryConfirm(out _) && model.Rows.Count(row => row.Selected) == 2 && file.ToJsonText() == before);
        int prompts = 0; string prompt = "";
        Check("Z12p5i rejecting one combined counted prompt keeps every choice", !model.TryConfirm(out _, text => { prompts++; prompt = text; return false; })
            && prompts == 1 && prompt.Contains("2 database targets", StringComparison.Ordinal) && prompt.Contains("Stored group r (2)", StringComparison.Ordinal)
            && prompt.Contains("Action alpha (2)", StringComparison.Ordinal) && model.CanConfirm && model.Rows.Count(row => row.Selected) == 2);
        prompts = 0;
        Check("Z12p5i accepting asks once for all selected broad labels", model.TryConfirm(out var record, _ => { prompts++; return true; })
            && prompts == 1 && record!.Choices.Count == 2 && file.ToJsonText() == before);
        var halfFile = EvidenceMatchingFile();
        halfFile.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "tr|P70001|invented", Sequence = "AAAA" });
        halfFile.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "tr|P70002|invented", Sequence = "TTTT" });
        var half = new PanelEvidenceMatching(halfFile);
        Check("Z12p5i exactly half does not ask and exclusions cannot dilute the target denominator", half.DatabaseTargetEntries == 4 && snapshot.DatabaseTargetEntries == 2
            && half.ConfirmationWarning(new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Keyword, "Action alpha") }) == "");
        keyword.Selected = false; family.Value = "A newly entered exact label"; prompts = 0;
        Check("Z12p5i edited zero-coverage labels do not ask about their old counts", model.TryConfirm(out _, _ => { prompts++; return true; }) && prompts == 0);
        family.Value = "Stored group r";
        Check("Z12p5i evidence changed during the question refuses confirmation", !model.TryConfirm(out _, _ => { file.MyExtendedFastaItems[0].Evidence!.Keywords.Add("Concurrent edit"); return true; })
            && !model.CanConfirm && model.Summary.Contains("reopen", StringComparison.Ordinal));
        var oldTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var dialog = new BroadEvidenceMatchWindow(prompt) { ShowInTaskbar = false, ShowActivated = false };
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ScreenHarness.Save(dialog, Path.Combine(output, "broad-confirm-" + theme + ".png"), 600, 260);
                    dialog.KeepButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Check("Z12p5i " + theme + " native broad confirmation returns an explicit Keep answer", dialog.ShowDialog() == true);
                var fresh = EvidenceMatchingFile(); var source = new PanelEvidenceMatching(fresh);
                var editor = new PanelEvidenceMatchViewModel(new(source, fresh.FamilyPanel!.Families[0], null, () => true));
                editor.Rows.Single(row => row.Kind == PanelEvidenceMatchKind.UniProtFamily).Selected = true;
                int nativePrompts = 0;
                var matching = new PanelEvidenceMatchWindow(editor) { ShowInTaskbar = false, ShowActivated = false,
                    BroadMatchConfirmationOverride = _ => { nativePrompts++; return true; } };
                matching.Loaded += (_, _) => matching.Dispatcher.BeginInvoke(new Action(() => ((Button)matching.FindName("ConfirmMatchLabelsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
                Check("Z12p5i " + theme + " Confirm matches calls the broad guard once", matching.ShowDialog() == true && nativePrompts == 1 && matching.Matches!.Choices.Count == 1);
            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
    }
}
