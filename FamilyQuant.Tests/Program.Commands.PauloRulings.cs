using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PauloRulingsCommandTable() =>
    [
        new(LedgerPauloRulings.Command, 10150, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == LedgerPauloRulings.Command, (args, _) => ApplyPauloRulings(args)),
    ];
}
