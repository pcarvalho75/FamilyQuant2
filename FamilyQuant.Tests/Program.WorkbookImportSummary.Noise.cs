using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void WorkbookSummaryNoiseTests(string root)
    {
        string path = Path.Combine(root, "summary-noise.xlsx");
        WriteNeutralWorkbook(path, onlyFamily: true);
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var book = document.WorkbookPart!;
            var sheet = book.Workbook!.Sheets!.Elements<XL.Sheet>().Single(sheet => sheet.Name == "Data 9");
            var part = (WorksheetPart)book.GetPartById(sheet.Id!);
            var cells = part.Worksheet!.Descendants<XL.Cell>().ToDictionary(cell => cell.CellReference!.Value!, StringComparer.Ordinal);
            for (int row = 2; row <= 41; row++) cells["M" + row].InlineString = new XL.InlineString(new XL.Text("-"));
            foreach (var pair in new Dictionary<string, string>
            {
                ["M3"] = "...", ["M4"] = "?", ["M5"] = "A (A1, A2); B [B1; B2]", ["M6"] = "C, D",
                ["M7"] = "E (E1 [E2, E3], E4)", ["M8"] = "F\nG"
            }) cells[pair.Key].InlineString = new XL.InlineString(new XL.Text(pair.Value));
            part.Worksheet.Save();
        }
        byte[] workbookBefore = File.ReadAllBytes(path);
        var importer = new FamilyPanelSpreadsheetImporter();
        var source = ReviewSummaryFixture(importer.ProposeWorkbook(path), importer);
        string sourceBefore = JsonSerializer.Serialize(source);
        var stored = new FamilyClusterFile(); string storedBefore = stored.ToJsonText();
        var matching = new PanelEvidenceMatching(stored);
        var evidence = new Dictionary<string, IReadOnlyList<PanelEvidenceChoice>>();
        var complete = WorkbookImportSummaries.Build(source, matching, reviewedEvidence: evidence);
        var names = complete.ExpectedSubclassNames;
        Check("Z16a9d punctuation-only placeholder cells add no expected subclass names", names.All(name => name.DisplayName.Any(char.IsLetterOrDigit)));
        Check("Z16a9d only top-level separators split names and bracketed text stays whole", names.Select(name => name.DisplayName).SequenceEqual(new[]
            { "A (A1, A2)", "B [B1; B2]", "C", "D", "E (E1 [E2, E3], E4)", "F", "G" }));
        Check("Z16a9d the review has exactly one finding per real expected name", complete.SubclassProposals.Count == 7
            && complete.Sections.Single(section => section.Kind == WorkbookImportSectionKind.Subclasses).Items.Count == 7);
        Check("Z16a9d expected names retain their exact source row and unsplit cell text", names.All(name => name.SourceRows.Count == 1
            && name.SourceRows[0].SheetName == source.FamilySheet
            && name.SourceRows[0].Cells["h13"] == source.FamilyTable!.Rows.Single(row => row.RowNumber == name.SourceRows[0].RowNumber).Cells["h13"]));

        var pending = SummaryCopy(source);
        var labels = pending.Decisions.Take(2).Select(hint => hint.SourceValue).ToArray();
        foreach (var hint in pending.Decisions.Where(hint => labels.Contains(hint.SourceValue, StringComparer.Ordinal))) hint.ProposedMapping = null;
        string pendingBefore = JsonSerializer.Serialize(pending);
        int ReviewCount(WorkbookImportSummary summary) => summary.Sections.SelectMany(section => section.Items)
            .Count(item => item.State is WorkbookImportItemState.Uncertain or WorkbookImportItemState.CouldNotPlace);
        var summary = WorkbookImportSummaries.Build(pending, matching, reviewedEvidence: evidence);
        var decisions = summary.Sections.Single(section => section.Kind == WorkbookImportSectionKind.Decisions).Items;
        Check("Z16a9d repeated rows share one unresolved finding for each decision label", labels.Length == 2
            && labels.All(label => decisions.Count(item => item.Label == label && item.Required && item.State == WorkbookImportItemState.Uncertain) == 1));
        Check("Z16a9d unmapped decisions are not counted again under family rows", summary.Sections.Single(section => section.Kind == WorkbookImportSectionKind.FamilyRows)
            .Items.All(item => item.Page != WorkbookImportPage.Decisions) && ReviewCount(summary) == ReviewCount(complete) + 2);
        var duplicate = SummaryCopy(pending);
        string decisionColumn = duplicate.FamilyProposal.Choices.ColumnHeaders[PanelImportFields.Decision];
        int decisionRow = duplicate.FamilyTable!.Rows.First(row => row.Cells[decisionColumn] == labels[0]).RowNumber;
        duplicate.Problems.Add(new() { Kind = PanelImportProblemKind.DecisionMappingInvalid, Severity = PanelImportSeverity.Blocking,
            RowNumber = decisionRow, Column = decisionColumn, Value = labels[0], SheetName = duplicate.FamilySheet, Message = "Review this unresolved label." });
        var duplicateSummary = WorkbookImportSummaries.Build(duplicate, matching, reviewedEvidence: evidence);
        Check("Z16a9d invalid mapping for the same unresolved label is counted once", duplicateSummary.Sections.Single(section => section.Kind == WorkbookImportSectionKind.FamilyRows)
            .Items.All(item => item.Page != WorkbookImportPage.Decisions) && ReviewCount(duplicateSummary) == ReviewCount(complete) + 2);
        var other = SummaryCopy(pending);
        other.Problems.Add(new() { Kind = PanelImportProblemKind.DecisionMappingInvalid, Severity = PanelImportSeverity.Blocking,
            Column = decisionColumn, Message = "An unrepresented global mapping problem." });
        other.Problems.Add(new() { Kind = PanelImportProblemKind.MissingName, Severity = PanelImportSeverity.Blocking,
            RowNumber = decisionRow, Column = "h02", Value = labels[0], Message = "An independent family row problem." });
        var otherSummary = WorkbookImportSummaries.Build(other, matching, reviewedEvidence: evidence);
        Check("Z16a9d genuine global and non-decision importer problems remain visible and blocking", otherSummary.HasBlockingItems
            && otherSummary.Sections.SelectMany(section => section.Items).Count(item => item.Required
                && item.State == WorkbookImportItemState.CouldNotPlace && (item.Reason == "An unrepresented global mapping problem."
                    || item.Reason == "An independent family row problem.")) == 2);
        Check("Z16a9d showing an unresolved decision once still refuses import", summary.HasBlockingItems
            && !WorkbookImportSummaries.AcceptAll(summary, importer, summary.ReviewedSubclassSelections, evidence).CanImport);
        Check("Z16a9d preparing a quieter summary preserves inputs and workbook bytes", JsonSerializer.Serialize(source) == sourceBefore
            && JsonSerializer.Serialize(pending) == pendingBefore && stored.ToJsonText() == storedBefore && File.ReadAllBytes(path).SequenceEqual(workbookBefore));
    }
}
