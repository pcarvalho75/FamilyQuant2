using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferencePanelLegacyCommands() =>
    [
        new("--test-reference-panel-legacy", 10183, TestCommandPhase.Raw,
            args => args.Length is 1 or 2 && args[0] == "--test-reference-panel-legacy", (args, _) =>
            {
                ReferencePanelLegacyTests(args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "FamilyQuant.ReferencePanelLegacy"));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--record-reference-panel-legacy-project", 10184, TestCommandPhase.Raw,
            args => args.Length == 2 && args[0] == "--record-reference-panel-legacy-project", (args, _) =>
            {
                RecordReferencePanelLegacyProject(Path.GetFullPath(args[1]));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            })
    ];

    private static string ReferencePanelLegacyFixtureRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-panel-v2111");

    private static void RecordReferencePanelLegacyProject(string output)
    {
        int writerVersion = ProjectStore.CurrentFormatVersion;
        Check("Z15a4 the envelope recorder uses the old version-2 project writer", writerVersion == 2,
            "A newer writer must not silently regenerate the legacy input.");
        if (writerVersion != 2) return;
        Directory.CreateDirectory(output);
        string sourcePath = Path.Combine(ReferencePanelLegacyFixtureRoot, "source.json");
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        string sourceJson = Encoding.UTF8.GetString(sourceBytes);
        var file = FamilyClusterFile.FromJsonText(sourceJson);
        string project = Path.Combine(output, "legacy-full.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        using var zip = ZipFile.OpenRead(project);
        using var curation = zip.GetEntry("curation.json")!.Open();
        using var bytes = new MemoryStream(); curation.CopyTo(bytes);
        using var manifestReader = new StreamReader(zip.GetEntry("manifest.json")!.Open(), Encoding.UTF8);
        int version = JsonNode.Parse(manifestReader.ReadToEnd())!["FormatVersion"]!.GetValue<int>();
        Check("Z15a4 the recorded envelope holds the unchanged old curation and project version", version == 2 && sourceBytes.SequenceEqual(bytes.ToArray()));
        File.WriteAllText(Path.Combine(output, "legacy-record.json"), JsonSerializer.Serialize(new
        {
            Baseline = "fq/v2111-release 2167ad77", ProjectFormatVersion = version, CurationFormat = 1,
            CoreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(FamilyClusterFile).Assembly.Location))),
            SourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)),
            EmbeddedCurationSha256 = Convert.ToHexString(SHA256.HashData(bytes.ToArray())),
            ProjectSha256 = HashLegacyFile(project), PanelEntries = file.EvidenceSource!.ReferencePanel!.Entries.Count
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ReferencePanelLegacyTests(string output)
    {
        Section("Z15a4 recorded full reference panel survives old-project open and replay");
        Directory.CreateDirectory(output);
        var format = typeof(FamilyClusterFile).Assembly.GetType("FamilyQuant.CurationFormat");
        var requestType = typeof(ReferencePanelFetcher).Assembly.GetType("FamilyQuant.Evidence.ReferencePanelRequest");
        bool readerReady = format != null && ProjectStore.CurrentFormatVersion >= 3;
        Check("Z15a4 the format-2 reader contract is present", readerReady,
            "Requires Z15b1 8ef550cc; a version-1-only reader cannot close the compatibility gate.");
        Check("Z15a4 the explicit scoped-fetch contract is present", requestType != null, "Requires approved Z15a1/Z15a2.");
        if (!readerReady || requestType == null) return;
        string root = ReferencePanelLegacyFixtureRoot;
        string[] names = { "source.json", "recipe.json", "applied.json", "legacy-full.fqproj", "legacy-record.json" };
        Check("Z15a4 every recorded synthetic input is present", names.All(name => File.Exists(Path.Combine(root, name))));
        if (names.Any(name => !File.Exists(Path.Combine(root, name)))) return;
        var inputHashes = names.ToDictionary(name => name, name => HashLegacyFile(Path.Combine(root, name)), StringComparer.Ordinal);
        string source = File.ReadAllText(Path.Combine(root, "source.json"));
        string applied = File.ReadAllText(Path.Combine(root, "applied.json"));
        var recipe = JsonSerializer.Deserialize<EvidenceRecipe>(File.ReadAllText(Path.Combine(root, "recipe.json")))!;
        var expectedEntries = JsonNode.Parse(source)!["EvidenceSource"]!["ReferencePanel"]!["Entries"]!.DeepClone();
        string expectedPanel = expectedEntries.ToJsonString();
        var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "legacy-record.json")))!;
        Check("Z15a4 fixture provenance pins the original source and legacy project bytes",
            receipt["ProjectFormatVersion"]!.GetValue<int>() == 2 && receipt["CurationFormat"]!.GetValue<int>() == 1
            && receipt["SourceSha256"]!.GetValue<string>() == inputHashes["source.json"]
            && receipt["EmbeddedCurationSha256"]!.GetValue<string>() == inputHashes["source.json"]
            && receipt["ProjectSha256"]!.GetValue<string>() == inputHashes["legacy-full.fqproj"]);
        string oldFcf = Path.Combine(output, "recorded-format1.fcf"); File.WriteAllText(oldFcf, source, new UTF8Encoding(false));
        var detect = format!.GetMethod("Detect", BindingFlags.Public | BindingFlags.Static,
            binder: null, types: new[] { typeof(Func<Stream>) }, modifiers: null)!;
        int inputFormat = (int)detect.Invoke(null, new object[] { (Func<Stream>)(() => File.OpenRead(oldFcf)) })!;
        Check("Z15a4 the frozen curation input is actually format 1", inputFormat == 1);

        var initial = FamilyClusterFile.FromJsonText(source);
        var roundTrip = typeof(Program).GetMethod("CheckCurationFormatRoundTrip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Check("Z15a4 the shared format-roundtrip contract helper is present", roundTrip != null);
        if (roundTrip == null) return;
        var format2 = (byte[])roundTrip.Invoke(null, new object?[] { "Z15a4 full legacy panel", initial, null })!;
        File.WriteAllBytes(Path.Combine(output, "explicit-format2.json"), format2);
        var readers = new (string Name, Func<FamilyClusterFile> Read)[]
        {
            ("text", () => FamilyClusterFile.FromJsonText(source)),
            ("fcf", () => FamilyClusterFile.Load(oldFcf)),
            ("shell", () => ShellProjectOperations.Read(oldFcf).Curation),
            ("project", () => ProjectStore.ReadFqproj(Path.Combine(root, "legacy-full.fqproj")).fcf),
            ("format2", () => FamilyClusterFile.FromJsonText(Encoding.UTF8.GetString(format2)))
        };
        foreach (var reader in readers)
        {
            var file = reader.Read();
            Check("Z15a4 " + reader.Name + " opens the complete original curation", JsonNode.DeepEquals(JsonNode.Parse(source), JsonNode.Parse(file.ToJsonText())));
            Retained(reader.Name + " open", file);
            string beforeProposal = file.ToJsonText();
            var session = new EvidenceGroupingSession(file, "legacy fixture");
            session.Propose(string.Join(", ", recipe.Options.ScopeTerms.Select(term => "\"" + term + "\"")));
            Check("Z15a4 " + reader.Name + " proposing without refetch leaves the old project unchanged", file.ToJsonText() == beforeProposal);
            Retained(reader.Name + " proposal", file);
            EvidenceApply.ApplyRecipe(file, recipe);
            Check("Z15a4 " + reader.Name + " replays the frozen recipe to the exact recorded state", JsonNode.DeepEquals(JsonNode.Parse(applied), JsonNode.Parse(file.ToJsonText())));
            Retained(reader.Name + " replay", file);
            string saved = Path.Combine(output, reader.Name + "-saved.fqproj");
            ProjectStore.WriteFqproj(saved, file, Array.Empty<QuantRow>(), new QuantManifest());
            var reopened = ProjectStore.ReadFqproj(saved).fcf;
            Check("Z15a4 " + reader.Name + " new-format save/open preserves the exact replayed state", JsonNode.DeepEquals(JsonNode.Parse(applied), JsonNode.Parse(reopened.ToJsonText())));
            Retained(reader.Name + " saved", reopened);
        }

        // The sole allowed coverage transition is an explicit public fetch operation. All references
        // are already stored, so the offline handler must see no download for this scope conversion.
        var explicitFetch = ProjectStore.ReadFqproj(Path.Combine(root, "legacy-full.fqproj")).fcf;
        using var handler = new LegacyPanelNoNetworkHandler();
        var request = Activator.CreateInstance(requestType, new object[] { recipe.Options.ScopeTerms, false })!;
        var retry = typeof(EvidenceFetchSession).GetMethod("RetryPanelAsync", BindingFlags.Public | BindingFlags.Static)!;
        var task = (Task)retry.Invoke(null, new object?[] { explicitFetch, handler, null, CancellationToken.None,
            new UniProtFetchOptions { DelayAsync = (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, UtcNow = () => DateTimeOffset.UnixEpoch }, request })!;
        task.GetAwaiter().GetResult();
        var outcome = (EvidenceFetchOutcome)task.GetType().GetProperty("Result")!.GetValue(task)!;
        var coverage = typeof(ReferencePanel).GetProperty("Coverage")!.GetValue(explicitFetch.EvidenceSource!.ReferencePanel!);
        Check("Z15a4 only explicit fetch attaches scoped coverage, reusing every stored legacy entry",
            outcome.Changed && coverage != null && handler.Requests == 0
            && JsonNode.DeepEquals(JsonNode.Parse(expectedPanel), JsonSerializer.SerializeToNode(explicitFetch.EvidenceSource.ReferencePanel!.Entries)));
        Check("Z15a4 every frozen input remains byte unchanged", inputHashes.All(pair => HashLegacyFile(Path.Combine(root, pair.Key)) == pair.Value));
        File.WriteAllText(Path.Combine(output, "legacy-panel.json"), JsonSerializer.Serialize(new
        {
            Inputs = inputHashes, RecordedEntries = expectedEntries.AsArray().Count, Readers = readers.Select(reader => reader.Name).ToArray(),
            ReplayComparison = "All recorded JSON values, with no field normalization; whitespace/property order are not scientific state",
            EntryComparison = "Every original entry and field, in original order; Coverage stays absent until explicit public fetch",
            ExplicitFetchRequests = handler.Requests
        }, new JsonSerializerOptions { WriteIndented = true }));

        void Retained(string stage, FamilyClusterFile file)
        {
            var panel = file.EvidenceSource!.ReferencePanel!;
            Check("Z15a4 " + stage + " retains every recorded full-panel entry and has no implicit scope",
                panel.IsComplete && typeof(ReferencePanel).GetProperty("Coverage")!.GetValue(panel) == null
                && JsonNode.DeepEquals(JsonNode.Parse(expectedPanel), JsonSerializer.SerializeToNode(panel.Entries)));
        }
    }

    private sealed class LegacyPanelNoNetworkHandler : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromException<HttpResponseMessage>(new InvalidOperationException("The stored full panel must satisfy this explicit scope conversion without a download."));
        }
    }

    private static string HashLegacyFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
