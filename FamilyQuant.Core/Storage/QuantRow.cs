using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant
{
    /// <summary>
    /// One per-peptide quant row as it is STORED in a project: which side measured it, which run and
    /// precursor it belongs to, the credited isotope areas and their sum, and the family it was
    /// attributed to (null until an attribution pass sets it).
    ///
    /// This is the raw grain of phase Q2: one row per unique extracted precursor, isotope by isotope,
    /// with nothing derived from it. The fractions, partitions and family rollups that read these
    /// numbers are a later Q-phase and are deliberately NOT computed here.
    /// </summary>
    public sealed class QuantRow
    {
        /// <summary>"identified" for a peptide the database search found, "denovo" for one only PEAKS
        /// sequenced. The two are stored together and told apart by this, never merged.</summary>
        public string Side { get; set; } = string.Empty;

        /// <summary>The run this precursor was extracted from, normalized by
        /// <see cref="DeNovo.DeNovoReconciler.NormalizeRunName"/> so "x.raw" and "x" denote one run.</summary>
        public string Run { get; set; } = string.Empty;

        /// <summary>The MS2 scan that anchored the extraction.</summary>
        public int Scan { get; set; }

        /// <summary>The precursor m/z handed to the extractor: already rounded to
        /// <see cref="DeNovo.DeNovoXicExtractor.MzDecimals"/> decimals, so it is the exact key the
        /// window was selected by.</summary>
        public double PrecursorMz { get; set; }

        public int Charge { get; set; }

        public string Sequence { get; set; } = string.Empty;

        /// <summary>PatternLab/SEPro's primary PSM score (XCorr) for an identified precursor. Null on
        /// de novo rows and on projects written before this field was persisted. This is an
        /// identification score, never a quantitation value.</summary>
        public double? PatternLabXScore { get; set; }

        /// <summary>Source-aware evidence for a de novo precursor. The first item is the deterministic
        /// representative used to label the one XIC; later items are alternate source/sequence claims on
        /// that same precursor. Empty on identified rows and legacy projects.</summary>
        public List<DeNovo.DeNovoEvidence> DeNovoEvidence { get; set; } =
            new List<DeNovo.DeNovoEvidence>();

        public DeNovo.DeNovoEvidence? PrimaryDeNovoEvidence =>
            DeNovoEvidence != null && DeNovoEvidence.Count > 0 ? DeNovoEvidence[0] : null;

        /// <summary>The credited isotope areas, index 0 = M+0, up to the manifest's isotope count. A row
        /// extracted at the go-forward 6-isotope basis carries [M+0 .. M+5]; the
        /// <see cref="DeNovo.DeNovoXicExtractor.NoCluster"/> sentinel survives untouched, because -1 and
        /// 0 mean different things, and so does
        /// <see cref="DeNovo.DeNovoXicExtractor.UnderSampled"/> (-2), which means a third thing again: a
        /// cluster was found and the minimum-points rule declined to quantify it.</summary>
        public double[] IsotopeAreas { get; set; } = Array.Empty<double>();

        /// <summary>
        /// The ion current of this row's credited isotopes that a cluster WAS found for but that the
        /// minimum-points rule refused to quantify, in the same intensity-times-minutes units as the
        /// areas. Zero on every row of a store computed at
        /// <see cref="DeNovo.DeNovoXicExtractor.LegacyMinPointsForQuant"/>, and zero on a store written
        /// before this column existed.
        ///
        /// It is stored, and it keeps its row alive even when every isotope was refused, so that an
        /// identified peptide which was not quantified cannot disappear into the unassigned remainder.
        /// </summary>
        public double UnderSampledArea { get; set; }

        /// <summary>True when any part of <see cref="UnderSampledArea"/> is the single-point ESTIMATE
        /// (intensity times the run's median MS1 scan spacing) rather than a trapezoid. It rides with the
        /// number so nothing downstream can present an estimate as a measurement.</summary>
        public bool UnderSampledIsEstimate { get; set; }

        /// <summary>The sum of the credited isotopes with the no-cluster sentinel clamped, captured at
        /// extraction. Stored rather than recomputed so the table round-trips exactly what was measured,
        /// including for a reader that credits a different isotope count.</summary>
        public double EnvelopeSum { get; set; }

        /// <summary>The family (its UserID) this peptide was attributed to, or null when no attribution
        /// has been made. Q2 leaves it null: attributing a peptide to a family is a Q3 concern and this
        /// column exists so the store can already carry it round-trip.</summary>
        public int? FamilyUserId { get; set; }

        private double AreaAt(int i) => IsotopeAreas != null && i < IsotopeAreas.Length ? IsotopeAreas[i] : 0.0;

        /// <summary>M+0, the monoisotopic area, i.e. <see cref="IsotopeAreas"/>[0]. The number that
        /// reproduces PatternLab's stored quant bit for bit. It carries the sentinels through untouched,
        /// so a reader that means "the area that counts" wants <see cref="QuantifiedAreaM0"/>.</summary>
        public double AreaM0 => AreaAt(0);

        /// <summary>M+0 as a QUANTITY: the area when there is one, and 0 for either sentinel, because
        /// neither -1 (no cluster) nor -2 (under-sampled) is an area and summing them would subtract ion
        /// current that does not exist. On every row a pre-existing store holds, this is AreaM0 itself,
        /// bit for bit.</summary>
        public double QuantifiedAreaM0 => AreaM0 > 0 ? AreaM0 : 0.0;

        /// <summary>
        /// One row per extracted precursor of a run, on one side, built from the extractor's own results.
        /// Failures and rows carrying no ion current at all are dropped: a row that measured nothing is
        /// not a quantity to store, and the -1 sentinel is not an area.
        ///
        /// A row whose M+0 was UNDER-SAMPLED is kept when it carries under-sampled ion current, even
        /// though its quantified M+0 is zero. Dropping it would be the whole defect this accounting
        /// exists to prevent: the peptide was identified and detected, and the current it carries has to
        /// land in the identified-but-not-quantified block rather than migrate into the remainder.
        /// </summary>
        public static List<QuantRow> FromResults(string side, string run,
            IEnumerable<DeNovo.DeNovoXicResult> results, int? familyUserId = null)
        {
            if (side == null) throw new ArgumentNullException(nameof(side));
            if (results == null) throw new ArgumentNullException(nameof(results));

            var rows = new List<QuantRow>();
            foreach (DeNovo.DeNovoXicResult r in results)
            {
                if (r == null || r.Failure != null) continue;
                if (r.AreaM0 <= DeNovo.DeNovoXicExtractor.NoCluster && !(r.UnderSampledArea > 0)) continue;

                rows.Add(new QuantRow
                {
                    Side = side,
                    Run = string.IsNullOrEmpty(run) ? r.RunName : run,
                    Scan = r.ScanNoMS2,
                    PrecursorMz = r.PrecursorMz,
                    Charge = r.Charge,
                    Sequence = r.Tag ?? string.Empty,
                    // A copy, not the extractor's array: the result list is released after this and the
                    // stored row must not alias a buffer that will be collected with it.
                    IsotopeAreas = (double[])(r.IsotopeAreas ?? Array.Empty<double>()).Clone(),
                    EnvelopeSum = r.EnvelopeSum,
                    UnderSampledArea = r.UnderSampledArea,
                    UnderSampledIsEstimate = r.UnderSampledIsEstimate,
                    FamilyUserId = familyUserId,
                });
            }
            return rows;
        }

        /// <summary>Attaches source identity, confidence field/value and input provenance to de novo
        /// quant rows after extraction. Matching uses the exact extraction key, so alternate sequence
        /// claims never create a second XIC.</summary>
        public static void AttachDeNovoEvidence(IEnumerable<QuantRow> quantRows,
                                                 IEnumerable<DeNovo.PeaksDeNovoRow> observations)
        {
            if (quantRows == null) throw new ArgumentNullException(nameof(quantRows));
            if (observations == null) throw new ArgumentNullException(nameof(observations));

            var byKey = observations
                .GroupBy(r => DeNovo.DeNovoXicCache.KeyFor(r.Scan,
                    Math.Round(r.Mz, DeNovo.DeNovoXicExtractor.MzDecimals), r.Charge), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (QuantRow q in quantRows)
            {
                string key = DeNovo.DeNovoXicCache.KeyFor(q.Scan, q.PrecursorMz, q.Charge);
                if (!byKey.TryGetValue(key, out DeNovo.PeaksDeNovoRow? source)) continue;

                IEnumerable<DeNovo.DeNovoEvidence> evidence = source.SourceEvidence != null
                    && source.SourceEvidence.Count > 0
                    ? source.SourceEvidence
                    : new[]
                    {
                        new DeNovo.DeNovoEvidence
                        {
                            SourceId = source.SourceId,
                            SourceDisplayName = source.SourceDisplayName,
                            ConfidenceField = source.ConfidenceField,
                            ConfidenceValue = source.ConfidenceValue,
                            Sequence = source.Peptide,
                            InputFilePath = source.InputFilePath,
                            InputFileSha256 = source.InputFileSha256,
                        },
                    };

                q.DeNovoEvidence = evidence.Select(e => new DeNovo.DeNovoEvidence
                {
                    SourceId = e.SourceId,
                    SourceDisplayName = e.SourceDisplayName,
                    ConfidenceField = e.ConfidenceField,
                    ConfidenceValue = e.ConfidenceValue,
                    Sequence = e.Sequence,
                    InputFilePath = e.InputFilePath,
                    InputFileSha256 = e.InputFileSha256,
                }).ToList();
            }
        }

        /// <summary>Attaches PatternLab's primary PSM score to identified quant rows using the exact
        /// extraction key (scan, rounded precursor m/z and reported charge). Run matching is performed
        /// by the caller, so no score can cross from one acquisition into another.</summary>
        public static void AttachPatternLabScores(IEnumerable<QuantRow> quantRows,
                                                  IEnumerable<DeNovo.PatternLabPsm> psms)
        {
            if (quantRows == null) throw new ArgumentNullException(nameof(quantRows));
            if (psms == null) throw new ArgumentNullException(nameof(psms));

            var byKey = psms
                .GroupBy(p => DeNovo.DeNovoXicCache.KeyFor(p.ScanNumber,
                    Math.Round(p.PrecursorMz, DeNovo.DeNovoXicExtractor.MzDecimals), p.ChargeState),
                    StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.XCorr)
                    .ThenBy(p => p.Sequence, StringComparer.Ordinal).First(), StringComparer.Ordinal);

            foreach (QuantRow q in quantRows)
            {
                if (!string.Equals(q.Side, DeNovo.NativeQuantFractions.SideIdentified,
                    StringComparison.Ordinal)) continue;
                string key = DeNovo.DeNovoXicCache.KeyFor(q.Scan, q.PrecursorMz, q.Charge);
                if (byKey.TryGetValue(key, out DeNovo.PatternLabPsm? psm))
                    q.PatternLabXScore = psm.XCorr;
            }
        }
    }
}
