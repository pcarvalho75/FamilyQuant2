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
        IReadOnlyCollection<int>? additionalPositionFamilyIds = null)
    {
        operation.Report("evidence", "Preparing the evidence fetch");
        var source = Document.MyExtendedFastaItems.Select(p => (Item: p, p.SequenceIdentifier, p.Domains, p.Evidence)).ToArray();
        string domainSource = Document.DomainSource;
        var evidenceSource = Document.EvidenceSource;
        var positionAccessions = Quantitation.PositionAccessions(additionalPositionFamilyIds);
        var prepared = await Task.Run(() => FamilyClusterFile.FromJsonText(Document.ToJsonText()), operation.CancellationToken);
        operation.ThrowIfCancellationRequested();
        var result = await EvidenceFetchSession.FetchAsync(prepared, handler, new CurationFetchProgress(operation),
            cancellationToken: operation.CancellationToken, positionAccessions: positionAccessions);
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
            "Fetching evidence: " + value.CompletedBatches.ToString(CultureInfo.CurrentCulture) + " of "
            + value.TotalBatches.ToString(CultureInfo.CurrentCulture) + " batches or families; "
            + value.Found.ToString("N0", CultureInfo.CurrentCulture) + " entries received.", value.CompletedBatches, value.TotalBatches);
    }

    public static string DescribeEvidenceFetch(EvidenceFetchOutcome outcome)
    {
        var result = outcome.Evidence;
        int missing = result.Asked - result.Found;
        string message = result.Asked == 0
            ? "No protein in this file has a UniProt code. A database of another origin stays unassigned."
            : result.WithDomains.ToString("N0", CultureInfo.CurrentCulture) + " of " + result.Asked.ToString("N0", CultureInfo.CurrentCulture)
                + " proteins have Pfam domains in " + result.Source + "."
                + (missing > 0 ? " " + missing.ToString("N0", CultureInfo.CurrentCulture) + " entries were not found and kept their previous evidence." : "")
                + " The Domains column and Search use the stored evidence. Proteins too short for Pfam may have none. Save the file to keep it.";
        if (outcome.Panel is { IsComplete: true })
            message += " Reviewed reference panel: " + outcome.Panel.Entries.Count.ToString("N0", CultureInfo.CurrentCulture)
                + " entries from " + outcome.Panel.Families.Count.ToString("N0", CultureInfo.CurrentCulture) + " families, " + outcome.Panel.Release + ". Stored for offline grouping.";
        message += result.PositionAsked > 0
            ? " Position coordinates: " + result.PositionFound.ToString("N0", CultureInfo.CurrentCulture) + " of "
                + result.PositionAsked.ToString("N0", CultureInfo.CurrentCulture) + " requested proteins in "
                + result.PositionElapsedSeconds.ToString("N1", CultureInfo.CurrentCulture) + " s. Save to use them offline."
            : " Position coordinates are fetched for proteins with mapped peptides after Compute.";
        if (outcome.PanelFailure != null)
            message += (result.Asked > 0 ? " Protein evidence was saved in the document. " : " Stored protein evidence was kept. ")
                + "The reference panel is incomplete. Reason: " + outcome.PanelFailure.Message + " Retry Fetch evidence to complete the panel.";
        return message;
    }
}
