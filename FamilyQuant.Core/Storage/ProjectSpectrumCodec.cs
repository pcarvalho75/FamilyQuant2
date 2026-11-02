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

namespace FamilyQuant
{
    internal static class ProjectSpectrumCodec
    {
        private static readonly JsonSerializerOptions SpectrumComparisonJsonOptions = new();
        /// <summary>
        /// Streams the spectrum comparison table into a gzip JSON-Lines entry. Only one row-sized JSON
        /// string exists at a time, avoiding the former manifest-wide allocation. The enclosing ZIP uses
        /// NoCompression because these bytes already form a gzip file and double compression adds CPU
        /// without making the extracted entry directly useful.
        /// </summary>
        internal static void Write(ZipArchive zip,
            IEnumerable<DeNovo.SpectrumComparisonRow> rows, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = zip.CreateEntry(ProjectFileSchema.SpectrumComparisonEntryName,
                CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            using var gzip = new GZipStream(stream, CompressionMode.Compress);
            using var writer = new StreamWriter(gzip, new UTF8Encoding(false));

            foreach (DeNovo.SpectrumComparisonRow row in rows)
            {
                operation?.ThrowIfCancellationRequested();
                if (row == null) continue;
                writer.Write(JsonSerializer.Serialize(row, SpectrumComparisonJsonOptions));
                writer.Write('\n');
            }
        }

        /// <summary>
        /// Reads the optional version-2 spectrum comparison entry one line at a time. Null means there
        /// was no sidecar and lets the caller retain version-1 rows embedded in manifest.json; an empty
        /// list means the sidecar existed and was empty. Malformed rows are rejected with their physical
        /// line number rather than silently producing an incomplete scientific table.
        /// </summary>
        internal static List<DeNovo.SpectrumComparisonRow>? Read(ZipArchive zip, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry? entry = zip.GetEntry(ProjectFileSchema.SpectrumComparisonEntryName);
            if (entry == null) return null;

            var rows = new List<DeNovo.SpectrumComparisonRow>();
            using Stream stream = entry.Open();
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);

            string? line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                operation?.ThrowIfCancellationRequested();
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    DeNovo.SpectrumComparisonRow? row =
                        JsonSerializer.Deserialize<DeNovo.SpectrumComparisonRow>(line,
                            SpectrumComparisonJsonOptions);
                    if (row == null)
                        throw new JsonException("The row deserialised to null.");
                    rows.Add(row);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException(
                        "The " + ProjectFileSchema.SpectrumComparisonEntryName + " entry contains invalid JSON at line " +
                        lineNumber.ToString(CultureInfo.InvariantCulture) + ".", ex);
                }
            }

            return rows;
        }

    }
}
