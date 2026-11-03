using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>Support and fitted ratio for one fixed neutral-mass bin.</summary>
    public sealed class ReferenceEnvelopeBinDiagnostic
    {
        public int Bin { get; set; }
        public double LowerMassDa { get; set; }
        /// <summary>Exclusive upper bound, or null for the final open-ended bin. Null keeps the
        /// persisted manifest and supplementary JSON standards-compliant; JSON has no Infinity number.</summary>
        public double? UpperMassDa { get; set; }
        public int IdentifiedPeptides { get; set; }
        public double IdentifiedM0 { get; set; }
        public double IdentifiedEnvelope { get; set; }
        public double EnvelopeToM0Ratio { get; set; }
        public bool UsesPooledFallback { get; set; }
    }

    /// <summary>
    /// Diagnostics for the reference-envelope model. Cross-validation predicts every identified
    /// representative after removing that representative from the fitted ratio; it tests transport of
    /// the mass-bin rule, not peptide identification accuracy. The sensitivity range compares the fixed
    /// bins with one pooled identified ratio and is not a confidence interval.
    /// </summary>
    public sealed class ReferenceEnvelopeDiagnostics
    {
        public string Method { get; set; } = string.Empty;
        public int IdentifiedPeptides { get; set; }
        public int DeNovoPeptides { get; set; }
        public double PooledEnvelopeToM0Ratio { get; set; }
        public int CrossValidatedPeptides { get; set; }
        public double CrossValidatedWeightedAbsolutePercentError { get; set; }
        public double CrossValidatedMedianAbsolutePercentError { get; set; }
        public double CrossValidatedP90AbsolutePercentError { get; set; }
        public double CrossValidatedSignedBiasFraction { get; set; }
        public double FixedBinDeNovoEstimate { get; set; }
        public double PooledRatioDeNovoEstimate { get; set; }
        public double SensitivityLowerEstimate { get; set; }
        public double SensitivityUpperEstimate { get; set; }
        public List<ReferenceEnvelopeBinDiagnostic> Bins { get; set; } =
            new List<ReferenceEnvelopeBinDiagnostic>();

        public void Validate()
        {
            if (IdentifiedPeptides < 0 || DeNovoPeptides < 0 || CrossValidatedPeptides < 0)
                throw new InvalidOperationException("Reference-envelope diagnostic counts cannot be negative.");
            if (double.IsNaN(PooledEnvelopeToM0Ratio) || PooledEnvelopeToM0Ratio < 1.0)
                throw new InvalidOperationException("The pooled reference-envelope ratio is invalid.");
            if (SensitivityLowerEstimate > SensitivityUpperEstimate)
                throw new InvalidOperationException("The reference-envelope sensitivity range is reversed.");
        }
    }

    /// <summary>
    /// Estimates a de novo isotope envelope from its measured M+0 using the envelope/M+0 relationship
    /// observed on identified peptides of comparable neutral mass. This is an explicitly model-based
    /// estimate, not an empirical calibration of de novo peptide accuracy or an observed de novo
    /// envelope. The directly credited six-window envelope remains available as a separate diagnostic.
    /// </summary>
    public static class ReferenceEnvelopeEstimator
    {
        // Broad, fixed bins keep the estimator stable with modest peptide counts. Their support and a
        // pooled-ratio sensitivity analysis are emitted so this design choice is visible to the reader.
        public static readonly double[] MassBinUpperBounds = { 800, 1200, 1600, 2000, 2500, 3000, double.PositiveInfinity };

        public const string Method = "identified-reference-envelope-ratio-by-fixed-mass-bin";
        public const string ProvenanceLabel =
            "model-estimated:denovo-M0 * identified-envelope/M0 within fixed neutral-mass bins; " +
            "pooled fallback; not an empirical de novo calibration";

        public static void Apply(IReadOnlyList<NativePeptideQuant> peptides)
        {
            if (peptides == null) throw new ArgumentNullException(nameof(peptides));

            List<NativePeptideQuant> identified = ValidIdentified(peptides);
            double pooledRatio = PooledRatio(identified);
            double[] ratios = FitRatios(identified, pooledRatio);

            foreach (NativePeptideQuant p in peptides)
            {
                p.ReferenceCalibratedEnvelope = string.Equals(
                        p.Side, NativeQuantFractions.SideDeNovo, StringComparison.Ordinal) && p.AreaM0 > 0
                    ? p.AreaM0 * ratios[BinOf(p.MonoisotopicMass)]
                    : p.EnvelopeSum;
            }
        }

        /// <summary>
        /// Characterizes model support, leave-one-representative-out prediction error and sensitivity to
        /// replacing the mass bins with one pooled ratio. These diagnostics should accompany a quoted
        /// model estimate; the sensitivity bounds are not confidence limits.
        /// </summary>
        public static ReferenceEnvelopeDiagnostics Evaluate(IReadOnlyList<NativePeptideQuant> peptides,
            FamilyQuant.Operations.OperationContext? operation = null, string progressStage = "reference-validation")
        {
            if (peptides == null) throw new ArgumentNullException(nameof(peptides));

            List<NativePeptideQuant> identified = ValidIdentified(peptides);
            operation?.Report(progressStage, "Checking reference-envelope representatives", 0, identified.Count);
            List<NativePeptideQuant> rawDeNovo = peptides.Where(p =>
                string.Equals(p.Side, NativeQuantFractions.SideDeNovo, StringComparison.Ordinal)
                && p.AreaM0 > 0 && p.MonoisotopicMass > 0).ToList();
            Dictionary<string, List<NativePeptideQuant>> identifiedByIdentity = identified
                .GroupBy(NativeQuantFractions.CrossSideKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            List<NativePeptideQuant> deNovo = rawDeNovo.Where(p =>
                !identifiedByIdentity.TryGetValue(NativeQuantFractions.CrossSideKey(p),
                    out List<NativePeptideQuant>? candidates)
                || !candidates.Any(q => NativeQuantFractions.IsCrossSideEquivalent(q, p))).ToList();

            double pooledRatio = PooledRatio(identified);
            double[] ratios = FitRatios(identified, pooledRatio);
            var result = new ReferenceEnvelopeDiagnostics
            {
                Method = Method,
                IdentifiedPeptides = identified.Count,
                DeNovoPeptides = deNovo.Count,
                PooledEnvelopeToM0Ratio = pooledRatio,
                FixedBinDeNovoEstimate = deNovo.Sum(p => p.AreaM0 * ratios[BinOf(p.MonoisotopicMass)]),
                PooledRatioDeNovoEstimate = deNovo.Sum(p => p.AreaM0 * pooledRatio),
            };
            result.SensitivityLowerEstimate = Math.Min(
                result.FixedBinDeNovoEstimate, result.PooledRatioDeNovoEstimate);
            result.SensitivityUpperEstimate = Math.Max(
                result.FixedBinDeNovoEstimate, result.PooledRatioDeNovoEstimate);

            var membersByBin = new List<NativePeptideQuant>[MassBinUpperBounds.Length];
            double lower = 0.0;
            for (int bin = 0; bin < MassBinUpperBounds.Length; bin++)
            {
                int current = bin;
                List<NativePeptideQuant> members = identified
                    .Where(p => BinOf(p.MonoisotopicMass) == current).ToList();
                membersByBin[bin] = members;
                double m0 = members.Sum(p => p.AreaM0);
                double envelope = members.Sum(p => p.EnvelopeSum);
                result.Bins.Add(new ReferenceEnvelopeBinDiagnostic
                {
                    Bin = bin,
                    LowerMassDa = lower,
                    UpperMassDa = double.IsPositiveInfinity(MassBinUpperBounds[bin])
                        ? null : MassBinUpperBounds[bin],
                    IdentifiedPeptides = members.Count,
                    IdentifiedM0 = m0,
                    IdentifiedEnvelope = envelope,
                    EnvelopeToM0Ratio = SafeRatio(envelope, m0, pooledRatio),
                    UsesPooledFallback = !(m0 > 0),
                });
                lower = MassBinUpperBounds[bin];
            }

            var absolutePercentErrors = new List<double>();
            double absoluteError = 0.0;
            double signedError = 0.0;
            double observedTotal = 0.0;
            int completed = 0;
            foreach (NativePeptideQuant heldOut in identified)
            {
                operation?.ThrowIfCancellationRequested();
                int bin = BinOf(heldOut.MonoisotopicMass);
                // The bin lists retain the original order. Keep the exact same Sum operation and
                // remove all occurrences of the held-out reference, just as the original loop did.
                // Subtracting one value from a precomputed total would change floating-point results.
                List<NativePeptideQuant> members = membersByBin[bin];
                double peerM0 = members.Where(p => !ReferenceEquals(p, heldOut)).Sum(p => p.AreaM0);
                double peerEnvelope = members.Where(p => !ReferenceEquals(p, heldOut)).Sum(p => p.EnvelopeSum);
                double localRatio = peerM0 > 0 ? peerEnvelope / peerM0 : double.NaN;
                double ratio;
                if (double.IsFinite(localRatio)) ratio = Math.Max(1.0, localRatio);
                else
                {
                    // Only an unsupported/invalid local ratio uses the pooled fallback. Preserve
                    // its old peer order and calculation when it is actually needed.
                    List<NativePeptideQuant> peers = identified.Where(p => !ReferenceEquals(p, heldOut)).ToList();
                    ratio = SafeRatio(peerEnvelope, peerM0, PooledRatio(peers));
                }
                double predicted = heldOut.AreaM0 * ratio;
                double error = predicted - heldOut.EnvelopeSum;
                absoluteError += Math.Abs(error);
                signedError += error;
                observedTotal += heldOut.EnvelopeSum;
                if (heldOut.EnvelopeSum > 0)
                    absolutePercentErrors.Add(Math.Abs(error) / heldOut.EnvelopeSum);
                if (++completed % 128 == 0 || completed == identified.Count)
                    operation?.Report(progressStage, "Reference representatives checked " + completed.ToString(CultureInfo.InvariantCulture)
                        + " of " + identified.Count.ToString(CultureInfo.InvariantCulture), completed, identified.Count);
            }

            absolutePercentErrors.Sort();
            result.CrossValidatedPeptides = absolutePercentErrors.Count;
            result.CrossValidatedWeightedAbsolutePercentError = observedTotal > 0
                ? absoluteError / observedTotal : 0.0;
            result.CrossValidatedSignedBiasFraction = observedTotal > 0 ? signedError / observedTotal : 0.0;
            result.CrossValidatedMedianAbsolutePercentError = Quantile(absolutePercentErrors, 0.50);
            result.CrossValidatedP90AbsolutePercentError = Quantile(absolutePercentErrors, 0.90);
            result.Validate();
            return result;
        }

        /// <summary>Machine-readable diagnostics for supplementary use.</summary>
        public static string ToCsv(ReferenceEnvelopeDiagnostics diagnostics)
        {
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            diagnostics.Validate();
            var b = new StringBuilder();
            b.AppendLine("section,metric_name,bin,lower_mass_da,upper_mass_da,identified_peptides,identified_m0,identified_envelope,envelope_to_m0_ratio,pooled_fallback,value,definition");
            foreach (ReferenceEnvelopeBinDiagnostic row in diagnostics.Bins)
            {
                b.Append("bin,,").Append(row.Bin.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(Num(row.LowerMassDa)).Append(',')
                 .Append(row.UpperMassDa.HasValue ? Num(row.UpperMassDa.Value) : "Inf").Append(',')
                 .Append(row.IdentifiedPeptides.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(Num(row.IdentifiedM0)).Append(',').Append(Num(row.IdentifiedEnvelope)).Append(',')
                 .Append(Num(row.EnvelopeToM0Ratio)).Append(',')
                 .Append(row.UsesPooledFallback ? "true" : "false").Append(",,")
                 .AppendLine("fixed neutral-mass-bin support and fitted identified envelope/M0 ratio");
            }
            Metric(b, "cross_validated_weighted_absolute_percent_error",
                diagnostics.CrossValidatedWeightedAbsolutePercentError,
                "leave-one-representative-out sum absolute prediction error / sum observed envelope");
            Metric(b, "cross_validated_median_absolute_percent_error",
                diagnostics.CrossValidatedMedianAbsolutePercentError,
                "median leave-one-representative-out absolute percent error");
            Metric(b, "cross_validated_p90_absolute_percent_error",
                diagnostics.CrossValidatedP90AbsolutePercentError,
                "90th percentile leave-one-representative-out absolute percent error");
            Metric(b, "cross_validated_signed_bias_fraction", diagnostics.CrossValidatedSignedBiasFraction,
                "leave-one-representative-out signed error / sum observed envelope");
            Metric(b, "fixed_bin_de_novo_estimate", diagnostics.FixedBinDeNovoEstimate,
                "de novo M0 multiplied by identified ratios in fixed mass bins");
            Metric(b, "pooled_ratio_de_novo_estimate", diagnostics.PooledRatioDeNovoEstimate,
                "sensitivity comparator using one pooled identified ratio");
            Metric(b, "sensitivity_lower_estimate", diagnostics.SensitivityLowerEstimate,
                "lower of fixed-bin and pooled-ratio estimates; not a confidence limit");
            Metric(b, "sensitivity_upper_estimate", diagnostics.SensitivityUpperEstimate,
                "upper of fixed-bin and pooled-ratio estimates; not a confidence limit");
            return b.ToString();
        }

        public static int BinOf(double neutralMass)
        {
            for (int i = 0; i < MassBinUpperBounds.Length; i++)
                if (neutralMass < MassBinUpperBounds[i]) return i;
            return MassBinUpperBounds.Length - 1;
        }

        private static double SafeRatio(double numerator, double denominator, double fallback)
        {
            double ratio = denominator > 0 ? numerator / denominator : fallback;
            if (double.IsNaN(ratio) || double.IsInfinity(ratio)) ratio = fallback;
            // The measured envelope contains M0 and therefore cannot legitimately be below M0. There is
            // no corresponding upper bound: for a sufficiently heavy peptide M0 can be smaller than one
            // or more later isotopologues, so envelope/M0 may exceed the number of measured windows.
            return Math.Max(1.0, ratio);
        }

        private static List<NativePeptideQuant> ValidIdentified(IReadOnlyList<NativePeptideQuant> peptides) =>
            peptides.Where(p =>
                string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal)
                && p.AreaM0 > 0 && p.EnvelopeSum >= p.AreaM0 && p.MonoisotopicMass > 0).ToList();

        private static double PooledRatio(IReadOnlyList<NativePeptideQuant> identified)
        {
            double m0 = identified.Sum(p => p.AreaM0);
            double envelope = identified.Sum(p => p.EnvelopeSum);
            return SafeRatio(envelope, m0, 1.0);
        }

        private static double[] FitRatios(IReadOnlyList<NativePeptideQuant> identified, double pooledRatio)
        {
            var ratios = new double[MassBinUpperBounds.Length];
            for (int bin = 0; bin < ratios.Length; bin++)
            {
                int current = bin;
                double m0 = identified.Where(p => BinOf(p.MonoisotopicMass) == current).Sum(p => p.AreaM0);
                double envelope = identified.Where(p => BinOf(p.MonoisotopicMass) == current)
                    .Sum(p => p.EnvelopeSum);
                ratios[bin] = SafeRatio(envelope, m0, pooledRatio);
            }
            return ratios;
        }

        private static double Quantile(IReadOnlyList<double> sorted, double fraction)
        {
            if (sorted.Count == 0) return 0.0;
            double position = Math.Max(0.0, Math.Min(1.0, fraction)) * (sorted.Count - 1);
            int lo = (int)Math.Floor(position);
            int hi = (int)Math.Ceiling(position);
            if (lo == hi) return sorted[lo];
            double weight = position - lo;
            return sorted[lo] * (1.0 - weight) + sorted[hi] * weight;
        }

        private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static void Metric(StringBuilder b, string name, double value, string definition)
        {
            b.Append("metric,").Append(name).Append(",,,,,,,,,").Append(Num(value)).Append(',')
             .AppendLine(definition);
        }
    }
}
