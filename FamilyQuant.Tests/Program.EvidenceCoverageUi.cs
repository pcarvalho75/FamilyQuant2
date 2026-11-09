using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceCoverageUiTests(string output)
    {
        Section("Evidence coverage display - recorded query residues, missing legacy facts and rule line");
        EnsureApplication(); Directory.CreateDirectory(output);
        string Format(int? residues) => (string)typeof(EvidenceProposalPresentation)
            .GetMethod("NeighbourText", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                new object[] { new NearestNeighbour { Accession = "provided", IdentityPercent = 80,
                    AlignmentLength = 150, QueryLength = 100, ReferenceLength = 180, QueryAlignedResidues = residues } })!;
        string seventy = Format(70), complete = Format(100), missing = Format(null), invalid = Format(101), zero = Format(0);
        Check("the formatter reports the recorded seventy query residues, not 150 alignment columns as coverage",
            seventy.Contains("70% of query residues", StringComparison.Ordinal) && seventy.Contains("70/100", StringComparison.Ordinal)
            && !seventy.Contains("150%", StringComparison.Ordinal));
        Check("a fully covered query never displays the gapped column ratio above 100", complete.Contains("100% of query residues", StringComparison.Ordinal)
            && !complete.Contains("150%", StringComparison.Ordinal));
        Check("legacy and invalid residue facts display not recorded, never an invented zero", missing.Contains("not recorded", StringComparison.Ordinal)
            && invalid.Contains("not recorded", StringComparison.Ordinal) && !missing.Contains("0% of query residues", StringComparison.Ordinal));
        Check("a recorded zero remains distinguishable from missing coverage", zero.Contains("0% of query residues", StringComparison.Ordinal)
            && !zero.Contains("not recorded", StringComparison.Ordinal));
        const string rule = "At least 70 percent of the query's residues occur in the alignment; inserted gap columns do not count.";
        Check("Help states the query-residue rule and excludes inserted gaps", GroupingReviewHelp.Text.Contains(rule, StringComparison.Ordinal));
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null,
                    "", "", KeywordMatch.All, null, null), GroupingReviewScreens.Fixture(12), null)
                    { Width = 1180, Height = 740, Left = -30000, Top = -30000,
                        WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.ShowAllOptions = true;
                    window.Show(); PumpDispatcher();
                    var text = window.FindName("TextBlockQueryCoverageRule") as TextBlock;
                    Check(theme + " evidence window shows plain coverage words with the exact definition in its tooltip",
                        text?.Text == EvidenceProposalPresentation.QueryCoverageSummary && text.ToolTip as string == rule && text.IsVisible);
                    System.Threading.Thread.Sleep(350); PumpDispatcher();
                    ScreenHarness.Save(window, Path.Combine(output, "coverage-" + theme + ".png"), 1180, 740);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
