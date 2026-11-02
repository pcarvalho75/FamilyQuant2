using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ShellMessageFrameCommand(string[] args, string workDir)
    {
        ShellMessageFrameTests(System.Array.IndexOf(args, "--negative-control") >= 0);
        System.Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellRoutesCommandTable() =>
    [
        new("--prove-shell-main", 10123, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-shell-main", (args, _) => ShellMainReceipts(args)),
        new("--shell-main-message-probe", 10122, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--shell-main-message-probe", (args, _) => ShellMainMessageProbe(args)),
        new("--test-shell-message-frames", 10121, TestCommandPhase.Raw,
            args => args.Length is 1 or 2 && args[0] == "--test-shell-message-frames", ShellMessageFrameCommand),
        new("--shell-route-probe", 10120, TestCommandPhase.Raw,
            args => args.Length == 2 && args[0] == "--shell-route-probe", (args, _) => ShellRouteProbe(args[1])),
    ];
}
