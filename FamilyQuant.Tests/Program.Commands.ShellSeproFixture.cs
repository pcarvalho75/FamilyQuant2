using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellSeproFixtureCommands() =>
    [
        new("--test-shell-sepro-fixture", 10124, TestCommandPhase.Raw,
            args => args.Length == 2 && args[0] == "--test-shell-sepro-fixture", ShellSeproFixtureCheck)
    ];
}
