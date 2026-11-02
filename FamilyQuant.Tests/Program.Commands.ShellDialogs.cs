using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellDialogsCommandTable() =>
    [
        new("--test-shell-dialogs", 415, TestCommandPhase.Raw, ShellDialogsRequested, RunShellDialogChecks),
    ];

    private static bool ShellDialogsRequested(string[] args) => args.Contains("--test-shell-dialogs", StringComparer.Ordinal);

    private static int RunShellDialogChecks(string[] args, string workDir)
    {
        string dialogWork = Path.Combine(Path.GetTempPath(), "FamilyQuant.Dialog.Tests");
        Directory.CreateDirectory(dialogWork);
        EnsureApplication();
        if (args.Contains("--after-busy", StringComparer.Ordinal)) BusyOperationTests(dialogWork);
        ShellDialogTests(dialogWork);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
