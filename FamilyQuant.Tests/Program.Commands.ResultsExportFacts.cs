using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsExportFactsCommandTable() =>
    [
        new("--prove-results-export-facts", 10095, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--prove-results-export-facts",
            (args, _) => ResultsExportFactsCommand(args)),
    ];
}
