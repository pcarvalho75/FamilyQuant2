using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceNewcomerTests()
    {
        Section("Group by evidence - newcomer wording and next actions");
        EnsureApplication();
        var window = ReviewLayoutWindow();
        try
        {
            window.Show(); PumpDispatcher(); window.UpdateLayout();
            var coverage = (TextBlock)window.FindName("TextBlockQueryCoverageRule");
            Check("coverage uses protein length words and preserves the precise definition", coverage.Text == EvidenceProposalPresentation.QueryCoverageSummary
                && !coverage.Text.Contains("query", StringComparison.OrdinalIgnoreCase)
                && coverage.ToolTip as string == EvidenceProposalPresentation.QueryCoverageRule);
        }
        finally { window.Close(); }
    }
}
