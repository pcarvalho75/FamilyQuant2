using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DatabaseGapCommandTable() =>
    [
        new("--test-database-gap", 10090, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-database-gap", (args, _) =>
            {
                DatabaseGapTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.DatabaseGap.Tests"));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--database-gap-screens", 10091, TestCommandPhase.Raw,
            args => args.Length == 3 && args[0] == "--database-gap-screens", (args, _) => DatabaseGapScreens(args[1], args[2])),
    ];
}
