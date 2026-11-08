using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyListRankedMatchCommands() =>
    [new("--test-family-list-ranked-matches", 62, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-family-list-ranked-matches", (args, _) =>
        {
            EnsureApplication();
            FamilyListImportRankedMatchTests(args.Length == 2 ? args[1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void FamilyListImportMatchTests(string? output = null)
    {
        var project = ManualChoiceFile();
        project.MyExtendedFastaItems[0].Description = "Invented alpha enzyme";
        project.MyExtendedFastaItems[0].Evidence!.Family = "Supplied alpha enzyme family";
        project.MyExtendedFastaItems[0].Evidence!.Reviewed = true;
        project.MyExtendedFastaItems[0].Evidence!.Keywords = new() { "Invented alpha function" };
        string before = project.ToJsonText();
        var panel = new FamilyPanel { Name = "Supplied list", Families = new() { new() { Key = "alpha", DisplayName = "Invented alpha enzyme",
            Names = new() { new() { Name = "Other-language name", Text = "Invented alpha" } } } } };
        string panelBefore = JsonSerializer.Serialize(panel);
        var adapter = new FamilyListStoredEvidenceMatches(project);
        var rows = adapter.Suggest(project, panel);
        var shared = new PanelEvidenceMatching(project).SuggestForPanel(panel)["alpha"];
        Check("wizard matching is the shared Core projection and starts unchecked", rows.Single().Suggestions.Count > 0
            && rows.Single().Suggestions.Select(s => s.Key).SequenceEqual(shared.Select(s => s.Key))
            && rows.Single().Suggestions.All(s => !s.Use) && project.ToJsonText() == before && JsonSerializer.Serialize(panel) == panelBefore);
        var selected = rows.Single().Suggestions[0]; selected.Use = true;
        selected.Text = "An explicitly edited value absent from stored evidence";
        Check("editing a suggested label refreshes exact coverage instead of retaining its old counts", selected.Proteins == 0 && selected.Reviewed == 0
            && selected.Details.Contains("No exact stored label", StringComparison.Ordinal));
        rows.Single().Choice = "Confirmed";
        var accepted = adapter.Accept(panel, rows);
        var confirmed = accepted.ConfirmedEvidenceMatches?.GetValueOrDefault("alpha");
        Check("only explicitly selected edited values are stored under the stable panel key", accepted.ConfirmedEvidenceMatches?.Count == 1
            && confirmed is { UserConfirmed: true }
            && confirmed.Choices.Single().Value == selected.Text
            && confirmed.NamesUsed.Contains("Invented alpha")
            && project.ToJsonText() == before && JsonSerializer.Serialize(panel) == panelBefore);
        rows.Single().Choice = "Skipped";
        Check("skipping a family adds no confirmed match records", adapter.Accept(panel, rows).ConfirmedEvidenceMatches == null);
        project.MyExtendedFastaItems[0].Evidence!.Keywords.Add("A later annotation");
        string after = project.ToJsonText(); bool refused = false;
        try { adapter.Accept(panel, rows); } catch (InvalidOperationException error) { refused = error.Message.Contains("evidence changed", StringComparison.Ordinal); }
        Check("the wizard adapter refuses a changed stored-evidence snapshot without edits", refused && project.ToJsonText() == after);
        FamilyListImportRankedMatchTests(output);
        FamilyListImportBroadMatchTests();
    }

    private static void FamilyListImportRankedMatchTests(string? output)
    {
        var file = RankedEvidenceFile();
        var panel = FamilyPanelValidation.Clone(file.FamilyPanel!);
        foreach (var family in panel.Families)
            family.Names.Add(new() { Name = "Other-language name", Text = "Shared catalogue tag" });
        panel.Families.Add(new() { Key = "keyword-only", DisplayName = "Shared catalogue tag" });
        string before = file.ToJsonText(), panelBefore = JsonSerializer.Serialize(panel);
        var adapter = new FamilyListStoredEvidenceMatches(file);
        var rows = adapter.Suggest(file, panel);
        var expected = new PanelEvidenceMatching(file).SuggestForPanel(panel);
        Check("Z12p5a1 wizard preserves the panel-wide ranking and every Core additional flag", rows.All(family =>
            family.Suggestions.Select(s => (s.Key, s.NameCoverage, s.IsAdditional, s.IsFamilyLevelMatch))
                .SequenceEqual(expected[family.Key].Select(s => (s.Key, s.NameCoverage, s.IsAdditional, s.IsFamilyLevelMatch)))));
        Check("Z12p5a1 the right family is in the first two primary suggestions despite many shared-word entries",
            rows.Where(family => family.Key != "keyword-only").All(family => family.PrimarySuggestions.Take(2)
                .Any(s => s.Text == family.Key + " family target")));
        Check("Z12p5a1 shared keywords and tissue stay under Show more and new suggestions stay unticked",
            rows.All(family => family.AdditionalSuggestions.Any(s => s.Text == "Shared catalogue tag")
                && family.AdditionalSuggestions.Where(s => s.Type == "Tissue phrase").Any()
                && family.Suggestions.All(s => !s.Use) && !family.ShowAdditional));
        Check("Z12p5a1 keyword-only evidence has no family-level match and suggestion groups lose no rows",
            rows.Single(family => family.Key == "keyword-only").HasNoFamilyLevelMatch
            && rows.All(family => family.PrimarySuggestions.Count + family.AdditionalSuggestions.Count == family.Suggestions.Count)
            && file.ToJsonText() == before && JsonSerializer.Serialize(panel) == panelBefore);

        var names = panel.Families[0].Names.Select(name => name.Text).Prepend(panel.Families[0].DisplayName);
        var previous = FamilyClusterFile.FromJsonText(file.ToJsonText());
        previous.FamilyPanel = PanelEvidenceMatching.WithConfirmed(panel, panel.Families[0].Key, PanelEvidenceMatching.Confirm(names,
            new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Keyword, "Shared catalogue tag"),
                new PanelEvidenceChoice(PanelEvidenceMatchKind.UniProtFamily, panel.Families[0].Key + " family target") }));
        var carried = new FamilyListStoredEvidenceMatches(previous).Suggest(previous, panel).First();
        Check("Z12p5a1 prior confirmations keep ranking flags and expose selected additional labels",
            carried.PreviouslyConfirmed && carried.ShowAdditional && carried.Suggestions.Count(s => s.Use) == 2
            && carried.AdditionalSuggestions.Single(s => s.Text == "Shared catalogue tag").Use
            && carried.PrimarySuggestions.Single(s => s.Text == panel.Families[0].Key + " family target").IsFamilyLevelMatch);

        string work = Path.Combine(Path.GetTempPath(), "fq-wizard-ranking-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string workbook = Path.Combine(work, "synthetic-ranking.xlsx");
        PanelImportTemplate.Write(workbook);
        FillTemplate(workbook, panel.Families.Select(family => new[]
            { family.Key, family.DisplayName, "Shared catalogue tag", "", "", "Quantify" }).ToArray());
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (Width: 1366, Height: 768), (Width: 1920, Height: 1080) })
            {
                Themes.ThemeManager.Apply(theme);
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)));
                var window = new FamilyListImportWindow(model)
                {
                    Width = size.Width, Height = size.Height, Left = -30000, Top = -30000,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
                };
                string label = "Z12p5a1 " + theme + " " + size.Width;
                try
                {
                    window.Show();
                    FinishReviewWork(() => model.LoadAsync(workbook));
                    FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); await model.NextAsync(); });
                    Check(label + " real template import reaches the matching page with all choices pending",
                        model.Page == 4 && model.MatchFamilies.Count == 3 && model.MatchFamilies.All(f => !f.Decided));
                    var target = model.MatchFamilies.Single(f => f.Key == panel.Families[0].Key);
                    model.SelectedMatchFamily = target;
                    PumpDispatcher();
                    var primary = (DataGrid)window.FindName("MatchSuggestionsGrid");
                    var additional = (DataGrid)window.FindName("AdditionalMatchSuggestionsGrid");
                    var more = (Expander)window.FindName("AdditionalMatchesExpander");
                    var noFamily = (TextBlock)window.FindName("NoFamilyLevelMatchText");
                    Check(label + " the primary table shows the ranked family before extras with no automatic ticks",
                        !more.IsExpanded && noFamily.Visibility == Visibility.Collapsed
                        && primary.Items.Cast<ImportEvidenceMatch>().All(s => !s.IsAdditional)
                        && primary.Items.Cast<ImportEvidenceMatch>().Take(2).Any(s => s.Text == target.Key + " family target")
                        && model.MatchFamilies.All(f => f.Suggestions.All(s => !s.Use)));
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "05-matches-ranked");
                    more.IsExpanded = true;
                    FinishReviewWork(() => Task.Delay(250)); PumpDispatcher();
                    Check(label + " Show more opens a bounded extra list with informative control and header tooltips",
                        target.ShowAdditional && additional.IsVisible && additional.ActualHeight <= 190
                        && additional.Items.Cast<ImportEvidenceMatch>().All(s => s.IsAdditional)
                        && more.ToolTip is string { Length: > 20 } && additional.ToolTip is string { Length: > 20 }
                        && additional.Columns.All(c => !string.IsNullOrWhiteSpace(Themes.TableAppearance.GetHeaderToolTip(c))));
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "05-matches-more");
                    model.SelectedMatchFamily = model.MatchFamilies.Single(f => f.Key == "keyword-only");
                    FinishReviewWork(() => Task.Delay(1)); PumpDispatcher();
                    Check(label + " a keyword-only family says No family-level match and has its own collapsed extras",
                        noFamily.IsVisible && noFamily.Text == "No family-level match" && !more.IsExpanded && !additional.IsVisible,
                        $"notice visible={noFamily.IsVisible}, visibility={noFamily.Visibility}, text={noFamily.Text}; "
                            + $"family no-match={model.SelectedMatchFamily?.HasNoFamilyLevelMatch}, expanded={more.IsExpanded}, "
                            + $"family expanded={model.SelectedMatchFamily?.ShowAdditional}, extras visible={additional.IsVisible}, "
                            + $"extras visibility={additional.Visibility}, extras height={additional.ActualHeight}, page={model.Page}");
                    CaptureFamilyListPage(window, output, theme, size.Width, size.Height, "05-matches-no-family");
                    model.SelectedMatchFamily = target; PumpDispatcher();
                    Check(label + " returning to a family restores its expansion without confirming or ticking anything",
                        more.IsExpanded && !model.CanNext && target.Suggestions.All(s => !s.Use));
                    var extra = target.AdditionalSuggestions.Single(s => s.Text == "Shared catalogue tag");
                    extra.Use = true;
                    additional.ScrollIntoView(extra); additional.CurrentCell = new DataGridCellInfo(extra, additional.Columns[1]);
                    bool editing = additional.BeginEdit(); PumpDispatcher();
                    var editor = WalkTree(additional).OfType<TextBox>().FirstOrDefault(box => ReferenceEquals(box.DataContext, extra));
                    Check(label + " an additional label can be edited in the real list", editing && editor != null);
                    if (editor != null) editor.Text = "Explicit edited extra label";
                    WalkTree(window).OfType<Button>().Single(b => Equals(b.Content, "Use selected matches"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpDispatcher();
                    var accepted = adapter.Accept(model.DraftPanel!, model.MatchFamilies.ToArray());
                    Check(label + " explicit confirmation commits the extra edit with refreshed coverage and no project mutation",
                        target.Choice == "Confirmed" && extra.Proteins == 0
                        && accepted.ConfirmedEvidenceMatches![target.Key].Choices.Single().Value == "Explicit edited extra label"
                        && file.ToJsonText() == before);
                    model.Back();
                    for (int index = 0; index < 6; index++)
                        model.DraftPanel!.Families.Add(new() { Key = "extra" + index, DisplayName = "Unrepresented import label " + index });
                    FinishReviewWork(async () => { await model.NextAsync(); });
                    var refreshed = model.MatchFamilies.Single(f => f.Key == target.Key);
                    var retainedExtra = refreshed.Suggestions.Single(s => s.Key == extra.Key);
                    Check(label + " panel-wide ranking refreshes after other families change while reviewed edits and ticks survive",
                        refreshed.Choice == "Confirmed" && refreshed.ShowAdditional && retainedExtra.Use
                        && retainedExtra.Text == "Explicit edited extra label" && retainedExtra.Proteins == 0
                        && !retainedExtra.IsAdditional && refreshed.PrimarySuggestions.Contains(retainedExtra)
                        && file.ToJsonText() == before);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
