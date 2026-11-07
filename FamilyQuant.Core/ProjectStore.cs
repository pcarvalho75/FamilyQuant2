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
    /// <summary>
    /// The .fqproj container: a single-file ZIP holding a project's curation, its stored per-peptide
    /// quant table, and the manifest describing how that table was made.
    ///
    /// WHY A CONTAINER. Curation (the families, the locks, the flags) and quantitation (the extracted
    /// areas) used to live apart: the .fcf held the first and the numbers were recomputed on every load.
    /// Phase Q2 makes FamilyQuant its own quantitation engine, so the numbers are now expensive to
    /// produce and worth keeping. The container carries both, keyed together, so a saved project reopens
    /// with the areas it was computed with instead of recomputing them.
    ///
    /// FOUR ENTRIES, EACH A FORMAT A HUMAN OR A SCRIPT CAN READ:
    ///   * curation.json   - the FamilyClusterFile, in exactly the JSON a .fcf holds, so nothing about
    ///                       curation changes and an old .fcf migrates by being read and re-saved here.
    ///   * quant.tsv.gz    - the per-peptide quant table, a GZip-compressed tab-separated file. Readable
    ///                       in pandas with pd.read_csv(path, sep="\t", compression="gzip"). Invariant
    ///                       decimals, round-trippable doubles.
    ///   * manifest.json   - the parameters, the per-run TIC, the app version, the timestamps and the
    ///                       per-quantity provenance.
    ///   * spectrum_comparison.jsonl.gz - the optional compute-time spectrum evidence table, one JSON
    ///                       object per line in a GZip stream. Version 1 embedded this potentially huge
    ///                       table in manifest.json; version 2 streams it separately so opening a project
    ///                       never materialises a multi-hundred-megabyte manifest string.
    ///
    /// WHY TSV.GZ AND NOT PARQUET. Parquet was the preferred table format, but Parquet.Net pulls
    /// IronCompress, which ships native compression binaries in a runtimes/ folder, and Snappier, which
    /// carries a known high-severity advisory. A native dependency and a flagged transitive package are
    /// both unacceptable in a ClickOnce-bundled app, so the documented fallback is used: a GZip TSV that
    /// needs no new dependency, is fully deterministic under InvariantCulture, and pandas reads directly.
    /// </summary>
    public static class ProjectStore
    {
        public const int CurrentFormatVersion = ProjectFileSchema.CurrentFormatVersion;
        public const string Extension = ProjectFileSchema.Extension;
        public const string CurationEntryName = ProjectFileSchema.CurationEntryName;
        public const string CurationFormat2EntryName = ProjectFileSchema.CurationFormat2EntryName;
        public const string QuantEntryName = ProjectFileSchema.QuantEntryName;
        public const string ManifestEntryName = ProjectFileSchema.ManifestEntryName;
        public const string SpectrumComparisonEntryName = ProjectFileSchema.SpectrumComparisonEntryName;
        public static readonly string[] QuantColumns = ProjectFileSchema.QuantColumns;
        public static readonly string[] LegacyQuantColumns = ProjectFileSchema.LegacyQuantColumns;
        private const int IsotopeColumnCount = ProjectFileSchema.IsotopeColumnCount;

        /// <summary>
        /// Writes a .fqproj: curation, quant table, manifest, and (when present) the streamed spectrum
        /// comparison table into one ZIP. The curation is written as curation format 2 in curation2.json, with the
        /// same shared options a .fcf uses, and curation.json keeps only the notice older builds read before they
        /// refuse the version-3 manifest; the content read back equals the plain file's. Written
        /// to a temporary file and moved into place, so a process killed mid-write leaves the previous
        /// project intact rather than a truncated one.
        /// </summary>
        public static void WriteFqproj(string path, FamilyClusterFile fcf,
            IEnumerable<QuantRow> quantRows, QuantManifest manifest, OperationContext? operation = null)
        {
            operation?.Report("project-save", "Preparing project output");
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (fcf == null) throw new ArgumentNullException(nameof(fcf));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            fcf.LastSaveCheck = null;
            var expectedCuration = Evidence.SavedFileCheck.Capture(fcf);

            if (manifest.FormatVersion > CurrentFormatVersion)
                throw new InvalidDataException(
                    "Cannot write a project whose FormatVersion is " +
                    manifest.FormatVersion.ToString(CultureInfo.InvariantCulture) +
                    ", because this FamilyQuant build writes at most version " +
                    CurrentFormatVersion.ToString(CultureInfo.InvariantCulture) +
                    ". Open and save it with a build that understands that newer format.");

            // The quant table carries a fixed area_m0..area_m5 column set (IsotopeColumnCount). A manifest
            // claiming more isotopes than there are columns would silently drop the M+6 and higher areas
            // while the pre-truncation EnvelopeSum survives, so the reloaded row's EnvelopeSum would no
            // longer equal the sum of its stored isotopes and the manifest would still claim the higher
            // count. Refuse it at write time rather than persist a lossy, self-inconsistent table.
            if (manifest.IsotopeCount > IsotopeColumnCount)
                throw new ArgumentOutOfRangeException(nameof(manifest),
                    "The quant table holds at most " + IsotopeColumnCount.ToString(CultureInfo.InvariantCulture) +
                    " isotope columns (area_m0..area_m" +
                    (IsotopeColumnCount - 1).ToString(CultureInfo.InvariantCulture) +
                    "), but the manifest requests IsotopeCount=" +
                    manifest.IsotopeCount.ToString(CultureInfo.InvariantCulture) +
                    "; storing it would silently drop the higher isotopes.");

            var rows = quantRows?.ToList() ?? new List<QuantRow>();
            List<DeNovo.SpectrumComparisonRow> spectrumRows = manifest.SpectrumComparisonRows
                ?? new List<DeNovo.SpectrumComparisonRow>();
            if (spectrumRows.Any(row => row == null))
                throw new InvalidDataException(
                    "SpectrumComparisonRows contains a null row. The project was not written because " +
                    "counting that row while omitting it from the sidecar would create an unreadable project.");

            int previousFormatVersion = manifest.FormatVersion;
            int? previousSpectrumCount = manifest.SpectrumComparisonRowCount;
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (manifest.FormatVersion <= CurrentFormatVersion) manifest.FormatVersion = CurrentFormatVersion;
                manifest.SpectrumComparisonRowCount = spectrumRows.Count;
                // A ZIP being built holds an open write handle; dispose it before the move, so the temporary
                // file is complete on disk when it replaces the target.
                using (var fileStream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
                {
                    operation?.ThrowIfCancellationRequested();
                    using (Stream curationStream = zip.CreateEntry(CurationFormat2EntryName, CompressionLevel.Optimal).Open())
                        fcf.WriteCurationFile(curationStream);
                    ProjectArchiveEntries.WriteTextEntry(zip, CurationEntryName, ProjectFileSchema.LegacyCurationNotice,
                        CompressionLevel.Optimal, operation);
                    ProjectQuantCodec.Write(zip, rows, operation);
                    if (spectrumRows.Count > 0) ProjectSpectrumCodec.Write(zip, spectrumRows, operation);
                    ProjectArchiveEntries.WriteTextEntry(zip, ManifestEntryName,
                        ProjectManifestCodec.Write(manifest), CompressionLevel.Optimal, operation);
                }

                fcf.LastSaveCheck = Evidence.SavedFileCheck.Verify(expectedCuration, temp, project: true, operation);
                Evidence.SavedFileCheck.RequirePassed(fcf.LastSaveCheck);
                operation?.Report("project-save", "Publishing project output", 1, 1);
                void Publish() => File.Move(temp, path, overwrite: true);
                if (operation == null) Publish();
                else operation.Commit(Publish);
            }
            catch
            {
                if (operation != null)
                {
                    manifest.FormatVersion = previousFormatVersion;
                    manifest.SpectrumComparisonRowCount = previousSpectrumCount;
                }
                throw;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        /// <summary>
        /// Reads a .fqproj back into its three parts. The curation goes through the same reader a .fcf uses
        /// (curation format 1 or 2, migration included), from the entry the manifest's version names.
        /// A container missing an entry, or carrying a format version this build does not understand, is
        /// refused rather than half-read.
        /// </summary>
        public static (FamilyClusterFile fcf, List<QuantRow> quant, QuantManifest manifest) ReadFqproj(string path, OperationContext? operation = null)
        {
            operation?.Report("project-read", "Opening project input", 0, 1);
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);

            // The manifest decides which entry holds the curation, so it is read and checked first.
            string manifestText = ProjectArchiveEntries.ReadTextEntry(zip, ManifestEntryName, operation)
                ?? throw new InvalidDataException(
                    "\"" + path + "\" is not a valid FamilyQuant project: its " + ManifestEntryName +
                    " entry is missing.");

            QuantManifest manifest = ProjectManifestCodec.Read(manifestText)
                ?? throw new InvalidDataException(
                    "\"" + path + "\" carries an unreadable " + ManifestEntryName + " entry.");

            if (manifest.FormatVersion > CurrentFormatVersion)
                throw new InvalidDataException(
                    "\"" + path + "\" is a version " + manifest.FormatVersion.ToString(CultureInfo.InvariantCulture) +
                    " project, and this build understands up to version " +
                    CurrentFormatVersion.ToString(CultureInfo.InvariantCulture) +
                    ". Update FamilyQuant to open it.");

            string curationEntryName = manifest.FormatVersion >= ProjectFileSchema.CurationFormat2Version
                ? CurationFormat2EntryName : CurationEntryName;
            ZipArchiveEntry curationEntry = zip.GetEntry(curationEntryName)
                ?? throw new InvalidDataException(
                    "\"" + path + "\" is not a valid FamilyQuant project: its " + curationEntryName +
                    " entry is missing.");

            FamilyClusterFile fcf = FamilyClusterFile.ReadJsonUtf8(curationEntry.Open);

            // Concordance manifests written before precursor-qualified one-to-one matching carried raw
            // shared-scan counts but left every primary-denominator field at its JSON default of zero.
            // Treating those zeros as a measured 0% agreement would be scientifically false. Refuse the
            // stale audit and require recomputation; a dataset with no shared scans remains valid.
            DeNovo.IdentificationConcordanceSummary? concordance = manifest.IdentificationConcordance;
            if (concordance != null && concordance.SharedDatabaseSearchScanKeys > 0
                && concordance.PrecursorQualifiedDatabaseSearchScanKeys == 0
                && concordance.PrecursorQualifiedDatabaseSearchPairs == 0
                && concordance.PatternLabPrecursorHypothesesInSharedScans == 0
                && concordance.PeaksDatabasePrecursorHypothesesInSharedScans == 0)
                throw new InvalidDataException(
                    "\"" + path + "\" contains a legacy database-search concordance audit with " +
                    concordance.SharedDatabaseSearchScanKeys.ToString(CultureInfo.InvariantCulture) +
                    " shared scan(s) but no precursor-qualified one-to-one PSM fields. Recompute full " +
                    "XIC with the current FamilyQuant build; raw shared scans cannot be used as the " +
                    "sequence-agreement denominator.");

            List<QuantRow> quant = ProjectQuantCodec.Read(zip, operation);

            // Version 2 stores this very large table outside manifest.json and records its exact row
            // count. Without that manifest metadata a missing sidecar was indistinguishable from a
            // legitimately empty table; a project could therefore lose its scientific evidence and
            // still open silently. Version 1 remains compatible with its embedded collection, and a
            // transitional v1 sidecar (if present) still takes precedence.
            List<DeNovo.SpectrumComparisonRow>? spectrumRows = ProjectSpectrumCodec.Read(zip, operation);
            if (manifest.FormatVersion >= 2)
            {
                if (!manifest.SpectrumComparisonRowCount.HasValue)
                    throw new InvalidDataException(
                        "\"" + path + "\" is a version-2 project whose " + ManifestEntryName +
                        " does not declare SpectrumComparisonRowCount. The spectrum evidence sidecar " +
                        "cannot be verified; recompute or re-save it with a current FamilyQuant build.");

                int expected = manifest.SpectrumComparisonRowCount.Value;
                if (expected < 0)
                    throw new InvalidDataException(
                        "\"" + path + "\" declares a negative SpectrumComparisonRowCount (" +
                        expected.ToString(CultureInfo.InvariantCulture) + ").");
                if (expected > 0 && spectrumRows == null)
                    throw new InvalidDataException(
                        "\"" + path + "\" declares " +
                        expected.ToString(CultureInfo.InvariantCulture) +
                        " spectrum comparison row(s), but its required " +
                        SpectrumComparisonEntryName + " entry is missing.");

                int actual = spectrumRows?.Count ?? 0;
                if (actual != expected)
                    throw new InvalidDataException(
                        "\"" + path + "\" declares " +
                        expected.ToString(CultureInfo.InvariantCulture) +
                        " spectrum comparison row(s), but " + SpectrumComparisonEntryName +
                        " contains " + actual.ToString(CultureInfo.InvariantCulture) +
                        ". The sidecar is incomplete or does not belong to this manifest.");

                manifest.SpectrumComparisonRows = spectrumRows
                    ?? new List<DeNovo.SpectrumComparisonRow>();
            }
            else
            {
                if (spectrumRows != null) manifest.SpectrumComparisonRows = spectrumRows;
                else manifest.SpectrumComparisonRows ??= new List<DeNovo.SpectrumComparisonRow>();
            }

            operation?.ThrowIfCancellationRequested();
            operation?.Report("project-read", "Project input read", 1, 1);
            return (fcf, quant, manifest);
        }

        /// <summary>True when the path names a container rather than a plain .fcf, by extension. The load
        /// flow branches on this so an old .fcf is still read exactly as before.</summary>
        public static bool IsProjectPath(string path) =>
            !string.IsNullOrEmpty(path) &&
            path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    }
}
