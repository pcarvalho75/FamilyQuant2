using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// One plotted precursor-hypothesis row for which both PEAKS and DeepNovo passed their complete
    /// configured filters. Raw annotated sequences are retained beside the PTM-cleaned strings used for
    /// the strict equality decision, so the plotted classification is fully auditable.
    /// </summary>
    public sealed class DeNovoAcceptedScorePoint
    {
        public string FileName { get; set; } = string.Empty;
        public string NormalizedRunName { get; set; } = string.Empty;
        public int ScanNumber { get; set; }
        public int PrecursorHypothesisIndex { get; set; }
        public int Charge { get; set; }
        public double PrecursorMz { get; set; }
        public string PeaksAnnotatedSequence { get; set; } = string.Empty;
        public string PeaksCleanSequence { get; set; } = string.Empty;
        public double PeaksAlc { get; set; }
        public string PeaksScoreField { get; set; } = string.Empty;
        public string DeepNovoAnnotatedSequence { get; set; } = string.Empty;
        public string DeepNovoCleanSequence { get; set; } = string.Empty;
        public double DeepNovoScore { get; set; }
        public string DeepNovoScoreField { get; set; } = string.Empty;
        public bool StrictSequenceMatch { get; set; }
        public double? PrimaryQuantValue { get; set; }
    }

    /// <summary>
    /// The project-native score scatter requested for the talk. Its population is deliberately narrower
    /// than a scan-only agreement audit: one dot is one persisted compatible precursor-hypothesis row for
    /// which both source calls have non-empty sequences/scores and both accepted flags are true.
    /// </summary>
    public sealed class DeNovoAcceptedScoreScatter
    {
        public const string CountingGrain =
            "normalized file + MS2 scan + equal reported charge + non-chaining, isotope-error-aware 20-ppm opening-m/z precursor-hypothesis cluster (M, M+-1, M+-2 of one precursor share a row); one deterministic top accepted call per source";
        public const string SequenceNormalization =
            "PTM notation and SEPro flanking residues removed; uppercase residue letters retained; I/L distinct";
        public const string ScoreScaleCaveat =
            "PEAKS ALC and DeepNovo SCORE are source-native confidence scales; equal numeric values are not calibrated as equivalent probabilities";

        public string PeaksScoreField { get; set; } = "ALC (%)";
        public string DeepNovoScoreField { get; set; } = "SCORE (%)";
        /// <summary>The configured acceptance cutoff when a source distribution was available.
        /// Null means that the chart must infer its extent from the accepted observations only.</summary>
        public double? PeaksMinimumAcceptedScore { get; set; }
        public double? DeepNovoMinimumAcceptedScore { get; set; }
        public string PeaksFilterDescription { get; set; } =
            "PEAKS call accepted by the complete configured score, length and local-confidence filters";
        public string DeepNovoFilterDescription { get; set; } =
            "DeepNovo call accepted by the complete configured score, length and local-confidence filters";
        public List<DeNovoAcceptedScorePoint> Points { get; set; } =
            new List<DeNovoAcceptedScorePoint>();

        public int Total => Points.Count;
        public int Matches => Points.Count(p => p.StrictSequenceMatch);
        public int Mismatches => Total - Matches;
        public double MatchFraction => Total > 0 ? (double)Matches / Total : 0.0;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(PeaksScoreField)
                || string.IsNullOrWhiteSpace(DeepNovoScoreField)
                || string.IsNullOrWhiteSpace(PeaksFilterDescription)
                || string.IsNullOrWhiteSpace(DeepNovoFilterDescription))
                throw new InvalidOperationException(
                    "The accepted-score scatter must state both score fields and both complete filters.");
            if (Points == null)
                throw new InvalidOperationException("The accepted-score scatter point list is null.");
            if ((PeaksMinimumAcceptedScore.HasValue
                    && !double.IsFinite(PeaksMinimumAcceptedScore.Value))
                || (DeepNovoMinimumAcceptedScore.HasValue
                    && !double.IsFinite(DeepNovoMinimumAcceptedScore.Value)))
                throw new InvalidOperationException(
                    "Accepted-score cutoffs must be finite when they are available.");
            if (Points.Any(p => p == null || p.ScanNumber <= 0 || p.Charge <= 0
                || p.PeaksCleanSequence.Length == 0 || p.DeepNovoCleanSequence.Length == 0
                || !double.IsFinite(p.PeaksAlc) || !double.IsFinite(p.DeepNovoScore)))
                throw new InvalidOperationException(
                    "Every accepted-score point must carry a scan, charge, two clean sequences and two finite scores.");
            if (Points.Any(p => p.StrictSequenceMatch != string.Equals(
                    p.PeaksCleanSequence, p.DeepNovoCleanSequence, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "An accepted-score point disagrees with strict PTM-cleaned sequence equality.");
        }
    }

    /// <summary>Builds the accepted-call scatter exclusively from the spectrum comparison table that the
    /// program persists and shows in its DataGrid. The chart and CSV therefore cannot use a different
    /// join, acceptance decision or sequence normalizer from the table the user inspects.</summary>
    public static class DeNovoAcceptedScoreScatterStudy
    {
        public const string CsvFileName = "de_novo_accepted_score_agreement.csv";

        public static DeNovoAcceptedScoreScatter Build(
            IEnumerable<SpectrumComparisonRow> spectrumRows,
            IEnumerable<DeNovoSourceScoreDistribution>? sourceDistributions = null)
        {
            if (spectrumRows == null) throw new ArgumentNullException(nameof(spectrumRows));
            List<DeNovoSourceScoreDistribution> distributions =
                (sourceDistributions ?? Enumerable.Empty<DeNovoSourceScoreDistribution>())
                .Where(d => d != null).ToList();
            DeNovoSourceScoreDistribution? peaks = distributions.FirstOrDefault(d =>
                !string.Equals(d.SourceId, DeNovoSource.DeepNovo.Id,
                    StringComparison.OrdinalIgnoreCase));
            DeNovoSourceScoreDistribution? deep = distributions.FirstOrDefault(d =>
                string.Equals(d.SourceId, DeNovoSource.DeepNovo.Id,
                    StringComparison.OrdinalIgnoreCase));

            var result = new DeNovoAcceptedScoreScatter
            {
                PeaksScoreField = peaks?.ConfidenceField ?? "ALC (%)",
                DeepNovoScoreField = deep?.ConfidenceField ?? "SCORE (%)",
                PeaksMinimumAcceptedScore = peaks == null
                    || !double.IsFinite(peaks.MinimumConfidence)
                    ? null : peaks.MinimumConfidence,
                DeepNovoMinimumAcceptedScore = deep == null
                    || !double.IsFinite(deep.MinimumConfidence)
                    ? null : deep.MinimumConfidence,
                PeaksFilterDescription = FilterDescription(peaks, "PEAKS"),
                DeepNovoFilterDescription = FilterDescription(deep, "DeepNovo"),
            };

            foreach (SpectrumComparisonRow row in spectrumRows
                .Where(r => r != null && r.PeaksDeNovoAccepted == true
                    && r.DeepNovoAccepted == true
                    && r.PeaksDeNovoScore.HasValue && r.DeepNovoScore.HasValue)
                .OrderBy(r => r.NormalizedRunName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.ScanNumber).ThenBy(r => r.PrecursorHypothesisIndex)
                .ThenBy(r => r.Charge).ThenBy(r => r.PrecursorMz))
            {
                string peaksClean = DeNovoReconciler.NormalizeSequenceIgnoringPtm(
                    row.PeaksDeNovoSequence, foldIsoleucineLeucine: false);
                string deepClean = DeNovoReconciler.NormalizeSequenceIgnoringPtm(
                    row.DeepNovoSequence, foldIsoleucineLeucine: false);
                double peaksScore = row.PeaksDeNovoScore!.Value;
                double deepScore = row.DeepNovoScore!.Value;
                if (peaksClean.Length == 0 || deepClean.Length == 0
                    || !double.IsFinite(peaksScore) || !double.IsFinite(deepScore)) continue;

                result.Points.Add(new DeNovoAcceptedScorePoint
                {
                    FileName = row.FileName,
                    NormalizedRunName = row.NormalizedRunName,
                    ScanNumber = row.ScanNumber,
                    PrecursorHypothesisIndex = row.PrecursorHypothesisIndex,
                    Charge = row.Charge,
                    PrecursorMz = row.PrecursorMz,
                    PeaksAnnotatedSequence = row.PeaksDeNovoSequence,
                    PeaksCleanSequence = peaksClean,
                    PeaksAlc = peaksScore,
                    PeaksScoreField = row.PeaksDeNovoScoreField,
                    DeepNovoAnnotatedSequence = row.DeepNovoSequence,
                    DeepNovoCleanSequence = deepClean,
                    DeepNovoScore = deepScore,
                    DeepNovoScoreField = row.DeepNovoScoreField,
                    StrictSequenceMatch = string.Equals(peaksClean, deepClean,
                        StringComparison.Ordinal),
                    PrimaryQuantValue = row.PrimaryQuantValue,
                });
            }

            result.Validate();
            return result;
        }

        public static string ToCsv(DeNovoAcceptedScoreScatter scatter)
        {
            if (scatter == null) throw new ArgumentNullException(nameof(scatter));
            scatter.Validate();
            var b = new StringBuilder();
            Row(b, "FileName", "NormalizedRunName", "ScanNumber",
                "PrecursorHypothesisIndex", "Charge", "PrecursorMz",
                "PeaksAnnotatedSequence", "PeaksCleanSequence", "PeaksScore",
                "PeaksScoreField", "DeepNovoAnnotatedSequence", "DeepNovoCleanSequence",
                "DeepNovoScore", "DeepNovoScoreField", "StrictSequenceMatch", "MatchClass",
                "PrimaryQuantValue", "PeaksMinimumAcceptedScore",
                "DeepNovoMinimumAcceptedScore", "PeaksFilter", "DeepNovoFilter", "CountingGrain",
                "SequenceNormalization", "ScoreScaleCaveat");
            foreach (DeNovoAcceptedScorePoint p in scatter.Points)
                Row(b, p.FileName, p.NormalizedRunName, p.ScanNumber,
                    p.PrecursorHypothesisIndex, p.Charge,
                    p.PrecursorMz.ToString("R", CultureInfo.InvariantCulture),
                    p.PeaksAnnotatedSequence, p.PeaksCleanSequence,
                    p.PeaksAlc.ToString("R", CultureInfo.InvariantCulture),
                    p.PeaksScoreField, p.DeepNovoAnnotatedSequence, p.DeepNovoCleanSequence,
                    p.DeepNovoScore.ToString("R", CultureInfo.InvariantCulture),
                    p.DeepNovoScoreField, p.StrictSequenceMatch,
                    p.StrictSequenceMatch ? "strict PTM-clean match" : "strict PTM-clean mismatch",
                    Num(p.PrimaryQuantValue), Num(scatter.PeaksMinimumAcceptedScore),
                    Num(scatter.DeepNovoMinimumAcceptedScore), scatter.PeaksFilterDescription,
                    scatter.DeepNovoFilterDescription, DeNovoAcceptedScoreScatter.CountingGrain,
                    DeNovoAcceptedScoreScatter.SequenceNormalization,
                    DeNovoAcceptedScoreScatter.ScoreScaleCaveat);
            return b.ToString();
        }

        private static string FilterDescription(DeNovoSourceScoreDistribution? distribution,
            string fallbackSource)
        {
            if (distribution == null)
                return fallbackSource +
                    " call accepted by the complete configured score, length and local-confidence filters";
            return distribution.SourceDisplayName + ": " + distribution.ConfidenceField + " >= " +
                distribution.MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture) +
                "; length >= " + distribution.MinLength.ToString(CultureInfo.InvariantCulture) +
                "; >= " + distribution.StretchLength.ToString(CultureInfo.InvariantCulture) +
                " consecutive residues at local confidence >= " +
                distribution.StretchFloor.ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static string Num(double? value) => value.HasValue
            ? value.Value.ToString("R", CultureInfo.InvariantCulture) : string.Empty;

        private static void Row(StringBuilder b, params object?[] values) =>
            b.Append(string.Join(",", values.Select(v => Csv(Convert.ToString(v,
                CultureInfo.InvariantCulture) ?? string.Empty)))).Append('\n');

        private static string Csv(string value) => SpreadsheetSafeCsv.Encode(value);
    }
}
