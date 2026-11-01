using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>One ranked PatternLab identification and every PEAKS de novo call carrying the exact
    /// same normalized acquisition-file name and scan number.</summary>
    public sealed class PsmDeNovoCounterpartRow
    {
        public int Rank { get; set; }
        public string PatternLabSequence { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public int Scan { get; set; }
        public double PatternLabXCorr { get; set; }
        public int PatternLabCharge { get; set; }
        public double PatternLabMz { get; set; }
        public List<PeaksDeNovoRow> PeaksCalls { get; } = new List<PeaksDeNovoRow>();
    }

    /// <summary>Auditable exact-file/exact-scan cross-engine tables. No precursor-m/z or charge filter is
    /// applied: disagreement in either is evidence to inspect, not a reason to erase a shared scan.</summary>
    public static class PsmDeNovoCounterpartStudy
    {
        public static List<PsmDeNovoCounterpartRow> TopShortPatternLabIdentifications(
            IEnumerable<PatternLabPsm> psms, IEnumerable<PeaksDeNovoRow> peaksRows,
            int maximumLengthExclusive = 8, int topN = 10)
        {
            if (psms == null) throw new ArgumentNullException(nameof(psms));
            if (peaksRows == null) throw new ArgumentNullException(nameof(peaksRows));
            if (maximumLengthExclusive <= 1) throw new ArgumentOutOfRangeException(nameof(maximumLengthExclusive));
            if (topN <= 0) throw new ArgumentOutOfRangeException(nameof(topN));

            string Key(string file, int scan) => DeNovoReconciler.NormalizeRunName(file) + "\t" +
                scan.ToString(CultureInfo.InvariantCulture);
            Dictionary<string, List<PeaksDeNovoRow>> byScan = peaksRows
                .GroupBy(r => Key(r.RunName, r.Scan), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key,
                    g => g.OrderByDescending(r => r.Alc).ThenBy(r => r.Peptide, StringComparer.Ordinal).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var selected = psms
                .Where(p => DeNovoReconciler.NormalizeSequenceIgnoringPtm(
                    p.Sequence, foldIsoleucineLeucine: false).Length < maximumLengthExclusive)
                .Where(p => byScan.ContainsKey(Key(p.FileName, p.ScanNumber)))
                .OrderByDescending(p => p.XCorr)
                .ThenBy(p => DeNovoReconciler.NormalizeRunName(p.FileName), StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.ScanNumber)
                .Take(topN)
                .ToList();

            var result = new List<PsmDeNovoCounterpartRow>();
            for (int i = 0; i < selected.Count; i++)
            {
                PatternLabPsm p = selected[i];
                var row = new PsmDeNovoCounterpartRow
                {
                    Rank = i + 1,
                    PatternLabSequence = p.Sequence,
                    File = DeNovoReconciler.NormalizeRunName(p.FileName),
                    Scan = p.ScanNumber,
                    PatternLabXCorr = p.XCorr,
                    PatternLabCharge = p.ChargeState,
                    PatternLabMz = p.PrecursorMz,
                };
                row.PeaksCalls.AddRange(byScan[Key(p.FileName, p.ScanNumber)]);
                result.Add(row);
            }
            return result;
        }

        public static string BuildCsv(IEnumerable<PsmDeNovoCounterpartRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var b = new StringBuilder();
            b.AppendLine("rank,patternlab_sequence,file,scan,patternlab_xcorr,patternlab_charge,patternlab_mz,peaks_call_count,peaks_results_ranked_by_alc");
            foreach (PsmDeNovoCounterpartRow row in rows)
            {
                string peaks = string.Join(" | ", row.PeaksCalls.Select(p =>
                    p.Peptide + " [ALC=" + p.Alc.ToString("0.###", CultureInfo.InvariantCulture) +
                    "%; z=" + p.Charge.ToString(CultureInfo.InvariantCulture) +
                    "; m/z=" + p.Mz.ToString("0.#####", CultureInfo.InvariantCulture) + "]"));
                CsvRow(b, row.Rank, row.PatternLabSequence, row.File, row.Scan,
                    row.PatternLabXCorr.ToString("R", CultureInfo.InvariantCulture), row.PatternLabCharge,
                    row.PatternLabMz.ToString("R", CultureInfo.InvariantCulture), row.PeaksCalls.Count, peaks);
            }
            return b.ToString();
        }

        private static void CsvRow(StringBuilder b, params object[] values) =>
            b.Append(string.Join(",", values.Select(v => Csv(Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty))))
             .Append('\n');

        private static string Csv(string value) => value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
