using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LedgerTextPreservedCommandTable() =>
    [
        new("--ledger-text-preserved", 10135, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-text-preserved", (args, _) => LedgerTextPreservedCommand(args)),
    ];
}
