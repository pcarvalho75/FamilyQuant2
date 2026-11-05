using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PatternLabPriorityCommands() =>
    [
        new("--patternlab-priority-replay", 10100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--patternlab-priority-replay", (args, _) => PatternLabPriorityReplay(args)),
        new("--test-patternlab-priority", 10100, TestCommandPhase.Raw, args => args.Contains("--test-patternlab-priority", StringComparer.Ordinal), (_, _) =>
        {
            PatternLabPriorityTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "scan-rule"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];
}
