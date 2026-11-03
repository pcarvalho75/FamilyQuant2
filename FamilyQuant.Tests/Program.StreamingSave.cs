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

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class SaveWriteProbe : MemoryStream
    {
        public int MaximumWrite { get; private set; }
        public override void Write(byte[] buffer, int offset, int count) { MaximumWrite = Math.Max(MaximumWrite, count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { MaximumWrite = Math.Max(MaximumWrite, buffer.Length); base.Write(buffer); }
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
        var digest = typeof(FamilyClusterFile).GetMethod("JsonContentSha256", BindingFlags.Instance | BindingFlags.NonPublic);
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
                if (previous != null) Check("J48 streamed JSON repeats across cultures", previous.SequenceEqual(stream.ToArray()));
                previous = stream.ToArray();
                string project = Path.Combine(folder, language + ".fqproj");
                var rows = new List<QuantRow> { Qr("identified", "save", 1, 2, "ACDEFGHIK", 10, 20) };
                string message = ShellProjectOperations.WriteVerifiedProject(project, file, rows, new QuantManifest());
                using (var archive = ZipFile.OpenRead(project))
                using (var entry = archive.GetEntry(ProjectStore.CurationEntryName)!.Open())
                using (var bytes = new MemoryStream())
                {
                    entry.CopyTo(bytes);
                    Check("J48 actual verified Save writes the exact curation entry bytes " + language,
                        expected.SequenceEqual(bytes.ToArray()) && message.StartsWith("File check passed", StringComparison.Ordinal));
                }
                Check("J48 every existing saved-file validation still passes " + language, SavedFileCheck.Check(file, project, project: true).Passed);
                var changed = FamilyClusterFile.FromJsonText(file.ToJsonText()); changed.MyExtendedFastaItems[0].UserAnnotation += " changed";
                var check = SavedFileCheck.Check(changed, project, project: true);
                Check("J48 changed curation is still rejected by the streamed hash validation " + language,
                    !check.Passed && check.Checks.Any(item => item.Code == "curation" && !item.Passed));
            }
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
