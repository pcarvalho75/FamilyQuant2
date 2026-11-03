using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> StreamingProvenanceHashCommands() =>
    [new("--test-streaming-provenance-hash", 18, TestCommandPhase.Raw,
        args => args.Contains("--test-streaming-provenance-hash", StringComparer.Ordinal), (_, _) =>
        {
            StreamingProvenanceHashTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void StreamingProvenanceHashTests()
    {
        Type type = typeof(FamilyClusterFile).Assembly.GetType("FamilyQuant.Evidence.LengthPrefixedSha256")!;
        MethodInfo? appendJson = type.GetMethod("AppendJson", BindingFlags.Instance | BindingFlags.NonPublic);
        Check("J38 streamed length-prefixed JSON fingerprint is present", appendJson != null);
        if (appendJson == null) return;
        MethodInfo append = type.GetMethod("Append", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo finish = type.GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic)!;
        string Streamed(object? value)
        {
            using var target = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
            append.Invoke(target, ["prefix"]);
            appendJson.MakeGenericMethod(value?.GetType() ?? typeof(object)).Invoke(target, [value]);
            append.Invoke(target, ["suffix"]);
            return (string)finish.Invoke(target, null)!;
        }
        string Original(object? value)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string text in new[] { "prefix", JsonSerializer.Serialize(value), "suffix" })
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        object?[] examples = [null, "Português 中文 😀 \\ \"", new { Domain = "PF00001", Start = 1, End = 40 },
            new[] { 0.0, -0.0, 1e-200, 1e200 }, new Dictionary<string, object?> { ["z"] = null, ["a"] = new[] { "I", "L" } }];
        foreach (object? value in examples)
            Check("J38 streamed JSON preserves original UTF8 byte count, prefix and digest " + (value?.GetType().Name ?? "null"), Streamed(value) == Original(value));
        var large = Enumerable.Range(0, 10000).Select(i => new { Accession = "entry-" + i, Sequence = new string('L', 70), Name = "proteína" }).ToArray();
        string expected = Original(large);
        _ = Streamed(large); // Warm serializer metadata and pooled buffers, outside allocation measurement.
        long before = GC.GetAllocatedBytesForCurrentThread();
        string actual = Streamed(large);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("J38 streamed large provenance retains the old digest", actual == expected);
        Check("J38 streamed provenance avoids materializing the complete JSON string/byte array", allocated < 500000,
            allocated.ToString() + " allocated bytes");
    }
}
