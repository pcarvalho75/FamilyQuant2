using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    private int domainPreviewGeneration;
    public IReadOnlyDictionary<FastaItemExtended, string> DomainPreview { get; private set; } = new Dictionary<FastaItemExtended, string>();

    public async Task<EvidenceFetchOutcome> FetchEvidenceAsync(HttpMessageHandler? handler, OperationContext operation,
        IReadOnlyCollection<int>? additionalPositionFamilyIds = null, bool retryPanelOnly = false, UniProtFetchOptions? options = null)
    {
        operation.Report("evidence", "Preparing the evidence fetch");
        var source = Document.MyExtendedFastaItems.Select(p => (Item: p, p.SequenceIdentifier, p.Sequence, p.Description, p.Domains, p.Evidence)).ToArray();
        string sourceFingerprint = EvidenceClassifier.Fingerprint(Document.MyExtendedFastaItems);
        string domainSource = Document.DomainSource;
        var evidenceSource = Document.EvidenceSource;
        SynchronizationContext? ui = SynchronizationContext.Current;
        int previewRequest = Interlocked.Increment(ref domainPreviewGeneration);
        var positionAccessions = Quantitation.PositionAccessions(additionalPositionFamilyIds);
        try
        {
            var prepared = await Task.Run(() => Document.DeepCopy(operation), operation.CancellationToken);
            operation.ThrowIfCancellationRequested();
            var result = retryPanelOnly
                ? await EvidenceFetchSession.RetryPanelAsync(prepared, handler, new CurationFetchProgress(operation), operation.CancellationToken, options)
                : await EvidenceFetchSession.FetchAsync(prepared, handler, new CurationFetchProgress(operation),
                    cancellationToken: operation.CancellationToken, positionAccessions: positionAccessions, options: options,
                    proteinDomainsReady: ready => OnPreviewContext(() =>
                    {
                        operation.ThrowIfCancellationRequested();
                        EnsureCurrentSource();
                        if (ready.Proteins.Count > 0 && string.IsNullOrWhiteSpace(ready.Release))
                            throw new InvalidOperationException("Fetched domain display has no UniProt release.");
                        if (previewRequest != domainPreviewGeneration) return;
                        var pending = new Dictionary<FastaItemExtended, string>(ReferenceEqualityComparer.Instance);
                        foreach (var row in ready.Proteins)
                        {
                            if (row.ProteinIndex < 0 || row.ProteinIndex >= source.Length || row.Identifier != source[row.ProteinIndex].SequenceIdentifier
                                || row.Sequence != source[row.ProteinIndex].Sequence) throw new InvalidOperationException("Fetched domain display does not match the open proteins.");
                            pending.Add(source[row.ProteinIndex].Item, row.Domains);
                        }
                        DomainPreview = new ReadOnlyDictionary<FastaItemExtended, string>(pending);
                        Changed(nameof(DomainPreview));
                    }));
            operation.ThrowIfCancellationRequested();
            if (!result.Changed) return result;
            operation.Commit(() =>
            {
                EnsureCurrentSource();
                for (int i = 0; i < source.Length; i++)
                {
                    source[i].Item.Domains = prepared.MyExtendedFastaItems[i].Domains;
                    source[i].Item.Evidence = prepared.MyExtendedFastaItems[i].Evidence;
                }
                Document.DomainSource = prepared.DomainSource;
                Document.EvidenceSource = prepared.EvidenceSource;
                NotifyDocumentChanged();
            });
            return result;
        }
        finally
        {
            await OnPreviewContext(() =>
            {
                if (previewRequest != domainPreviewGeneration) return;
                DomainPreview = new Dictionary<FastaItemExtended, string>();
                Changed(nameof(DomainPreview));
            });
        }

        void EnsureCurrentSource()
        {
            if (Document.MyExtendedFastaItems.Count != source.Length || Document.DomainSource != domainSource
                || !ReferenceEquals(Document.EvidenceSource, evidenceSource) || EvidenceClassifier.Fingerprint(Document.MyExtendedFastaItems) != sourceFingerprint
                || Document.MyExtendedFastaItems.Where((protein, index) => !ReferenceEquals(protein, source[index].Item)
                    || protein.SequenceIdentifier != source[index].SequenceIdentifier || protein.Sequence != source[index].Sequence
                    || protein.Description != source[index].Description || protein.Domains != source[index].Domains
                    || !ReferenceEquals(protein.Evidence, source[index].Evidence)).Any())
                throw new InvalidOperationException("The protein evidence changed during the fetch. Fetch again for the current document.");
        }

        Task OnPreviewContext(Action publish)
        {
            if (ui == null || ReferenceEquals(SynchronizationContext.Current, ui)) { publish(); return Task.CompletedTask; }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ui.Post(_ =>
            {
                try { publish(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
            }, null);
            return completion.Task;
        }
    }

    private sealed class CurationFetchProgress(OperationContext operation) : IProgress<FetchProgress>
    {
        public void Report(FetchProgress value) => operation.Report("evidence",
            value.Message.Length > 0 ? value.Message : "Fetching evidence: " + value.CompletedBatches.ToString(CultureInfo.CurrentCulture) + " of "
            + value.TotalBatches.ToString(CultureInfo.CurrentCulture) + " batches or families; "
            + value.Found.ToString("N0", CultureInfo.CurrentCulture) + " entries received.", value.CompletedBatches, value.TotalBatches);
    }

    public static string DescribeEvidenceFetch(EvidenceFetchOutcome outcome, bool panelOnly = false)
    {
        var result = outcome.Evidence;
        static string Count(int value, string singular, string plural) => value.ToString("N0", CultureInfo.CurrentCulture)
            + " " + (value == 1 ? singular : plural);
        string message = result.Asked == 0 ? panelOnly ? "Stored protein evidence kept."
                : "No protein in this file has a UniProt code. A database of another origin stays unassigned."
            : "Protein evidence added for " + Count(result.Found, "protein", "proteins") + "."
                + (result.Missing > 0 ? " " + Count(result.Missing, "entry kept its", "entries kept their") + " previous evidence." : "");
        if (outcome.Changed) message += " Use File > Save to keep these changes.";
        if (outcome.Panel is { IsComplete: true })
            message += " Reference panel added for " + Count(outcome.Panel.Families.Count, "family", "families") + ".";
        if (outcome.Panel is { IsComplete: false } panel)
        {
            var status = System.Text.RegularExpressions.Regex.Match(panel.FailureReason, @"\bHTTP [0-9]{3}\b");
            message += " Reference panel incomplete: " + Count(panel.MissingFamilies.Count, "family", "families")
                + " missing" + (panel.TotalBatches > 0 ? " in " + panel.FailedBatches.ToString("N0", CultureInfo.CurrentCulture)
                    + " of " + Count(panel.TotalBatches, "batch", "batches") : "")
                + (status.Success ? " (" + status.Value + ")" : " (UniProt request failed)") + ". Retry panel.";
        }
        if (result.PositionAsked > 0)
            message += " Position coordinates: " + result.PositionFound.ToString("N0", CultureInfo.CurrentCulture) + " of "
                + result.PositionAsked.ToString("N0", CultureInfo.CurrentCulture) + " requested proteins in "
                + result.PositionElapsedSeconds.ToString("N1", CultureInfo.CurrentCulture) + " s.";
        return message;
    }
}
