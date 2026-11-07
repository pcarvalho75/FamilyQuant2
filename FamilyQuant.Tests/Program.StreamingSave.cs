using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class SaveWriteProbe : MemoryStream
    {
        public int MaximumWrite { get; private set; }
        public override void Write(byte[] buffer, int offset, int count) { MaximumWrite = Math.Max(MaximumWrite, count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { MaximumWrite = Math.Max(MaximumWrite, buffer.Length); base.Write(buffer); }
    }
    private sealed class SaveShortRead(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(7, count));
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(7, buffer.Length)]);
    }

    [TestCommandTable]
    private static IEnumerable<TestCommand> StreamingSaveCommands() =>
    [new("--test-streaming-save", 448, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-streaming-save", (args, _) =>
        {
            StreamingSaveTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.StreamingSave"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void StreamingSaveTests(string folder)
    {
        Directory.CreateDirectory(folder);
        var file = new FamilyClusterFile { SourceSha256 = new string('a', 64), SourceSequenceCount = 2000,
            ReportDetails = new() { Title = "Synthetic save á < & >", WrittenInterpretation = "First paragraph.\n\nSecond paragraph." },
            MyExtendedFastaItems = Enumerable.Range(1, 2000).Select(index => new FastaItemExtended
            { SequenceIdentifier = (index % 7 == 0 ? "Reverse_" : "") + "save_" + index.ToString(CultureInfo.InvariantCulture),
                Sequence = "ACDEFGHIKPEPTIDEK", Description = "Synthetic description á < & > \"quoted\"\nsecond line",
                UserID = index % 9, USearchID = index % 11, UserAnnotation = "Annotation " + index.ToString(CultureInfo.InvariantCulture) }).ToList() };
        file.LockedUserIds.Add(2);
        var write = typeof(FamilyClusterFile).GetMethod("WriteJsonUtf8", BindingFlags.Instance | BindingFlags.NonPublic);
        var digest = typeof(FamilyClusterFile).GetMethod("JsonContentSha256", BindingFlags.Instance | BindingFlags.Public);
        byte[]? previous = null;
        var old = CultureInfo.CurrentCulture;
        try
        {
            foreach (string language in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                byte[] expected = Encoding.UTF8.GetBytes(file.ToJsonText());
                using var stream = new SaveWriteProbe();
                if (write != null) write.Invoke(file, new object[] { stream }); else stream.Write(expected);
                Check("J48 UTF-8 stream is byte-identical to the existing curation serializer " + language, expected.SequenceEqual(stream.ToArray()));
                Check("J48 curation writes stay bounded instead of one full JSON buffer " + language,
                    write != null && stream.CanWrite && expected.Length > 262144 && stream.MaximumWrite < 131072);
                Check("J48 streamed curation fingerprint equals the existing UTF-8 SHA256 " + language,
                    digest != null && (string)digest.Invoke(file, null)! == Convert.ToHexString(SHA256.HashData(expected)));
                Check("J48 shell dirty/saved-state fingerprint keeps the exact existing value " + language,
                    ShellDocumentState.Fingerprint(file) == Convert.ToHexString(SHA256.HashData(expected)));
                if (previous != null) Check("J48 streamed JSON repeats across cultures", previous.SequenceEqual(stream.ToArray()));
                previous = stream.ToArray();
                string project = Path.Combine(folder, language + ".fqproj");
                var rows = new List<QuantRow> { Qr("identified", "save", 1, 2, "ACDEFGHIK", 10, 20) };
                string message = ShellProjectOperations.WriteVerifiedProject(project, file, rows, new QuantManifest());
                // Z15b1: the file holds curation format 2 in its own entry; the canonical content above stays format 1.
                using (var archive = ZipFile.OpenRead(project))
                using (var entry = archive.GetEntry(ProjectStore.CurationFormat2EntryName)!.Open())
                using (var bytes = new MemoryStream())
                using (var format2 = new MemoryStream())
                {
                    entry.CopyTo(bytes);
                    CurationFormat.Write(file, format2);
                    Check("J48 actual verified Save writes the exact curation entry bytes " + language,
                        format2.ToArray().SequenceEqual(bytes.ToArray()) && message.StartsWith("File check passed", StringComparison.Ordinal));
                }
                Check("J48 every existing saved-file validation still passes " + language, SavedFileCheck.Check(file, project, project: true).Passed);
                var changed = FamilyClusterFile.FromJsonText(file.ToJsonText()); changed.MyExtendedFastaItems[0].UserAnnotation += " changed";
                var check = SavedFileCheck.Check(changed, project, project: true);
                Check("J48 changed curation is still rejected by the streamed hash validation " + language,
                    !check.Passed && check.Checks.Any(item => item.Code == "curation" && !item.Passed));
            }
        }
        finally { CultureInfo.CurrentCulture = old; }
        StreamingSaveReadTests();
    }

    private static void StreamingSaveReadTests()
    {
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        { new() { SequenceIdentifier = "read", Sequence = "PEPTIDEK", Description = "Á < & >", UserID = 1, USearchID = 2 } } };
        var method = typeof(FamilyClusterFile).GetMethod("ReadJsonUtf8", BindingFlags.Static | BindingFlags.NonPublic)!;
        FamilyClusterFile Read(byte[] bytes)
        {
            try { return (FamilyClusterFile)method.Invoke(null, new object[] { (Func<Stream>)(() => new SaveShortRead(bytes)) })!; }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        string modern = file.ToJsonText();
        Check("J48 streamed reader preserves modern explicit-empty lock semantics across short reads",
            Read(Encoding.UTF8.GetBytes(modern)).ToJsonText() == FamilyClusterFile.FromJsonText(modern).ToJsonText());
        var legacy = JsonNode.Parse(modern)!.AsObject(); legacy.Remove("LockedUserIds");
        string old = legacy.ToJsonString();
        Check("J48 streamed reader applies the exact legacy divergent-id lock migration",
            Read(Encoding.UTF8.GetBytes(old)).ToJsonText() == FamilyClusterFile.FromJsonText(old).ToJsonText()
            && Read(Encoding.UTF8.GetBytes(old)).LockedUserIds.SetEquals(new[] { 1, 2 }));
        legacy["UnknownNested"] = new JsonObject { ["LockedUserIds"] = new JsonArray() };
        string nested = legacy.ToJsonString();
        Check("J48 streamed reader preserves the original raw lock-token presence rule for unknown nested fields",
            Read(Encoding.UTF8.GetBytes(nested)).ToJsonText() == FamilyClusterFile.FromJsonText(nested).ToJsonText());
        byte[] utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(old)).ToArray();
        Check("J48 streamed reader retains legacy UTF16 BOM compatibility fallback",
            Read(utf16).ToJsonText() == FamilyClusterFile.FromJsonText(old).ToJsonText());
        byte[] bom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(old)).ToArray();
        Check("J48 streamed reader retains UTF8 BOM compatibility", Read(bom).ToJsonText() == FamilyClusterFile.FromJsonText(old).ToJsonText());
        Check("J48 streamed reader still rejects malformed curation JSON", Throws<JsonException>(() => Read(Encoding.UTF8.GetBytes("{invalid"))));
    }
}
