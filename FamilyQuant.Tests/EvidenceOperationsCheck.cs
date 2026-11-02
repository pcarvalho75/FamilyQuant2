using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace FamilyQuant.Tests
{
    // A checker only: the long evidence steps on Ana's real file, with their progress printed as it comes,
    // and Cancel pressed part way through each.
    internal static class EvidenceOperationsCheck
    {
        internal const string Argument = "--evidence-operations-check";

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string input = args.Length > flag + 1 && args[flag + 1].Length > 0 ? args[flag + 1] : "D:/FamilyQuant-data/out-opus/e4-ana-panel.fcf";
                string[] scope = { "Toxin", "venom gland" };
                Func<ProteinEvidence, bool> inScope = e => EvidenceClassifier.IsInScope(e, scope);
                FamilyClusterFile file = FamilyClusterFile.Load(input);
                var clock = Stopwatch.StartNew();
                void Print(OperationProgress p) => Console.WriteLine("  [" + clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)
                    + " s] " + p.Stage + ": " + p.Message + (p.Total != null ? " (" + p.Completed + "/" + p.Total + ")" : ""));

                // Cancel the search at its second progress report.
                string before = file.ToJsonText();
                using var cancel = new CancellationTokenSource();
                int reports = 0;
                double cancelledAt = 0;
                clock.Restart();
                OperationResult<NeighbourSearch> stopped = CoreOperation.Run("Nearest reviewed protein",
                    op => NearestReviewed.SearchAndStore(file, inScope, op),
                    cancel.Token, new Program.InlineOperationProgress(p =>
                    {
                        Print(p);
                        if (p.Stage == NearestReviewed.ProgressStage && ++reports == 2) { cancelledAt = clock.Elapsed.TotalSeconds; cancel.Cancel(); }
                    }));
                double stoppedAfter = clock.Elapsed.TotalSeconds - cancelledAt;
                bool usearchLeft = Process.GetProcessesByName("usearch11.0.667_win32").Any(p => p.StartTime > DateTime.Now.AddSeconds(-clock.Elapsed.TotalSeconds));
                check("Cancel during the search on Ana's file leaves it exactly as it was",
                    stopped.Status == OperationStatus.Cancelled && file.ToJsonText() == before,
                    "stopped " + stoppedAfter.ToString("0.0", CultureInfo.InvariantCulture) + " s after Cancel");
                check("no USEARCH started by the cancelled search is left running", !usearchLeft, null);

                // The whole search, its progress printed as it comes.
                clock.Restart();
                OperationResult<NeighbourSearch> done = CoreOperation.Run("Nearest reviewed protein",
                    op => NearestReviewed.SearchAndStore(file, inScope, op), progress: new Program.InlineOperationProgress(Print));
                check("the search on Ana's file completes with progress and is stored by its commit",
                    done.Status == OperationStatus.Succeeded && ReferenceEquals(file.EvidenceSource?.NearestReviewed, done.Value),
                    done.Status == OperationStatus.Succeeded ? done.Value.Neighbours.Count + " neighbours in "
                        + clock.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s" : done.Failure?.Message);

                // The panel fetch, live, cancelled after five families; then whole.
                var fresh = FamilyClusterFile.Load(input);
                fresh.EvidenceSource!.ReferencePanel = null;
                string freshBefore = fresh.ToJsonText();
                using var cancelPanel = new CancellationTokenSource();
                clock.Restart();
                OperationResult<ReferencePanel> panelStopped = CoreOperation.RunAsync("Reference panel",
                    op => ReferencePanelFetcher.FetchAndStoreAsync(fresh, op, inScope), cancelPanel.Token,
                    new Program.InlineOperationProgress(p => { Print(p); if (p.Completed == 5) cancelPanel.Cancel(); })).GetAwaiter().GetResult();
                check("Cancel during the live panel fetch leaves the file exactly as it was",
                    panelStopped.Status == OperationStatus.Cancelled && fresh.ToJsonText() == freshBefore, panelStopped.Status.ToString());
                clock.Restart();
                OperationResult<ReferencePanel> panel = CoreOperation.RunAsync("Reference panel",
                    op => ReferencePanelFetcher.FetchAndStoreAsync(fresh, op, inScope), progress: new Program.InlineOperationProgress(Print)).GetAwaiter().GetResult();
                check("the live panel fetch completes with progress per family",
                    panel.Status == OperationStatus.Succeeded && fresh.EvidenceSource?.ReferencePanel == panel.Value,
                    panel.Status == OperationStatus.Succeeded ? panel.Value.Entries.Count + " entries, release " + panel.Value.Release
                        + ", " + clock.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s" : panel.Failure?.Message);
            }
            catch (Exception ex) { check("evidence operations check completed", false, ex.GetBaseException().ToString()); }
        }

    }
}
