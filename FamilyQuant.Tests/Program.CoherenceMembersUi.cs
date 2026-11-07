using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void CoherenceMembersUiTests(string? output = null)
    {
        Section("Reviewed member roster - source, species and immutable stored facts");
        EnsureApplication();
        ProteinEvidence Evidence(string accession, string? species, int? taxon, bool fragment = false) => new()
        { Accession = accession, Reviewed = true, Family = "Invented family", Fragment = fragment, OrganismName = species, TaxonId = taxon, Keywords = new List<string> { "scope" } };
        var file = new FamilyClusterFile();
        for (int i = 0; i < 3; i++) file.MyExtendedFastaItems.Add(new FastaItemExtended
        {
            SequenceIdentifier = "sp|P1000" + i + "|invented_file", Sequence = new string('A', 90) + new string('C', 8 + i),
            Description = "Invented protein OS=Older header species OX=999",
            Evidence = Evidence("P1000" + i, "Invented file species " + (i % 2 == 0 ? "A" : "B"), 100 + i % 2)
        });
        file.EvidenceSource = new EvidenceProvenance { ReferencePanel = new ReferencePanel { Entries = new List<PanelEntry>
        {
            new() { Accession = "P20000", Sequence = new string('A', 90) + new string('C', 10), Evidence = Evidence("P20000", "Invented panel species", 200) },
            new() { Accession = "P20001", Sequence = new string('A', 90) + new string('C', 10), Evidence = Evidence("P20001", null, null) },
            new() { Accession = "P20002", Sequence = "AC", Evidence = Evidence("P20002", "Invented panel fragment", 201, fragment: true) }
        } } };
        file.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = "tr|P99999|invented_query", Sequence = new string('A', 90) + new string('C', 18),
            Evidence = new ProteinEvidence { Reviewed = false, Family = "Invented family", Fragment = false } });
        var profiles = CoherenceCheck.Learn(file);
        var proposal = new EvidenceProposal { SourceFingerprint = EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems),
            Families = new[] { new EvidenceFamilyProposal { Family = "Invented family", SuggestedName = "Invented family", Proteins = 4, ReviewedProteins = 3 } },
            Proteins = file.MyExtendedFastaItems.Select((p, i) => new EvidenceProteinProposal { ProteinIndex = i, Identifier = p.SequenceIdentifier,
                Family = "Invented family", Tier = EvidenceTier.UniProtFamily,
                Flags = i == 3 ? CoherenceCheck.Check(profiles["Invented family"], p, minimumMembers: 1).Select(f => new EvidenceFlag { Code = f.Code, Message = f.Message }).ToArray() : Array.Empty<EvidenceFlag>() }).ToArray() };
        string before = file.ToJsonText();
        var input = EvidenceProposalPresentation.Present(proposal, file, profiles);
        var session = new EvidenceGroupingSession(file, "fixture", (_, _) => new NeighbourSearch());
        var produced = session.Propose("scope");
        Check("production review passes its learned profile roster without another fetch", produced.CoherenceProfiles?["Invented family"].ReviewedMembers.Count == 6
            && file.ToJsonText() == before);
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null), input, null, evidenceMode: true)
                { Width = 1366, Height = 768, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false };
                Window? roster = null;
                try
                {
                    window.Show(); PumpDispatcher();
                    var model = ((GroupingReviewViewModel)window.DataContext).Evidence!;
                    var grid = (DataGrid)window.FindName("evidenceGrid"); grid.SelectedItem = model.Proteins.Last(); grid.ScrollIntoView(grid.SelectedItem); PumpDispatcher();
                    var details = (TextBox)window.FindName("proteinDetails");
                    Check(theme + " details group complete reviewed members by file/panel and species", details.Text.Contains("3 from your file", StringComparison.Ordinal)
                        && details.Text.Contains("Invented file species A 2", StringComparison.Ordinal) && details.Text.Contains("Invented file species B 1", StringComparison.Ordinal)
                        && details.Text.Contains("2 from the reference panel", StringComparison.Ordinal) && details.Text.Contains("Invented panel species 1", StringComparison.Ordinal)
                        && details.Text.Contains("Species not recorded 1", StringComparison.Ordinal) && !details.Text.Contains("Older header species", StringComparison.Ordinal));
                    var flagText = WalkTree(grid).OfType<TextBlock>().First(t => t.Text == model.Proteins.Last().Flags);
                    Check(theme + " flag tooltip repeats the same source/species list", flagText.ToolTip is string tooltip
                        && tooltip.Contains("Invented file species A 2", StringComparison.Ordinal) && tooltip.Contains("Species not recorded 1", StringComparison.Ordinal));
                    var button = window.FindName("profileMembersButton") as Button;
                    Check(theme + " reviewed member button is available for the selected profile", button?.IsEnabled == true && button.IsVisible);
                    button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    roster = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.GetType().Name == "CoherenceMembersWindow");
                    var members = roster?.FindName("membersGrid") as DataGrid;
                    Check(theme + " roster opens on the five complete members with source/species measurements", members?.Items.Count == 5 && members.IsReadOnly
                        && new[] { "Accession", "Species", "Source", "Length", "Cysteines" }.All(h => members.Columns.Any(c => (string)c.Header == h)));
                    if (roster?.FindName("completeOnly") is CheckBox complete)
                    {
                        complete.IsChecked = false; PumpDispatcher();
                        Check(theme + " all-reviewed option includes the stored fragment", members?.Items.Count == 6);
                    }
                    if (output != null && roster != null)
                    {
                        Directory.CreateDirectory(Path.Combine(output, theme.ToString().ToLowerInvariant()));
                        ScreenHarness.Save(roster, Path.Combine(output, theme.ToString().ToLowerInvariant(), "members.png"), 1000, 640);
                        ScreenHarness.Save(window, Path.Combine(output, theme.ToString().ToLowerInvariant(), "details.png"), 1366, 768);
                    }
                    Check(theme + " viewing source members never rewrites the file", file.ToJsonText() == before);
                }
                finally { roster?.Close(); window.Close(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }
}
