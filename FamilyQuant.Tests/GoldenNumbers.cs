using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    internal static class GoldenNumbers
    {
        private const string CompareArgument = "--golden";
        private const string RecordArgument = "--golden-record";
        private const string VerifyArgument = "--golden-verify";
        private const string DefaultProject = "C:/Users/paulo/Desktop/FamilyQuant/BrProt_Talk/all+DeepNovo/scientific_results.fqproj";
        private const string DefaultSnapshot = "D:/FamilyQuant-data/golden";
        private const string DefaultOutput = "D:/FamilyQuant-data/out-evidence/golden-current";
        private const string ReferenceRoot = "D:/FamilyQuant-data/reference-1.5.0.6";

        internal static bool IsRequested(string[] args) => args.Any(IsArgument);

        private static bool IsArgument(string arg) => arg == CompareArgument || arg == RecordArgument || arg == VerifyArgument;

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int index = Array.FindIndex(args, IsArgument);
                string argument = args[index];
                string project = ArgumentAt(1, DefaultProject);
                string snapshot = ArgumentAt(2, DefaultSnapshot);
                string output = ArgumentAt(3, DefaultOutput);
                CheckPaths(project, snapshot, output);

                if (argument == VerifyArgument)
                {
                    VerifySource(snapshot, ProjectHash(project));
                    Compare(snapshot, output, check);
                    return;
                }

                string hash = ProjectHash(project);
                if (argument == RecordArgument) RequireFrozenProduct();
                if (argument == RecordArgument && Directory.Exists(snapshot)
                    && Directory.EnumerateFileSystemEntries(snapshot).Any())
                    throw new InvalidOperationException("The golden snapshot already contains files. Recording never replaces it.");
                if (argument == CompareArgument)
                    VerifySource(snapshot, hash);

                Export(project, output);
                if (hash != ProjectHash(project))
                    throw new InvalidOperationException("The input project changed during the golden export.");

                if (argument == RecordArgument)
                {
                    Directory.CreateDirectory(Path.Combine(snapshot, "exports"));
                    foreach (var file in ReadExports(output))
                        File.WriteAllBytes(Path.Combine(snapshot, "exports", file.Key), file.Value);
                    File.WriteAllText(Path.Combine(snapshot, "source.json"), JsonSerializer.Serialize(new
                    {
                        projectFile = Path.GetFileName(project),
                        sha256 = hash,
                        referenceAssemblySha256 = ProjectHash(Path.Combine(ReferenceRoot, "app", "FamilyQuant.dll")),
                        referenceSource = File.ReadAllText(Path.Combine(ReferenceRoot, "README.txt")),
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    check("golden snapshot recorded from the stored project", true, hash);
                }
                Compare(snapshot, output, check);

                string ArgumentAt(int offset, string fallback) => args.Length > index + offset
                    && !string.IsNullOrWhiteSpace(args[index + offset]) ? args[index + offset] : fallback;
            }
            catch (Exception ex)
            {
                check("golden numbers guard completed", false, ex.GetBaseException().Message);
            }
        }

        private static void CheckPaths(string project, string snapshot, string output)
        {
            string repository = FindRepository();
            if (Within(snapshot, repository) || Within(output, repository))
                throw new ArgumentException("Golden snapshots and outputs must stay outside the repository.");
            if (Within(snapshot, output) || Within(output, snapshot)
                || Within(project, output) || Within(project, snapshot))
                throw new ArgumentException("The input project, snapshot and output folders must be separate.");
        }

        internal static string FindRepository()
        {
            for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "FamilyQuant2.sln")) ||
                    File.Exists(Path.Combine(dir.FullName, "FamilyQuant.sln"))) return dir.FullName;
            throw new DirectoryNotFoundException("Run the golden guard from a FamilyQuant worktree build.");
        }

        private static bool Within(string path, string parent)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string ProjectHash(string project)
        {
            using FileStream input = File.OpenRead(project);
            return Convert.ToHexString(SHA256.HashData(input));
        }

        private static void VerifySource(string snapshot, string hash)
        {
            using JsonDocument source = JsonDocument.Parse(File.ReadAllText(Path.Combine(snapshot, "source.json")));
            if (source.RootElement.GetProperty("sha256").GetString() != hash)
                throw new InvalidDataException("The stored project differs from the golden snapshot's source. The snapshot was kept.");
            if (!source.RootElement.TryGetProperty("referenceAssemblySha256", out JsonElement reference)
                || reference.GetString() != ProjectHash(Path.Combine(ReferenceRoot, "app", "FamilyQuant.dll")))
                throw new InvalidDataException("The snapshot does not identify the frozen reference build.");
        }

        private static void RequireFrozenProduct()
        {
            string expected = ProjectHash(Path.Combine(ReferenceRoot, "app", "FamilyQuant.dll"));
            string running = ProjectHash(typeof(FamilyClusterFile).Assembly.Location);
            string expectedParser = ProjectHash(Path.Combine(ReferenceRoot, "app", "PatternTools.dll"));
            string runningParser = ProjectHash(typeof(PatternTools.FastaTools.FastaItem).Assembly.Location);
            if (running != expected || runningParser != expectedParser)
                throw new InvalidOperationException("Record with ReferenceGoldenHost: the loaded program and parser must match the frozen reference bytes.");
            Console.WriteLine("Golden reference product SHA-256: " + running);
            Console.WriteLine("Golden reference parser SHA-256: " + runningParser);
        }

        private static void Export(string project, string output)
        {
            Console.WriteLine("Golden: opening stored project.");
            var (curation, rows, manifest) = ProjectStore.ReadFqproj(project);
            var control = new ClusterGenerationControl(curation);
            Console.WriteLine("Golden: computing the current family column with the program's homology and attribution.");
            control.SetFullXicQuant(rows, manifest.PerRunTic);
            if (!control.HasFullXic) throw new InvalidOperationException("The stored project did not produce a Full XIC family column.");
            string composition = control.FullXicCompositionCsv()
                ?? throw new InvalidOperationException("The program did not produce a composition CSV.");
            var attribution = control.CurrentFullXicAttribution()
                ?? throw new InvalidOperationException("The program did not produce family attribution.");
            var analytics = new AnalyticsControl(rows, manifest, curation, attribution);
            AnalyticsData data = analytics.Data ?? throw new InvalidOperationException("The analytics screen has no data.");
            FamilyEnvelopeQuantResult families = analytics.Families
                ?? throw new InvalidOperationException("The analytics screen has no family results.");

            // Read the same export state the curation button uses, including its column heading.
            var mode = (NormalizationMode?)typeof(ClusterGenerationControl)
                .GetProperty("DisplayedMode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control);
            string heading = (string)typeof(ClusterGenerationControl)
                .GetMethod("FullXicCsvHeader", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null)!;
            string familyCsv = CsvReport.Build(curation.MyExtendedFastaItems, mode, heading);
            string summary = AnalyticsCsv.BuildSummaryCsv(data.Fractions, data.Grouped);

            Console.WriteLine("Golden: exporting the program's evidence pack into scratch.");
            string pack = Path.Combine(output, "evidence-pack");
            PresentationEvidencePack.Save(data, manifest, curation, families, pack);
            string numbers = NumericValues(File.ReadAllText(Path.Combine(pack, PresentationEvidencePack.PlotDataFile)));
            var exports = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["families.csv"] = familyCsv,
                ["composition.csv"] = composition,
                ["analytics_summary.csv"] = summary,
                ["evidence_pack_numbers.csv"] = numbers,
            };
            Directory.CreateDirectory(Path.Combine(output, "exports"));
            foreach (var file in exports)
                File.WriteAllText(Path.Combine(output, "exports", file.Key), file.Value, new UTF8Encoding(false));
        }

        // Export timestamps are metadata. This guard retains every numeric leaf from the pack writer,
        // with its JSON path, so no floating point number is parsed, rounded or regenerated here.
        internal static string NumericValues(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            var csv = new StringBuilder("\"json_path\",\"number\"\r\n");
            Visit(document.RootElement, "$");
            return csv.ToString();

            void Visit(JsonElement element, string path)
            {
                if (element.ValueKind == JsonValueKind.Number)
                    csv.Append(Field(path)).Append(',').Append(Field(element.GetRawText())).Append("\r\n");
                else if (element.ValueKind == JsonValueKind.Object)
                    foreach (JsonProperty property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                        Visit(property.Value, path + "/" + property.Name.Replace("~", "~0").Replace("/", "~1"));
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    int i = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                        Visit(item, path + "/" + (i++).ToString(CultureInfo.InvariantCulture));
                }
            }
            static string Field(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static Dictionary<string, byte[]> ReadExports(string folder)
        {
            string directory = Path.Combine(folder, "exports");
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("No golden exports at " + directory);
            return Directory.GetFiles(directory).ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes, StringComparer.Ordinal);
        }

        private static void Compare(string snapshot, string output, Action<string, bool, string?> check)
        {
            Dictionary<string, byte[]> expected = ReadExports(snapshot);
            Dictionary<string, byte[]> actual = ReadExports(output);
            if (expected.Count != 4) throw new InvalidDataException("The golden snapshot must contain the four required exports.");
            List<string> differences = Differences(expected, actual);
            foreach (string name in expected.Keys.Union(actual.Keys, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
                check("golden bytes: " + name, !differences.Contains(name), differences.Contains(name) ? "missing or changed bytes" : null);
        }

        internal static List<string> Differences(IReadOnlyDictionary<string, byte[]> expected,
                                                   IReadOnlyDictionary<string, byte[]> actual)
        {
            return expected.Keys.Union(actual.Keys, StringComparer.Ordinal)
                .Where(name => !expected.TryGetValue(name, out byte[]? before)
                    || !actual.TryGetValue(name, out byte[]? after) || !before.SequenceEqual(after))
                .OrderBy(name => name, StringComparer.Ordinal).ToList();
        }
    }
}
