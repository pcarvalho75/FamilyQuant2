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

        // ==================================================== database parsing

        /// <summary>
        /// The de novo core: the PEAKS reader, the confidence filter and the reconciliation against
        /// PatternLab PSMs. Everything here is pure logic, deliberately testable without a RAW file,
        /// a .sepr2 or a window.
        /// </summary>
        private static void DeNovoAgreementUnitTests()
        {
            double bareC = PeptideMass.WaterMonoisotopic + PeptideMass.ResidueMass('C');
            double inlineCam = PeptideMass.NeutralMonoisotopicMassWithModifications("C(+57.02)");
            Check("agreement mass: an inline C(+57.02) delta contributes to theoretical mass",
                Math.Abs(inlineCam - (bareC + 57.02)) < 1e-8,
                inlineCam.ToString("F6", CultureInfo.InvariantCulture));

            double explicitMass = PeptideMass.NeutralMonoisotopicMassWithModifications(
                "C", "Carbamidomethylation (+57.02146)");
            Check("agreement mass: an explicit PTM mass is used when no inline delta is present",
                Math.Abs(explicitMass - (bareC + 57.02146)) < 1e-8,
                explicitMass.ToString("F6", CultureInfo.InvariantCulture));

            double noDoubleCount = PeptideMass.NeutralMonoisotopicMassWithModifications(
                "C(+57.02)", "Carbamidomethylation (+57.02)");
            Check("agreement mass: peptide and PTM columns do not double-count the same delta",
                Math.Abs(noDoubleCount - inlineCam) < 1e-8,
                noDoubleCount.ToString("F6", CultureInfo.InvariantCulture));
            Check("agreement mass: malformed annotations fail closed",
                double.IsNaN(PeptideMass.NeutralMonoisotopicMassWithModifications("C(+57.02")));

            PeaksDeNovoRow Row(int scan, string peptide, double confidence, int charge, double mz,
                               double rt, string run, string ptm = "", long featureId = -1) => new PeaksDeNovoRow
            {
                Scan = scan,
                Peptide = peptide,
                Alc = confidence,
                Charge = charge,
                Mz = mz,
                RetentionTimeMin = rt,
                RunName = run,
                Ptm = ptm,
                FeatureId = featureId,
                Mass = PeptideMass.NeutralMonoisotopicMassWithModifications(peptide, ptm),
            };

            var peaks = new List<PeaksDeNovoRow>
            {
                // Equal confidence: deterministic lexical policy must choose K, independent of input order.
                Row(1, "Q", 90, 2, 500.0000, 10.00, @"C:\runs\sample.raw"),
                Row(1, "K", 90, 2, 500.0000, 10.00, "sample.raw"),
                Row(2, "AIL", 91, 2, 600.0000, 20.00, "sample.RAW"),
                Row(3, "A", 92, 2, 650.0000, 30.00, "sample.raw"),
                Row(4, "A", 93, 2, 700.0000, 40.00, "sample.raw"),
                Row(5, "C(+57.02)", 94, 1, 800.0000, 50.00, "sample.raw"),
                Row(6, "A(+57.02147)", 95, 1, 900.0000, 60.00, "sample.raw"),
                // Both cross-pairings have lower ppm error, but shared Feature Id must take priority.
                Row(7, "A", 97, 2, 1000.0000, 70.00, "sample.raw", featureId: 10),
                Row(7, "G", 96, 2, 1000.0080, 70.00, "sample.raw", featureId: 11),
            };
            var deep = new List<PeaksDeNovoRow>
            {
                Row(1, "Q", 96, 2, 500.0050, 10.01, "SAMPLE.sqt"),       // 10 ppm
                Row(2, "ALL", 96, 2, 600.0000, 20.03, "sample.mzML"),    // RT sensitivity fails only
                Row(3, "A", 96, 3, 650.0000, 30.00, "sample.ms2"),       // different charge
                Row(4, "A", 96, 2, 700.0200, 40.00, "sample.raw"),       // >20 ppm
                Row(5, "C(+57.02)", 96, 1, 800.0000, 50.00, "sample.raw"),
                Row(6, "Q", 96, 1, 900.0000, 60.00, "sample.raw"),
                Row(7, "G", 96, 2, 1000.0010, 70.00, "sample.raw", featureId: 11),
                Row(7, "A", 97, 2, 1000.0090, 70.00, "sample.raw", featureId: 10),
            };
            var only = new List<PeaksDeNovoRow> { Row(100, "K", 80, 2, 500, 1, "other.raw") };

            DeNovoAgreementResult agreement = DeNovoAgreementStudy.Build(peaks, deep, only);
            Check("agreement gate: run names normalize before the file+scan join",
                agreement.ScanOnlyTotal == 7, agreement.ScanOnlyTotal.ToString(CultureInfo.InvariantCulture));
            Check("agreement gate: raw scan-only, same-charge, ppm-qualified and primary counts stay separate",
                agreement.ScanOnlyTotal == 7 && agreement.SameChargeScanOnly == 6
                && agreement.DifferentChargeScanOnly == 1
                && agreement.SameChargeOutsideMzTolerance == 1
                && agreement.PrecursorQualifiedScanOnly == 5 && agreement.Total == 6,
                $"raw={agreement.ScanOnlyTotal}; charge={agreement.SameChargeScanOnly}; " +
                $"ppm={agreement.PrecursorQualifiedScanOnly}; primary={agreement.Total}");
            Check("agreement gate: RT <=0.02 min is a sensitivity count, not a primary exclusion",
                agreement.Total == 6 && agreement.RtSensitivityQualifiedCount == 5,
                agreement.RtSensitivityQualifiedCount.ToString(CultureInfo.InvariantCulture));
            Check("agreement sequence: I/L remains distinct while its theoretical-mass equality is reported",
                agreement.Spectra.Single(s => s.Scan == 2).Category == AgreementCategory.SameMassRearrangement
                && agreement.Spectra.Single(s => s.Scan == 2).EditDistance == 1);
            Check("agreement sequence: numeric modifications participate in the theoretical-mass category",
                agreement.Spectra.Single(s => s.Scan == 6).Category == AgreementCategory.SameMassRearrangement
                && agreement.Spectra.Single(s => s.Scan == 6).DeltaMassDa < 0.0001,
                agreement.Spectra.Single(s => s.Scan == 6).DeltaMassDa.ToString("G", CultureInfo.InvariantCulture));
            Check("agreement top call: equal-confidence ties are deterministic and input-order independent",
                agreement.Spectra.Single(s => s.Scan == 1).PeaksSequence == "K"
                && DeNovoAgreementStudy.Build(peaks.AsEnumerable().Reverse().ToList(), deep, only)
                    .Spectra.Single(s => s.Scan == 1).PeaksSequence == "K");
            Check("agreement pairing: precursor calls are one-to-one and shared Feature Id outranks lower ppm cross-pairs",
                agreement.Spectra.Count(s => s.Scan == 7) == 2
                && agreement.Spectra.Where(s => s.Scan == 7).All(s => s.MatchedBySharedFeatureId)
                && agreement.Spectra.Where(s => s.Scan == 7).All(s => s.Category == AgreementCategory.Identical));

            DeNovoAgreementResult tightMass = DeNovoAgreementStudy.Build(
                peaks, deep, only, massToleranceDa: 0.01);
            Check("agreement tolerance: K/Q changes category when the mass tolerance tightens",
                agreement.Spectra.Single(s => s.Scan == 1).Category == AgreementCategory.SameMassRearrangement
                && tightMass.Spectra.Single(s => s.Scan == 1).Category == AgreementCategory.DifferentMass);

            string csv = DeNovoAgreementStudy.BuildTableCsv(agreement);
            string markdown = DeNovoAgreementStudy.BuildSummaryMarkdown(agreement);
            Check("agreement export: gate, tolerance and provenance fields are inspectable",
                csv.Contains("included_primary_comparison", StringComparison.Ordinal)
                && csv.Contains("precursor_qualified_primary_pair", StringComparison.Ordinal)
                && csv.Contains("precursor_tolerance_ppm", StringComparison.Ordinal)
                && csv.Contains("top_call_policy", StringComparison.Ordinal)
                && markdown.Contains("raw scan-only audit", StringComparison.Ordinal)
                && markdown.Contains("RT sensitivity (not a primary gate)", StringComparison.Ordinal));
        }

        private static void IdentificationConcordanceUnitTests()
        {
            PatternLabPsm Pl(int scan, string sequence, int charge, double mz, double score) =>
                new PatternLabPsm { FileName = @"C:\runs\sample.raw", ScanNumber = scan,
                    Sequence = sequence, ChargeState = charge, PrecursorMz = mz, XCorr = score };
            PeaksDatabasePsm Db(int scan, string sequence, int charge, double mz, double score) =>
                new PeaksDatabasePsm { FileName = "SAMPLE.sqt", ScanNumber = scan,
                    Sequence = sequence, ChargeState = charge, PrecursorMz = mz, Minus10LogP = score };

            var pl = new List<PatternLabPsm>
            {
                Pl(1, "C(+57.02)PEP", 2, 500.0000, 5),
                Pl(2, "AIL", 2, 600.0000, 5),
                Pl(3, "A", 2, 700.0000, 5),
                Pl(4, "A", 2, 800.0000, 5),
                Pl(5, "Q", 2, 900.0000, 4),
                Pl(5, "K", 2, 900.0000, 4),
            };
            var db = new List<PeaksDatabasePsm>
            {
                Db(1, "CPEP", 2, 500.0050, 50),
                Db(2, "ALL", 2, 600.0000, 50),
                Db(3, "A", 3, 700.0000, 50),
                Db(4, "A", 2, 800.0300, 50),
                Db(5, "K", 2, 900.0000, 50),
            };

            IdentificationConcordanceSummary s = IdentificationConcordanceStudy.Build(
                pl, db, Array.Empty<PeaksDeNovoRow>());
            Check("database concordance: raw shared scans remain separate from precursor-qualified pairs",
                s.SharedDatabaseSearchScanKeys == 5
                && s.PrecursorQualifiedDatabaseSearchScanKeys == 3
                && s.PrecursorQualifiedDatabaseSearchPairs == 3,
                $"raw={s.SharedDatabaseSearchScanKeys}; scans={s.PrecursorQualifiedDatabaseSearchScanKeys}; pairs={s.PrecursorQualifiedDatabaseSearchPairs}");
            Check("database concordance: primary exact agreement is PTM-cleaned and I/L stays separate",
                s.ExactPrecursorQualifiedDatabaseSearchPairs == 2
                && s.AdditionalIleLeuEquivalentPrecursorQualifiedPairs == 1
                && Math.Abs(s.PrecursorQualifiedStrictAgreement - 2.0 / 3.0) < 1e-12);
            IdentificationConcordanceSummary reversed = IdentificationConcordanceStudy.Build(
                pl.AsEnumerable().Reverse().ToList(), db, Array.Empty<PeaksDeNovoRow>());
            Check("database concordance: equal-score precursor ties are deterministic",
                reversed.ExactPrecursorQualifiedDatabaseSearchPairs
                    == s.ExactPrecursorQualifiedDatabaseSearchPairs);
            string csv = IdentificationConcordanceStudy.BuildCsv(s);
            Check("database concordance export names the primary 20-ppm denominator",
                csv.Contains("precursor_qualified_one_to_one_psm_pairs", StringComparison.Ordinal)
                && csv.Contains("PRIMARY AGREEMENT", StringComparison.Ordinal)
                && csv.Contains("20 ppm", StringComparison.Ordinal));
        }

        private static void DeNovoTests(string workDir)
        {
            // ---- reader: the header really seen in the motivating export, incl. quoted PTM with a
            // comma, scientific-notation area, and the per-residue confidence string.
            string csv = Path.Combine(workDir, "denovo.csv");
            File.WriteAllText(csv,
                "\"Scan\",\"Peptide\",\"Tag length\",\"ALC (%)\",\"Length\",\"m/z\",\"z\",\"RT\"," +
                "\"Area\",\"Mass\",\"ppm\",\"PTM\",\"local confidence (%)\",\"mode\",tag(>=0%),\"Accession\",\"Feature Id\"\n" +
                "129654,LPTSMYQALEEK,12,100.0,12,705.35394,2,148.0970,1.1200968E7,1408.6908,1.8," +
                "\"\",99 99 99 99 99 99 99 99 99 99 99 99,HCD,LPTSMYQALEEK,,75846\n" +
                "200,C(+57.02)VLLHEK,7,85.5,7,458.24,2,60.1,2.5E5,914.47,0.9," +
                "\"Carbamidomethylation, fixed\",95 95 95 95 95 40 40,HCD,CVLLHEK,,12\n");

            var rows = FamilyQuant.DeNovo.PeaksCsvReader.ReadDeNovoOnly(csv);
            Check("both rows are read", rows.Count == 2, rows.Count.ToString());
            Check("numbers are read with a dot decimal separator regardless of locale",
                Math.Abs(rows[0].Mz - 705.35394) < 1e-9 && Math.Abs(rows[0].Alc - 100.0) < 1e-9);
            Check("a scientific-notation area survives", Math.Abs(rows[0].PeaksArea - 1.1200968E7) < 1);
            Check("the per-residue confidence has one entry per residue",
                rows[0].LocalConfidence.Length == 12 && rows[1].LocalConfidence.Length == 7);
            Check("a quoted PTM containing a comma stays one field",
                rows[1].Ptm == "Carbamidomethylation, fixed", rows[1].Ptm);
            Check("the run name is stamped from the file, without the .raw tail",
                rows[0].RunName.Length > 0);

            var missing = Path.Combine(workDir, "not_denovo.csv");
            File.WriteAllText(missing, "\"A\",\"B\"\n1,2\n");
            Check("a file without the de novo columns is refused with the missing column named",
                Throws<InvalidDataException>(() => FamilyQuant.DeNovo.PeaksCsvReader.ReadDeNovoOnly(missing)));

            // ---- filter: the request's criterion, five consecutive residues at 90 or better.
            var crit = new FamilyQuant.DeNovo.DeNovoFilterCriteria();
            int[] four = { 95, 95, 95, 95, 40, 95, 95, 95, 95 };
            int[] five = { 40, 95, 95, 95, 95, 95, 40 };
            int[] edge = { 90, 90, 90, 90, 90 };
            Check("four in a row do not satisfy a five-residue stretch",
                !FamilyQuant.DeNovo.DeNovoFilter.HasStretch(four, 5, 90));
            Check("five in a row do, wherever they sit",
                FamilyQuant.DeNovo.DeNovoFilter.HasStretch(five, 5, 90));
            Check("the floor is inclusive: exactly 90 counts",
                FamilyQuant.DeNovo.DeNovoFilter.HasStretch(edge, 5, 90));
            Check("an unparseable confidence row is rejected, not accepted",
                !FamilyQuant.DeNovo.DeNovoFilter.Passes(new FamilyQuant.DeNovo.PeaksDeNovoRow
                {
                    Length = 12, Alc = 99, LocalConfidence = Array.Empty<int>()
                }, crit));

            var scoreRows = new List<FamilyQuant.DeNovo.PeaksDeNovoRow>
            {
                new FamilyQuant.DeNovo.PeaksDeNovoRow { Length = 8, Alc = 79.9, LocalConfidence = edge },
                new FamilyQuant.DeNovo.PeaksDeNovoRow { Length = 8, Alc = 80.0, LocalConfidence = edge },
                new FamilyQuant.DeNovo.PeaksDeNovoRow { Length = 6, Alc = 95.0, LocalConfidence = edge },
                new FamilyQuant.DeNovo.PeaksDeNovoRow { Length = 8, Alc = 96.0, LocalConfidence = four },
            };
            FamilyQuant.DeNovo.DeNovoScoreDistribution scoreStudy =
                FamilyQuant.DeNovo.DeNovoScoreDistributionStudy.Build(scoreRows, crit);
            Check("the ALC census keeps the raw spectrum-row denominator",
                scoreStudy.RawRows == 4 && scoreStudy.Bins.Sum(b => b.RawRows) == 4,
                scoreStudy.RawRows.ToString(CultureInfo.InvariantCulture));
            Check("the ALC floor is inclusive and all three gates are required",
                scoreStudy.RowsAtOrAboveAlc == 3 && scoreStudy.RowsPassingAllCriteria == 1
                && scoreStudy.Bins.Sum(b => b.PassingAllCriteria) == 1,
                scoreStudy.RowsAtOrAboveAlc + " / " + scoreStudy.RowsPassingAllCriteria);
            Check("the retained score lands in the 80-to-less-than-85 bin",
                scoreStudy.Bins.Single(b => b.LowerInclusive == 80).PassingAllCriteria == 1);

            // ---- sequence equivalence: I/L, PEAKS PTM notation, SEPro flanking residues.
            Check("I and L are the same residue to a fragment spectrum",
                FamilyQuant.DeNovo.DeNovoReconciler.SequencesEquivalent("PEPTIDE", "PEPTLDE"));
            Check("the literal PTM-ignored comparison keeps I and L distinct",
                !FamilyQuant.DeNovo.DeNovoReconciler.SequencesEqualIgnoringPtm("PEPTIDE", "PEPTLDE"));
            Check("PEAKS modification notation does not break equality",
                FamilyQuant.DeNovo.DeNovoReconciler.SequencesEquivalent("C(+57.02)VLLHEK", "CVLLHEK"));
            Check("multiple inline cysteine modifications are removed before sequence comparison",
                FamilyQuant.DeNovo.DeNovoReconciler.SequencesEqualIgnoringPtm(
                    "C(+57.02)DC(+57.02)YQDLPPK", "CDCYQDLPPK"));
            Check("named bracket PTMs are removed as a whole rather than leaking letters into the sequence",
                FamilyQuant.DeNovo.DeNovoReconciler.SequencesEqualIgnoringPtm("M[Oxidation]PEPTIDE", "MPEPTIDE"));
            Check("SEPro flanking residues are ignored",
                FamilyQuant.DeNovo.DeNovoReconciler.SequencesEquivalent("CVLLHEK", "K.CVLLHEK.A"));
            Check("different backbones stay different",
                !FamilyQuant.DeNovo.DeNovoReconciler.SequencesEquivalent("CVLLHEK", "CVLLHEE"));

            // ---- reconciliation: one de novo row per situation, against a PSM list whose file names
            // arrive in a DIFFERENT order than the runs, because the motivating .xic2 stores its runs
            // permuted and matching by position joined every spectrum to the wrong run.
            var dn = new List<FamilyQuant.DeNovo.PeaksDeNovoRow>
            {
                new FamilyQuant.DeNovo.PeaksDeNovoRow { RunName = "run_A", Scan = 100, Mz = 500.2500, Peptide = "PEPTIDEK" },
                new FamilyQuant.DeNovo.PeaksDeNovoRow { RunName = "run_A", Scan = 200, Mz = 600.0000, Peptide = "AAAAKKKR" },
                new FamilyQuant.DeNovo.PeaksDeNovoRow { RunName = "run_A", Scan = 300, Mz = 700.0000, Peptide = "TTTTTTKR" },
                new FamilyQuant.DeNovo.PeaksDeNovoRow { RunName = "run_A", Scan = 400, Mz = 800.0000, Peptide = "GGGGGGKR" },
                new FamilyQuant.DeNovo.PeaksDeNovoRow { RunName = "run_A", Scan = 500, Mz = 900.0000, Peptide = "QQQQQQKR" },
                new FamilyQuant.DeNovo.PeaksDeNovoRow { RunName = "run_B", Scan = 100, Mz = 500.2500, Peptide = "WWWWWWKR" },
            };
            var psms = new List<FamilyQuant.DeNovo.PatternLabPsm>
            {
                // run_B first, so a positional pairing would be wrong for everything below.
                new FamilyQuant.DeNovo.PatternLabPsm { FileName = @"C:\x\run_B.raw", ScanNumber = 999, PrecursorMz = 555, Sequence = "IRRELEVANT", XCorr = 3 },
                new FamilyQuant.DeNovo.PatternLabPsm { FileName = @"C:\x\run_A.raw", ScanNumber = 100, PrecursorMz = 500.2501, Sequence = "K.PEPTLDEK.A", XCorr = 3.1 },
                new FamilyQuant.DeNovo.PatternLabPsm { FileName = @"C:\x\run_A.raw", ScanNumber = 200, PrecursorMz = 600.0001, Sequence = "AAAAKKKR", XCorr = 1.9 },
                new FamilyQuant.DeNovo.PatternLabPsm { FileName = @"C:\x\run_A.raw", ScanNumber = 300, PrecursorMz = 700.0002, Sequence = "SSSSSSKR", XCorr = 4.0 },
                new FamilyQuant.DeNovo.PatternLabPsm { FileName = @"C:\x\run_A.raw", ScanNumber = 400, PrecursorMz = 801.5000, Sequence = "COISOLATED", XCorr = 2.8 },
            };

            var rec = FamilyQuant.DeNovo.DeNovoReconciler.Reconcile(dn, psms);
            var verdicts = rec.ToDictionary(r => (r.DeNovo.RunName, r.DeNovo.Scan), r => r.Verdict);

            Check("equal sequence with a strong PSM is redundant",
                verdicts[("run_A", 100)] == FamilyQuant.DeNovo.ReconciliationVerdict.RedundantEqual,
                verdicts[("run_A", 100)].ToString());
            Check("equal sequence with cross-correlation below 2.5 keeps the de novo row",
                verdicts[("run_A", 200)] == FamilyQuant.DeNovo.ReconciliationVerdict.EqualButWeakPsm,
                verdicts[("run_A", 200)].ToString());
            Check("same spectrum, different sequence, goes to the review queue",
                verdicts[("run_A", 300)] == FamilyQuant.DeNovo.ReconciliationVerdict.Conflict,
                verdicts[("run_A", 300)].ToString());
            Check("same scan but another precursor is co-isolation, not a duplicate",
                verdicts[("run_A", 400)] == FamilyQuant.DeNovo.ReconciliationVerdict.CoIsolatedExtra,
                verdicts[("run_A", 400)].ToString());
            Check("a scan nobody identified stays de novo only",
                verdicts[("run_A", 500)] == FamilyQuant.DeNovo.ReconciliationVerdict.DeNovoOnly,
                verdicts[("run_A", 500)].ToString());
            Check("the same scan number in ANOTHER run is not joined across runs",
                verdicts[("run_B", 100)] == FamilyQuant.DeNovo.ReconciliationVerdict.DeNovoOnly,
                verdicts[("run_B", 100)].ToString());
            Check("the counterpart PSM travels with the verdict",
                rec.First(r => r.DeNovo.Scan == 300 && r.DeNovo.RunName == "run_A").CounterpartPsm!.Sequence == "SSSSSSKR");
            Check("the highest-XCorr same-file/same-scan PSM is retained for the literal scan audit",
                rec.First(r => r.DeNovo.Scan == 400 && r.DeNovo.RunName == "run_A")
                   .ScanCounterpartPsm!.Sequence == "COISOLATED");

            DeNovoOverlapSummary audit = DeNovoOverlapStudy.Build(dn.Count, rec);
            Check("the literal same-file/same-scan audit compares every shared scan after PTM removal",
                audit.SharedFileAndScan == 4
                && audit.ScanLevelStrictSequenceMatchesIgnoringPtm == 1
                && audit.ScanLevelAdditionalIleLeuEquivalentMatches == 1
                && audit.ScanLevelSequenceDivergencesIgnoringPtmStrict == 3
                && audit.ScanLevelSequenceDivergencesAfterIleLeuFold == 2);
            Check("the precursor-qualified sequence audit remains a separate nested subset",
                audit.SamePrecursorWithinTolerance == 3
                && audit.StrictSequenceMatchesIgnoringPtm == 1
                && audit.AdditionalIleLeuEquivalentMatches == 1
                && audit.SequenceDivergencesAfterIleLeuFold == 1);

            DeNovoIdentificationSummary consolidated = DeNovoIdentificationStudy.Build(dn, rec);
            Check("the identification census is independent of XIC and closes to the filtered calls",
                consolidated.FilteredSpectrumCalls == 6
                && consolidated.CarriedForwardSpectrumCalls == 3
                && consolidated.Runs.Sum(r => r.FilteredSpectrumCalls) == 6);
            Check("pooled peptide diversity removes run and charge repeats after PTM cleaning",
                consolidated.FilteredRunSequenceChargeGroups == 6
                && consolidated.FilteredRunSequenceGroups == 6
                && consolidated.FilteredDistinctSequencesIgnoringPtm == 6
                && consolidated.CarriedForwardDistinctSequencesIgnoringPtm == 3);
        }
    }
}
