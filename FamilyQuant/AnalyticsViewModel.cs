using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant;

public enum ResultsExportKind { Summary, Peptides, Figures, EvidencePack, Homology, DatabaseGap, SuggestedAdditions }
public sealed record ResultsSummaryCard(string Title, string Value, string Detail);

/// <summary>Results derived from the held quantitation, with no RAW extraction or document mutation.</summary>
public sealed class AnalyticsViewModel : ScreenViewModel
{
    private readonly QuantManifest? manifest;
    private readonly FamilyClusterFile? curation;
    private int topN = FamilyContributionPlot.DefaultTopN;
    private bool percent;
    private string status = "";
    private ResultsExportKind exportKind;
    public AnalyticsData? Data { get; }
    public FamilyEnvelopeQuantResult? Families { get; }
    public DeNovoHomology? Homology { get; }
    public DatabaseGapViewModel DatabaseGap { get; }
    public event EventHandler? ResultsOptionsChanged;
    public bool HasHomology => Homology != null;
    public string HomologySummary => Homology == null ? "" :
        $"Latest homology snapshot: {Homology.MappedToTargets:N0} of {Homology.PeptidesSearched:N0} distinct " +
        (Homology.PeptidesSearched == 1 ? "peptide" : "peptides") + " reached targets. " +
        $"Source: {Path.GetFileName(Homology.FcfPath)}. This snapshot is independent of full XIC.";
    public bool HasData => Data != null;
    public string Details { get; } = "";
    private const string MissingQuantitation = "No full XIC is stored. Results reads FamilyQuant's own full isotope-envelope quantitation. " +
        "A plain .fcf carries no quantitation, and a .fqproj saved before full XIC was ever computed carries none either. " +
        "Load the project, open Quantify, compute full XIC, then return here to draw the graphs.";
    public string EmptyMessage => MissingQuantitation + (HasHomology ? " The standalone homology snapshot can still be exported here." : "");
    public IReadOnlyList<ResultsSummaryCard> Cards { get; } = Array.Empty<ResultsSummaryCard>();
    public int FamilyTopN { get => topN; set => Set(ref topN, value); }
    public bool FamilyAsPercent { get => percent; set => Set(ref percent, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public ResultsExportKind ExportKind { get => exportKind; set { Set(ref exportKind, value); Changed(nameof(CanExport)); } }
    public bool CanExport => ExportKind switch {
        ResultsExportKind.DatabaseGap => DatabaseGap.HasData,
        ResultsExportKind.SuggestedAdditions => DatabaseGap.CanExportFasta,
        ResultsExportKind.Homology => HasHomology,
        _ => HasData && (ExportKind != ResultsExportKind.EvidencePack || manifest != null && curation != null && Families != null) };

    public AnalyticsViewModel(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest,
        FamilyClusterFile? curation = null, PeptideFamilyAttribution? attribution = null, DeNovoHomology? homology = null)
    {
        this.manifest = manifest;
        this.curation = curation;
        Homology = homology;
        DatabaseGap = new DatabaseGapViewModel(manifest?.FamilyWorldResult, curation, manifest?.DatabaseGapMinimumTagLength, length =>
        {
            if (manifest == null) return;
            manifest.DatabaseGapMinimumTagLength = length == FamilyWorldOptions.DefaultMinimumTagLength ? null : length;
            ResultsOptionsChanged?.Invoke(this, EventArgs.Empty);
        });
        DatabaseGap.PropertyChanged += (_, _) => Changed(nameof(CanExport));
        if (rows == null || rows.Count == 0)
        {
            if (homology != null)
            {
                exportKind = ResultsExportKind.Homology;
                Cards = new[] { new ResultsSummaryCard("Homology snapshot", homology.Families.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    (homology.Families.Count == 1 ? " family" : " families"),
                    HomologySummary) };
            }
            if (DatabaseGap.HasStoredResult) exportKind = ResultsExportKind.DatabaseGap;
            return;
        }
        Data = AnalyticsBuilder.Build(rows, manifest?.PerRunTic ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
        if (manifest?.DerivedFractions is { Count: > 0 })
            NativeQuantFractions.ApplyDetectedToFractions(Data.Fractions, Data.Grouped, manifest.DerivedFractions);
        if (curation != null)
        {
            try { Families = attribution != null ? FamilyEnvelopeQuant.BuildFromAttribution(rows, attribution)
                : FamilyEnvelopeQuant.BuildFromCuration(rows, curation); }
            catch (Exception ex) { Status = "Family attribution is unavailable. " + ex.Message; }
        }
        Details = "The headline uses the primary reference-calibrated estimate. The directly observed six-window " +
            "diagnostic below uses a different basis. The representative table is counted before cross-side " +
            "suppression; recovery counts are after suppression.\n\n" + SummaryLine(Data, Families, curation, manifest);
        var grouped = Data.Grouped;
        Cards = new[] {
            new ResultsSummaryCard("Primary recovery estimate", grouped is { Tic: > 0 }
                ? grouped.RecoveredFractionReferenceCalibrated.ToString("P2", CultureInfo.CurrentCulture) : "Unavailable",
                "Identified observed envelope plus de novo reference-calibrated estimate, divided by the recorded RAW TIC."),
            new ResultsSummaryCard("Peptide representatives", Data.Peptides.Count.ToString("N0", CultureInfo.CurrentCulture),
                $"{Data.IdentifiedCount:N0} identified; {Data.DeNovoCount:N0} de novo. Collapsed table before cross-side suppression; one representative per run, side, peptidoform and charge."),
            new ResultsSummaryCard("Families reached", Families?.Families.Count.ToString("N0", CultureInfo.CurrentCulture) ?? "Unavailable",
                curation == null ? "No Family Cluster File is open beside this quantitation." : "Current curated families reached by homology. Shared and unattributed signal remain explicit.") };
    }

    public void ShowStatus(string message) => Status = message;
    public IReadOnlyList<AnalyticsFigure> BuildCompositionFigures(bool strict = false)
    {
        try { return StoredCompositionFigures.Build(manifest,
            Families?.Families.ToDictionary(f => f.FamilyUserId, f => f.Description), project: curation); }
        catch (Exception error) when (!strict && error is ArgumentException or InvalidOperationException)
        {
            Status = "Stored composition figures are unavailable. " + error.Message;
            return Array.Empty<AnalyticsFigure>();
        }
    }
    public string ExportUnavailableReason => ExportKind switch
    {
        ResultsExportKind.Homology => "Nothing was exported: no homology search result is stored, so there are no assignments to write. Open De novo and press Search first, then return to Results.",
        ResultsExportKind.SuggestedAdditions => DatabaseGap.FastaState,
        ResultsExportKind.DatabaseGap => DatabaseGap.EmptyMessage,
        _ => "The selected export needs its stored result and required project evidence."
    };
    private string ExportRefusal() => ExportUnavailableReason;
    public void Export(string path)
    {
        if (!CanExport) { Status = ExportRefusal(); return; }
        try
        {
            var kind = ExportKind;
            string detail = ExportDescription(kind);
            ExportWriter(description => detail = description)(path);
            Status = ExportSuccess(path, kind, detail);
        }
        catch (Exception ex) { Status = ExportFailure(ExportKind, ex); }
    }

    public async System.Threading.Tasks.Task ExportAsync(string path, AppOperationHost operations)
    {
        if (!CanExport) { Status = ExportRefusal(); return; }
        if (operations.IsBusy) return;
        using var operation = operations.Begin("Export results");
        var kind = ExportKind;
        string detail = ExportDescription(kind);
        var writer = ExportWriter(description => detail = description);
        bool folder = kind is ResultsExportKind.Figures or ResultsExportKind.EvidencePack;
        try
        {
            await AppOperationFiles.RunSta(() =>
            {
                if (folder) AppOperationFiles.WriteFolder(path, writer, operation.Context);
                else AppOperationFiles.WriteFile(path, writer, operation.Context);
            });
            Status = ExportSuccess(path, kind, detail);
        }
        catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
        { Status = "Cancelled. The previous export files were kept."; }
        catch (Exception ex) { Status = ExportFailure(kind, ex); }
    }

    private static string ExportDescription(ResultsExportKind kind) => kind switch {
        ResultsExportKind.Peptides => "the per-peptide analytics table",
        ResultsExportKind.Summary => "the per-run and grouped fraction summary",
        _ => "the selected result" };

    private static string ExportSuccess(string path, ResultsExportKind kind, string detail) =>
        "Written to " + path + ": " + detail + ". " +
        (kind is ResultsExportKind.Figures or ResultsExportKind.EvidencePack or ResultsExportKind.SuggestedAdditions ? ""
            : "Numbers use a dot for the decimal separator regardless of your machine's language. ") +
        "Charts and family choices are unchanged.";

    private static string ExportFailure(ResultsExportKind kind, Exception error) =>
        "The export did not finish. Reason given by the program: " + error.Message + (kind switch {
            ResultsExportKind.EvidencePack => " No partial claim is substituted. Load or recompute any named missing study, " +
                "or check write access to the folder, then try again.",
            ResultsExportKind.Figures => " Next step: check that you can write to that folder and that no PNG of the same name " +
                "is open in another program, then save again.",
            ResultsExportKind.SuggestedAdditions => " Next step: check that you can write to that folder, then export again.",
            _ => " Next step: check that the CSV is not open in Excel and that you can write to that folder, then export again." });

    private Action<string> ExportWriter(Action<string> describe)
    {
        int top = FamilyTopN;
        bool asPercent = FamilyAsPercent;
        return ExportKind switch
        {
            ResultsExportKind.Summary => path => File.WriteAllText(path, AnalyticsCsv.BuildSummaryCsv(Data!.Fractions, Data.Grouped)),
            ResultsExportKind.Peptides => path => File.WriteAllText(path, AnalyticsCsv.BuildPeptideCsv(Data!.Peptides)),
            ResultsExportKind.Figures => path => {
                var composition = BuildCompositionFigures(strict: true);
                var written = AnalyticsFigures.Save(Data!, Families, path, top, asPercent);
                foreach (var figure in composition)
                {
                    string figurePath = Path.Combine(path, figure.FileName);
                    File.WriteAllBytes(figurePath, FamilyCompositionFigures.RenderPng(figure));
                    written.Add(figurePath);
                }
                describe(written.Count.ToString("N0", CultureInfo.CurrentCulture) + " PNG figure(s): " +
                    string.Join(", ", written.Select(Path.GetFileName)) + ". Standard figures are 1920 by 1080 device pixels; " +
                    "composition figures expand vertically to retain every label. All use the Light projector palette");
            },
            ResultsExportKind.EvidencePack => path => {
                var written = PresentationEvidencePack.Save(Data!, manifest!, curation!, Families!, path);
                describe(written.FigurePaths.Count.ToString("N0", CultureInfo.CurrentCulture) + " FamilyQuant PNG plot(s) plus " +
                    written.DataPaths.Count.ToString("N0", CultureInfo.CurrentCulture) + " numeric/definition file(s). " +
                    "The PNGs are 1920 by 1080 device pixels in the Light palette; the CSV and JSON files contain the exact plotted numbers");
            },
            ResultsExportKind.Homology => path => HomologyCsv.Write(path, Homology!),
            ResultsExportKind.DatabaseGap => path => File.WriteAllText(path, DatabaseGap.BuildCsv()),
            ResultsExportKind.SuggestedAdditions => path => File.WriteAllText(path, DatabaseGap.BuildSuggestedFasta()),
            _ => throw new InvalidOperationException("Choose a supported export.")
        };
    }

        internal static string SummaryLine(AnalyticsData d, FamilyEnvelopeQuantResult? fam,
                                          FamilyClusterFile? curation, QuantManifest? manifest)
        {
            string familyClause = fam != null && fam.Families.Count > 0
                ? " The family chart stands on " +
                  fam.Families.Count.ToString("N0", CultureInfo.CurrentCulture) +
                  " family(ies) of the open grouping, reached by homology; families below the cut are " +
                  "folded into one bar and the signal that reached no family is drawn apart. Its bars count a " +
                  "peptide shared by several families in each of them; counted once instead, the peptides " +
                  "shared between families are " +
                  fam.SharedShare.ToString("P2", CultureInfo.CurrentCulture) + " of the quantified signal and " +
                  fam.UnattributedShare.ToString("P2", CultureInfo.CurrentCulture) + " reached no family (the " +
                  "evidence pack's family_composition.csv uses this basis; the curation screen's Full XIC column " +
                  "can show it too, with 'Family-unique only' and 'the quantified signal' chosen in the quantitation panel)."
                : curation == null
                    ? " No Family Cluster File is open beside this quantitation, so the family chart has " +
                      "nothing to draw: open the project's .fcf or .fqproj and return here."
                    : " No family of the open grouping was reached by any peptide, so the family chart is " +
                      "empty. Check that the curation and the quantitation come from the same database.";

            string identificationClause = IdentificationCountingClause(d, manifest);
            string sourceClause = DeNovoSourceClause(manifest);

            if (d.Grouped == null || !(d.Grouped.Tic > 0))
            {
                return "Read " + d.Peptides.Count.ToString("N0", CultureInfo.CurrentCulture) +
                       " peptide(s) after collapsing each to one representative peak. No total ion current " +
                       "is recorded, so the recovery shares cannot be taken; the mass and isotope graphs " +
                       "still read from the table." + sourceClause + identificationClause + familyClause;
            }

            NativeRunFraction g = d.Grouped;

            // The identified-but-not-quantified block is named even when it is zero. "Nothing was
            // excluded" and "exclusions were not reported" are different facts, and a reader has to be
            // able to tell them apart from the line alone.
            string underClause = string.Format(CultureInfo.CurrentCulture,
                " A further {0:P2} was identified but NOT quantified: {1:N0} peptide(s) whose peaks carried " +
                "too few points to integrate{2}. It is accounted for as its own block rather than left to " +
                "the remainder.",
                g.UnderSampledFraction,
                g.IdentifiedUnderSampledPeptides + g.DeNovoUnderSampledPeptides,
                g.UnderSampledIsEstimate
                    ? ", and part of that number is an ESTIMATE (a single-point peak has no width, so it is " +
                      "given the run's median scan spacing)"
                    : string.Empty);

            return string.Format(CultureInfo.CurrentCulture,
                "Across {0} run(s): the identified (DDA) envelope explains {1:P2} of the total ion current, " +
                "the de novo envelope adds {2:P2}, and together they recover {3:P2}. " +
                "{4:N0} identified and {5:N0} de novo peptide(s), each counted once. Every number is " +
                "FamilyQuant's own full-envelope quant, deduped to one deterministic largest-M+0 " +
                "representative per run, evidence side, canonical PTM-aware peptidoform and charge, divided " +
                "by the raw TIC; nothing here is read from the .plp or the .xic2.",
                d.Fractions.Count,
                g.IdentifiedFractionEnvelope,
                g.DeNovoFractionEnvelope,
                g.RecoveredFractionEnvelope,
                g.IdentifiedPeptides,
                g.DeNovoPeptides) + sourceClause + identificationClause + underClause + familyClause;
        }

        private static string DeNovoSourceClause(QuantManifest? manifest)
        {
            if (manifest?.DeNovoInputs == null || manifest.DeNovoInputs.Count == 0) return string.Empty;

            string sources = string.Join(" + ", manifest.DeNovoInputs
                .GroupBy(i => i.SourceId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Min(i => i.SourcePriority))
                .Select(g => g.First().SourceDisplayName + " [" + g.First().CutoffDescription + "]"));
            return " De novo source provenance: " + sources + ". " +
                manifest.DeNovoInputs.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " input file(s) are fingerprinted in the project manifest" +
                (manifest.DeNovoUnionResolutionMethod.Length > 0
                    ? "; competing source calls share one XIC and retain all alternate evidence"
                    : string.Empty) + ".";
        }

        private static string IdentificationCountingClause(AnalyticsData d, QuantManifest? manifest)
        {
            DeNovoIdentificationSummary? s = manifest?.DeNovoIdentification;
            if (s == null) return string.Empty;

            return string.Format(CultureInfo.CurrentCulture,
                " Identification census (no XIC): {0:N0} accepted de novo spectrum calls become {1:N0} " +
                "distinct PTM-cleaned sequences carried forward after search reconciliation, pooled across " +
                "runs and charges. Quantification is a separate grain: {2:N0} post-XIC de novo run × sequence " +
                "× charge representatives; this is not a unique-peptide count.",
                s.FilteredSpectrumCalls,
                s.CarriedForwardDistinctSequencesIgnoringPtm,
                d.DeNovoCount);
        }

}
