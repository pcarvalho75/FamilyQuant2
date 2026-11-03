using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HomologyCaptureReplayCommands() =>
    [new("--replay-homology-capture", 17, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--replay-homology-capture", (args, _) =>
        {
            if (args.Length != 4) throw new ArgumentException("Use --replay-homology-capture <capture> <fresh-output> <expected-call-count>.");
            return ReplayHomologyCapture(args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture));
        })];

    private sealed record CapturedProtein(string Id, string Sequence);
    private sealed record CapturedSearch(int Call, int Queries, int Proteins, int SeedLength, bool HasSeedIndex,
        string DatabaseSha256, string QueriesSha256, string OptionsSha256, string ResultsSha256);

    private static int ReplayHomologyCapture(string captureDirectory, string outputDirectory, int expectedCalls)
    {
        string capture = Path.GetFullPath(captureDirectory), output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Use a fresh replay output folder.");
        string[] calls = Directory.GetDirectories(capture).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (calls.Length != expectedCalls) throw new InvalidDataException("Captured call count differs: " + calls.Length);
        Directory.CreateDirectory(output);
        var report = new List<object>();
        bool allEqual = true;
        foreach (string directory in calls)
        {
            var result = ReplayCapturedSearch(directory, output);
            report.Add(result.Report); allEqual &= result.Equal;
            File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(new
                { ExpectedCalls = expectedCalls, CompletedCalls = report.Count, AllEqual = allEqual, Calls = report },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(result.Report));
        }
        return allEqual ? 0 : 1;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (bool Equal, object Report) ReplayCapturedSearch(string directory, string output)
    {
        string Hash(string name)
        {
            using var file = File.OpenRead(Path.Combine(directory, name));
            return Convert.ToHexString(SHA256.HashData(file));
        }
        T Read<T>(string name)
        {
            using var file = File.OpenRead(Path.Combine(directory, name));
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<T>(gzip) ?? throw new InvalidDataException("Missing captured data: " + name);
        }
        var metadata = JsonSerializer.Deserialize<CapturedSearch>(File.ReadAllText(Path.Combine(directory, "call.json")))
            ?? throw new InvalidDataException("Missing captured search metadata.");
        if (Hash("database.json.gz") != metadata.DatabaseSha256 || Hash("queries.json.gz") != metadata.QueriesSha256
            || Hash("options.json.gz") != metadata.OptionsSha256 || Hash("results.json.gz") != metadata.ResultsSha256)
            throw new InvalidDataException("Captured search file changed.");
        var proteins = Read<List<CapturedProtein>>("database.json.gz");
        var queries = Read<string[]>("queries.json.gz");
        var options = Read<HomologyOptions>("options.json.gz");
        if (proteins.Count != metadata.Proteins || queries.Length != metadata.Queries) throw new InvalidDataException("Capture count mismatch.");
        var database = HomologyDatabase.Build(proteins.Select(p => (p.Id, p.Sequence)), metadata.SeedLength, metadata.HasSeedIndex);
        using var process = Process.GetCurrentProcess();
        TimeSpan cpuStart = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
#pragma warning disable CS0618 // Explicit equivalence replay against the frozen reference.
        var actual = HomologyMapper.Search(queries, database, options);
#pragma warning restore CS0618
        watch.Stop();
        // The recorder uses this exact field order. Both identities, matched residues and both
        // competition flags participate; only candidate/index work diagnostics are excluded.
        var shape = actual.Select(r => new { r.Peptide, r.NormalizedPeptide, r.Rejection,
            r.DecoysSuppressed, r.TargetsSuppressedByDecoyCompetition,
            Hits = r.Hits.Select(h => new { h.ProteinId, h.Offset, h.Score, h.IdentityFraction,
                h.GateIdentityFraction, h.MatchedSubsequence, h.IsDecoy }) });
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(shape);
        byte[] expected;
        using (var file = File.OpenRead(Path.Combine(directory, "results.json.gz")))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var memory = new MemoryStream()) { gzip.CopyTo(memory); expected = memory.ToArray(); }
        bool equal = bytes.AsSpan().SequenceEqual(expected);
        string before = Convert.ToHexString(SHA256.HashData(bytes)), after = Convert.ToHexString(SHA256.HashData(expected));
        if (!equal) File.WriteAllBytes(Path.Combine(output, "mismatch-old-" + metadata.Call + ".json"), bytes);
        return (equal, new { metadata.Call, metadata.Queries, metadata.Proteins, Hits = actual.Sum(r => r.Hits.Count),
            Equal = equal, BeforeSha256 = before, AfterSha256 = after, LegacySeconds = watch.Elapsed.TotalSeconds,
            LegacyCpuSeconds = (process.TotalProcessorTime - cpuStart).TotalSeconds,
            CapturedDatabaseSha256 = metadata.DatabaseSha256, CapturedQueriesSha256 = metadata.QueriesSha256,
            CapturedOptionsSha256 = metadata.OptionsSha256 });
    }
}
