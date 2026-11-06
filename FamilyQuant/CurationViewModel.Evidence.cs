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
