using FamilyQuant.Homology;
using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
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

/// <summary>Disposable cache of the complete source-specific census. Invalid entries are recomputed.</summary>
internal static class FamilySourceCensusCache
{
    // Change whenever source-claim selection, search, mapping or result serialization changes.
    private const string Format = "FamilyQuant source census v1";
    private const int DigestSize = 32;
    private const int MaximumPayloadBytes = 64 * 1024 * 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly JsonSerializerOptions Json = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private sealed class Payload
    {
        public Payload() { }
        public string Format { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public double MappingMinimumIdentity { get; set; }
        public int SearchInputDistinctSequences { get; set; }
        public int PeaksDeNovoOnlyInputDistinctSequences { get; set; }
        public int DeepNovoInputDistinctSequences { get; set; }
        public int UnknownSourceInputDistinctSequences { get; set; }
        public List<FamilySourceSequenceRow> Families { get; set; } = new();
    }

    internal static FamilySourceSequenceSummary GetOrCompute(IReadOnlyList<QuantRow> rows, HomologyDatabase database,
        List<FastaItemExtended> targets, HomologyOptions options, HomologyOptions identifiedOptions,
        IReadOnlyDictionary<int, string> descriptions, string? cacheDirectory, OperationContext? operation,
        Func<FamilySourceSequenceSummary> compute)
    {
        CancellationToken token = operation?.CancellationToken ?? default;
        string directory = cacheDirectory ?? Path.Combine(ApplicationPaths.LocalDataDirectory, FullXicComputer.CacheFolderName, "source-census");
        string? key = null, path = null;
        FamilySourceSequenceSummary? cached = null;
        operation?.Report("report-census-cache-start", "Data step 2: checking the source census cache", 2, 8);
        try
        {
            key = Fingerprint(rows, database, targets, options, identifiedOptions, descriptions, token);
            path = Path.Combine(directory, key + ".fqcensus");
            if (TryRead(path, key, out var candidate)
                && key == Fingerprint(rows, database, targets, options, identifiedOptions, descriptions, token))
                cached = candidate;
        }
        catch (Exception ex) when (IsCacheFailure(ex)) { /* A cache failure must not suppress fresh computation. */ }
        token.ThrowIfCancellationRequested();
        if (cached != null)
        {
            operation?.Report("report-census-cache", "Data step 2: source census cache hit", 2, 8);
            operation?.Report("report-census-cache-end", "Data step 2: source census cache lookup complete", 2, 8);
            operation?.Report("report-census-identified-start", "Data step 2: source census 1 of 2 cached", 2, 8);
            operation?.Report("report-census-identified-end", "Data step 2: source census 1 of 2 cached", 2, 8);
            operation?.Report("report-census-denovo-start", "Data step 2: source census 2 of 2 cached", 2, 8);
            operation?.Report("report-census-denovo-end", "Data step 2: source census 2 of 2 cached", 2, 8);
            return cached;
        }
        operation?.Report("report-census-cache", "Data step 2: source census cache miss", 2, 8);
        operation?.Report("report-census-cache-end", "Data step 2: source census cache lookup complete", 2, 8);
        var result = compute();
        token.ThrowIfCancellationRequested();
        if (path != null && key != null)
        {
            string? after = null;
            try { after = Fingerprint(rows, database, targets, options, identifiedOptions, descriptions, token); }
            catch (Exception ex) when (IsCacheFailure(ex)) { /* Return a fresh result without publishing an unverifiable cache. */ }
            if (after != null && key != after) throw new InvalidDataException("Source census inputs changed during computation.");
            try
            {
                if (after != null) Write(path, key, result, token);
            }
            catch (Exception ex) when (IsCacheFailure(ex)) { /* The computed result remains available without a cache. */ }
        }
        return result;
    }

    private static bool IsCacheFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or JsonException or NotSupportedException or ArgumentException or OverflowException;

    private static string Fingerprint(IReadOnlyList<QuantRow> rows, HomologyDatabase database, List<FastaItemExtended> targets,
        HomologyOptions options, HomologyOptions identifiedOptions, IReadOnlyDictionary<int, string> descriptions, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(byte[] bytes)
        {
            Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length); hash.AppendData(bytes);
        }
        void Text(string? value)
        {
            if (value != null) { Add(Utf8.GetBytes(value)); return; }
            Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, -1); hash.AppendData(length);
        }
        Text(Format); Text(FamilySourceSequenceSummary.CountingGrain); Text(FamilySourceSequenceSummary.AcceptedTier);
        Text(FamilyEnvelopeQuant.AttributionMethod);
        Text(DeNovoUnionResolver.PrecursorTolerancePpm.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Text(database.SeedLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Text(database.HasSeedIndex ? "1" : "0");
        Text("database"); Text(database.Proteins.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var protein in database.Proteins) { token.ThrowIfCancellationRequested(); Text(protein.Id); Text(protein.Sequence); }
        Text("targets"); Text(targets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested(); Text(target?.SequenceIdentifier);
            Text(target == null ? "" : target.UserID.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        Text("descriptions"); Text(descriptions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var pair in descriptions.OrderBy(p => p.Key))
        { Text(pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)); Text(pair.Value); }
        Add(JsonSerializer.SerializeToUtf8Bytes(options, Json)); Add(JsonSerializer.SerializeToUtf8Bytes(identifiedOptions, Json));
        // Whole rows include non-folded per-caller sequences, source IDs, run/charge and precursor m/z.
        Text("rows"); Text(rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var row in rows) { token.ThrowIfCancellationRequested(); Add(JsonSerializer.SerializeToUtf8Bytes(row, Json)); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool TryRead(string path, string key, out FamilySourceSequenceSummary? result)
    {
        result = null;
        if (!File.Exists(path)) return false;
        var info = new FileInfo(path);
        if (info.Length <= DigestSize || info.Length > MaximumPayloadBytes + DigestSize) return false;
        byte[] file = File.ReadAllBytes(path);
        int size = file.Length - DigestSize;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(file.AsSpan(0, size)), file.AsSpan(size, DigestSize))) return false;
        var payload = JsonSerializer.Deserialize<Payload>(file.AsSpan(0, size), Json);
        if (payload == null || payload.Format != Format || payload.Key != key || payload.Families == null
            || !double.IsFinite(payload.MappingMinimumIdentity) || payload.SearchInputDistinctSequences < 0
            || payload.PeaksDeNovoOnlyInputDistinctSequences < 0 || payload.DeepNovoInputDistinctSequences < 0
            || payload.UnknownSourceInputDistinctSequences < 0
            || payload.Families.Any(f => f == null || f.Description == null || f.SearchDistinctSequences < 0
                || f.PeaksDeNovoOnlyExclusiveDistinctSequences < 0 || f.DeepNovoExclusiveDistinctSequences < 0
                || f.SharedPeaksDeepNovoDistinctSequences < 0 || f.PeaksDeNovoOnlyTotalDistinctSequences < 0
                || f.DeepNovoTotalDistinctSequences < 0 || f.DeNovoUnionDistinctSequences < 0)
            || payload.Families.Select(f => f.FamilyUserId).Distinct().Count() != payload.Families.Count) return false;
        result = new FamilySourceSequenceSummary { MappingMinimumIdentity = payload.MappingMinimumIdentity,
            SearchInputDistinctSequences = payload.SearchInputDistinctSequences, PeaksDeNovoOnlyInputDistinctSequences = payload.PeaksDeNovoOnlyInputDistinctSequences,
            DeepNovoInputDistinctSequences = payload.DeepNovoInputDistinctSequences, UnknownSourceInputDistinctSequences = payload.UnknownSourceInputDistinctSequences };
        result.Families.AddRange(payload.Families);
        return true;
    }

    private static void Write(string path, string key, FamilySourceSequenceSummary value, CancellationToken token)
    {
        var payload = new Payload { Format = Format, Key = key, MappingMinimumIdentity = value.MappingMinimumIdentity,
            SearchInputDistinctSequences = value.SearchInputDistinctSequences, PeaksDeNovoOnlyInputDistinctSequences = value.PeaksDeNovoOnlyInputDistinctSequences,
            DeepNovoInputDistinctSequences = value.DeepNovoInputDistinctSequences, UnknownSourceInputDistinctSequences = value.UnknownSourceInputDistinctSequences,
            Families = value.Families.ToList() };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        if (bytes.Length > MaximumPayloadBytes) throw new InvalidDataException("The source census cache is too large.");
        string directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Cache directory is unavailable.");
        Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Write(SHA256.HashData(bytes)); stream.Flush(true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
