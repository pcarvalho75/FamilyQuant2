using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> InterpretationNumberTraceCommands() =>
    [new("--test-interpretation-number-trace", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-interpretation-number-trace", (_, _) =>
        {
            InterpretationNumberTraceTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void InterpretationNumberTraceTests()
    {
        var cells = new[] { ("count", "1200"), ("share_fraction", "0.125"), ("rate", "2.3456"), ("value", "-0.2") };
        try
        {
            var missing = InterpretationNumberTrace.Unmatched("Count 1,200; share 12.5%; rate 2.35; values -0.2 and 97 and 97.", cells);
            Check("J46 numeric trace accepts exact numbers and percentages from fraction columns", !missing.Contains("1,200") && !missing.Contains("12.5%") && !missing.Contains("-0.2"));
            Check("J46 numeric trace flags invented and rounded numbers at each occurrence", missing.SequenceEqual(new[] { "2.35", "97", "97" }));
            var percent = InterpretationNumberTrace.Unmatched("Rate 234.56%", cells);
            Check("J46 percent conversion is not invented for non-fraction columns", percent.SequenceEqual(new[] { "234.56%" }));
        }
        catch
        {
            Check("J46 numeric trace accepts exact numbers and percentages from fraction columns", false);
            Check("J46 numeric trace flags invented and rounded numbers at each occurrence", false);
            Check("J46 percent conversion is not invented for non-fraction columns", false);
        }
    }
}
