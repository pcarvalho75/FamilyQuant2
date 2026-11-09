using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void TaskTaxaClipboardTests()
    {
        EnsureApplication();
        var file = TaskFile(); string before = file.ToJsonText(); int proposals = 0;
        var facts = new EvidenceTaskScopeInput("Fixture clade", new[]
        {
            new ReviewTaskTaxon("Fixture Alpha", 11, 1200, 10, 1190),
            new ReviewTaskTaxon("Fixture Beta", 12, 25, 1, 24),
        }, Array.Empty<ReviewTaskKeyword>(), 11, Array.Empty<string>(), "fixture");
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
        var window = new SuperClusterPreviewWindow(preview, null, _ => { proposals++; return GroupingReviewScreens.Fixture(12); },
            evidenceMode: true, discoverTask: _ => Task.FromResult(facts));
        var copied = new List<string>();
        GridCopy.ClipboardOverride = copied.Add;
        try
        {
            window.ShowAllOptions = true;
            window.Show(); PumpDispatcher();
            var grid = (DataGrid)window.FindName("taskTaxaGrid");
            var button = (Button)window.FindName("CopyTaskTaxaButton");
            var command = (RoutedCommand)button.Command;
            command.Execute(button.CommandParameter, button.CommandTarget);
            string header = "Taxon\tTaxonomy ID\tEntries\tFASTA sp\tFASTA tr";
            Check("Z12b2 Copy button includes every row and all column headers",
                copied.Count == 1 && copied[0] == header + Environment.NewLine + "Fixture Alpha\t11\t1200\t10\t1190"
                    + Environment.NewLine + "Fixture Beta\t12\t25\t1\t24");
            grid.SelectedItems.Add(facts.Taxa[1]);
            ApplicationCommands.Copy.Execute(null, grid);
            Check("Z12b2 Ctrl+C command copies selected rows with their columns", copied[^1] == header
                + Environment.NewLine + "Fixture Beta\t12\t25\t1\t24");
            grid.SelectedItems.Clear();
            CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Add(new SortDescription(nameof(ReviewTaskTaxon.Entries), ListSortDirection.Ascending));
            grid.Columns[1].DisplayIndex = 0;
            ApplicationCommands.Copy.Execute(null, grid);
            Check("Z12b2 pasted order follows displayed rows and reordered columns", copied[^1] == "Taxonomy ID\tTaxon\tEntries\tFASTA sp\tFASTA tr"
                + Environment.NewLine + "12\tFixture Beta\t25\t1\t24" + Environment.NewLine + "11\tFixture Alpha\t1200\t10\t1190");
            grid.ItemsSource = new[] { new ReviewTaskTaxon("Quoted \"label\"\twith line\nbreak", 21) };
            ApplicationCommands.Copy.Execute(null, grid);
            Check("Z12b2 spreadsheet cells retain quotes tabs and line breaks with empty unknown counts", copied[^1]
                == "Taxonomy ID\tTaxon\tEntries\tFASTA sp\tFASTA tr" + Environment.NewLine + "21\t\"Quoted \"\"label\"\"\twith line\nbreak\"\t\t\t");
            grid.ItemsSource = Array.Empty<ReviewTaskTaxon>();
            Check("Z12b2 an empty table disables Copy", !ApplicationCommands.Copy.CanExecute(null, grid));
            Check("Z12b2 copying keeps the scope and project intact", proposals == 0 && file.ToJsonText() == before);
        }
        finally { GridCopy.ClipboardOverride = null; window.Close(); PumpDispatcher(); }
    }
}
