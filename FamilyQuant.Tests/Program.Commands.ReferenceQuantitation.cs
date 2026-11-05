using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferenceQuantitationCommands() =>
    [new("--test-reference-quantitation", 10100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-reference-quantitation", (args, _) =>
    {
        if (args.Length != 4) throw new ArgumentException("Supply PEAKS root, acquisition root and owned scratch directory.");
        EnsureApplication();
        string peaks = Path.GetFullPath(args[1]), data = Path.GetFullPath(args[2]), scratch = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(scratch);
        int priorSkipped = skipped;
        FullXicComputeRealTests(data, scratch);
        NativeTicFractionRealTests(peaks, data, scratch);
        Ms1EnvelopeRealTests(peaks, data, scratch);
        AnalyticsRealTests(peaks, data, scratch);
        Check("M13 all four requested real-data reference checks execute without skips", skipped == priorSkipped);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];
}
