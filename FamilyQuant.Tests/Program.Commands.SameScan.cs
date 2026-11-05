using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SameScanCommands() =>
    [
        new("--same-scan-conflicts", 10100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--same-scan-conflicts", (args, _) => SameScanConflictsCommand(args)),
        new("--test-same-scan-conflicts", 10100, TestCommandPhase.Raw, args => args.Contains("--test-same-scan-conflicts", StringComparer.Ordinal), (_, _) =>
        {
            SameScanConflictTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];
}
