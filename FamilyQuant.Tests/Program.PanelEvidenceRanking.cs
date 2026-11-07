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
    private static IEnumerable<TestCommand> PanelEvidenceRankingCommandTable() =>
    [
        new("--test-panel-evidence-ranking", 74, TestCommandPhase.Raw, args => args.Contains("--test-panel-evidence-ranking", StringComparer.Ordinal), (_, _) =>
        {
            PanelEvidenceRankingTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];
    private static FamilyClusterFile RankedEvidenceFile()
    {
        var file = new FamilyClusterFile { FamilyPanel = new() { Name = "Invented ranked list" } };
        foreach (var (word, qualifier) in new[] { ("Invented", "B2"), ("Alternate", "D2") })
        {
            file.FamilyPanel.Families.Add(new() { Key = word, DisplayName = word + " modifier " + qualifier,
                Names = new() { new() { Name = "Other source header", Text = word + " modifier " + qualifier } } });
            for (int group = 0; group <= 30; group++)
            for (int copy = 0; copy < (group == 30 ? 1 : 6); copy++)
            {
                int index = file.MyExtendedFastaItems.Count;
                string name = word + " modifier " + (group == 30 ? qualifier : "A" + group);
                file.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "tr|P" + (20000 + index) + "|invented", Description = name, Sequence = "ACDEFGHIK",
                    UserID = index, USearchID = index, Evidence = new() { Family = word + (group == 30 ? " family target" : " unrelated family " + group), Reviewed = false,
                        Keywords = new() { "Shared catalogue tag", "Shared boundary tag", word + " narrow phrase" }, TissueSpecificity = new() { "A long shared tissue sentence." },
                        Pfam = new() { new() { Accession = word + (group == 30 ? "-target-domain" : "-other-domain-" + group), Name = name + " fold" } } } });
            }
        }
        return file;
    }
    private static void PanelEvidenceRankingTests()
    {
        var file = RankedEvidenceFile(); string before = file.ToJsonText(); var matching = new PanelEvidenceMatching(file);
        foreach (var family in file.FamilyPanel!.Families)
        {
            var rows = matching.Suggest(family.Names.Select(name => name.Text).Prepend(family.DisplayName));
            var sourceFamilies = rows.Where(row => row.Kind == PanelEvidenceMatchKind.UniProtFamily).ToArray();
            var domains = rows.Where(row => row.Kind == PanelEvidenceMatchKind.Pfam).ToArray();
            Check("Z12p5a " + family.Key + " name coverage outranks high-volume families sharing one name word",
                sourceFamilies[0].Value == family.Key + " family target");
            Check("Z12p5a " + family.Key + " short imported qualifiers distinguish the correct Pfam name",
                domains[0].Value == family.Key + "-target-domain");
            Check("Z12p5a " + family.Key + " primary family and domain rows precede keywords and tissue",
                rows.TakeWhile(row => row.IsFamilyLevelMatch).Count() == rows.Count(row => row.IsFamilyLevelMatch));
        }
        Check("Z12p5a ranking changes no stored file or confirmed choice", file.ToJsonText() == before && file.FamilyPanel.ConfirmedEvidenceMatches == null);
        foreach (var family in file.FamilyPanel.Families) family.Names.Add(new() { Name = "Short supplied name", Text = "modifier" });
        Check("Z12p5a a short generic alternate name cannot erase the better full-name match",
            file.FamilyPanel.Families.All(family => matching.Suggest(family.Names.Select(name => name.Text).Prepend(family.DisplayName))
                .First(row => row.Kind == PanelEvidenceMatchKind.UniProtFamily).Value == family.Key + " family target"));
        foreach (var family in file.FamilyPanel.Families) family.Names.Add(new() { Name = "Another supplied header", Text = "Shared catalogue tag" });
        var batched = matching.SuggestForPanel(file.FamilyPanel);
        Check("Z12p5a common keywords and tissue are additional even when a supplied name matches the keyword exactly",
            batched.Values.All(rows => rows.Single(row => row.Value == "Shared catalogue tag").IsAdditional
                && rows.Where(row => row.Kind == PanelEvidenceMatchKind.TissuePhrase).All(row => row.IsAdditional)));
        Check("Z12p5a a family-specific keyword stays available outside Show more",
            batched.All(pair => !pair.Value.Single(row => row.Value == pair.Key + " narrow phrase").IsAdditional));
        var reversed = FamilyClusterFile.FromJsonText(file.ToJsonText()); reversed.MyExtendedFastaItems.Reverse();
        Check("Z12p5a ranking and additional labels are independent of database entry order",
            JsonSerializer.Serialize(batched) == JsonSerializer.Serialize(new PanelEvidenceMatching(reversed).SuggestForPanel(reversed.FamilyPanel!)));
    }

    private static void PanelEvidenceRankingUiTests(string output)
    {
        EnsureApplication(); var oldTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = RankedEvidenceFile(); var panel = file.FamilyPanel!;
                foreach (var family in panel.Families) family.Names.Add(new() { Name = "Another supplied header", Text = "Shared catalogue tag" });
                var source = new PanelEvidenceMatching(file);
                var model = new PanelEvidenceMatchViewModel(new(source, panel.Families[0], null, () => source.IsCurrent(file), panel));
                var window = new PanelEvidenceMatchWindow(model) { ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); PumpDispatcher(); var grid = (DataGrid)window.FindName("MatchLabelsGrid");
                    Check("Z12p5a " + theme + " native default grid places the exact name first without ticking it",
                        grid.Items[0] is PanelEvidenceMatchRow { Value: "Invented family target", Selected: false }
                        && model.Rows.All(row => !row.Selected) && model.VisibleRows.All(row => !row.IsAdditional));
                    ScreenHarness.Save(window, Path.Combine(output, "ranking-primary-" + theme + ".png"), 1180, 680);
                    ((CheckBox)window.FindName("ShowMoreMatchLabels")).IsChecked = true; PumpDispatcher();
                    Check("Z12p5a " + theme + " Show more reveals common keywords and tissue in the native grid",
                        model.ShowMore && grid.Items.Cast<PanelEvidenceMatchRow>().Any(row => row.Value == "Shared catalogue tag")
                        && grid.Items.Cast<PanelEvidenceMatchRow>().Any(row => row.Kind == PanelEvidenceMatchKind.TissuePhrase));
                    ScreenHarness.Save(window, Path.Combine(output, "ranking-more-" + theme + ".png"), 1180, 680);
                    var selected = model.Rows.Single(row => row.Value == "Shared catalogue tag"); selected.Selected = true; model.ShowMore = false; PumpDispatcher();
                    Check("Z12p5a " + theme + " selected additional labels stay visible and return only by explicit confirmation",
                        grid.Items.Contains(selected) && model.TryConfirm(out var record, _ => true) && record!.Choices.Single().Value == selected.Value
                        && panel.ConfirmedEvidenceMatches == null);
                    Check("Z12p5a " + theme + " normal confirmation hint explains the draft and Undo", model.ConfirmHint.Contains("draft", StringComparison.Ordinal) && model.ConfirmHint.Contains("Undo", StringComparison.Ordinal));
                    var blank = model.AddLabel();
                    Check("Z12p5a " + theme + " blank selected labels disable confirmation and explain the repair", !model.CanConfirm && model.ConfirmHint.StartsWith("Fill in or untick", StringComparison.Ordinal));
                    blank.Selected = false; file.MyExtendedFastaItems[0].Description += " changed"; model.TryConfirm(out _);
                    Check("Z12p5a " + theme + " changed evidence has a close and reopen hint", !model.CanConfirm && model.ConfirmHint.Contains("reopen", StringComparison.Ordinal));
                }
                finally { window.Close(); PumpDispatcher(); }
                var noMatchFile = EvidenceMatchingFile(); noMatchFile.FamilyPanel!.Families[0].DisplayName = "Action alpha"; noMatchFile.FamilyPanel.Families[0].Names.Clear();
                var noMatch = new PanelEvidenceMatchViewModel(new(new(noMatchFile), noMatchFile.FamilyPanel.Families[0], null, () => true, noMatchFile.FamilyPanel));
                Check("Z12p5a " + theme + " keyword-only fit plainly reports no family-level match", noMatch.FamilyMatchStatus == "No family-level match"
                    && noMatch.VisibleRows.All(row => row.Kind == PanelEvidenceMatchKind.Keyword) && noMatch.Rows.All(row => !row.Selected));
                var noMatchWindow = new PanelEvidenceMatchWindow(noMatch) { ShowActivated = false, ShowInTaskbar = false };
                try { noMatchWindow.Show(); PumpDispatcher(); ScreenHarness.Save(noMatchWindow, Path.Combine(output, "ranking-no-family-" + theme + ".png"), 1180, 680); }
                finally { noMatchWindow.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
    }
}
