using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    public async Task<EvidenceFetchOutcome> FetchEvidenceAsync(HttpMessageHandler? handler, OperationContext operation,
        IReadOnlyCollection<int>? additionalPositionFamilyIds = null, bool retryPanelOnly = false, UniProtFetchOptions? options = null)
    {
        operation.Report("evidence", "Preparing the evidence fetch");
        var source = Document.MyExtendedFastaItems.Select(p => (Item: p, p.SequenceIdentifier, p.Domains, p.Evidence)).ToArray();
        string domainSource = Document.DomainSource;
        var evidenceSource = Document.EvidenceSource;
        var positionAccessions = Quantitation.PositionAccessions(additionalPositionFamilyIds);
        var prepared = await Task.Run(() => FamilyClusterFile.FromJsonText(Document.ToJsonText()), operation.CancellationToken);
        operation.ThrowIfCancellationRequested();
        var result = retryPanelOnly
            ? await EvidenceFetchSession.RetryPanelAsync(prepared, handler, new CurationFetchProgress(operation), operation.CancellationToken, options)
            : await EvidenceFetchSession.FetchAsync(prepared, handler, new CurationFetchProgress(operation),
                cancellationToken: operation.CancellationToken, positionAccessions: positionAccessions, options: options);
        operation.ThrowIfCancellationRequested();
        if (!result.Changed) return result;
        operation.Commit(() =>
        {
            if (Document.MyExtendedFastaItems.Count != source.Length || Document.DomainSource != domainSource
                || !ReferenceEquals(Document.EvidenceSource, evidenceSource)
                || Document.MyExtendedFastaItems.Where((p, i) => !ReferenceEquals(p, source[i].Item)
                    || p.SequenceIdentifier != source[i].SequenceIdentifier || p.Domains != source[i].Domains
                    || !ReferenceEquals(p.Evidence, source[i].Evidence)).Any())
                throw new InvalidOperationException("The protein evidence changed during the fetch. Fetch again for the current document.");
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

    private sealed class CurationFetchProgress(OperationContext operation) : IProgress<FetchProgress>
    {
        public void Report(FetchProgress value) => operation.Report("evidence",
            value.Message.Length > 0 ? value.Message : "Fetching evidence: " + value.CompletedBatches.ToString(CultureInfo.CurrentCulture) + " of "
            + value.TotalBatches.ToString(CultureInfo.CurrentCulture) + " batches or families; "
            + value.Found.ToString("N0", CultureInfo.CurrentCulture) + " entries received.", value.CompletedBatches, value.TotalBatches);
    }

    public static string DescribeEvidenceFetch(EvidenceFetchOutcome outcome)
    {
        var result = outcome.Evidence;
        string message = result.Asked == 0 ? "Stored protein evidence kept."
            : "Protein evidence saved for " + result.Found.ToString("N0", CultureInfo.CurrentCulture) + " proteins."
                + (result.Missing > 0 ? " " + result.Missing.ToString("N0", CultureInfo.CurrentCulture) + " entries kept their previous evidence." : "");
        if (outcome.Panel is { IsComplete: true })
            message += " Reference panel saved for " + outcome.Panel.Families.Count.ToString("N0", CultureInfo.CurrentCulture) + " families.";
        if (outcome.Panel is { IsComplete: false } panel)
        {
            var status = System.Text.RegularExpressions.Regex.Match(panel.FailureReason, @"\bHTTP [0-9]{3}\b");
            message += " Reference panel incomplete: " + panel.MissingFamilies.Count.ToString("N0", CultureInfo.CurrentCulture)
                + " families missing" + (panel.TotalBatches > 0 ? " in " + panel.FailedBatches.ToString("N0", CultureInfo.CurrentCulture)
                    + " of " + panel.TotalBatches.ToString("N0", CultureInfo.CurrentCulture) + " batches" : "")
                + (status.Success ? " (" + status.Value + ")" : " (UniProt request failed)") + ". Retry panel.";
        }
        if (result.PositionAsked > 0)
            message += " Position coordinates: " + result.PositionFound.ToString("N0", CultureInfo.CurrentCulture) + " of "
                + result.PositionAsked.ToString("N0", CultureInfo.CurrentCulture) + " requested proteins in "
                + result.PositionElapsedSeconds.ToString("N1", CultureInfo.CurrentCulture) + " s.";
        return message;
    }
}
