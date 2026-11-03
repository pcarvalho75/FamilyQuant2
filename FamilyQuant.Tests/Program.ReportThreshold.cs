using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportThresholdCommandTable() =>
    [
        new("--test-report-threshold", 72, TestCommandPhase.Raw,
            args => args.Contains("--test-report-threshold", StringComparer.Ordinal), (_, _) =>
            {
                ReportThresholdTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void ReportThresholdTests()
    {
        var type = typeof(FamilyRunSharesResult).Assembly.GetType("FamilyQuant.FamilyReportThreshold");
        var build = type?.GetMethod("Build", new[] { typeof(FamilyRunSharesResult), typeof(double) });
        var setting = typeof(FamilyClusterFile).GetProperty("ReportThreshold");
        Check("J5c threshold builder and persisted project setting exist", build != null && setting != null);
        if (build == null || setting == null) return;

        FamilyRunShareSummary Row(int id, double? mean, double? a, double? b) => new()
        {
            FamilyUserId = id, MeanShare = mean,
            Runs = new() { new() { Run = "a", ShareOfAssignedSignal = a }, new() { Run = "b", ShareOfAssignedSignal = b } }
        };
        var input = new FamilyRunSharesResult
        {
            Runs = new() { new() { Run = "a", AssignedSignal = 100, MeasuredSignal = 100 }, new() { Run = "b", AssignedSignal = 100, MeasuredSignal = 100 } },
            Families = new() { Row(1, .001, .0005, .0015), Row(2, .0008, .0014, .0002),
                Row(3, .0004, .0006, .0002), Row(4, null, null, null) }
        };
        var result = (FamilyReportThresholdResult)build.Invoke(null, new object[] { input, .001 })!;
        Check("J5c compares the mean strictly below threshold and retains unavailable rows",
            result.ListedFamilies.Select(x => x.FamilyUserId).SequenceEqual(new[] { 1 })
            && result.OtherFamilies.Select(x => x.FamilyUserId).SequenceEqual(new[] { 2, 3 })
            && result.UnavailableFamilies.Select(x => x.FamilyUserId).SequenceEqual(new[] { 4 }));
        Check("J5c others aggregate run shares and mean without losing the supplement rows",
            result.OthersRuns.Count == 2 && Math.Abs(result.OthersRuns[0].ShareOfAssignedSignal!.Value - .002) < 1e-12
            && Math.Abs(result.OthersRuns[1].ShareOfAssignedSignal!.Value - .0004) < 1e-12
            && Math.Abs(result.OthersMeanShare!.Value - .0012) < 1e-12);
        var missing = new FamilyRunSharesResult { Runs = new() { new() { Run = "a", AssignedSignal = 100, MeasuredSignal = 100 }, new() { Run = "b", AssignedSignal = 100, MeasuredSignal = 100 } },
            Families = new() { Row(5, .0001, .0001, null) } };
        var incomplete = (FamilyReportThresholdResult)build.Invoke(null, new object[] { missing, .001 })!;
        Check("J5c missing run shares remain unavailable instead of being treated as zero",
            incomplete.OthersRuns[1].ShareOfAssignedSignal == null && incomplete.OthersMeanShare == .0001);
        bool rejected = false;
        try { FamilyReportThreshold.Build(input, double.NaN); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check("J5c rejects an invalid threshold", rejected);
        var emptyOthers = new FamilyRunSharesResult
        {
            Runs = new() { new() { Run = "a", AssignedSignal = 10, MeasuredSignal = 0 },
                new() { Run = "b", AssignedSignal = 0, MeasuredSignal = 0 },
                new() { Run = "c", AssignedSignal = 0, MeasuredSignal = 20 } },
            Families = new() { Row(1, 1, 1, null) }
        };
        var emptyResult = FamilyReportThreshold.Build(emptyOthers, .001);
        Check("J5c empty others respects unavailable assigned and measured denominators",
            emptyResult.OtherFamilies.Count == 0 && emptyResult.OthersRuns[0].ShareOfAssignedSignal == 0
            && emptyResult.OthersRuns[0].ShareOfMeasuredSignal == null
            && emptyResult.OthersRuns[1].ShareOfAssignedSignal == null && emptyResult.OthersRuns[1].ShareOfMeasuredSignal == null
            && emptyResult.OthersRuns[2].ShareOfAssignedSignal == null && emptyResult.OthersRuns[2].ShareOfMeasuredSignal == 0);
        var inconsistent = Row(5, .0001, .0001, null);
        inconsistent.Runs[0].ShareOfMeasuredSignal = .0001;
        var noDenominators = new FamilyRunSharesResult { Runs = new() { new() { Run = "a", AssignedSignal = 0, MeasuredSignal = 0 },
            new() { Run = "b", AssignedSignal = 1, MeasuredSignal = 1 } }, Families = new() { inconsistent } };
        var nonemptyResult = FamilyReportThreshold.Build(noDenominators, .001);
        Check("J5c nonempty others also respects unavailable denominators",
            nonemptyResult.OtherFamilies.Count == 1 && nonemptyResult.OthersRuns[0].ShareOfAssignedSignal == null
            && nonemptyResult.OthersRuns[0].ShareOfMeasuredSignal == null);
        var project = new FamilyClusterFile();
        Check("J5c default threshold is 0.1 percent", setting.GetValue(project) == null && FamilyReportThreshold.Build(input, project).Threshold == .001);
        setting.SetValue(project, .025);
        var reopened = FamilyClusterFile.FromJsonText(project.ToJsonText());
        Check("J5c user threshold persists with the project", (double?)setting.GetValue(reopened) == .025);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".fqproj");
        try
        {
            ProjectStore.WriteFqproj(path, project, new List<QuantRow>(), new QuantManifest());
            Check("J5c threshold survives project archive reopen", ProjectStore.ReadFqproj(path).fcf.ReportThreshold == .025);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
}


