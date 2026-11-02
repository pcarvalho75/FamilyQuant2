using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsTooltipCommandTable() =>
    [
        new("--test-results-tooltips", 10092, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-results-tooltips", (args, _) => ResultsTooltipProof(args)),
    ];
}
