using System;
using System.IO;
using System.Text.Json;

namespace FamilyQuant;

/// <summary>A detached copy with the default JSON contract. Small records stay in a bounded buffer,
/// while large evidence and recipe values use the same delete-on-close TEMP spool as curation copies.</summary>
internal static class JsonStreamCopy
{
    internal static T Clone<T>(T value)
    {
        using var stream = new CopyStream();
        JsonSerializer.Serialize(stream, value);
        stream.Flush();
        stream.Position = 0;
        return JsonSerializer.Deserialize<T>(stream)!;
    }

    private sealed class CopyStream : Stream
    {
        private const int MemoryLimit = 64 * 1024;
        private Stream storage = new MemoryStream();

        public override bool CanRead => storage.CanRead;
        public override bool CanSeek => storage.CanSeek;
        public override bool CanWrite => storage.CanWrite;
        public override long Length => storage.Length;
        public override long Position { get => storage.Position; set => storage.Position = value; }
        public override void Flush() => storage.Flush();
        public override int Read(byte[] buffer, int offset, int count) => storage.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => storage.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => storage.Seek(offset, origin);
        public override void SetLength(long value) => storage.SetLength(value);

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
            string path = Path.Combine(Path.GetTempPath(), "FamilyQuant-json-copy-" + Guid.NewGuid().ToString("N") + ".tmp");
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.Read | FileShare.Delete, 65536, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            try
            {
                memory.WriteTo(file);
                file.Position = memory.Position;
                storage = file;
            }
            catch { file.Dispose(); throw; }
            memory.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) storage.Dispose();
            base.Dispose(disposing);
        }
    }
}
