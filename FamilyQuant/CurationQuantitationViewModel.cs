using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FamilyQuant.Operations;

namespace FamilyQuant;

// Presentation state over the unchanged production quantitation and CSV services.
public sealed partial class CurationQuantitationViewModel : ScreenViewModel
{
    public FamilyClusterFile Document { get; }
    private string summary = "No full XIC is attached to these families.";
    private int basisIndex;
    public SharedPeptidePolicy SharedPolicy { get; set; } = SharedPeptidePolicy.FamilyUnique;
    public int BasisIndex { get => basisIndex; set { basisIndex = value; Changed(); } }
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public bool CanUseTic => fullXicTic > 0;
    public bool IsPending => fullXicPending;
    public string TicHelp => CanUseTic ? "Divide Full XIC by the summed TIC of every recorded run, updating displayed shares immediately. Measured areas and files stay unchanged; choose the previous basis to restore it, since grouping Undo does not reset it."
        : "The TIC basis is unavailable: " + (fullXicTicRefusal ?? "no full XIC has been computed yet")
            + ". Recompute with a complete, consistent positive TIC for every quantified run to enable it; the quantified-signal basis stays available.";
    public event EventHandler? Refreshed;
    public CurationQuantitationViewModel(FamilyClusterFile document) => Document = document;
    private void NotifyState()
    {
        Changed(nameof(HasFullXic)); Changed(nameof(IsPending)); Changed(nameof(CanUseTic)); Changed(nameof(TicHelp));
        Changed(nameof(BasisIndex)); Changed(nameof(FullXicFamilies));
        NotifyPresentationState();
    }
    private void PublishChanged() { NotifyState(); Refreshed?.Invoke(this, EventArgs.Empty); }

    private List<DeNovo.NativePeptideQuant>? fullXicPeptides;
    private CurationHomologyHits? fullXicHits;
    private FamilyEnvelopeQuantResult? fullXicResult;
    private PeptideFamilyAttribution? fullXicAttribution;
    private double fullXicTic;
    private int fullXicRuns;
    private string? fullXicTicRefusal;
    private string? fullXicProteinFingerprint;
    // Kept per view model so focused tests can use a private cache without changing the program default.
    private string? fullXicCacheDirectory = null;
    private Homology.HomologyOptions fullXicMappingOptions = new();
    public void SetFullXicMappingProvenance(FamilyMappingProvenance? mapping) =>
        fullXicMappingOptions = Homology.HomologyDefaults.ForStoredMapping(mapping);
    private int fullXicGeneration;
    private bool fullXicPending;
    // Each item's User ID AND annotation: the annotation names its family (FamilyNaming), so an
    // annotation-only edit has to refresh the family names the export and the spectrum table read.
    private Dictionary<FastaItemExtended, (int UserID, string Annotation)>? fullXicGrouping;
    private SharedPeptidePolicy fullXicPolicyShown;
    private bool fullXicOverTicShown;

    /// <summary>The primary estimate of SCIENTIFIC_METHOD.md, named wherever the column is described.</summary>
    private const string FullXicQuantity =
        "identified observed envelope plus the de novo measured envelope";

    /// <summary>The protein-set fingerprint of the attached quantitation's curation, or null.</summary>
    public string? FullXicProteinFingerprint => HasFullXic ? fullXicProteinFingerprint : null;

    /// <summary>True when a full-XIC quantitation is attached to this screen.</summary>
    public bool HasFullXic => fullXicPeptides != null && fullXicPeptides.Count > 0 && fullXicHits != null;

    /// <summary>The family table behind the Full XIC column as last computed, or null.</summary>
    public FamilyEnvelopeQuantResult? FullXicFamilies => fullXicResult;

    /// <summary>The peptide-to-family attribution on the CURRENT grouping, or null without a full XIC.
    /// Cheap: the homology hits are cached; only the join onto the User IDs is redone.</summary>
    public PeptideFamilyAttribution? CurrentFullXicAttribution()
    {
        RefreshFullXicShares();
        return HasFullXic ? fullXicAttribution : null;
    }

    private sealed class FullXicPrepared
    {
        public List<DeNovo.NativePeptideQuant> Peptides = new List<DeNovo.NativePeptideQuant>();
        public CurationHomologyHits Hits = new CurationHomologyHits();
        public double Tic;
        public int Runs;
        public string? TicRefusal;
        public string ProteinFingerprint = string.Empty;
    }

    /// <summary>
    /// The expensive part, safe off the interface thread: it reads only the rows, the TIC map and a
    /// snapshot of the protein list (identifiers and sequences, which curation never changes).
    ///
    /// The TIC basis is every run the computation recorded a TIC for (a run can be processed and
    /// keep no row, and it still belongs in "the TIC of all runs"), keyed by the normalized run name
    /// on both sides (invariant 1). It is refused, never silently narrowed, when a quantified run has
    /// no positive TIC or one run is recorded twice with different values.
    /// </summary>
    private static FullXicPrepared PrepareFullXic(IReadOnlyList<QuantRow> rows,
        IReadOnlyDictionary<string, double>? perRunTic, FamilyClusterFile proteins)
        => PrepareFullXic(rows, perRunTic, proteins, null, null, null);

    private static FullXicPrepared PrepareFullXic(IReadOnlyList<QuantRow> rows,
        IReadOnlyDictionary<string, double>? perRunTic, FamilyClusterFile proteins, OperationContext? operation,
        string? cacheDirectory) => PrepareFullXic(rows, perRunTic, proteins, operation, cacheDirectory, null);

    private static FullXicPrepared PrepareFullXic(IReadOnlyList<QuantRow> rows,
        IReadOnlyDictionary<string, double>? perRunTic, FamilyClusterFile proteins, OperationContext? operation,
        string? cacheDirectory, Homology.HomologyOptions? mappingOptions)
    {
        var prepared = new FullXicPrepared
        {
            Peptides = FamilyEnvelopeQuant.PreparePeptides(rows),
            Hits = CurationHomologyCache.GetOrSearch(rows, proteins, cacheDirectory, mappingOptions,
                cancellationToken: operation?.CancellationToken ?? default, operation: operation),
            // Once per quantitation, here off the interface thread: protein content never changes
            // during curation, so a regrouping does not invalidate it.
            ProteinFingerprint = FamilyEnvelopeQuant.ProteinSetFingerprint(proteins),
        };

        var tic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (perRunTic != null)
        {
            foreach (KeyValuePair<string, double> kv in perRunTic)
            {
                string run = DeNovo.DeNovoReconciler.NormalizeRunName(kv.Key ?? string.Empty);
                if (run.Length == 0) continue;
                if (tic.TryGetValue(run, out double seen) && seen != kv.Value)
                    prepared.TicRefusal = "the TIC of run " + run + " is recorded twice with different values";
                tic[run] = kv.Value;
            }
        }

        List<string> quantRuns = rows.Select(r => DeNovo.DeNovoReconciler.NormalizeRunName(r.Run ?? string.Empty))
            .Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        List<string> missing = quantRuns
            .Where(r => !tic.TryGetValue(r, out double v) || !(v > 0) || double.IsInfinity(v)).ToList();

        if (prepared.TicRefusal == null && tic.Count == 0)
            prepared.TicRefusal = "the project records no total ion current";
        else if (prepared.TicRefusal == null && missing.Count > 0)
            prepared.TicRefusal = "no positive TIC is recorded for run(s) " + string.Join(", ", missing.Take(3)) +
                (missing.Count > 3 ? " and " + (missing.Count - 3).ToString(CultureInfo.InvariantCulture) + " more" : string.Empty);
        else if (prepared.TicRefusal == null && tic.Values.Any(v => !(v > 0) || double.IsInfinity(v)))
            prepared.TicRefusal = "a recorded TIC is not a positive number";

        prepared.Runs = tic.Count;
        prepared.Tic = prepared.TicRefusal == null ? tic.Values.Sum() : 0;
        return prepared;
    }

    /// <summary>
    /// Attaches the project's full-XIC quant (or detaches it, with null or an empty list) and fills the
    /// Full XIC share column, synchronously. The application uses <see cref="SetFullXicQuantAsync"/>;
    /// this form is for a caller that needs the column filled when it returns (the test harness).
    /// </summary>
    public void SetFullXicQuant(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic)
    {
        int generation = ++fullXicGeneration;
        fullXicDone?.TrySetResult(false);
        fullXicDone = null;
        ClearFullXic();

        FullXicPrepared? prepared = null;
        string? error = null;
        if (Document != null && rows != null && rows.Count > 0)
        {
            try
            {
                prepared = PrepareFullXic(rows.ToList(), perRunTic, ProteinSnapshot(), null, fullXicCacheDirectory, fullXicMappingOptions.Clone());
            }
            catch (Exception ex) { error = ex.Message; }
        }

        ApplyFullXic(prepared, error, generation);
    }

    /// <summary>
    /// The same, with the homology search off the interface thread: the screen stays usable (Save
    /// included) while it runs, the summary line says it is running, and a result that arrives after
    /// a newer quantitation was attached is discarded.
    /// </summary>
    public async Task SetFullXicQuantAsync(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic)
    {
        int generation = ++fullXicGeneration;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fullXicDone?.TrySetResult(false);
        fullXicDone = done;
        try
        {
            await AttachAsync(rows, perRunTic, generation);
        }
        finally
        {
            done.TrySetResult(true);
        }
    }

    // Completed when the latest background attach has been applied (or replaced): what a caller that
    // needs the attribution awaits, instead of starting a second homology search of its own.
    private TaskCompletionSource<bool>? fullXicDone;

    /// <summary>Completes when no background attach is pending any more.</summary>
    public Task FullXicReady => fullXicDone?.Task ?? Task.CompletedTask;

    private async Task AttachAsync(IReadOnlyList<QuantRow>? rows, IReadOnlyDictionary<string, double>? perRunTic,
                                   int generation)
    {
        ClearFullXic();

        if (Document == null || rows == null || rows.Count == 0)
        {
            ApplyFullXic(null, null, generation);
            return;
        }

        fullXicPending = true;
        NotifyState();
                        Summary = "Attributing the full-XIC quantitation to the families (a homology " +
                "search, which can take a minute on a large database). The Full XIC column fills in when it " +
                "finishes; you can keep working, and saving now saves the quantitation.";
        PublishChanged();

        List<QuantRow> rowCopy = rows.ToList();
        Dictionary<string, double>? ticCopy = perRunTic?.ToDictionary(kv => kv.Key, kv => kv.Value);
        FamilyClusterFile proteins = ProteinSnapshot();
        var mappingOptions = fullXicMappingOptions.Clone();

        FullXicPrepared? prepared = null;
        string? error = null;
        try { prepared = await Task.Run(() => PrepareFullXic(rowCopy, ticCopy, proteins, null, fullXicCacheDirectory, mappingOptions)); }
        catch (Exception ex) { error = ex.Message; }

        if (generation != fullXicGeneration) return;   // a newer quantitation was attached meanwhile
        ApplyFullXic(prepared, error, generation);
    }

    /// <summary>The proteins as they stand, in a list the interface thread will not reassign under a
    /// background search (UpdateGUI replaces the list when it re-sorts).</summary>
    private FamilyClusterFile ProteinSnapshot() =>
        new FamilyClusterFile { MyExtendedFastaItems = (Document?.MyExtendedFastaItems ?? new List<FastaItemExtended>()).ToList() };

    private void ClearFullXic()
    {
        fullXicPeptides = null;
        fullXicHits = null;
        fullXicResult = null;
        fullXicAttribution = null;
        fullXicGrouping = null;
        fullXicTic = 0;
        fullXicRuns = 0;
        fullXicTicRefusal = null;
        fullXicProteinFingerprint = null;
        fullXicPending = false;
        fullXicError = null;
    }

    private void ApplyFullXic(FullXicPrepared? prepared, string? error, int generation)
    {
        if (generation != fullXicGeneration) return;

        fullXicPending = false;
        fullXicError = error;
        if (prepared != null)
        {
            fullXicPeptides = prepared.Peptides;
            fullXicHits = prepared.Hits;
            fullXicTic = prepared.Tic;
            fullXicRuns = prepared.Runs;
            fullXicTicRefusal = prepared.TicRefusal;
            fullXicProteinFingerprint = prepared.ProteinFingerprint;
        }

        if (fullXicTic <= 0) basisIndex = 0;

        if (error != null)
            Summary = "The full-XIC quantitation could not be attributed to the families, so " +
                "the Full XIC column stays empty. Reason given by the program: " + error;

        PublishChanged();
        NotifyState();
    }

    public bool FullXicOverTic => BasisIndex == 1 && fullXicTic > 0;

    public string FullXicBasisLabel => FullXicOverTic
        ? "the total ion current of " + fullXicRuns.ToString(CultureInfo.InvariantCulture) + " run(s)"
        : "the quantified signal (every quantified peptide counted once)";

    /// <summary>The CSV heading of the Full XIC column: the quantity, what it is a fraction of, and the rule.</summary>
    public string FullXicCsvHeader() => !HasFullXic
        ? CsvReport.NoFullXicHeader
        : "Full XIC family share (" + FullXicQuantity + "), fraction 0-1 of " + FullXicBasisLabel + ", " +
          SharedPolicy.ShortName() + " (User families)";

    /// <summary>The exact grouping (and annotations) the column was last computed for, still the current one?</summary>
    private bool GroupingUnchanged()
    {
        if (Document == null || fullXicGrouping == null || fullXicGrouping.Count != Document.MyExtendedFastaItems.Count) return false;
        foreach (FastaItemExtended item in Document.MyExtendedFastaItems)
            if (!fullXicGrouping.TryGetValue(item, out var seen) || seen.UserID != item.UserID
                || !string.Equals(seen.Annotation, item.UserAnnotation ?? string.Empty, StringComparison.Ordinal))
                return false;
        return true;
    }

    /// <summary>
    /// Recomputes the Full XIC column when the grouping, the shared-peptide rule or the basis changed
    /// since the last time (or when <paramref name="force"/>), and writes the summary line.
    /// </summary>
    public void RefreshFullXicShares(bool force = false)
    {
        if (Document == null) return;


        if (!HasFullXic)
        {
            foreach (FastaItemExtended item in Document.MyExtendedFastaItems) item.FullXicShare = null;
            fullXicResult = null;
            fullXicAttribution = null;
            fullXicGrouping = null;
            if (!fullXicPending && !HasFullXicError)
                Summary = "No full XIC is attached. Open Quantify to compute it for the current families.";
            return;
        }

        SharedPeptidePolicy policy = SharedPolicy;
        bool overTic = FullXicOverTic;
        if (!force && fullXicResult != null && policy == fullXicPolicyShown && overTic == fullXicOverTicShown
            && GroupingUnchanged()) return;

        PeptideFamilyAttribution attribution = FamilyEnvelopeQuant.AttributeHits(fullXicHits!, Document);
        IReadOnlyList<int> none = Array.Empty<int>();
        fullXicResult = FamilyEnvelopeQuant.BuildFromPeptides(fullXicPeptides!,
            s => attribution.IdentifiedBySequence.TryGetValue(s, out IReadOnlyList<int>? v) ? v : none,
            s => attribution.DeNovoBySequence.TryGetValue(s, out IReadOnlyList<int>? v) ? v : none,
            id => attribution.FamilyDescriptions.TryGetValue(id, out string? d) ? d : string.Empty);
        fullXicResult.IdentityThreshold = attribution.IdentityThreshold;
        fullXicAttribution = attribution;
        fullXicPolicyShown = policy;
        fullXicOverTicShown = overTic;
        fullXicGrouping = new Dictionary<FastaItemExtended, (int, string)>(ReferenceEqualityComparer.Instance);
        foreach (FastaItemExtended item in Document.MyExtendedFastaItems)
            fullXicGrouping[item] = (item.UserID, item.UserAnnotation ?? string.Empty);

        double denominator = overTic ? fullXicTic : fullXicResult.MeasuredTotalEnvelopeReferenceCalibrated;
        Dictionary<int, double> byFamily = fullXicResult.Families.ToDictionary(f => f.FamilyUserId, f => f.Signal(policy));
        foreach (FastaItemExtended item in Document.MyExtendedFastaItems)
            item.FullXicShare = denominator > 0
                ? (byFamily.TryGetValue(item.UserID, out double s) ? s / denominator : 0)
                : (double?)null;


        string P(double v) => denominator > 0 ? (v / denominator).ToString("P2", CultureInfo.CurrentCulture) : "n/a";
        double families = fullXicResult.Families.Sum(f => f.Signal(policy));
        string text = "Full XIC column (" + FullXicQuantity + "), as a share of " + FullXicBasisLabel +
            ": families " + P(families);
        if (policy == SharedPeptidePolicy.FamilyUnique)
            text += ", peptides shared between families " + P(fullXicResult.SharedTotalEnvelopeReferenceCalibrated);
        text += ", peptides attributed to no family " + P(fullXicResult.UnattributedTotalEnvelopeReferenceCalibrated) + ".";
        if (policy == SharedPeptidePolicy.CountInEveryFamily)
            text += " Shared peptides are counted in every family they reach, so the families overlap and " +
                    "must not be added up.";
        else if (!overTic)
            text += " These add up to 100%.";
        else
            text += " Together they are the share of the TIC the quantified peptides explain.";
        text += " Peptides are attributed to families by homology at " +
                fullXicResult.IdentityThreshold.ToString("P0", CultureInfo.CurrentCulture) + " identity.";
        if (fullXicTicRefusal != null)
            text += " The TIC basis is unavailable: " + fullXicTicRefusal + ".";
        Summary = text;
    }

    /// <summary>The family composition the Full XIC column stands on, as CSV, or null without a full XIC.
    /// Public so the export is testable without a file dialog.</summary>
    public string? FullXicCompositionCsv()
    {
        RefreshFullXicShares();
        if (!HasFullXic || fullXicResult == null) return null;
        double denominator = FullXicOverTic ? fullXicTic : fullXicResult.MeasuredTotalEnvelopeReferenceCalibrated;
        return CsvReport.BuildFamilyComposition(fullXicResult, SharedPolicy, denominator,
            "fraction of " + FullXicBasisLabel + "; signal = " + FullXicQuantity);
    }

}
