using System;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Text.Json;

namespace FamilyQuant.Evidence;

internal sealed class LengthPrefixedSha256 : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    // Callers retain their existing null policy and field/row traversal order.
    internal void Append(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    // Preserve Append(JsonSerializer.Serialize(value)) exactly, including its byte-length prefix.
    // The input must remain unchanged/re-enumerable, as the enclosing currentness check requires.
    internal void AppendJson<T>(T value)
    {
        using var count = new JsonHashStream(null);
        JsonSerializer.Serialize(count, value);
        hash.AppendData(BitConverter.GetBytes(count.Written));
        using var output = new JsonHashStream(hash);
        JsonSerializer.Serialize(output, value);
        if (output.Written != count.Written) throw new InvalidDataException("Fingerprint input changed during serialization.");
    }

    private sealed class JsonHashStream(IncrementalHash? destination) : Stream
    {
        internal int Written { get; private set; }
        public override void Write(ReadOnlySpan<byte> bytes)
        {
            Written = checked(Written + bytes.Length);
            destination?.AppendData(bytes);
        }
        public override void Write(byte[] bytes, int offset, int count) => Write(bytes.AsSpan(offset, count));
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal string Finish() => Convert.ToHexString(hash.GetHashAndReset());
    public void Dispose() => hash.Dispose();
}
