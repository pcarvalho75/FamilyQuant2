using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// Three disjoint regions of the PEAKS de-novo-only / DeepNovo sequence comparison.
    /// Counts are exact PTM-cleaned residue strings with I/L kept distinct.
    /// </summary>
    public sealed class DeNovoSequenceSetRegions
    {
        public string StageId { get; set; } = string.Empty;
        public string StageLabel { get; set; } = string.Empty;
        public string Scope { get; set; } = "pooled";
        public string Grain { get; set; } = string.Empty;
        public int PeaksExclusive { get; set; }
        public int Shared { get; set; }
        public int DeepNovoExclusive { get; set; }
        public int Union { get; set; }
        public int PeaksTotal => PeaksExclusive + Shared;
        public int DeepNovoTotal => DeepNovoExclusive + Shared;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(StageId) || string.IsNullOrWhiteSpace(StageLabel)
                || string.IsNullOrWhiteSpace(Scope) || string.IsNullOrWhiteSpace(Grain))
                throw new InvalidOperationException("Every de novo Venn region row must state its stage, scope and counting grain.");
            if (PeaksExclusive < 0 || Shared < 0 || DeepNovoExclusive < 0 || Union < 0)
                throw new InvalidOperationException("De novo Venn region counts cannot be negative.");
            int expected = checked(PeaksExclusive + Shared + DeepNovoExclusive);
            if (Union != expected)
                throw new InvalidOperationException("The de novo Venn union must equal its three disjoint regions.");
        }
    }

    /// <summary>One caller's complete source-filter cascade before precursor union resolution.</summary>
    public sealed class DeNovoSourceFilterFunnel
    {
        public string SourceId { get; set; } = string.Empty;
        public string SourceDisplayName { get; set; } = string.Empty;
        public string ConfidenceField { get; set; } = string.Empty;
        public double MinimumConfidence { get; set; }
        public int MinLength { get; set; }
        public int StretchLength { get; set; }
        public int StretchFloor { get; set; }
        public int RawRows { get; set; }
        public int ScorePassingRows { get; set; }
        public int ScoreAndLengthPassingRows { get; set; }
        public int AllFiltersPassingRows { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SourceId) || string.IsNullOrWhiteSpace(SourceDisplayName)
                || string.IsNullOrWhiteSpace(ConfidenceField))
                throw new InvalidOperationException("Every source-filter funnel must identify its source and confidence field.");
            if (MinimumConfidence < 0 || MinimumConfidence > 100 || MinLength < 0
                || StretchLength < 0 || StretchFloor < 0 || StretchFloor > 100)
                throw new InvalidOperationException("The de novo filter settings are outside their valid range.");
            if (RawRows < ScorePassingRows || ScorePassingRows < ScoreAndLengthPassingRows
                || ScoreAndLengthPassingRows < AllFiltersPassingRows || AllFiltersPassingRows < 0)
                throw new InvalidOperationException("The source-filter funnel must be monotonically non-increasing.");
        }
    }

    /// <summary>
    /// Reproducible source-set evidence for the exact PEAKS de-novo-only + DeepNovo computation.
    /// The two stages deliberately answer different questions and therefore carry independent grain text.
    /// </summary>
    public sealed class DeNovoSourceVennSummary
    {
        public const string Normalization =
            "PTM notation removed; uppercase residue letters retained; I/L distinct; empty sequences excluded";

        public DeNovoSequenceSetRegions FilteredBeforeUnion { get; set; } =
            new DeNovoSequenceSetRegions();
        public List<DeNovoSequenceSetRegions> FilteredBeforeUnionByRun { get; set; } =
            new List<DeNovoSequenceSetRegions>();
        public DeNovoSequenceSetRegions CarriedForwardClaimProvenance { get; set; } =
            new DeNovoSequenceSetRegions();
        public List<DeNovoSequenceSetRegions> CarriedForwardClaimProvenanceByRun { get; set; } =
            new List<DeNovoSequenceSetRegions>();
        public DeNovoSourceFilterFunnel PeaksFunnel { get; set; } = new DeNovoSourceFilterFunnel();
        public DeNovoSourceFilterFunnel DeepNovoFunnel { get; set; } = new DeNovoSourceFilterFunnel();

        public void Validate()
        {
            FilteredBeforeUnion.Validate();
            CarriedForwardClaimProvenance.Validate();
            foreach (DeNovoSequenceSetRegions row in FilteredBeforeUnionByRun) row.Validate();
            foreach (DeNovoSequenceSetRegions row in CarriedForwardClaimProvenanceByRun) row.Validate();
            PeaksFunnel.Validate();
            DeepNovoFunnel.Validate();
            if (!string.Equals(PeaksFunnel.SourceId, DeNovoSource.DenovoOnly.Id,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(DeepNovoFunnel.SourceId, DeNovoSource.DeepNovo.Id,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The source Venn summary must compare PEAKS de-novo-only with DeepNovo.");
        }
    }

    /// <summary>
    /// Computes exact caller sequence-set regions from source rows and from the provenance attached to
    /// carried-forward rows in the same multi-source union. No count is inferred by subtracting results
    /// from separately quantified source variants.
    /// </summary>
    public static class DeNovoSourceVennStudy
    {
        public const string CsvFileName = "de_novo_source_venn.csv";

        public const string FilteredBeforeUnionGrain =
            "pooled distinct PTM-cleaned source-call residue sequence after that source's score, length and local-confidence filters, before precursor union resolution; run and charge ignored; I/L distinct";

        public const string CarriedForwardClaimProvenanceGrain =
            "pooled distinct PTM-cleaned source-claim residue sequence attached by source provenance to a union-resolved precursor row retained as DeNovoOnly or CoIsolatedExtra after PatternLab reconciliation; run and charge ignored; I/L distinct; not inferred from separate source-variant totals";

        public static DeNovoSourceVennSummary Build(
            IEnumerable<PeaksDeNovoRow> peaksRaw,
            IEnumerable<PeaksDeNovoRow> deepNovoRaw,
            DeNovoFilterCriteria criteria,
            double peaksMinimumConfidence,
            double deepNovoMinimumConfidence,
            IEnumerable<ReconciledRow> unionReconciliation)
        {
            if (peaksRaw == null) throw new ArgumentNullException(nameof(peaksRaw));
            if (deepNovoRaw == null) throw new ArgumentNullException(nameof(deepNovoRaw));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            if (unionReconciliation == null) throw new ArgumentNullException(nameof(unionReconciliation));

            List<PeaksDeNovoRow> peaks = peaksRaw.ToList();
            List<PeaksDeNovoRow> deep = deepNovoRaw.ToList();
            List<PeaksDeNovoRow> peaksAccepted = DeNovoFilter.Apply(
                peaks, criteria, peaksMinimumConfidence);
            List<PeaksDeNovoRow> deepAccepted = DeNovoFilter.Apply(
                deep, criteria, deepNovoMinimumConfidence);
            List<ReconciledRow> reconciliation = unionReconciliation.ToList();

            var summary = new DeNovoSourceVennSummary
            {
                PeaksFunnel = Funnel(peaks, DeNovoSource.DenovoOnly, criteria,
                    peaksMinimumConfidence),
                DeepNovoFunnel = Funnel(deep, DeNovoSource.DeepNovo, criteria,
                    deepNovoMinimumConfidence),
                FilteredBeforeUnion = Regions("filtered-before-union",
                    "After all three source filters · before precursor union", "pooled",
                    FilteredBeforeUnionGrain,
                    peaksAccepted.Select(r => r.Peptide), deepAccepted.Select(r => r.Peptide)),
            };

            foreach (string run in peaksAccepted.Select(r => Run(r.RunName))
                .Concat(deepAccepted.Select(r => Run(r.RunName)))
                .Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            {
                summary.FilteredBeforeUnionByRun.Add(Regions("filtered-before-union",
                    "After all three source filters · before precursor union", run,
                    FilteredBeforeUnionGrain,
                    peaksAccepted.Where(r => SameRun(r.RunName, run)).Select(r => r.Peptide),
                    deepAccepted.Where(r => SameRun(r.RunName, run)).Select(r => r.Peptide)));
            }

            (List<string> peaksClaims, List<string> deepClaims) = CarriedClaims(reconciliation);
            summary.CarriedForwardClaimProvenance = Regions("carried-forward-claim-provenance",
                "Carried-forward union rows · source-claim provenance", "pooled",
                CarriedForwardClaimProvenanceGrain, peaksClaims, deepClaims);

            foreach (string run in reconciliation.Where(IsCarried)
                .Select(r => Run(r.DeNovo.RunName)).Where(r => r.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            {
                (List<string> runPeaks, List<string> runDeep) = CarriedClaims(
                    reconciliation.Where(r => SameRun(r.DeNovo.RunName, run)));
                summary.CarriedForwardClaimProvenanceByRun.Add(Regions(
                    "carried-forward-claim-provenance",
                    "Carried-forward union rows · source-claim provenance", run,
                    CarriedForwardClaimProvenanceGrain, runPeaks, runDeep));
            }

            summary.Validate();
            return summary;
        }

        public static string ToCsv(DeNovoSourceVennSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            summary.Validate();
            var b = new StringBuilder();
            Row(b, "record_type", "stage_id", "stage_label", "scope", "source_id",
                "source_name", "confidence_field", "minimum_confidence", "raw_rows",
                "score_passing_rows", "score_and_length_passing_rows", "all_filters_passing_rows",
                "peaks_exclusive", "shared", "deepnovo_exclusive", "union", "peaks_total",
                "deepnovo_total", "counting_grain", "sequence_normalization");

            FunnelRow(b, summary.PeaksFunnel);
            FunnelRow(b, summary.DeepNovoFunnel);
            RegionRow(b, summary.FilteredBeforeUnion);
            foreach (DeNovoSequenceSetRegions row in summary.FilteredBeforeUnionByRun) RegionRow(b, row);
            RegionRow(b, summary.CarriedForwardClaimProvenance);
            foreach (DeNovoSequenceSetRegions row in summary.CarriedForwardClaimProvenanceByRun)
                RegionRow(b, row);
            return b.ToString();
        }

        private static DeNovoSourceFilterFunnel Funnel(List<PeaksDeNovoRow> rows,
            DeNovoSource source, DeNovoFilterCriteria criteria, double cutoff)
        {
            int score = rows.Count(r => r.ConfidenceValue >= cutoff);
            int scoreLength = rows.Count(r => r.ConfidenceValue >= cutoff
                && r.Length >= criteria.MinLength);
            int all = rows.Count(r => r.ConfidenceValue >= cutoff
                && r.Length >= criteria.MinLength
                && DeNovoFilter.HasStretch(r.LocalConfidence, criteria.StretchLength,
                    criteria.StretchFloor));
            return new DeNovoSourceFilterFunnel
            {
                SourceId = source.Id,
                SourceDisplayName = source.DisplayName,
                ConfidenceField = source.ConfidenceField,
                MinimumConfidence = cutoff,
                MinLength = criteria.MinLength,
                StretchLength = criteria.StretchLength,
                StretchFloor = criteria.StretchFloor,
                RawRows = rows.Count,
                ScorePassingRows = score,
                ScoreAndLengthPassingRows = scoreLength,
                AllFiltersPassingRows = all,
            };
        }

        private static DeNovoSequenceSetRegions Regions(string stageId, string stageLabel,
            string scope, string grain, IEnumerable<string> peaksSequences,
            IEnumerable<string> deepSequences)
        {
            var peaks = new HashSet<string>(peaksSequences.Select(Normalize)
                .Where(s => s.Length > 0), StringComparer.Ordinal);
            var deep = new HashSet<string>(deepSequences.Select(Normalize)
                .Where(s => s.Length > 0), StringComparer.Ordinal);
            int shared = peaks.Count(deep.Contains);
            return new DeNovoSequenceSetRegions
            {
                StageId = stageId,
                StageLabel = stageLabel,
                Scope = scope,
                Grain = grain,
                PeaksExclusive = peaks.Count - shared,
                Shared = shared,
                DeepNovoExclusive = deep.Count - shared,
                Union = peaks.Count + deep.Count - shared,
            };
        }

        private static (List<string> peaks, List<string> deep) CarriedClaims(
            IEnumerable<ReconciledRow> reconciliation)
        {
            var peaks = new List<string>();
            var deep = new List<string>();
            foreach (ReconciledRow row in reconciliation.Where(IsCarried))
            {
                IReadOnlyList<DeNovoEvidence> evidence = row.DeNovo.SourceEvidence != null
                    && row.DeNovo.SourceEvidence.Count > 0
                    ? row.DeNovo.SourceEvidence
                    : new[]
                    {
                        new DeNovoEvidence
                        {
                            SourceId = row.DeNovo.SourceId,
                            Sequence = row.DeNovo.Peptide,
                        },
                    };
                foreach (DeNovoEvidence claim in evidence)
                {
                    if (string.Equals(claim.SourceId, DeNovoSource.DenovoOnly.Id,
                        StringComparison.OrdinalIgnoreCase))
                        peaks.Add(claim.Sequence);
                    else if (string.Equals(claim.SourceId, DeNovoSource.DeepNovo.Id,
                        StringComparison.OrdinalIgnoreCase))
                        deep.Add(claim.Sequence);
                }
            }
            return (peaks, deep);
        }

        private static bool IsCarried(ReconciledRow row) =>
            row.Verdict == ReconciliationVerdict.DeNovoOnly
            || row.Verdict == ReconciliationVerdict.CoIsolatedExtra;

        private static string Normalize(string sequence) =>
            DeNovoReconciler.NormalizeSequenceIgnoringPtm(sequence ?? string.Empty,
                foldIsoleucineLeucine: false);

        private static string Run(string run) => DeNovoReconciler.NormalizeRunName(run ?? string.Empty);

        private static bool SameRun(string sourceRun, string normalizedRun) =>
            string.Equals(Run(sourceRun), normalizedRun, StringComparison.OrdinalIgnoreCase);

        private static void FunnelRow(StringBuilder b, DeNovoSourceFilterFunnel f) => Row(b,
            "filter_funnel", string.Empty, "Three source filters", "pooled", f.SourceId,
            f.SourceDisplayName, f.ConfidenceField,
            f.MinimumConfidence.ToString("R", CultureInfo.InvariantCulture), f.RawRows,
            f.ScorePassingRows, f.ScoreAndLengthPassingRows, f.AllFiltersPassingRows,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            "source spectrum rows; filters applied cumulatively: score, then length, then local-confidence stretch",
            DeNovoSourceVennSummary.Normalization);

        private static void RegionRow(StringBuilder b, DeNovoSequenceSetRegions r) => Row(b,
            "venn_regions", r.StageId, r.StageLabel, r.Scope, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            r.PeaksExclusive, r.Shared, r.DeepNovoExclusive, r.Union, r.PeaksTotal,
            r.DeepNovoTotal, r.Grain, DeNovoSourceVennSummary.Normalization);

        private static void Row(StringBuilder b, params object[] values) =>
            b.Append(string.Join(",", values.Select(v => Csv(Convert.ToString(v,
                CultureInfo.InvariantCulture) ?? string.Empty)))).Append('\n');

        private static string Csv(string value) => SpreadsheetSafeCsv.Encode(value);
    }
}
