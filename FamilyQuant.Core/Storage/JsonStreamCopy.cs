using System;
using System.IO;
using System.Text.Json;
using FamilyQuant.Operations;

namespace FamilyQuant;

/// <summary>A detached copy with the default JSON contract. Small records stay in a bounded buffer,
/// while large evidence and recipe values use the same delete-on-close TEMP spool as curation copies.</summary>
internal static class JsonStreamCopy
{
    internal static T Clone<T>(T value)
    {
        return ReadWritten<T>(stream => JsonSerializer.Serialize(stream, value), open =>
        {
            using var source = open();
            return JsonSerializer.Deserialize<T>(source)!;
        });
    }

    /// <summary>Uses the same bounded store with caller-supplied canonical write/read contracts.
    /// The reader factory resets the position and borrows the store for encoding or format retries.</summary>
    internal static T ReadWritten<T>(Action<Stream> write, Func<Func<Stream>, T> read,
        OperationContext? operation = null, bool forceFile = false)
    {
        operation?.ThrowIfCancellationRequested();
        using var stream = new CopyStream(forceFile);
        write(stream);
        stream.Flush();
        operation?.ThrowIfCancellationRequested();
        T result = read(stream.OpenRead);
        operation?.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Exact default-JSON byte equality, with one bounded store and a comparing write stream.</summary>
    internal static bool SameSerializedBytes<T>(T? first, T? second) where T : class
    {
        if (ReferenceEquals(first, second)) return true;
        if (first == null || second == null) return false;
        return ReadWritten(stream => JsonSerializer.Serialize(stream, first), open =>
        {
            using var source = open();
            using var comparison = new ByteComparisonStream(source);
            JsonSerializer.Serialize(comparison, second);
            return comparison.Matches && source.ReadByte() == -1;
        });
    }

    private sealed class CopyStream : Stream
    {
        private const int MemoryLimit = 64 * 1024;
        private Stream storage;

        internal CopyStream(bool forceFile) => storage = forceFile ? CreateFile(curation: true) : new MemoryStream();

        internal Stream OpenRead()
        {
            storage.Position = 0;
            return new BorrowedReadStream(storage);
        }

        public override bool CanRead => storage.CanRead;
        public override bool CanSeek => storage.CanSeek;
        public override bool CanWrite => storage.CanWrite;
        public override long Length => storage.Length;
        public override long Position { get => storage.Position; set => storage.Position = value; }
        public override void Flush() => storage.Flush();
        public override int Read(byte[] buffer, int offset, int count) => storage.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => storage.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => storage.Seek(offset, origin);
        public override void SetLength(long value)
        {
            if (storage is MemoryStream memory && value > MemoryLimit) Spill(memory);
            storage.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            SpillIfNeeded(count);
            storage.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            SpillIfNeeded(buffer.Length);
            storage.Write(buffer);
        }

        private void SpillIfNeeded(int count)
        {
            if (storage is not MemoryStream memory || count <= MemoryLimit - memory.Position) return;
            Spill(memory);
        }

        private void Spill(MemoryStream memory)
        {
            var file = CreateFile(curation: false);
            try
            {
                memory.WriteTo(file);
                file.Position = memory.Position;
                storage = file;
            }
            catch { file.Dispose(); throw; }
            memory.Dispose();
        }

        private static FileStream CreateFile(bool curation)
        {
            string prefix = curation ? "FamilyQuant-curation-" : "FamilyQuant-json-copy-";
            string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N") + ".tmp");
            return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.Read | FileShare.Delete, 65536, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) storage.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class BorrowedReadStream(Stream source) : Stream
    {
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => source.CanSeek;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => source.Position; set => source.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => source.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ByteComparisonStream(Stream source) : Stream
    {
        private readonly byte[] buffer = new byte[8192];
        internal bool Matches { get; private set; } = true;
        public override void Write(byte[] bytes, int offset, int count) => Write(bytes.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> bytes)
        {
            while (Matches && !bytes.IsEmpty)
            {
                int take = Math.Min(buffer.Length, bytes.Length), read = 0;
                while (read < take)
                {
                    int count = source.Read(buffer, read, take - read);
                    if (count == 0) { Matches = false; return; }
                    read += count;
                }
                Matches = bytes[..take].SequenceEqual(buffer.AsSpan(0, take));
                bytes = bytes[take..];
            }
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
