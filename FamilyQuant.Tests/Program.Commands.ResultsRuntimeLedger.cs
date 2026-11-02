using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsRuntimeLedgerCommandTable() =>
    [
        new(ResultsRuntimeSnapshot.Argument, 10093, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == ResultsRuntimeSnapshot.Argument,
            (args, _) => ResultsRuntimeSnapshot.Run(args)),
        new("--prove-results-runtime", 10094, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-results-runtime",
            (args, _) => ResultsRuntimeLedgerCommand(args)),
    ];
}
