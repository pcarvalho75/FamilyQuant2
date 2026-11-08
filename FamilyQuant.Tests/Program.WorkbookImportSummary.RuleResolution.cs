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
    private static void WorkbookSummaryRuleResolutionTests(string root)
    {
        string path = System.IO.Path.Combine(root, "explicit-rule-kind.xlsx");
        WriteNeutralWorkbook(path, includeLegend: false, includeDecision: false);
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var book = document.WorkbookPart!;
            var sheet = book.Workbook!.Sheets!.Elements<XL.Sheet>().Single(sheet => sheet.Name == "Data 3");
            var part = (WorksheetPart)book.GetPartById(sheet.Id!);
            var cells = part.Worksheet!.Descendants<XL.Cell>().ToDictionary(cell => cell.CellReference!.Value!, StringComparer.Ordinal);
            cells["C1"].InlineString = new XL.InlineString(new XL.Text("Absent Pfam"));
            cells["C2"].InlineString = new XL.InlineString(new XL.Text("+ DomainOne (PF01234)"));
            cells["D2"].InlineString = new XL.InlineString(new XL.Text("Shared domain"));
            part.Worksheet.Save();
        }
        var stored = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "tr|Q79001|invented", Sequence = "ACDEFGHIK",
            Evidence = new() { Pfam = new() { new() { Accession = "PF01234", Name = "DomainOne" },
                new() { Accession = "PF05678", Name = "Shared domain" }, new() { Accession = "PF09090", Name = "Shared domain" } } } } } };
        string storedBefore = stored.ToJsonText();
        var matching = new PanelEvidenceMatching(stored); var importer = new FamilyPanelSpreadsheetImporter();
        var source = ReviewSummaryFixture(importer.ProposeWorkbook(path), importer);
        string sourceBefore = JsonSerializer.Serialize(source);
        var evidence = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>>();
        var initial = WorkbookImportSummaries.Build(source, matching, reviewedEvidence: evidence);
        var target = initial.SubclassProposals.Single(proposal => proposal.SourceRows.Any(row => row.SheetName == "Data 3" && row.RowNumber == 2));
        var canonical = target.Rules.Single(rule => rule.Accession == "PF01234");
        var unresolved = target.Rules.Single(rule => rule.CandidateAccessions.Count == 2);
        Check("Z16a9c synthetic conflicting polarity starts uncertain with a unique canonical accession", canonical.IsUncertain
            && canonical.CandidateAccessions.SequenceEqual(new[] { "PF01234" }) && unresolved.IsUncertain && unresolved.Accession.Length == 0);
        PanelSubclassAcceptance Choice(PanelSubclassRuleKind? kind, bool selected = true, string? ruleId = null) => new()
        {
            ProposalId = target.ProposalId, Accepted = true, FamilyKey = "F07", Key = "reviewed-rule", DisplayName = "Reviewed rule",
            Rules = new() { new() { RuleId = ruleId ?? canonical.RuleId, Accepted = selected, Kind = kind } }
        };
        foreach (var kind in new[] { PanelSubclassRuleKind.RequiredPfam, PanelSubclassRuleKind.AbsentPfam })
        {
            var choices = new[] { Choice(kind) };
            var summary = WorkbookImportSummaries.Build(source, matching, choices, evidence);
            var result = WorkbookImportSummaries.AcceptAll(summary, importer, choices, evidence);
            var definition = result.Panel?.Families.Single(family => family.Key == "F07").Subclasses.SingleOrDefault();
            Check("Z16a9c explicit selected " + kind + " resolves polarity and imports exactly that rule", !summary.HasBlockingItems && result.CanImport
                && definition != null && definition.RequiredPfam.SequenceEqual(kind == PanelSubclassRuleKind.RequiredPfam ? new[] { canonical.Accession } : Array.Empty<string>())
                && definition.AbsentPfam.SequenceEqual(kind == PanelSubclassRuleKind.AbsentPfam ? new[] { canonical.Accession } : Array.Empty<string>()));
        }
        var unset = new[] { Choice(null) };
        var unsetSummary = WorkbookImportSummaries.Build(source, matching, unset, evidence);
        var unsetResult = WorkbookImportSummaries.AcceptAll(unsetSummary, importer, unset, evidence);
        Check("Z16a9c selected uncertain polarity left unset blocks with its source reason", unsetSummary.HasBlockingItems && !unsetResult.CanImport
            && unsetResult.Items.SelectMany(item => item.Problems).Any(problem => problem.Blocking && problem.Message.Contains(canonical.Reason, StringComparison.Ordinal)));
        var ambiguous = new[] { Choice(PanelSubclassRuleKind.RequiredPfam, ruleId: unresolved.RuleId) };
        var ambiguousSummary = WorkbookImportSummaries.Build(source, matching, ambiguous, evidence);
        Check("Z16a9c explicit kind cannot resolve an ambiguous accession", ambiguousSummary.HasBlockingItems
            && !WorkbookImportSummaries.AcceptAll(ambiguousSummary, importer, ambiguous, evidence).CanImport);
        var invalid = new[] { Choice((PanelSubclassRuleKind)99) };
        var invalidSummary = WorkbookImportSummaries.Build(source, matching, invalid, evidence);
        Check("Z16a9c an undefined selected kind still blocks", invalidSummary.HasBlockingItems
            && !WorkbookImportSummaries.AcceptAll(invalidSummary, importer, invalid, evidence).CanImport);
        var unselected = new[] { Choice(PanelSubclassRuleKind.RequiredPfam, selected: false) };
        var unselectedSummary = WorkbookImportSummaries.Build(source, matching, unselected, evidence);
        var unselectedResult = WorkbookImportSummaries.AcceptAll(unselectedSummary, importer, unselected, evidence);
        var notesOnly = unselectedResult.Panel?.Families.Single(family => family.Key == "F07").Subclasses.SingleOrDefault();
        Check("Z16a9c setting a kind without selecting the rule never executes it", unselectedResult.CanImport && notesOnly != null
            && notesOnly.RequiredPfam.Count == 0 && notesOnly.AbsentPfam.Count == 0);
        var draft = initial.DraftSubclassSelections.Single(choice => choice.ProposalId == target.ProposalId);
        Check("Z16a9c uncertain source rules stay unselected in the default draft", !draft.Accepted
            && !draft.Rules.Single(rule => rule.RuleId == canonical.RuleId).Accepted);
        Check("Z16a9c explicit review never mutates source uncertainty or stored data", JsonSerializer.Serialize(source) == sourceBefore
            && initial.SubclassProposals.Single(proposal => proposal.ProposalId == target.ProposalId).Rules.Single(rule => rule.RuleId == canonical.RuleId).IsUncertain
            && stored.ToJsonText() == storedBefore);
    }
}
