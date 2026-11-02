using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CoreRelocationCommandTable() =>
    [
        new("--ledger-core-relocation", 10120, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-core-relocation", (args, _) => CoreRelocationCommand(args)),
    ];
}
