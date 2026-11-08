using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> JsonStreamCopyCommands() =>
    [new("--test-json-stream-copy", 453, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-json-stream-copy", (_, _) =>
        {
            JsonStreamCopyTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void JsonStreamCopyTests()
    {
        Section("Shared JSON store: exact streamed comparison and default-contract copies");
        var type = typeof(FamilyClusterFile).Assembly.GetType("FamilyQuant.JsonStreamCopy")!;
        var equal = type.GetMethod("SameSerializedBytes", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(List<string>));
        var clone = type.GetMethod("Clone", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(List<string>));
        bool Same(List<string>? first, List<string>? second) => (bool)equal.Invoke(null, new object?[] { first, second })!;
        var cases = new (List<string>? First, List<string>? Second)[]
        {
            (null, null), (null, new()), (new(), new()),
            (new() { "one", "two" }, new() { "one", "two" }),
            (new() { "one", "two" }, new() { "two", "one" }),
            (new() { "one" }, new() { "one", "tail" }),
            (new() { "Unicode \u00e1 \ud83e\uddec \"quoted\"\nline" }, new() { "Unicode \u00e1 \ud83e\uddec \"quoted\"\nline" }),
        };
        for (int index = 0; index < cases.Length; index++)
        {
            var item = cases[index];
            bool expected = JsonSerializer.SerializeToUtf8Bytes(item.First).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(item.Second));
            Check("shared JSON comparison matches literal bytes for case " + index, Same(item.First, item.Second) == expected);
        }
        var large = Enumerable.Range(0, 128).Select(index => index.ToString(CultureInfo.InvariantCulture) + new string('x', 4096)).ToList();
        var before = Directory.GetFiles(Path.GetTempPath(), "FamilyQuant-json-copy-*.tmp").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var detached = (List<string>)clone.Invoke(null, new object[] { large })!;
        Check("shared JSON copy preserves the complete large default-JSON value", JsonSerializer.Serialize(large) == JsonSerializer.Serialize(detached)
            && !ReferenceEquals(large, detached));
        Check("shared JSON equality compares values beyond its memory threshold", Same(large, detached));
        detached[^1] += "changed";
        Check("shared JSON equality notices a difference after the memory threshold", !Same(large, detached));
        Check("shared JSON comparison and copy remove their spill files", Directory.GetFiles(Path.GetTempPath(), "FamilyQuant-json-copy-*.tmp").All(before.Contains));
    }
}
