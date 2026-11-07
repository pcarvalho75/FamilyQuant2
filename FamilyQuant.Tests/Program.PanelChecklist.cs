using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelChecklistCommandTable() =>
    [
        new("--test-panel-checklist", 74, TestCommandPhase.Raw, args => args.Contains("--test-panel-checklist", StringComparer.Ordinal), (args, _) =>
        {
            PanelChecklistTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelChecklist"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static FamilyClusterFile ChecklistFile()
    {
        var file = PanelEvidenceFile();
        file.FamilyPanel!.Families.Add(new() { Key = "missing", DisplayName = "Source group missing",
            Names = new() { new() { Name = "English", Text = "Additional supplied activity" } },
            Pfam = new() { new() { Accession = "domain-z" } } });
        file.FamilyPanel.Families.Add(new() { Key = "empty", DisplayName = "No supplied match" });
        file.FamilyPanel.Families.Add(new() { Key = "excluded", DisplayName = "Excluded supplied row", Decision = FamilyPanelDecision.NotAFamily });
        file.MyExtendedFastaItems[3].Description = "Additional supplied activity OS=Fixture label";
        file.MyExtendedFastaItems[3].Evidence!.Family = "Source group missing";
        file.MyExtendedFastaItems[3].Evidence!.Keywords = new() { "alternate activity" };
        file.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "tr|P10006|unknown_status", Sequence = "ACDEFGHIK", UserID = 2, USearchID = 2,
            Description = "Additional supplied activity", Evidence = new() { Accession = "P10006", Keywords = new() { "alternate activity" } } });
        foreach (string identifier in new[] { "Reverse_tr|P10007|decoy", "contaminant_sp|P10008|excluded" })
            file.MyExtendedFastaItems.Add(new() { SequenceIdentifier = identifier, Sequence = "ACDEFGHIK", Description = "Additional supplied activity",
                Evidence = new() { Family = "Source group missing", Reviewed = true } });
        file.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "tr|P10009|metadata_only", Sequence = "ACDEFGHIK", Description = "Unrelated OS=Additional supplied activity" });
        file.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "tr|P10010|word_boundary", Sequence = "ACDEFGHIK", Description = "Additional supplied activityase" });
        return file;
    }

    private static void PanelChecklistTests(string output)
    {
        var file = ChecklistFile(); string before = file.ToJsonText();
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
        var proposal = PanelEvidenceClassifier.Propose(file, options);
        string proposed = JsonSerializer.Serialize(proposal);
        var rows = PanelFamilyChecklist.Build(file, proposal);
        var missing = rows.Single(row => row.Key == "missing");
        Check("Z12n1 checklist retains every panel family in supplied order", rows.Select(row => row.Key).SequenceEqual(file.FamilyPanel!.Families.Select(family => family.Key)));
        Check("Z12n1 overlapping name, source-family and Pfam matches count each target once",
            missing is { Matches: 2, Reviewed: 0, Placed: 0, Outside: 2 }
            && missing.Proteins.Single(protein => protein.ProteinIndex == 3).Match == (PanelChecklistMatch.UniProtFamily | PanelChecklistMatch.Pfam | PanelChecklistMatch.NameText));
        Check("Z12n1 unknown status remains unknown and decoys, contaminants, metadata and partial words are excluded",
            missing.Proteins.Select(protein => protein.ProteinIndex).SequenceEqual(new[] { 3, 5 }) && missing.Proteins[1].Reviewed == null);
        string contaminantAccession = FamilyNaming.Accession(file.MyExtendedFastaItems[7].SequenceIdentifier);
        int auditContaminants = PanelAuditCsv.BuildCsv(file).Split('\n').Count(line => line.Split(',')[0].Trim('"') == contaminantAccession);
        Check("Z12n1-fix matching contaminants agree with separate audit rows and remain outside every target count",
            auditContaminants == 1 && missing.ExcludedContaminants == auditContaminants && rows.Where(row => row.Key != "missing").All(row => row.ExcludedContaminants == 0)
            && missing is { Matches: 2, Reviewed: 0, Placed: 0, Outside: 2 }
            && missing.OutsideSummary.Contains("A contaminant never joins a family.", StringComparison.Ordinal));
        Check("Z12n1 counts placements from the actual proposal instead of rescuing domain/name matches",
            rows.All(row => row.Placed == row.Proteins.Count(protein => proposal.Proteins.Single(p => p.ProteinIndex == protein.ProteinIndex).Family == row.TargetKey))
            && proposal.Proteins.Single(protein => protein.ProteinIndex == 3).Family.Length == 0);
        string reason = proposal.Proteins.Single(protein => protein.ProteinIndex == 3).Reason;
        Check("Z12n1 outside reasons share the panel audit scope explanation and preserve its actual reviewed-relative reason",
            missing.Proteins[0].Reason == PanelAuditCsv.ExplainScopeExclusion(file.MyExtendedFastaItems[3].Evidence, options.ScopeTerms, null, reason)
            && missing.Proteins[0].Reason.Contains("selected", StringComparison.Ordinal) && missing.Proteins[0].Reason.EndsWith(reason, StringComparison.Ordinal));
        Check("Z12n1 unmatched and excluded panel rows stay visible", rows.Single(row => row.Key == "empty").Matches == 0
            && rows.Single(row => row.Key == "excluded").Decision == "Excluded by panel");
        Check("Z12n1 reading checklist leaves proposal and file byte exact", file.ToJsonText() == before && JsonSerializer.Serialize(proposal) == proposed);
        var neighbourFile = PanelEvidenceFile();
        neighbourFile.MyExtendedFastaItems = new() { neighbourFile.MyExtendedFastaItems[0], new()
        {
            SequenceIdentifier = "tr|P10011|relative_only", Sequence = neighbourFile.MyExtendedFastaItems[0].Sequence,
            Description = "Unrelated supplied description", Evidence = new() { Keywords = new() { "selected" }, Reviewed = false },
        } };
        var neighbourSession = new EvidenceGroupingSession(neighbourFile, "relative fixture");
        var neighbourInput = neighbourSession.Propose("selected");
        var neighbourRow = neighbourInput.PanelChecklist!.Single(row => row.Key == "broad");
        Check("Z12n1 native relative placements stay counted when own names and domains do not match",
            neighbourRow.Placed == 2 && neighbourRow.Proteins.Single(protein => protein.ProteinIndex == 1).Match == PanelChecklistMatch.ProposedPlacement);
        EnsureApplication(); Directory.CreateDirectory(output);
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (1366, 768), (1920, 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var session = new EvidenceGroupingSession(file, "checklist fixture");
                var input = session.Propose("selected");
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
                var window = new SuperClusterPreviewWindow(preview, input, session.Propose, initialScope: "selected", evidenceMode: true)
                    { Width = size.Item1, Height = size.Item2, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.Show(); PumpDispatcher();
                    var tab = (TabItem)window.FindName("PanelChecklistTab"); tab.IsSelected = true;
                    var grid = (DataGrid)window.FindName("PanelChecklistGrid");
                    var model = (GroupingReviewViewModel)window.DataContext;
                    grid.SelectedItem = model.Evidence!.PanelChecklist.Single(row => row.Key == "missing");
                    grid.ScrollIntoView(grid.SelectedItem);
                    window.UpdateLayout(); PumpDispatcher();
                    Check("Z12n1 " + theme + " " + size.Item1 + " native checklist is read only with all rows and explanatory detail",
                        tab.IsVisible && grid.IsReadOnly && grid.Items.Count == rows.Count && grid.ActualHeight > 100
                        && ((TextBox)window.FindName("PanelChecklistDetail")).IsReadOnly
                        && model.Evidence.ChecklistDetail.Contains(session.Proposal!.Proteins.Single(protein => protein.ProteinIndex == 3).Reason, StringComparison.Ordinal)
                        && model.Evidence.ChecklistDetail.Contains("1 matching contaminant is excluded. A contaminant never joins a family.", StringComparison.Ordinal));
                    ScreenHarness.Save(window, Path.Combine(output, "checklist-" + theme + "-" + size.Item1 + ".png"), size.Item1, size.Item2);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
        Check("Z12n1 review screens change no source file", file.ToJsonText() == before);
    }
}
