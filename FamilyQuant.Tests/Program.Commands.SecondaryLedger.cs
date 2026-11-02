using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SecondaryLedgerCommandTable() =>
    [
        new("--ledger-secondary-compare", 10400, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-secondary-compare", (args, _) => SecondaryLedgerCommand(args)),
        new("--ledger-secondary-runtime", 10410, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-secondary-runtime", (args, _) => SecondaryRuntimeCommand(args)),
    ];
}
