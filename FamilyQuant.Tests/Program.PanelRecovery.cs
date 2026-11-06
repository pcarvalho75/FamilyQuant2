using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void PanelRecoveryTests(string workDir)
    {
        using var scratch = new FetchFixtureScratch();
        var families = Enumerable.Range(0, 100).Select(i => "F" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + " family").ToArray();
        using var handler = new ResumeUniProtHandler { FailMarker = families[50] };
        var options = new UniProtFetchOptions
        {
            ResumeDirectory = scratch.Root,
            DelayAsync = (_, _) => Task.CompletedTask,
        };
        var panel = ReferencePanelFetcher.FetchAsync(families, handler, options: options).GetAwaiter().GetResult();
        var file = new FamilyClusterFile { EvidenceSource = new EvidenceProvenance { ReferencePanel = panel } };
        var reopened = FamilyClusterFile.FromJsonText(file.ToJsonText());
        Check("Z1d one refused family among 100 preserves 99 validated entries and names the missing family after reopening",
            !panel.IsComplete && panel.Entries.Count == 99 && panel.MissingFamilies.SequenceEqual(new[] { families[50] })
            && panel.TotalBatches == 1 && panel.FailedBatches == 1
            && reopened.EvidenceSource?.ReferencePanel is { IsComplete: false, Entries.Count: 99 } stored
            && stored.MissingFamilies.SequenceEqual(panel.MissingFamilies));
        bool Refuses(Action read)
        {
            try { read(); return false; }
            catch (InvalidDataException ex) { return ex.Message.Contains("incomplete", StringComparison.Ordinal) && ex.Message.Contains("Retry panel", StringComparison.Ordinal); }
        }
        Check("Z1d incomplete panels cannot become neighbour references", Refuses(() => ReferencePanelFetcher.References(panel)));
        Check("Z1d incomplete panels cannot train coherence profiles", Refuses(() => CoherenceCheck.Learn(file)));
        Check("Z1d incomplete panels cannot answer reference evidence", Refuses(() => NearestReviewed.ReferenceEvidence(file)));
        Check("Z1d incomplete panels cannot supply the family world", Refuses(() => FamilyWorld.Collect(file)));
        Check("Z1d incomplete panels cannot supply architecture profiles", Refuses(() => EvidenceArchitecture.Suggest(
            Array.Empty<FastaItemExtended>(), EvidenceClassifier.Propose(Array.Empty<FastaItemExtended>(),
                new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } }), panel)));
        var world = CoreOperation.RunAsync("Fetch family world", operation => FamilyWorldFetcher.FetchAndStoreAsync(file,
            new EvidenceTaskChoice(), operation, handler)).GetAwaiter().GetResult();
        Check("Z1d Fetch family world reports Retry panel before reading an incomplete panel",
            world.Status == OperationStatus.Failed && world.Failure!.Message.Contains("Retry panel", StringComparison.Ordinal));
        handler.FailMarker = null;
        var completed = ReferencePanelFetcher.FetchAsync(families, handler, options: options).GetAwaiter().GetResult();
        Check("Z1d retry completes a stored incomplete panel without retaining missing-family flags",
            completed.IsComplete && completed.Entries.Count == 100 && completed.MissingFamilies.Count == 0 && completed.FailedBatches == 0);
    }
}
