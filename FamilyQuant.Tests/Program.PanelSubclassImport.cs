using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelSubclassImportCommands() =>
    [new("--test-panel-subclass-import", 21605, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-panel-subclass-import", (args, _) =>
    {
        PanelSubclassImportTests(args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "subclass-import", Guid.NewGuid().ToString("N")));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static PanelSubclassAcceptance ConfirmSubclass(PanelSubclassProposal proposal) => new()
    {
        ProposalId = proposal.ProposalId, Accepted = true,
        Rules = proposal.Rules.Select(rule => new PanelSubclassRuleAcceptance { RuleId = rule.RuleId, Accepted = true }).ToList(),
    };

    private static void PanelSubclassImportTests(string output)
    {
        Section("Z16a5 sourced subclass proposals and explicit canonical rules");
        Directory.CreateDirectory(output);
        var keys = new[] { "F1", "F2" };
        var vocabulary = new List<FamilyPanelDomain>
        {
            new() { Accession = "PF01234", Name = "Anchor domain" }, new() { Accession = "PF05678", Name = "Tail domain" },
            new() { Accession = "PF09090", Name = "Extra domain" },
        };
        const string architecture = "Anchor domain (PF01234) + Tail domain; sem Extra domain; keep this note\nexactly as written";
        var familySource = new PanelSubclassSourceRow { SheetName = "Family list", RowNumber = 7,
            Cells = new() { ["Key"] = "F1", ["Expected"] = "First class; Missing class", ["Note"] = "Original family-list cell" } };
        var expected = new List<PanelSubclassExpectedName>
        {
            new() { ExpectedNameId = "first", FamilyKey = "F1", DisplayName = "First class", SourceRows = new() { familySource } },
            new() { ExpectedNameId = "missing", FamilyKey = "F1", DisplayName = "Missing class", SourceRows = new() { familySource } },
            new() { ExpectedNameId = "sourceless", FamilyKey = "F1", DisplayName = "Unsourced class" },
        };
        PanelSpreadsheetRow Row(int number, string family, string name, string domains) => new()
            { RowNumber = number, Cells = new() { ["Family"] = family, ["Class"] = name, ["Architecture"] = domains, ["Notes"] = "Kept row note" } };
        var table = new PanelWorkbookSubclassTable { SheetName = "Class details", FamilyKeys = new() { "F1", "F2" },
            FamilyKeyColumn = "Family", ClassColumn = "Class", DomainColumns = new() { "Architecture" }, Table = new()
            { Headers = new() { "Family", "Class", "Architecture", "Notes" }, Rows = new()
            { Row(3, "F1", "First class", architecture), Row(4, "F2", "Notes class", "Unrecognized domain material"),
                Row(5, "F9", "Unplaced class", "Anchor domain"), Row(6, "F1", "", "Anchor domain") } } };
        var caseTable = new PanelWorkbookSubclassTable { SheetName = "Case test", FamilyKeys = new() { "f1" }, ClassColumn = "Class",
            DomainColumns = new() { "Architecture" }, Table = new() { Rows = new() { Row(2, "f1", "Case class", "Anchor domain") } } };
        var emptyTable = new PanelWorkbookSubclassTable { SheetName = "Empty source", Table = new() { Headers = new() { "Kept header" } } };
        var tables = new[] { table, caseTable, emptyTable };
        string sourceBefore = JsonSerializer.Serialize(new { tables, expected, vocabulary });
        var proposed = PanelSubclassImport.Propose(tables, keys, vocabulary, expected);
        var first = proposed.Proposals.Single(proposal => proposal.ExpectedNameId == "first");
        Check("Z16a5 every expected name produces exactly one proposal", expected.All(name => proposed.Proposals.Count(proposal => proposal.ExpectedNameId == name.ExpectedNameId) == 1));
        Check("Z16a5 matched expected name keeps both original family-list and table source cells", first.HasMatchedTableRow
            && first.SourceRows.Any(row => row.SheetName == familySource.SheetName && row.RowNumber == 7 && row.Cells["Expected"] == familySource.Cells["Expected"])
            && first.SourceRows.Any(row => row.SheetName == table.SheetName && row.RowNumber == 3 && row.Cells["Architecture"] == architecture));
        var missing = proposed.Proposals.Single(proposal => proposal.ExpectedNameId == "missing");
        var sourceless = proposed.Proposals.Single(proposal => proposal.ExpectedNameId == "sourceless");
        Check("Z16a5 expected unmatched and matched empty are distinct visible outcomes", !missing.HasMatchedTableRow && missing.IsUncertain
            && missing.SourceRows.Single().Cells["Expected"] == familySource.Cells["Expected"]
            && proposed.Proposals.Single(proposal => proposal.DisplayName == "Notes class").HasMatchedTableRow
            && proposed.Proposals.Single(proposal => proposal.DisplayName == "Notes class").Rules.Count == 0);
        Check("Z16a5 missing expected provenance stays visible and uncertain without an invented row", sourceless.IsUncertain && sourceless.SourceRows.Count == 0
            && sourceless.Reason.Contains("no source row", StringComparison.Ordinal));
        Check("Z16a5 ordinal family keys and unknown row keys never acquire a guessed family", proposed.Proposals.Single(proposal => proposal.DisplayName == "Case class").FamilyKey == ""
            && proposed.Proposals.Single(proposal => proposal.DisplayName == "Unplaced class").FamilyKey == "");
        var unnamed = proposed.Proposals.Single(proposal => proposal.SourceRows.Any(row => row.SheetName == table.SheetName && row.RowNumber == 6));
        Check("Z16a5 an empty class name retains its associated family and required rule", unnamed is { FamilyKey: "F1", DisplayName: "", IsUncertain: true }
            && unnamed.Rules is [{ Accession: "PF01234", Kind: PanelSubclassRuleKind.RequiredPfam }]
            && unnamed.Reason.Contains("subclass name is empty", StringComparison.Ordinal)
            && !unnamed.Reason.Contains("choose one shown family key", StringComparison.OrdinalIgnoreCase));
        Check("Z16a5 every source row and empty source remains visible", tables.All(source => source.Table.Rows.All(row => proposed.Proposals.Any(proposal => proposal.SourceRows
            .Any(origin => origin.SheetName == source.SheetName && origin.RowNumber == row.RowNumber && row.Cells.All(cell => origin.Cells[cell.Key] == cell.Value)))))
            && proposed.Proposals.Any(proposal => proposal.Notes.Any(note => note.Name == "Source sheet" && note.Text == "Empty source")));
        Check("Z16a5 name plus parenthesized accession proposes one rule with the correct polarity", first.Rules.Count == 3
            && first.Rules.Single(rule => rule.Accession == "PF01234").Kind == PanelSubclassRuleKind.RequiredPfam
            && first.Rules.Single(rule => rule.Accession == "PF09090").Kind == PanelSubclassRuleKind.AbsentPfam);
        Check("Z16a5 proposing never confirms or mutates its inputs", sourceBefore == JsonSerializer.Serialize(new { tables, expected, vocabulary })
            && PanelSubclassImport.BuildDefinitions(proposed.Proposals, Array.Empty<PanelSubclassAcceptance>(), keys) is { CanBuild: true, Definitions.Count: 0 });
        var built = PanelSubclassImport.BuildDefinitions(proposed.Proposals, new[] { ConfirmSubclass(first) }, keys);
        Check("Z16a5 explicit choices return canonical accessions only", built.CanBuild && built.Definitions.Count == 1
            && built.Definitions[0].Definition.RequiredPfam.SequenceEqual(new[] { "PF01234", "PF05678" })
            && built.Definitions[0].Definition.AbsentPfam.SequenceEqual(new[] { "PF09090" }));
        var definition = built.Definitions.Single().Definition;
        Check("Z16a5 a definition with executable rules keeps every raw source cell including unrecognized text", first.SourceRows.All(row => row.Cells.All(cell =>
            definition.TextFields.Any(field => field.Text == cell.Value))) && definition.TextFields.Any(field => field.Text == architecture));
        var family = new FamilyPanelFamily { Key = "F1", Subclasses = new() { definition } };
        ProteinEvidence Facts(bool? fragment, params string[] domains) => new() { Fragment = fragment,
            Pfam = domains.Select(accession => new EvidenceDomain { Accession = accession, Name = "Display text" }).ToList() };
        Check("Z16a5 the real subclass engine executes the confirmed imported definition", SubclassAssignment.ClassifyDirect(family, Facts(false, "PF01234", "PF05678"))
            is { Status: ProteinSubclassStatus.Assigned, SubclassKey: "First class" });
        Check("Z16a5 missing domains, fragments and unknown completeness cannot establish absence", new[] { Facts(false), Facts(true, "PF01234", "PF05678"),
            Facts(null, "PF01234", "PF05678"), Facts(false, "PF01234", "PF05678", "PF09090") }
            .All(facts => SubclassAssignment.ClassifyDirect(family, facts).Status == ProteinSubclassStatus.Unknown));
        Check("Z16a5 canonical accession case is retained for the engine's ordinal match", SubclassAssignment.ClassifyDirect(family, Facts(false, "pf01234", "PF05678")).Status == ProteinSubclassStatus.Unknown);
        var omitted = ConfirmSubclass(first); omitted.Rules.Clear();
        var noRules = PanelSubclassImport.BuildDefinitions(proposed.Proposals, new[] { omitted }, keys);
        Check("Z16a5 omitted rule choices retain text but cannot execute", noRules.CanBuild && noRules.Definitions.Single().Definition.RequiredPfam.Count == 0
            && noRules.Definitions.Single().Definition.AbsentPfam.Count == 0 && SubclassAssignment.ClassifyDirect(new FamilyPanelFamily
                { Key = "F1", Subclasses = new() { noRules.Definitions.Single().Definition } }, Facts(false, "PF01234", "PF05678")).Status == ProteinSubclassStatus.Unknown);
        var rejected = ConfirmSubclass(first); rejected.Accepted = false;
        Check("Z16a5 a false parent choice accepts nothing", PanelSubclassImport.BuildDefinitions(proposed.Proposals, new[] { rejected }, keys) is { CanBuild: true, Definitions.Count: 0 });

        void Refusal(string label, IReadOnlyList<PanelSubclassProposal> proposals, params PanelSubclassAcceptance[] choices)
        {
            var invalid = PanelSubclassImport.BuildDefinitions(proposals, choices, keys);
            Check("Z16a5 " + label + " returns item problems and no partial definitions", !invalid.CanBuild && invalid.Definitions.Count == 0
                && invalid.Problems.Count > 0 && invalid.Problems.All(problem => problem.ProposalId.Length > 0 && problem.Message.Length > 0));
        }
        var orphan = ConfirmSubclass(first); orphan.FamilyKey = "F9"; Refusal("stale family key", proposed.Proposals, orphan);
        var unplaced = proposed.Proposals.Single(proposal => proposal.DisplayName == "Unplaced class");
        var emptyTarget = PanelSubclassImport.BuildDefinitions(proposed.Proposals, new[] { ConfirmSubclass(unplaced) }, new[] { "", "F1" });
        Check("Z16a5 an empty target key cannot become a resolved family", !emptyTarget.CanBuild && emptyTarget.Definitions.Count == 0);
        var placedChoice = ConfirmSubclass(unplaced); placedChoice.FamilyKey = "F1"; placedChoice.Key = "reviewed"; placedChoice.DisplayName = "Reviewed class";
        var placed = PanelSubclassImport.BuildDefinitions(proposed.Proposals, new[] { placedChoice }, keys);
        Check("Z16a5 explicit review can place and rename a sourced unplaced row", placed.CanBuild && placed.Definitions.Single() is
            { FamilyKey: "F1", Definition.Key: "reviewed", Definition.DisplayName: "Reviewed class" }
            && placed.Definitions.Single().Definition.TextFields.Any(field => field.Text == "F9"));
        var editedPolarity = ConfirmSubclass(first);
        editedPolarity.Rules.Single(choice => choice.RuleId == first.Rules.Single(rule => rule.Accession == "PF09090").RuleId).Kind = PanelSubclassRuleKind.RequiredPfam;
        var edited = PanelSubclassImport.BuildDefinitions(proposed.Proposals, new[] { editedPolarity }, keys);
        Check("Z16a5 explicit polarity edits change only the selected canonical rule", edited.CanBuild && edited.Definitions.Single().Definition.AbsentPfam.Count == 0
            && edited.Definitions.Single().Definition.RequiredPfam.SequenceEqual(new[] { "PF01234", "PF05678", "PF09090" }));
        var unknownRule = ConfirmSubclass(first); unknownRule.Rules.Add(new() { RuleId = "old-rule", Accepted = true }); Refusal("unknown rule", proposed.Proposals, unknownRule);
        Refusal("unknown proposal", proposed.Proposals, ConfirmSubclass(first), new() { ProposalId = "old-proposal", Accepted = true });
        Refusal("duplicate selection", proposed.Proposals, ConfirmSubclass(first), ConfirmSubclass(first));
        var second = proposed.Proposals.Single(proposal => proposal.DisplayName == "Notes class");
        var duplicateKey = ConfirmSubclass(second); duplicateKey.FamilyKey = "F1"; duplicateKey.Key = first.Key;
        Refusal("duplicate subclass key", proposed.Proposals, ConfirmSubclass(first), duplicateKey);
        var polarity = ConfirmSubclass(first); polarity.Rules[0].Kind = (PanelSubclassRuleKind)99; Refusal("invalid polarity", proposed.Proposals, polarity);
        var badIds = JsonSerializer.Deserialize<List<PanelSubclassProposal>>(JsonSerializer.Serialize(proposed.Proposals))!;
        badIds[1].ProposalId = badIds[0].ProposalId; Refusal("duplicate proposed identifier", badIds, ConfirmSubclass(first));

        var markerTable = new PanelWorkbookSubclassTable { SheetName = "F1 rules", FamilyKeys = new() { "F1" }, ClassColumn = "Class", DomainColumns = new() { "Architecture" } };
        string[] markers = { "without", "no", "not", "sem", "sin", "sans", "ohne", "-", "+", "" };
        for (int index = 0; index < markers.Length; index++) markerTable.Table.Rows.Add(Row(index + 2, "", "Marker " + index.ToString(CultureInfo.InvariantCulture), markers[index] + " Anchor domain"));
        var markerProposals = PanelSubclassImport.Propose(new[] { markerTable }, keys, vocabulary, Array.Empty<PanelSubclassExpectedName>()).Proposals;
        Check("Z16a5 the authorized absence markers and plus/plain listings propose their visible polarities", markerProposals.Select((proposal, index) =>
            proposal.Rules.Single().Kind == (index < 8 ? PanelSubclassRuleKind.AbsentPfam : PanelSubclassRuleKind.RequiredPfam)).All(value => value));
        markerTable.Table.Rows = new() { Row(2, "", "Paired", "without Anchor domain (PF01234)") };
        var paired = PanelSubclassImport.Propose(new[] { markerTable }, keys, vocabulary, Array.Empty<PanelSubclassExpectedName>()).Proposals.Single();
        Check("Z16a5 a parenthesized canonical accession keeps the preceding absence marker", paired.Rules is [{ Accession: "PF01234", Kind: PanelSubclassRuleKind.AbsentPfam }]);
        markerTable.FamilyKeys.Add("F2");
        var multipleFamilies = PanelSubclassImport.Propose(new[] { markerTable }, keys, vocabulary, Array.Empty<PanelSubclassExpectedName>()).Proposals.Single();
        Check("Z16a5 a table naming several families cannot silently choose one", multipleFamilies is { FamilyKey: "", IsUncertain: true, CandidateFamilyKeys.Count: 2 });
        markerTable.FamilyKeys.Remove("F2");
        markerTable.Table.Rows = new() { Row(2, "", "First class", "Anchor domain"), Row(3, "", "First class", "Tail domain") };
        var repeatedArchitecture = PanelSubclassImport.Propose(new[] { markerTable }, keys, vocabulary, expected).Proposals.Single(proposal => proposal.ExpectedNameId == "first");
        Check("Z16a5 multiple architecture rows keep one expected proposal with uncertainty and all sources", repeatedArchitecture.IsUncertain
            && repeatedArchitecture.SourceRows.Count == 3 && repeatedArchitecture.Reason.Contains("More than one architecture", StringComparison.Ordinal));
        markerTable.Table.Rows = new() { Row(2, "", "Conflict", "Anchor domain; without PF01234") };
        var conflict = PanelSubclassImport.Propose(new[] { markerTable }, keys, vocabulary, Array.Empty<PanelSubclassExpectedName>()).Proposals.Single();
        Refusal("required/absent conflict", new[] { conflict }, ConfirmSubclass(conflict));
        var ambiguousVocabulary = new List<FamilyPanelDomain> { new() { Accession = "PF01111", Name = "Shared name" }, new() { Accession = "PF02222", Name = "Shared name" } };
        markerTable.Table.Rows = new() { Row(2, "", "Ambiguous", "Shared name") };
        var ambiguous = PanelSubclassImport.Propose(new[] { markerTable }, keys, ambiguousVocabulary, Array.Empty<PanelSubclassExpectedName>()).Proposals.Single();
        Check("Z16a5 an ambiguous Pfam name stays visible without an accession", ambiguous.IsUncertain && ambiguous.Rules.Single() is { Accession: "", IsUncertain: true, CandidateAccessions.Count: 2 });
        Refusal("unresolved domain alongside a valid definition", new[] { first, ambiguous }, ConfirmSubclass(first), ConfirmSubclass(ambiguous));
        var ambiguousResult = PanelSubclassImport.BuildDefinitions(new[] { ambiguous }, new[] { ConfirmSubclass(ambiguous) }, keys);
        Check("Z16a5 an unresolved rule problem names the precise proposal and rule", ambiguousResult.Problems.Any(problem => problem.ProposalId == ambiguous.ProposalId && problem.RuleId == ambiguous.Rules.Single().RuleId));
        var unresolvedCopy = JsonSerializer.Deserialize<PanelSubclassProposal>(JsonSerializer.Serialize(ambiguous))!;
        unresolvedCopy.Rules.Single().Accession = "PF01111";
        Refusal("a still-ambiguous vocabulary choice", new[] { unresolvedCopy }, ConfirmSubclass(unresolvedCopy));

        var panel = new FamilyPanel { Families = new() { family, new() { Key = "F2" } } };
        FamilyPanelValidation.Validate(panel);
        string project = Path.Combine(output, "confirmed.fqproj");
        ProjectStore.WriteFqproj(project, new FamilyClusterFile { FamilyPanel = panel }, Array.Empty<QuantRow>(), new QuantManifest());
        Check("Z16a5 confirmed rules and verbatim notes survive actual project reopen", JsonSerializer.Serialize(ProjectStore.ReadFqproj(project).fcf.FamilyPanel) == JsonSerializer.Serialize(panel));
        string originalNote = first.Notes[0].Text;
        definition.TextFields[0].Text = "Changed detached result";
        first.SourceRows[0].Cells["Expected"] = "Changed detached proposal";
        Check("Z16a5 returned proposals and definitions do not alias source cells or notes", familySource.Cells["Expected"] == "First class; Missing class" && first.Notes[0].Text == originalNote);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); string one = JsonSerializer.Serialize(PanelSubclassImport.Propose(tables, keys, vocabulary, expected));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("Z16a5 proposal identifiers and text are culture invariant", one == JsonSerializer.Serialize(PanelSubclassImport.Propose(tables, keys, vocabulary, expected)));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        PanelSubclassWorkbookRoute(output, vocabulary);
        File.WriteAllText(Path.Combine(output, "proposals.json"), JsonSerializer.Serialize(proposed, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void PanelSubclassWorkbookRoute(string output, IReadOnlyList<FamilyPanelDomain> vocabulary)
    {
        string workbook = Path.Combine(output, "synthetic-workbook.xlsx");
        WriteNeutralWorkbook(workbook);
        string hash = FileFingerprint.Sha256(workbook);
        var importer = new FamilyPanelSpreadsheetImporter();
        var map = importer.ProposeWorkbook(workbook);
        var table = map.FamilyTable!; var choices = map.FamilyProposal!.Choices;
        string keyColumn = choices.ColumnHeaders[PanelImportFields.FamilyKey];
        var keys = table.Rows.Select(row => row.Cells[keyColumn].Trim()).Distinct(StringComparer.Ordinal).ToArray();
        // Explicitly choose the fixture's decision column, leaving its labels unconfirmed.
        choices.ColumnHeaders[PanelImportFields.Decision] = table.Headers[5];
        var unresolved = importer.Check(table, choices, Array.Empty<FamilyPanelDecisionMapping>());
        var proposed = PanelSubclassImport.Propose(map.SubclassTables, keys, vocabulary, Array.Empty<PanelSubclassExpectedName>());
        Check("Z16a5 real workbook reader/map proposes subclasses before a checked panel exists", unresolved.Panel == null && proposed.Proposals.Count == 9
            && proposed.Proposals.All(proposal => proposal.FamilyKey == "F07" && proposal.Rules.Count == 2));
        string decisionColumn = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Decision, "");
        var explicitDecisions = table.Rows.Select(row => row.Cells.GetValueOrDefault(decisionColumn, "")).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal)
            .Select(value => new FamilyPanelDecisionMapping { SourceValue = value, Decision = FamilyPanelDecision.Quantify }).ToArray();
        var checkedColors = importer.Check(table, choices, explicitDecisions);
        choices.ColorLabels = checkedColors.Colors.Select((value, index) => new FamilyPanelColorLabel { Color = value.Color, Label = "Category " + index.ToString(CultureInfo.InvariantCulture) }).ToList();
        var checkedFamilies = importer.Check(table, choices, explicitDecisions);
        Check("Z16a5 explicit family-list choices produce the detached target panel", checkedFamilies.CanImport);
        if (checkedFamilies.Panel == null) return;
        var selected = proposed.Proposals[0];
        var definitions = PanelSubclassImport.BuildDefinitions(proposed.Proposals, new[] { ConfirmSubclass(selected) }, checkedFamilies.Panel.Families.Select(family => family.Key).ToArray());
        foreach (var definition in definitions.Definitions) checkedFamilies.Panel.Families.Single(family => family.Key == definition.FamilyKey).Subclasses.Add(definition.Definition);
        var target = checkedFamilies.Panel.Families.Single(family => family.Key == "F07");
        Check("Z16a5 imported real-table rules execute in the native subclass engine", definitions.CanBuild && SubclassAssignment.ClassifyDirect(target,
            new ProteinEvidence { Fragment = false, Pfam = new() { new() { Accession = "PF01234" }, new() { Accession = "PF05678" } } }).SubclassKey == selected.Key);
        Check("Z16a5 interpreting and confirming never changes input workbook bytes", FileFingerprint.Sha256(workbook) == hash);
    }
}
