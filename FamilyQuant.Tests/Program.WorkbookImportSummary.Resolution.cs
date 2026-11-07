using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void WorkbookSummaryResolutionTests(string root)
    {
        string path = System.IO.Path.Combine(root, "explicit-resolution.xlsx");
        WriteNeutralWorkbook(path, includeLegend: false, includeDecision: false);
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var book = document.WorkbookPart!;
            var sheet = book.Workbook!.Sheets!.Elements<XL.Sheet>().Single(sheet => sheet.Name == "Data 3");
            var part = (WorksheetPart)book.GetPartById(sheet.Id!);
            var cells = part.Worksheet!.Descendants<XL.Cell>().ToDictionary(cell => cell.CellReference!.Value!, StringComparer.Ordinal);
            cells["C2"].InlineString = new XL.InlineString(new XL.Text("DomainOne (PF01234); unparsed material"));
            cells["D2"].InlineString = new XL.InlineString(new XL.Text("Shared domain"));
            part.Worksheet.Save();
        }
        var stored = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "tr|Q78001|invented", Sequence = "ACDEFGHIK",
            Evidence = new() { Pfam = new() { new() { Accession = "PF01234", Name = "DomainOne" },
                new() { Accession = "PF05678", Name = "Shared domain" }, new() { Accession = "PF09090", Name = "Shared domain" } } } } } };
        string before = stored.ToJsonText();
        var matching = new PanelEvidenceMatching(stored); var importer = new FamilyPanelSpreadsheetImporter();
        var proposal = ReviewSummaryFixture(importer.ProposeWorkbook(path), importer);
        string proposalBefore = JsonSerializer.Serialize(proposal);
        var evidence = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>>();
        var initial = WorkbookImportSummaries.Build(proposal, matching, reviewedEvidence: evidence);
        var target = initial.SubclassProposals.Single(item => item.SourceRows.Any(row => row.SheetName == "Data 3" && row.RowNumber == 2));
        var certain = target.Rules.Single(rule => rule.Accession == "PF01234");
        var uncertain = target.Rules.Single(rule => rule.IsUncertain && rule.CandidateAccessions.Count == 2);
        Check("Z16a9 an initially uncertain class stays unticked with no confirmed definition", target.IsUncertain
            && !initial.DraftSubclassSelections.Single(choice => choice.ProposalId == target.ProposalId).Accepted && stored.FamilyPanel == null);
        PanelSubclassAcceptance Choice(bool rules) => new() { ProposalId = target.ProposalId, Accepted = true,
            FamilyKey = "F08", Key = "resolved-class", DisplayName = "Resolved source class", Rules = target.Rules.Select(rule =>
                new PanelSubclassRuleAcceptance { RuleId = rule.RuleId, Accepted = rules && !rule.IsUncertain, Kind = rule.Kind }).ToList() };
        var selected = new[] { Choice(true) };
        var summary = WorkbookImportSummaries.Build(proposal, matching, selected, evidence);
        var accepted = WorkbookImportSummaries.AcceptAll(summary, importer, selected, evidence);
        var family = accepted.Panel?.Families.Single(item => item.Key == "F08");
        var definition = family?.Subclasses.SingleOrDefault();
        Check("Z16a9 explicit valid family key key and name resolve the uncertain class", !summary.HasBlockingItems && accepted.CanImport
            && definition is { Key: "resolved-class", DisplayName: "Resolved source class" });
        Check("Z16a9 resolving a class executes only its selected certain canonical rule", definition != null
            && definition.RequiredPfam.SequenceEqual(new[] { certain.Accession }) && definition.AbsentPfam.Count == 0
            && SubclassAssignment.ClassifyDirect(family!, new ProteinEvidence { Fragment = false, Pfam = new() { new() { Accession = "PF01234" } } }).Status == ProteinSubclassStatus.Assigned);
        Check("Z16a9 unresolved rules stay unaccepted after the class is resolved", definition != null
            && !definition.RequiredPfam.Concat(definition.AbsentPfam).Any(uncertain.CandidateAccessions.Contains)
            && summary.Sections.SelectMany(section => section.Items).Where(item => item.State == WorkbookImportItemState.Uncertain)
                .All(item => !accepted.Items.Single(result => result.ItemId == item.Id).Accepted));
        var invalidRule = Choice(true); invalidRule.Rules.Single(rule => rule.RuleId == uncertain.RuleId).Accepted = true;
        var ruleSummary = WorkbookImportSummaries.Build(proposal, matching, new[] { invalidRule }, evidence);
        Check("Z16a9 an explicitly selected uncertain rule still refuses all definitions", !WorkbookImportSummaries.AcceptAll(ruleSummary, importer,
            new[] { invalidRule }, evidence).CanImport);
        var incomplete = Choice(true); incomplete.Key = "";
        var incompleteSummary = WorkbookImportSummaries.Build(proposal, matching, new[] { incomplete }, evidence);
        Check("Z16a9 an incomplete explicit resolution remains blocked", !WorkbookImportSummaries.AcceptAll(incompleteSummary, importer, new[] { incomplete }, evidence).CanImport);
        var wrongFamily = Choice(true); wrongFamily.FamilyKey = "not-in-summary";
        var wrongSummary = WorkbookImportSummaries.Build(proposal, matching, new[] { wrongFamily }, evidence);
        Check("Z16a9 explicit resolution still requires a family shown in the summary", !WorkbookImportSummaries.AcceptAll(wrongSummary, importer,
            new[] { wrongFamily }, evidence).CanImport);
        var notesOnly = Choice(false); notesOnly.Key = "notes-only"; notesOnly.DisplayName = "Source notes class"; notesOnly.Rules.Clear();
        var notesSummary = WorkbookImportSummaries.Build(proposal, matching, new[] { notesOnly }, evidence);
        var notesResult = WorkbookImportSummaries.AcceptAll(notesSummary, importer, new[] { notesOnly }, evidence);
        var notesFamily = notesResult.Panel?.Families.Single(item => item.Key == "F08");
        var notesDefinition = notesFamily?.Subclasses.SingleOrDefault();
        Check("Z16a9 explicit notes-only acceptance is a legal detached definition", notesResult.CanImport && notesDefinition is { Key: "notes-only" }
            && notesDefinition.TextFields.Any(field => field.Text.Contains("unparsed material", StringComparison.Ordinal)));
        Check("Z16a9 notes-only definitions never execute as canonical rules", notesDefinition != null && notesDefinition.RequiredPfam.Count == 0
            && notesDefinition.AbsentPfam.Count == 0 && notesDefinition.UniProtSubfamilies.Count == 0
            && SubclassAssignment.ClassifyDirect(notesFamily!, new ProteinEvidence { Fragment = false, Pfam = new() { new() { Accession = "PF01234" } } }).Status == ProteinSubclassStatus.Unknown);
        Check("Z16a9 explicit resolution preserves immutable source uncertainty and input text", summary.SubclassProposals.Single(item => item.ProposalId == target.ProposalId).IsUncertain
            && JsonSerializer.Serialize(proposal) == proposalBefore && stored.ToJsonText() == before);
    }
}
