using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class GroupingReviewViewModel
{
    private readonly Func<string, string, CancellationToken, Task<EvidenceScopePreview>>? previewScope;
    private CancellationTokenSource? scopePreviewCancellation;
    private int scopePreviewGeneration;
    private string scopePreviewBaseline = "";
    private bool scopePreviewBaselineCaptured, suppressScopePreview;
    private EvidenceScopePreview? scopeChangePreview;
    private bool scopePreviewBusy;
    private string scopePreviewError = "";
    private IReadOnlyList<string> requestedScopeTerms = Array.Empty<string>();

    public Task ScopePreviewCompletion { get; private set; } = Task.CompletedTask;
    public EvidenceScopePreview? ScopeChangePreview => scopeChangePreview;
    public bool IsScopePreviewBusy => scopePreviewBusy;
    public bool HasScopeChangePreview => scopePreviewBusy || scopePreviewError.Length > 0 || scopeChangePreview?.AddedTerms.Count > 0;
    public IReadOnlyList<ScopePreviewFamily> ScopePreviewFamilies => scopeChangePreview?.Families ?? Array.Empty<ScopePreviewFamily>();
    public string ScopePreviewTerms => "Added words " + string.Join(", ", requestedScopeTerms.Select(term => "\"" + term + "\""));
    public string ScopePreviewSummary
    {
        get
        {
            if (scopePreviewBusy) return "Reading stored evidence...";
            if (scopePreviewError.Length > 0) return scopePreviewError;
            if (scopeChangePreview == null) return "";
            return string.Format(CultureInfo.CurrentCulture,
                "Matching entries {0:N0} | Reviewed {1:N0} | Unreviewed {2:N0} | Status unavailable {3:N0} | New matches beyond previous scope {4:N0}",
                scopeChangePreview.Proteins, scopeChangePreview.Reviewed, scopeChangePreview.Unreviewed, scopeChangePreview.UnknownStatus, scopeChangePreview.Additional);
        }
    }
    public string ScopePreviewRelativeNote
    {
        get
        {
            var options = new EvidenceClassificationOptions();
            return "Unreviewed relatives carrying none of these words may still join through an in-scope reviewed relative at "
                + options.UnannotatedIdentityPercent.ToString("0.##", CultureInfo.CurrentCulture) + "% identity over at least "
                + options.MinQueryAlignedPercent.ToString("0.##", CultureInfo.CurrentCulture)
                + "% of the protein's length with overlapping, non-conflicting observed domains.";
        }
    }
    public string ScopePreviewContaminantNote
    {
        get
        {
            if (scopeChangePreview == null) return "";
            int count = scopeChangePreview.ExcludedContaminants;
            if (count == 0) return "Contaminants are excluded because a contaminant never joins a family.";
            return count == 1 ? "1 contaminant carrying these words is excluded. A contaminant never joins a family."
                : count.ToString("N0", CultureInfo.CurrentCulture) + " contaminants carrying these words are excluded. A contaminant never joins a family.";
        }
    }
    public string ScopePreviewEmptyText => !scopePreviewBusy && scopePreviewError.Length == 0 && scopeChangePreview is { Proteins: 0 }
        ? "No stored keyword or tissue annotations match these added words." : "";

    private void RequestScopePreview()
    {
        if (previewScope == null || suppressScopePreview) return;
        CancelScopePreview();
        var baseline = WordCluster.ParseKeywords(scopePreviewBaseline).ToHashSet(StringComparer.OrdinalIgnoreCase);
        requestedScopeTerms = WordCluster.ParseKeywords(ScopeText).Where(term => !baseline.Contains(term)).ToArray();
        if (requestedScopeTerms.Count == 0) { NotifyScopePreview(); return; }
        TaskScopeExpanded = false;
        var cancellation = new CancellationTokenSource();
        scopePreviewCancellation = cancellation;
        int request = scopePreviewGeneration;
        scopePreviewBusy = true;
        NotifyScopePreview();
        ScopePreviewCompletion = RefreshScopePreviewAsync(request, scopePreviewBaseline, ScopeText, cancellation);
    }

    private async Task RefreshScopePreviewAsync(int request, string baseline, string edited, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(200, cancellation.Token);
            var result = await previewScope!(baseline, edited, cancellation.Token);
            if (request == scopePreviewGeneration && !cancellation.IsCancellationRequested) scopeChangePreview = result;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request == scopePreviewGeneration) scopePreviewError = "Scope preview could not finish: " + error.Message;
        }
        finally
        {
            if (ReferenceEquals(scopePreviewCancellation, cancellation)) scopePreviewCancellation = null;
            cancellation.Dispose();
            if (request == scopePreviewGeneration) { scopePreviewBusy = false; NotifyScopePreview(); }
        }
    }

    private void SetScopePreviewBaseline(string value)
    {
        CancelScopePreview();
        scopePreviewBaseline = value;
        scopePreviewBaselineCaptured = true;
    }

    private void CancelScopePreview()
    {
        scopePreviewGeneration++;
        var cancellation = scopePreviewCancellation;
        scopePreviewCancellation = null;
        cancellation?.Cancel();
        scopeChangePreview = null;
        scopePreviewBusy = false;
        scopePreviewError = "";
        requestedScopeTerms = Array.Empty<string>();
        NotifyScopePreview();
    }

    private void NotifyScopePreview()
    {
        Changed(nameof(ScopeChangePreview)); Changed(nameof(IsScopePreviewBusy)); Changed(nameof(HasScopeChangePreview));
        Changed(nameof(ScopePreviewFamilies)); Changed(nameof(ScopePreviewTerms)); Changed(nameof(ScopePreviewSummary)); Changed(nameof(ScopePreviewEmptyText));
        Changed(nameof(ScopePreviewContaminantNote));
    }
}
