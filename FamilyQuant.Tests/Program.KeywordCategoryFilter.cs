using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void KeywordCategoryFilterTests()
    {
        EnsureApplication();
        var file = TaskFile(); string source = file.ToJsonText(); int proposals = 0;
        var facts = new EvidenceTaskScopeInput("Fixture clade", Array.Empty<ReviewTaskTaxon>(), new[]
        {
            new ReviewTaskKeyword("Fixture activity", 2, "Molecular function", Evidence.EvidenceTaskDiscovery.FunctionalScopeCategoryId),
            new ReviewTaskKeyword("Fixture annotation", 9, "Technical term", "KW-9990"),
            new ReviewTaskKeyword("Stored activity", 1, "Molecular function"),
            new ReviewTaskKeyword("Unavailable category", 12),
        }, 12, new[] { "Fixture activity" }, "fixture");
        string before = JsonSerializer.Serialize(facts);
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
        var window = new SuperClusterPreviewWindow(preview, null, _ => { proposals++; return GroupingReviewScreens.Fixture(12); },
            evidenceMode: true, discoverTask: _ => Task.FromResult(facts));
        try
        {
            window.ShowAllOptions = true;
            window.Show(); PumpDispatcher();
            var model = (GroupingReviewViewModel)window.DataContext;
            string scope = model.ScopeText;
            var combo = (ComboBox)window.FindName("KeywordCategoryFilter");
            var grid = (DataGrid)window.FindName("taskKeywordsGrid");
            Check("Z12c2 All categories initially shows every supplied keyword", combo.SelectedIndex == 0
                && ReferenceEquals(grid.ItemsSource, facts.Keywords) && grid.Items.Count == 4);
            combo.SelectedIndex = 1; PumpDispatcher();
            Check("Z12c2 Molecular function only filters known IDs and stored category names",
                grid.Items.OfType<ReviewTaskKeyword>().Select(k => k.Keyword).SequenceEqual(new[] { "Fixture activity", "Stored activity" }));
            Check("Z12c2 filtering preserves source facts, counts, suggestion and editable scope",
                model.ScopeText == scope && JsonSerializer.Serialize(facts) == before && file.ToJsonText() == source && proposals == 0);
            FinishReviewWork(model.DiscoverTaskAsync); PumpDispatcher();
            Check("Z12c2 rediscovery retains the chosen filter and refreshes its rows", combo.SelectedIndex == 1
                && grid.Items.OfType<ReviewTaskKeyword>().Count() == 2);
            combo.SelectedIndex = 0; PumpDispatcher();
            Check("Z12c2 All categories restores the original complete table", ReferenceEquals(grid.ItemsSource, facts.Keywords)
                && grid.Items.Count == 4 && model.ScopeText == scope && proposals == 0);
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
