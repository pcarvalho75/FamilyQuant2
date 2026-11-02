using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> WorldFetchCommandTable() =>
    [
        new("--family-world-fetch", 10110, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--family-world-fetch", (args, _) => FamilyWorldFetchCommand(args)),
        new("--test-family-world-fetch", 10100, TestCommandPhase.Raw, args => args.Contains("--test-family-world-fetch", StringComparer.Ordinal), RunFamilyWorldFetchTests),
    ];

    private static int RunFamilyWorldFetchTests(string[] args, string workDir)
    {
        FamilyWorldFetchTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
