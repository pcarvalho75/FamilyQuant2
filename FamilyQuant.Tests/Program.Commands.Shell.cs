using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellCommandTable() =>
    [
        new("--busy-screens", 410, TestCommandPhase.Raw, BusyScreensRequested, RunBusyScreens),
        new("--test-busy", 420, TestCommandPhase.Raw, BusyTestsRequested, RunBusyTests),
    ];

    private static bool BusyScreensRequested(string[] args) => args.Length == 2 && args[0] == "--busy-screens";
    private static bool BusyTestsRequested(string[] args) => args.Contains("--test-busy", StringComparer.Ordinal);

    private static int RunBusyScreens(string[] args, string workDir)
    {
        return BusyScreens(args[1]);
    }

    private static int RunBusyTests(string[] args, string workDir)
    {
        string busyWork = Path.Combine(Path.GetTempPath(), "FamilyQuant.Busy.Tests");
        Directory.CreateDirectory(busyWork);
        EnsureApplication();
        BusyOperationTests(busyWork);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
