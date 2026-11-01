using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// Resolves a multi-source de novo union at the precursor grain. One run/scan/charge/rounded-m/z
    /// precursor owns one XIC, even when several tools or several sequence candidates claim it.
    ///
    /// Source confidence scales are never compared. A configured source priority decides between
    /// different sources; confidence is used only between candidates from the SAME source. Every claim is
    /// retained in <see cref="PeaksDeNovoRow.SourceEvidence"/> and later persisted with the quant row.
    /// </summary>
    public static class DeNovoUnionResolver
    {
        public const double PrecursorTolerancePpm = 20.0;

        public const string Method =
            "one-XIC-per-(normalized-run,scan,charge,isotope-error-aware 20-ppm precursor cluster); " +
            "m/z-sorted clusters compare each candidate with the cluster-opening m/z, directly or after " +
            "removing a +-1/+-2 (M, M+-1, M+-2) monoisotopic-peak selection error (no transitive ppm " +
            "chaining); source-priority; within-source confidence-desc; sequence/path ordinal; all source " +
            "claims retained";

        public static List<PeaksDeNovoRow> Resolve(
            IEnumerable<PeaksDeNovoRow> rows,
            IReadOnlyList<DeNovoSourceSelection> selections)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (selections == null) throw new ArgumentNullException(nameof(selections));

            var priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DeNovoSourceSelection s in selections.OrderBy(s => s.Priority))
                if (!priority.ContainsKey(s.Source.Id)) priority[s.Source.Id] = s.Priority;

            var resolved = new List<PeaksDeNovoRow>();

            var spectra = rows.GroupBy(SpectrumKey, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (IGrouping<string, PeaksDeNovoRow> spectrum in spectra)
            {
                foreach (List<PeaksDeNovoRow> candidates in PrecursorClusters(spectrum))
                {
                PeaksDeNovoRow winner = candidates
                    .OrderBy(r => SourcePriority(r.SourceId, priority))
                    // If callers deliberately give two sources the same priority, stable source id (not
                    // incomparable confidence values) breaks that tie first.
                    .ThenBy(r => r.SourceId, StringComparer.Ordinal)
                    // Confidence comparisons are now within one source only.
                    .ThenByDescending(r => r.ConfidenceValue)
                    .ThenBy(r => DeNovoReconciler.NormalizeSequence(r.Peptide), StringComparer.Ordinal)
                    .ThenBy(r => r.Peptide, StringComparer.Ordinal)
                    .ThenBy(r => r.InputFilePath, StringComparer.Ordinal)
                    .First();

                winner.SourceEvidence = candidates
                    .SelectMany(EvidenceOf)
                    .GroupBy(EvidenceKey, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .OrderBy(e => SourcePriority(e.SourceId, priority))
                    .ThenByDescending(e => e.ConfidenceValue)
                    .ThenBy(e => e.Sequence, StringComparer.Ordinal)
                    .ThenBy(e => e.InputFilePath, StringComparer.Ordinal)
                    .ToList();

                resolved.Add(winner);
                }
            }

            return resolved;
        }

        public static string PrecursorKey(PeaksDeNovoRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            return DeNovoReconciler.NormalizeRunName(row.RunName).ToUpperInvariant() + "|" +
                   row.Scan.ToString(CultureInfo.InvariantCulture) + "|" +
                   row.Charge.ToString(CultureInfo.InvariantCulture) + "|" +
                   Math.Round(row.Mz, DeNovoXicExtractor.MzDecimals)
                       .ToString("F" + DeNovoXicExtractor.MzDecimals, CultureInfo.InvariantCulture);
        }

        private static string SpectrumKey(PeaksDeNovoRow row) =>
            DeNovoReconciler.NormalizeRunName(row.RunName).ToUpperInvariant() + "|" +
            row.Scan.ToString(CultureInfo.InvariantCulture) + "|" +
            row.Charge.ToString(CultureInfo.InvariantCulture);

        /// <summary>Deterministic, non-chaining, isotope-error-aware windows. Every group is sorted by m/z
        /// and a new cluster opens when the candidate is not equivalent to the m/z that opened the current
        /// one, where equivalence is the direct 20-ppm agreement OR agreement after removing a +-1/+-2
        /// monoisotopic-peak selection error (M, M+-1, M+-2 of one precursor at the group's shared charge).
        /// Comparing with the opener rather than the previous candidate prevents a dense ladder from merging
        /// an arbitrarily wide precursor range, and the fixed isotope-offset search order keeps the pairing
        /// one-to-one regardless of input order.</summary>
        private static List<List<PeaksDeNovoRow>> PrecursorClusters(IEnumerable<PeaksDeNovoRow> rows)
        {
            List<PeaksDeNovoRow> sorted = rows
                .OrderBy(r => r.Mz)
                .ThenBy(r => r.SourceId, StringComparer.Ordinal)
                .ThenBy(r => r.Peptide, StringComparer.Ordinal)
                .ThenBy(r => r.InputFilePath, StringComparer.Ordinal)
                .ToList();
            var clusters = new List<List<PeaksDeNovoRow>>();
            List<PeaksDeNovoRow>? current = null;
            double openingMz = 0;
            int openingCharge = 0;

            foreach (PeaksDeNovoRow row in sorted)
            {
                bool outside = current == null || !(openingMz > 0) || !(row.Mz > 0)
                    || !PeptidoformIdentity.PrecursorMzEquivalent(openingMz, row.Mz, openingCharge,
                        out _, PeptidoformIdentity.MaxIsotopeOffset, PrecursorTolerancePpm);
                if (outside)
                {
                    current = new List<PeaksDeNovoRow>();
                    clusters.Add(current);
                    openingMz = row.Mz;
                    openingCharge = row.Charge;
                }
                current!.Add(row);
            }
            return clusters;
        }

        private static int SourcePriority(string sourceId, IReadOnlyDictionary<string, int> priority) =>
            priority.TryGetValue(sourceId ?? string.Empty, out int p) ? p : int.MaxValue;

        private static IEnumerable<DeNovoEvidence> EvidenceOf(PeaksDeNovoRow row)
        {
            List<DeNovoEvidence>? evidence = row.SourceEvidence;
            if (evidence != null && evidence.Count > 0) return evidence;
            return new[]
            {
                new DeNovoEvidence
                {
                    SourceId = row.SourceId,
                    SourceDisplayName = row.SourceDisplayName,
                    ConfidenceField = row.ConfidenceField,
                    ConfidenceValue = row.ConfidenceValue,
                    Sequence = row.Peptide,
                    InputFilePath = row.InputFilePath,
                    InputFileSha256 = row.InputFileSha256,
                },
            };
        }

        private static string EvidenceKey(DeNovoEvidence e) =>
            (e.SourceId ?? string.Empty) + "\u001f" +
            (e.ConfidenceField ?? string.Empty) + "\u001f" +
            e.ConfidenceValue.ToString("R", CultureInfo.InvariantCulture) + "\u001f" +
            (e.Sequence ?? string.Empty) + "\u001f" +
            (e.InputFileSha256 ?? string.Empty);
    }
}
