using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyQuant;

/// <summary>The curation file formats. Format 1 is the <see cref="FamilyClusterFile"/> object written at the root,
/// as every build up to 2.1.10 wrote it. Format 2 (2.1.11) is a root array holding one document: the format marker,
/// a notice, the parts used, their shared sections, then the curation shaped by those parts. The array root is what
/// makes an older build refuse the file: 2.1.10 deserializes the root as a FamilyClusterFile object and stops at the
/// first token instead of reading an empty curation. Projects (.fqproj, version 3) store format 2; the .fcf export,
/// "Families for FamilyQuant 1.5 (.fcf)", stays format 1 so FamilyQuant 1.5 and 2.1.10 still open it. Every reader
/// opens both formats. The canonical content form used for hashes and copies (<see cref="FamilyClusterFile.ToJsonText"/>,
/// <see cref="FamilyClusterFile.JsonContentSha256"/>, <see cref="FamilyClusterFile.DeepCopy"/>) stays format 1.</summary>
public static class CurationFormat
{
    public const int Legacy = 1;
    public const int Current = 2;
    public const string MarkerProperty = "FamilyQuantCurationFormat";
    public const string Notice = "FamilyQuant curation format 2, written by FamilyQuant 2.1.11 or later; older versions cannot open it.";

    /// <summary>The largest header (everything before the curation: marker, notice, parts and every part's section)
    /// a reader accepts. The header is held in memory while it is read, so past this size a file is refused rather
    /// than read without bound, and the writer refuses to write one, naming the part whose section is largest.</summary>
    public const int MaxHeaderBytes = 64 << 20;

    /// <summary>Room left in the header limit for the bytes that open the curation after the sections.</summary>
    private const int HeaderMargin = 4096;

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>2 when the first character after an optional UTF-8 byte order mark and JSON whitespace opens an
    /// array, otherwise 1. Anything that is not format 2 goes to the format-1 reader, which keeps its handling
    /// of legacy encodings and its own error for text that is not a curation.</summary>
    public static int Detect(Func<Stream> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        using Stream stream = open();
        var buffer = new byte[4096];
        int length = 0, read;
        while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0)
        {
            length += read;
            int? found = FirstByte(buffer.AsSpan(0, length), complete: false);
            if (found.HasValue) return found.Value == '[' ? Current : Legacy;
        }
        int? last = FirstByte(buffer.AsSpan(0, length), complete: true);
        return last == '[' ? Current : Legacy;
    }

    /// <inheritdoc cref="Detect(Func{Stream})"/>
    public static int Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (char c in text)
        {
            if (c == '\uFEFF' || c is ' ' or '\t' or '\r' or '\n') continue;
            return c == '[' ? Current : Legacy;
        }
        return Legacy;
    }

    private static int? FirstByte(ReadOnlySpan<byte> data, bool complete)
    {
        if (data.Length < Utf8Bom.Length && Utf8Bom.AsSpan(0, data.Length).SequenceEqual(data) && !complete) return null;
        if (data.StartsWith(Utf8Bom)) data = data[Utf8Bom.Length..];
        foreach (byte b in data)
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) return b;
        return complete ? -1 : null;
    }

    /// <summary>Writes the curation as format 2 with the given parts (the registered ones when null). Streaming:
    /// the parts' sections are prepared first, then the document is serialized straight into the destination.</summary>
    public static void Write(FamilyClusterFile file, Stream destination, IReadOnlyList<CurationFormatPart>? parts = null) =>
        Write(file, destination, parts, MaxHeaderBytes);

    /// <summary>The writer with its header limit as a parameter, so a test can reach the limit with small sections.</summary>
    internal static void Write(FamilyClusterFile file, Stream destination, IReadOnlyList<CurationFormatPart>? parts, int maxHeaderBytes)
    {
        ArgumentNullException.ThrowIfNull(file); ArgumentNullException.ThrowIfNull(destination);
        parts ??= CurationFormatRegistry.Parts;
        var context = new CurationFormatContext(Current, writing: true, parts.Select(p => p.Name));
        foreach (CurationFormatPart part in parts) part.PrepareWrite(file, context);
        var options = CurationFormatRegistry.CreateOptions(context, parts);
        var document = new CurationDocument { Format = Current, Parts = parts.Select(p => p.Name).ToList(), Sections = context.Sections };
        // Checked before a byte is written: a header the reader would refuse must never reach a file.
        long headerBytes = JsonSerializer.SerializeToUtf8Bytes(new[] { document }, options).LongLength;
        if (headerBytes + HeaderMargin > maxHeaderBytes)
        {
            var largest = context.Sections.Select(section => (Part: section.Key, Bytes: (long)Encoding.UTF8.GetByteCount(section.Value.GetRawText())))
                .OrderByDescending(section => section.Bytes).ThenBy(section => section.Part, StringComparer.Ordinal).FirstOrDefault();
            throw new InvalidOperationException("The curation was not written: its format-2 header would be " + Megabytes(headerBytes)
                + ", over the " + Megabytes(maxHeaderBytes) + " a reader accepts. "
                + (largest.Part == null ? "No part wrote a section." : "The part \"" + largest.Part + "\" writes " + Megabytes(largest.Bytes) + " in its section."));
        }
        document.Curation = file;
        JsonSerializer.Serialize(destination, new[] { document }, options);
    }

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    /// <summary>Reads a curation of either format through the same reader every file route uses.
    /// <paramref name="parts"/> stands for the parts this build knows (the registered ones when null).</summary>
    public static FamilyClusterFile Read(Func<Stream> open, IReadOnlyList<CurationFormatPart>? parts = null) =>
        Detect(open) == Current ? FamilyClusterFile.ReadCurationFormat2(open, parts) : FamilyClusterFile.ReadJsonUtf8(open);

    /// <summary>Reads only the header of a format-2 file: the marker, the parts listed and their sections.</summary>
    internal static CurationHeader ReadHeader(Stream stream)
    {
        var buffer = new byte[64 * 1024];
        int length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                if (buffer.Length >= MaxHeaderBytes)
                    throw new InvalidDataException("The curation format-2 header is larger than "
                        + (MaxHeaderBytes >> 20).ToString(CultureInfo.InvariantCulture) + " MB; the file is not a curation this build can read.");
                Array.Resize(ref buffer, buffer.Length * 2);
            }
            int read = stream.Read(buffer, length, buffer.Length - length);
            length += read;
            if (TryParseHeader(buffer.AsSpan(0, length), read == 0, out CurationHeader? header)) return header!;
            if (read == 0) throw new InvalidDataException("The file ends before its curation format-2 header is complete.");
        }
    }

    private static bool TryParseHeader(ReadOnlySpan<byte> data, bool final, out CurationHeader? header)
    {
        header = null;
        if (data.StartsWith(Utf8Bom)) data = data[Utf8Bom.Length..];
        var reader = new Utf8JsonReader(data, final, default);
        if (!reader.Read()) return false;
        if (reader.TokenType != JsonTokenType.StartArray) throw NotFormat2("its root is not an array");
        if (!reader.Read()) return false;
        if (reader.TokenType != JsonTokenType.StartObject) throw NotFormat2("its array does not start with the document");
        int? format = null;
        var parts = new List<string>();
        var sections = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        while (true)
        {
            if (!reader.Read()) return false;
            if (reader.TokenType == JsonTokenType.EndObject) throw NotFormat2("it holds no curation");
            string name = reader.GetString()!;
            if (name == nameof(CurationDocument.Curation)) break;
            if (!reader.Read()) return false;
            switch (name)
            {
                case MarkerProperty:
                    if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int value))
                        throw NotFormat2("its " + MarkerProperty + " is not a whole number");
                    format = value;
                    break;
                case nameof(CurationDocument.Parts):
                    if (reader.TokenType != JsonTokenType.StartArray) throw NotFormat2("its Parts is not a list");
                    while (true)
                    {
                        if (!reader.Read()) return false;
                        if (reader.TokenType == JsonTokenType.EndArray) break;
                        if (reader.TokenType != JsonTokenType.String) throw NotFormat2("its Parts holds something other than names");
                        parts.Add(reader.GetString()!);
                    }
                    break;
                case nameof(CurationDocument.Sections):
                    long start = reader.TokenStartIndex;
                    if (!reader.TrySkip()) return false;
                    using (var document = JsonDocument.Parse(data[(int)start..(int)reader.BytesConsumed].ToArray()))
                    {
                        if (document.RootElement.ValueKind != JsonValueKind.Object) throw NotFormat2("its Sections is not an object");
                        foreach (var section in document.RootElement.EnumerateObject()) sections[section.Name] = section.Value.Clone();
                    }
                    break;
                default:
                    if (!reader.TrySkip()) return false;
                    break;
            }
        }
        if (format == null) throw NotFormat2("it has no " + MarkerProperty + " marker before its curation");
        header = new CurationHeader(format.Value, parts, sections);
        return true;
    }

    private static InvalidDataException NotFormat2(string reason) =>
        new("The text is not a valid FamilyQuant curation format-2 file: " + reason + ".");
}

/// <summary>The marker, parts and sections read ahead of a format-2 curation.</summary>
internal sealed record CurationHeader(int Format, IReadOnlyList<string> Parts, IReadOnlyDictionary<string, JsonElement> Sections);

/// <summary>The single element of a format-2 file's root array. The property order is the file's order: the
/// header comes first, so a reader knows the format and the parts before the curation starts.</summary>
internal sealed class CurationDocument
{
    [JsonPropertyName(CurationFormat.MarkerProperty), JsonPropertyOrder(0)]
    public int Format { get; set; }

    [JsonPropertyOrder(1)]
    public string Notice { get; set; } = CurationFormat.Notice;

    [JsonPropertyOrder(2)]
    public List<string> Parts { get; set; } = new();

    [JsonPropertyOrder(3)]
    public Dictionary<string, JsonElement> Sections { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyOrder(4)]
    public FamilyClusterFile? Curation { get; set; }
}

public partial class FamilyClusterFile
{
    /// <summary>Writes this curation as the current format (2), streaming, with the registered parts. The project
    /// container uses it; the .fcf export, hashes and copies keep the canonical format-1 content.</summary>
    internal void WriteCurationFile(Stream destination) => CurationFormat.Write(this, destination);

    /// <summary>Reads a format-2 curation in two passes over <paramref name="open"/>: the header first (format,
    /// parts, sections), then the whole document with only the parts the file lists. A newer format, or a part this
    /// build does not know, is refused with a message rather than read wrongly.</summary>
    internal static FamilyClusterFile ReadCurationFormat2(Func<Stream> open, IReadOnlyList<CurationFormatPart>? known = null)
    {
        known ??= CurationFormatRegistry.Parts;
        CurationHeader header;
        using (Stream stream = open()) header = CurationFormat.ReadHeader(stream);
        if (header.Format != CurationFormat.Current)
            throw new InvalidDataException(header.Format > CurationFormat.Current
                ? "This curation was written by a newer FamilyQuant (curation format " + header.Format.ToString(CultureInfo.InvariantCulture)
                    + "); this build reads formats 1 and 2. Update FamilyQuant to open it."
                : "The text declares curation format " + header.Format.ToString(CultureInfo.InvariantCulture) + ", which does not exist.");
        var unknown = header.Parts.Where(name => known.All(p => p.Name != name)).ToArray();
        if (unknown.Length > 0)
            throw new InvalidDataException("This curation uses parts this FamilyQuant does not know (" + string.Join(", ", unknown)
                + "). Update FamilyQuant to open it.");
        if (header.Parts.Distinct(StringComparer.Ordinal).Count() != header.Parts.Count)
            throw new InvalidDataException("The text is not a valid FamilyQuant curation format-2 file: a part is listed twice.");
        var active = known.Where(p => header.Parts.Contains(p.Name)).ToArray();
        var context = new CurationFormatContext(CurationFormat.Current, writing: false, active.Select(p => p.Name), header.Sections);
        CurationDocument[]? documents;
        using (Stream stream = open())
            documents = JsonSerializer.Deserialize<CurationDocument[]>(stream, CurationFormatRegistry.CreateOptions(context, active));
        FamilyClusterFile loaded = documents is { Length: 1 } && documents[0].Curation is { } curation ? curation
            : throw new InvalidDataException("The text is not a valid FamilyQuant curation format-2 file: it must hold exactly one document with a curation.");
        foreach (CurationFormatPart part in active) part.CompleteRead(loaded, context);
        // Format 2 is only written by builds that store LockedUserIds, so the pre-lock migration never applies.
        return FinishJsonLoad(loaded, hasLockField: true);
    }
}
