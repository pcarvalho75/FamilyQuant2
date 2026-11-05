using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        // ============================================================ native fraction of TIC (Q3)

        /// <summary>A stored quant row for the Q3 unit tests: M+0 in isotope slot 0, the envelope given
        /// directly (as the store captures it at extraction), 6-isotope basis.</summary>
        private static QuantRow Qr(string side, string run, int scan, int z, string seq, double m0, double env)
        {
            return new QuantRow
            {
                Side = side,
                Run = run,
                Scan = scan,
                Charge = z,
                Sequence = seq,
                PrecursorMz = Math.Round(500.0 + scan * 1e-3, DeNovoXicExtractor.MzDecimals),
                IsotopeAreas = new[] { m0, 0.0, 0.0, 0.0 },
                EnvelopeSum = env,
            };
        }

        /// <summary>
        /// The per-peptide-dedup method on synthetic rows, so its invariants are proved on a machine that
        /// has never seen a RAW: the collapse of repeated anchors of one peak to one area, the fraction
        /// taken over the TIC on the consistent basis, that a fraction over 100% is impossible by
        /// construction of the method where the un-collapsed per-anchor sum would exceed the TIC, the
        /// envelope partition closing, the derived section persisting through a .fqproj round trip, and
        /// the envelope family rollup with a shared peptide.
        /// </summary>
        private static void NativeFractionUnitTests(string workDir)
        {
            // ---- fixture 1: one peak sampled five times, the method demonstration --------------------
            // Five MS2 anchors inside ONE elution peak of one peptide. The store holds five rows; the
            // ion current is one peak. Summing per anchor gives 465 > the 300 TIC (155%); the collapse
            // gives one 100 area (33%). This is the whole defect and the whole fix, in miniature.
            var demo = new List<QuantRow>
            {
                Qr("identified", "demo_01", 10, 2, "REDUNDANTK", 90, 180),
                Qr("identified", "demo_01", 12, 2, "REDUNDANTK", 95, 190),
                Qr("identified", "demo_01", 14, 2, "REDUNDANTK", 100, 200),   // the max-M+0 anchor
                Qr("identified", "demo_01", 16, 2, "REDUNDANTK", 92, 184),
                Qr("identified", "demo_01", 18, 2, "REDUNDANTK", 88, 176),
            };

            List<NativePeptideQuant> collapsed = NativeQuantFractions.DedupToPeptides(demo);
            Check("Q3 dedup: five anchors of one peak collapse to one peptide", collapsed.Count == 1,
                collapsed.Count.ToString());
            Check("Q3 dedup: the collapse counts the anchors it removed",
                collapsed.Count == 1 && collapsed[0].AnchorCount == 5, collapsed.Count == 1 ? collapsed[0].AnchorCount.ToString() : "-");
            Check("Q3 dedup: the representative is the largest-M+0 anchor, envelope taken from it",
                collapsed.Count == 1 && collapsed[0].AreaM0 == 100.0 && collapsed[0].EnvelopeSum == 200.0,
                collapsed.Count == 1 ? collapsed[0].AreaM0 + "/" + collapsed[0].EnvelopeSum : "-");

            double naiveM0 = demo.Sum(r => r.AreaM0);
            var demoTic = new Dictionary<string, double> { ["demo_01"] = 300.0 };
            NativeRunFraction demoF = NativeQuantFractions.Compute(demo, demoTic).Single();

            Check("Q3 method: the un-collapsed per-anchor sum WOULD exceed the TIC (over 100%)",
                naiveM0 / 300.0 > 1.0, (naiveM0 / 300.0).ToString("P2", CultureInfo.InvariantCulture));
            Check("Q3 method: the deduped M+0 fraction is below 100% by construction",
                demoF.IdentifiedFractionM0 < 1.0 && Math.Abs(demoF.IdentifiedFractionM0 - 100.0 / 300.0) < 1e-12,
                demoF.IdentifiedFractionM0.ToString("P4", CultureInfo.InvariantCulture));
            Check("Q3 method: the deduped envelope fraction is below 100% and exceeds M+0",
                demoF.IdentifiedFractionEnvelope < 1.0 && demoF.IdentifiedFractionEnvelope > demoF.IdentifiedFractionM0,
                demoF.IdentifiedFractionEnvelope.ToString("P4", CultureInfo.InvariantCulture));

            // ---- fixture 2: two runs, both sides, the fractions, the partition and the manifest -------
            var rows = new List<QuantRow>
            {
                // run_01 identified: PEPTIK sampled twice (rep M+0 105, env 241.5), AAAK once
                Qr("identified", "run_01", 20, 2, "PEPTIK", 100, 230),
                Qr("identified", "run_01", 24, 2, "PEPTIK", 105, 241.5),
                Qr("identified", "run_01", 30, 2, "AAAK", 60, 138),
                // run_01 de novo: DENAVAA sampled twice (rep M+0 44, env 96.8)
                Qr("denovo", "run_01", 40, 2, "DENAVAA", 40, 88),
                Qr("denovo", "run_01", 44, 2, "DENAVAA", 44, 96.8),
                // run_02 identified: PEPTIK once; de novo: DENAVAC once
                Qr("identified", "run_02", 50, 2, "PEPTIK", 90, 207),
                Qr("denovo", "run_02", 60, 3, "DENAVAC", 55, 121),
            };
            var perRunTic = new Dictionary<string, double> { ["run_01"] = 2000.0, ["run_02"] = 1500.0 };

            List<NativeRunFraction> fr = NativeQuantFractions.Compute(rows, perRunTic);
            NativeRunFraction r01 = fr.Single(f => f.Run == "run_01");
            NativeRunFraction r02 = fr.Single(f => f.Run == "run_02");
            NativeRunFraction grouped = NativeQuantFractions.Grouped(fr);

            Check("Q3 compute: run_01 identified deduped to two peptides (M+0 165, envelope 379.5)",
                r01.IdentifiedPeptides == 2 && Math.Abs(r01.IdentifiedAreaM0 - 165) < 1e-9
                    && Math.Abs(r01.IdentifiedEnvelope - 379.5) < 1e-9,
                r01.IdentifiedAreaM0 + "/" + r01.IdentifiedEnvelope + " over " + r01.IdentifiedPeptides + " peptide(s)");
            Check("Q3 compute: run_01 de novo deduped to one peptide (M+0 44, envelope 96.8)",
                r01.DeNovoPeptides == 1 && Math.Abs(r01.DeNovoAreaM0 - 44) < 1e-9 && Math.Abs(r01.DeNovoEnvelope - 96.8) < 1e-9,
                r01.DeNovoAreaM0 + "/" + r01.DeNovoEnvelope);
            Check("Q3 compute: every run's fractions are below 100% on both bases",
                fr.All(f => f.IdentifiedFractionEnvelope < 1.0 && f.RecoveredFractionEnvelope < 1.0),
                string.Join(" | ", fr.Select(f => f.Run + " rec=" + f.RecoveredFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture))));
            Check("Q3 compute: the envelope fraction exceeds the M+0 fraction for every run",
                fr.All(f => f.IdentifiedFractionEnvelope > f.IdentifiedFractionM0));
            Check("Q3 compute: DDA + de novo recovered is identified plus de novo envelope over the TIC",
                Math.Abs(r01.RecoveredFractionEnvelope - (379.5 + 96.8) / 2000.0) < 1e-12,
                r01.RecoveredFractionEnvelope.ToString("R", CultureInfo.InvariantCulture));
            Check("Q3 primary: identified-reference calibration learns the 2.3x identified ratio",
                Math.Abs(r01.DeNovoEnvelopeReferenceCalibrated - 44.0 * 2.3) < 1e-9
                && Math.Abs(r02.DeNovoEnvelopeReferenceCalibrated - 55.0 * 2.3) < 1e-9,
                r01.DeNovoEnvelopeReferenceCalibrated.ToString("R", CultureInfo.InvariantCulture));
            Check("Q3 primary: M+0, calibrated primary, and unadjusted diagnostic remain separate",
                r01.RecoveredFractionM0 != r01.RecoveredFractionReferenceCalibrated
                && r01.RecoveredFractionReferenceCalibrated != r01.RecoveredFractionEnvelope,
                r01.RecoveredFractionM0.ToString("R", CultureInfo.InvariantCulture) + " / " +
                r01.RecoveredFractionReferenceCalibrated.ToString("R", CultureInfo.InvariantCulture) + " / " +
                r01.RecoveredFractionEnvelope.ToString("R", CultureInfo.InvariantCulture));
            var highRatio = new List<NativePeptideQuant>
            {
                new NativePeptideQuant { Side = NativeQuantFractions.SideIdentified, Charge = 1,
                    PrecursorMz = 1001.007276466621, AreaM0 = 1, EnvelopeSum = 8 },
                new NativePeptideQuant { Side = NativeQuantFractions.SideDeNovo, Charge = 1,
                    PrecursorMz = 1001.007276466621, AreaM0 = 2, EnvelopeSum = 3 },
            };
            ReferenceEnvelopeEstimator.Apply(highRatio);
            Check("Q3 primary: a valid envelope/M+0 ratio above six is not clipped to the window count",
                Math.Abs(highRatio[1].ReferenceCalibratedEnvelope - 16.0) < 1e-12,
                highRatio[1].ReferenceCalibratedEnvelope.ToString("R", CultureInfo.InvariantCulture));
            Check("Q3 grouped: the pooled fraction is the pooled numerator over the pooled TIC",
                Math.Abs(grouped.Tic - 3500) < 1e-9
                    && Math.Abs(grouped.IdentifiedFractionM0 - 255.0 / 3500.0) < 1e-12
                    && Math.Abs(grouped.RecoveredFractionEnvelope - (586.5 + 217.8) / 3500.0) < 1e-9,
                grouped.IdentifiedFractionM0.ToString("P4", CultureInfo.InvariantCulture));

            // ---- the envelope three-way partition, native on both sides ------------------------------
            var (m0Part, envPart) = NativeQuantFractions.BuildNativePartitions(rows, perRunTic);
            ThreeWayPartitionRow envGrouped = ThreeWayPartition.Grouped(envPart);

            Check("Q3 partition: one envelope row per run and the grouped row closes on 100%",
                envPart.Count == 2 && envPart.All(r => r.Closes) && envGrouped.Closes,
                string.Join(" | ", envPart.Select(r => r.ShareSum.ToString("R", CultureInfo.InvariantCulture))));
            Check("Q3 partition: no envelope remainder is negative (the numerators fit under the TIC)",
                envPart.All(r => !r.NegativeRemainder) && !envGrouped.NegativeRemainder);
            Check("Q3 partition: the identified slice is labelled as the native envelope, measured here",
                envPart.All(r => r.PatternLab.Label == "measured:" + ThreeWayPartition.SourceNativeIdentifiedEnvelope),
                envPart[0].PatternLab.Label);
            Check("Q3 partition: the de novo slice is labelled as the native envelope, measured here",
                envPart.All(r => r.DeNovo.Label == "measured:" + ThreeWayPartition.SourceNativeDeNovoEnvelope),
                envPart[0].DeNovo.Label);
            Check("Q3 partition: the M+0 comparison partition is kept alongside and closes",
                m0Part.Count == 2 && m0Part.All(r => r.Closes)
                    && m0Part.All(r => r.PatternLab.Label == "measured:" + ThreeWayPartition.SourceNativeIdentifiedM0),
                m0Part[0].PatternLab.Label);

            // ---- the derived section, computed and persisted -----------------------------------------
            var manifest = new QuantManifest
            {
                IsotopeCount = 4,
                Ppm = DeNovoXicExtractor.ProductionPpm,
                PerRunTic = new Dictionary<string, double>(perRunTic, StringComparer.OrdinalIgnoreCase),
                Provenance = QuantManifest.DefaultProvenance(),
            };
            NativeQuantFractions.PopulateManifest(manifest, rows);

            Check("Q3 manifest: the derived section carries one row per run plus the grouped row",
                manifest.DerivedFractions.Count == 3 && manifest.DerivedFractions.Count(d => d.Grouped) == 1,
                manifest.DerivedFractions.Count.ToString());
            Check("Q3 manifest: the method and the derived provenance are recorded",
                manifest.DerivedMethod == NativeQuantFractions.Method
                    && manifest.Provenance.TryGetValue("identified_fraction_envelope", out string? pv) && pv == "derived"
                    && manifest.Provenance.ContainsKey("recovered_fraction_reference_calibrated"),
                manifest.DerivedMethod);

            var fcf = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|X|Y", USearchID = 0, UserID = 0 },
                },
            };
            string path = Path.Combine(workDir, "q3_derived.fqproj");
            ProjectStore.WriteFqproj(path, fcf, rows, manifest);
            var (_, _, mf2) = ProjectStore.ReadFqproj(path);

            DerivedRunFraction? gIn = manifest.DerivedFractions.FirstOrDefault(d => d.Grouped);
            DerivedRunFraction? gOut = mf2.DerivedFractions.FirstOrDefault(d => d.Grouped);
            Check("Q3 manifest: the derived section survives a .fqproj round trip bit for bit",
                gIn != null && gOut != null
                    && BitConverter.DoubleToInt64Bits(gIn.RecoveredFractionEnvelope)
                        == BitConverter.DoubleToInt64Bits(gOut.RecoveredFractionEnvelope)
                    && BitConverter.DoubleToInt64Bits(gIn.RecoveredFractionReferenceCalibrated)
                        == BitConverter.DoubleToInt64Bits(gOut.RecoveredFractionReferenceCalibrated)
                    && mf2.DerivedMethod == NativeQuantFractions.Method,
                gOut == null ? "no grouped row" : gOut.RecoveredFractionEnvelope.ToString("R", CultureInfo.InvariantCulture));

            // ---- the envelope family rollup, with a shared peptide -----------------------------------
            // NormalizeSequence folds I->L and uppercases: "PEPTIK" -> "PEPTLK". The resolvers are keyed
            // on the normalized form, as the homology mapper produces.
            var idFam = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["PEPTLK"] = new[] { 10 },
                ["AAAK"] = new[] { 10, 20 },   // shared between two families
            };
            var dnFam = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["DENAVAA"] = new[] { 20 },
                ["DENAVAC"] = new[] { 30 },
            };
            IReadOnlyList<int> Empty = Array.Empty<int>();
            FamilyEnvelopeQuantResult fam = FamilyEnvelopeQuant.Build(
                rows,
                s => idFam.TryGetValue(s, out var v) ? v : Empty,
                s => dnFam.TryGetValue(s, out var v) ? v : Empty,
                id => "family " + id.ToString(CultureInfo.InvariantCulture));

            FamilyEnvelopeQuantRow? f10 = fam.Families.FirstOrDefault(f => f.FamilyUserId == 10);
            FamilyEnvelopeQuantRow? f20 = fam.Families.FirstOrDefault(f => f.FamilyUserId == 20);
            FamilyEnvelopeQuantRow? f30 = fam.Families.FirstOrDefault(f => f.FamilyUserId == 30);

            Check("Q3 family: three families are quantified and nothing is left unattributed",
                fam.Families.Count == 3
                    && fam.UnattributedIdentifiedEnvelope == 0 && fam.UnattributedDeNovoEnvelope == 0,
                fam.Families.Count + " fam, unattributed id " + fam.UnattributedIdentifiedEnvelope);
            Check("Q3 family: family 10 sums PEPTIK across both runs and AAAK (identified envelope 586.5)",
                f10 != null && Math.Abs(f10.IdentifiedEnvelope - 586.5) < 1e-9 && f10.DeNovoEnvelope == 0,
                f10 == null ? "missing" : f10.IdentifiedEnvelope.ToString("R", CultureInfo.InvariantCulture));
            Check("Q3 family: family 20 carries identified (shared AAAK) AND de novo signal",
                f20 != null && Math.Abs(f20.IdentifiedEnvelope - 138) < 1e-9 && Math.Abs(f20.DeNovoEnvelope - 96.8) < 1e-9,
                f20 == null ? "missing" : f20.IdentifiedEnvelope + "/" + f20.DeNovoEnvelope);
            Check("Q3 family: family 30 is de novo only", f30 != null && f30.IdentifiedEnvelope == 0 && Math.Abs(f30.DeNovoEnvelope - 121) < 1e-9);
            // The shared peptide overlaps the family sums past the deduped unique total, by exactly its
            // own area. This is the count-in-every-family policy made visible, not a bug.
            double uniqueIdentifiedEnvelope = 241.5 + 138 + 207;   // PEPTIK_01 + AAAK + PEPTIK_02
            double familySummedIdentified = fam.Families.Sum(f => f.IdentifiedEnvelope);
            Check("Q3 family: the shared peptide overlaps the family sums by exactly its own area",
                Math.Abs((familySummedIdentified - uniqueIdentifiedEnvelope) - 138) < 1e-9,
                (familySummedIdentified - uniqueIdentifiedEnvelope).ToString("R", CultureInfo.InvariantCulture));

            var crossSide = new List<QuantRow>
            {
                Qr("identified", "overlap_01", 1, 2, "PEPTIDEI", 100, 230),
                Qr("denovo", "overlap_01", 2, 2, "PEPTIDEL", 80, 400),
            };
            NativeRunFraction overlap = NativeQuantFractions.Compute(crossSide,
                new Dictionary<string, double> { ["overlap_01"] = 1000 }).Single();
            Check("Q3 cross-side: exact/I-L-folded sequence+charge overlap is suppressed conservatively",
                overlap.DeNovoSuppressedCrossSideOverlaps == 1 && overlap.DeNovoAreaM0 == 0
                && overlap.DeNovoEnvelope == 0 && overlap.RecoveredFractionEnvelope == 0.23,
                overlap.DeNovoSuppressedCrossSideOverlaps.ToString(CultureInfo.InvariantCulture));

            Check("Q3 peptidoform: fixed C(+57.02) notation canonicalizes to implicit carbamidomethyl C",
                PeptidoformIdentity.Canonicalize("AC(+57.02)DK", false)
                    == PeptidoformIdentity.Canonicalize("ACDK", false));
            Check("Q3 peptidoform: non-fixed modification position remains part of the identity",
                PeptidoformIdentity.Canonicalize("AM(+15.99)CK", false)
                    != PeptidoformIdentity.Canonicalize("AMC(+15.99)K", false));

            var fixedNotation = new List<QuantRow>
            {
                Qr("identified", "fixed_01", 100, 2, "ACDK", 50, 110),
                Qr("identified", "fixed_01", 101, 2, "AC(+57.02)DK", 55, 121),
            };
            Check("Q3 dedup: explicit and implicit fixed-C notation share one representative",
                NativeQuantFractions.DedupToPeptides(fixedNotation).Count == 1);

            var distinctForms = new List<QuantRow>
            {
                Qr("identified", "forms_01", 100, 2, "AMCK", 50, 110),
                Qr("identified", "forms_01", 101, 2, "AM(+15.99)CK", 45, 99),
            };
            Check("Q3 dedup: a non-fixed modified peptidoform is not collapsed into the bare backbone",
                NativeQuantFractions.DedupToPeptides(distinctForms).Count == 2);

            var isotopeOffsetMasses = new List<QuantRow>
            {
                Qr("identified", "masses_01", 100, 2, "SAMEK", 50, 110),
                Qr("identified", "masses_01", 101, 2, "SAMEK", 45, 99),
            };
            isotopeOffsetMasses[0].PrecursorMz = 500.0;
            isotopeOffsetMasses[1].PrecursorMz = 501.0;
            // Same peptidoform and charge fixes the neutral mass, so the two precursor masses are
            // isotope-offset (+-1 Da monoisotopic-peak misassignment) copies of ONE peptide, not a new
            // peptidoform. They must collapse to a single representative, the largest-M+0 anchor.
            List<NativePeptideQuant> isotopeOffsetPeptides = NativeQuantFractions.DedupToPeptides(isotopeOffsetMasses);
            Check("Q3 dedup: same peptidoform and charge at isotope-offset precursor masses collapse to one peptide",
                isotopeOffsetPeptides.Count == 1
                    && isotopeOffsetPeptides[0].AreaM0 == 50
                    && isotopeOffsetPeptides[0].PrecursorMz == 500.0
                    && isotopeOffsetPeptides[0].AnchorCount == 2,
                isotopeOffsetPeptides.Count.ToString(CultureInfo.InvariantCulture));

            // Three anchors of the same peptidoform and charge at close precursor masses. For a fixed
            // peptidoform and charge the neutral mass is determined, so these are isotope-offset copies
            // of ONE peptide, never distinct peptides. The whole (seq, charge) group therefore collapses
            // to a SINGLE representative, the largest-M+0 anchor (B, M+0 30 / envelope 300), and every one
            // of the six input permutations must yield that one representative with AnchorCount 3 and feed
            // identical TIC and family totals (the collapse is chosen by data keys, not row order).
            var chainRows = new List<QuantRow>
            {
                Qr("identified", "chain_01", 301, 2, "CHAINK", 10, 100),
                Qr("identified", "chain_01", 302, 2, "CHAINK", 30, 300),
                Qr("identified", "chain_01", 303, 2, "CHAINK", 20, 200),
            };
            chainRows[0].PrecursorMz = 501.007276; // neutral mass 1000.000 Da
            chainRows[1].PrecursorMz = 501.014776; // +15 ppm from A, the largest-M+0 anchor kept
            chainRows[2].PrecursorMz = 501.022276; // +30 ppm from A, +15 ppm from B

            int[][] chainPermutations =
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
            };
            string? firstDedupSignature = null;
            string? firstFractionSignature = null;
            string? firstFamilySignature = null;
            bool singleRepresentativeExpected = true;
            bool dedupPermutationInvariant = true;
            bool fractionPermutationInvariant = true;
            bool familyPermutationInvariant = true;

            foreach (int[] permutation in chainPermutations)
            {
                List<QuantRow> permuted = permutation.Select(i => chainRows[i]).ToList();
                List<NativePeptideQuant> chainPeptides = NativeQuantFractions.DedupToPeptides(permuted);
                string dedupSignature = string.Join("|", chainPeptides.Select(p =>
                    p.PrecursorMz.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    p.AreaM0.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    p.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    p.AnchorCount.ToString(CultureInfo.InvariantCulture)));
                firstDedupSignature ??= dedupSignature;
                dedupPermutationInvariant &= dedupSignature == firstDedupSignature;
                singleRepresentativeExpected &= chainPeptides.Count == 1
                    && chainPeptides[0].PrecursorMz == chainRows[1].PrecursorMz
                    && chainPeptides[0].AreaM0 == 30 && chainPeptides[0].EnvelopeSum == 300
                    && chainPeptides[0].AnchorCount == 3;

                NativeRunFraction chainFraction = NativeQuantFractions.Compute(permuted,
                    new Dictionary<string, double> { ["chain_01"] = 1000 }).Single();
                string fractionSignature = chainFraction.IdentifiedPeptides + ":" +
                    chainFraction.IdentifiedAreaM0.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    chainFraction.IdentifiedEnvelope.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    chainFraction.IdentifiedFractionM0.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    chainFraction.IdentifiedFractionEnvelope.ToString("R", CultureInfo.InvariantCulture);
                firstFractionSignature ??= fractionSignature;
                fractionPermutationInvariant &= fractionSignature == firstFractionSignature
                    && chainFraction.IdentifiedPeptides == 1
                    && chainFraction.IdentifiedAreaM0 == 30
                    && chainFraction.IdentifiedEnvelope == 300
                    && chainFraction.IdentifiedFractionM0 == 0.03
                    && chainFraction.IdentifiedFractionEnvelope == 0.3;

                FamilyEnvelopeQuantResult chainFamilies = FamilyEnvelopeQuant.Build(
                    permuted, _ => new[] { 41 }, _ => new[] { 41 }, id => "family " + id);
                FamilyEnvelopeQuantRow chainFamily = chainFamilies.Families.Single(f => f.FamilyUserId == 41);
                string familySignature = chainFamilies.Families.Count + ":" +
                    chainFamily.IdentifiedPeptides + ":" +
                    chainFamily.IdentifiedM0.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    chainFamily.IdentifiedEnvelope.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    chainFamilies.UnattributedIdentifiedPeptides;
                firstFamilySignature ??= familySignature;
                familyPermutationInvariant &= familySignature == firstFamilySignature
                    && chainFamilies.Families.Count == 1
                    && chainFamily.IdentifiedPeptides == 1
                    && chainFamily.IdentifiedM0 == 30
                    && chainFamily.IdentifiedEnvelope == 300
                    && chainFamilies.UnattributedIdentifiedPeptides == 0;
            }

            Check("Q3 dedup collapse: a same-peptidoform isotope-offset group becomes ONE largest-M+0 representative",
                singleRepresentativeExpected, firstDedupSignature ?? "no result");
            Check("Q3 dedup determinism: all row permutations preserve the group, anchor count and representative",
                dedupPermutationInvariant, firstDedupSignature ?? "no result");
            Check("Q3 dedup determinism: all row permutations preserve peptide count and TIC numerators",
                fractionPermutationInvariant, firstFractionSignature ?? "no result");
            Check("Q3 dedup determinism: all row permutations preserve family attribution",
                familyPermutationInvariant, firstFamilySignature ?? "no result");

            // Analytics decorates the native representative by rejoining it to its stored row. Prove
            // that an equal-M+0 tie uses the same larger-envelope anchor (and therefore the same isotope
            // vector and family) when the raw rows are reversed.
            var tiedRepresentativeRows = new List<QuantRow>
            {
                Qr("identified", "tie_01", 401, 2, "TIEK", 25, 100),
                Qr("identified", "tie_01", 402, 2, "TIEK", 25, 200),
            };
            tiedRepresentativeRows[0].PrecursorMz = 601.007276;
            tiedRepresentativeRows[0].IsotopeAreas = new[] { 25.0, 75.0 };
            tiedRepresentativeRows[0].FamilyUserId = 7;
            tiedRepresentativeRows[1].PrecursorMz = 601.012276;
            tiedRepresentativeRows[1].IsotopeAreas = new[] { 25.0, 175.0 };
            tiedRepresentativeRows[1].FamilyUserId = 8;

            string? firstAnalyticsTieSignature = null;
            bool analyticsTieInvariant = true;
            foreach (List<QuantRow> order in new[]
                     {
                         tiedRepresentativeRows,
                         tiedRepresentativeRows.AsEnumerable().Reverse().ToList(),
                     })
            {
                AnalyticsPeptide tied = AnalyticsBuilder.BuildPeptides(order).Single();
                string signature = tied.PrecursorMz.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    tied.Envelope.ToString("R", CultureInfo.InvariantCulture) + ":" +
                    tied.AreaAt(1).ToString("R", CultureInfo.InvariantCulture) + ":" +
                    tied.FamilyUserId;
                firstAnalyticsTieSignature ??= signature;
                analyticsTieInvariant &= signature == firstAnalyticsTieSignature
                    && tied.PrecursorMz == tiedRepresentativeRows[1].PrecursorMz
                    && tied.Envelope == 200 && tied.AreaAt(1) == 175
                    && tied.FamilyUserId == 8;
            }
            Check("Q3 analytics determinism: an equal-M+0 tie preserves isotope data and family on reversal",
                analyticsTieInvariant, firstAnalyticsTieSignature ?? "no result");

            var fixedCrossSide = new List<QuantRow>
            {
                Qr("identified", "fixed_cross_01", 100, 2, "ACDK", 50, 110),
                Qr("denovo", "fixed_cross_01", 101, 2, "AC(+57.02)DK", 45, 99),
            };
            NativeRunFraction fixedCross = NativeQuantFractions.Compute(fixedCrossSide,
                new Dictionary<string, double> { ["fixed_cross_01"] = 1000 }).Single();
            Check("Q3 cross-side: fixed-C notation suppresses the same precursor once",
                fixedCross.DeNovoSuppressedCrossSideOverlaps == 1 && fixedCross.DeNovoPeptides == 0);

            var modifiedCrossSide = new List<QuantRow>
            {
                Qr("identified", "modified_cross_01", 100, 2, "AMCK", 50, 110),
                Qr("denovo", "modified_cross_01", 101, 2, "AM(+15.99)CK", 45, 99),
            };
            NativeRunFraction modifiedCross = NativeQuantFractions.Compute(modifiedCrossSide,
                new Dictionary<string, double> { ["modified_cross_01"] = 1000 }).Single();
            Check("Q3 cross-side: a genuinely different modified peptidoform is not suppressed",
                modifiedCross.DeNovoSuppressedCrossSideOverlaps == 0 && modifiedCross.DeNovoPeptides == 1);

            var diagnosticRows = new List<NativePeptideQuant>
            {
                new NativePeptideQuant { Side = "identified", Charge = 2, PrecursorMz = 450,
                    AreaM0 = 100, EnvelopeSum = 210 },
                new NativePeptideQuant { Side = "identified", Charge = 2, PrecursorMz = 500,
                    AreaM0 = 80, EnvelopeSum = 184 },
                new NativePeptideQuant { Side = "identified", Charge = 2, PrecursorMz = 550,
                    AreaM0 = 60, EnvelopeSum = 150 },
                new NativePeptideQuant { Side = "identified", Charge = 2, PrecursorMz = 900,
                    AreaM0 = 40, EnvelopeSum = 112 },
                new NativePeptideQuant { Side = "denovo", Charge = 2, PrecursorMz = 525,
                    AreaM0 = 50, EnvelopeSum = 160 },
            };
            ReferenceEnvelopeEstimator.Apply(diagnosticRows);
            ReferenceEnvelopeDiagnostics modelAudit = ReferenceEnvelopeEstimator.Evaluate(diagnosticRows);
            Check("Q3 model: diagnostics expose bin support and leave-one-out error",
                modelAudit.Bins.Count == ReferenceEnvelopeEstimator.MassBinUpperBounds.Length
                && modelAudit.CrossValidatedPeptides == 4
                && modelAudit.CrossValidatedWeightedAbsolutePercentError > 0);
            Check("Q3 model: fixed-bin vs pooled-ratio sensitivity is ordered and machine-readable",
                modelAudit.SensitivityLowerEstimate <= modelAudit.SensitivityUpperEstimate
                && ReferenceEnvelopeEstimator.ToCsv(modelAudit).Contains(
                    "cross_validated_weighted_absolute_percent_error"));

            var summaryFixture = new DeNovoVariantSummary
            {
                Variant = "fixture", Sources = "PEAKS", ConfidenceMetrics = "ALC",
                ConfidenceCutoffNote = "ALC >=80", IdentifiedFractionOfTic = 0.28,
                DeNovoModelEstimateFractionOfTic = 0.14, CombinedModelEstimateFractionOfTic = 0.42,
                RelativeUplift = 0.5, SearchRepresentatives = 10, DeNovoRepresentatives = 5,
                TotalRepresentatives = 15, AcceptedDeNovoSpectrumCalls = 20,
                DistinctDeNovoSequencesNoXic = 8, CrossSideSuppressed = 1,
                EnvelopeModelMethod = ReferenceEnvelopeEstimator.Method,
                EnvelopeModelCrossValidatedWape = 0.12,
                CombinedSensitivityLowerFractionOfTic = 0.40,
                CombinedSensitivityUpperFractionOfTic = 0.44,
            };
            string variantCsv = DeNovoVariantComparison.ToCsv(new[] { summaryFixture });
            Check("Q3 variant comparison: native CSV round-trips and Markdown names model sensitivity",
                DeNovoVariantComparison.ParseCsv(variantCsv).Single().TotalRepresentatives == 15
                && DeNovoVariantComparison.ToMarkdown(new[] { summaryFixture }).Contains("not a confidence interval"));
            string ordinaryVariantName = summaryFixture.Variant;
            summaryFixture.Variant = "=HYPERLINK(\"https://invalid\")";
            string formulaSafeVariantCsv = DeNovoVariantComparison.ToCsv(new[] { summaryFixture });
            summaryFixture.Variant = ordinaryVariantName;
            Check("Q3 variant CSV neutralizes spreadsheet formulas in labels",
                formulaSafeVariantCsv.Contains("'=HYPERLINK", StringComparison.Ordinal));

            string formulaSafeTalkCsv = DeNovoTalkComparison.ToCsv(new[]
            {
                new DeNovoTalkComparisonRow
                {
                    Condition = "=HYPERLINK(\"https://invalid\")",
                    Sources = "@SUM(1+1)",
                    CarriedForwardDistinctSequences = 1,
                    ObservedSearchAssignedFractionOfTic = 0.2,
                    DeNovoModelEstimateFractionOfTic = 0.1,
                    CombinedAssignedFractionOfTic = 0.3,
                },
            });
            Check("compact-talk CSV neutralizes spreadsheet formulas in both text fields",
                formulaSafeTalkCsv.Contains("'=HYPERLINK", StringComparison.Ordinal)
                && formulaSafeTalkCsv.Contains("'@SUM", StringComparison.Ordinal));

            var sourceFamilyRows = new List<QuantRow>
            {
                AnQr("identified", "source_01", 1, 2, "PEPTIDEK", 500,
                    new[] { 100.0, 40.0 }),
                AnQr("denovo", "source_01", 2, 2, "C(+57.02)DEEPKA", 600,
                    new[] { 80.0, 30.0 }),
                AnQr("denovo", "source_01", 3, 2, "AFGHILMN", 700,
                    new[] { 70.0, 20.0 }),
                AnQr("denovo", "source_01", 4, 2, "MNPQRSTV", 800,
                    new[] { 60.0, 20.0 }),
                // PEAKS wins this shared precursor with a different sequence.  DeepNovo's alternate
                // is exactly the search sequence and must be suppressed per claim.
                AnQr("denovo", "source_01", 5, 2, "KAFGHILM", 500,
                    new[] { 50.0, 15.0 }),
            };
            sourceFamilyRows[1].DeNovoEvidence.Add(new DeNovoEvidence
            {
                SourceId = DeNovoSource.DenovoOnly.Id,
                SourceDisplayName = DeNovoSource.DenovoOnly.DisplayName,
                Sequence = "C(+57.02)DEEPKA",
            });
            sourceFamilyRows[2].DeNovoEvidence.Add(new DeNovoEvidence
            {
                SourceId = DeNovoSource.DeepNovo.Id,
                SourceDisplayName = DeNovoSource.DeepNovo.DisplayName,
                Sequence = "AFGHILMN",
            });
            foreach (DeNovoSource source in new[] { DeNovoSource.DenovoOnly, DeNovoSource.DeepNovo })
                sourceFamilyRows[3].DeNovoEvidence.Add(new DeNovoEvidence
                {
                    SourceId = source.Id, SourceDisplayName = source.DisplayName,
                    Sequence = "MNPQRSTV",
                });
            sourceFamilyRows[4].DeNovoEvidence.Add(new DeNovoEvidence
            {
                SourceId = DeNovoSource.DenovoOnly.Id,
                SourceDisplayName = DeNovoSource.DenovoOnly.DisplayName,
                Sequence = "KAFGHILM",
            });
            sourceFamilyRows[4].DeNovoEvidence.Add(new DeNovoEvidence
            {
                SourceId = DeNovoSource.DeepNovo.Id,
                SourceDisplayName = DeNovoSource.DeepNovo.DisplayName,
                Sequence = "PEPTIDEK",
            });
            var sourceFamilyCuration = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "target_source_fixture", UserID = 42, USearchID = 42,
                        Description = "Source-resolved toxin family",
                        Sequence = "MPEPTIDEKCDEEPKAFGHILMNPQRSTVWY",
                    },
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "Reverse_target_source_fixture", UserID = 43, USearchID = 43,
                        Sequence = "YWVTSRQPNMLIHGFAKPEEDCKEDITPEPM",
                    },
                },
            };
            FamilyEnvelopeQuantResult sourceFamily = FamilyEnvelopeQuant.BuildFromCuration(
                sourceFamilyRows, sourceFamilyCuration);
            FamilySourceSequenceRow sourceCounts = sourceFamily.SourceSequenceSummary.Families
                .Single(r => r.FamilyUserId == 42);
            Check("family source census strips PTMs, separates exclusive/shared callers and suppresses a search-equivalent alternate per claim",
                sourceCounts.SearchDistinctSequences == 1
                && sourceCounts.PeaksDeNovoOnlyExclusiveDistinctSequences == 2
                && sourceCounts.DeepNovoExclusiveDistinctSequences == 1
                && sourceCounts.SharedPeaksDeepNovoDistinctSequences == 1
                && sourceCounts.PeaksDeNovoOnlyTotalDistinctSequences == 3
                && sourceCounts.DeepNovoTotalDistinctSequences == 2
                && sourceCounts.DeNovoUnionDistinctSequences == 4);

            Console.WriteLine("        Q3 unit method OK: dedup restores <100% where the per-anchor sum was " +
                (naiveM0 / 300.0).ToString("P0", CultureInfo.InvariantCulture) + " of TIC");
        }

        /// <summary>
        /// Q3 on the real data: compute the Q2 store for run _01 with BOTH sides (identified reusing the
        /// Q1 cache, de novo extracted and cached here), then take the native fraction of the TIC the
        /// paper reports - identified M+0, identified envelope, DDA+de novo recovered - and the envelope
        /// three-way partition, all from FamilyQuant's own extraction and never the .plp or .xic2.
        ///
        /// THE SANITY GATE. The identified M+0 fraction must be below 100% and land in a reasonable band
        /// around F6's 11.87% stored-quant number for run _01, which is the independent proof the collapse
        /// reproduces the published figure rather than inventing one. The envelope fraction is larger, and
        /// the DDA+de novo recovered fraction is below 100%.
        ///
        /// COST: one RAW load for the de novo side of _01 (the identified side reuses the Q1 cache),
        /// bounded, cached into workDir.
        /// </summary>
        private static void NativeTicFractionRealTests(string? peaksDir, string? desktopDir, string workDir)
        {
            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            string? raw = desktopDir == null ? null
                : Path.Combine(desktopDir, "20220624_Bjararaca_1ug_3step-240min_01.raw");

            if (peaksDir == null || !Directory.Exists(peaksDir) || sepr2 == null || !File.Exists(sepr2)
                || raw == null || !File.Exists(raw))
            {
                Console.WriteLine("  SKIP  native fraction of TIC - needs the PEAKS directory, PLVI.sepr2 and the _01 RAW");
                skipped++;
                return;
            }

            string run01 = DeNovoReconciler.NormalizeRunName(raw);

            // Identified reuses the cache the Q1 section wrote; de novo has its own Q3 cache so a rerun of
            // the suite pays no RAW load the second time.
            Func<string, string, string> cacheFor = (side, run) => side == "identified"
                ? Path.Combine(workDir, "identified_xic_run01_n6.json")
                : Path.Combine(workDir, "q3_denovo_xic_run01_n6.json");

            var options = new FullXicOptions
            {
                IsotopeCount = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount,
                AppVersion = "test-q3",
                // Frozen reference quantities were computed before D65.
                PatternLabWinsScanConflicts = false,
                // Restricted to _01, so its spectrum table is partial: keep it out of the shared data
                // directory, where it would overwrite the complete three-run table (see the Q2 gate).
                SpectrumComparisonCsvPath = Path.Combine(workDir, "q3_run01_spectrum_evidence.csv"),
            };

            FullXicComputeResult store;
            try
            {
                store = FullXicComputer.ComputeFullXic(
                    desktopDir!, sepr2, peaksDir, options, cacheFor,
                    m => Console.WriteLine("        " + m), new[] { run01 },
                    run => Path.Combine(workDir, "ms1envelope_" + run + ".json"));
            }
            catch (Exception ex)
            {
                Check("the Q3 compute path runs against the _01 RAW", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            int idRows = store.Rows.Count(r => r.Side == "identified");
            int dnRows = store.Rows.Count(r => r.Side == "denovo");
            Check("Q3 real: the store carries both identified and de novo rows for _01",
                idRows > 1000 && dnRows > 100, idRows.ToString("N0") + " id / " + dnRows.ToString("N0") + " dn");

            List<NativeRunFraction> fr = NativeQuantFractions.Compute(store.Rows, store.Manifest.PerRunTic);
            NativeRunFraction f = fr.Single(x => x.Run == run01);

            Console.WriteLine($"        run _01 TIC = {f.Tic.ToString("E6", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"        identified: {f.IdentifiedPeptides:N0} peptide(s) | M+0 {f.IdentifiedAreaM0.ToString("E6", CultureInfo.InvariantCulture)} " +
                $"({f.IdentifiedFractionM0.ToString("P4", CultureInfo.InvariantCulture)}) | envelope {f.IdentifiedEnvelope.ToString("E6", CultureInfo.InvariantCulture)} " +
                $"({f.IdentifiedFractionEnvelope.ToString("P4", CultureInfo.InvariantCulture)})");
            Console.WriteLine($"        de novo:    {f.DeNovoPeptides:N0} peptide(s) | envelope {f.DeNovoEnvelope.ToString("E6", CultureInfo.InvariantCulture)} " +
                $"({f.DeNovoFractionEnvelope.ToString("P4", CultureInfo.InvariantCulture)})");
            Console.WriteLine($"        DDA + de novo recovered (envelope): {f.RecoveredFractionEnvelope.ToString("P4", CultureInfo.InvariantCulture)}");

            // The sanity gate: the deduped identified M+0 fraction is below 100% and near F6's 11.87%.
            Check("Q3 real: the identified M+0 fraction of TIC is below 100%",
                f.IdentifiedFractionM0 < 1.0, f.IdentifiedFractionM0.ToString("P4", CultureInfo.InvariantCulture));
            Check("Q3 real: and it lands in the 5%-30% band around F6's 11.87% stored-quant figure",
                f.IdentifiedFractionM0 >= 0.05 && f.IdentifiedFractionM0 <= 0.30,
                f.IdentifiedFractionM0.ToString("P4", CultureInfo.InvariantCulture));
            Check("Q3 real: the identified envelope fraction is larger than the M+0 fraction",
                f.IdentifiedFractionEnvelope > f.IdentifiedFractionM0
                    && f.IdentifiedFractionEnvelope < 1.0,
                f.IdentifiedFractionEnvelope.ToString("P4", CultureInfo.InvariantCulture));
            Check("Q3 real: the DDA + de novo recovered fraction is below 100%",
                f.RecoveredFractionEnvelope < 1.0 && f.RecoveredFractionEnvelope > f.IdentifiedFractionEnvelope,
                f.RecoveredFractionEnvelope.ToString("P4", CultureInfo.InvariantCulture));

            // The envelope partition on the real store closes.
            var (m0Part, envPart) = NativeQuantFractions.BuildNativePartitions(store.Rows, store.Manifest.PerRunTic);
            ThreeWayPartitionRow gEnv = ThreeWayPartition.Grouped(envPart);
            Check("Q3 real: the native envelope partition closes on 100%",
                envPart.All(r => r.Closes) && gEnv.Closes && !gEnv.NegativeRemainder,
                gEnv.ShareSum.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine($"        envelope partition _01: identified {envPart[0].SharePatternLabPercent.ToString("F2", CultureInfo.InvariantCulture)}% | " +
                $"de novo {envPart[0].ShareDeNovoPercent.ToString("F2", CultureInfo.InvariantCulture)}% | " +
                $"without an owner {envPart[0].ShareRemainderPercent.ToString("F2", CultureInfo.InvariantCulture)}%");

            // The derived section the compute path stored, round-tripped.
            Check("Q3 real: the compute path stored the derived fractions in the manifest",
                store.Manifest.DerivedMethod == NativeQuantFractions.Method
                    && store.Manifest.DerivedFractions.Any(d => d.Run == run01 && !d.Grouped),
                store.Manifest.DerivedFractions.Count.ToString());

            string path = Path.Combine(workDir, "q3_run01.fqproj");
            var fcf = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|X|Y", USearchID = 0, UserID = 0 },
                },
            };
            ProjectStore.WriteFqproj(path, fcf, store.Rows, store.Manifest);
            var (_, _, mf2) = ProjectStore.ReadFqproj(path);
            DerivedRunFraction? d01 = mf2.DerivedFractions.FirstOrDefault(d => d.Run == run01 && !d.Grouped);
            Check("Q3 real: the derived _01 M+0 fraction survives the .fqproj round trip bit for bit",
                d01 != null && BitConverter.DoubleToInt64Bits(d01.IdentifiedFractionM0)
                    == BitConverter.DoubleToInt64Bits(f.IdentifiedFractionM0),
                d01 == null ? "missing" : d01.IdentifiedFractionM0.ToString("R", CultureInfo.InvariantCulture));

            // The envelope family rollup on the real store, when the venom family file is available:
            // identified and de novo _01 sequences mapped to the 70% venom families by homology. Best
            // effort - it needs USEARCH for the clustering - so its absence skips rather than fails.
            string? database = desktopDir == null ? null : Path.Combine(desktopDir,
                "uniprotkb_taxonomy_id_8724_2026_06_25_ORDENADO_CONT123_NO-Redundancy.T-R");
            if (database != null && File.Exists(database))
            {
                FamilyClusterFile? venom = EnsureVenomFcf(database, workDir, out _);
                if (venom != null)
                {
                    var db = FamilyQuant.Homology.HomologyDatabase.Build(venom.MyExtendedFastaItems);

                    Dictionary<string, IReadOnlyList<int>> ResolveFor(string side)
                    {
                        var seqs = store.Rows.Where(r => r.Side == side)
                            .Select(r => DeNovoReconciler.NormalizeSequence(r.Sequence))
                            .Where(s => s.Length > 0)
                            .Distinct(StringComparer.Ordinal)
                            .ToList();
                        var res = FamilyQuant.Homology.HomologySearch.Search(seqs, db);
                        var asg = FamilyQuant.Homology.HomologySearch.MapToFamilies(res, venom.MyExtendedFastaItems);
                        var map = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
                        foreach (var a in asg) map[a.Peptide] = a.FamilyIds;
                        return map;
                    }

                    var idMap = ResolveFor("identified");
                    var dnMap = ResolveFor("denovo");
                    IReadOnlyList<int> None = Array.Empty<int>();

                    var describe = venom.MyExtendedFastaItems
                        .GroupBy(i => i.UserID)
                        .ToDictionary(g => g.Key, g => g.First().Description ?? string.Empty);

                    FamilyEnvelopeQuantResult fam = FamilyEnvelopeQuant.Build(
                        store.Rows,
                        s => idMap.TryGetValue(s, out var v) ? v : None,
                        s => dnMap.TryGetValue(s, out var v) ? v : None,
                        id => describe.TryGetValue(id, out var d) ? d : string.Empty);

                    Check("Q3 real: the envelope family rollup names at least one venom family",
                        fam.Families.Count > 0, fam.Families.Count.ToString());
                    // The family sums may overlap (shared peptides), but each family's identified share
                    // cannot exceed the run's whole identified envelope - a per-family sanity floor.
                    Check("Q3 real: no single family claims more identified envelope than the whole run",
                        fam.Families.All(fm => fm.IdentifiedEnvelope <= f.IdentifiedEnvelope + 1e-3),
                        fam.Families.Count == 0 ? "0" : fam.Families.Max(fm => fm.IdentifiedEnvelope).ToString("E4", CultureInfo.InvariantCulture));
                    Console.WriteLine($"        envelope family rollup: {fam.Families.Count:N0} venom families");
                    foreach (var fm in fam.Families.Take(5))
                    {
                        string desc = fm.Description.Length > 48 ? fm.Description.Substring(0, 48) : fm.Description;
                        Console.WriteLine($"        family {fm.FamilyUserId,5}  env {fm.TotalEnvelope.ToString("E4", CultureInfo.InvariantCulture)}  " +
                            $"(id {fm.IdentifiedPeptides}, dn {fm.DeNovoPeptides})  {desc}");
                    }
                }
                else
                {
                    Console.WriteLine("        (envelope family rollup skipped - venom family file unavailable)");
                }
            }
            else
            {
                Console.WriteLine("        (envelope family rollup skipped - venom T-R not present)");
            }

            Console.WriteLine("        PAPER NUMBERS (run _01, native, per-peptide dedup over TIC): " +
                $"identified M+0 {f.IdentifiedFractionM0.ToString("P2", CultureInfo.InvariantCulture)} | " +
                $"identified envelope {f.IdentifiedFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture)} | " +
                $"DDA+de novo {f.RecoveredFractionEnvelope.ToString("P2", CultureInfo.InvariantCulture)}");
        }
    }
}
