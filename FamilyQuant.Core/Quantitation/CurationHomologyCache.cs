using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace FamilyQuant;

/// <summary>Disposable external cache of compact curation hits. A miss or invalid file searches normally.</summary>
public static class CurationHomologyCache
{
    // Bump whenever the search, serialization or localization semantics change.
    private const string Format = "FamilyQuant curation hits v1";
    private const int DigestSize = 32;
    private const int MaximumTextBytes = 16 * 1024 * 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly JsonSerializerOptions RowJson = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    /// <param name="cacheDirectory">Optional private test or caller cache. The default is the program's local cache folder.</param>
    /// <param name="progress">Called with true for a verified cache hit and false before a fresh search.</param>
    public static CurationHomologyHits GetOrSearch(IReadOnlyList<QuantRow> rows, FamilyClusterFile proteins,
        string? cacheDirectory = null, HomologyOptions? options = null,
        bool identifiedBestScoreOnly = FamilyEnvelopeQuant.IdentifiedBestScoreOnlyDefault,
        Action<bool>? progress = null, CancellationToken cancellationToken = default,
        FamilyQuant.Operations.OperationContext? operation = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(proteins);
        cancellationToken.ThrowIfCancellationRequested();
        operation?.Report("attribution-cache", "Checking the peptide-to-protein hits cache", 0, 1);
        var requested = (options ?? new HomologyOptions()).Clone();
        string directory = cacheDirectory ?? Path.Combine(ApplicationPaths.LocalDataDirectory, FullXicComputer.CacheFolderName, "curation-homology");
        string? key = null, path = null;
        try
        {
            key = Fingerprint(rows, proteins, requested, identifiedBestScoreOnly, cancellationToken);
            path = Path.Combine(directory, key + ".fqhits");
            if (TryRead(path, key, rows, proteins, requested, identifiedBestScoreOnly, cancellationToken, out var loaded)
                && key == Fingerprint(rows, proteins, requested, identifiedBestScoreOnly, cancellationToken))
            {
                progress?.Invoke(true);
                operation?.Report("attribution-cache", "Peptide-to-protein hits read from the cache", 1, 1);
                cancellationToken.ThrowIfCancellationRequested();
                return loaded!;
            }
        }
        catch (Exception ex) when (IsCacheFailure(ex)) { /* cache is optional; preserve the fresh search */ }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke(false);
        operation?.Report("attribution-cache", "Searching the stored peptide sequences against the proteins", 1, 1);
        cancellationToken.ThrowIfCancellationRequested();
        var hits = FamilyEnvelopeQuant.SearchCuration(rows, proteins, requested, identifiedBestScoreOnly, operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (path != null && key != null)
        {
            try
            {
                // A changed input must never publish a different search under the earlier key.
                if (key == Fingerprint(rows, proteins, requested, identifiedBestScoreOnly, cancellationToken))
                    Write(path, key, hits, cancellationToken);
            }
            catch (Exception ex) when (IsCacheFailure(ex)) { /* an unwritable cache must not lose valid hits */ }
        }
        return hits;
    }

    private static bool IsCacheFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or JsonException or NotSupportedException or ArgumentException or OverflowException or InvalidOperationException;

    private static string Fingerprint(IReadOnlyList<QuantRow> rows, FamilyClusterFile proteins, HomologyOptions options,
        bool identifiedBestScoreOnly, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(byte[] bytes)
        {
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(size, bytes.Length);
            hash.AppendData(size); hash.AppendData(bytes);
        }
        void Text(string? value) => Add(Utf8.GetBytes(value ?? string.Empty));
        Text(Format); Text(FamilyEnvelopeQuant.AttributionMethod);
        Text(FamilyEnvelopeQuant.ProteinSetFingerprint(proteins));
        // ProteinSetFingerprint is order-free, but repeated identifiers and search tie order are not.
        foreach (var item in proteins.MyExtendedFastaItems ?? new List<FastaItemExtended>())
        {
            token.ThrowIfCancellationRequested();
            if (item == null) { Text(null); Text(null); }
            else { Text(item.SequenceIdentifier); Text(item.Sequence); }
        }
        using (var data = new MemoryStream())
        {
            using (var writer = new BinaryWriter(data, Utf8, true))
            {
                writer.Write(options.MinIdentity); writer.Write(options.ConsiderILSame); writer.Write(options.KQEquivalent);
                writer.Write(options.TopHitsPerPeptide); writer.Write(options.BestScoreOnly); writer.Write(options.DecoyLabel ?? string.Empty);
                writer.Write((int)options.Strategy); writer.Write(options.SeedLength); writer.Write(options.BruteForceProteinLimit);
                writer.Write(identifiedBestScoreOnly);
            }
            Add(data.ToArray());
        }
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            Add(JsonSerializer.SerializeToUtf8Bytes(row, RowJson));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Write(string path, string key, CurationHomologyHits hits, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Cache directory is unavailable.");
        Directory.CreateDirectory(directory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file, Utf8))
            {
                PutText(writer, Format); PutText(writer, key);
                writer.Write(hits.ProteinIds.Length);
                foreach (string id in hits.ProteinIds) { token.ThrowIfCancellationRequested(); PutText(writer, id); }
                WriteSide(writer, hits, NativeQuantFractions.SideIdentified, hits.Identified, token);
                WriteSide(writer, hits, NativeQuantFractions.SideDeNovo, hits.DeNovo, token);
            }
            byte[] digest;
            using (var file = File.OpenRead(temp)) digest = SHA256.HashData(file);
            token.ThrowIfCancellationRequested();
            using (var file = new FileStream(temp, FileMode.Append, FileAccess.Write, FileShare.None)) { file.Write(digest); file.Flush(true); }
            token.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void WriteSide(BinaryWriter writer, CurationHomologyHits hits, string side,
        List<(string Key, int[] Proteins)> entries, CancellationToken token)
    {
        writer.Write(entries.Count);
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            PutText(writer, entry.Key); writer.Write(entry.Proteins.Length);
            var offsets = hits.LocalizationOffsets(side, entry.Key);
            foreach (int index in entry.Proteins)
            {
                if (index < 0 || index >= hits.ProteinIds.Length || !offsets.TryGetValue(hits.ProteinIds[index], out int offset))
                    throw new InvalidDataException("A homology hit has no valid localization offset.");
                writer.Write(index); writer.Write(offset);
            }
        }
    }

    private static void PutText(BinaryWriter writer, string text)
    {
        byte[] bytes = Utf8.GetBytes(text);
        if (bytes.Length > MaximumTextBytes) throw new InvalidDataException("A cache field is too long.");
        writer.Write(bytes.Length); writer.Write(bytes);
    }

    private static bool TryRead(string path, string key, IReadOnlyList<QuantRow> rows, FamilyClusterFile proteins,
        HomologyOptions options, bool identifiedBestScoreOnly, CancellationToken token, out CurationHomologyHits? hits)
    {
        hits = null;
        if (!File.Exists(path)) return false;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length <= DigestSize) return false;
        long payloadLength = file.Length - DigestSize;
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            byte[] buffer = new byte[128 * 1024];
            long remaining = payloadLength;
            while (remaining > 0)
            {
                token.ThrowIfCancellationRequested();
                int count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (count == 0) return false;
                hash.AppendData(buffer, 0, count); remaining -= count;
            }
            byte[] expected = new byte[DigestSize];
            file.ReadExactly(expected);
            if (!CryptographicOperations.FixedTimeEquals(expected, hash.GetHashAndReset())) return false;
        }
        file.Position = 0;
        using var reader = new BinaryReader(file, Utf8, true);
        if (GetText(reader, payloadLength) != Format || GetText(reader, payloadLength) != key) return false;
        var items = (proteins.MyExtendedFastaItems ?? new List<FastaItemExtended>()).ToList();
        var lengths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.SequenceIdentifier) || string.IsNullOrWhiteSpace(item.Sequence)) continue;
            int length = item.Sequence.Trim().Length;
            if (!lengths.TryGetValue(item.SequenceIdentifier, out int previous) || length > previous) lengths[item.SequenceIdentifier] = length;
        }
        int proteinCount = ReadCount(reader, items.Count, payloadLength);
        var ids = new string[proteinCount];
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ids.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            ids[i] = GetText(reader, payloadLength);
            if (!lengths.ContainsKey(ids[i]) || !uniqueIds.Add(ids[i])) throw new InvalidDataException("Invalid cached protein identifier.");
        }
        HomologyOptions effective = options.Clone();
        effective.TopHitsPerPeptide = int.MaxValue;
        HomologyOptions identified = effective.Clone();
        identified.BestScoreOnly = identifiedBestScoreOnly;
        var result = new CurationHomologyHits { ProteinIds = ids, IdentityThreshold = effective.MinIdentity,
            EffectiveOptions = effective, IdentifiedOptions = identified };
        List<string> Queries(string side) => items.Count == 0 ? new List<string>() : rows.Where(row => row != null && string.Equals(row.Side, side, StringComparison.Ordinal))
            .Select(row => DeNovoReconciler.NormalizeSequence(row.Sequence ?? string.Empty)).Where(sequence => sequence.Length > 0)
            .Distinct(StringComparer.Ordinal).ToList();
        result.Identified = ReadSide(reader, payloadLength, result, NativeQuantFractions.SideIdentified, Queries(NativeQuantFractions.SideIdentified), lengths, token);
        result.DeNovo = ReadSide(reader, payloadLength, result, NativeQuantFractions.SideDeNovo, Queries(NativeQuantFractions.SideDeNovo), lengths, token);
        if (file.Position != payloadLength) throw new InvalidDataException("Unexpected trailing cache data.");
        token.ThrowIfCancellationRequested();
        if (items.Count > 0) result.Database = HomologyDatabase.Build(items);
        hits = result;
        return true;
    }

    private static List<(string Key, int[] Proteins)> ReadSide(BinaryReader reader, long end, CurationHomologyHits hits,
        string side, List<string> queries, Dictionary<string, int> lengths, CancellationToken token)
    {
        int count = ReadCount(reader, queries.Count, end);
        if (count != queries.Count) throw new InvalidDataException("Cached query count differs from the stored rows.");
        var entries = new List<(string, int[])>(count);
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            string key = GetText(reader, end);
            if (key != queries[i]) throw new InvalidDataException("Cached query differs from the stored rows.");
            int hitCount = ReadCount(reader, hits.ProteinIds.Length, end);
            var indices = new int[hitCount]; var offsets = new int[hitCount]; var seen = new HashSet<int>();
            for (int j = 0; j < hitCount; j++)
            {
                if (reader.BaseStream.Position > end - 8) throw new InvalidDataException("Truncated homology hit.");
                int index = reader.ReadInt32(), offset = reader.ReadInt32();
                if (index < 0 || index >= hits.ProteinIds.Length || !seen.Add(index) || offset < 0
                    || offset > lengths[hits.ProteinIds[index]] - key.Length)
                    throw new InvalidDataException("Invalid cached homology hit.");
                indices[j] = index; offsets[j] = offset;
            }
            entries.Add((key, indices));
            hits.RecordLocalizationOffsets(side, key, indices, offsets);
        }
        return entries;
    }

    private static int ReadCount(BinaryReader reader, int maximum, long end)
    {
        if (reader.BaseStream.Position > end - 4) throw new InvalidDataException("Truncated cache count.");
        int count = reader.ReadInt32();
        if (count < 0 || count > maximum) throw new InvalidDataException("Invalid cache count.");
        return count;
    }

    private static string GetText(BinaryReader reader, long end)
    {
        int length = ReadCount(reader, MaximumTextBytes, end);
        if (reader.BaseStream.Position > end - length) throw new InvalidDataException("Truncated cache text.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new InvalidDataException("Truncated cache text.");
        return Utf8.GetString(bytes);
    }
}
