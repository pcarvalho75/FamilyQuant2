using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static class ThreeWayAudit
    {
        public const string Argument = "--three-way-audit";

        public static void Run(string peaksDirectory, string desktopDirectory)
        {
            List<PatternLabPsm> patternLab = SeproPsmAdapter.Load(Path.Combine(desktopDirectory, "PLVI.sepr2"));
            var peaksDb = new List<PeaksDatabasePsm>();
            var deNovo = new List<PeaksDeNovoRow>();
            foreach (string path in Directory.GetFiles(peaksDirectory, "db.psms.csv", SearchOption.AllDirectories))
                peaksDb.AddRange(PeaksDatabasePsmReader.Read(path));
            foreach (string path in Directory.GetFiles(peaksDirectory, "*.denovoOnly.csv", SearchOption.AllDirectories))
                deNovo.AddRange(DeNovoFilter.Apply(PeaksCsvReader.ReadDeNovoOnly(path), new DeNovoFilterCriteria()));

            IdentificationConcordanceSummary concordance = IdentificationConcordanceStudy.Build(
                patternLab, peaksDb, deNovo);

            string Key(string file, int scan) => DeNovoReconciler.NormalizeRunName(file) + "\t" +
                scan.ToString(CultureInfo.InvariantCulture);
            var dbByScan = peaksDb.GroupBy(p => Key(p.FileName, p.ScanNumber), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Minus10LogP).ToList(), StringComparer.OrdinalIgnoreCase);
            var plByScan = patternLab.GroupBy(p => Key(p.FileName, p.ScanNumber), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.XCorr).ToList(), StringComparer.OrdinalIgnoreCase);

            int plShared = 0, plAnyExact = 0, plSameCharge = 0, plSameChargeExact = 0;
            foreach (var group in plByScan)
            {
                if (!dbByScan.TryGetValue(group.Key, out List<PeaksDatabasePsm>? db)) continue;
                plShared++;
                if (group.Value.Any(p => db.Any(q => DeNovoReconciler.SequencesEqualIgnoringPtm(p.Sequence, q.Sequence))))
                    plAnyExact++;
                if (group.Value.Any(p => db.Any(q => p.ChargeState == q.ChargeState))) plSameCharge++;
                if (group.Value.Any(p => db.Any(q => p.ChargeState == q.ChargeState
                    && DeNovoReconciler.SequencesEqualIgnoringPtm(p.Sequence, q.Sequence)))) plSameChargeExact++;
            }

            int dnSharedPl = 0, dnSameChargePl = 0, dnSameChargeMz = 0, dnSameChargeMzExact = 0;
            int dnSharedDb = 0, dnSameChargeDb = 0, dnSameChargeDbExact = 0;
            foreach (PeaksDeNovoRow dn in deNovo)
            {
                string key = Key(dn.RunName, dn.Scan);
                if (plByScan.TryGetValue(key, out List<PatternLabPsm>? pl))
                {
                    dnSharedPl++;
                    List<PatternLabPsm> charge = pl.Where(p => p.ChargeState == dn.Charge).ToList();
                    if (charge.Count > 0) dnSameChargePl++;
                    List<PatternLabPsm> mz = charge.Where(p => Math.Abs(p.PrecursorMz - dn.Mz) / dn.Mz * 1e6 <= 20).ToList();
                    if (mz.Count > 0)
                    {
                        dnSameChargeMz++;
                        if (mz.Any(p => DeNovoReconciler.SequencesEqualIgnoringPtm(dn.Peptide, p.Sequence)))
                            dnSameChargeMzExact++;
                    }
                }
                if (dbByScan.TryGetValue(key, out List<PeaksDatabasePsm>? db))
                {
                    dnSharedDb++;
                    List<PeaksDatabasePsm> charge = db.Where(p => p.ChargeState == dn.Charge).ToList();
                    if (charge.Count > 0) dnSameChargeDb++;
                    if (charge.Any(p => DeNovoReconciler.SequencesEqualIgnoringPtm(dn.Peptide, p.Sequence)))
                        dnSameChargeDbExact++;
                }
            }

            Console.WriteLine("PatternLab PSM rows: " + patternLab.Count.ToString("N0"));
            Console.WriteLine("PEAKS database PSM rows: " + peaksDb.Count.ToString("N0"));
            Console.WriteLine("PRIMARY PatternLab-vs-PEAKS precursor-qualified one-to-one PSM pairs: " +
                concordance.PrecursorQualifiedDatabaseSearchPairs.ToString("N0") +
                "; exact PTM-cleaned sequence " +
                concordance.ExactPrecursorQualifiedDatabaseSearchPairs.ToString("N0") + " (" +
                (100 * concordance.PrecursorQualifiedStrictAgreement).ToString("F2",
                    CultureInfo.InvariantCulture) + "%); additional I/L-equivalent " +
                concordance.AdditionalIleLeuEquivalentPrecursorQualifiedPairs.ToString("N0") +
                "; qualified scans " + concordance.PrecursorQualifiedDatabaseSearchScanKeys.ToString("N0") +
                " / raw shared scans " + concordance.SharedDatabaseSearchScanKeys.ToString("N0"));
            Console.WriteLine("PatternLab scan keys also in PEAKS database PSMs: " + plShared.ToString("N0") +
                "; any exact sequence " + plAnyExact.ToString("N0") +
                "; any same charge " + plSameCharge.ToString("N0") +
                "; exact sequence at same charge " + plSameChargeExact.ToString("N0"));
            Console.WriteLine("Filtered PEAKS de novo rows: " + deNovo.Count.ToString("N0"));
            Console.WriteLine("vs PatternLab: shared scan " + dnSharedPl.ToString("N0") +
                "; same charge " + dnSameChargePl.ToString("N0") +
                "; same charge+m/z20ppm " + dnSameChargeMz.ToString("N0") +
                "; exact sequence there " + dnSameChargeMzExact.ToString("N0"));
            Console.WriteLine("vs PEAKS database: shared scan " + dnSharedDb.ToString("N0") +
                "; same charge " + dnSameChargeDb.ToString("N0") +
                "; exact sequence at same charge " + dnSameChargeDbExact.ToString("N0"));
        }
    }
}
