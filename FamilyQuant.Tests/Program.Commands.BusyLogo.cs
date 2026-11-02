using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> BusyLogoCommandTable() =>
    [
        new("--test-busy-logo", 421, TestCommandPhase.Raw,
            args => args.Length == 2 && args[0] == "--test-busy-logo", (args, _) => BusyLogoProof(args[1]))
    ];
}
