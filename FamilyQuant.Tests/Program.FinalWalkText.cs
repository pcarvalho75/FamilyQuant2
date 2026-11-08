using FamilyQuant.Panels;
using System;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FinalWalkGroupingTextTests(ClusterGenerationControl screen, TextBlock status, string language)
    {
        var document = screen.fcf;
        var previousItems = document.MyExtendedFastaItems;
        var previousPanel = document.FamilyPanel;
        var previousMode = screen.ViewModel.Quantitation.DisplayedMode;
        var complete = typeof(ClusterGenerationControl).GetMethod("ShowGroupingCompletion", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var refresh = typeof(ClusterGenerationControl).GetMethod("UpdateStatusLine", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            document.MyExtendedFastaItems = Enumerable.Range(1, 7).Select(index => new FastaItemExtended
            { SequenceIdentifier = "walk_fixture_" + index, Sequence = "PEPTIDEK", Description = "Synthetic grouping status",
                UserID = index, USearchID = index, XICUser = index <= 2 ? 0.25 : 0 }).ToList();
            document.FamilyPanel = new FamilyPanel { Name = "Synthetic panel", Families = new()
            { new() { Key = "AA", DisplayName = "First family" }, new() { Key = "BB", DisplayName = "Second family" } } };
            string before = document.ToJsonText();
            screen.ViewModel.Quantitation.DisplayedMode = NormalizationMode.IdentifiedSignal;
            refresh.Invoke(screen, null);
            Check("Z12p5y2 native panel status uses the original group counts " + language,
                status.Text.Contains("2 of 7 groups quantified", StringComparison.Ordinal) && document.ToJsonText() == before, status.Text);
            complete.Invoke(screen, new object[] { "Grouping applied. Undo restores the previous grouping." });
            const string reminder = "Run Quantify again before Export report.";
            Check("Z12p5y2 panel grouping completion names Quantify before Export report once " + language,
                status.Text.StartsWith(reminder, StringComparison.Ordinal) && status.Text.IndexOf(reminder, StringComparison.Ordinal) == status.Text.LastIndexOf(reminder, StringComparison.Ordinal)
                && screen.ShellStatusText == status.Text && document.ToJsonText() == before, status.Text);
            document.FamilyPanel = null;
            complete.Invoke(screen, new object[] { "Grouping applied. Undo restores the previous grouping." });
            Check("Z12p5y2 non-panel grouping keeps its ordinary completion text " + language,
                !status.Text.Contains(reminder, StringComparison.Ordinal) && status.Text.StartsWith("Grouping applied.", StringComparison.Ordinal), status.Text);
        }
        finally
        {
            document.MyExtendedFastaItems = previousItems;
            document.FamilyPanel = previousPanel;
            screen.ViewModel.Quantitation.DisplayedMode = previousMode;
        }
    }
}
