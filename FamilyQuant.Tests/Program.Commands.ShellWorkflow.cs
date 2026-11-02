using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellWorkflowCommandTable() =>
    [
        new("--test-shell-workflow", 10150, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-shell-workflow", RunShellWorkflowChecks),
    ];

    private static int RunShellWorkflowChecks(string[] args, string workDir)
    {
        string output = Path.Combine(Path.GetTempPath(), "FamilyQuant.Shell.Workflow.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        EnsureApplication();
        ShellStatusTests(output);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
