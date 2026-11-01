using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void EvidenceOperationTests()
        {
            string a = NearestBase(180, 131), b = NearestBase(160, 137);
            var file = new FamilyClusterFile();
            for (int i = 0; i < 9; i++)
                file.MyExtendedFastaItems.Add(NearestItem("tr|A0A9" + i.ToString("00") + "|OP_TEST", NearestMutate(i % 2 == 0 ? a : b, 6 + i), false));
            file.MyExtendedFastaItems.Add(NearestItem("sp|P70001|OPA_TEST", a, true, "Alpha family"));
            file.MyExtendedFastaItems.Add(NearestItem("sp|P70002|OPB_TEST", b, true, "Beta family"));
            List<FastaItemExtended> targets = NearestReviewed.Targets(file);
            List<NeighbourReference> references = NearestReviewed.References(file);

            NearestReviewed.ForgetMemo();
            NeighbourSearch whole;
            try { whole = NearestReviewed.Search(targets, references); }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  evidence operations - USEARCH could not run here (" + ex.GetBaseException().Message.Split('\n')[0] + ")");
                skipped++;
                return;
            }
            NearestReviewed.ForgetMemo();
            NeighbourSearch inRuns = NearestReviewed.Search(targets, references, queriesPerRun: 4);
            Check("aligning in runs of four gives exactly the neighbours of one run",
                JsonSerializer.Serialize(inRuns) == JsonSerializer.Serialize(whole) && whole.Neighbours.Count == 9, whole.Neighbours.Count.ToString());

            NearestReviewed.ForgetMemo();
            var reports = new List<OperationProgress>();
            OperationResult<NeighbourSearch> done = CoreOperation.Run("Nearest reviewed protein",
                op => NearestReviewed.SearchAndStore(file, null, op, queriesPerRun: 4), progress: new InlineOperationProgress(reports.Add));
            var own = reports.Where(r => r.Stage == NearestReviewed.ProgressStage).ToList();
            Check("the search reports its progress after every run, counted in proteins (USEARCH's own messages aside)",
                done.Status == OperationStatus.Succeeded && own.Select(r => r.Completed).SequenceEqual(new long?[] { 0, 4, 8, 11 })
                && own.All(r => r.Total == 11) && reports.Count(r => r.Stage != NearestReviewed.ProgressStage) == 3,
                string.Join(",", reports.Select(r => r.Stage + ":" + r.Completed)));
            Check("a completed search is stored by its commit", ReferenceEquals(file.EvidenceSource?.NearestReviewed, done.Value));

            NeighbourSearch? before = file.EvidenceSource?.NearestReviewed;
            string beforeText = file.ToJsonText();
            NearestReviewed.ForgetMemo();
            using var cancel = new CancellationTokenSource();
            int seen = 0;
            OperationResult<NeighbourSearch> stopped = CoreOperation.Run("Nearest reviewed protein",
                op => NearestReviewed.SearchAndStore(file, null, op, queriesPerRun: 4),
                cancel.Token, new InlineOperationProgress(_ => { if (++seen == 2) cancel.Cancel(); }));
            Check("Cancel during the search leaves the file exactly as it was",
                stopped.Status == OperationStatus.Cancelled && ReferenceEquals(file.EvidenceSource?.NearestReviewed, before)
                && file.ToJsonText() == beforeText);
            long runs = NearestReviewed.SearchRunCount;
            NearestReviewed.Search(targets, references);
            Check("a cancelled search is not remembered: the next one runs again", NearestReviewed.SearchRunCount == runs + 1);

            var panelFile = new FamilyClusterFile();
            panelFile.MyExtendedFastaItems.Add(NearestItem("tr|A0A951|P_TEST", a, false, "alpha family"));
            panelFile.MyExtendedFastaItems.Add(NearestItem("tr|A0A952|P_TEST", b, false, "beta family"));
            CannedPanelUniProt Handler() => new CannedPanelUniProt(
                (HttpStatusCode.OK, PanelPage(PanelEntryJson("P71001", "Belongs to the alpha family.", a)), "2026_03", null),
                (HttpStatusCode.OK, PanelPage(PanelEntryJson("P71002", "Belongs to the beta family.", b)), "2026_03", null));
            var panelReports = new List<OperationProgress>();
            OperationResult<ReferencePanel> fetched = CoreOperation.RunAsync("Reference panel",
                op => ReferencePanelFetcher.FetchAndStoreAsync(panelFile, op, null, Handler()),
                progress: new InlineOperationProgress(panelReports.Add)).GetAwaiter().GetResult();
            Check("the panel fetch reports each family and stores the panel by its commit",
                fetched.Status == OperationStatus.Succeeded && ReferenceEquals(panelFile.EvidenceSource?.ReferencePanel, fetched.Value)
                && panelReports.Select(r => r.Completed).SequenceEqual(new long?[] { 0, 1, 2 })
                && panelReports.All(r => r.Stage == ReferencePanelFetcher.ProgressStage && r.Total == 2),
                string.Join(",", panelReports.Select(r => r.Completed)));

            var untouched = new FamilyClusterFile();
            untouched.MyExtendedFastaItems.AddRange(panelFile.MyExtendedFastaItems.Select(i => NearestItem(i.SequenceIdentifier, i.Sequence, false, i.Evidence!.Family)));
            string untouchedText = untouched.ToJsonText();
            using var cancelPanel = new CancellationTokenSource();
            OperationResult<ReferencePanel> cancelled = CoreOperation.RunAsync("Reference panel",
                op => ReferencePanelFetcher.FetchAndStoreAsync(untouched, op, null, Handler()), cancelPanel.Token,
                new InlineOperationProgress(r => { if (r.Completed == 1) cancelPanel.Cancel(); })).GetAwaiter().GetResult();
            Check("Cancel during the panel fetch leaves the file exactly as it was",
                cancelled.Status == OperationStatus.Cancelled && untouched.EvidenceSource == null && untouched.ToJsonText() == untouchedText);
        }
    }
}
