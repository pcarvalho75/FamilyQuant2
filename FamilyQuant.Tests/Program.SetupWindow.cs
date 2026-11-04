using FamilyQuant.Setup;
using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SetupWindowCommands() =>
    [new("--test-setup-window", 10497, TestCommandPhase.Raw, args => args.Length == 1 && args[0] == "--test-setup-window", (_, _) =>
    {
        SetupWindowTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];
    private static void SetupWindowTests()
    {
        EnsureApplication();
        try { foreach (string message in InstallerWindowPreview.CheckInteractions()) Check(message, true); }
        catch (Exception error) { Check("installer window fake-controller interactions", false, error.ToString()); }
    }
}
