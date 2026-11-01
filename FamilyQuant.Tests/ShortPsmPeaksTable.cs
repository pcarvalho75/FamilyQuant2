using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests
{
    internal static class ShortPsmPeaksTable
    {
        public const string Argument = "--short-psm-peaks-table";
        public const string FileName = "top10_short_patternlab_with_peaks.csv";

        public static string Run(string peaksDirectory, string desktopDirectory)
        {
            string sepr2 = Path.Combine(desktopDirectory, "PLVI.sepr2");
            if (!File.Exists(sepr2)) throw new FileNotFoundException("PLVI.sepr2 was not found.", sepr2);
            if (!Directory.Exists(peaksDirectory))
                throw new DirectoryNotFoundException("The PEAKS export directory was not found: " + peaksDirectory);

            List<PatternLabPsm> psms = SeproPsmAdapter.Load(sepr2);
            var peaks = new List<PeaksDeNovoRow>();
            foreach (string path in Directory.GetFiles(peaksDirectory, "*.denovoOnly.csv", SearchOption.AllDirectories))
                peaks.AddRange(PeaksCsvReader.ReadDeNovoOnly(path));

            List<PsmDeNovoCounterpartRow> rows = PsmDeNovoCounterpartStudy
                .TopShortPatternLabIdentifications(psms, peaks, maximumLengthExclusive: 8, topN: 10);
            string folder = Path.Combine(desktopDirectory, TalkFigures.TalkFolderName);
            Directory.CreateDirectory(folder);
            string pathOut = Path.Combine(folder, FileName);
            File.WriteAllText(pathOut, PsmDeNovoCounterpartStudy.BuildCsv(rows), new UTF8Encoding(false));

            Console.WriteLine("rank | PatternLab sequence | XCorr | file | scan | PEAKS result(s), highest ALC first");
            Console.WriteLine("-----|---------------------|-------|------|------|-----------------------------------");
            foreach (PsmDeNovoCounterpartRow row in rows)
            {
                string calls = string.Join(" | ", row.PeaksCalls.Select(p => p.Peptide +
                    " (ALC " + p.Alc.ToString("0.0", CultureInfo.InvariantCulture) + "%, z=" +
                    p.Charge.ToString(CultureInfo.InvariantCulture) + ")"));
                Console.WriteLine(row.Rank.ToString(CultureInfo.InvariantCulture) + " | " +
                    row.PatternLabSequence + " | " + row.PatternLabXCorr.ToString("0.###", CultureInfo.InvariantCulture) +
                    " | " + row.File + " | " + row.Scan.ToString(CultureInfo.InvariantCulture) + " | " + calls);
            }
            if (rows.Count > 0)
            {
                string rawPath = Path.Combine(desktopDirectory, rows[0].File + ".raw");
                RawScanAudit.Print(rawPath, rows[0].Scan);
            }
            Console.WriteLine("written: " + pathOut);
            return pathOut;
        }
    }
}
