using FamilyQuant.Evidence;
using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> WorkbookImportSummaryCommands() =>
    [new("--test-workbook-import-summary", 21609, TestCommandPhase.Raw,
        args => args is ["--test-workbook-import-summary"], (_, _) =>
        {
            WorkbookImportSummaryTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void WorkbookImportSummaryTests()
    {
        Section("Z16a9 detached workbook summary and explicit accept all");
        var api = typeof(FamilyPanel).Assembly.GetType("FamilyQuant.Panels.Import.WorkbookImportSummaries");
        var build = api?.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
        var accept = api?.GetMethod("AcceptAll", BindingFlags.Public | BindingFlags.Static);
        Check("Z16a9 the detached summary and explicit acceptance contract exists", build != null && accept != null);
        if (build == null || accept == null) return;
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.WorkbookSummary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var importer = new FamilyPanelSpreadsheetImporter();
        var file = new FamilyClusterFile(); var matching = new PanelEvidenceMatching(file);
        string sourceBefore = file.ToJsonText();
        foreach (string shape in new[] { "shipped", "one-sheet", "no-legend" })
        {
            string path = Path.Combine(root, shape + ".xlsx");
            if (shape == "shipped") ExampleFamilyList.Write(path);
            else WriteNeutralWorkbook(path, onlyFamily: shape == "one-sheet", includeLegend: false, includeDecision: false);
            var proposal = importer.ProposeWorkbook(path);
            string before = JsonSerializer.Serialize(proposal);
            dynamic summary = build.Invoke(null, new object?[] { proposal, matching, null, null })!;
            var items = SummaryItems((object)summary).ToArray();
            Check("Z16a9 " + shape + " has all eight finding sections", ((IEnumerable)summary.Sections).Cast<object>().Count() == 8);
            Check("Z16a9 " + shape + " includes the detected family sheet and columns", items.Any(item => item.Page.ToString() == "FileAndSheets"
                && ((string)item.Value).Contains(proposal.FamilySheet, StringComparison.Ordinal))
                && items.Any(item => item.Page.ToString() == "Columns"));
            Check("Z16a9 " + shape + " every finding has a reason and source", items.All(item => !string.IsNullOrWhiteSpace((string)item.Reason)
                && ((IEnumerable)item.Sources).Cast<object>().Any()));
            Check("Z16a9 " + shape + " building the summary changes no input", JsonSerializer.Serialize(proposal) == before && file.ToJsonText() == sourceBefore);
            if (proposal.ColorsState == PanelWorkbookReadState.NotRead || proposal.DecisionsState == PanelWorkbookReadState.NotRead)
            {
                dynamic refusal = accept.Invoke(null, new object?[] { summary, importer, Array.Empty<PanelSubclassAcceptance>(),
                    new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>>(), "Summary fixture", false, null })!;
                Check("Z16a9 " + shape + " unread parts cannot become a panel", refusal.Panel == null && !(bool)refusal.CanImport);
            }
            var noEvidence = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>>();
            var blockedSummary = WorkbookImportSummaries.Build(proposal, matching, reviewedEvidence: noEvidence);
            var probe = new NotesImporterProbe((_, _, _, _) => throw new InvalidOperationException("An unresolved summary must not call Check."));
            var blocked = WorkbookImportSummaries.AcceptAll(blockedSummary, probe, blockedSummary.DraftSubclassSelections, noEvidence);
            Check("Z16a9 " + shape + " unresolved required choices block before family Check", blockedSummary.HasBlockingItems && !blocked.CanImport && probe.Calls == 0);
            Check("Z16a9 " + shape + " each required item names its resolving page", blockedSummary.Sections.SelectMany(section => section.Items)
                .Where(item => item.Required && item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace)
                .All(item => blocked.Items.Single(result => result.ItemId == item.Id).Problems.Count(problem => problem.Blocking
                    && problem.Message.Contains(item.Label, StringComparison.Ordinal) && problem.Message.Contains(item.Page.ToString(), StringComparison.Ordinal)) == 1));

            var reviewed = ReviewSummaryFixture(proposal, importer);
            var ready = WorkbookImportSummaries.Build(reviewed, matching);
            Check("Z16a9 " + shape + " explicit reviewed choices make the summary ready", !ready.HasBlockingItems,
                string.Join("; ", ready.Sections.SelectMany(section => section.Items).Where(item => item.Required && item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace)
                    .Select(item => item.Label + ": " + item.Reason)));
            var selected = ready.ProposedEvidenceSelections;
            var result = WorkbookImportSummaries.AcceptAll(ready, importer, ready.ReviewedSubclassSelections, selected, "Summary fixture");
            Check("Z16a9 " + shape + " one accept returns the shown import", result.CanImport,
                string.Join("; ", result.Items.SelectMany(item => item.Problems).Where(problem => problem.Blocking).Select(problem => problem.Message)));
            if (result.Panel == null) continue;
            var direct = importer.CheckConfirmedWorkbook(reviewed, ready.FamilyChoices, ready.ProposedDecisions, "Summary fixture");
            Check("Z16a9 " + shape + " family rows notes and decisions exactly match the displayed confirmed calls", direct.Panel != null
                && JsonSerializer.Serialize(result.Panel.Families) == JsonSerializer.Serialize(direct.Panel.Families)
                && JsonSerializer.Serialize(result.Panel.SourceNotes) == JsonSerializer.Serialize(reviewed.Notes)
                && JsonSerializer.Serialize(result.Panel.DecisionMappings) == JsonSerializer.Serialize(direct.Panel.DecisionMappings));
            Check("Z16a9 " + shape + " confirmed matches contain only the typed shown selections", (result.Panel.ConfirmedEvidenceMatches?.Count ?? 0) == selected.Count
                && selected.All(pair => result.Panel.ConfirmedEvidenceMatches![pair.Key].Choices.SequenceEqual(
                    PanelEvidenceMatching.Confirm(Array.Empty<string>(), pair.Value).Choices)));
            Check("Z16a9 " + shape + " optional unknown subclass items stay unaccepted", ready.Sections.Where(section => section.Kind == WorkbookImportSectionKind.Subclasses)
                .SelectMany(section => section.Items).Where(item => item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace)
                .All(item => !result.Items.Single(outcome => outcome.ItemId == item.Id).Accepted)
                && result.Panel.Families.All(family => family.Subclasses.Count == 0));
            Check("Z16a9 " + shape + " pre-Check family keys equal the visible summary keys", ready.FamilyKeys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(WorkbookImportSummaries.ProposedFamilyKeys(reviewed)));
            Check("Z16a9 " + shape + " sheet domain labels never become provider vocabulary", reviewed.Pfam.Count > 0 && ready.RecognizedPfamDomains.Count == 0
                && ready.SubclassProposals.All(subclass => subclass.Rules.Count == 0));
            Check("Z16a9 " + shape + " source-bearing expected names are prepared in Core", ready.ExpectedSubclassNames.Count > 0
                && ready.ExpectedSubclassNames.All(name => name.SourceRows.Any(row => row.SheetName == reviewed.FamilySheet && row.Cells.Count > 1)));
            Check("Z16a9 " + shape + " detached family rows show the checked names decisions and merge targets", direct.Panel != null
                && direct.Panel.Families.All(family => ready.FamilyRows.Any(row => row.Key == family.Key && row.DisplayName == family.DisplayName
                    && row.Decision == family.Decision && row.MergeTargetFamilyKey == family.MergeTargetFamilyKey && row.SourceCells.Count > 1)));
            string acceptedJson = JsonSerializer.Serialize(result.Panel);
            if (reviewed.Notes.Count > 0) reviewed.Notes[0].Text = "Changed outside the summary";
            reviewed.FamilyProposal.Choices.ColumnHeaders.Clear();
            ready.FamilyChoices.ColumnHeaders.Clear();
            var exposedNames = ready.ExpectedSubclassNames;
            if (exposedNames.Count > 0) exposedNames[0].DisplayName = "Changed exposed name";
            var exposedClasses = ready.SubclassProposals;
            if (exposedClasses.Count > 0) exposedClasses[0].FamilyKey = "Changed exposed key";
            var again = WorkbookImportSummaries.AcceptAll(ready, importer, ready.ReviewedSubclassSelections, selected, "Summary fixture");
            Check("Z16a9 " + shape + " source and typed getter edits cannot change the private plan", JsonSerializer.Serialize(again.Panel) == acceptedJson
                && file.ToJsonText() == sourceBefore);
        }
        WorkbookSummarySelectionTests(root);
        WorkbookSummaryResolutionTests(root);
        WorkbookSummaryRuleResolutionTests(root);
        Console.WriteLine("Z16a9 synthetic artifacts: " + root);
    }

    private static PanelWorkbookProposal ReviewSummaryFixture(PanelWorkbookProposal source, FamilyPanelSpreadsheetImporter importer)
    {
        // These assignments model explicit review actions, never a source-word decision heuristic.
        var proposal = importer.ProposeWorkbook(SummaryCopy(source.Workbook), source.FamilySheet);
        var choices = proposal.FamilyProposal.Choices;
        var table = proposal.FamilyTable!;
        // Neutral fixture headers are declared by WriteNeutralWorkbook; this models choosing its source role.
        if (table.Headers.Contains("h13", StringComparer.Ordinal)) choices.ColumnHeaders[WorkbookImportSummaryFields.Subclasses] = "h13";
        if (!choices.ColumnHeaders.ContainsKey(PanelImportFields.Decision) && proposal.FamilyProposal.DecisionHint.Length > 0)
            choices.ColumnHeaders[PanelImportFields.Decision] = proposal.FamilyProposal.DecisionHint;
        foreach (var hint in proposal.Decisions)
            hint.ProposedMapping ??= new() { SourceValue = hint.SourceValue, Decision = FamilyPanelDecision.ReportUnderOthers };
        if (proposal.Colors.Any(color => color.Entries.Count == 0 || color.IsUncertain))
            choices.ColumnHeaders[PanelImportFields.ColorCategory] = ""; // Explicitly leave unresolved fills as source text.
        string keyColumn = choices.ColumnHeaders[PanelImportFields.FamilyKey];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        choices.RowChoices ??= new();
        foreach (var row in table.Rows)
        {
            string key = row.Cells.GetValueOrDefault(keyColumn, "").Trim();
            if (key.Length == 0) continue;
            bool duplicate = !seen.Add(key);
            bool ambiguous = proposal.MultiFamilyRows.Any(multi => multi.RowNumber == row.RowNumber && multi.SuggestedAction == null);
            if (!duplicate && !ambiguous) continue;
            var choice = new FamilyPanelRowChoice { RowNumber = row.RowNumber, Action = FamilyPanelRowAction.Import };
            if (duplicate) choice.Key = key + "_reviewed_" + row.RowNumber;
            choices.RowChoices.RemoveAll(existing => existing.RowNumber == row.RowNumber); choices.RowChoices.Add(choice);
        }
        return proposal;
    }

    private static void WorkbookSummarySelectionTests(string root)
    {
        var importer = new FamilyPanelSpreadsheetImporter();
        string path = Path.Combine(root, "selection-cases.xlsx"); WriteNeutralWorkbook(path, includeLegend: false, includeDecision: false);
        var source = ReviewSummaryFixture(importer.ProposeWorkbook(path), importer);
        var emptyFile = new FamilyClusterFile(); var matching = new PanelEvidenceMatching(emptyFile);
        var empty = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>>();
        var savedUntick = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>> { ["F01"] = Array.Empty<PanelEvidenceChoice>() };
        var unticked = WorkbookImportSummaries.Build(source, matching, reviewedEvidence: savedUntick);
        var untickResult = WorkbookImportSummaries.AcceptAll(unticked, importer, unticked.DraftSubclassSelections, savedUntick);
        Check("Z16a9 an explicit empty selection overrides sheet defaults without erasing source domains", untickResult.Panel?.ConfirmedEvidenceMatches is { Count: 1 } matches
            && matches["F01"].Choices.Count == 0 && untickResult.Panel.Families.Single(family => family.Key == "F01").Pfam.Count == 2);
        var selected = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>> { ["F01"] = new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Pfam, "PF01234") } };
        var shown = WorkbookImportSummaries.Build(source, matching, reviewedEvidence: selected);
        var hidden = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>> { ["F01"] = new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Pfam, "PF99999") } };
        var probe = new NotesImporterProbe((_, _, _, _) => throw new InvalidOperationException("Changed selections must not reach Check."));
        var refused = WorkbookImportSummaries.AcceptAll(shown, probe, shown.DraftSubclassSelections, hidden);
        Check("Z16a9 evidence not shown in the summary is refused before import", !refused.CanImport && probe.Calls == 0
            && refused.Items.SelectMany(item => item.Problems).Any(problem => problem.Blocking && problem.Message.Contains("changed", StringComparison.Ordinal)));

        var reduced = SummaryCopy(source);
        var firstHint = reduced.Pfam.First(hint => hint.FamilyKey == "F01");
        string removed = firstHint.Domains.Last().Accession; firstHint.Domains.RemoveAt(firstHint.Domains.Count - 1);
        var reducedSummary = WorkbookImportSummaries.Build(reduced, matching, reviewedEvidence: empty);
        var reducedResult = WorkbookImportSummaries.AcceptAll(reducedSummary, importer, reducedSummary.DraftSubclassSelections, empty);
        Check("Z16a9 a source domain removed from the displayed proposal cannot be reintroduced by Check", reducedResult.Panel != null
            && reducedResult.Panel.Families.Single(family => family.Key == "F01").Pfam.All(domain => domain.Accession != removed)
            && reducedResult.Panel.Families.Single(family => family.Key == "F01").TextFields.Any(field => field.Text.Contains(removed, StringComparison.Ordinal)));

        var stored = new FamilyClusterFile();
        for (int index = 0; index < 2; index++) stored.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "tr|Q7000" + index + "|invented",
            Sequence = "ACDEFGHIK", Evidence = new() { Pfam = new() { new() { Accession = "PF01234", Name = "DomainOne" }, new() { Accession = "PF05678", Name = "DomainTwo" } } } });
        string storedBefore = stored.ToJsonText();
        var storedMatching = new PanelEvidenceMatching(stored);
        var broad = WorkbookImportSummaries.Build(source, storedMatching, reviewedEvidence: selected);
        var noQuestion = WorkbookImportSummaries.AcceptAll(broad, importer, broad.DraftSubclassSelections, selected);
        var confirmed = WorkbookImportSummaries.AcceptAll(broad, importer, broad.DraftSubclassSelections, selected, broadPfamConfirmed: true);
        Check("Z16a9 broad source matches retain the counted confirmation gate", !noQuestion.CanImport && confirmed.CanImport
            && broad.Sections.SelectMany(section => section.Items).Any(item => item.ConfirmationWarning.Contains("PF01234 (2)", StringComparison.Ordinal)));
        Check("Z16a9 provider vocabulary comes from stored evidence rather than sheet names", broad.RecognizedPfamDomains.Count == 2
            && broad.RecognizedPfamDomains.Any(domain => domain.Accession == "PF01234" && domain.Name == "DomainOne"));
        var ordinalSource = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "tr|Q79999|invented", Evidence = new()
            { Pfam = new() { new() { Accession = "PF09999", Name = "Shared provider name" }, new() { Accession = "pf09999", Name = "Shared provider name" } } } } } };
        var ordinalVocabulary = new PanelEvidenceMatching(ordinalSource).RecognizedPfamDomains();
        Check("Z16a9 provider vocabulary preserves ordinal accession pairs instead of the folded matching index", ordinalVocabulary.Count == 2
            && ordinalVocabulary.Select(domain => domain.Accession).SequenceEqual(new[] { "PF09999", "pf09999" }));

        var candidates = broad.SubclassProposals;
        var known = candidates.First(proposal => !proposal.IsUncertain && proposal.FamilyKey == "F07" && proposal.Rules.Count == 2);
        var selection = ConfirmSubclass(known);
        selection.FamilyKey = "F08"; selection.Key = "Reviewed class"; selection.DisplayName = "Reviewed class name";
        var absentRule = known.Rules.Single(rule => rule.Accession == "PF05678");
        selection.Rules.Single(rule => rule.RuleId == absentRule.RuleId).Kind = PanelSubclassRuleKind.AbsentPfam;
        var acceptedClasses = new[] { selection };
        var classSummary = WorkbookImportSummaries.Build(source, storedMatching, acceptedClasses, empty);
        Check("Z16a9 Core prepares real subclass proposals from source columns before a checked panel exists", classSummary.ExpectedSubclassNames.Count > 0
            && classSummary.SubclassProposals.Any(proposal => proposal.ProposalId == known.ProposalId && proposal.SourceRows.Any(row => row.SheetName == "Data 3")));
        var classResult = WorkbookImportSummaries.AcceptAll(classSummary, importer, acceptedClasses, empty);
        var definition = classResult.Panel?.Families.Single(family => family.Key == "F08").Subclasses.SingleOrDefault();
        Check("Z16a9 explicit edited family key name and rule polarity reach the real BuildDefinitions", definition?.Key == "Reviewed class"
            && definition.DisplayName == "Reviewed class name" && definition.RequiredPfam.SequenceEqual(new[] { "PF01234" })
            && definition.AbsentPfam.SequenceEqual(new[] { "PF05678" }));
        Check("Z16a9 accepted source rules execute in the native subclass engine", definition != null
            && SubclassAssignment.ClassifyDirect(new() { Key = "F08", Subclasses = new() { definition } },
                new ProteinEvidence { Fragment = false, Pfam = new() { new() { Accession = "PF01234" } } }).Status == ProteinSubclassStatus.Assigned);
        var changedClasses = SummaryCopy(acceptedClasses.ToList()); changedClasses[0].DisplayName = "Changed after display";
        Check("Z16a9 subclass edits after display require a rebuilt summary", !WorkbookImportSummaries.AcceptAll(classSummary, importer, changedClasses, empty).CanImport);
        Check("Z16a9 certain subclass drafts start selected and uncertain items start unselected", broad.SubclassProposals.All(proposal =>
            broad.DraftSubclassSelections.Single(choice => choice.ProposalId == proposal.ProposalId).Accepted == !proposal.IsUncertain)
            && broad.SubclassProposals.SelectMany(proposal => proposal.Rules).All(rule => broad.DraftSubclassSelections.SelectMany(choice => choice.Rules)
                .Single(choice => choice.RuleId == rule.RuleId).Accepted == !rule.IsUncertain));
        var omitted = WorkbookImportSummaries.Build(source, storedMatching, Array.Empty<PanelSubclassAcceptance>(), empty);
        var omittedResult = WorkbookImportSummaries.AcceptAll(omitted, importer, Array.Empty<PanelSubclassAcceptance>(), empty);
        Check("Z16a9 an explicit empty subclass review never implies acceptance", omittedResult.Panel != null && omittedResult.Panel.Families.All(family => family.Subclasses.Count == 0));
        var unknown = new PanelSubclassAcceptance { ProposalId = "not-shown", Accepted = true };
        var unknownSummary = WorkbookImportSummaries.Build(source, storedMatching, new[] { unknown }, empty);
        Check("Z16a9 unknown subclass ids return per-item problems rather than definitions", !WorkbookImportSummaries.AcceptAll(unknownSummary, importer, new[] { unknown }, empty).CanImport);
        var unread = SummaryCopy(source); unread.DecisionsState = PanelWorkbookReadState.NotRead;
        Check("Z16a9 an unread part remains required even when the caller has choices", WorkbookImportSummaries.Build(unread, storedMatching, reviewedEvidence: empty).HasBlockingItems);
        Check("Z16a9 proposing and accepting never mutate the stored evidence or project", stored.ToJsonText() == storedBefore && emptyFile.MyExtendedFastaItems.Count == 0);
    }

    private static T SummaryCopy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private static IEnumerable<dynamic> SummaryItems(dynamic summary)
    {
        foreach (dynamic section in (IEnumerable)summary.Sections)
            foreach (dynamic item in (IEnumerable)section.Items) yield return item;
    }
}
