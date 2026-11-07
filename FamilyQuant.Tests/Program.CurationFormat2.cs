using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using FamilyQuant.Panels;
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
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationFormat2Commands() =>
    [
        new("--test-curation-format2", 449, TestCommandPhase.Raw,
            args => args.Length is 1 or 2 && args[0] == "--test-curation-format2", (args, _) =>
            {
                CurationFormat2Tests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.CurationFormat2"));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    // ---- shared helpers for every format-2 part (Z15b2 to Z15b5) and for tests that open a saved project ----

    /// <summary>Z15b1 helper for every format-2 part: writes the file as format 2 with the given parts (the registered
    /// ones when null), reads it back with the same parts and checks that nothing a gate hashes moved. The fingerprints
    /// are the canonical content hash and the default-options serialization of the whole file, of each protein's
    /// evidence and positions, of each reference-panel entry and its evidence, of the recipe and of the family panel:
    /// unchanged on the original after the write, and equal on the copy read back. Returns the format-2 bytes.</summary>
    internal static byte[] CheckCurationFormatRoundTrip(string label, FamilyClusterFile file, IReadOnlyList<CurationFormatPart>? parts = null)
    {
        var before = CurationFingerprints(file);
        byte[] bytes;
        using (var stream = new MemoryStream()) { CurationFormat.Write(file, stream, parts); bytes = stream.ToArray(); }
        var afterWrite = CurationFingerprints(file);
        var read = CurationFormat.Read(() => new MemoryStream(bytes, writable: false), parts);
        var copy = CurationFingerprints(read);
        string Moved(Dictionary<string, string> other) => string.Join(", ", before.Where(pair => other[pair.Key] != pair.Value).Select(pair => pair.Key));
        Check(label + ": writing format 2 leaves every fingerprint of the file unchanged", Moved(afterWrite).Length == 0, Moved(afterWrite));
        Check(label + ": the copy read back has every fingerprint of the original", Moved(copy).Length == 0, Moved(copy));
        Check(label + ": the bytes are curation format 2", CurationFormat.Detect(() => new MemoryStream(bytes, writable: false)) == CurationFormat.Current);
        return bytes;
    }

    /// <summary>The values the gates hash, each as SHA-256, with default serializer options (never a part's).</summary>
    internal static Dictionary<string, string> CurationFingerprints(FamilyClusterFile file)
    {
        string Hash(IEnumerable<string> parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))));
        var entries = file.EvidenceSource?.ReferencePanel?.Entries ?? new List<PanelEntry>();
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["content"] = file.JsonContentSha256(),
            ["file"] = Hash(new[] { JsonSerializer.Serialize(file) }),
            ["protein-evidence"] = Hash(file.MyExtendedFastaItems.Select(p => JsonSerializer.Serialize(p.Evidence))),
            ["protein-positions"] = Hash(file.MyExtendedFastaItems.Select(p => JsonSerializer.Serialize(p.Evidence?.Positions))),
            ["panel-entries"] = Hash(entries.Select(e => JsonSerializer.Serialize(e))),
            ["panel-evidence"] = Hash(entries.Select(e => JsonSerializer.Serialize(e.Evidence))),
            ["recipe"] = Hash(new[] { JsonSerializer.Serialize(file.EvidenceRecipe) }),
            ["family-panel"] = Hash(new[] { JsonSerializer.Serialize(file.FamilyPanel) }),
        };
    }

    /// <summary>The stored curation object of a saved project as JSON text: curation2.json's document body in a
    /// version-3 project, curation.json in an older one. For inspection; open projects with ProjectStore.</summary>
    internal static string StoredCurationJson(ZipArchive zip)
    {
        var format2 = zip.GetEntry(ProjectStore.CurationFormat2EntryName);
        using var reader = new StreamReader((format2 ?? zip.GetEntry(ProjectStore.CurationEntryName)!).Open(), Encoding.UTF8);
        string text = reader.ReadToEnd();
        return format2 == null ? text : JsonNode.Parse(text)![0]!["Curation"]!.ToJsonString();
    }

    /// <summary>Edits the stored curation object of a saved project in place, whichever layout it has.</summary>
    internal static void EditStoredCuration(string project, Action<JsonObject> edit)
    {
        using var zip = ZipFile.Open(project, ZipArchiveMode.Update);
        var format2 = zip.GetEntry(ProjectStore.CurationFormat2EntryName);
        var entry = format2 ?? zip.GetEntry(ProjectStore.CurationEntryName)!;
        string name = entry.FullName, text;
        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) text = reader.ReadToEnd();
        JsonNode root = JsonNode.Parse(text)!;
        edit((format2 == null ? root : root[0]!["Curation"]!).AsObject());
        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(root.ToJsonString());
    }

    // ---- the 2.1.10 reader path, transcribed ----

    /// <summary>FamilyQuant 2.1.10's .fcf open, transcribed from tag v2.1.10: FamilyClusterFile.Load reads the text
    /// (FamilyQuant.Core/FamilyQuantFile.cs:138) and FromJsonText deserializes it as a FamilyClusterFile object with
    /// default options (:163).</summary>
    private static FamilyClusterFile ReadFcfAs2110(string path) =>
        JsonSerializer.Deserialize<FamilyClusterFile>(File.ReadAllText(path)) ?? throw new InvalidDataException("The text is not a valid Family Cluster File.");

    /// <summary>FamilyQuant 2.1.10's .fqproj open up to its version check, transcribed from tag v2.1.10
    /// (FamilyQuant.Core/ProjectStore.cs:153-182 and Storage/CurationJsonStream.cs:23-37): it requires curation.json,
    /// deserializes it as a FamilyClusterFile object, reads manifest.json and refuses a version above 2.</summary>
    private static FamilyClusterFile ReadProjectAs2110(string path)
    {
        const string CurationEntryName = "curation.json", ManifestEntryName = "manifest.json";
        const int CurrentFormatVersion = 2;
        using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);
        ZipArchiveEntry curationEntry = zip.GetEntry(CurationEntryName)
            ?? throw new InvalidDataException("\"" + path + "\" is not a valid FamilyQuant project: its " + CurationEntryName + " entry is missing.");
        var manifestEntry = zip.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("\"" + path + "\" is not a valid FamilyQuant project: its " + ManifestEntryName + " entry is missing.");
        string manifestText; using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8)) manifestText = reader.ReadToEnd();
        FamilyClusterFile fcf;
        using (var stream = curationEntry.Open())
            fcf = JsonSerializer.Deserialize<FamilyClusterFile>(stream) ?? throw new InvalidDataException("The text is not a valid Family Cluster File.");
        int formatVersion = JsonNode.Parse(manifestText)!["FormatVersion"]!.GetValue<int>();
        if (formatVersion > CurrentFormatVersion)
            throw new InvalidDataException("\"" + path + "\" is a version " + formatVersion.ToString(CultureInfo.InvariantCulture)
                + " project, and this build understands up to version " + CurrentFormatVersion.ToString(CultureInfo.InvariantCulture)
                + ". Update FamilyQuant to open it.");
        return fcf;
    }

    // ---- synthetic parts: they prove the frame, they are not parts of the product ----

    /// <summary>Leaves every protein's Description out of the body and stores the descriptions once as a section.</summary>
    [CurationFormatPart("test-description-pool", 900)]
    private sealed class DescriptionPoolTestPart : CurationFormatPart
    {
        public override void PrepareWrite(FamilyClusterFile file, CurationFormatContext context) =>
            context.SetSection(this, file.MyExtendedFastaItems.Select(p => p.Description).ToList());
        public override void ModifyType(JsonTypeInfo typeInfo, CurationFormatContext context)
        {
            if (typeInfo.Type != typeof(FastaItemExtended)) return;
            foreach (var property in typeInfo.Properties.Where(p => p.Name == "Description")) property.ShouldSerialize = (_, _) => false;
        }
        public override void CompleteRead(FamilyClusterFile file, CurationFormatContext context)
        {
            if (!context.TryGetSection(this, out List<string>? descriptions) || descriptions == null || descriptions.Count != file.MyExtendedFastaItems.Count)
                throw new InvalidDataException("The description section does not match the proteins.");
            for (int i = 0; i < descriptions.Count; i++) file.MyExtendedFastaItems[i].Description = descriptions[i];
        }
    }

    /// <summary>Writes every set of integers as one text value, in iteration order.</summary>
    [CurationFormatPart("test-integer-text", 901)]
    private sealed class IntegerTextTestPart : CurationFormatPart
    {
        public override IEnumerable<JsonConverter> CreateConverters(CurationFormatContext context) { yield return new IntegerTextConverter(); }
        private sealed class IntegerTextConverter : JsonConverter<HashSet<int>>
        {
            public override HashSet<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                reader.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToHashSet();
            public override void Write(Utf8JsonWriter writer, HashSet<int> value, JsonSerializerOptions options) =>
                writer.WriteStringValue(string.Join(",", value.Select(v => v.ToString(CultureInfo.InvariantCulture))));
        }
    }

    /// <summary>Pools a large text in its section, to reach the header limit.</summary>
    [CurationFormatPart("test-large-pool", 902)]
    private sealed class LargePoolTestPart : CurationFormatPart
    {
        public override void PrepareWrite(FamilyClusterFile file, CurationFormatContext context) =>
            context.SetSection(this, new[] { new string('P', 200_000) });
    }

    private sealed class UnmarkedTestPart : CurationFormatPart { }

    [CurationFormatPart("test-description-pool", 5)]
    private sealed class DuplicateNameTestPart : CurationFormatPart { }

    [CurationFormatPart("Not A Name", 5)]
    private sealed class BadNameTestPart : CurationFormatPart { }

    [CurationFormatPart("test-aaa-first", 900)]
    private sealed class OrderTieTestPart : CurationFormatPart { }

    /// <summary>Synthetic proteins, decoys, position evidence with one shared feature batch, a reference panel and a
    /// family panel: the shapes the format-2 parts change. Every accession and sequence is invented.</summary>
    private static FamilyClusterFile CurationFormat2Fixture()
    {
        var batch = new PositionEvidenceSource
        {
            Provider = "Synthetic", Release = "2099_01", RetrievedAtUtc = new DateTimeOffset(2099, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Responses = { new PositionEvidenceResponse { Url = "https://example.invalid/features?accessions=X1,X2,X3", Sha256 = new string('b', 64) } },
        };
        ProteinEvidence Evidence(string accession, string sequence, bool reviewed) => new()
        {
            Accession = accession, Reviewed = reviewed, Family = "Invented family", Keywords = new() { "Invented keyword" },
            Positions = new ProteinPositionEvidence
            {
                Accession = accession, SequenceLength = sequence.Length,
                SequenceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(sequence))).ToLowerInvariant(),
                Pfam = { new PfamDomainPosition { Accession = "PF99999", Name = "Invented", Start = 2, End = 6 } },
                Features = { new ProteinFeaturePosition { Type = "Chain", Start = 1, End = sequence.Length, Evidences = { new() { EvidenceCode = "ECO:0000000" } } } },
                FeatureSource = batch,
            },
        };
        string[] sequences = { "MKTAYIAKQRQISFVK", "MKTAYLAKQRQISFVR", "MGSSHHHHHHSSGLVPR" };
        var proteins = new List<FastaItemExtended>();
        for (int i = 0; i < sequences.Length; i++)
            proteins.Add(new FastaItemExtended
            {
                SequenceIdentifier = "tr|X" + (i + 1).ToString(CultureInfo.InvariantCulture) + "|X" + (i + 1).ToString(CultureInfo.InvariantCulture) + "_INVENT",
                Sequence = sequences[i], Description = "Invented protein " + i.ToString(CultureInfo.InvariantCulture) + " (quotes \" and accents \u00e1\u00ea)",
                UserID = 1 + i % 2, USearchID = 1 + i % 2, Evidence = Evidence("X" + (i + 1).ToString(CultureInfo.InvariantCulture), sequences[i], i == 0),
            });
        foreach (var target in proteins.ToArray())
            proteins.Add(new FastaItemExtended
            {
                SequenceIdentifier = "Reverse_" + target.SequenceIdentifier, Sequence = new string(target.Sequence.Reverse().ToArray()),
                Description = target.Description, UserID = target.UserID, USearchID = target.USearchID,
            });
        return new FamilyClusterFile
        {
            ClusteringIdentity = 70, SourceSha256 = new string('c', 64), SourceSequenceCount = proteins.Count, ClusteringMethod = "Synthetic",
            LockedUserIds = new HashSet<int> { 2, 1 },
            MyExtendedFastaItems = proteins,
            EvidenceSource = new EvidenceProvenance
            {
                Provider = "Synthetic", Release = "2099_01",
                ReferencePanel = new ReferencePanel
                {
                    Provider = "Synthetic", Release = "2099_01", Families = { "Invented family" },
                    Entries = sequences.Select((s, i) => new PanelEntry { Accession = "R" + i.ToString(CultureInfo.InvariantCulture), Sequence = s,
                        Evidence = Evidence("R" + i.ToString(CultureInfo.InvariantCulture), s, true) }).ToList(),
                },
            },
            FamilyPanel = new FamilyPanel { Name = "Invented panel", Families = { new FamilyPanelFamily { Key = "INV", DisplayName = "Invented family" } } },
        };
    }

    private static void CurationFormat2Tests(string folder)
    {
        Section("Z15b1 curation format 2: marker, reader for formats 1 and 2, part registry, older builds refuse");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);
        Exception? Thrown(Action work) { try { work(); return null; } catch (Exception error) { return error is TargetInvocationException { InnerException: { } inner } ? inner : error; } }

        // Detection.
        Check("Z15b1 detection: a root object is format 1, a root array format 2, through a byte order mark and whitespace",
            CurationFormat.Detect("{}") == 1 && CurationFormat.Detect("\uFEFF \r\n\t[") == 2 && CurationFormat.Detect("") == 1
            && CurationFormat.Detect(() => new MemoryStream(new byte[] { 0xEF, 0xBB, 0xBF, (byte)' ', (byte)'[' })) == 2
            && CurationFormat.Detect(() => new MemoryStream(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("{}")).ToArray())) == 1
            && CurationFormat.Detect(() => new SaveShortRead(Encoding.UTF8.GetBytes("   \n  [ ]"))) == 2);

        // The registry.
        var synthetic = CurationFormatRegistry.Discover(new[] { typeof(IntegerTextTestPart), typeof(DescriptionPoolTestPart), typeof(OrderTieTestPart) });
        Check("Z15b1 registry: parts are ordered by Order, then by name, and take their name from the attribute",
            synthetic.Select(p => p.Name).SequenceEqual(new[] { "test-aaa-first", "test-description-pool", "test-integer-text" })
            && synthetic.Select(p => p.Order).SequenceEqual(new[] { 900, 900, 901 }));
        Check("Z15b1 registry: a part without the attribute, a duplicate name or a bad name is refused",
            Thrown(() => CurationFormatRegistry.Discover(new[] { typeof(UnmarkedTestPart) }))?.Message.Contains("has no [CurationFormatPart]", StringComparison.Ordinal) == true
            && Thrown(() => CurationFormatRegistry.Discover(new[] { typeof(DescriptionPoolTestPart), typeof(DuplicateNameTestPart) }))?.Message.Contains("Two curation format parts", StringComparison.Ordinal) == true
            && Thrown(() => CurationFormatRegistry.Discover(new[] { typeof(BadNameTestPart) }))?.Message.Contains("lower-case", StringComparison.Ordinal) == true);
        var registered = CurationFormatRegistry.Parts;
        Check("Z15b1 registry: the shipped list loads, with unique names", registered.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == registered.Count,
            string.Join(", ", registered.Select(p => p.Name)));

        // A .fcf: the export "Families for FamilyQuant 1.5 (.fcf)" stays format 1 so 1.5 and 2.1.10 open it
        // (DU7's frozen 1.5 reader, docs/PROJECT-FORMATS.md); a format-2 .fcf still opens everywhere.
        var fixture = CurationFormat2Fixture();
        string legacyText = fixture.ToJsonText();
        string legacyFcf = Path.Combine(folder, "legacy.fcf");
        File.WriteAllText(legacyFcf, legacyText, new UTF8Encoding(false));
        var opened = FamilyClusterFile.Load(legacyFcf);
        Check("Z15b1 a format-1 .fcf opens unchanged", opened.ToJsonText() == legacyText);
        string exportedFcf = Path.Combine(folder, "exported.fcf");
        opened.Save(exportedFcf);
        Check("Z15b1 the .fcf export stays format 1, byte-identical to the canonical content, with its file check passed",
            File.ReadAllText(exportedFcf) == legacyText && opened.LastSaveCheck?.Passed == true, opened.LastSaveCheck?.Message);
        string savedFcf = Path.Combine(folder, "format2.fcf");
        using (var stream = File.Create(savedFcf)) CurationFormat.Write(opened, stream);
        byte[] savedBytes = File.ReadAllBytes(savedFcf);
        var savedJson = JsonNode.Parse(savedBytes)!;
        Check("Z15b1 the format-2 writer: one document in a root array, marker 2, the notice, the registered parts, sections before the curation",
            savedJson is JsonArray { Count: 1 } && savedJson[0]![CurationFormat.MarkerProperty]!.GetValue<int>() == 2
            && savedJson[0]!["Notice"]!.GetValue<string>() == CurationFormat.Notice
            && savedJson[0]!["Parts"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(registered.Select(p => p.Name))
            && savedJson[0]!["Curation"] is JsonObject && Text(savedBytes).IndexOf("\"Curation\"", StringComparison.Ordinal) > Text(savedBytes).IndexOf("\"Sections\"", StringComparison.Ordinal),
            Text(savedBytes)[..Math.Min(200, savedBytes.Length)]);
        var reopened = FamilyClusterFile.Load(savedFcf);
        Check("Z15b1 a format-2 .fcf opens to the identical curation (Load, FromJsonText, the shell route)",
            reopened.ToJsonText() == legacyText && FamilyClusterFile.FromJsonText(Text(savedBytes)).ToJsonText() == legacyText
            && ShellProjectOperations.Read(savedFcf).Curation.ToJsonText() == legacyText);

        // A format-1 (version 2) project opens and saves as version 3 with format 2.
        string template = Path.Combine(folder, "template.fqproj");
        var rows = new List<QuantRow> { new() { Side = "identified", Run = "r1", Scan = 7, Charge = 2, Sequence = "MKTAYIAK", PrecursorMz = 400.5,
            IsotopeAreas = new[] { 1.5, 2.5 }, EnvelopeSum = 4, FamilyUserId = 1 } };
        ProjectStore.WriteFqproj(template, fixture, rows, new QuantManifest());
        string legacyProject = Path.Combine(folder, "legacy.fqproj");
        File.Copy(template, legacyProject);
        using (var zip = ZipFile.Open(legacyProject, ZipArchiveMode.Update))
        {
            // Rewrite the container in the version-2 layout 2.1.10 wrote: curation format 1 in curation.json.
            zip.GetEntry(ProjectStore.CurationFormat2EntryName)!.Delete();
            zip.GetEntry(ProjectStore.CurationEntryName)!.Delete();
            using (var writer = new StreamWriter(zip.CreateEntry(ProjectStore.CurationEntryName).Open(), new UTF8Encoding(false))) writer.Write(legacyText);
            var manifestEntry = zip.GetEntry(ProjectStore.ManifestEntryName)!;
            string manifestText; using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8)) manifestText = reader.ReadToEnd();
            var manifest = JsonNode.Parse(manifestText)!.AsObject(); manifest["FormatVersion"] = 2;
            manifestEntry.Delete();
            using var manifestWriter = new StreamWriter(zip.CreateEntry(ProjectStore.ManifestEntryName).Open(), new UTF8Encoding(false));
            manifestWriter.Write(manifest.ToJsonString());
        }
        var legacyRead = ProjectStore.ReadFqproj(legacyProject);
        Check("Z15b1 a version-2 project (curation format 1) opens unchanged", legacyRead.fcf.ToJsonText() == legacyText
            && legacyRead.manifest.FormatVersion == 2 && legacyRead.quant.Single().Scan == 7);
        string resaved = Path.Combine(folder, "resaved.fqproj");
        ProjectStore.WriteFqproj(resaved, legacyRead.fcf, legacyRead.quant, legacyRead.manifest);
        using (var zip = ZipFile.OpenRead(resaved))
        {
            string notice; using (var reader = new StreamReader(zip.GetEntry(ProjectStore.CurationEntryName)!.Open(), Encoding.UTF8)) notice = reader.ReadToEnd();
            string manifestText; using (var reader = new StreamReader(zip.GetEntry(ProjectStore.ManifestEntryName)!.Open(), Encoding.UTF8)) manifestText = reader.ReadToEnd();
            byte[] entry; using (var stream = zip.GetEntry(ProjectStore.CurationFormat2EntryName)!.Open()) using (var copy = new MemoryStream()) { stream.CopyTo(copy); entry = copy.ToArray(); }
            Check("Z15b1 it saves as version 3: curation format 2 in curation2.json, byte-identical to the format-2 writer, and only the notice in curation.json",
                JsonNode.Parse(manifestText)!["FormatVersion"]!.GetValue<int>() == 3 && ProjectStore.CurrentFormatVersion == 3
                && entry.SequenceEqual(savedBytes) && notice.Contains("Update FamilyQuant to open it.", StringComparison.Ordinal)
                && JsonNode.Parse(notice) is JsonObject { Count: 1 });
        }
        var resavedRead = ProjectStore.ReadFqproj(resaved);
        Check("Z15b1 the version-3 project reopens to the identical curation and rows",
            resavedRead.fcf.ToJsonText() == legacyText && resavedRead.quant.Single().EnvelopeSum == 4 && resavedRead.manifest.FormatVersion == 3);
        string noCuration = Path.Combine(folder, "no-curation2.fqproj");
        File.Copy(resaved, noCuration);
        using (var zip = ZipFile.Open(noCuration, ZipArchiveMode.Update)) zip.GetEntry(ProjectStore.CurationFormat2EntryName)!.Delete();
        Check("Z15b1 a version-3 project without curation2.json is refused, never read from the notice",
            Thrown(() => ProjectStore.ReadFqproj(noCuration))?.Message.EndsWith("its curation2.json entry is missing.", StringComparison.Ordinal) == true);

        // The 2.1.10 reader path refuses both files.
        var fcfRefusal = Thrown(() => ReadFcfAs2110(savedFcf));
        Check("Z15b1 the 2.1.10 .fcf reader refuses a format-2 file at its first token instead of reading an empty curation",
            fcfRefusal is JsonException && fcfRefusal.Message.Contains("Path: $ ", StringComparison.Ordinal), fcfRefusal?.Message);
        var stub = JsonSerializer.Deserialize<FamilyClusterFile>(ProjectFileNotice(resaved))!;
        var projectRefusal = Thrown(() => ReadProjectAs2110(resaved));
        Check("Z15b1 the 2.1.10 project reader reads the notice as an empty curation and refuses at its version check with its own words",
            stub.MyExtendedFastaItems.Count == 0 && projectRefusal is InvalidDataException
            && projectRefusal.Message == "\"" + resaved + "\" is a version 3 project, and this build understands up to version 2. Update FamilyQuant to open it.",
            projectRefusal?.Message);
        Check("Z15b1 the 2.1.10 project reader still opens a version-2 project (the transcription is faithful)",
            Thrown(() => ReadProjectAs2110(legacyProject)) == null && ReadProjectAs2110(legacyProject).ToJsonText() == legacyText);

        // Round trips: the registered parts and the synthetic ones, on several fixtures.
        var fixtures = new (string Name, FamilyClusterFile File)[]
        {
            ("format-2 fixture", CurationFormat2Fixture()), ("streaming fixture", CurationStreamingFixture(64, 4096)),
            ("manual choices", ManualChoiceFile()), ("census", CensusFixture()), ("task", TaskFile()),
        };
        foreach (var (name, file) in fixtures)
        {
            CheckCurationFormatRoundTrip("Z15b1 " + name + " with the registered parts", file);
            byte[] withSynthetic = CheckCurationFormatRoundTrip("Z15b1 " + name + " with two synthetic parts", file, synthetic);
            var body = JsonNode.Parse(withSynthetic)![0]!;
            Check("Z15b1 " + name + ": the synthetic parts shaped the wire only (descriptions in one section, integer sets as text)",
                body["Sections"]!["test-description-pool"] is JsonArray pool && pool.Count == file.MyExtendedFastaItems.Count
                && body["Curation"]!["MyExtendedFastaItems"]!.AsArray().All(p => p!["Description"] == null)
                && body["Curation"]!["LockedUserIds"]!.GetValueKind() == JsonValueKind.String
                && JsonSerializer.Serialize(file.LockedUserIds).StartsWith("[", StringComparison.Ordinal));
        }
        Check("Z15b1 wire only: the shared options gain no converter or resolver from any part",
            FamilyClusterFile.CurationJsonOptions.Converters.Count == 0 && FamilyClusterFile.CurationJsonOptions.TypeInfoResolver is null or DefaultJsonTypeInfoResolver
            && !typeof(FamilyClusterFile).Assembly.GetTypes().SelectMany(t => t.GetCustomAttributes<JsonConverterAttribute>(false)
                .Concat(t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SelectMany(p => p.GetCustomAttributes<JsonConverterAttribute>(false))))
                .Any(a => a.ConverterType != null && (typeof(CurationFormatPart).IsAssignableFrom(a.ConverterType.DeclaringType)
                    || (a.ConverterType.Namespace ?? "").StartsWith("FamilyQuant.CurationFormat2", StringComparison.Ordinal))));

        // Which parts run on read.
        var poolOnly = CurationFormatRegistry.Discover(new[] { typeof(DescriptionPoolTestPart) });
        byte[] poolBytes; using (var stream = new MemoryStream()) { CurationFormat.Write(fixture, stream, poolOnly); poolBytes = stream.ToArray(); }
        byte[] bothBytes; using (var stream = new MemoryStream()) { CurationFormat.Write(fixture, stream, synthetic); bothBytes = stream.ToArray(); }
        Check("Z15b1 a part the file does not list stays inactive on read (an older format-2 file opens in a newer build)",
            CurationFormat.Read(() => new MemoryStream(poolBytes, false), synthetic).ToJsonText() == legacyText);
        Check("Z15b1 a part this build does not know is refused by name, not misread",
            Thrown(() => CurationFormat.Read(() => new MemoryStream(bothBytes, false), poolOnly))?.Message
                == "This curation uses parts this FamilyQuant does not know (test-aaa-first, test-integer-text). Update FamilyQuant to open it.");
        string Patch(byte[] source, Action<JsonObject> patch) { var root = JsonNode.Parse(source)!; patch(root[0]!.AsObject()); return root.ToJsonString(); }
        Check("Z15b1 a newer format is refused with a clear message",
            Thrown(() => FamilyClusterFile.FromJsonText(Patch(poolBytes, d => d[CurationFormat.MarkerProperty] = 3)))?.Message
                == "This curation was written by a newer FamilyQuant (curation format 3); this build reads formats 1 and 2. Update FamilyQuant to open it.");
        string?[] malformed =
        {
            Thrown(() => FamilyClusterFile.FromJsonText(Patch(poolBytes, d => d.Remove(CurationFormat.MarkerProperty))))?.Message,
            Thrown(() => FamilyClusterFile.FromJsonText("[1]"))?.Message,
            Thrown(() => CurationFormat.Read(() => new MemoryStream(Encoding.UTF8.GetBytes(Patch(poolBytes,
                d => d["Parts"] = new JsonArray("test-description-pool", "test-description-pool")))), poolOnly))?.Message,
        };
        Check("Z15b1 a root array that is not a format-2 document is refused as such",
            malformed[0]?.Contains("has no FamilyQuantCurationFormat marker", StringComparison.Ordinal) == true
            && malformed[1]?.Contains("does not start with the document", StringComparison.Ordinal) == true
            && malformed[2]?.Contains("listed twice", StringComparison.Ordinal) == true, string.Join(" | ", malformed));

        // Streaming: bounded writes, short reads, a header larger than the first read buffer.
        var large = CurationStreamingFixture(96, 4096);
        for (int i = 0; i < large.MyExtendedFastaItems.Count; i++)
            large.MyExtendedFastaItems[i].Description = "Synthetic streaming protein " + i.ToString(CultureInfo.InvariantCulture) + " " + new string('D', 1024);
        using (var probe = new SaveWriteProbe())
        {
            CurationFormat.Write(large, probe, synthetic);
            byte[] bytes = probe.ToArray();
            Check("Z15b1 the format-2 writer streams in bounded writes", bytes.Length > 262144 && probe.MaximumWrite < 131072,
                bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes, largest write " + probe.MaximumWrite.ToString(CultureInfo.InvariantCulture));
            var read = CurationFormat.Read(() => new SaveShortRead(bytes), synthetic);
            Check("Z15b1 the format-2 reader works through 7-byte reads, with a header section larger than its first buffer",
                read.ToJsonText() == large.ToJsonText() && JsonNode.Parse(bytes)![0]!["Sections"]!.ToJsonString().Length > 64 * 1024);
        }
        // Z15b1a: the writer refuses a header the reader would refuse, before writing a byte, and names the part.
        var largePool = CurationFormatRegistry.Discover(new[] { typeof(DescriptionPoolTestPart), typeof(LargePoolTestPart) });
        var limited = typeof(CurationFormat).GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic,
            new[] { typeof(FamilyClusterFile), typeof(Stream), typeof(IReadOnlyList<CurationFormatPart>), typeof(int) })!;
        using (var refused = new MemoryStream())
        {
            var error = Thrown(() => limited.Invoke(null, new object?[] { fixture, refused, largePool, 128 * 1024 }));
            Check("Z15b1a the writer refuses a header over its limit before writing, naming the part with the largest section",
                error is InvalidOperationException && refused.Length == 0
                && error.Message.Contains("over the 0.1 MB a reader accepts", StringComparison.Ordinal)
                && error.Message.EndsWith("The part \"test-large-pool\" writes 0.2 MB in its section.", StringComparison.Ordinal), error?.Message);
        }
        using (var accepted = new MemoryStream())
        {
            limited.Invoke(null, new object?[] { fixture, accepted, largePool, 512 * 1024 });
            byte[] bytes = accepted.ToArray();
            Check("Z15b1a under the limit the same parts write, and the 64 MB limit is the reader's own",
                CurationFormat.Read(() => new MemoryStream(bytes, false), largePool).ToJsonText() == legacyText && CurationFormat.MaxHeaderBytes == 64 << 20);
        }
        Check("Z15b1 the stored-curation helpers read and edit both layouts",
            EditAndRead(template) && EditAndRead(legacyProject));
        bool EditAndRead(string project)
        {
            string copy = Path.Combine(folder, "edit-" + Path.GetFileName(project));
            File.Copy(project, copy, overwrite: true);
            EditStoredCuration(copy, curation => curation["ClusteringMethod"] = "Edited");
            using var zip = ZipFile.OpenRead(copy);
            return JsonNode.Parse(StoredCurationJson(zip))!["ClusteringMethod"]!.GetValue<string>() == "Edited"
                && ProjectStore.ReadFqproj(copy).fcf.ClusteringMethod == "Edited";
        }
    }

    private static string ProjectFileNotice(string project)
    {
        using var zip = ZipFile.OpenRead(project);
        using var reader = new StreamReader(zip.GetEntry(ProjectStore.CurationEntryName)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
