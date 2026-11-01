using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static class ChargeAudit
    {
        public const string Argument = "--charge-audit";

        public static void Run(string peaksDirectory, string desktopDirectory)
        {
            string sepr2 = Path.Combine(desktopDirectory, "PLVI.sepr2");
            List<PatternLabPsm> psms = SeproPsmAdapter.Load(sepr2);
            var filtered = new List<PeaksDeNovoRow>();
            var peaksDb = new List<PeaksDatabasePsm>();
            foreach (string path in Directory.GetFiles(peaksDirectory, "*.denovoOnly.csv", SearchOption.AllDirectories))
                filtered.AddRange(DeNovoFilter.Apply(PeaksCsvReader.ReadDeNovoOnly(path), new DeNovoFilterCriteria()));
            foreach (string path in Directory.GetFiles(peaksDirectory, "db.psms.csv", SearchOption.AllDirectories))
                peaksDb.AddRange(PeaksDatabasePsmReader.Read(path));

            int pTotal = 0, pKnown = 0, pMatch = 0, pMismatch = 0, pRawZero = 0, pReportedZ1 = 0;
            int dTotal = 0, dKnown = 0, dMatch = 0, dMismatch = 0, dRawZero = 0, dReportedZ1 = 0;
            int bTotal = 0, bKnown = 0, bMatch = 0, bMismatch = 0, bRawZero = 0, bReportedZ1 = 0;
            foreach (string rawPath in Directory.GetFiles(desktopDirectory, "*.raw", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string run = DeNovoReconciler.NormalizeRunName(rawPath);
                List<PatternLabPsm> rp = psms.Where(p => string.Equals(
                    DeNovoReconciler.NormalizeRunName(p.FileName), run, StringComparison.OrdinalIgnoreCase)).ToList();
                List<PeaksDeNovoRow> rd = filtered.Where(p => string.Equals(
                    DeNovoReconciler.NormalizeRunName(p.RunName), run, StringComparison.OrdinalIgnoreCase)).ToList();
                List<PeaksDatabasePsm> rb = peaksDb.Where(p => string.Equals(
                    DeNovoReconciler.NormalizeRunName(p.FileName), run, StringComparison.OrdinalIgnoreCase)).ToList();
                Dictionary<int, int> rawCharge = RawScanChargeReader.Read(rawPath,
                    rp.Select(p => p.ScanNumber).Concat(rd.Select(d => d.Scan)).Concat(rb.Select(p => p.ScanNumber)));

                int pm = rp.Count(p => rawCharge.TryGetValue(p.ScanNumber, out int z) && z > 0 && z == p.ChargeState);
                int px = rp.Count(p => rawCharge.TryGetValue(p.ScanNumber, out int z) && z > 0 && z != p.ChargeState);
                int pz = rp.Count(p => !rawCharge.TryGetValue(p.ScanNumber, out int z) || z <= 0);
                int dm = rd.Count(p => rawCharge.TryGetValue(p.Scan, out int z) && z > 0 && z == p.Charge);
                int dx = rd.Count(p => rawCharge.TryGetValue(p.Scan, out int z) && z > 0 && z != p.Charge);
                int dz = rd.Count(p => !rawCharge.TryGetValue(p.Scan, out int z) || z <= 0);
                int bm = rb.Count(p => rawCharge.TryGetValue(p.ScanNumber, out int z) && z > 0 && z == p.ChargeState);
                int bx = rb.Count(p => rawCharge.TryGetValue(p.ScanNumber, out int z) && z > 0 && z != p.ChargeState);
                int bz = rb.Count(p => !rawCharge.TryGetValue(p.ScanNumber, out int z) || z <= 0);

                pTotal += rp.Count; pKnown += pm + px; pMatch += pm; pMismatch += px; pRawZero += pz;
                dTotal += rd.Count; dKnown += dm + dx; dMatch += dm; dMismatch += dx; dRawZero += dz;
                pReportedZ1 += rp.Count(p => p.ChargeState == 1);
                dReportedZ1 += rd.Count(p => p.Charge == 1);
                bTotal += rb.Count; bKnown += bm + bx; bMatch += bm; bMismatch += bx; bRawZero += bz;
                bReportedZ1 += rb.Count(p => p.ChargeState == 1);

                Console.WriteLine(run + ": PatternLab " + rp.Count.ToString("N0") +
                    ": raw-charge match " + pm.ToString("N0") + ", mismatch " + px.ToString("N0") +
                    ", raw unknown " + pz.ToString("N0") + "; filtered PEAKS de novo " +
                    rd.Count.ToString("N0") + ": match " + dm.ToString("N0") + ", mismatch " +
                    dx.ToString("N0") + ", raw unknown " + dz.ToString("N0"));
                Console.WriteLine("  PEAKS database " + rb.Count.ToString("N0") + ": raw-charge match " +
                    bm.ToString("N0") + ", mismatch " + bx.ToString("N0") + ", raw unknown " + bz.ToString("N0"));

                foreach (PatternLabPsm p in rp.Where(p => rawCharge[p.ScanNumber] > 0
                    && rawCharge[p.ScanNumber] != p.ChargeState).OrderByDescending(p => p.XCorr).Take(5))
                    Console.WriteLine("  PL mismatch: scan " + p.ScanNumber + " " + p.Sequence +
                        " reported z=" + p.ChargeState + " RAW z=" + rawCharge[p.ScanNumber] +
                        " XCorr=" + p.XCorr.ToString("0.###", CultureInfo.InvariantCulture));

                foreach (PatternLabPsm p in rp.Where(p => p.ChargeState == 1)
                    .OrderByDescending(p => p.XCorr).Take(3))
                    Console.WriteLine("  PL reported z1: scan " + p.ScanNumber + " " + p.Sequence +
                        " source=" + p.FileName + " RAW trailer z=" + rawCharge[p.ScanNumber] + " m/z=" +
                        p.PrecursorMz.ToString("0.#####", CultureInfo.InvariantCulture) +
                        " XCorr=" + p.XCorr.ToString("0.###", CultureInfo.InvariantCulture));
                PatternLabPsm? firstZ1 = rp.Where(p => p.ChargeState == 1)
                    .OrderByDescending(p => p.XCorr).FirstOrDefault();
                if (firstZ1 != null) RawScanAudit.Print(rawPath, firstZ1.ScanNumber);
            }

            Console.WriteLine("TOTAL PatternLab: " + pTotal.ToString("N0") + "; raw known " +
                pKnown.ToString("N0") + "; match " + pMatch.ToString("N0") + "; mismatch " +
                pMismatch.ToString("N0") + "; raw unknown " + pRawZero.ToString("N0") +
                "; reported z=1 " + pReportedZ1.ToString("N0"));
            Console.WriteLine("TOTAL filtered PEAKS de novo: " + dTotal.ToString("N0") + "; raw known " +
                dKnown.ToString("N0") + "; match " + dMatch.ToString("N0") + "; mismatch " +
                dMismatch.ToString("N0") + "; raw unknown " + dRawZero.ToString("N0") +
                "; reported z=1 " + dReportedZ1.ToString("N0"));
            Console.WriteLine("TOTAL PEAKS database: " + bTotal.ToString("N0") + "; raw known " +
                bKnown.ToString("N0") + "; match " + bMatch.ToString("N0") + "; mismatch " +
                bMismatch.ToString("N0") + "; raw unknown " + bRawZero.ToString("N0") +
                "; reported z=1 " + bReportedZ1.ToString("N0"));
        }
    }
}
