using FamilyQuant.Operations;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ShellEvidenceFetchCancellationTests()
    {
        EnsureApplication();
        Section("Shell Cancel reaches both atomic evidence fetch phases");
        foreach (bool cancelAtPanel in new[] { false, true })
        {
            var document = AtomicFetchFile();
            string before = document.ToJsonText();
            var originalProtein = document.MyExtendedFastaItems[0];
            var originalSource = document.EvidenceSource;
            var screen = new ClusterGenerationControl(document);
            var host = screen.Operations;
            using var lease = host.Begin("Fetch evidence");
            using var handler = new EvidenceAndPanelHandler();
            bool reached = false, networkCancelled = false;
            int changed = 0;
            screen.DocumentChanged += (_, _) => changed++;
            handler.DuringRequest = (panel, requestToken) =>
            {
                if (panel != cancelAtPanel) return;
                reached = true;
                Application.Current.Dispatcher.Invoke(() => host.CancelCommand.Execute(null));
                networkCancelled = requestToken.IsCancellationRequested;
                requestToken.ThrowIfCancellationRequested();
            };
            var task = AppOperationHost.OnUi(screen, () => screen.ViewModel.FetchEvidenceAsync(handler, lease.Context));
            bool cancelled = false;
            try { FinishBusyTask(task); task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            string phase = cancelAtPanel ? "panel" : "protein evidence";
            Check("shared Cancel cancels the active " + phase + " HTTP request token", reached && networkCancelled);
            Check("cancelled " + phase + " fetch keeps every document byte and original object",
                cancelled && before == document.ToJsonText() && changed == 0 && !lease.Context.HasCommitted
                && ReferenceEquals(document.MyExtendedFastaItems[0], originalProtein) && ReferenceEquals(document.EvidenceSource, originalSource));
            lease.Dispose();
            Check("cancelled " + phase + " fetch releases the shared host", !host.IsBusy);
        }
        {
            var document = AtomicFetchFile();
            var originalProtein = document.MyExtendedFastaItems[0];
            var screen = new ClusterGenerationControl(document);
            var host = screen.Operations;
            using var lease = host.Begin("Fetch evidence");
            using var handler = new EvidenceAndPanelHandler();
            bool staged = false;
            int changed = 0;
            screen.DocumentChanged += (_, _) => changed++;
            handler.DuringRequest = (panel, _) =>
            {
                if (panel) staged = originalProtein.Domains == "Stored domain" && document.EvidenceSource!.Release == "old"
                    && !lease.Context.HasCommitted && changed == 0;
            };
            var task = AppOperationHost.OnUi(screen, () => screen.ViewModel.FetchEvidenceAsync(handler, lease.Context));
            FinishBusyTask(task);
            var result = task.GetAwaiter().GetResult();
            Check("successful shell evidence fetch keeps staging private until one final document commit",
                staged && result.Changed && lease.Context.HasCommitted && changed == 1
                && ReferenceEquals(document.MyExtendedFastaItems[0], originalProtein)
                && originalProtein.UserID == 71 && originalProtein.USearchID == 51
                && document.LockedUserIds.SetEquals(new[] { 71 }) && originalProtein.XICUser == 2.5
                && document.EvidenceSource?.ReferencePanel?.Entries.Count == 1);
            host.Cancel();
            Check("successful final shell evidence commit wins a late Cancel",
                !host.IsCancelling && document.EvidenceSource?.Release == "2026_03" && changed == 1);
        }
        {
            var document = AtomicFetchFile();
            var screen = new ClusterGenerationControl(document);
            int changed = 0;
            screen.DocumentChanged += (_, _) => changed++;
            using var lease = screen.Operations.Begin("Fetch evidence");
            using var handler = new EvidenceAndPanelHandler(failPanel: true);
            var task = AppOperationHost.OnUi(screen, () => screen.ViewModel.FetchEvidenceAsync(handler, lease.Context));
            FinishBusyTask(task);
            var outcome = task.GetAwaiter().GetResult();
            var reopened = FamilyClusterFile.FromJsonText(document.ToJsonText());
            Check("Z1a shell panel failure commits protein evidence once and saves the missing panel reason",
                outcome.PanelFailure != null && lease.Context.HasCommitted && changed == 1
                && reopened.MyExtendedFastaItems[0].Domains == "Supplied domain"
                && reopened.MyExtendedFastaItems[0].Evidence?.Family == "fixture family"
                && reopened.EvidenceSource?.Release == "2026_03"
                && reopened.EvidenceSource.ReferencePanel is { IsComplete: false } missing
                && missing.MissingFamilies.SequenceEqual(new[] { "fixture family" })
                && missing.FailureReason.Contains("HTTP 400", StringComparison.Ordinal));
        }
        {
            var document = AtomicFetchFile();
            var expected = FamilyClusterFile.FromJsonText(document.ToJsonText());
            expected.MyExtendedFastaItems[0].Domains = "User edit";
            var screen = new ClusterGenerationControl(document);
            int changed = 0;
            screen.DocumentChanged += (_, _) => changed++;
            using var lease = screen.Operations.Begin("Fetch evidence");
            using var handler = new EvidenceAndPanelHandler(failPanel: true);
            handler.DuringRequest = (panel, _) => { if (panel) document.MyExtendedFastaItems[0].Domains = "User edit"; };
            var task = AppOperationHost.OnUi(screen, () => screen.ViewModel.FetchEvidenceAsync(handler, lease.Context));
            bool refused = false;
            try { FinishBusyTask(task); task.GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { refused = true; }
            Check("Z1a a shell source edit still rejects fetched evidence after a panel failure",
                refused && !lease.Context.HasCommitted && changed == 0 && document.ToJsonText() == expected.ToJsonText());
        }
    }
}
