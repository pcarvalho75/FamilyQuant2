using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationStreamingBoundaryCommands() =>
    [new("--test-curation-streaming-boundaries", 21116, TestCommandPhase.Raw,
        args => args is ["--test-curation-streaming-boundaries"], (_, _) =>
        {
            CurationStreamingBoundaryTests(Path.Combine(Path.GetTempPath(), "curation-streaming-boundaries"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void CurationStreamingBoundaryTests(string parent)
    {
        Section("Z12p5f5 - streamed boundary and failed-spool regressions with planted bugs");
        string root = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        string temp = Path.Combine(root, "temp"); Directory.CreateDirectory(temp);
        string? oldTemp = Environment.GetEnvironmentVariable("TEMP"), oldTmp = Environment.GetEnvironmentVariable("TMP");
        try
        {
            Environment.SetEnvironmentVariable("TEMP", temp); Environment.SetEnvironmentVariable("TMP", temp);
            CurationSurrogateBoundaryTests(root);
            CurationLockBoundaryTests();
            CurationSpoolBoundaryTests(temp);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMP", oldTemp); Environment.SetEnvironmentVariable("TMP", oldTmp);
            string allowed = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("Boundary cleanup escaped its owned parent.");
            Directory.Delete(root, recursive: true);
        }
        Check("Z12p5f5 boundary fixture removes its entire owned scratch directory", !Directory.Exists(root));
    }

    private static void CurationSurrogateBoundaryTests(string root)
    {
        const string prefix = "{\"DomainSource\":\"", emoji = "\ud83e\uddec";
        string text = prefix + new string('p', 4095 - prefix.Length) + emoji + "\",\"MyExtendedFastaItems\":[],\"LockedUserIds\":[]}";
        Check("Z12p5f5 high and low surrogate straddle character positions 4095 and 4096", text.IndexOf(emoji, StringComparison.Ordinal) == 4095);
        byte[] expected = Encoding.UTF8.GetBytes(text);
        foreach (var encoding in new Encoding[] { Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32, new UTF32Encoding(true, true) })
        {
            byte[] legacy = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
            string label = encoding.WebName;
            using var source = new MemoryStream(legacy);
            using var normalized = CurationBoundaryNativeStream("CurationUtf8ReadStream", source);
            byte[] actual = ReadCurationBoundaryBytes(normalized);
            Check("Z12p5f5 " + label + " streamed encoder retains the split surrogate pair byte-for-byte", actual.SequenceEqual(expected));
            string path = Path.Combine(root, "surrogate-" + encoding.CodePage.ToString(CultureInfo.InvariantCulture) + ".fcf");
            File.WriteAllBytes(path, legacy);
            var old = FamilyClusterFile.FromJsonText(text);
            var loaded = FamilyClusterFile.Load(path);
            var shell = ShellProjectOperations.Read(path);
            Check("Z12p5f5 " + label + " public Load and shell retain the same actual emoji", loaded.DomainSource == old.DomainSource
                && shell.Curation.DomainSource == old.DomainSource && loaded.DomainSource.EndsWith(emoji, StringComparison.Ordinal));

            using var badSource = new MemoryStream(legacy);
            using var badNormalized = CurationBoundaryNativeStream("CurationUtf8ReadStream", badSource);
            var encoder = (Encoder)badNormalized.GetType().GetField("encoder", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(badNormalized)!;
            // Planted product-state bug: reset the actual native Encoder between output reads,
            // dropping its pending high surrogate before the next 4096-character source chunk.
            using var resetEncoder = new CurationBoundaryReadStream(badNormalized, encoder.Reset);
            byte[] broken = ReadCurationBoundaryBytes(resetEncoder);
            Check("Z12p5f5 " + label + " parity predicate rejects a planted encoder reset", !broken.SequenceEqual(expected)
                && Encoding.UTF8.GetString(broken).Contains('\ufffd'));
        }
    }

    private static void CurationLockBoundaryTests()
    {
        const int markerStart = 4097;
        const string prefix = "{\"Padding\":\"", suffix = "\",";
        string text = prefix + new string('p', markerStart - prefix.Length - suffix.Length) + suffix
            + "\"LockedUserIds\":[7],\"MyExtendedFastaItems\":[{\"SequenceIdentifier\":\"SYNTHETIC_LOCK\",\"Sequence\":\"ACDEFGHIK\",\"USearchID\":1,\"UserID\":2}]}";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Check("Z12p5f5 lock field starts just past byte 4096", text.IndexOf("\"LockedUserIds\"", StringComparison.Ordinal) == markerStart);
        // A forced read edge bisects the marker as well as placing it past a 4096-byte edge.
        long[] cuts = [4096, markerStart + 7];
        var sharedReader = typeof(FamilyClusterFile).GetMethod("ReadJsonUtf8", BindingFlags.NonPublic | BindingFlags.Static)!;
        FamilyClusterFile? read = null;
        Exception? failure = null;
        try
        {
            // Detection, reading and legacy fallback can each open the same bytes independently.
            read = (FamilyClusterFile)sharedReader.Invoke(null, new object[]
                { (Func<Stream>)(() => new CurationBoundaryReadStream(new MemoryStream(bytes), cuts: cuts)) })!;
        }
        catch (Exception error) { failure = error is TargetInvocationException { InnerException: { } inner } ? inner : error; }
        Check("Z12p5f5 native shared reader finds a split lock marker and suppresses legacy lock inference",
            read?.LockedUserIds.SetEquals(new[] { 7 }) == true, failure?.ToString());

        using var badSource = new CurationBoundaryReadStream(new MemoryStream(bytes), cuts: cuts);
        using var probe = CurationBoundaryNativeStream("CurationReadProbe", badSource);
        var matched = probe.GetType().GetField("matched", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Planted product-state bug: forget the actual tracker's partial marker before every read.
        using var resetMatch = new CurationBoundaryReadStream(probe, () => matched.SetValue(probe, 0));
        var broken = JsonSerializer.Deserialize<FamilyClusterFile>(resetMatch)!;
        bool observed = (bool)probe.GetType().GetProperty("HasLockField")!.GetValue(probe)!;
        var finish = typeof(FamilyClusterFile).GetMethod("FinishJsonLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
        broken = (FamilyClusterFile)finish.Invoke(null, new object[] { broken, observed })!;
        Check("Z12p5f5 lock predicate rejects a planted partial-marker reset", !observed && !broken.LockedUserIds.SetEquals(new[] { 7 })
            && broken.LockedUserIds.SetEquals(new[] { 1, 2, 7 }));
    }

    private static void CurationSpoolBoundaryTests(string temp)
    {
        var observer = typeof(FamilyClusterFile).GetProperty("JsonWriteObserved", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = observer.GetValue(null);
        HashSet<string> Spools() => Directory.GetFiles(temp, "FamilyQuant-curation-*.tmp").ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (bool cancel in new[] { false, true })
            foreach (bool plantLeak in new[] { false, true })
            {
                var before = Spools();
                using var cancellation = new CancellationTokenSource();
                bool sawOpenSpool = false;
                observer.SetValue(null, (Action)(() =>
                {
                    sawOpenSpool = Spools().Except(before).Any();
                    if (cancel) cancellation.Cancel();
                    else throw new IOException("Synthetic failed-write fault after spool creation.");
                }));
                bool expectedException = false;
                var file = CurationStreamingFixture(4, 64);
                try
                {
                    if (plantLeak) CurationPlantedLeakyCopy(file, new OperationContext(cancellation.Token));
                    else file.DeepCopy(new OperationContext(cancellation.Token));
                }
                catch (IOException) when (!cancel) { expectedException = true; }
                catch (OperationCanceledException) when (cancel) { expectedException = true; }
                bool clean = Spools().SetEquals(before);
                string scenario = cancel ? "cancellation observed between write and read" : "failed write after spool creation";
                Check("Z12p5f5 " + scenario + (plantLeak ? " predicate rejects a planted missing-delete flag" : " deletes the native spool"),
                    sawOpenSpool && expectedException && clean == !plantLeak);
                foreach (string created in Spools().Except(before)) File.Delete(created);
            }
        }
        finally { observer.SetValue(null, previous); }
    }

    // Negative control only: the native write/cancel seams, with the deletion flag deliberately
    // omitted. The exact same cleanup predicate must reject this leaking copy on both exit paths.
    private static void CurationPlantedLeakyCopy(FamilyClusterFile file, OperationContext operation)
    {
        string path = Path.Combine(Path.GetTempPath(), "FamilyQuant-curation-planted-" + Guid.NewGuid().ToString("N") + ".tmp");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
        try { typeof(FamilyClusterFile).GetMethod("WriteJsonUtf8", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(file, new object[] { stream }); }
        catch (TargetInvocationException error) when (error.InnerException is IOException write) { throw write; }
        stream.Flush(); operation.ThrowIfCancellationRequested();
    }

    private static Stream CurationBoundaryNativeStream(string nestedName, Stream source) => (Stream)Activator.CreateInstance(
        typeof(FamilyClusterFile).GetNestedType(nestedName, BindingFlags.NonPublic)!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null, args: new object[] { source }, culture: CultureInfo.InvariantCulture)!;

    private static byte[] ReadCurationBoundaryBytes(Stream stream)
    {
        using var output = new MemoryStream();
        byte[] one = new byte[1];
        for (int count; (count = stream.Read(one, 0, 1)) != 0;) output.Write(one, 0, count);
        return output.ToArray();
    }

    private sealed class CurationBoundaryReadStream(Stream inner, Action? beforeRead = null, long[]? cuts = null) : Stream
    {
        private long consumed;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            beforeRead?.Invoke();
            long edge = cuts?.FirstOrDefault(cut => cut > consumed) ?? 0;
            if (edge > 0) buffer = buffer[..(int)Math.Min(buffer.Length, edge - consumed)];
            int read = inner.Read(buffer); consumed += read; return read;
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => consumed; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
