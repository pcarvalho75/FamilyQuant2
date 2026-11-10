using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>Chart projections of the recorded native quality studies. No source calls are re-read.</summary>
public static class QualityChartData
{
    public sealed record Row(string Label, double? Value, string Text, string Unit, string Base);
    public sealed record Point(double X, double Y, string Category, string XText, string YText);
    public sealed record Plot(string Id, string Title, string Legend, string Unit, IReadOnlyList<Row> Rows)
    {
        public IReadOnlyList<Point> Points { get; init; } = Array.Empty<Point>();
        public string XLabel { get; init; } = "";
        public string YLabel { get; init; } = "";
        public IReadOnlyList<DeNovoSourceScoreDistribution> Scores { get; init; } = Array.Empty<DeNovoSourceScoreDistribution>();
    }
    public static int ImportedCalls(QuantManifest manifest) => manifest.DeNovoInputs.Count > 0
        ? manifest.DeNovoInputs.Sum(i => i.RawRows) : manifest.DeNovoScoreDistribution?.RawRows ?? 0;
    public static int CreditedFeatures(AnalyticsData data) => data.Grouped?.DeNovoPeptides
        ?? data.Fractions.Where(f => !f.IsGrouped).Sum(f => f.DeNovoPeptides);
    public static IReadOnlyList<(string Run, int Count)> CreditedByRun(AnalyticsData data) => data.Peptides
        .Select(p => p.Run).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
        .Select(run => (run, data.Fractions.FirstOrDefault(f => !f.IsGrouped
            && string.Equals(f.Run, run, StringComparison.OrdinalIgnoreCase))?.DeNovoPeptides ?? 0)).ToArray();
    private static Row Count(string label, double value, string unit, string basis = "Count; no percentage denominator") =>
        new(label, value, value.ToString("N0", CultureInfo.InvariantCulture), unit, basis);
    private static Row Rate(string label, double? value, string basis) => new(label, value.HasValue ? 100 * value : null,
        value.HasValue ? (100 * value.Value).ToString("0.####", CultureInfo.InvariantCulture) + "%" : "Not available", "%", basis);

    public static IReadOnlyList<FamilyWorldFamilySummary[]> WorldPages(QuantManifest manifest, FamilyReportThresholdResult? grouping)
    {
        var world = manifest.FamilyWorldResult?.ForLength(manifest.DatabaseGapMinimumTagLength ?? FamilyWorldOptions.DefaultMinimumTagLength);
        if (world == null) return Array.Empty<FamilyWorldFamilySummary[]>();
        var hidden = ReportWordMembership.HiddenWorldFamilies(world, grouping);
        return world.Families.Where(row => !hidden.Contains(row.Family)).OrderBy(row => row.Family, StringComparer.Ordinal)
            .Chunk(12).ToArray();
    }

    public static IReadOnlyList<Plot> Build(AnalyticsData data, QuantManifest manifest, FamilyReportThresholdResult? grouping = null)
    {
        var plots = new List<Plot>();
        var scores = manifest.DeNovoSourceScoreDistributions.Count > 0 ? manifest.DeNovoSourceScoreDistributions
            : manifest.DeNovoScoreDistribution != null ? new() { DeNovoSourceScoreDistributionStudy.FromLegacy(manifest.DeNovoScoreDistribution) }
            : new List<DeNovoSourceScoreDistribution>();
        if (scores.Count > 0)
        {
            foreach (var score in scores) score.Validate();
            plots.Add(new("de_novo_score_distribution.png", "Source confidence and retained calls",
                "Counts pool source calls across runs before peptide feature collapse. Each source keeps its own confidence scale and cutoff. Narrow bars passed every stored score, length and local-stretch filter; these are not accuracy probabilities.", "source calls", Array.Empty<Row>()) { Scores = scores });
        }
        if (manifest.DeNovoIdentification is { } identification)
        {
            identification.Validate();
            plots.Add(new("de_novo_consolidation.png", "From imported calls to credited peptide features",
                "These stages have different counting units. They are not a common-denominator funnel: pooled sequences merge runs and charges, while peptide features keep them separate. No percentage or uncertainty interval is implied.", "counts", new[]
                {
                    Count("Imported", ImportedCalls(manifest), "source calls"),
                    Count("After source filters and precursor union", identification.FilteredSpectrumCalls, "calls"),
                    Count("Distinct sequences, PTMs removed; I/L distinct", identification.FilteredDistinctSequencesIgnoringPtm, "pooled sequences"),
                    Count("Credited after search-overlap suppression", CreditedFeatures(data), "peptide features"),
                }));
            var rows = new List<Row>
            {
                Count("Accepted calls", identification.FilteredSpectrumCalls, "calls"),
                Count("Additional after search reconciliation", identification.CarriedForwardSpectrumCalls, "calls"),
                Count("Distinct additional sequences", identification.CarriedForwardDistinctSequencesIgnoringPtm, "pooled sequences"),
                Count("Credited de novo", CreditedFeatures(data), "peptide features"),
            };
            rows.AddRange(CreditedByRun(data).Select(run => Count(run.Run, run.Count, "credited de novo peptide features")));
            plots.Add(new("peptide_representatives.png", "Calls, sequences and peptide features", "Identification counts do not require XIC signal. Credited peptide features have measured signal, are counted once per run and charge, and exclude identified-equivalent de novo calls. Run counts are separate observations, not a mean.", "counts", rows));
        }
        if (manifest.IdentificationConcordance is { } concordance)
        {
            concordance.Validate();
            if (concordance.PrecursorQualifiedDatabaseSearchPairs > 0)
                plots.Add(new("database_search_concordance.png", "Database-search sequence agreement",
                    "The agreement denominator is the recorded one-to-one precursor-qualified PSM pairs. PTMs are removed; I/L remain distinct. Shared scans are an audit count, not the agreement denominator or an accuracy estimate.", "counts", new[]
                    {
                        Count("Precursor-qualified PSM pairs", concordance.PrecursorQualifiedDatabaseSearchPairs, "pairs"),
                        Count("Exact sequence matches", concordance.ExactPrecursorQualifiedDatabaseSearchPairs, "pairs"),
                        Count("Strict mismatches", concordance.PrecursorQualifiedDatabaseSearchPairs - concordance.ExactPrecursorQualifiedDatabaseSearchPairs, "pairs"),
                        Count("Additional I/L-equivalent pairs", concordance.AdditionalIleLeuEquivalentPrecursorQualifiedPairs, "pairs"),
                        Count("Shared database-search scans", concordance.SharedDatabaseSearchScanKeys, "scans"),
                        Count("Shared scans with a precursor-qualified pair", concordance.PrecursorQualifiedDatabaseSearchScanKeys, "scans"),
                        Count("PatternLab PSM rows", concordance.PatternLabPsmRows, "PSM rows"),
                        Count("PEAKS database PSM rows", concordance.PeaksDatabasePsmRows, "PSM rows"),
                        Rate("Strict agreement", concordance.PrecursorQualifiedStrictAgreement, "Exact sequence matches / precursor-qualified PSM pairs"),
                    }));
            if (manifest.DeNovoOverlap is { HasScanLevelSequenceComparison: true } overlap)
            {
                overlap.Validate();
                plots.Add(new("de_novo_alternative_triage.png", "De novo precursor triage", "Source scope: " + overlap.SourceScopeLabel
                    + ". Counts use the native same-run, scan, reported-charge and precursor-m/z gates, within "
                    + overlap.PrecursorTolerancePpm.ToString("0.###", CultureInfo.InvariantCulture)
                    + " ppm. They describe source/search overlap; they do not estimate caller accuracy. Search-overlapping calls are excluded from additive de novo signal. PEAKS database context uses the same scan and charge.", "calls", new[]
                {
                    Count("No search PSM in the same scan", overlap.NoSearchPsmInFileAndScan, "calls"),
                    Count("Different reported charge", overlap.DifferentChargeInSharedScan, "calls"),
                    Count("Same charge; different precursor m/z", overlap.SameChargeButNoMzMatch, "calls"),
                    Count("Same scan, charge and precursor m/z", overlap.SamePrecursorWithinTolerance, "calls"),
                    Count("Strict matches after PTM removal", overlap.StrictSequenceMatchesIgnoringPtm, "calls"),
                    Count("Additional I/L-equivalent matches", overlap.AdditionalIleLeuEquivalentMatches, "calls"),
                    Count("Different sequences after I/L folding", overlap.SequenceDivergencesAfterIleLeuFold, "calls"),
                    Count("Same-charge PEAKS database context", concordance.DeNovoRowsSharingPeaksDatabaseScanAndCharge, "calls"),
                    Count("Exact PEAKS database sequence in that context", concordance.DeNovoRowsExactToPeaksDatabaseAtSameCharge, "calls"),
                }));
            }
        }
        if (manifest.DeNovoCallerAgreement is { } agreement)
        {
            agreement.Validate();
            plots.Add(new("de_novo_peaks_deepnovo_agreement.png", "Agreement among imported caller pairs",
                agreement.PopulationDefinition + ". All imported calls are compared before the configured quantitation filters. Observed confidence floors: PEAKS ALC " + agreement.PeaksObservedMinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture)
                    + "; DeepNovo SCORE " + agreement.DeepNovoObservedMinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture)
                    + ". Precursor tolerance: " + agreement.PrecursorTolerancePpm.ToString("0.###", CultureInfo.InvariantCulture)
                    + " ppm; theoretical-mass tolerance: " + agreement.MassToleranceDa.ToString("0.###", CultureInfo.InvariantCulture)
                    + " Da; RT sensitivity tolerance: " + agreement.RtSensitivityToleranceMin.ToString("0.###", CultureInfo.InvariantCulture)
                    + " min. Identity is 100 x (1 - edit distance / longer PTM-cleaned sequence length), with I/L distinct. Points without mass or a comparable sequence are omitted. Dots pool runs and show source agreement rather than identification accuracy.", "pairs", new[]
                {
                    Count("Compared pairs", agreement.Total, "pairs"), Count("Identical sequences", agreement.Identical, "pairs"),
                    Count("Different sequence; theoretical mass match", agreement.SameMassRearrangement, "pairs"),
                    Count("Different theoretical mass", agreement.DifferentMass, "pairs"),
                    Count("Sequence disagreements", agreement.Disagreements, "pairs"),
                    Count("Single equal-mass residue swaps", agreement.SingleIsobaricSwaps, "pairs"),
                    Count("Shared scans before precursor gates", agreement.ScanOnlyTotal, "scans"),
                    Count("Precursor-qualified shared scans", agreement.PrecursorQualifiedScanOnly, "scans"),
                    Count("RT-sensitivity qualified comparisons", agreement.RtSensitivityQualifiedCount, "pairs"),
                    new Row("De-novo-only source membership", agreement.DeNovoOnlyMembershipAvailable ? agreement.InDeNovoOnlyCount : null,
                        agreement.DeNovoOnlyMembershipAvailable ? agreement.InDeNovoOnlyCount.ToString("N0", CultureInfo.InvariantCulture) : "Not evaluated",
                        "pairs", "Compatible members of the loaded de-novo-only audit source"),
                    Rate("Identical share", agreement.IdenticalFraction, "Identical sequences / compared precursor-call pairs"),
                    Rate("Same theoretical mass share", agreement.Total > 0 ? (double)agreement.SameMassRearrangement / agreement.Total : null, "Same theoretical mass pairs / compared precursor-call pairs"),
                    Rate("Different theoretical mass share", agreement.Total > 0 ? (double)agreement.DifferentMass / agreement.Total : null, "Different theoretical mass pairs / compared precursor-call pairs"),
                }) { XLabel = "PEAKS neutral mass (Da)", YLabel = "Sequence identity (%)", Points = agreement.Spectra.Where(p => p.MassDa > 0 && double.IsFinite(p.SequenceIdentityPercent)).Select(p =>
                    new Point(p.MassDa, p.SequenceIdentityPercent, p.Category.ToString(), p.MassDa.ToString("G17", CultureInfo.InvariantCulture) + " Da",
                        p.SequenceIdentityPercent.ToString("G17", CultureInfo.InvariantCulture) + "% sequence identity")).ToArray() });
        }
        if (manifest.DeNovoSourceVenn is { } venn)
        {
            venn.Validate();
            var rows = new List<Row>();
            foreach (var stage in new[] { venn.FilteredBeforeUnion, venn.CarriedForwardClaimProvenance })
            {
                rows.Add(Count(stage.StageLabel + ": PEAKS only", stage.PeaksExclusive, "pooled sequences"));
                rows.Add(Count(stage.StageLabel + ": both callers", stage.Shared, "pooled sequences"));
                rows.Add(Count(stage.StageLabel + ": DeepNovo only", stage.DeepNovoExclusive, "pooled sequences"));
                rows.Add(Count(stage.StageLabel + ": sequence union", stage.Union, "pooled sequences"));
            }
            var funnel = venn.DeepNovoFunnel;
            rows.Add(Count("DeepNovo imported", funnel.RawRows, "source calls"));
            rows.Add(Count("DeepNovo passed score cutoff", funnel.ScorePassingRows, "source calls"));
            rows.Add(Count("DeepNovo passed score and length", funnel.ScoreAndLengthPassingRows, "source calls"));
            rows.Add(Count("DeepNovo passed every source filter", funnel.AllFiltersPassingRows, "source calls"));
            plots.Add(new("de_novo_source_venn.png", "Distinct sequences by calling source", "The three regions within each stage are disjoint. Source sets remove PTM notation, keep I/L distinct and pool runs and charge states. Carried-forward claims come from this exact union, not subtraction of independent computations.", "pooled sequences", rows));
        }
        if (manifest.DeNovoAcceptedScoreScatter is { Total: > 0 } scatter)
        {
            scatter.Validate();
            plots.Add(new("de_novo_accepted_score_agreement.png", "Scores when both callers passed",
                scatter.PeaksFilterDescription + ". " + scatter.DeepNovoFilterDescription + ". " + DeNovoAcceptedScoreScatter.ScoreScaleCaveat + " Each dot is one compatible precursor hypothesis; shapes separate exact PTM-cleaned matches from mismatches. I/L remain distinct.", "pairs", new[]
                { Count("Jointly accepted pairs", scatter.Total, "pairs"), Count("Identical sequences", scatter.Matches, "pairs"),
                    Count("Sequence mismatches", scatter.Mismatches, "pairs"),
                    Rate("Identical share", scatter.MatchFraction, "Identical sequences / jointly accepted precursor pairs"),
                    Rate("Mismatch share", 1 - scatter.MatchFraction, "Sequence mismatches / jointly accepted precursor pairs") })
            { XLabel = "PEAKS ALC", YLabel = "DeepNovo SCORE", Points = scatter.Points.Select(p => new Point(p.PeaksAlc, p.DeepNovoScore,
                p.StrictSequenceMatch ? "Match" : "Mismatch", p.PeaksAlc.ToString("G17", CultureInfo.InvariantCulture) + " PEAKS ALC",
                p.DeepNovoScore.ToString("G17", CultureInfo.InvariantCulture) + " DeepNovo SCORE")).ToArray() });
        }
        int page = 0;
        foreach (var rows in WorldPages(manifest, grouping))
        {
            page++;
            plots.Add(new("family_world_rates_" + page.ToString(CultureInfo.InvariantCulture) + ".png", "Family-world support rates",
                "Recorded family-world evidence, not identification FDR. Decoy ratio divides decoy signal by credited signal. Right rate divides correct known credits by known credits and is unavailable below the native evidence floor. Rates are not clamped to 100%.", "%", rows.SelectMany(row => new[]
                {
                    Rate(row.Family + ": decoy ratio", row.DecoyRate, "Decoy signal / credited family-world signal"),
                    Rate(row.Family + ": right rate", row.RightRate, "Correct known credits / known credits"),
                }).ToArray()));
        }
        return plots;
    }
}
