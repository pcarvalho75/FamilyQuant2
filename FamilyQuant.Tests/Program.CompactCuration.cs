using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CompactCurationCommands() =>
    [new("--test-compact-curation", 451, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-compact-curation", (args, _) =>
        {
            if (args.Length is not (1 or 2)) throw new ArgumentException("Use --test-compact-curation [output-directory].");
            CompactCurationTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.CompactCuration"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void CompactCurationTests(string folder)
    {
        Section("Z12p5j compact curation keeps old indented files and objects");
        Directory.CreateDirectory(folder);
        var legacyOptions = new JsonSerializerOptions { WriteIndented = true };
        var compactOptions = new JsonSerializerOptions { WriteIndented = false };
        var variants = new[] { new FamilyClusterFile(), CurationStreamingFixture(16, 2048), ManualChoiceFile() };
        variants[1].MyExtendedFastaItems[0].Description = "Unicode \u00e1 \ud83e\uddec, quotes \"kept\", tab\tand line\nbreak";
        Check("Z12p5j the shared curation options choose compact output", !FamilyClusterFile.CurationJsonOptions.WriteIndented);
        for (int index = 0; index < variants.Length; index++)
        {
            var file = variants[index];
            string indented = JsonSerializer.Serialize(file, legacyOptions);
            string old = Path.Combine(folder, "old-indented-" + index.ToString(CultureInfo.InvariantCulture) + ".fcf");
            File.WriteAllText(old, indented, new UTF8Encoding(false));
            var opened = FamilyClusterFile.Load(old);
            string objects = JsonSerializer.Serialize(opened, compactOptions);
            Check("Z12p5j fixture " + index + " old indented FCF loads the same objects", objects
                == JsonSerializer.Serialize(FamilyClusterFile.FromJsonText(indented), compactOptions));
            string rewritten = Path.Combine(folder, "compact-" + index.ToString(CultureInfo.InvariantCulture) + ".fcf");
            opened.Save(rewritten);
            byte[] bytes = File.ReadAllBytes(rewritten);
            string text = opened.ToJsonText();
            Check("Z15b7 fixture " + index + " saved FCF is compact format 2", CurationFormat.Detect(() => File.OpenRead(rewritten)) == CurationFormat.Current
                && !Encoding.UTF8.GetString(bytes).Contains('\n'));
            var reloaded = FamilyClusterFile.Load(rewritten);
            Check("Z12p5j fixture " + index + " compact rewrite retains every loaded object", JsonSerializer.Serialize(reloaded, compactOptions) == objects);
            Check("Z15b7 ledger fixture " + index + " compares current-reader retained objects across both wire formats",
                System.Text.Json.Nodes.JsonNode.DeepEquals(RetainedFcfForLedger(old), RetainedFcfForLedger(rewritten))
                && System.Text.Json.Nodes.JsonNode.DeepEquals(RetainedFcfForLedger(rewritten), System.Text.Json.Nodes.JsonNode.Parse(objects)));
            Check("Z12p5j fixture " + index + " compact hash is exact and verified Save still passes", opened.JsonContentSha256()
                == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))) && opened.LastSaveCheck?.Passed == true);
            string project = Path.Combine(folder, "compact-" + index.ToString(CultureInfo.InvariantCulture) + ".fqproj");
            ProjectStore.WriteFqproj(project, opened, Array.Empty<QuantRow>(), new QuantManifest());
            var projectRead = ProjectStore.ReadFqproj(project).fcf;
            Check("Z12p5j fixture " + index + " project readback restores the same compact canonical bytes", projectRead.ToJsonText() == text);
            using (var archive = ZipFile.OpenRead(project))
            using (var stream = archive.GetEntry(ProjectStore.CurationFormat2EntryName)!.Open())
            using (var actual = new MemoryStream())
            {
                stream.CopyTo(actual);
                Check("Z15b7 fixture " + index + " project format-2 entry is byte-identical to FCF", actual.ToArray().SequenceEqual(bytes));
            }
            if (index > 0)
                Check("Z12p5j fixture " + index + " removes formatting bytes without losing state", bytes.Length < Encoding.UTF8.GetByteCount(indented));
        }
    }
}
