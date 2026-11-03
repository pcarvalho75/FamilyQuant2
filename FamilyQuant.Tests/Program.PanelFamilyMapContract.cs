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
            });
            Check("M26 a current map remains current when individual groups conflict", map.IsCurrent);
            Check("M26 non-conflicting current families retain their decisions", map.UserFamilies[10].PanelFamilyKey == "a"
                && map.UserFamilies[20].Decision == FamilyPanelDecision.ReportUnderOthers);
            Check("M26 two keys and mixed outside members are individually flagged", map.ConflictingUserIds.SequenceEqual(new[] { 30, 40 })
                && map.UserFamilies[30].PanelFamilyKey == null && map.UserFamilies[40].PanelFamilyKey == null);
            Check("M26 excluded and merged source rows are retained without double counting", map.PanelRows.Single(r => r.PanelRowKey == "excluded").EffectivePanelKey == null
                && map.PanelRows.Single(r => r.PanelRowKey == "alias").FamilyUserIds.SequenceEqual(new[] { 10 }));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];
}
