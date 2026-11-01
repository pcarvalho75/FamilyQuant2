using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void EvidenceHelpTests()
        {
            string empty = EvidenceHelp.Build();
            Check("the evidence help says what each step does and what is new compared with words",
                empty.Contains("GROUPING BY EVIDENCE", StringComparison.Ordinal)
                && empty.Contains("WHAT IS NEW COMPARED WITH WORD AND KEYWORD GROUPING", StringComparison.Ordinal)
                && empty.Contains("never by the words in its name", StringComparison.Ordinal));
            Check("without a validation report it quotes no result and says how to get one",
                empty.Contains("Run the validation report to see how the rule does on this file.", StringComparison.Ordinal));
            Check("its thresholds are the engine's own constants",
                empty.Contains(NearestReviewed.DefaultMinIdentityPercent.ToString("0.##") + "% identity or more over "
                    + NearestReviewed.DefaultMinQueryAlignedPercent + "%", StringComparison.Ordinal)
                && empty.Contains("(at least " + CoherenceCheck.DefaultMinimumMembers + ")", StringComparison.Ordinal)
                && empty.Contains(new EvidenceClassificationOptions().MarkerVoteFraction * 100 + "% of the labelled", StringComparison.Ordinal));

            var report = new LeaveOneOutResult
            {
                MinIdentityPercent = 60, Reviewed = 1234, Right = 1111, Wrong = 22, NoNeighbour = 101,
                Confusion = new List<ConfusionRow> { new() { Families = "one family", NeighbourFamilies = "two family", Count = 7 } },
            };
            var routes = new List<RouteScore> { new() { Route = "words", Proteins = 4321, ReviewedMembersTaken = 210, ReviewedMembersMissed = 14, ReviewedOthersTaken = 9 } };
            string full = EvidenceHelp.Build(report, routes, "one family");
            Check("every result on the page is the validation report's own number",
                full.Contains("Leave-one-out on 1,234 reviewed proteins, neighbour at 60% or more:", StringComparison.Ordinal)
                && full.Contains("1,111 right, 22 wrong, 101 without a neighbour.", StringComparison.Ordinal)
                && full.Contains("7 of one family called two family.", StringComparison.Ordinal)
                && full.Contains("words: 4,321 proteins; reviewed members taken 210, missed 14; other reviewed proteins taken 9.", StringComparison.Ordinal));
            Check("the page has no long dash", !full.Contains('—') && !full.Contains('–'));

            Window window = TextReportWindow.Create("FamilyQuant help: grouping by evidence", full);
            try
            {
                window.Measure(new Size(900, 700));
                string? shown = WalkTree(window).OfType<TextBox>().SingleOrDefault()?.Text;
                Check("the evidence help opens in the help window with its whole text", shown == full, shown?.Length.ToString());
            }
            finally { window.Close(); }
        }
    }
}
