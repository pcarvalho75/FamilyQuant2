using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilySensitivityCommands() =>
    [new("--test-family-sensitivity", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-family-sensitivity", (_, _) =>
    {
        FamilySensitivityTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "family-sensitivity"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];
}
