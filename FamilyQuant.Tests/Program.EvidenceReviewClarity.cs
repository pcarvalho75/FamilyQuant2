using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceReviewClarityTests(string? output = null)
    {
        Section("Evidence review - warnings and choices in plain words");
        EnsureApplication();
        string[] codes = ["conflicting_markers", "domain_compatibility", "multiple_source_families",
            "neighbour_out_of_scope", "neighbour_refused", "panel_domain_conflict", "panel_link_unsupported",
            "panel_link_ambiguous", "panel_link_conflict", "coherence.length.above", "coherence.length.below",
            "coherence.cysteines.above", "coherence.cysteines.below", "coherence.transmembrane", "coherence.domain"];
        var input = GroupingReviewScreens.Fixture(12);
        foreach (string code in codes)
        {
            var source = input.Proteins[1] with { Flags = new[] { code + ": Supplied evidence detail." } };
            var row = new EvidenceReviewViewModel(input with { Proteins = input.Proteins.Select(p => p.ProteinIndex == 1 ? source : p).ToArray() }).Proteins[1];
            string help = typeof(ReviewProteinRow).GetProperty("FlagsHelp")?.GetValue(row) as string ?? "";
            Check(code + " has a readable label and a check to make", !row.Flags.Contains(code, StringComparison.Ordinal)
                && !row.Flags.Contains('_') && help.Contains("Check ", StringComparison.Ordinal)
                && help.Contains("Supplied evidence detail.", StringComparison.Ordinal));
        }
        var defaults = new EvidenceReviewViewModel(input);
        var family = defaults.Families.Single(f => f.Members.Any(p => p.Source.IsLocked));
        Check("locked and flagged proteins start unticked", defaults.Proteins.Where(p => p.Source.IsLocked || p.IsFlagged).All(p => !p.Selected));
        family.Selected = true;
        Check("full family tick counts eligible proteins and keeps locked proteins off", family.Selected == true
            && family.Members.Where(p => p.CanSelect).All(p => p.Selected) && family.Members.Where(p => !p.CanSelect).All(p => !p.Selected));
        family.Members.First(p => p.CanSelect).Selected = false;
        Check("some eligible proteins give a dash", family.Selected == null);
        family.Selected = false;
        Check("no eligible proteins selected gives an empty tick", family.Selected == false);
        var preview = new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null);
        var window = new SuperClusterPreviewWindow(preview, input, null, evidenceMode: true);
        try
        {
            var legend = window.FindName("proteinWarningLegend") as TextBlock;
            Check("protein table explains red italic warning rows", legend?.Text == "Red italic rows carry a warning flag; hover over Flags for what to check.");
            var ticks = window.FindName("familyTickLegend") as TextBlock;
            Check("family legend states tick meanings and initial exclusions", ticks?.Text.Contains("Full tick: all eligible", StringComparison.Ordinal) == true
                && ticks.Text.Contains("dash: some; empty: none", StringComparison.Ordinal)
                && ticks.Text.Contains("Flagged start unticked; locked stay unticked", StringComparison.Ordinal));
        }
        finally { window.Close(); }
        EvidenceReviewFamilyLayoutTests(output);
    }
}
