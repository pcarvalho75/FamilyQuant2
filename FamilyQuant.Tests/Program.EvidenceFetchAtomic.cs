using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static FamilyClusterFile AtomicFetchFile() => new()
    {
        SourceSha256 = "fixture source", SourceSequenceCount = 1, DomainSource = "stored domains",
        LockedUserIds = new HashSet<int> { 71 },
        EvidenceSource = new EvidenceProvenance { Provider = "Stored provider", Release = "old",
            ReferencePanel = new ReferencePanel { Provider = "Stored provider", Release = "old" } },
        MyExtendedFastaItems = new List<FastaItemExtended> { new()
        {
            SequenceIdentifier = "tr|A0A710|FIXTURE", Sequence = "PEPTIDEK", Description = "Fetch fixture",
            UserID = 71, USearchID = 51, UserAnnotation = "Kept annotation", XIC = 1.25, XICUser = 2.5, FullXicShare = 0.25,
            Domains = "Stored domain", Evidence = new ProteinEvidence { Accession = "A0A710", Family = "stored family" },
        } },
    };

    private sealed class FetchOperationProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    private static void EvidenceFetchAtomicTests()
    {
        static OperationResult<EvidenceFetchOutcome> Run(FamilyClusterFile file, EvidenceAndPanelHandler handler,
            CancellationToken token = default, IProgress<OperationProgress>? progress = null, bool cancelAfter = false,
            CancellationTokenSource? cancellation = null) => Task.Run(() => CoreOperation.RunAsync("Fetch evidence", async context =>
            {
                var outcome = await EvidenceFetchSession.FetchAsync(file, context, handler);
                if (cancelAfter) cancellation!.Cancel();
                return outcome;
            }, token, progress)).GetAwaiter().GetResult();

        foreach (string phase in new[] { "before start", "evidence request", "between phases", "panel request", "panel complete", "before publication" })
        {
            var file = AtomicFetchFile(); string before = file.ToJsonText();
            var originalProtein = file.MyExtendedFastaItems[0]; var originalSource = file.EvidenceSource;
            using var cancellation = new CancellationTokenSource(); using var handler = new EvidenceAndPanelHandler();
            handler.DuringRequest = (panel, token) =>
            {
                if ((phase == "evidence request" && !panel) || (phase == "panel request" && panel))
                { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            };
            var progress = new FetchOperationProgress(value =>
            {
                bool completed = value.Total > 0 && value.Completed == value.Total;
                if ((phase == "between phases" && value.Stage == EvidenceFetchSession.EvidenceStage && completed)
                    || (phase == "panel complete" && value.Stage == ReferencePanelFetcher.ProgressStage && completed)
                    || (phase == "before publication" && value.Stage == EvidenceFetchSession.ProgressStage && value.Message.StartsWith("Publishing", StringComparison.Ordinal)))
                    cancellation.Cancel();
            });
            if (phase == "before start") cancellation.Cancel();
            var result = Run(file, handler, cancellation.Token, progress);
            Check("S9 cancel at " + phase + " preserves every original byte and object", result.Status == OperationStatus.Cancelled
                && file.ToJsonText() == before && ReferenceEquals(file.MyExtendedFastaItems[0], originalProtein)
                && ReferenceEquals(file.EvidenceSource, originalSource));
        }
        foreach (bool changedRelease in new[] { false, true })
        {
            var file = AtomicFetchFile(); string before = file.ToJsonText();
            using var handler = new EvidenceAndPanelHandler(failPanel: !changedRelease);
            if (changedRelease) handler.PanelRelease = "changed release";
            var result = Run(file, handler);
            Check("S9 " + (changedRelease ? "cross-phase release change" : "panel failure") + " leaves the complete file unchanged",
                result.Status == OperationStatus.Failed && file.ToJsonText() == before);
        }
        {
            var file = AtomicFetchFile(); var originalProtein = file.MyExtendedFastaItems[0];
            bool stagedUntilPanel = false; using var handler = new EvidenceAndPanelHandler();
            handler.DuringRequest = (panel, _) => { if (panel) stagedUntilPanel = originalProtein.Domains == "Stored domain" && file.EvidenceSource!.Release == "old"; };
            var result = Run(file, handler);
            Check("S9 successful fetch stages both phases and preserves protein identity, IDs, locks and quantitation", stagedUntilPanel
                && result.Status == OperationStatus.Succeeded && result.Value.Changed && result.Value.PanelFailure == null
                && ReferenceEquals(file.MyExtendedFastaItems[0], originalProtein) && originalProtein.UserID == 71 && originalProtein.USearchID == 51
                && originalProtein.UserAnnotation == "Kept annotation" && originalProtein.Sequence == "PEPTIDEK"
                && originalProtein.XIC == 1.25 && originalProtein.XICUser == 2.5 && originalProtein.FullXicShare == 0.25
                && file.LockedUserIds.SetEquals(new[] { 71 }) && file.EvidenceSource?.Release == "2026_03"
                && file.EvidenceSource.ReferencePanel?.Entries.Count == 1 && originalProtein.Evidence?.TissueSpecificity.Count == 1);
        }
        {
            var file = AtomicFetchFile(); using var cancellation = new CancellationTokenSource(); using var handler = new EvidenceAndPanelHandler();
            var result = Run(file, handler, cancellation.Token, cancelAfter: true, cancellation: cancellation);
            Check("S9 successful final commit wins a later cancel", result.Status == OperationStatus.Succeeded
                && file.EvidenceSource?.ReferencePanel?.Entries.Count == 1);
        }
        {
            var file = AtomicFetchFile(); string before = file.ToJsonText();
            var expected = FamilyClusterFile.FromJsonText(before); expected.MyExtendedFastaItems[0].UserAnnotation = "User edit";
            using var handler = new EvidenceAndPanelHandler();
            handler.DuringRequest = (panel, _) => { if (panel) file.MyExtendedFastaItems[0].UserAnnotation = "User edit"; };
            var result = Run(file, handler);
            Check("S9 a source edit during fetching is retained and refuses the stale transaction", result.Status == OperationStatus.Failed
                && file.ToJsonText() == expected.ToJsonText());
        }
        {
            var file = AtomicFetchFile(); file.MyExtendedFastaItems[0].SequenceIdentifier = "local_entry";
            string before = file.ToJsonText(); using var cancellation = new CancellationTokenSource(); using var handler = new EvidenceAndPanelHandler();
            handler.DuringRequest = (panel, token) => { if (panel) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); } };
            var result = Run(file, handler, cancellation.Token);
            Check("S9 cancel at a panel-only fetch preserves the existing provenance object", result.Status == OperationStatus.Cancelled
                && before == file.ToJsonText() && handler.ProteinRequests == 0);
        }
    }
}
