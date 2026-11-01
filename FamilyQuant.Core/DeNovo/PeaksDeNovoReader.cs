using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>One source's sequence claim for an extracted precursor. A union keeps all of these while
    /// selecting exactly one representative sequence for the one XIC that precursor owns.</summary>
    public sealed class DeNovoEvidence
    {
        public string SourceId { get; set; } = string.Empty;
        public string SourceDisplayName { get; set; } = string.Empty;
        public string ConfidenceField { get; set; } = string.Empty;
        public double ConfidenceValue { get; set; }
        public string Sequence { get; set; } = string.Empty;
        public string InputFilePath { get; set; } = string.Empty;
        public string InputFileSha256 { get; set; } = string.Empty;
    }

    /// <summary>
    /// One row of a PEAKS "de novo only" export: a spectrum PEAKS sequenced that its own database
    /// search could not identify.
    /// </summary>
    public class PeaksDeNovoRow
    {
        private double confidenceValue;

        public int Scan { get; set; }
        public string Peptide { get; set; } = string.Empty;
        /// <summary>The source's confidence value. The historical property name remains for source
        /// compatibility, but callers displaying it must use <see cref="ConfidenceField"/>: the value is
        /// PEAKS ALC for PEAKS and DeepNovo SCORE for DeepNovo, and those scales are not interchangeable.</summary>
        public double Alc { get => confidenceValue; set => confidenceValue = value; }

        public double ConfidenceValue { get => confidenceValue; set => confidenceValue = value; }

        public string ConfidenceField { get; set; } = "ALC (%)";

        public string SourceId { get; set; } = DeNovoSource.DenovoOnly.Id;

        public string SourceDisplayName { get; set; } = DeNovoSource.DenovoOnly.DisplayName;

        /// <summary>Canonical source path and content fingerprint. Both are persisted downstream; the
        /// fingerprint is the identity, while the path tells a human where the bytes were read from.</summary>
        public string InputFilePath { get; set; } = string.Empty;

        public string InputFileSha256 { get; set; } = string.Empty;

        /// <summary>Every source claim on this precursor after deterministic union resolution, including
        /// the selected row itself. Empty on legacy/in-memory rows that never passed through the reader.</summary>
        public List<DeNovoEvidence> SourceEvidence { get; set; } = new List<DeNovoEvidence>();
        public int Length { get; set; }
        public double Mz { get; set; }
        public int Charge { get; set; }
        public double RetentionTimeMin { get; set; }

        /// <summary>The neutral monoisotopic peptide mass PEAKS reports in the "Mass" column, in Da.
        /// Optional: 0 when the source has no "Mass" column. Read only, never used to drive an
        /// extraction; a study that plots mass takes it from here rather than recomputing it.</summary>
        public double Mass { get; set; }

        /// <summary>PEAKS' own feature area. Kept for cross-checks, never used as the reported XIC:
        /// the reported number must come from the same extraction machinery as the PatternLab side,
        /// or the two series of the final plot would not be comparable.</summary>
        public double PeaksArea { get; set; }

        public double Ppm { get; set; }
        public string Ptm { get; set; } = string.Empty;

        /// <summary>Per-residue local confidence, 0-100, one entry per residue of <see cref="Peptide"/>
        /// (net of PTM annotations).</summary>
        public int[] LocalConfidence { get; set; } = Array.Empty<int>();

        /// <summary>The PEAKS feature this spectrum was assigned to, or -1. Links into the features
        /// export, which carries the RT begin/end used to bound XIC extraction.</summary>
        public long FeatureId { get; set; } = -1;

        /// <summary>The run this row belongs to, as the RAW file name without extension. PEAKS exports
        /// one folder per run; the reader stamps this from the file it read.</summary>
        public string RunName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Reads PEAKS CSV exports. Header-driven on purpose: the column set differs between PEAKS
    /// versions and even between files of one export (the tag column is "tag(&gt;=0%)" in the de novo
    /// file and "tag(&gt;=0.0%)" in the PSM file), so positional parsing would break silently.
    ///
    /// All numbers are parsed with InvariantCulture: the files use dots and scientific notation
    /// ("1.1200968E7") regardless of the machine's locale, and this application has already been
    /// bitten once by locale-sensitive parsing.
    /// </summary>
    public static class PeaksCsvReader
    {
        /// <summary>Reads a PEAKS de novo export whose confidence column is the default "ALC (%)".
        /// Kept so every existing caller and test is untouched; it delegates to the overload.</summary>
        public static List<PeaksDeNovoRow> ReadDeNovoOnly(string path)
            => Read(path, DeNovoSource.DenovoOnly);

        /// <summary>Reads one configured source and stamps every observation with stable source identity,
        /// the source's own confidence-field name, canonical input path and SHA-256 fingerprint.</summary>
        public static List<PeaksDeNovoRow> Read(string path, DeNovoSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return ReadCore(path, source.ConfidenceColumn, source.Id, source.DisplayName);
        }

        /// <summary>
        /// Reads a PEAKS de novo export, taking its per-spectrum confidence from
        /// <paramref name="confidenceColumn"/> into <see cref="PeaksDeNovoRow.Alc"/>. "ALC (%)" is the
        /// PEAKS de novo column; DeepNovo names the same idea "SCORE (%)". The two scales are NOT the same
        /// units, so a caller that mixes sources must label each panel with its own field; here the value
        /// only populates .Alc so the existing filter and every downstream reader keep working unchanged.
        ///
        /// Parsing is BY NAME (header lookup), so a source that drops "Accession" or adds "Delta RT"
        /// parses correctly with no code change: only the columns this method Requires by name must be
        /// present, and "RT" and "Delta RT" are distinct keys that never collide.
        /// </summary>
        public static List<PeaksDeNovoRow> ReadDeNovoOnly(string path, string confidenceColumn = "ALC (%)")
            => ReadCore(path, confidenceColumn,
                string.Equals(confidenceColumn, DeNovoSource.DeepNovo.ConfidenceColumn,
                    StringComparison.OrdinalIgnoreCase)
                    ? DeNovoSource.DeepNovo.Id : DeNovoSource.DenovoOnly.Id,
                string.Equals(confidenceColumn, DeNovoSource.DeepNovo.ConfidenceColumn,
                    StringComparison.OrdinalIgnoreCase)
                    ? DeNovoSource.DeepNovo.DisplayName : DeNovoSource.DenovoOnly.DisplayName);

        private static List<PeaksDeNovoRow> ReadCore(string path, string confidenceColumn,
                                                      string sourceId, string sourceDisplayName)
        {
            if (string.IsNullOrEmpty(confidenceColumn))
                throw new ArgumentException("A confidence column name is required.", nameof(confidenceColumn));

            string inputPath = Path.GetFullPath(path);
            string inputFingerprint = FileFingerprint.Sha256(inputPath);

            using var reader = new StreamReader(inputPath);
            string? headerLine = reader.ReadLine();
            if (headerLine == null) return new List<PeaksDeNovoRow>();

            var header = IndexHeader(SplitCsvLine(headerLine));

            int iScan = Require(header, path, "Scan");
            int iPep = Require(header, path, "Peptide");
            int iAlc = Require(header, path, confidenceColumn);
            int iLen = Require(header, path, "Length");
            int iMz = Require(header, path, "m/z");
            int iZ = Require(header, path, "z");
            int iRt = Require(header, path, "RT");
            int iConf = Require(header, path, "local confidence (%)");
            int iArea = Optional(header, "Area");
            int iMass = Optional(header, "Mass");
            int iPpm = Optional(header, "ppm");
            int iPtm = Optional(header, "PTM");
            int iFeat = Optional(header, "Feature Id");

            string run = Path.GetFileName(path);
            int rawTag = run.IndexOf(".raw", StringComparison.OrdinalIgnoreCase);
            if (rawTag > 0) run = run.Substring(0, rawTag);

            var rows = new List<PeaksDeNovoRow>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                var f = SplitCsvLine(line);
                if (f.Count <= iConf) continue;   // truncated line; skip rather than misparse

                var row = new PeaksDeNovoRow
                {
                    Scan = int.Parse(f[iScan], CultureInfo.InvariantCulture),
                    Peptide = f[iPep],
                    Alc = double.Parse(f[iAlc], CultureInfo.InvariantCulture),
                    ConfidenceField = confidenceColumn,
                    SourceId = sourceId,
                    SourceDisplayName = sourceDisplayName,
                    InputFilePath = inputPath,
                    InputFileSha256 = inputFingerprint,
                    Length = int.Parse(f[iLen], CultureInfo.InvariantCulture),
                    Mz = double.Parse(f[iMz], NumberStyles.Float, CultureInfo.InvariantCulture),
                    Charge = int.Parse(f[iZ], CultureInfo.InvariantCulture),
                    RetentionTimeMin = double.Parse(f[iRt], NumberStyles.Float, CultureInfo.InvariantCulture),
                    PeaksArea = iArea >= 0 && f[iArea].Length > 0
                        ? double.Parse(f[iArea], NumberStyles.Float, CultureInfo.InvariantCulture) : 0,
                    Mass = iMass >= 0 && f.Count > iMass && f[iMass].Length > 0
                        ? double.Parse(f[iMass], NumberStyles.Float, CultureInfo.InvariantCulture) : 0,
                    Ppm = iPpm >= 0 && f[iPpm].Length > 0
                        ? double.Parse(f[iPpm], NumberStyles.Float, CultureInfo.InvariantCulture) : 0,
                    Ptm = iPtm >= 0 ? f[iPtm] : string.Empty,
                    LocalConfidence = ParseConfidence(f[iConf]),
                    FeatureId = iFeat >= 0 && f[iFeat].Length > 0
                        ? long.Parse(f[iFeat], CultureInfo.InvariantCulture) : -1,
                    RunName = run,
                };
                row.SourceEvidence.Add(new DeNovoEvidence
                {
                    SourceId = row.SourceId,
                    SourceDisplayName = row.SourceDisplayName,
                    ConfidenceField = row.ConfidenceField,
                    ConfidenceValue = row.ConfidenceValue,
                    Sequence = row.Peptide,
                    InputFilePath = row.InputFilePath,
                    InputFileSha256 = row.InputFileSha256,
                });
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>"99 99 82 100" into integers; tolerant of doubled spaces, empty on garbage.</summary>
        internal static int[] ParseConfidence(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<int>();

            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]))
                    return Array.Empty<int>();
            }
            return result;
        }

        private static Dictionary<string, int> IndexHeader(List<string> fields)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fields.Count; i++)
            {
                if (!map.ContainsKey(fields[i])) map.Add(fields[i], i);
            }
            return map;
        }

        private static int Require(Dictionary<string, int> header, string path, string name)
        {
            if (header.TryGetValue(name, out int i)) return i;
            throw new InvalidDataException(
                "The file does not look like the selected de novo export: the column \"" + name +
                "\" is missing.\n\nFile: " + path +
                "\nColumns found: " + string.Join(", ", header.Keys));
        }

        private static int Optional(Dictionary<string, int> header, string name)
            => header.TryGetValue(name, out int i) ? i : -1;

        /// <summary>RFC 4180 splitting: quoted fields may hold commas and doubled quotes. PEAKS quotes
        /// the PTM and description columns, which regularly contain both.</summary>
        internal static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else current.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }

            fields.Add(current.ToString());
            return fields;
        }
    }
}
