using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelFamilyMapContractCommandTable() =>
    [
        new("--test-panel-family-map", 77, TestCommandPhase.Raw, a => a.Contains("--test-panel-family-map", StringComparer.Ordinal), (_, _) =>
        {
            var panel = new FamilyPanel { Families = new()
            {
                new() { Key = "a", Decision = FamilyPanelDecision.Quantify },
                new() { Key = "b", Decision = FamilyPanelDecision.ReportUnderOthers },
                new() { Key = "alias", Decision = FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey = "a" },
                new() { Key = "excluded", Decision = FamilyPanelDecision.NotAFamily },
            } };
            var map = PanelAssignmentEvidenceStore.BuildFamilyMap(panel, new[]
            {
                new PanelProteinAssignment { FamilyUserId = 10, PanelFamilyKey = "a", IsApplied = true },
                new PanelProteinAssignment { FamilyUserId = 20, PanelFamilyKey = "b", IsApplied = true },
                new PanelProteinAssignment { FamilyUserId = 30, PanelFamilyKey = "a", IsApplied = true },
                new PanelProteinAssignment { FamilyUserId = 30, PanelFamilyKey = "b", IsApplied = true },
                new PanelProteinAssignment { FamilyUserId = 40, PanelFamilyKey = "a", IsApplied = true },
                new PanelProteinAssignment { FamilyUserId = 40, IsApplied = false },
                new PanelProteinAssignment { FamilyUserId = 50, PanelFamilyKey = "excluded", IsApplied = true },
                new PanelProteinAssignment { FamilyUserId = 60, PanelFamilyKey = "a", IsApplied = true },
                new PanelProteinAssignment { FamilyUserId = 60, PanelFamilyKey = "excluded", IsApplied = true },
                new PanelProteinAssignment { FamilyUserId = 70, PanelFamilyKey = "excluded", IsApplied = true },
                new PanelProteinAssignment { FamilyUserId = 70, IsApplied = false },
            });
            Check("M26 a current map remains current when individual groups conflict", map.IsCurrent);
            Check("M26 non-conflicting current families retain their decisions", map.UserFamilies[10].PanelFamilyKey == "a"
                && map.UserFamilies[20].Decision == FamilyPanelDecision.ReportUnderOthers);
            Check("M26 two keys and mixed outside members are individually flagged", map.ConflictingUserIds.SequenceEqual(new[] { 30, 40, 60, 70 })
                && map.UserFamilies[30].PanelFamilyKey == null && map.UserFamilies[40].PanelFamilyKey == null);
            Check("M26 excluded and merged source rows are retained without double counting", map.PanelRows.Single(r => r.PanelRowKey == "excluded").EffectivePanelKey == null
                && map.PanelRows.Single(r => r.PanelRowKey == "excluded").FamilyUserIds.Count == 0
                && map.PanelRows.Single(r => r.PanelRowKey == "alias").FamilyUserIds.SequenceEqual(new[] { 10 }));
            Check("M43 excluded user families retain the panel key and decision for reporting", map.UserFamilies[50].PanelFamilyKey == "excluded"
                && map.UserFamilies[50].PanelKeys.SequenceEqual(new[] { "excluded" })
                && map.UserFamilies[50].Decision == FamilyPanelDecision.NotAFamily
                && !map.UserFamilies[50].HasOutsideMembers && !map.UserFamilies[50].HasConflict);
            Check("M43 excluded keys mixed with included or outside members remain conflicts", map.UserFamilies[60].PanelKeys.SequenceEqual(new[] { "a", "excluded" })
                && map.UserFamilies[60].HasConflict && !map.UserFamilies[60].HasOutsideMembers
                && map.UserFamilies[70].PanelKeys.SequenceEqual(new[] { "excluded" })
                && map.UserFamilies[70].HasConflict && map.UserFamilies[70].HasOutsideMembers);
            var shares = new FamilyRunSharesResult { Families = new()
            {
                new() { FamilyUserId = 10, MeanShare = .1 }, new() { FamilyUserId = 20, MeanShare = .1 },
                new() { FamilyUserId = 50, MeanShare = .1 }, new() { FamilyUserId = 60, MeanShare = .1 },
                new() { FamilyUserId = 70, MeanShare = .1 },
            } };
            var report = FamilyReportThreshold.Build(shares, new FamilyClusterFile { FamilyPanel = panel }, map);
            Check("M43 the report excludes Not-a-family UserIDs through the producer map", report.ExcludedFamilies.Select(f => f.FamilyUserId).SequenceEqual(new[] { 50 })
                && report.ListedFamilies.Select(f => f.FamilyUserId).SequenceEqual(new[] { 10 })
                && report.OtherFamilies.Select(f => f.FamilyUserId).SequenceEqual(new[] { 20 })
                && report.ConflictFamilies.Select(f => f.Family.FamilyUserId).SequenceEqual(new[] { 60, 70 }));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];
}
