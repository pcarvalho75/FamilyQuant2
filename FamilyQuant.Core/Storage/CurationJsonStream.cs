using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using FamilyQuant.Operations;

namespace FamilyQuant;

public partial class FamilyClusterFile
{
    // Same metadata/options/order as ToJsonText, with UTF-8 written incrementally to the destination.
    internal static Action? JsonWriteObserved { get; set; }
    internal void WriteJsonUtf8(Stream destination)
    {
        JsonWriteObserved?.Invoke();
        JsonSerializer.Serialize(destination, this, CurationJsonOptions);
    }

    /// <summary>The same SHA-256 as UTF8(ToJsonText()), without materializing the full string or byte array.</summary>
    public string JsonContentSha256()
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        WriteJsonUtf8(stream); stream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash!);
    }

    /// <summary>Copies the stored curation through the canonical UTF-8 stream, without a whole-file
    /// string or byte array. The temporary spool is removed when the operation completes or fails.</summary>
    public FamilyClusterFile DeepCopy(OperationContext? operation = null) =>
        JsonStreamCopy.ReadWritten<FamilyClusterFile>(WriteJsonUtf8, ReadJsonUtf8, operation, forceFile: true);

    internal static FamilyClusterFile ReadJsonUtf8(Func<Stream> open)
    {
        if (CurationFormat.Detect(open) == CurationFormat.Current) return ReadCurationFormat2(open);
        try
        {
            using var source = open(); using var tracked = new CurationReadProbe(source);
            var loaded = JsonSerializer.Deserialize<FamilyClusterFile>(tracked)
                ?? throw new InvalidDataException("The text is not a valid Family Cluster File.");
            return FinishJsonLoad(loaded, tracked.HasLockField);
        }
        catch (JsonException)
        {
            // Retain the existing StreamReader encoding/BOM/replacement behavior for older unusual inputs.
            using var source = open();
            using var normalized = new CurationUtf8ReadStream(source);
            using var tracked = new CurationReadProbe(normalized);
            var loaded = JsonSerializer.Deserialize<FamilyClusterFile>(tracked)
                ?? throw new InvalidDataException("The text is not a valid Family Cluster File.");
            return FinishJsonLoad(loaded, tracked.HasLockField);
        }
    }

    // The compatibility reader converts only a bounded chunk at a time. StreamReader supplies the
    // original BOM detection and replacement fallbacks; Encoder carries split surrogate pairs.
    private sealed class CurationUtf8ReadStream(Stream source) : Stream
    {
        private readonly StreamReader reader = new(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        private readonly Encoder encoder = Encoding.UTF8.GetEncoder();
        private readonly char[] characters = new char[4096];
        private readonly byte[] bytes = new byte[Encoding.UTF8.GetMaxByteCount(4096)];
        private int offset, length;
        private bool finished;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> destination)
        {
            int written = 0;
            while (written < destination.Length)
            {
                if (offset == length)
                {
                    if (finished) break;
                    int count = reader.Read(characters, 0, characters.Length);
                    finished = count == 0;
                    length = encoder.GetBytes(characters, 0, count, bytes, 0, flush: finished);
                    offset = 0;
                    if (length == 0) continue;
                }
                int take = Math.Min(length - offset, destination.Length - written);
                bytes.AsSpan(offset, take).CopyTo(destination[written..]);
                offset += take; written += take;
            }
            return written;
        }
        protected override void Dispose(bool disposing) { if (disposing) reader.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Preserve the old raw-text Contains("\"LockedUserIds\"") migration rule across read boundaries.
    private sealed class CurationReadProbe(Stream source) : Stream
    {
        private static readonly byte[] LockField = Encoding.UTF8.GetBytes("\"LockedUserIds\"");
        private int matched;
        public bool HasLockField { get; private set; }
        private void Observe(ReadOnlySpan<byte> bytes)
        {
            if (HasLockField) return;
            foreach (byte value in bytes)
            {
                matched = value == LockField[matched] ? matched + 1 : value == LockField[0] ? 1 : 0;
                if (matched == LockField.Length) { HasLockField = true; return; }
            }
        }
        public override int Read(byte[] buffer, int offset, int count) { int length = source.Read(buffer, offset, count); Observe(buffer.AsSpan(offset, length)); return length; }
        public override int Read(Span<byte> buffer) { int length = source.Read(buffer); Observe(buffer[..length]); return length; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
