using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelFamilyDependencyContractCommandTable() =>
    [
        new("--test-panel-family-dependencies", 78, TestCommandPhase.Raw, a => a.Contains("--test-panel-family-dependencies", StringComparer.Ordinal), (_, _) =>
        {
            var panel = new FamilyPanel { Families = new() { new() { Key = "one", Pfam = new() { new() { Accession = "domain-a" } } } } };
            string family = PanelDerivedDataCurrentness.FamilyFingerprint(panel), subclass = PanelDerivedDataCurrentness.Fingerprint(panel);
            var file = new FamilyClusterFile { FamilyPanel = panel,
                PanelProteinAssignments = new(), PanelProteinAssignmentsPanelFingerprint = family,
                ProteinSubclassLabels = new(), ProteinSubclassLabelsPanelFingerprint = subclass };
            panel.Families[0].Subclasses.Add(new() { Key = "part", RequiredPfam = new() { "domain-a" } });
            var state = PanelDerivedDataCurrentness.Check(file);
            Check("M29 subclass definitions do not change the family fingerprint", PanelDerivedDataCurrentness.FamilyFingerprint(panel) == family);
            Check("M29 subclass-only edits require only subclass recomputation", state.FamilyAssignments == PanelDerivedDataStatus.Current
                && state.SubclassLabels == PanelDerivedDataStatus.OutOfDate && state.Message == "Subclass rules changed. Recompute subclass labels.");
            panel.Families[0].Pfam.Add(new() { Accession = "domain-b" });
            Check("M29 family-domain edits invalidate both dependency sets", PanelDerivedDataCurrentness.Check(file).FamilyAssignments == PanelDerivedDataStatus.OutOfDate
                && PanelDerivedDataCurrentness.Check(file).SubclassLabels == PanelDerivedDataStatus.OutOfDate);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];
}
