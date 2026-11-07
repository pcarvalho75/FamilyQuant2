using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsExportFactsCommandTable() =>
    [
        new("--test-results-export-facts", 10094, TestCommandPhase.Raw,
            args => args.Length is 2 or 3 && args[0] == "--test-results-export-facts"
                && (args.Length == 2 || args[2] == "--timeout-probe"),
            (args, _) => ResultsExportFixtureCommand(args)),
        new("--prove-results-export-facts", 10095, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-results-export-facts",
            (args, _) => ResultsExportFactsCommand(args)),
    ];
}
