using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// Regressions for the isotope-selection-aware precursor equivalence rule and its join call sites. The
    /// within-peptide collapse (commit 164fa58) already merges the M, M+-1 and M+-2 copies of ONE peptide;
    /// these cover the JOIN/matching paths, where "same precursor" pairs calls across sources or sides. A
    /// genuine co-isolated neighbour must stay separate, and the pairing must be deterministic and
    /// one-to-one regardless of input order.
    /// </summary>
    internal static class IsotopePrecursorEquivalenceTests
    {
        public static void Run(Action<string, bool, string?> check)
        {
            double spacing = PeptidoformIdentity.Carbon13SpacingDa;

            // ---- direct m/z agreement -----------------------------------------------------------------
            check("isotope rule: identical reported m/z is a direct match with offset 0",
                PeptidoformIdentity.PrecursorMzEquivalent(500.25, 500.25, 2, out int d0) && d0 == 0,
                d0.ToString());
            check("isotope rule: a match within 20 ppm on the reported m/z is direct, not isotope-corrected",
                PeptidoformIdentity.PrecursorMzEquivalent(500.25, 500.25 + 500.25 * 5e-6, 2, out int d1)
                    && d1 == 0 && PeptidoformIdentity.MatchBasis(d1) == "direct",
                d1.ToString());

            // ---- M+1 and M+2 equivalence at z = 1,2,3,4 -----------------------------------------------
            foreach (int z in new[] { 1, 2, 3, 4 })
            {
                double baseMz = 400.0 + z;              // an arbitrary but positive base m/z
                double step = spacing / z;
                bool p1 = PeptidoformIdentity.PrecursorMzEquivalent(baseMz, baseMz + step, z, out int o1);
                check("isotope rule: M+1 selection is equivalent at z=" + z + " with offset -1",
                    p1 && o1 == -1 && PeptidoformIdentity.MatchBasis(o1) == "isotope-corrected",
                    o1.ToString());
                bool p2 = PeptidoformIdentity.PrecursorMzEquivalent(baseMz, baseMz + 2 * step, z, out int o2);
                check("isotope rule: M+2 selection is equivalent at z=" + z + " with offset -2",
                    p2 && o2 == -2, o2.ToString());
            }

            // ---- negative isotope offsets (M-1, M-2) --------------------------------------------------
            {
                double baseMz = 512.6;
                double step = spacing / 3;              // z = 3
                check("isotope rule: M-1 selection is equivalent with offset +1",
                    PeptidoformIdentity.PrecursorMzEquivalent(baseMz, baseMz - step, 3, out int oM1)
                        && oM1 == 1, "M-1");
                check("isotope rule: M-2 selection is equivalent with offset +2",
                    PeptidoformIdentity.PrecursorMzEquivalent(baseMz, baseMz - 2 * step, 3, out int oM2)
                        && oM2 == 2, "M-2");
            }

            // ---- same scan+charge but NON-isotope m/z stays separate ----------------------------------
            {
                double baseMz = 600.0;
                double step = spacing / 2;              // z = 2
                check("isotope rule: a genuine co-isolated neighbour half a C13 gap away is NOT equivalent",
                    !PeptidoformIdentity.PrecursorMzEquivalent(baseMz, baseMz + 0.5 * step, 2, out _),
                    "half-spacing neighbour");
                check("isotope rule: a neighbour beyond the +-2 window (M+3) is NOT equivalent",
                    !PeptidoformIdentity.PrecursorMzEquivalent(baseMz, baseMz + 3 * step, 2, out _),
                    "M+3 neighbour");
            }

            // ---- neutral-mass form: direct, M+1, M+2, and a non-isotope gap ----------------------------
            {
                double mass = 1200.6;
                check("isotope rule: neutral-mass M+1 is equivalent with offset -1",
                    PeptidoformIdentity.PrecursorMassEquivalent(mass, mass + spacing, out int n1) && n1 == -1,
                    n1.ToString());
                check("isotope rule: neutral-mass M+2 is equivalent with offset -2",
                    PeptidoformIdentity.PrecursorMassEquivalent(mass, mass + 2 * spacing, out int n2) && n2 == -2,
                    n2.ToString());
                check("isotope rule: a neutral mass 0.5 Da away is NOT equivalent",
                    !PeptidoformIdentity.PrecursorMassEquivalent(mass, mass + 0.5, out _), "0.5 Da");
            }

            // ---- cross-side same-peptidoform isotope-offset suppression -------------------------------
            {
                var identified = MakePeptide(NativeQuantFractions.SideIdentified, "PEPTIDEK", 2, 500.25);
                // A de novo call of the same peptidoform whose reported precursor is the M+1 selection.
                double denovoMz = 500.25 + spacing / 2;
                var deNovo = MakePeptide(NativeQuantFractions.SideDeNovo, "PEPTIDEK", 2, denovoMz);
                check("cross-side: an identified M+0 and a de novo M+1 of one peptidoform are equivalent",
                    NativeQuantFractions.IsCrossSideEquivalent(identified, deNovo), "M0 vs M1");

                var unrelated = MakePeptide(NativeQuantFractions.SideDeNovo, "PEPTIDEK", 2,
                    500.25 + 0.5 * (spacing / 2));
                check("cross-side: a non-isotope co-isolated de novo neighbour is NOT suppressed",
                    !NativeQuantFractions.IsCrossSideEquivalent(identified, unrelated), "half-spacing");
            }

            // ---- differing sequences share one isotope-equivalent precursor/XIC -----------------------
            // PEAKS reports M+0 with one sequence; DeepNovo reports the M+1 selection with a DIFFERENT
            // sequence. They must collapse into ONE precursor row that shares a single quant, while both
            // sequence claims remain visible and the basis is recorded as isotope-corrected.
            {
                var peaks = MakeRow(DeNovoSource.DenovoOnly, scan: 101, charge: 2, mz: 500.25,
                    peptide: "PEPTIDEK", confidence: 90);
                var deep = MakeRow(DeNovoSource.DeepNovo, scan: 101, charge: 2,
                    mz: 500.25 + PeptidoformIdentity.Carbon13SpacingDa / 2, peptide: "PEPTLDEK",
                    confidence: 80);

                List<SpectrumComparisonRow> table = SpectrumComparisonTable.Build(
                    new[] { peaks }, new[] { deep }, Array.Empty<PatternLabPsm>(),
                    Array.Empty<QuantRow>());
                var scanRows = table.Where(r => r.ScanNumber == 101).ToList();
                check("table: an isotope-equivalent PEAKS M+0 and DeepNovo M+1 collapse into one precursor row",
                    scanRows.Count == 1, scanRows.Count.ToString());
                if (scanRows.Count == 1)
                {
                    SpectrumComparisonRow row = scanRows[0];
                    check("table: both differing source sequences are retained on the shared row",
                        row.PeaksDeNovoSequence == "PEPTIDEK" && row.DeepNovoSequence == "PEPTLDEK",
                        row.PeaksDeNovoSequence + " / " + row.DeepNovoSequence);
                    check("table: the shared row is labelled isotope-corrected with a non-zero offset",
                        row.PrecursorMatchBasis == "isotope-corrected"
                        && row.PrecursorIsotopeOffsetSelected != 0,
                        row.PrecursorMatchBasis + " offset " + row.PrecursorIsotopeOffsetSelected);
                }

                // The union resolver assigns exactly one XIC-owning winner while keeping both claims.
                var selections = new List<DeNovoSourceSelection>
                {
                    new DeNovoSourceSelection(DeNovoSource.DenovoOnly, 0, priority: 0),
                    new DeNovoSourceSelection(DeNovoSource.DeepNovo, 0, priority: 1),
                };
                List<PeaksDeNovoRow> resolved = DeNovoUnionResolver.Resolve(
                    new[] { peaks, deep }, selections);
                check("union: differing sequences on one isotope-equivalent precursor resolve to a single XIC",
                    resolved.Count == 1, resolved.Count.ToString());
                if (resolved.Count == 1)
                    check("union: both source sequences stay auditable in the winner's evidence",
                        resolved[0].SourceEvidence.Select(e => e.Sequence)
                            .OrderBy(s => s, StringComparer.Ordinal)
                            .SequenceEqual(new[] { "PEPTIDEK", "PEPTLDEK" }),
                        string.Join(",", resolved[0].SourceEvidence.Select(e => e.Sequence)));

                // ---- deterministic one-to-one pairing under input permutations ------------------------
                List<PeaksDeNovoRow> reversed = DeNovoUnionResolver.Resolve(
                    new[] { deep, peaks }, selections);
                bool sameWinner = resolved.Count == reversed.Count
                    && resolved.Count == 1
                    && resolved[0].Peptide == reversed[0].Peptide
                    && resolved[0].SourceId == reversed[0].SourceId;
                check("union: the resolved winner is identical when the input rows are permuted",
                    sameWinner, resolved.Count == 1 && reversed.Count == 1
                        ? resolved[0].Peptide + "/" + reversed[0].Peptide : "count mismatch");

                List<SpectrumComparisonRow> tableReversed = SpectrumComparisonTable.Build(
                    new[] { deep }, new[] { peaks }, Array.Empty<PatternLabPsm>(),
                    Array.Empty<QuantRow>());
                check("table: the collapse is permutation invariant (one row either input order)",
                    tableReversed.Count(r => r.ScanNumber == 101) == 1,
                    tableReversed.Count(r => r.ScanNumber == 101).ToString());
            }
        }

        private static NativePeptideQuant MakePeptide(string side, string sequence, int charge, double mz) =>
            new NativePeptideQuant
            {
                Run = "run01",
                Side = side,
                Sequence = sequence,
                Charge = charge,
                PrecursorMz = mz,
            };

        private static PeaksDeNovoRow MakeRow(DeNovoSource source, int scan, int charge, double mz,
            string peptide, double confidence) => new PeaksDeNovoRow
        {
            RunName = "run01",
            Scan = scan,
            Charge = charge,
            Mz = mz,
            Peptide = peptide,
            Length = peptide.Length,
            ConfidenceValue = confidence,
            ConfidenceField = source.ConfidenceColumn,
            SourceId = source.Id,
            SourceDisplayName = source.DisplayName,
            InputFilePath = source.Id + ".csv",
            InputFileSha256 = source.Id,
        };
    }
}
