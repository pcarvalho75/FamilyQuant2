using System.Collections.Generic;

namespace FamilyQuant.Tests;
internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> UpdateCheckCommands() =>
    [new("--test-update-check", 10610, TestCommandPhase.Raw,
        args => args.Length == 2 && args[0] == "--test-update-check", UpdateCheckCommand)];
}
