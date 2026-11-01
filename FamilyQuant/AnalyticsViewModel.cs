using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant;

public enum ResultsExportKind { Summary, Peptides, Figures, EvidencePack, Homology }
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
    public bool HasHomology => Homology != null;
    public string HomologySummary => Homology == null ? "" :
        $"Latest homology snapshot: {Homology.MappedToTargets:N0} of {Homology.PeptidesSearched:N0} distinct " +
        (Homology.PeptidesSearched == 1 ? "peptide" : "peptides") + " reached targets. " +
        $"Source: {Path.GetFileName(Homology.FcfPath)}. This snapshot is independent of full XIC.";
    public bool HasData => Data != null;
    public string Details { get; } = "";
    public string EmptyMessage => HasHomology ? "No full XIC is stored. The standalone homology snapshot can be exported here."
        : "No full XIC is stored. Open Quantify, compute the full XIC, and return to Results.";
    public IReadOnlyList<ResultsSummaryCard> Cards { get; } = Array.Empty<ResultsSummaryCard>();
    public int FamilyTopN { get => topN; set => Set(ref topN, value); }
    public bool FamilyAsPercent { get => percent; set => Set(ref percent, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public ResultsExportKind ExportKind { get => exportKind; set { Set(ref exportKind, value); Changed(nameof(CanExport)); } }
    public bool CanExport => ExportKind == ResultsExportKind.Homology ? HasHomology :
        HasData && (ExportKind != ResultsExportKind.EvidencePack || manifest != null && curation != null && Families != null);

    public AnalyticsViewModel(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest,
        FamilyClusterFile? curation = null, PeptideFamilyAttribution? attribution = null, DeNovoHomology? homology = null)
    {
        this.manifest = manifest;
        this.curation = curation;
        Homology = homology;
        if (rows == null || rows.Count == 0)
        {
            if (homology != null)
            {
                exportKind = ResultsExportKind.Homology;
                Cards = new[] { new ResultsSummaryCard("Homology snapshot", homology.Families.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    (homology.Families.Count == 1 ? " family" : " families"),
                    HomologySummary) };
            }
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
    public void Export(string path)
    {
        if (!CanExport) { Status = "The selected export needs its stored result and required project evidence."; return; }
        try
        {
            switch (ExportKind)
            {
                case ResultsExportKind.Summary: File.WriteAllText(path, AnalyticsCsv.BuildSummaryCsv(Data!.Fractions, Data.Grouped)); break;
                case ResultsExportKind.Peptides: File.WriteAllText(path, AnalyticsCsv.BuildPeptideCsv(Data!.Peptides)); break;
                case ResultsExportKind.Figures: AnalyticsFigures.Save(Data!, Families, path, FamilyTopN, FamilyAsPercent); break;
                case ResultsExportKind.EvidencePack: PresentationEvidencePack.Save(Data!, manifest!, curation!, Families!, path); break;
                case ResultsExportKind.Homology: HomologyCsv.Write(path, Homology!); break;
            }
            Status = "Written to " + path + ".";
        }
        catch (Exception ex) { Status = "The export did not finish. " + ex.Message; }
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
