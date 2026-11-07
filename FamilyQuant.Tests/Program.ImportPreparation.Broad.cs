using FamilyQuant.Evidence;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ImportPreparationBroadMatchTests(string? output)
    {
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = ManualChoiceFile();
                foreach (var protein in file.MyExtendedFastaItems)
                { protein.Description = "Broad shared keyword"; protein.Evidence = new() { Family = "Stored broad family", Keywords = new() { "Broad shared keyword", "Another broad keyword" } }; }
                string original = file.ToJsonText();
                string work = Path.Combine(Path.GetTempPath(), "fq-prepared-broad-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
                string workbook = Path.Combine(work, "broad.xlsx"); PanelImportTemplate.Write(workbook);
                FillTemplate(workbook, new[] { new[] { "broad", "Broad shared keyword", "", "", "", "Quantify" } });
                int uiThread = Environment.CurrentManagedThreadId, copyThread = 0;
                var matcher = new PreparationMatcher(file);
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)), matcher: matcher,
                    copyProject: (source, context) => { copyThread = Environment.CurrentManagedThreadId; return source.DeepCopy(context); });
                var wizard = new FamilyListImportWindow(model)
                { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    wizard.Show(); FinishReviewWork(() => model.LoadAsync(workbook));
                    FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); await model.NextAsync(); }); PumpDispatcher();
                    var family = model.MatchFamilies.Single(); family.ShowAdditional = true;
                    var broad = family.Suggestions.Where(s => s.Text is "Broad shared keyword" or "Another broad keyword" or "Stored broad family").ToArray();
                    foreach (var label in broad) label.Use = true;
                    Check(theme + " combined flow prepares on a worker and retains the broad choices", copyThread != uiThread && broad.Length == 3 && model.Page == 4 && !model.IsBusy);
                    string expected = new PanelEvidenceMatching(file).ConfirmationWarning(broad.Select(s => PanelEvidenceMatching.ChoiceFromKey(s.Key) with { Value = s.Text }));
                    var shown = new List<string>();
                    var confirm = WalkTree(wizard).OfType<Button>().Single(b => b.Content as string == "Use selected matches");
                    void PressConfirmation(bool keep)
                    {
                        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                        {
                            var dialog = Application.Current.Windows.OfType<BroadEvidenceMatchWindow>().SingleOrDefault();
                            if (dialog == null) return;
                            dialog.Left = -30000; dialog.Top = -30000; dialog.ShowInTaskbar = false;
                            shown.Add(string.Join("\n", WalkTree(dialog).OfType<TextBlock>().Select(t => t.Text)));
                            if (output != null)
                            {
                                Directory.CreateDirectory(output);
                                ScreenHarness.Save(dialog, Path.Combine(output, theme + (keep ? "-broad-keep.png" : "-broad-back.png")), 600, (int)Math.Ceiling(dialog.ActualHeight));
                            }
                            var button = keep ? dialog.KeepButton : WalkTree(dialog).OfType<Button>().Single(b => b.Content as string == "Back");
                            var peer = new ButtonAutomationPeer(button);
                            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                        }));
                        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    }
                    PressConfirmation(false);
                    Check(theme + " the prepared result asks the real counted broad-label question", shown.Count == 1 && shown[0].Contains(expected, StringComparison.Ordinal));
                    Check(theme + " native Back preserves ticks and leaves prepared labels unconfirmed", family.Choice == "Needs a choice"
                        && broad.All(s => s.Use) && !model.CanNext && file.ToJsonText() == original);
                    PressConfirmation(true);
                    Check(theme + " native Keep confirms only the prepared draft", shown.Count == 2 && family.Choice == "Confirmed"
                        && broad.All(s => s.Use) && model.CanNext && file.ToJsonText() == original);
                }
                finally { wizard.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }
}
