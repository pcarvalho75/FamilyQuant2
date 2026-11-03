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

/// <summary>Stages protein evidence and the reviewed panel without touching the open file.
/// Only the final operation commit attaches both phases. A failure or cancellation attaches neither.</summary>
public static class EvidenceFetchSession
{
    public const string ProgressStage = "Evidence fetch";
    public const string EvidenceStage = "Protein evidence";

    public static Task<EvidenceFetchOutcome> FetchAsync(FamilyClusterFile file, HttpMessageHandler? handler = null,
        IProgress<FetchProgress>? progress = null, CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? positionAccessions = null) =>
        FetchStagedAsync(file, new OperationContext(cancellationToken), handler, progress, positionAccessions);

    public static Task<EvidenceFetchOutcome> FetchAsync(FamilyClusterFile file, OperationContext operation,
        HttpMessageHandler? handler = null, IReadOnlyCollection<string>? positionAccessions = null) => FetchStagedAsync(file, operation, handler, null, positionAccessions);

    private static async Task<EvidenceFetchOutcome> FetchStagedAsync(FamilyClusterFile file, OperationContext operation,
        HttpMessageHandler? handler, IProgress<FetchProgress>? progress, IReadOnlyCollection<string>? positionAccessions)
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
            MyExtendedFastaItems = originals.Select(p => new FastaItemExtended
            {
                SequenceIdentifier = p.SequenceIdentifier, Sequence = p.Sequence, Description = p.Description,
                UserID = p.UserID, USearchID = p.USearchID, UserAnnotation = p.UserAnnotation,
                Domains = p.Domains, Evidence = EvidenceApply.Clone(p.Evidence),
            }).ToList(),
        };
        var stagedEvidence = staged.MyExtendedFastaItems.Select(p => p.Evidence).ToArray();
        operation.ThrowIfCancellationRequested();
        var evidence = await EvidenceFetcher.FetchAsync(staged, handler,
            new FetchStageProgress(operation, EvidenceStage, progress), operation.CancellationToken, positionAccessions).ConfigureAwait(false);
        ReferencePanel? panel = null;
        if (ReferencePanelFetcher.FamiliesFound(staged).Count > 0)
        {
            panel = await ReferencePanelFetcher.FetchAndStoreAsync(staged, handler: handler,
                progress: new FetchStageProgress(operation, ReferencePanelFetcher.ProgressStage, progress),
                cancellationToken: operation.CancellationToken).ConfigureAwait(false);
            if (evidence.Asked > 0 && evidence.Provenance.Release != panel.Release)
                throw new InvalidDataException("The UniProt release changed between protein evidence and the panel. No evidence was applied.");
        }
        var outcome = new EvidenceFetchOutcome(evidence, panel, null);
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
            operation.Report(stage, value.CompletedBatches.ToString(CultureInfo.InvariantCulture) + " of "
                + value.TotalBatches.ToString(CultureInfo.InvariantCulture) + "; " + value.Found.ToString(CultureInfo.InvariantCulture)
                + " entries received", value.CompletedBatches, value.TotalBatches);
            progress?.Report(value);
            operation.ThrowIfCancellationRequested();
        }
    }

    public static bool IsFetchFailure(Exception ex) => ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException;
}
