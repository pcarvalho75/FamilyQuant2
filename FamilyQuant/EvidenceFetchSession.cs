using FamilyQuant.Evidence;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed record EvidenceFetchOutcome(EvidenceFetchResult Evidence, ReferencePanel? Panel, Exception? PanelFailure)
{
    public bool Changed => Evidence.Asked > 0 || Panel != null;
}

/// <summary>The explicit online action; the grouping preview uses only its stored results.</summary>
public static class EvidenceFetchSession
{
    public static async Task<EvidenceFetchOutcome> FetchAsync(FamilyClusterFile file, HttpMessageHandler? handler = null,
        IProgress<FetchProgress>? progress = null)
    {
        var evidence = await EvidenceFetcher.FetchAsync(file, handler, progress);
        if (evidence.Asked > 0) file.DomainSource = evidence.Source;
        if (ReferencePanelFetcher.FamiliesFound(file).Count == 0) return new EvidenceFetchOutcome(evidence, null, null);
        try
        {
            var panel = await ReferencePanelFetcher.FetchAndStoreAsync(file, handler: handler, progress: progress);
            return new EvidenceFetchOutcome(evidence, panel, null);
        }
        catch (Exception ex) when (IsFetchFailure(ex))
        {
            return new EvidenceFetchOutcome(evidence, null, ex);
        }
    }

    public static bool IsFetchFailure(Exception ex) => ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException;
}
