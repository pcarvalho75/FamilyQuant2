using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceOutsideScopeHelpTests(string? output = null)
    {
        Section("Worked outside-scope guide - direct words, qualifying relatives and explicit hand choices");
        string guide = EvidenceOutsideScopeHelp.Build();
        string[] steps = guide.Split('\n').Where(line => Regex.IsMatch(line, @"^[1-8]\. ")).ToArray();
        Check("worked guide has the eight requested actions in order", steps.Length == 8 && steps.Select(s => s[0]).SequenceEqual("12345678")
            && steps[0].Contains("Category", StringComparison.Ordinal) && steps[0].Contains("Molecular function only", StringComparison.Ordinal)
            && steps[1].Contains("Double-click", StringComparison.Ordinal) && steps[1].Contains("Add to scope", StringComparison.Ordinal)
            && steps[2].Contains("preview", StringComparison.Ordinal) && steps[3].Contains("Update proposal", StringComparison.Ordinal)
            && steps[4].Contains("Unassigned", StringComparison.Ordinal) && steps[5].Contains("Add to family", StringComparison.Ordinal)
            && steps[6].Contains("Apply grouping", StringComparison.Ordinal) && steps[7].Contains("Group by keywords", StringComparison.Ordinal));
        Check("preview guidance distinguishes annotation matches, exclusions and later family decisions", guide.Contains("stored keyword and tissue matches in your file", StringComparison.Ordinal)
            && guide.Contains("Contaminants are excluded because a contaminant never joins a family", StringComparison.Ordinal)
            && guide.Contains("word preview alone does not decide its family", StringComparison.Ordinal));
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "Metalloprotease inhibitor" } };
        Check("relative criteria retain both thresholds, scope and domain compatibility", guide.Contains(options.UnannotatedIdentityPercent + "% identity over at least " + options.MinQueryAlignedPercent + "%", StringComparison.Ordinal)
            && guide.Contains("in-scope reviewed relative in the file or reference panel", StringComparison.Ordinal)
            && guide.Contains("overlapping, non-conflicting observed domains", StringComparison.Ordinal));
        Check("hand route states note, tier, update persistence, removal, locks and saving", guide.Contains("Added by hand", StringComparison.Ordinal)
            && guide.Contains("optional note", StringComparison.Ordinal) && guide.Contains("Update proposal keeps the hand choice", StringComparison.Ordinal)
            && guide.Contains("Remove from family restores the automatic proposal", StringComparison.Ordinal)
            && guide.Contains("Locked proteins are refused", StringComparison.Ordinal) && guide.Contains("File > Save", StringComparison.Ordinal)
            && guide.Contains("unticked proteins keep their current families and are still quantified", StringComparison.Ordinal));
        Check("description route explains offered non-matches and respects locks", guide.Contains("description match can offer other members of its current family", StringComparison.Ordinal)
            && guide.Contains("red rows without the words can be unticked", StringComparison.Ordinal)
            && guide.Contains("These routes respect family locks", StringComparison.Ordinal));
        foreach (string page in new[] { EvidenceHelp.Build(), EvidenceHelp.Build(panelMode: true) })
            Check("both evidence modes include the complete worked guide", page.Contains(guide, StringComparison.Ordinal));
        Check("example is explicitly invented and contains no real accession or database-specific authored copy", guide.Contains("Fictitious example", StringComparison.Ordinal)
            && guide.Contains("invented protein labels, not database accessions", StringComparison.Ordinal)
            && !Regex.IsMatch(guide, @"\b[OPQ][0-9][A-Z0-9]{3}[0-9]\b") && !DatabaseSpecificCopy.IsMatch(guide)
            && !guide.Contains('\u2013') && !guide.Contains('\u2014'));

        ProteinEvidence Evidence(bool reviewed, string family, bool words, string domain) => new()
        { Reviewed = reviewed, Family = family, Keywords = words ? new List<string> { "Metalloprotease inhibitor" } : new(),
            Pfam = domain.Length == 0 ? new() : new List<EvidenceDomain> { new() { Accession = domain } } };
        var proteins = new[]
        {
            new FastaItemExtended { SequenceIdentifier = "example_A", Sequence = new string('A', 100), Evidence = Evidence(true, "Invented inhibitor family", true, "PF-invented") },
            new FastaItemExtended { SequenceIdentifier = "example_B", Sequence = new string('A', 100), Evidence = Evidence(false, "", false, "PF-invented") },
            new FastaItemExtended { SequenceIdentifier = "example_C", Sequence = new string('C', 100), Evidence = Evidence(false, "", false, "") },
            new FastaItemExtended { SequenceIdentifier = SequenceLabels.ContaminantPrefix + "example", Sequence = new string('A', 100), Evidence = Evidence(true, "Invented inhibitor family", true, "PF-invented") }
        };
        NearestNeighbour Relative(double identity, int coverage) => new() { Query = "example_B", Reference = "example_A", Accession = "example_A",
            Family = "Invented inhibitor family", IdentityPercent = identity, QueryAlignedResidues = coverage, QueryLength = 100, ReferenceLength = 100, AlignmentLength = coverage };
        EvidenceProposal Propose(double identity, int coverage) => EvidenceClassifier.Propose(proteins, options,
            new Dictionary<string, NearestNeighbour> { ["example_B"] = Relative(identity, coverage) }, _ => proteins[0].Evidence);
        var actual = Propose(70, 70);
        Check("invented A/B/C behavior matches the real classifier at the documented gate", actual.Proteins[0].Family == "Invented inhibitor family"
            && actual.Proteins[1].Tier == EvidenceTier.UnannotatedNeighbour && actual.Proteins[2].Tier == EvidenceTier.Unassigned
            && actual.Proteins[3].Tier == EvidenceTier.Unassigned);
        Check("both identity and covered-length gates matter for the wordless relative", Propose(69, 70).Proteins[1].Tier == EvidenceTier.Unassigned
            && Propose(70, 69).Proteins[1].Tier == EvidenceTier.Unassigned);
        proteins[1].Evidence!.Pfam.Clear();
        Check("relative identity alone cannot replace compatible observed domains", Propose(70, 70).Proteins[1].Tier == EvidenceTier.Unassigned);
        if (output != null)
        {
            EnsureApplication();
            var previous = Themes.ThemeManager.Current;
            try
            {
                foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
                {
                    Themes.ThemeManager.Apply(theme);
                    var window = TextReportWindow.Create("Quantify a family outside the suggested scope", guide, true);
                    window.Width = 1100; window.Height = 768; window.Left = -30000; window.Top = -30000;
                    window.WindowStartupLocation = WindowStartupLocation.Manual; window.WindowStyle = WindowStyle.None;
                    window.ResizeMode = ResizeMode.NoResize; window.ShowActivated = false; window.ShowInTaskbar = false;
                    try
                    {
                        window.Show(); PumpDispatcher();
                        var box = WalkTree(window).OfType<TextBox>().Single();
                        Check(theme + " worked guide renders its full wrapped text", box.Text == guide && box.TextWrapping == TextWrapping.Wrap);
                        string folder = Path.Combine(output, theme.ToString().ToLowerInvariant()); Directory.CreateDirectory(folder);
                        box.ScrollToHome(); PumpDispatcher(); ScreenHarness.Save(window, Path.Combine(folder, "guide-top.png"), 1100, 768);
                        box.ScrollToEnd(); PumpDispatcher(); ScreenHarness.Save(window, Path.Combine(folder, "guide-bottom.png"), 1100, 768);
                    }
                    finally { window.Close(); }
                }
            }
            finally { Themes.ThemeManager.Apply(previous); }
        }
    }
}
