using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellExecution3Commands() =>
    [new("--prove-shell-execution3", 10560, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--prove-shell-execution3", (args, _) => ShellExecution3(args))];
}
