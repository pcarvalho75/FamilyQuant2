using FamilyQuant.Operations;
using PatternTools.FastaTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.DatabaseUpdate
{
    /// <summary>One record of the new database file, read exactly as Build families reads it: the identifier
    /// up to the first space, the rest of the header as the description, the sequence as the parser joins it.</summary>
    public sealed record DatabaseUpdateEntry(string Identifier, string Description, string Sequence);

    /// <summary>The new database file of an update: its summary and its records in file order.</summary>
    public sealed class DatabaseUpdateSource
    {
        public DatabaseSourceSummary Summary { get; }
        /// <summary>The hash of the summary and every record kept, in order. A comparison and a placement carry it,
        /// so a placement made from one file can never be applied with another file's comparison.</summary>
        public string Fingerprint { get; }
        public IReadOnlyList<DatabaseUpdateEntry> Records { get; }
        /// <summary>Records left out because they hold no residues (Build families cannot cluster them either).</summary>
        public int EmptyRecords { get; }

        private DatabaseUpdateSource(DatabaseSourceSummary summary, IReadOnlyList<DatabaseUpdateEntry> records, int empty)
        {
            Summary = summary;
            Records = records;
            EmptyRecords = empty;
            using var hash = new Evidence.LengthPrefixedSha256();
            hash.Append("Database update source v1");
            hash.Append(summary.FileName); hash.Append(summary.Sha256);
            hash.Append(summary.SequenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (DatabaseUpdateEntry record in records) { hash.Append(record.Identifier); hash.Append(record.Description); hash.Append(record.Sequence); }
            Fingerprint = hash.Finish();
        }

        /// <summary>Reads a FASTA or target-decoy file with the same parser Build families uses
        /// (USearchTools.cs:256-260). A repeated identifier is refused, as Build families refuses it.</summary>
        public static DatabaseUpdateSource Read(string path, OperationContext? operation = null)
        {
            ArgumentNullException.ThrowIfNull(path);
            operation?.Report("database-update", "Reading the new database");
            var parser = new FastaFileParser { PreserveHead = true };
            using (var reader = new StreamReader(path)) parser.ParseFile(reader, false);
            operation?.ThrowIfCancellationRequested();
            var entries = parser.MyItems.Select(i => new DatabaseUpdateEntry(i.SequenceIdentifier ?? string.Empty,
                i.Description ?? string.Empty, i.Sequence ?? string.Empty)).ToList();
            return Create(Path.GetFileName(path), FileFingerprint.Sha256(path), entries.Count, entries);
        }

        /// <summary>A source from records already in memory; the summary is given by the caller.</summary>
        public static DatabaseUpdateSource FromEntries(string fileName, string sha256, IReadOnlyList<DatabaseUpdateEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            return Create(fileName, sha256, entries.Count, entries);
        }

        private static DatabaseUpdateSource Create(string fileName, string sha256, int count, IReadOnlyList<DatabaseUpdateEntry> entries)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var repeated = new List<string>();
            foreach (DatabaseUpdateEntry entry in entries)
                if (!seen.Add(entry.Identifier) && repeated.Count < 5) repeated.Add(entry.Identifier);
            if (repeated.Count > 0)
                throw new InvalidDataException("The new database repeats the identifier \"" + string.Join("\", \"", repeated)
                    + "\". Remove the duplicate first; Build families refuses it for the same reason.");
            var kept = entries.Where(e => e.Sequence.Length > 0).ToList();
            if (kept.Count == 0) throw new InvalidDataException("The new database holds no sequences.");
            return new DatabaseUpdateSource(new DatabaseSourceSummary { FileName = fileName ?? string.Empty, Sha256 = sha256 ?? string.Empty, SequenceCount = count },
                kept, entries.Count - kept.Count);
        }
    }
}
