using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using FamilyQuant.Evidence;
using FamilyQuant.Examples;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> WorkbookSubclassReviewCommands() =>
    [new("--test-workbook-subclass-review", 21119, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-workbook-subclass-review", (args, _) =>
        {
            WorkbookSubclassReviewTests(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static IReadOnlyList<PanelSubclassProposal> WorkbookSubclassReviewFixture()
    {
        PanelSubclassSourceRow Source(string sheet, int row) => new()
        {
            SheetName = sheet, RowNumber = row,
            Cells = new() { ["Class"] = "Original \"class\"", ["Architecture"] = "Required statement\nOriginal second line", ["Blank"] = "" },
        };
        return new PanelSubclassProposal[]
        {
            new()
            {
                ProposalId = "known", ExpectedNameId = "expected-known", FamilyKey = "family-a", Key = "part", DisplayName = "Known subclass",
                HasMatchedTableRow = true, CandidateFamilyKeys = new() { "family-a" }, SourceRows = new() { Source("Architecture", 3) },
                Reason = "The source row proposes rules for review.", Notes = new() { new() { Name = "Original note", Text = "Keep this note verbatim.\nAnd this line." } },
                Rules = new()
                {
                    new() { RuleId = "required", Accession = "PF00001", Name = "Domain A", Kind = PanelSubclassRuleKind.RequiredPfam, SourceRows = new() { Source("Architecture", 3) } },
                    new() { RuleId = "absent", Accession = "PF00002", Name = "Domain B", Kind = PanelSubclassRuleKind.AbsentPfam, SourceRows = new() { Source("Architecture", 4) } },
                },
            },
            new()
            {
                ProposalId = "expected-only", ExpectedNameId = "expected-missing", FamilyKey = "family-a", Key = "expected", DisplayName = "Expected subclass",
                IsUncertain = true, Reason = "No matching source table row was supplied.",
            },
            new()
            {
                ProposalId = "unplaced-row", Key = "unplaced", DisplayName = "Unplaced class", IsUncertain = true, HasMatchedTableRow = true,
                CandidateFamilyKeys = new() { "family-a", "family-b" }, SourceRows = new() { Source("Unplaced rows", 12) },
                Reason = "Choose the family for this source row.", Rules = new()
                {
                    new() { RuleId = "ambiguous", Name = "Shared domain name", CandidateAccessions = new() { "PF00010", "PF00011" },
                        IsUncertain = true, Reason = "The source name has more than one recognized accession.", SourceRows = new() { Source("Unplaced rows", 12) } },
                },
            },
            new()
            {
                ProposalId = "empty-table", IsUncertain = true, Reason = "The source table has no class rows.",
                Notes = new() { new() { Name = "Source sheet", Text = "Empty architecture table" }, new() { Name = "Source headers", Text = "Class; Architecture" } },
            },
            new()
            {
                ProposalId = "unnamed-row", FamilyKey = "family-b", HasMatchedTableRow = true, IsUncertain = true,
                Reason = "The subclass name is empty; enter a name before accepting this row.", CandidateFamilyKeys = new() { "family-b" },
                SourceRows = new() { new() { SheetName = "Unnamed class", RowNumber = 7, Cells = new() { ["Class"] = "", ["Architecture"] = "Original unnamed-row statement" } } },
            },
        };
    }

    private static void WorkbookSubclassReviewTests(string? output = null)
    {
        Section("Z16a9 sourced subclass review, explicit acceptance and item messages");
        EnsureApplication();
        var proposals = WorkbookSubclassReviewFixture();
        string original = JsonSerializer.Serialize(proposals);
        var choices = proposals.Select(proposal => ImportSubclassChoice.FromCore(proposal)).ToArray();
        Check("Z16a9 standalone proposals and rules are unselected and unconfirmed", choices.All(row => !row.Use && !row.Confirmed)
            && choices.SelectMany(row => row.Rules).All(rule => !rule.Use && !rule.Confirmed));
        Check("Z16a9 every expected, unplaced row and empty table remains visible", choices.Select(row => row.ProposalId).SequenceEqual(proposals.Select(row => row.ProposalId))
            && choices[1].SourceStatus.Contains("No source row", StringComparison.Ordinal)
            && choices[3].Title == "Unplaced source table" && choices[3].SourceNotes.Any(note => note.Text == "Empty architecture table"));
        Check("Z16a9 an unnamed row retains its known family without implying a class match", choices[4].Family == "family-b"
            && choices[4].Title == "Source row without a subclass name" && choices[4].SourceFamily == "Proposed family: family-b" && !choices[4].Use);
        Check("Z16a9 source rows, all original cells, notes and candidate accessions survive exactly",
            choices[0].SourceRows[0].SheetName == "Architecture" && choices[0].SourceRows[0].RowNumber == 3
            && choices[0].SourceRows[0].Cells.Any(cell => cell.Header == "Architecture" && cell.Text == "Required statement\nOriginal second line")
            && choices[0].SourceRows[0].Cells.Any(cell => cell.Header == "Blank" && cell.Text == "")
            && choices[2].Rules[0].CandidateAccessions.SequenceEqual(new[] { "PF00010", "PF00011" }));
        var empty = PanelSubclassImport.BuildDefinitions(proposals, ImportSubclassChoice.Capture(choices), new[] { "family-a", "family-b" });
        Check("Z16a9 initial uncertain items do not become definitions", empty.CanBuild && empty.Definitions.Count == 0);
        choices[0].Family = "family-b"; choices[0].Abbreviation = "edited-key"; choices[0].Name = "Edited display";
        choices[0].Use = true; choices[0].Rules[0].Use = true; choices[0].Rules[0].Kind = ImportSubclassRuleKind.AbsentPfam;
        choices[0].Rules[1].Reject();
        var expected = new PanelSubclassAcceptance
        {
            ProposalId = "known", Accepted = true, FamilyKey = "family-b", Key = "edited-key", DisplayName = "Edited display",
            Rules = new() { new() { RuleId = "required", Accepted = true, Kind = PanelSubclassRuleKind.AbsentPfam }, new() { RuleId = "absent", Accepted = false } },
        };
        Check("Z16a9 edited choices capture the exact typed acceptance", JsonSerializer.Serialize(choices[0].Capture()) == JsonSerializer.Serialize(expected));
        var built = PanelSubclassImport.BuildDefinitions(proposals, ImportSubclassChoice.Capture(choices), new[] { "family-a", "family-b" });
        Check("Z16a9 only included rules build with the explicitly changed polarity", built.CanBuild && built.Definitions.Count == 1
            && built.Definitions[0].FamilyKey == "family-b" && built.Definitions[0].Definition.Key == "edited-key"
            && built.Definitions[0].Definition.DisplayName == "Edited display" && built.Definitions[0].Definition.RequiredPfam.Count == 0
            && built.Definitions[0].Definition.AbsentPfam.SequenceEqual(new[] { "PF00001" }));
        Check("Z16a9 user choices and validation never confirm or edit the source", choices.All(row => !row.Confirmed)
            && choices.SelectMany(row => row.Rules).All(rule => !rule.Confirmed) && JsonSerializer.Serialize(proposals) == original);
        choices[1].Use = true;
        built = PanelSubclassImport.BuildDefinitions(proposals, ImportSubclassChoice.Capture(choices), new[] { "family-a", "family-b" });
        Check("Z16a9 an uncertain name-only item enters only after explicit inclusion", built.CanBuild && built.Definitions.Count == 2
            && built.Definitions.Single(item => item.Definition.Key == "expected").Definition.RequiredPfam.Count == 0);
        choices[0].Reject();
        Check("Z16a9 explicit rejection leaves the proposal out without hiding it", !choices[0].Use && choices[0].State == "Left out of draft"
            && choices[0].Rules.Count == 2 && choices[0].SourceRows.Count == 1);

        var seed = new PanelSubclassAcceptance { ProposalId = "known", Accepted = true,
            Rules = new() { new() { RuleId = "required", Accepted = true, Kind = null } } };
        string seedBefore = JsonSerializer.Serialize(seed);
        var seeded = ImportSubclassChoice.FromCore(proposals[0], seed);
        Check("Z16a9 summary input sets exact draft ticks without confirmation", seeded.Use && seeded.Rules[0].Use && !seeded.Rules[1].Use
            && !seeded.Confirmed && seeded.Rules.All(rule => !rule.Confirmed));
        Check("Z16a9 an untouched typed draft preserves omitted rules and empty overrides", JsonSerializer.Serialize(seeded.Capture()) == seedBefore);
        var explicitUntick = ImportSubclassChoice.FromCore(proposals[0], new PanelSubclassAcceptance { ProposalId = "known", Accepted = false });
        Check("Z16a9 an explicit empty typed selection remains unticked without inferred rules", !explicitUntick.Use
            && explicitUntick.Rules.All(rule => !rule.Use) && explicitUntick.Capture().Rules.Count == 0 && !explicitUntick.Confirmed);
        var rawOnly = new PanelSubclassProposal { ProposalId = "raw-only", FamilyKey = "family-a", Key = "raw", DisplayName = "Source-only class",
            SourceRows = new() { new() { SheetName = "Source text", RowNumber = 2, Cells = new() { ["Statement"] = "PF99999 without another domain" } } } };
        var rawChoice = ImportSubclassChoice.FromCore(rawOnly); rawChoice.Use = true;
        var rawBuilt = PanelSubclassImport.BuildDefinitions(new[] { rawOnly }, new[] { rawChoice.Capture() }, new[] { "family-a" });
        Check("Z16a9 rule-looking cell text never becomes a rule in the control", rawChoice.Rules.Count == 0 && rawBuilt.CanBuild
            && rawBuilt.Definitions.Single().Definition.RequiredPfam.Count == 0 && rawBuilt.Definitions.Single().Definition.AbsentPfam.Count == 0
            && rawBuilt.Definitions.Single().Definition.TextFields.Any(field => field.Text.Contains("PF99999 without another domain", StringComparison.Ordinal)));
        seeded.Rules[0].Kind = ImportSubclassRuleKind.AbsentPfam; seeded.Rules[1].Reject();
        var seedEdit = seeded.Capture();
        Check("Z16a9 edited draft rules retain IDs and become explicit selections", seedEdit.Rules.Count == 2
            && seedEdit.Rules[0].Kind == PanelSubclassRuleKind.AbsentPfam && !seedEdit.Rules[1].Accepted && JsonSerializer.Serialize(seed) == seedBefore);
        var detached = ImportSubclassChoice.FromCore(proposals[0]);
        proposals[0].SourceRows[0].Cells["Architecture"] = "Changed outside the control";
        proposals[0].Rules[0].Name = "Changed outside the control";
        Check("Z16a9 the presentation retains detached source records", detached.SourceRows[0].Cells.Any(cell => cell.Text == "Required statement\nOriginal second line")
            && detached.Rules[0].Name == "Domain A");
        WorkbookSubclassReviewProblemTests();
        WorkbookSubclassReviewSummaryTests();
        WorkbookSubclassReviewLayoutTests(output);
    }

    private static void WorkbookSubclassReviewSummaryTests()
    {
        string scratch = Path.GetFullPath(Path.GetTempPath());
        var root = Path.GetFullPath(Path.Combine(scratch, "subclass-summary-ui-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var importer = new FamilyPanelSpreadsheetImporter();
            var file = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "review-reference", Sequence = "ACDEFGHIK",
                Evidence = new() { Pfam = new() { new() { Accession = "PF01234", Name = "DomainOne" }, new() { Accession = "PF05678", Name = "DomainTwo" } } } } } };
            var matching = new PanelEvidenceMatching(file); string before = file.ToJsonText();
            var noEvidence = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>>();
            foreach (string shape in new[] { "shipped", "one-sheet", "no-legend" })
            {
                string path = Path.Combine(root, shape + ".xlsx");
                if (shape == "shipped") ExampleFamilyList.Write(path);
                else WriteNeutralWorkbook(path, onlyFamily: shape == "one-sheet", includeLegend: false, includeDecision: false);
                var proposal = ReviewSummaryFixture(importer.ProposeWorkbook(path), importer);
                var summary = WorkbookImportSummaries.Build(proposal, matching, reviewedEvidence: noEvidence);
                var seeds = summary.DraftSubclassSelections;
                var rows = summary.SubclassProposals.Select(source => ImportSubclassChoice.FromCore(source,
                    seeds.FirstOrDefault(seed => seed.ProposalId == source.ProposalId))).ToArray();
                var control = new FamilyListSubclassReview { SubclassChoices = rows, FamilyKeys = summary.FamilyKeys };
                var captured = control.CaptureAcceptances();
                Check("Z16a9 real " + shape + " summary draft round-trips exactly through the control", JsonSerializer.Serialize(captured) == JsonSerializer.Serialize(seeds)
                    && rows.All(row => !row.Confirmed) && rows.SelectMany(row => row.Rules).All(rule => !rule.Confirmed));
                var rebuilt = WorkbookImportSummaries.Build(proposal, matching, captured, noEvidence);
                var accepted = WorkbookImportSummaries.AcceptAll(rebuilt, importer, captured, noEvidence);
                Check("Z16a9 real " + shape + " captured draft reaches canonical AcceptAll", accepted.CanImport,
                    string.Join("; ", accepted.Items.SelectMany(item => item.Problems).Select(problem => problem.Message)));
                var selected = rows.FirstOrDefault(row => row.Use && row.Rules.Any(rule => rule.Use));
                if (selected != null)
                {
                    selected.Reject();
                    var edited = control.CaptureAcceptances();
                    var editedSummary = WorkbookImportSummaries.Build(proposal, matching, edited, noEvidence);
                    var editedResult = WorkbookImportSummaries.AcceptAll(editedSummary, importer, edited, noEvidence);
                    var canonical = (PanelSubclassProposal)selected.SourceProposal!;
                    string family = canonical.FamilyKey, key = canonical.Key;
                    Check("Z16a9 real " + shape + " explicit untick stays out after rebuild and acceptance", editedResult.CanImport
                        && editedResult.Panel!.Families.Where(item => item.Key == family).SelectMany(item => item.Subclasses).All(item => item.Key != key));
                }
                var uncertain = rows.FirstOrDefault(row => row.IsUncertain && row.Name.Length > 0);
                if (uncertain != null && summary.FamilyKeys.Count > 0)
                {
                    uncertain.Family = summary.FamilyKeys.First(); uncertain.Abbreviation = "reviewed-" + uncertain.ProposalId;
                    uncertain.Name = "Reviewed name-only class"; uncertain.Use = true;
                    foreach (var rule in uncertain.Rules) rule.Reject();
                    var resolved = control.CaptureAcceptances();
                    var resolvedSummary = WorkbookImportSummaries.Build(proposal, matching, resolved, noEvidence);
                    var resolvedResult = WorkbookImportSummaries.AcceptAll(resolvedSummary, importer, resolved, noEvidence);
                    Check("Z16a9 real " + shape + " explicit legal uncertain resolution reaches AcceptAll", resolvedResult.CanImport
                        && resolvedResult.Panel!.Families.SelectMany(item => item.Subclasses).Any(item => item.Key == uncertain.Abbreviation
                            && item.RequiredPfam.Count == 0 && item.AbsentPfam.Count == 0),
                        string.Join("; ", resolvedResult.Items.SelectMany(item => item.Problems).Select(problem => problem.Message).Distinct(StringComparer.Ordinal).Take(3)));
                }
            }
            Check("Z16a9 summary review and canonical acceptance never mutate the project", file.ToJsonText() == before);
        }
        finally
        {
            string allowed = scratch.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("Refusing cleanup outside the owned test scratch root.");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void WorkbookSubclassReviewProblemTests()
    {
        var choices = WorkbookSubclassReviewFixture().Select(proposal => ImportSubclassChoice.FromCore(proposal)).ToArray();
        var control = new FamilyListSubclassReview { SubclassChoices = choices, FamilyKeys = new[] { "family-a", "family-b" } };
        choices[0].Use = true; choices[0].Family = "missing-family";
        var result = control.CheckChoices();
        Check("Z16a9 a BuildDefinitions family problem appears on its proposal", !result.CanBuild && result.Definitions.Count == 0
            && choices[0].Problems.Count > 0 && choices[0].ProblemText.Contains("family key", StringComparison.Ordinal));
        choices[0].Family = "family-a"; choices[2].Family = "family-b"; choices[2].Use = true; choices[2].Rules[0].Use = true;
        result = control.CheckChoices();
        Check("Z16a9 an unresolved accession problem appears on its exact rule", !result.CanBuild
            && choices[2].Rules[0].Problems.Count > 0 && choices[2].Rules[0].ProblemText.Contains("accession", StringComparison.Ordinal));
        choices[2].Rules[0].Reject(); result = control.CheckChoices();
        Check("Z16a9 rejecting the unresolved rule clears problems on the next check", result.CanBuild && result.Definitions.Count == 2
            && choices.All(row => !row.HasProblems) && choices.All(row => !row.Confirmed));
        control.ShowProblems(new[] { new PanelSubclassImportProblem { ProposalId = "removed-proposal", RuleId = "removed-rule", Message = "Refresh the source." } });
        Check("Z16a9 unknown problem IDs remain visible", control.UnmatchedProblemText.Contains("removed-proposal", StringComparison.Ordinal)
            && control.UnmatchedProblemText.Contains("removed-rule", StringComparison.Ordinal) && control.UnmatchedProblemText.Contains("Refresh the source.", StringComparison.Ordinal));
        var staleSeed = new PanelSubclassAcceptance { ProposalId = "known", Accepted = true,
            Rules = new() { new() { RuleId = "removed-rule", Accepted = true } } };
        var stale = ImportSubclassChoice.FromCore(WorkbookSubclassReviewFixture()[0], staleSeed);
        control.SubclassChoices = new[] { stale }; result = control.CheckChoices();
        Check("Z16a9 a stale typed rule is neither dropped nor silently accepted", !result.CanBuild && stale.Rules.Any(rule => rule.RuleId == "removed-rule"
            && rule.HasProblems) && stale.Capture().Rules.Single().RuleId == "removed-rule");
    }

    private static void WorkbookSubclassReviewLayoutTests(string? output)
    {
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var choices = new ObservableCollection<ImportSubclassChoice>(WorkbookSubclassReviewFixture().Select(proposal => ImportSubclassChoice.FromCore(proposal)));
                var host = new WorkbookSubclassHost(choices);
                var control = new FamilyListSubclassReview { DataContext = host, FamilyKeys = new[] { "family-a", "family-b" } };
                var frame = new Grid { Margin = new Thickness(24) };
                frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(244) }); frame.ColumnDefinitions.Add(new ColumnDefinition());
                frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) }); frame.RowDefinitions.Add(new RowDefinition());
                frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
                Grid.SetColumn(control, 1); Grid.SetRow(control, 1); frame.Children.Add(control);
                var window = new Window { Title = "Subclass review", Width = 1366, Height = 768, Content = frame,
                    Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
                try
                {
                    window.Show(); PumpDispatcher(); window.UpdateLayout();
                    var list = (ListBox)control.FindName("ProposalList");
                    Check(theme + " Z16a9 existing host bindings show every grouped source state", list.Items.Count == 5
                        && control.SelectedSubclass != null && ReferenceEquals(control.SelectedSubclass, host.SelectedSubclass));
                    bool allStates = true;
                    foreach (var choice in choices)
                    {
                        host.SelectedSubclass = choice; list.ScrollIntoView(choice); PumpDispatcher(); window.UpdateLayout();
                        allStates &= ReferenceEquals(control.SelectedSubclass, choice) && list.SelectedItem == choice
                            && WorkbookSubclassWalk(control).OfType<TextBlock>().Any(text => text.Text == choice.Title);
                    }
                    Check(theme + " Z16a9 every normal, expected, unplaced-row and empty-table state renders", allStates);
                    host.SelectedSubclass = choices[0]; PumpDispatcher(); window.UpdateLayout();
                    var include = (CheckBox)control.FindName("IncludeSubclass");
                    include.IsChecked = true; include.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
                    Check(theme + " Z16a9 the native checkbox edits only the draft", choices[0].Use && !choices[0].Confirmed);
                    var detail = (ScrollViewer)control.FindName("DetailScroll"); detail.ScrollToEnd(); PumpDispatcher(); window.UpdateLayout();
                    Check(theme + " Z16a9 multiline original cell text remains reachable without truncation", detail.ScrollableHeight > 0
                        && WorkbookSubclassWalk(control).OfType<TextBox>().Any(text => text.Text == "Required statement\nOriginal second line"));
                    if (output != null)
                    {
                        string folder = Path.Combine(output, theme.ToString().ToLowerInvariant()); Directory.CreateDirectory(folder);
                        ScreenHarness.Save(window, Path.Combine(folder, "03-original-source-cells.png"), 1366, 768);
                    }
                    detail.ScrollToTop(); PumpDispatcher(); window.UpdateLayout();
                    bool inside = new[] { "ProposalList", "DetailScroll", "CheckButton" }.All(name =>
                    {
                        var element = (FrameworkElement)control.FindName(name);
                        var bounds = element.TransformToAncestor(control).TransformBounds(new Rect(element.RenderSize));
                        return element.ActualWidth > 0 && element.ActualHeight > 0 && bounds.Left >= -1 && bounds.Top >= -1
                            && bounds.Right <= control.ActualWidth + 1 && bounds.Bottom <= control.ActualHeight + 1;
                    });
                    Check(theme + " Z16a9 the list, detail viewport and check action fit the 1366x768 host", inside);
                    var interactive = WorkbookSubclassWalk(control).OfType<FrameworkElement>().Where(element =>
                        element is Button or CheckBox or TextBox or ComboBox or ListBox or Expander).ToArray();
                    var missing = interactive.Where(element => element.ToolTip is not string tip || tip.Length < 35).Select(element => element.GetType().Name + ":" + element.Name).ToArray();
                    Check(theme + " Z16a9 every rendered input, action, list and expander has an informative tooltip", interactive.Length >= 15 && missing.Length == 0,
                        string.Join(", ", missing));
                    control.IsEnabled = false;
                    Check(theme + " Z16a9 disabled draft controls retain their hover text", interactive.All(ToolTipService.GetShowOnDisabled));
                    control.IsEnabled = true;
                    Check(theme + " Z16a9 selected proposal text stays readable with the theme selection surface",
                        ChartPalette.ContrastRatio((Brush)control.FindResource("TextBrush"), (Brush)control.FindResource("SelectionBrush")) >= 4.5);
                    if (output != null)
                    {
                        string folder = Path.Combine(output, theme.ToString().ToLowerInvariant()); Directory.CreateDirectory(folder);
                        ScreenHarness.Save(window, Path.Combine(folder, "01-subclass-review.png"), 1366, 768);
                    }
                    host.SelectedSubclass = choices[2]; choices[2].Family = "family-b"; choices[2].Use = true; choices[2].Rules[0].Use = true;
                    control.CheckChoices(); PumpDispatcher(); window.UpdateLayout();
                    var ruleMessage = WorkbookSubclassWalk(control).OfType<TextBlock>().FirstOrDefault(text => text.Name == "RuleProblem"
                        && text.DataContext is ImportSubclassRuleChoice { HasProblems: true });
                    bool messageVisible = ruleMessage != null && ruleMessage.Text.Contains("accession", StringComparison.Ordinal);
                    if (ruleMessage != null)
                    {
                        var bounds = ruleMessage.TransformToAncestor(detail).TransformBounds(new Rect(ruleMessage.RenderSize));
                        messageVisible &= bounds.Top >= -1 && bounds.Bottom <= detail.ActualHeight + 1;
                    }
                    Check(theme + " Z16a9 checking choices brings its exact rule message into view", messageVisible);
                    if (output != null) ScreenHarness.Save(window, Path.Combine(output, theme.ToString().ToLowerInvariant(), "02-rule-problem.png"), 1366, 768);
                    var draft = new PanelSubclassAcceptance { ProposalId = "known", Accepted = true,
                        Rules = new() { new() { RuleId = "required", Accepted = true, Kind = PanelSubclassRuleKind.AbsentPfam } } };
                    var draftRows = WorkbookSubclassReviewFixture().Select(proposal => ImportSubclassChoice.FromCore(proposal,
                        proposal.ProposalId == draft.ProposalId ? draft : null)).ToArray();
                    control.SubclassChoices = draftRows; control.SelectedSubclass = draftRows[0];
                    PumpDispatcher(); window.UpdateLayout(); detail.ScrollToTop();
                    Check(theme + " Z16a9 typed summary ticks render as unconfirmed draft choices", control.DraftHint.Contains("summary draft", StringComparison.Ordinal)
                        && ((CheckBox)control.FindName("IncludeSubclass")).IsChecked == true && draftRows.All(row => !row.Confirmed)
                        && draftRows.SelectMany(row => row.Rules).All(rule => !rule.Confirmed)
                        && JsonSerializer.Serialize(control.CaptureAcceptances()[0]) == JsonSerializer.Serialize(draft));
                    if (output != null) ScreenHarness.Save(window, Path.Combine(output, theme.ToString().ToLowerInvariant(), "04-typed-summary-draft.png"), 1366, 768);
                    control.ShowIntroduction = false; PumpDispatcher(); window.UpdateLayout();
                    Check(theme + " Z16a9 hosted mode removes duplicate introduction without hiding source details",
                        ((FrameworkElement)control.FindName("Introduction")).Visibility == Visibility.Collapsed
                        && ((FrameworkElement)control.FindName("DetailScroll")).IsVisible && control.CaptureAcceptances()[0].Accepted);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
    }

    private static IEnumerable<DependencyObject> WorkbookSubclassWalk(DependencyObject root)
    {
        yield return root;
        if (root is not Visual) yield break;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in WorkbookSubclassWalk(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private sealed class WorkbookSubclassHost(ObservableCollection<ImportSubclassChoice> choices) : ScreenViewModel
    {
        private ImportSubclassChoice? selected;
        public ObservableCollection<ImportSubclassChoice> SubclassChoices { get; } = choices;
        public ImportSubclassChoice? SelectedSubclass { get => selected; set => Set(ref selected, value); }
    }
}
