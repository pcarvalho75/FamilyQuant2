using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace FamilyQuant;

public partial class FamilyClusterFile
{
    // Same metadata/options/order as ToJsonText, with UTF-8 written incrementally to the destination.
    internal void WriteJsonUtf8(Stream destination) => JsonSerializer.Serialize(destination, this, CurationJsonOptions);

    /// <summary>The same SHA-256 as UTF8(ToJsonText()), without materializing the full string or byte array.</summary>
    public string JsonContentSha256()
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        WriteJsonUtf8(stream); stream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash!);
    }

    internal static FamilyClusterFile ReadJsonUtf8(Func<Stream> open)
    {
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
            using var source = open(); using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return FromJsonText(reader.ReadToEnd());
        }
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
