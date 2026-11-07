using FamilyQuant.Evidence;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ScopeCountWordingTests()
    {
        EnsureApplication();
        var file = TaskFile();
        file.MyExtendedFastaItems.Add(TaskProtein("Reverse_contaminant_sp|P00999|IGNORED", 99, false));
        foreach (var protein in file.MyExtendedFastaItems) protein.Evidence!.Reviewed = false;
        using var handler = new TaskTaxonomyHandler();
        var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, handler).GetAwaiter().GetResult();
        Check("Z12c6 entry definitions follow target/decoy and contaminant counting", proposal.TargetProteins == 3
            && proposal.IgnoredContaminants == 1 && proposal.ReviewedProteins == 0);
        var session = new EvidenceGroupingSession(file, "fixture", discoverTask: (_, _) => Task.FromResult(proposal));
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
        var window = new SuperClusterPreviewWindow(preview, null, session.Propose, evidenceMode: true, discoverTask: session.DiscoverTaskAsync);
        try
        {
            window.Show(); PumpDispatcher();
            var model = (GroupingReviewViewModel)window.DataContext;
            Check("Z12c6 counted and excluded labels use plain words with definitions",
                ((TextBlock)window.FindName("DatabaseEntriesCount")).Text == "Database entries counted: 3"
                && ((TextBlock)window.FindName("ExcludedContaminantsCount")).Text == "Contaminants excluded: 1"
                && ((TextBlock)window.FindName("DatabaseEntriesCount")).ToolTip as string == EvidenceHelp.DatabaseEntryCountDefinition);
            Check("Z12c6 overlapping keyword entry counts and abundance are explained on screen",
                WalkTree(window).OfType<TextBlock>().Any(block => block.Text == EvidenceHelp.KeywordCountExplanation));
            Check("Z12c6 zero reviewed proteins has an accurate suggested-scope status despite stored evidence",
                file.MyExtendedFastaItems.Any(protein => protein.Evidence != null) && model.ScopeText.Length == 0
                && model.Status == "No reviewed proteins were found for a suggested scope. Fetch evidence or enter a scope.");
        }
        finally { window.Close(); PumpDispatcher(); }
        Check("Z12c6 both Help routes define entry counts and overlapping keywords", new[] { false, true }.All(panel =>
            EvidenceHelp.Build(panelMode: panel).Contains(EvidenceHelp.CountDefinitionsHelp, StringComparison.Ordinal)));
    }
}
