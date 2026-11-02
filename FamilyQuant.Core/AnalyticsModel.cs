using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant
{
    /// <summary>
    /// One peptide as the analytics screen (Q4) reads it: the representative extracted peak of a
    /// run, side, canonical PTM-aware peptidoform and charge, decorated with the
    /// numbers the graphs plot - its monoisotopic mass, its per-isotope areas, its credited envelope,
    /// how many isotopes actually carried signal, and the family it was attributed to.
    ///
    /// WHY THE REPRESENTATIVE AND NOT THE ANCHOR. The Q2 store holds one row per MS2 anchor, so a peptide
    /// sampled by several scans in one elution peak appears several times; summing them straight
    /// double-counts (see <see cref="NativeQuantFractions"/>). The analytics therefore collapse to the
    /// SAME representative the fraction-of-TIC method uses - the largest-M+0 anchor per key - so every
    /// number the graphs show matches the paper's recovery figures instead of a different, inflated set.
    /// </summary>
    public sealed class AnalyticsPeptide
    {
        public string Run { get; set; } = string.Empty;

        /// <summary>"identified" or "denovo", the two sides told apart, never merged.</summary>
        public string Side { get; set; } = string.Empty;

        public string Sequence { get; set; } = string.Empty;

        public int Charge { get; set; }

        public double PrecursorMz { get; set; }

        /// <summary>The peptide's neutral monoisotopic mass, derived from the precursor m/z and charge by
        /// <see cref="AnalyticsBuilder.MonoisotopicMass"/>. The x axis of the mass graphs.</summary>
        public double Mass { get; set; }

        /// <summary>The credited isotope areas of the representative anchor, index 0 = M+0, padded to the
        /// six-isotope go-forward basis so the CSV always writes area_m0..area_m5.</summary>
        public double[] Areas { get; set; } = Array.Empty<double>();

        /// <summary>The same representative peak's IDENTIFIED-BUT-NOT-QUANTIFIED ion current: clusters
        /// that were found and that the minimum-points rule refused. Zero when nothing was refused.</summary>
        public double UnderSampledArea { get; set; }

        /// <summary>True when any part of <see cref="UnderSampledArea"/> is the single-point ESTIMATE
        /// rather than a trapezoid. It rides with the number into the CSV.</summary>
        public bool UnderSampledIsEstimate { get; set; }

        /// <summary>The representative anchor's isotope envelope, i.e. the same number the fraction
        /// method sums. Equal to <see cref="NativePeptideQuant.EnvelopeSum"/> for this peptide.</summary>
        public double Envelope { get; set; }

        /// <summary>The identified-reference envelope model estimate used for de novo recovery. It is not
        /// an empirical calibration; the directly observed six-window <see cref="Envelope"/> remains a
        /// separate diagnostic.</summary>
        public double ReferenceCalibratedEnvelope { get; set; }

        /// <summary>The family (its UserID) this peptide carries, or null when none was attributed.</summary>
        public int? FamilyUserId { get; set; }

        public double AreaAt(int i) => Areas != null && i >= 0 && i < Areas.Length ? Areas[i] : 0.0;

        /// <summary>How many isotopes were actually credited: the count of area_mN strictly greater than
        /// zero over the six-isotope basis. The value the isotope-count histogram bins, 1..6. Most
        /// peptides do not reach the ceiling, which is the point that histogram makes: the crediting is
        /// EMPIRICAL, so an isotope that is not there is simply not credited.</summary>
        public int IsotopesCredited => AnalyticsBuilder.CountIsotopesCredited(Areas);
    }

    /// <summary>
    /// Everything the analytics screen reads, computed once from the loaded project's quant rows and its
    /// manifest: the per-peptide table (deduped, decorated) and the per-run and grouped native fractions
    /// of the TIC (Q3), reused, not recomputed.
    /// </summary>
    public sealed class AnalyticsData
    {
        public List<AnalyticsPeptide> Peptides { get; } = new List<AnalyticsPeptide>();

        /// <summary>The per-run native fractions (Q3), in run order. Never carries the grouped row; that
        /// is <see cref="Grouped"/>.</summary>
        public List<NativeRunFraction> Fractions { get; } = new List<NativeRunFraction>();

        /// <summary>The pooled fraction over every run, or null when there were no runs.</summary>
        public NativeRunFraction? Grouped { get; set; }

        /// <summary>The largest peptide mass in the table, or 0 when empty. The mass axis ceiling.</summary>
        public double MaxMass => Peptides.Count == 0 ? 0 : Peptides.Max(p => p.Mass);

        public int IdentifiedCount => Peptides.Count(p => p.Side == NativeQuantFractions.SideIdentified);
        public int DeNovoCount => Peptides.Count(p => p.Side == NativeQuantFractions.SideDeNovo);
    }

    /// <summary>
    /// Builds the analytics table from the Q2 store, reusing the Q3 dedup so the numbers on the graphs
    /// are the numbers in the paper. The mass and isotope-count derivations are pure and are exposed so
    /// they can be checked without a window.
    /// </summary>
    public static class AnalyticsBuilder
    {
        /// <summary>The mass of a proton in daltons, the value the m/z-to-mass inversion subtracts once
        /// per charge. The same constant the rest of the pipeline uses for a singly-charged offset.</summary>
        public const double ProtonMass = 1.007276;

        /// <summary>The neutral monoisotopic mass of a precursor from its m/z and charge:
        /// mass = (m/z - proton) * charge. A charge of zero cannot be inverted and yields 0 rather than a
        /// division that never happens, so a malformed row does not poison the axis.</summary>
        public static double MonoisotopicMass(double mz, int charge) =>
            charge <= 0 ? 0.0 : (mz - ProtonMass) * charge;

        /// <summary>How many of the isotope slots carried signal: the count of areas strictly greater
        /// than zero, over the go-forward basis. A peptide with only M+0 and M+1 credited gives 2.</summary>
        public static int CountIsotopesCredited(IReadOnlyList<double>? areas)
        {
            if (areas == null) return 0;
            int n = 0;
            for (int i = 0; i < areas.Count && i < IsotopeBasis; i++)
                if (areas[i] > 0) n++;
            return n;
        }

        /// <summary>The go-forward isotope basis the store credits, six (M+0..M+5), the same ceiling as
        /// <see cref="DeNovoXicExtractor.DefaultEnvelopeIsotopeCount"/>. A CEILING and not a floor: an
        /// absent isotope contributes nothing, no averagine fills it in, and the histogram bins 1..6.</summary>
        public const int IsotopeBasis = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount;

        /// <summary>
        /// The per-peptide analytics table. The peptide identities, their M+0 and their envelope come
        /// straight from <see cref="NativeQuantFractions.DedupToPeptides"/> - the SAME collapse the paper
        /// numbers use - so the table cannot drift from the recovery figures. Each representative is then
        /// joined back to the store row it was chosen from (the largest-M+0 anchor of its key) to recover
        /// the per-isotope areas, the precursor m/z and the family, which the fraction method does not
        /// carry. The join is by the identical key and the identical M+0, so it lands on the very anchor
        /// the collapse kept.
        /// </summary>
        public static List<AnalyticsPeptide> BuildPeptides(IEnumerable<QuantRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var rowList = rows.Where(r => r != null).ToList();

            // The store rows grouped by the SAME coarse key the dedup uses. Representative identity is
            // matched below from the complete deterministic tie-break tuple; input order is irrelevant.
            var byKey = new Dictionary<string, List<QuantRow>>(StringComparer.Ordinal);
            foreach (QuantRow r in rowList)
            {
                // The SAME admission rule the dedup uses, kept in lockstep with it: a row whose M+0 is a
                // sentinel is admitted when it carries under-sampled ion current, so an identified-but-not-
                // quantified peptide still finds its representative row here instead of landing in the
                // table with no m/z and a mass of zero.
                if (r.AreaM0 <= DeNovoXicExtractor.NoCluster && !(r.UnderSampledArea > 0)) continue;
                string key = KeyOf(DeNovoReconciler.NormalizeRunName(r.Run ?? string.Empty),
                                    r.Side ?? string.Empty, r.Sequence ?? string.Empty, r.Charge);
                if (!byKey.TryGetValue(key, out List<QuantRow>? list)) byKey[key] = list = new List<QuantRow>();
                list.Add(r);
            }

            var peptides = new List<AnalyticsPeptide>();
            List<NativePeptideQuant> nativePeptides = NativeQuantFractions.DedupToPeptides(rowList);
            ReferenceEnvelopeEstimator.Apply(nativePeptides);
            foreach (NativePeptideQuant p in nativePeptides)
            {
                string key = KeyOf(p.Run, p.Side, p.Sequence ?? string.Empty, p.Charge);
                QuantRow? rep = null;
                if (byKey.TryGetValue(key, out List<QuantRow>? group))
                {
                    // DedupToPeptides carries every numeric field of its selected anchor. Match all of
                    // them bit-for-bit, plus its canonical sequence, before the final stable scan tie.
                    // This is stronger than the former "first row with this M+0" join: equal-M+0 anchors
                    // with different envelopes, isotope areas or FamilyUserId now reach the analytics
                    // table identically under every store-row permutation.
                    string canonical = PeptidoformIdentity.Canonicalize(
                        p.Sequence ?? string.Empty, foldIsoleucineLeucine: false);
                    rep = group.Where(r =>
                            SameDoubleBits(r.PrecursorMz, p.PrecursorMz)
                            && SameDoubleBits(r.QuantifiedAreaM0, p.AreaM0)
                            && SameDoubleBits(r.EnvelopeSum, p.EnvelopeSum)
                            && SameDoubleBits(r.UnderSampledArea > 0 ? r.UnderSampledArea : 0.0,
                                              p.UnderSampledArea)
                            && (r.UnderSampledIsEstimate && r.UnderSampledArea > 0)
                                == p.UnderSampledIsEstimate
                            && string.Equals(PeptidoformIdentity.Canonicalize(
                                    r.Sequence ?? string.Empty, foldIsoleucineLeucine: false),
                                canonical, StringComparison.Ordinal))
                        .OrderBy(r => r.Sequence, StringComparer.Ordinal)
                        .ThenBy(r => r.Scan)
                        .FirstOrDefault();

                    // Defensive fallback for a hand-authored/legacy row whose serialized floating-point
                    // representation was altered. It remains deterministic and still prefers the exact
                    // representative m/z and M+0 before any tolerance-based recovery.
                    rep ??= group.Where(r => SameDoubleBits(r.PrecursorMz, p.PrecursorMz)
                                             && SameDoubleBits(r.QuantifiedAreaM0, p.AreaM0))
                        .OrderByDescending(r => r.EnvelopeSum)
                        .ThenByDescending(r => r.UnderSampledArea > 0 ? r.UnderSampledArea : 0.0)
                        .ThenBy(r => r.UnderSampledIsEstimate && r.UnderSampledArea > 0)
                        .ThenBy(r => r.Sequence, StringComparer.Ordinal)
                        .ThenBy(r => r.Scan)
                        .FirstOrDefault();

                    rep ??= group
                        .OrderBy(r => Math.Abs(MonoisotopicMass(r.PrecursorMz, r.Charge) - p.MonoisotopicMass))
                        .ThenByDescending(r => r.QuantifiedAreaM0)
                        .ThenByDescending(r => r.EnvelopeSum)
                        .ThenBy(r => r.Sequence, StringComparer.Ordinal)
                        .ThenBy(r => r.Scan)
                        .FirstOrDefault();
                }

                double[] areas = PadAreas(rep?.IsotopeAreas);

                peptides.Add(new AnalyticsPeptide
                {
                    Run = p.Run,
                    Side = p.Side,
                    Sequence = p.Sequence ?? string.Empty,
                    Charge = p.Charge,
                    PrecursorMz = rep?.PrecursorMz ?? 0.0,
                    Mass = MonoisotopicMass(rep?.PrecursorMz ?? 0.0, p.Charge),
                    // The M+0 and the envelope are the collapse's own numbers, so area_m0 and the envelope
                    // column reproduce the paper path exactly rather than a re-read of the anchor.
                    Areas = ReplaceM0(areas, p.AreaM0),
                    Envelope = p.EnvelopeSum,
                    ReferenceCalibratedEnvelope = p.ReferenceCalibratedEnvelope,
                    // The collapse's own numbers again, so the CSV's under-sampled column is the very
                    // number the recovery bar's identified-but-not-quantified block is summed from.
                    UnderSampledArea = p.UnderSampledArea,
                    UnderSampledIsEstimate = p.UnderSampledIsEstimate,
                    FamilyUserId = rep?.FamilyUserId,
                });
            }

            return peptides;
        }

        /// <summary>
        /// The full analytics view: the deduped table and the Q3 per-run and grouped fractions, both from
        /// the store and the manifest's per-run TIC. A store with no TIC (an unmanifested project) still
        /// yields a table; its fractions divide by zero and read zero, which the recovery chart says.
        /// </summary>
        public static AnalyticsData Build(IEnumerable<QuantRow> rows, IReadOnlyDictionary<string, double>? perRunTic)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var data = new AnalyticsData();
            data.Peptides.AddRange(BuildPeptides(rows));

            var tic = perRunTic ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            List<NativeRunFraction> fr = NativeQuantFractions.Compute(rows, tic);
            data.Fractions.AddRange(fr);
            if (fr.Count > 0) data.Grouped = NativeQuantFractions.Grouped(fr);

            return data;
        }

        /// <summary>The coarse part of the dedup key: normalized run, side, canonical non-I/L-folded
        /// peptidoform and charge. The representative join additionally checks precursor mass, mirroring
        /// <see cref="NativeQuantFractions.DedupToPeptides"/>.</summary>
        private static string KeyOf(string run, string side, string seq, int charge) =>
            run + "\u0001" + side + "\u0001" +
            PeptidoformIdentity.Canonicalize(seq, foldIsoleucineLeucine: false) + "\u0001" +
            charge.ToString(CultureInfo.InvariantCulture);

        private static bool SameDoubleBits(double first, double second) =>
            BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);

        private static double[] PadAreas(double[]? areas)
        {
            var padded = new double[IsotopeBasis];
            if (areas != null)
                for (int i = 0; i < IsotopeBasis && i < areas.Length; i++)
                    padded[i] = areas[i] > 0 ? areas[i] : 0.0;   // a no-cluster sentinel is not an area
            return padded;
        }

        private static double[] ReplaceM0(double[] areas, double m0)
        {
            var copy = (double[])areas.Clone();
            if (copy.Length > 0) copy[0] = m0;
            return copy;
        }
    }

    /// <summary>
    /// The analytics tables as CSV, both written invariantly with a dot decimal so any program reads them:
    /// the per-peptide table the graphs are built from, and the per-run and grouped fraction summary.
    /// </summary>
    public static class AnalyticsCsv
    {
        /// <summary>The per-peptide table columns, in order. The header written and the header a reader
        /// expects are this one array so the two cannot drift.</summary>
        public static readonly string[] PeptideColumns =
        {
            "run", "side", "sequence", "charge", "mass",
            "area_m0", "area_m1", "area_m2", "area_m3", "area_m4", "area_m5",
            "envelope_unadjusted", "envelope_reference_calibrated", "isotopes_credited",
            "under_sampled", "under_sampled_estimate", "family_userid",
        };

        /// <summary>The fixed head of <see cref="SummaryColumns"/>, before the per-charge tail. Declared
        /// BEFORE the field that reads it: static initializers run in declaration order, and the other way
        /// round this array is still null when the tail is built.</summary>
        private static readonly string[] SummaryColumnsHead =
        {
            "run", "grouped", "tic",
            "identified_peptides", "identified_m0", "identified_envelope",
            "denovo_peptides", "denovo_envelope",
            "identified_fraction_m0", "identified_fraction_envelope",
            "denovo_fraction_envelope_unadjusted", "recovered_fraction_envelope_unadjusted",
            "denovo_envelope_reference_calibrated", "denovo_fraction_reference_calibrated",
            "recovered_fraction_reference_calibrated", "recovered_fraction_m0",
            "denovo_suppressed_cross_side_overlaps",
            // The identified-but-not-quantified block, per side and pooled. under_sampled_estimate flags
            // the single-point ESTIMATE. These columns are not combined with the independent detector into
            // a physical partition of TIC.
            "identified_under_sampled", "denovo_under_sampled",
            "identified_under_sampled_peptides", "denovo_under_sampled_peptides",
            "under_sampled_fraction", "under_sampled_estimate",
            "resolved_isotope_series_fraction_diagnostic",
            // The uncalibrated detector diagnostic split by charge. z >= 2 is more specific than z == 1;
            // neither subset has a peptide-level false-discovery calibration. The two areas sum exactly to
            // detected_peptidelike_area.
            "detected_peptidelike_area",
            "detected_area_z_ge_2", "detected_area_z_eq_1",
            "detected_fraction_z_ge_2", "detected_fraction_z_eq_1",
            "detected_features_z_ge_2", "detected_features_z_eq_1",
        };

        /// <summary>The per-run and grouped summary columns, in order. The per-charge tail is generated
        /// from the detector's own charge range, so a change there cannot leave the file writing a column
        /// set the header does not name.</summary>
        public static readonly string[] SummaryColumns = BuildSummaryColumns();

        private static string[] BuildSummaryColumns()
        {
            var columns = new List<string>(SummaryColumnsHead);
            // The whole per-charge breakdown, one area and one count column per charge the detector can
            // assign, so a reader is never limited to the two-group rollup.
            for (int z = Ms1EnvelopeDetector.MinCharge; z <= Ms1EnvelopeDetector.MaxCharge; z++)
            {
                string suffix = z.ToString(CultureInfo.InvariantCulture);
                columns.Add("detected_area_z" + suffix);
                columns.Add("detected_features_z" + suffix);
            }
            return columns.ToArray();
        }

        public static string BuildPeptideCsv(IEnumerable<AnalyticsPeptide> peptides)
        {
            if (peptides == null) throw new ArgumentNullException(nameof(peptides));

            var sb = new StringBuilder();
            sb.Append(string.Join(",", PeptideColumns)).Append('\n');

            foreach (AnalyticsPeptide p in peptides)
            {
                sb.Append(Field(p.Run)).Append(',');
                sb.Append(Field(p.Side)).Append(',');
                sb.Append(Field(p.Sequence)).Append(',');
                sb.Append(Int(p.Charge)).Append(',');
                sb.Append(Num(p.Mass)).Append(',');
                sb.Append(Num(p.AreaAt(0))).Append(',');
                sb.Append(Num(p.AreaAt(1))).Append(',');
                sb.Append(Num(p.AreaAt(2))).Append(',');
                sb.Append(Num(p.AreaAt(3))).Append(',');
                sb.Append(Num(p.AreaAt(4))).Append(',');
                sb.Append(Num(p.AreaAt(5))).Append(',');
                sb.Append(Num(p.Envelope)).Append(',');
                sb.Append(Num(p.ReferenceCalibratedEnvelope)).Append(',');
                sb.Append(Int(p.IsotopesCredited)).Append(',');
                sb.Append(Num(p.UnderSampledArea)).Append(',');
                sb.Append(p.UnderSampledIsEstimate ? "1" : "0").Append(',');
                sb.Append(p.FamilyUserId.HasValue ? Int(p.FamilyUserId.Value) : string.Empty);
                sb.Append('\n');
            }

            return sb.ToString();
        }

        public static string BuildSummaryCsv(IEnumerable<NativeRunFraction> fractions, NativeRunFraction? grouped)
        {
            if (fractions == null) throw new ArgumentNullException(nameof(fractions));

            var sb = new StringBuilder();
            sb.Append(string.Join(",", SummaryColumns)).Append('\n');

            foreach (NativeRunFraction f in fractions) AppendSummaryRow(sb, f);
            if (grouped != null) AppendSummaryRow(sb, grouped);

            return sb.ToString();
        }

        private static void AppendSummaryRow(StringBuilder sb, NativeRunFraction f)
        {
            sb.Append(Field(f.Run)).Append(',');
            sb.Append(f.IsGrouped ? "1" : "0").Append(',');
            sb.Append(Num(f.Tic)).Append(',');
            sb.Append(Int(f.IdentifiedPeptides)).Append(',');
            sb.Append(Num(f.IdentifiedAreaM0)).Append(',');
            sb.Append(Num(f.IdentifiedEnvelope)).Append(',');
            sb.Append(Int(f.DeNovoPeptides)).Append(',');
            sb.Append(Num(f.DeNovoEnvelope)).Append(',');
            sb.Append(Num(f.IdentifiedFractionM0)).Append(',');
            sb.Append(Num(f.IdentifiedFractionEnvelope)).Append(',');
            sb.Append(Num(f.DeNovoFractionEnvelope)).Append(',');
            sb.Append(Num(f.RecoveredFractionEnvelope)).Append(',');
            sb.Append(Num(f.DeNovoEnvelopeReferenceCalibrated)).Append(',');
            sb.Append(Num(f.DeNovoFractionReferenceCalibrated)).Append(',');
            sb.Append(Num(f.RecoveredFractionReferenceCalibrated)).Append(',');
            sb.Append(Num(f.RecoveredFractionM0)).Append(',');
            sb.Append(Int(f.DeNovoSuppressedCrossSideOverlaps)).Append(',');
            sb.Append(Num(f.IdentifiedUnderSampled)).Append(',');
            sb.Append(Num(f.DeNovoUnderSampled)).Append(',');
            sb.Append(Int(f.IdentifiedUnderSampledPeptides)).Append(',');
            sb.Append(Int(f.DeNovoUnderSampledPeptides)).Append(',');
            sb.Append(Num(f.UnderSampledFraction)).Append(',');
            sb.Append(f.UnderSampledIsEstimate ? "1" : "0").Append(',');
            sb.Append(Num(f.DetectedFractionPeptideLike)).Append(',');

            // The detected level and its charge split. The two areas sum exactly to the total by
            // construction (the detector adds the parts to make it), so a reader can check the arithmetic
            // in the file rather than trusting the caption.
            sb.Append(Num(f.DetectedPeptideLikeArea)).Append(',');
            sb.Append(Num(f.DetectedAreaMultiplyCharged)).Append(',');
            sb.Append(Num(f.DetectedAreaSinglyCharged)).Append(',');
            sb.Append(Num(f.DetectedFractionMultiplyCharged)).Append(',');
            sb.Append(Num(f.DetectedFractionSinglyCharged)).Append(',');
            sb.Append(Int(f.DetectedCountMultiplyCharged)).Append(',');
            sb.Append(Int(f.DetectedCountSinglyCharged));

            for (int z = Ms1EnvelopeDetector.MinCharge; z <= Ms1EnvelopeDetector.MaxCharge; z++)
            {
                double area = f.DetectedAreaByCharge != null && z < f.DetectedAreaByCharge.Length
                    ? f.DetectedAreaByCharge[z] : 0.0;
                int count = f.DetectedCountByCharge != null && z < f.DetectedCountByCharge.Length
                    ? f.DetectedCountByCharge[z] : 0;
                sb.Append(',').Append(Num(area)).Append(',').Append(Int(count));
            }

            sb.Append('\n');
        }

        private static string Num(double v) => CsvFormatting.RoundTrip(v);

        private static string Int(int v) => CsvFormatting.Number(v);

        /// <summary>RFC 4180 quoting: a field carrying a comma, a quote or a newline is wrapped in quotes
        /// with inner quotes doubled. Sequences and run names should never contain them; this is a guard.</summary>
        private static string Field(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0
                && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
