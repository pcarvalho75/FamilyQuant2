using FamilyQuant.Operations;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class CurationQuantitationViewModel
{
    public async Task<bool> AttachWithOperationAsync(IReadOnlyList<QuantRow>? rows,
        IReadOnlyDictionary<string, double>? perRunTic, OperationContext operation)
    {
        int generation = ++fullXicGeneration;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fullXicDone?.TrySetResult(false); fullXicDone = done;
        ClearFullXic();
        try
        {
            operation.Report("attribution", "Preparing the full-XIC family attribution");
            if (rows == null || rows.Count == 0)
            {
                operation.Commit(() => ApplyFullXic(null, null, generation)); return true;
            }
            fullXicPending = true;
            Summary = "Attributing full XIC to the current families...";
            PublishChanged();
            var rowCopy = rows.ToList();
            var ticCopy = perRunTic?.ToDictionary(p => p.Key, p => p.Value);
            var proteins = ProteinSnapshot();
            var prepared = await Task.Run(() => PrepareFullXic(rowCopy, ticCopy, proteins, operation, fullXicCacheDirectory), operation.CancellationToken);
            operation.ThrowIfCancellationRequested();
            if (generation != fullXicGeneration) return false;
            operation.Commit(() => ApplyFullXic(prepared, null, generation));
            return true;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (generation == fullXicGeneration)
            {
                ClearFullXic(); ApplyFullXic(null, "Attribution was cancelled. The stored quantitation was kept.", generation);
            }
            return false;
        }
        catch (Exception ex)
        {
            if (generation == fullXicGeneration) { ClearFullXic(); ApplyFullXic(null, ex.Message, generation); }
            return false;
        }
        finally { done.TrySetResult(true); }
    }

    internal sealed record RecalculationPrepared(PatternLabProject Project, FamilyClusterFile Calculated,
        NormalizationMode Mode, SharedPeptidePolicy Policy,
        IReadOnlyList<(FastaItemExtended Item, int Family, string Annotation)> Source);

    internal Task<RecalculationPrepared> PrepareRecalculationAsync(NormalizationMode mode,
        SharedPeptidePolicy policy, OperationContext operation)
    {
        var project = CurrentPLP ?? throw new InvalidOperationException("Load a PatternLab project before recalculating its shares.");
        return Task.Run(() =>
        {
            operation.Report("recalculate", "Preparing the current family grouping");
            var source = Document.MyExtendedFastaItems.Select(p => (p, p.UserID, p.UserAnnotation ?? "")).ToArray();
            var calculated = FamilyClusterFile.FromJsonText(Document.ToJsonText());
            operation.ThrowIfCancellationRequested();
            calculated.SetLookupDictionary(project);
            operation.Report("recalculate", "Calculating the family shares");
            calculated.AttributeXICToGroups(project, mode, policy);
            operation.ThrowIfCancellationRequested();
            return new RecalculationPrepared(project, calculated, mode, policy, source);
        }, operation.CancellationToken);
    }

    internal void PublishRecalculation(RecalculationPrepared prepared)
    {
        if (!ReferenceEquals(CurrentPLP, prepared.Project) || Document.MyExtendedFastaItems.Count != prepared.Source.Count
            || Document.MyExtendedFastaItems.Where((p, i) => !ReferenceEquals(p, prepared.Source[i].Item)
                || p.UserID != prepared.Source[i].Family || (p.UserAnnotation ?? "") != prepared.Source[i].Annotation).Any())
            throw new InvalidOperationException("The curation changed during recalculation. Recalculate the current families again.");
        for (int i = 0; i < prepared.Source.Count; i++)
            Document.MyExtendedFastaItems[i].XICUser = prepared.Calculated.MyExtendedFastaItems[i].XICUser;
        DisplayedMode = prepared.Mode; DisplayedSharedPolicy = prepared.Policy;
        WriteRecalculationDiagnostic(prepared.Mode, prepared.Policy);
    }
}
