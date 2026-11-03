using System;
using System.Collections.Generic;
using System.Linq;
using FamilyQuant.Panels;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportThresholdPolicyCommandTable() =>
    [
        new("--test-report-threshold-policy", 78, TestCommandPhase.Raw,
            args => args.Contains("--test-report-threshold-policy", StringComparer.Ordinal), (_, _) =>
            {
                ReportThresholdPolicyTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void ReportThresholdPolicyTests()
    {
        var method = typeof(FamilyReportThreshold).GetMethod("Build", new[] { typeof(FamilyRunSharesResult), typeof(FamilyClusterFile), typeof(IReadOnlyDictionary<int, IReadOnlyList<string>>) });
        Check("J5c canonical accessor accepts explicit family-to-panel mappings", method != null);
        if (method == null) return;
        FamilyRunShareSummary Row(int id, double mean, double a, double b, double measuredA, double measuredB) => new()
        {
            FamilyUserId = id, MeanShare = mean,
            Runs = new() { new() { Run = "a", ShareOfAssignedSignal = a, ShareOfMeasuredSignal = measuredA },
                new() { Run = "b", ShareOfAssignedSignal = b, ShareOfMeasuredSignal = measuredB } }
        };
        var source = new FamilyRunSharesResult { Runs = new() { new() { Run = "a", AssignedSignal = 100, MeasuredSignal = 200 },
            new() { Run = "b", AssignedSignal = 100, MeasuredSignal = 200 } }, Families = new()
        {
            Row(1, .0005, .0002, .0008, .0001, .0004), Row(2, .2, .1, .3, .05, .15),
            Row(3, .0001, .0001, .0001, .0001, .0001), Row(4, .1, .1, .1, .1, .1), Row(5, .5, .5, .5, .5, .5)
        } };
        var project = new FamilyClusterFile { FamilyPanel = new FamilyPanel { Families = new()
        {
            new() { Key = "under", DisplayName = "Under" },
            new() { Key = "forced", DisplayName = "Forced", Decision = FamilyPanelDecision.ReportUnderOthers },
            new() { Key = "excluded", DisplayName = "Excluded", Decision = FamilyPanelDecision.NotAFamily },
            new() { Key = "one", DisplayName = "One" }, new() { Key = "two", DisplayName = "Two" },
        } } };
        IReadOnlyDictionary<int, IReadOnlyList<string>> mappings = new Dictionary<int, IReadOnlyList<string>>
        { [1] = new[] { "under" }, [2] = new[] { "forced" }, [3] = new[] { "excluded" }, [4] = new[] { "one", "two" } };
        dynamic result = method.Invoke(null, new object[] { source, project, mappings })!;
        var others = (IEnumerable<FamilyRunShareSummary>)result.OtherFamilies;
        var listed = (IEnumerable<FamilyRunShareSummary>)result.ListedFamilies;
        var excluded = (IEnumerable<FamilyRunShareSummary>)result.ExcludedFamilies;
        Check("J5c threshold and explicit panel decisions share one canonical partition",
            others.Select(x => x.FamilyUserId).SequenceEqual(new[] { 1, 2 }) && excluded.Single().FamilyUserId == 3
            && listed.Single().FamilyUserId == 5 && ((System.Collections.ICollection)result.ConflictFamilies).Count == 1);
        var reasons = ((System.Collections.IEnumerable)result.OtherMembership).Cast<dynamic>().ToDictionary(x => (int)x.FamilyUserId, x => x.Reason.ToString());
        Check("J5c records why each family enters others", reasons[1].Contains("Threshold", StringComparison.Ordinal)
            && reasons[2].Contains("PanelDecision", StringComparison.Ordinal));
        dynamic assigned = result.OthersAssignedStatistics, measured = result.OthersMeasuredStatistics;
        bool Near(double? a, double b) => a.HasValue && Math.Abs(a.Value - b) < 1e-12;
        Check("J5c assigned others statistics derive from summed per-run shares", (int)assigned.AvailableRunCount == 2
            && Near((double?)assigned.Mean, .2005) && Near((double?)assigned.StandardDeviation, .2006 / Math.Sqrt(2))
            && Near((double?)assigned.CoefficientOfVariation, (.2006 / Math.Sqrt(2)) / .2005));
        Check("J5c measured others statistics use the measured per-run shares", (int)measured.AvailableRunCount == 2
            && Near((double?)measured.Mean, .10025) && Near((double?)measured.StandardDeviation, .1003 / Math.Sqrt(2)));
        source.Runs[1].MeasuredSignal = 0;
        dynamic incomplete = method.Invoke(null, new object[] { source, project, mappings })!;
        dynamic incompleteMeasured = incomplete.OthersMeasuredStatistics;
        Check("J5c incomplete measured runs do not create a complete measured summary", (int)incompleteMeasured.AvailableRunCount == 1
            && incompleteMeasured.Mean == null && incompleteMeasured.StandardDeviation == null && incompleteMeasured.CoefficientOfVariation == null);
        project.MyExtendedFastaItems = new()
        {
            new FastaItemExtended { SequenceIdentifier = "p1", UserID = 1 }, new FastaItemExtended { SequenceIdentifier = "p2", UserID = 2 },
            new FastaItemExtended { SequenceIdentifier = "p4a", UserID = 4 }, new FastaItemExtended { SequenceIdentifier = "p4b", UserID = 4 },
        };
        project.PanelProteinAssignments = new()
        {
            new PanelProteinAssignment { ProteinIndex = 0, Identifier = "p1", FamilyUserId = 1, PanelFamilyKey = "under", IsApplied = true },
            new PanelProteinAssignment { ProteinIndex = 1, Identifier = "p2", FamilyUserId = 2, PanelFamilyKey = "forced", IsApplied = true },
            new PanelProteinAssignment { ProteinIndex = 2, Identifier = "p4a", FamilyUserId = 4, PanelFamilyKey = "one", IsApplied = true },
            new PanelProteinAssignment { ProteinIndex = 3, Identifier = "p4b", FamilyUserId = 4, PanelFamilyKey = "two", IsApplied = true },
        };
        project.PanelProteinAssignmentsPanelFingerprint = PanelDerivedDataCurrentness.Fingerprint(project.FamilyPanel!);
        var fromProject = FamilyReportThreshold.Build(source, project);
        Check("J5c project accessor uses current applied assignments and flags conflicts",
            fromProject.OtherFamilies.Select(x => x.FamilyUserId).SequenceEqual(new[] { 1, 2, 3 })
            && fromProject.ConflictFamilies.Single().Family.FamilyUserId == 4);
        project.PanelProteinAssignmentsPanelFingerprint = "stale";
        bool staleRefused = false;
        try { FamilyReportThreshold.Build(source, project); } catch (InvalidOperationException) { staleRefused = true; }
        Check("J5c refuses stale panel assignment evidence", staleRefused);
        project.PanelProteinAssignmentsPanelFingerprint = PanelDerivedDataCurrentness.Fingerprint(project.FamilyPanel!);
        project.MyExtendedFastaItems[1].UserID = 99;
        bool changedRefused = false;
        try { FamilyReportThreshold.Build(source, project); } catch (InvalidOperationException) { changedRefused = true; }
        Check("J5c refuses assignment IDs that no longer match project proteins", changedRefused);
        var peptideMap = new PeptideFamilyAttribution();
        peptideMap.IdentifiedBySequence["AAAAAAAK"] = new[] { 1 };
        peptideMap.IdentifiedBySequence["CCCCCCCK"] = new[] { 2 };
        var nativeRows = new[]
        {
            Qr("identified", "A", 1, 2, "AAAAAAAK", 1, 1), Qr("identified", "A", 2, 2, "CCCCCCCK", 99, 99),
            Qr("identified", "B", 3, 2, "AAAAAAAK", 1, 1), Qr("identified", "B", 4, 2, "CCCCCCCK", 1, 1),
        };
        var nativeShares = FamilyCompositionStudy.BuildRunShares(FamilyEnvelopeQuant.AttributePeptides(
            FamilyEnvelopeQuant.PreparePeptides(nativeRows), peptideMap));
        var nativeReport = FamilyReportThreshold.Build(nativeShares, .3);
        Check("J5c consumes native run shares and groups on mean rather than pooled signal",
            nativeReport.OtherFamilies.Single().FamilyUserId == 1 && Near(nativeReport.OthersAssignedStatistics.Mean, .255)
            && nativeShares.Families.Single(x => x.FamilyUserId == 1).PooledShare < .03);
    }
}
