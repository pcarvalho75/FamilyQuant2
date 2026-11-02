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
using static FamilyQuant.ProjectFileSchema;

namespace FamilyQuant
{
    internal static class ProjectQuantCodec
    {
        /// <summary>
        /// Writes the quant table as a gzip TSV into the container. The zip entry itself is stored
        /// uncompressed (NoCompression): its bytes ARE the gzip stream, so a reader that extracts
        /// quant.tsv.gz gets a file pandas reads directly, and double-compressing it would only cost CPU
        /// for no gain. InvariantCulture throughout, doubles in round-trippable "R" form.
        /// </summary>
        internal static void Write(ZipArchive zip, List<QuantRow> rows, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = zip.CreateEntry(QuantEntryName, CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            using var gzip = new GZipStream(stream, CompressionMode.Compress);
            using var writer = new StreamWriter(gzip, new UTF8Encoding(false));

            writer.Write(string.Join("\t", QuantColumns));
            writer.Write('\n');

            foreach (QuantRow r in rows)
            {
                operation?.ThrowIfCancellationRequested();
                writer.Write(TsvField(r.Side)); writer.Write('\t');
                writer.Write(TsvField(r.Run)); writer.Write('\t');
                writer.Write(r.Scan.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.PrecursorMz.ToString("R", CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.Charge.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(TsvField(r.Sequence)); writer.Write('\t');
                writer.Write(r.PatternLabXScore.HasValue
                    ? r.PatternLabXScore.Value.ToString("R", CultureInfo.InvariantCulture)
                    : string.Empty); writer.Write('\t');
                writer.Write(TsvField(r.DeNovoEvidence != null && r.DeNovoEvidence.Count > 0
                    ? JsonSerializer.Serialize(r.DeNovoEvidence)
                    : string.Empty)); writer.Write('\t');

                for (int i = 0; i < IsotopeColumnCount; i++)
                {
                    writer.Write(IsotopeCell(r.IsotopeAreas, i));
                    writer.Write('\t');
                }

                writer.Write(r.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.UnderSampledArea.ToString("R", CultureInfo.InvariantCulture)); writer.Write('\t');
                writer.Write(r.UnderSampledIsEstimate ? "1" : "0"); writer.Write('\t');
                writer.Write(r.FamilyUserId.HasValue
                    ? r.FamilyUserId.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
                writer.Write('\n');
            }
        }

        internal static List<QuantRow> Read(ZipArchive zip, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            var rows = new List<QuantRow>();

            ZipArchiveEntry? entry = zip.GetEntry(QuantEntryName);
            if (entry == null) return rows;

            using Stream stream = entry.Open();
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);

            string? header = reader.ReadLine();
            if (header == null) return rows;

            // Columns are resolved BY NAME from the header, not by position, so a container written
            // before the 6-isotope basis (LegacyQuantColumns: no area_m4/area_m5, no under-sampled
            // columns) still opens and simply carries zeros where those columns are absent. The six
            // structural columns are required: without them the file is not this table at all.
            string[] cols = header.Split('\t');
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < cols.Length; i++) index[cols[i].Trim()] = i;

            int Column(string name) => index.TryGetValue(name, out int i) ? i : -1;

            foreach (string required in new[] { "side", "run", "scan", "precursor_mz", "charge", "sequence" })
            {
                if (Column(required) < 0)
                    throw new InvalidDataException(
                        "The quant table's header is missing the \"" + required + "\" column, so this is " +
                        "not a FamilyQuant quant table. Found: " + header);
            }

            int iSide = Column("side"), iRun = Column("run"), iScan = Column("scan");
            int iMz = Column("precursor_mz"), iCharge = Column("charge"), iSeq = Column("sequence");
            int iPatternLabXScore = Column("patternlab_xscore");
            int iDeNovoEvidence = Column("denovo_evidence_json");
            int iEnvelope = Column("envelope_sum"), iFamily = Column("family_userid");
            int iUnder = Column("under_sampled"), iUnderEstimate = Column("under_sampled_estimate");

            var isotopeColumns = new List<int>(IsotopeColumnCount);
            for (int i = 0; i < IsotopeColumnCount; i++)
            {
                int at = Column("area_m" + i.ToString(CultureInfo.InvariantCulture));
                if (at < 0) break;   // the isotope columns are contiguous and trailing
                isotopeColumns.Add(at);
            }

            string Cell(string[] f, int at) => at >= 0 && at < f.Length ? f[at] : string.Empty;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                operation?.ThrowIfCancellationRequested();
                if (line.Length == 0) continue;

                string[] f = line.Split('\t');
                if (f.Length <= iSeq) continue;

                var isotopes = new List<double>(isotopeColumns.Count);
                foreach (int at in isotopeColumns)
                {
                    string cell = Cell(f, at);
                    if (cell.Length == 0) break;   // absent isotopes are trailing; stop at the first
                    isotopes.Add(double.Parse(cell, NumberStyles.Float, CultureInfo.InvariantCulture));
                }

                string familyCell = Cell(f, iFamily);
                string envelopeCell = Cell(f, iEnvelope);
                string underCell = Cell(f, iUnder);

                string evidenceCell = Cell(f, iDeNovoEvidence);
                List<DeNovo.DeNovoEvidence> evidence = evidenceCell.Length == 0
                    ? new List<DeNovo.DeNovoEvidence>()
                    : JsonSerializer.Deserialize<List<DeNovo.DeNovoEvidence>>(evidenceCell)
                        ?? new List<DeNovo.DeNovoEvidence>();

                rows.Add(new QuantRow
                {
                    Side = Cell(f, iSide),
                    Run = Cell(f, iRun),
                    Scan = int.Parse(Cell(f, iScan), NumberStyles.Integer, CultureInfo.InvariantCulture),
                    PrecursorMz = double.Parse(Cell(f, iMz), NumberStyles.Float, CultureInfo.InvariantCulture),
                    Charge = int.Parse(Cell(f, iCharge), NumberStyles.Integer, CultureInfo.InvariantCulture),
                    Sequence = Cell(f, iSeq),
                    PatternLabXScore = Cell(f, iPatternLabXScore).Length == 0
                        ? (double?)null
                        : double.Parse(Cell(f, iPatternLabXScore), NumberStyles.Float,
                            CultureInfo.InvariantCulture),
                    DeNovoEvidence = evidence,
                    IsotopeAreas = isotopes.ToArray(),
                    EnvelopeSum = envelopeCell.Length == 0
                        ? 0.0
                        : double.Parse(envelopeCell, NumberStyles.Float, CultureInfo.InvariantCulture),
                    UnderSampledArea = underCell.Length == 0
                        ? 0.0
                        : double.Parse(underCell, NumberStyles.Float, CultureInfo.InvariantCulture),
                    UnderSampledIsEstimate = Cell(f, iUnderEstimate) == "1",
                    FamilyUserId = familyCell.Length == 0
                        ? (int?)null
                        : int.Parse(familyCell, NumberStyles.Integer, CultureInfo.InvariantCulture),
                });
            }

            return rows;
        }

        private static string IsotopeCell(double[]? isotopes, int i) =>
            isotopes != null && i < isotopes.Length
                ? isotopes[i].ToString("R", CultureInfo.InvariantCulture)
                : string.Empty;

        /// <summary>Makes a value safe inside a tab-separated field: a tab, a newline or a carriage
        /// return in a sequence or a run name would split or truncate the row, so they are replaced with
        /// a space. Sequences and run names never legitimately contain them; this is a guard, not a
        /// transform.</summary>
        private static string TsvField(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOf('\t') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value) sb.Append(c == '\t' || c == '\n' || c == '\r' ? ' ' : c);
            return sb.ToString();
        }
    }
}
