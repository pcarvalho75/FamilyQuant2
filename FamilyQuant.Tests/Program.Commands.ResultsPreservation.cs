using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsPreservationCommandTable() =>
    [
        new("--prove-results-interactions", 10070, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-results-interactions", ResultsPreservationCommand),
        new("--prove-results-members", 10080, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-results-members", (args, _) => ResultsMemberComparisonCommand(args)),
    ];
}
