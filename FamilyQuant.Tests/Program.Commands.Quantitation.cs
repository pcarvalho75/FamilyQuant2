using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> QuantitationCommandTable() =>
    [
        new(FullXicPipelineSnapshot.Argument, 350, TestCommandPhase.Raw, PipelineSnapshotRequested, RunPipelineSnapshot),
        new("--test-quantitation-stages", 360, TestCommandPhase.Raw, QuantitationStagesRequested, RunQuantitationStages),
    ];

    private static bool PipelineSnapshotRequested(string[] args) =>
        args.Length > 0 && args[0] == FullXicPipelineSnapshot.Argument;

    private static int RunPipelineSnapshot(string[] args, string workDir) => FullXicPipelineSnapshot.Run(args);

    private static bool QuantitationStagesRequested(string[] args) =>
        args.Contains("--test-quantitation-stages", StringComparer.Ordinal);

    private static int RunQuantitationStages(string[] args, string workDir)
    {
        QuantitationStageTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
