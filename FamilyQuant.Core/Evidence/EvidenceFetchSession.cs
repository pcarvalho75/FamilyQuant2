using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed record EvidenceFetchOutcome(EvidenceFetchResult Evidence, ReferencePanel? Panel, Exception? PanelFailure)
{
    public bool Changed => Evidence.Asked > 0 || Panel != null;
}

public sealed record ProteinDomainPreview(int ProteinIndex, string Identifier, string Sequence, string Domains);
public sealed record ProteinDomainPreviewBatch(IReadOnlyList<ProteinDomainPreview> Proteins, string Release);

/// <summary>Stages protein evidence and the reviewed panel without touching the open file.
/// Only the final operation commit attaches the result. A panel failure keeps fetched protein evidence;
/// cancellation, a release change or a source edit attaches neither phase.</summary>
public static class EvidenceFetchSession
{
    public const string ProgressStage = "Evidence fetch";
    public const string EvidenceStage = "Protein evidence";

    internal static FastaItemExtended CloneProteinForFetch(FastaItemExtended source) => new()
    {
        SequenceIdentifier = source.SequenceIdentifier, Sequence = source.Sequence, Description = source.Description,
        AssignmentState = source.AssignmentState, USearchID = source.USearchID, UserAnnotation = source.UserAnnotation,
        Domains = source.Domains, Evidence = EvidenceApply.Clone(source.Evidence),
    };

    public static Task<EvidenceFetchOutcome> FetchAsync(FamilyClusterFile file, HttpMessageHandler? handler = null,
        IProgress<FetchProgress>? progress = null, CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? positionAccessions = null, UniProtFetchOptions? options = null,
        Func<ProteinDomainPreviewBatch, Task>? proteinDomainsReady = null) =>
        FetchStagedAsync(file, new OperationContext(cancellationToken), handler, progress, positionAccessions, options, false, proteinDomainsReady);

    public static Task<EvidenceFetchOutcome> FetchAsync(FamilyClusterFile file, OperationContext operation,
        HttpMessageHandler? handler = null, IReadOnlyCollection<string>? positionAccessions = null, UniProtFetchOptions? options = null,
        Func<ProteinDomainPreviewBatch, Task>? proteinDomainsReady = null) => FetchStagedAsync(file, operation, handler, null, positionAccessions, options, false, proteinDomainsReady);

    public static Task<EvidenceFetchOutcome> RetryPanelAsync(FamilyClusterFile file, HttpMessageHandler? handler = null,
        IProgress<FetchProgress>? progress = null, CancellationToken cancellationToken = default, UniProtFetchOptions? options = null) =>
        FetchStagedAsync(file, new OperationContext(cancellationToken), handler, progress, null, options, true);

    private static async Task<EvidenceFetchOutcome> FetchStagedAsync(FamilyClusterFile file, OperationContext operation,
        HttpMessageHandler? handler, IProgress<FetchProgress>? progress, IReadOnlyCollection<string>? positionAccessions, UniProtFetchOptions? options,
        bool panelOnly, Func<ProteinDomainPreviewBatch, Task>? proteinDomainsReady = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(operation);
        operation.Report(ProgressStage, "Preparing the evidence fetch");
        var originals = file.MyExtendedFastaItems.ToArray();
        string[] domains = originals.Select(p => p.Domains).ToArray();
        string fingerprint = EvidenceClassifier.Fingerprint(originals);
        string source = JsonSerializer.Serialize(file.EvidenceSource);
        string metadata = Metadata();
        // Copy only the data these fetchers read. Sequences are immutable strings; mutable evidence
        // and provenance are detached, including the panel-only path where E2 has no accessions.
        var staged = new FamilyClusterFile
        {
            DomainSource = file.DomainSource,
            EvidenceSource = EvidenceApply.Clone(file.EvidenceSource),
            MyExtendedFastaItems = originals.Select(CloneProteinForFetch).ToList(),
        };
        var stagedEvidence = staged.MyExtendedFastaItems.Select(p => p.Evidence).ToArray();
        operation.ThrowIfCancellationRequested();
        var evidence = panelOnly ? new EvidenceFetchResult
        {
            Provenance = staged.EvidenceSource ?? new EvidenceProvenance(),
            Found = staged.MyExtendedFastaItems.Count(p => p.Evidence != null),
            WithDomains = staged.MyExtendedFastaItems.Count(p => !string.IsNullOrWhiteSpace(p.Domains)),
        } : await EvidenceFetcher.FetchAsync(staged, handler,
            new FetchStageProgress(operation, EvidenceStage, progress), operation.CancellationToken, positionAccessions, options).ConfigureAwait(false);
        operation.ThrowIfCancellationRequested();
        if (!panelOnly && proteinDomainsReady != null)
        {
            var ready = staged.MyExtendedFastaItems.Select((protein, index) => (protein, index))
                .Where(row => !ReferenceEquals(row.protein.Evidence, stagedEvidence[row.index]))
                .Select(row => new ProteinDomainPreview(row.index, row.protein.SequenceIdentifier, row.protein.Sequence, row.protein.Domains)).ToArray();
            await proteinDomainsReady(new ProteinDomainPreviewBatch(ready, evidence.Provenance.Release)).ConfigureAwait(false);
            operation.ThrowIfCancellationRequested();
        }
        ReferencePanel? panel = null;
        Exception? panelFailure = null;
        var families = ReferencePanelFetcher.FamiliesFound(staged);
        if (families.Count > 0)
        {
            try
            {
                panel = await ReferencePanelFetcher.FetchAndStoreAsync(staged, handler: handler,
                    progress: new FetchStageProgress(operation, ReferencePanelFetcher.ProgressStage, progress),
                    cancellationToken: operation.CancellationToken,
                    expectedRelease: (evidence.Asked > 0 || panelOnly) && evidence.Provenance.Release.Length > 0
                        ? evidence.Provenance.Release : null, options: options).ConfigureAwait(false);
                if (!panel.IsComplete) panelFailure = new HttpRequestException(panel.FailureReason);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
            {
                operation.ThrowIfCancellationRequested();
                panelFailure = ex;
                panel = new ReferencePanel
                {
                    Provider = "UniProt", Release = evidence.Provenance.Release,
                    Families = families, MissingFamilies = families.ToList(), FailureReason = ex.Message,
                    TotalBatches = ReferencePanelFetcher.PlannedStreamBatchCount(families), FailedBatches = 1,
                };
                staged.EvidenceSource ??= new EvidenceProvenance();
                staged.EvidenceSource.ReferencePanel = panel;
            }
        }
        var outcome = new EvidenceFetchOutcome(evidence, panel, panelFailure);
        operation.Report(ProgressStage, "Publishing fetched evidence and reviewed panel", 1, 1);
        operation.Commit(() =>
        {
            if (file.MyExtendedFastaItems.Count != originals.Length
                || originals.Where((p, index) => !ReferenceEquals(p, file.MyExtendedFastaItems[index]) || p.Domains != domains[index]).Any()
                || EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems) != fingerprint
                || JsonSerializer.Serialize(file.EvidenceSource) != source || Metadata() != metadata)
                throw new InvalidDataException("The file changed while evidence was fetched. No fetched evidence was applied.");
            operation.ThrowIfCancellationRequested();
            if (!outcome.Changed) return;
            // All validation and cancellation checks precede publication. A cancel after this boundary
            // loses to the completed transaction, so the loop cannot expose a cancelled half-commit.
            for (int index = 0; index < originals.Length; index++)
            {
                var prepared = staged.MyExtendedFastaItems[index];
                if (!ReferenceEquals(prepared.Evidence, stagedEvidence[index])) originals[index].Evidence = prepared.Evidence;
                originals[index].Domains = prepared.Domains;
            }
            file.DomainSource = staged.DomainSource;
            file.EvidenceSource = staged.EvidenceSource;
        });
        return outcome;

        string Metadata() => JsonSerializer.Serialize(new
        {
            file.DomainSource, file.SourceSha256, file.SourceSequenceCount, file.ClusteringIdentity,
            file.WordSimilarity, file.IsKeywordSuperCluster, Locks = file.LockedUserIds.OrderBy(value => value),
        });
    }

    private sealed class FetchStageProgress(OperationContext operation, string stage, IProgress<FetchProgress>? progress) : IProgress<FetchProgress>
    {
        public void Report(FetchProgress value)
        {
            operation.Report(stage, value.Message.Length > 0 ? value.Message : value.CompletedBatches.ToString(CultureInfo.InvariantCulture) + " of "
                + value.TotalBatches.ToString(CultureInfo.InvariantCulture) + "; " + value.Found.ToString(CultureInfo.InvariantCulture)
                + " entries received", value.CompletedBatches, value.TotalBatches);
            progress?.Report(value);
            operation.ThrowIfCancellationRequested();
        }
    }

    public static bool IsFetchFailure(Exception ex) => ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or UniProtReleaseException;
}
