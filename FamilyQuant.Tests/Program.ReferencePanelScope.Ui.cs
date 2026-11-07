using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferencePanelScopeUiCommands() =>
    [new("--test-reference-panel-scope-ui", 100, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-reference-panel-scope-ui", (args, _) =>
        {
            ReferencePanelScopeUiTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelScopeUi"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
        })];

    private static void ReferencePanelScopeUiTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var (_, catalogue) = ScopedPanelDatabase(true);
        var fresh = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "sp|P50000|invented", Sequence = catalogue[0].Entry.Sequence } } };
        using (var handler = new ScopedPanelHandler(catalogue))
        {
            bool preview = false, selected = false;
            var outcome = EvidenceFetchSession.FetchAsync(fresh, handler, proteinDomainsReady: ready => { preview = ready.Proteins.Count == 1; return Task.CompletedTask; },
                selectPanel: staged =>
                {
                    selected = preview && staged.MyExtendedFastaItems[0].Evidence?.Pfam.Count > 0 && fresh.MyExtendedFastaItems[0].Evidence == null
                        && handler.Queries.All(query => !query.Contains("family:", StringComparison.Ordinal));
                    return Task.FromResult<ReferencePanelRequest?>(new(new[] { "Chosen phase" }));
                }).GetAwaiter().GetResult();
            Check("Z15a protein evidence and domains arrive before choosing panel scope", selected && outcome.Panel is { Coverage: not null, IsComplete: true }
                && fresh.MyExtendedFastaItems[0].Evidence?.Pfam.Count > 0);
        }
        var cancelled = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "sp|P50000|invented", Sequence = catalogue[0].Entry.Sequence } } };
        string cancelledBefore = cancelled.ToJsonText(); using (var handler = new ScopedPanelHandler(catalogue))
            Check("Z15a cancelling the scope choice retains the original commit guard", Throws<OperationCanceledException>(() => EvidenceFetchSession.FetchAsync(cancelled, handler,
                selectPanel: _ => Task.FromResult<ReferencePanelRequest?>(null)).GetAwaiter().GetResult()) && cancelled.ToJsonText() == cancelledBefore);
        var oldTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var picker = new ReferencePanelScopeWindow(Array.Empty<string>()) { ShowActivated = false, ShowInTaskbar = false };
                picker.Loaded += (_, _) => picker.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Check("Z15a " + theme + " scope picker starts disabled and offers full coverage", !picker.FetchButton.IsEnabled);
                    picker.ScopeBox.Text = "..."; Check("Z15a " + theme + " punctuation alone is not a scope", !picker.FetchButton.IsEnabled);
                    picker.FullPanelBox.IsChecked = true;
                    Check("Z15a " + theme + " Full panel enables fetch independently of scope", picker.FetchButton.IsEnabled && !picker.ScopeBox.IsEnabled);
                    picker.FullPanelBox.IsChecked = false; picker.ScopeBox.Text = "\"Chosen phase\"";
                    ScreenHarness.Save(picker, Path.Combine(output, "scope-picker-" + theme + ".png"), 600, 300);
                    picker.FetchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Check("Z15a " + theme + " native picker returns only the explicit scope", picker.ShowDialog() == true && picker.Request is { FullPanel: false }
                    && picker.Request.ScopeTerms.SequenceEqual(WordCluster.ParseKeywords("\"Chosen phase\"")));

            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
    }
}
