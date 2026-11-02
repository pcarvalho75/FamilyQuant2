using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ShellMetadataCommandTable() =>
    [
        new("--ledger-shell-native", 10118, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--ledger-shell-native", (args, _) => ShellMetadataReceipts(args)),
        new("--test-shell-metadata", 10117, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-shell-metadata", ShellMetadataTests),
        new(ShellMetadataSnapshot.Argument, 10116, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == ShellMetadataSnapshot.Argument, (args, _) => ShellMetadataSnapshot.Run(args)),
    ];
}
