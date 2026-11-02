using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CoreRuntimeLedgerCommandTable() =>
    [
        new("--ledger-storage-runtime", 10130, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-storage-runtime", (args, _) => StorageRuntimeLedgerCommand(args)),
    ];
}
