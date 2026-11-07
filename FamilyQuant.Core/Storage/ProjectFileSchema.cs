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
    /// <summary>The versioned on-disk contract shared by the container and table codecs.
    /// Version 1 embeds spectrum rows; version 2 streams them with an exact manifest count.</summary>
    internal static class ProjectFileSchema
    {
        /// <summary>Version 2 moves SpectrumComparisonRows from manifest.json to the optional streaming
        /// spectrum_comparison.jsonl.gz entry. The version-2 reader remains backward compatible with
        /// version-1 manifests that embedded that collection. Version 3 stores the curation as curation
        /// format 2 in <see cref="CurationFormat2EntryName"/> and leaves <see cref="CurationEntryName"/> as a
        /// small notice, so an older build reads the notice, reaches its own manifest version check and asks for
        /// an update instead of reading a curation it cannot understand. Versions 1 and 2 still open.</summary>
        internal const int CurrentFormatVersion = 3;

        /// <summary>The first version whose curation lives in <see cref="CurationFormat2EntryName"/>.</summary>
        internal const int CurationFormat2Version = 3;

        internal const string Extension = ".fqproj";

        /// <summary>The curation entry of versions 1 and 2 (curation format 1). From version 3 it holds only
        /// <see cref="LegacyCurationNotice"/>.</summary>
        internal const string CurationEntryName = "curation.json";

        /// <summary>The curation entry from version 3: curation format 2, exactly the bytes
        /// <see cref="CurationFormat.Write"/> produces for the same curation.</summary>
        internal const string CurationFormat2EntryName = "curation2.json";

        /// <summary>What a version-3 project keeps in <see cref="CurationEntryName"/>: a JSON object an older build
        /// deserializes as an empty curation without error, so its reader goes on to manifest.json and refuses the
        /// project there with "is a version 3 project ... Update FamilyQuant to open it".</summary>
        internal const string LegacyCurationNotice =
            "{\"FamilyQuantNotice\":\"This project was saved by FamilyQuant 2.1.11 or later. Its curation is in curation2.json. Update FamilyQuant to open it.\"}";

        /// <summary>The quant entry's name IS its format: a reader that extracts it gets a valid gzip
        /// stream whose decompressed content is the tab-separated table. Named .tsv.gz and not .parquet
        /// on purpose (see the type remarks).</summary>
        internal const string QuantEntryName = "quant.tsv.gz";

        internal const string ManifestEntryName = "manifest.json";

        /// <summary>The complete spectrum-evidence table, stored as a GZip-compressed JSON Lines stream.
        /// The ZIP entry itself is not compressed again, so extracting it produces a directly readable
        /// .jsonl.gz file. It is optional when a project has no spectrum-comparison rows.</summary>
        internal const string SpectrumComparisonEntryName = "spectrum_comparison.jsonl.gz";

        /// <summary>The columns of the quant table, in order. The header written is this one array, and a
        /// reader resolves every column BY NAME from the header it finds, so a table written by an older
        /// build (which had no area_m4/area_m5 and no under-sampled columns) still opens.</summary>
        internal static readonly string[] QuantColumns =
        {
            "side", "run", "scan", "precursor_mz", "charge", "sequence",
            "patternlab_xscore", "denovo_evidence_json",
            "area_m0", "area_m1", "area_m2", "area_m3", "area_m4", "area_m5",
            "envelope_sum", "under_sampled", "under_sampled_estimate", "family_userid",
        };

        /// <summary>The columns a version-1 container written before the 6-isotope basis carried. Kept so
        /// the reader can name what it is being lenient about instead of merely being lenient.</summary>
        internal static readonly string[] LegacyQuantColumns =
        {
            "side", "run", "scan", "precursor_mz", "charge", "sequence",
            "area_m0", "area_m1", "area_m2", "area_m3", "envelope_sum", "family_userid",
        };

        internal const int IsotopeColumnCount = 6;   // area_m0..area_m5

    }
}
