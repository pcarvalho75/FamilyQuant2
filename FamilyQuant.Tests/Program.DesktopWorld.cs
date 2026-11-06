using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FamilyQuant.Evidence;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DesktopWorldCommands() =>
    [new("--test-desktop-world", 10460, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-desktop-world", (args, _) => DesktopWorldTests())];

    private static int DesktopWorldTests()
    {
        DesktopWorldChecks();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static void DesktopWorldChecks()
    {
        EnsureApplication();
        var view = new ClusterGenerationControl(TaskFile());
        Check("Y8 curation exposes a family-world fetch button beside evidence", view.FindName("ButtonFetchFamilyWorld") is Button);
        Check("Y8 curation exposes persistent family-world state", view.FindName("TextBlockFamilyWorldState") is TextBlock);
        Check("Y8 family-world tooltip uses at most two sentences", TooltipSentenceCount(((Button)view.FindName("ButtonFetchFamilyWorld")).ToolTip.ToString()!) <= 2);
        Check("Y8 no world is reported as not fetched", view.ViewModel.FamilyWorldState == "Family world: not fetched.");
        var withoutEvidence = TaskFile();
        foreach (var protein in withoutEvidence.MyExtendedFastaItems) protein.Evidence = null;
        var noEvidenceScreen = new ClusterGenerationControl(withoutEvidence);
        string noEvidenceBytes = withoutEvidence.ToJsonText();
        WaitDesktopWorld(noEvidenceScreen.FetchFamilyWorldAsync());
        Check("Y8 a missing evidence prerequisite leaves the file unchanged", noEvidenceBytes == withoutEvidence.ToJsonText()
            && !noEvidenceScreen.Operations.IsBusy && withoutEvidence.EvidenceSource?.FamilyWorld == null);
        foreach (bool openGrouping in new[] { false, true })
        {
            var file = TaskFile();
            var screen = new ClusterGenerationControl(file);
            using var taxonomy = new TaskTaxonomyHandler();
            using var fetch = new WorldFetchHandler();
            screen.FamilyWorldTaskHandlerOverride = taxonomy;
            screen.FamilyWorldHandlerOverride = fetch;
            int offers = 0, reviews = 0;
            screen.ConfirmFamilyWorldGroupingOfferOverride = () => { offers++; return openGrouping; };
            screen.ConfirmEvidenceReviewOverride = _ => { reviews++; return null; };
            screen.ConfirmFamilyWorldScopeOverride = _ => throw new InvalidOperationException("An answered grouping offer must not fall through to scope confirmation.");
            string before = file.ToJsonText();
            ((Button)screen.FindName("ButtonFetchFamilyWorld")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitDesktopWorld(screen.FamilyWorldFetchCompletion);
            Check("Y8 actual button answers the grouping offer: " + (openGrouping ? "open review" : "cancel"),
                offers == 1 && reviews == (openGrouping ? 1 : 0) && fetch.Requests == 0
                && before == file.ToJsonText() && !screen.Operations.IsBusy && screen.IsEnabled);
        }
        foreach (string scenario in new[] { "confirm cancelled", "fetch cancelled", "HTTP failure", "success" })
        {
            var file = TaskFile();
            var screen = new ClusterGenerationControl(file);
            using var taxonomy = new TaskTaxonomyHandler();
            using var fetch = new WorldFetchHandler { FailClade = scenario == "HTTP failure" };
            screen.FamilyWorldTaskHandlerOverride = taxonomy;
            screen.FamilyWorldHandlerOverride = fetch;
            int confirmations = 0, changes = 0;
            bool busySeen = false, progressSeen = false;
            screen.ConfirmFamilyWorldScopeOverride = proposal =>
            {
                confirmations++;
                return scenario == "confirm cancelled" ? null
                    : EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
            };
            screen.DocumentChanged += (_, _) => changes++;
            fetch.DuringRequest = (reviewed, token) =>
            {
                busySeen |= screen.Operations.IsBusy;
                if (!reviewed && scenario == "fetch cancelled") { screen.Operations.Cancel(); token.ThrowIfCancellationRequested(); }
            };
            screen.Operations.PropertyChanged += (_, _) => progressSeen |= screen.Operations.IsBusy;
            string before = file.ToJsonText();
            ((Button)screen.FindName("ButtonFetchFamilyWorld")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitDesktopWorld(screen.FamilyWorldFetchCompletion);
            Check("Y8 actual button confirms missing scope: " + scenario, confirmations == 1);
            Check("Y8 shared host ends and re-enables curation: " + scenario, !screen.Operations.IsBusy && screen.IsEnabled && progressSeen);
            if (scenario != "success")
                Check("Y8 " + scenario + " preserves every serialized file byte", before == file.ToJsonText() && changes == 0
                    && (scenario == "confirm cancelled" ? fetch.Requests == 0 : busySeen));
            else
            {
                var world = FamilyWorldFetcher.Stored(file);
                Check("Y8 actual button stores complete world and marks document changed", world.Entries.Count == 2 && changes == 1 && busySeen);
                var state = (TextBlock)screen.FindName("TextBlockFamilyWorldState");
                PumpDispatcher();
                Check("Y8 state includes stored release scope count and size", state.Text.Contains("fixture") && state.Text.Contains("Leading scope")
                    && state.Text.Contains("2 proteins in 1 family") && state.Text.Contains("MB")
                    && state.Text.Contains("in the open project", StringComparison.Ordinal)
                    && state.Text.Contains("Use File > Save", StringComparison.Ordinal));
                string message = ((TextBlock)screen.FindName("TextBlockStatusLine")).Text;
                Check("Z1l family-world completion explains the open-project result and File Save action",
                    message.Contains("Family world added to the open project.", StringComparison.Ordinal)
                    && message.Contains("Use File > Save", StringComparison.Ordinal)
                    && !message.Contains("stored", StringComparison.OrdinalIgnoreCase));
                string path = Path.Combine(Path.GetTempPath(), "fq-y8-" + Guid.NewGuid().ToString("N") + ".fqproj");
                ProjectStore.WriteFqproj(path, file, Array.Empty<QuantRow>(), new QuantManifest());
                var reopened = ProjectStore.ReadFqproj(path).fcf;
                Check("Y8 project save and offline reopen retain the complete world", reopened.ToJsonText() == file.ToJsonText()
                    && FamilyWorldFetcher.Stored(reopened).ContentSha256 == world.ContentSha256);
                // A previously confirmed task in the saved world needs no taxonomy request or new confirmation.
                var offline = new ClusterGenerationControl(reopened) { FamilyWorldHandlerOverride = new WorldFetchHandler() };
                offline.ConfirmFamilyWorldScopeOverride = _ => throw new Exception("Stored valid task should be reused.");
                WaitDesktopWorld(offline.FetchFamilyWorldAsync());
                Check("Y8 saved confirmed scope is reused without discovery", FamilyWorldFetcher.Stored(reopened).Entries.Count == 2);
                string storedBefore = reopened.ToJsonText();
                using var failedRefresh = new WorldFetchHandler { FailClade = true };
                offline.FamilyWorldHandlerOverride = failedRefresh;
                WaitDesktopWorld(offline.FetchFamilyWorldAsync());
                Check("Y8 failed refresh preserves the previously stored world and entire file", reopened.ToJsonText() == storedBefore);
                File.Delete(path);
            }
        }
    }

    private static void WaitDesktopWorld(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(45)) { PumpDispatcher(); Thread.Sleep(1); }
        if (!task.IsCompleted) throw new TimeoutException("Y8 desktop action did not finish.");
        task.GetAwaiter().GetResult(); PumpDispatcher();
    }
}
