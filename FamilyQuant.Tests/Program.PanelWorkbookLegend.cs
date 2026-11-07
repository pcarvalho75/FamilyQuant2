using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelWorkbookLegendCommands() =>
    [new("--test-panel-workbook-legend", 21602, TestCommandPhase.Raw,
        args => args is ["--test-panel-workbook-legend"], (_, _) =>
        {
            PanelWorkbookLegendTests(Path.Combine(Path.GetTempPath(), "panel-workbook-legend"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void PanelWorkbookLegendTests(string parent)
    {
        Section("Z16a2 source legend and explicit decision uncertainty");
        string root = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        try
        {
            var importer = new FamilyPanelSpreadsheetImporter();
            string shippedPath = Path.Combine(root, "shipped.xlsx"); ExampleFamilyList.Write(shippedPath);
            var shipped = importer.ProposeWorkbook(shippedPath);
            Check("Z16a2 shipped opening call completes both legend slots", shipped.ColorsState == PanelWorkbookReadState.Read
                && shipped.DecisionsState == PanelWorkbookReadState.Read && shipped.Colors.Count > 0 && shipped.Decisions.Count > 0);
            Check("Z16a2 each shipped colour entry keeps its exact source item, meaning and coordinates", shipped.Colors.SelectMany(color => color.Entries).All(entry =>
            {
                var table = shipped.Workbook.Sheets.Single(sheet => sheet.SheetName == entry.SheetName).Table!;
                var row = table.Rows.Single(row => row.RowNumber == entry.RowNumber);
                return entry.Label == row.Cells[table.Headers[0]] && entry.Meaning == row.Cells[table.Headers[1]];
            }));
            string path = Path.Combine(root, "neutral.xlsx"); WriteNeutralWorkbook(path);
            string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var proposal = importer.ProposeWorkbook(path);
            Check("Z16a2 five source fills receive five legend names and meanings", proposal.Colors.Count == 5
                && proposal.Colors.All(color => !color.IsUncertain && color.Name.Length > 0 && color.Meaning.Length > 0 && color.Entries.Count == 1));
            Check("Z16a2 legend matching proposes the decision column by values, not its header", proposal.FamilyProposal.Choices.ColumnHeaders[PanelImportFields.Decision] == "h06"
                && proposal.FamilyProposal.Choices.ColumnHeaders[PanelImportFields.ColorCategory] == proposal.FamilyProposal.Choices.ColumnHeaders[PanelImportFields.FamilyKey]);
            var alpha = proposal.Decisions.Single(decision => decision.SourceValue == "Action alpha");
            var beta = proposal.Decisions.Single(decision => decision.SourceValue == "Action beta");
            var gamma = proposal.Decisions.Single(decision => decision.SourceValue == "Action gamma");
            var unresolved = proposal.Decisions.Single(decision => decision.SourceValue == "Action delta");
            Check("Z16a2 program action words in legend meanings produce detached proposals", alpha.ProposedMapping?.Decision == FamilyPanelDecision.Quantify
                && beta.ProposedMapping?.Decision == FamilyPanelDecision.ReportUnderOthers && gamma.ProposedMapping?.Decision == FamilyPanelDecision.NotAFamily
                && alpha.LegendMeaning == "Quantify" && alpha.RowNumbers.Count > 0);
            Check("Z16a2 an unknown legend meaning stays uncertain without defaulting to Quantify", unresolved.IsUncertain
                && unresolved.ProposedMapping == null && unresolved.LegendMeaning == "An unresolved action");
            var merge = proposal.Decisions.Single(decision => decision.SourceValue == "Merge into F01");
            Check("Z16a2 a program merge retains its target and does not inherit another label from the same fill", merge.ProposedMapping?.Decision == FamilyPanelDecision.MergeIntoAnotherFamily
                && merge.ProposedMapping.MergeTargetFamilyKey == "F01" && merge.LegendEntries.Count == 0);
            var gate = importer.Check(proposal.FamilyTable!, proposal.FamilyProposal.Choices,
                proposal.Decisions.Where(decision => decision.ProposedMapping != null).Select(decision => decision.ProposedMapping!).ToArray());
            Check("Z16a2 incomplete proposals still require an explicit user mapping at the existing import gate", gate.Panel == null
                && gate.Problems.Any(problem => problem.Kind == PanelImportProblemKind.DecisionNotMapped && problem.Value == "Action delta"));
            Check("Z16a2 a single opening proposal does not edit workbook bytes", before == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

            string conflictPath = Path.Combine(root, "conflict.xlsx"); WriteNeutralWorkbook(conflictPath);
            using (var document = SpreadsheetDocument.Open(conflictPath, true))
            {
                var part = NeutralWorkbookPart(document.WorkbookPart!, "Data 2");
                var row = new XL.Row { RowIndex = 7 };
                row.Append(NeutralWorkbookCell("A7", "Action alpha", 1), NeutralWorkbookCell("B7", "Not a family"));
                part.Worksheet!.GetFirstChild<XL.SheetData>()!.Append(row); part.Worksheet.Save();
            }
            var conflict = importer.ProposeWorkbook(conflictPath);
            var conflicting = conflict.Decisions.Single(decision => decision.SourceValue == "Action alpha");
            Check("Z16a2 conflicting legend meanings are all kept and require a choice", conflicting.IsUncertain && conflicting.LegendEntries.Count == 2
                && conflicting.LegendMeaning.Contains("Quantify", StringComparison.Ordinal) && conflicting.LegendMeaning.Contains("Not a family", StringComparison.Ordinal));
            Check("Z16a2 a conflicting colour meaning is not silently chosen from the first row", conflict.Colors.Single(color => color.Entries.Any(entry => entry.Label == "Action alpha")).IsUncertain);

            string ambiguousPath = Path.Combine(root, "columns.xlsx"); WriteNeutralWorkbook(ambiguousPath);
            using (var document = SpreadsheetDocument.Open(ambiguousPath, true))
            {
                var part = NeutralWorkbookPart(document.WorkbookPart!, "Data 9");
                foreach (var row in part.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1))
                {
                    var cells = row.Elements<XL.Cell>().ToArray(); cells[6].InlineString = (XL.InlineString)cells[5].InlineString!.CloneNode(true);
                }
                part.Worksheet.Save();
            }
            var ambiguous = importer.ProposeWorkbook(ambiguousPath);
            Check("Z16a2 equally supported decision columns stay not-read and named for review", ambiguous.DecisionsState == PanelWorkbookReadState.NotRead
                && ambiguous.Decisions.Count == 0 && ambiguous.Problems.Any(problem => problem.Kind == PanelImportProblemKind.ColumnNotChosen
                    && problem.Message.Contains("h06", StringComparison.Ordinal) && problem.Message.Contains("h07", StringComparison.Ordinal)));

            string conditionalPath = Path.Combine(root, "conditional.xlsx"); WriteNeutralWorkbook(conditionalPath);
            using (var document = SpreadsheetDocument.Open(conditionalPath, true))
            {
                var part = NeutralWorkbookPart(document.WorkbookPart!, "Data 2");
                var row = part.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1).First();
                row.Elements<XL.Cell>().ElementAt(1).InlineString = new XL.InlineString(new XL.Text("Quantify only when an additional condition holds")); part.Worksheet.Save();
            }
            Check("Z16a2 conditional prose is not reduced to an unconditional action", importer.ProposeWorkbook(conditionalPath).Decisions
                .Single(decision => decision.SourceValue == "Action alpha").IsUncertain);
            string qualifiedPath = Path.Combine(root, "qualified-action.xlsx"); WriteNeutralWorkbook(qualifiedPath);
            using (var document = SpreadsheetDocument.Open(qualifiedPath, true))
            {
                var legend = NeutralWorkbookPart(document.WorkbookPart!, "Data 2");
                var first = legend.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1).First();
                first.Elements<XL.Cell>().First().InlineString = new XL.InlineString(new XL.Text("Quantify"));
                first.Elements<XL.Cell>().ElementAt(1).InlineString = new XL.InlineString(new XL.Text("Quantify only when an additional condition holds"));
                legend.Worksheet.Save();
                var list = NeutralWorkbookPart(document.WorkbookPart!, "Data 9");
                foreach (var row in list.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1))
                {
                    var cell = row.Elements<XL.Cell>().ElementAt(5);
                    if (cell.InnerText == "Action alpha") cell.InlineString = new XL.InlineString(new XL.Text("Quantify"));
                }
                list.Worksheet.Save();
            }
            Check("Z16a2 an exact action label does not override an unreadable legend qualifier", importer.ProposeWorkbook(qualifiedPath).Decisions
                .Single(decision => decision.SourceValue == "Quantify").IsUncertain);
            string[][] languageMeanings =
            [
                ["Keep and include this entry", "Exclude this entry", "Merge into F01", "Optional: quantify this entry", "Unspecified source description"],
                ["Manter e quantificar esta entrada", "Remover esta entrada", "Juntar com F01", "Opcional: quantificar esta entrada", "Descricao livre da origem"],
                ["Conservar y cuantificar esta entrada", "Eliminar esta entrada", "Unir con F01", "Condicional: cuantificar esta entrada", "Descripcion general de origen"]
            ];
            string[] languageNames = ["English", "Portuguese", "Spanish"];
            for (int language = 0; language < languageMeanings.Length; language++)
            {
                string translated = Path.Combine(root, "language-" + language + ".xlsx"); WriteNeutralWorkbook(translated);
                SetNeutralLegendMeanings(translated, languageMeanings[language]);
                var translatedProposal = importer.ProposeWorkbook(translated);
                var hints = translatedProposal.Decisions.ToDictionary(decision => decision.SourceValue, StringComparer.Ordinal);
                Check("Z16a2 " + languageNames[language] + " general action words propose quantify/exclude/valid-target merge",
                    hints["Action alpha"].ProposedMapping?.Decision == FamilyPanelDecision.Quantify
                    && hints["Action beta"].ProposedMapping?.Decision == FamilyPanelDecision.NotAFamily
                    && hints["Action gamma"].ProposedMapping is { Decision: FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey: "F01" });
                Check("Z16a2 " + languageNames[language] + " optional/conditional and unmatched meanings stay uncertain",
                    hints["Action delta"].IsUncertain && hints["Action epsilon"].IsUncertain);
                Check("Z16a2 " + languageNames[language] + " proposals carry matched words and remain unconfirmed", hints["Action alpha"].Reason.Contains("Matched program words:", StringComparison.Ordinal)
                    && hints["Action gamma"].Reason.Contains("Matched program words:", StringComparison.Ordinal)
                    && !hints["Action alpha"].ExactProgramDecision);
            }
            string invalidMerge = Path.Combine(root, "invalid-merge.xlsx"); WriteNeutralWorkbook(invalidMerge);
            SetNeutralLegendMeanings(invalidMerge, new[] { "Quantify", "Not a family", "Merge into UNKNOWN_KEY", "Quantify", "Quantify" });
            Check("Z16a2 a merging word without a valid source key remains uncertain", importer.ProposeWorkbook(invalidMerge).Decisions
                .Single(decision => decision.SourceValue == "Action gamma").IsUncertain);
            SetNeutralLegendMeanings(invalidMerge, new[] { "Quantify", "Not a family", "Merge into F01_extra", "Quantify", "Quantify" });
            Check("Z16a2 a source-key prefix inside a different identifier is not a merge target", importer.ProposeWorkbook(invalidMerge).Decisions
                .Single(decision => decision.SourceValue == "Action gamma").IsUncertain);
            string negated = Path.Combine(root, "negated.xlsx"); WriteNeutralWorkbook(negated);
            SetNeutralLegendMeanings(negated, new[] { "Do not quantify this entry", "Not a family", "Merge into F01", "Quantify", "Quantify" });
            Check("Z16a2 a negated quantify instruction is not an unconditional proposal", importer.ProposeWorkbook(negated).Decisions
                .Single(decision => decision.SourceValue == "Action alpha").IsUncertain);
            string keywordTarget = Path.Combine(root, "keyword-key.xlsx"); WriteNeutralWorkbook(keywordTarget);
            SetNeutralLegendMeanings(keywordTarget, new[] { "Quantify", "Not a family", "Merge into KEEP", "Quantify", "Quantify" });
            using (var document = SpreadsheetDocument.Open(keywordTarget, true))
            {
                var part = NeutralWorkbookPart(document.WorkbookPart!, "Data 9");
                part.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1).First().Elements<XL.Cell>().First().InlineString = new XL.InlineString(new XL.Text("KEEP"));
                part.Worksheet.Save();
            }
            Check("Z16a2 a valid merge target that spells an action word is treated as source data", importer.ProposeWorkbook(keywordTarget).Decisions
                .Single(decision => decision.SourceValue == "Action gamma").ProposedMapping is { Decision: FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey: "KEEP" });
            var noLegend = importer.ProposeWorkbook(new PanelSpreadsheetReader().ReadWorkbook(path), "Data 9",
                new Dictionary<string, PanelWorkbookSheetRole> { ["Data 2"] = PanelWorkbookSheetRole.Notes });
            Check("Z16a2 a role override removes legend interpretation but retains its text", noLegend.Colors.Count == 0
                && noLegend.ColorsState == PanelWorkbookReadState.Read && noLegend.Notes.Any(note => note.SheetName == "Data 2")
                && noLegend.Decisions.Single(decision => decision.SourceValue == "Action alpha").IsUncertain);
        }
        catch (Exception error) { Check("Z16a2 legend fixtures run without an exception", false, error.ToString()); }
        finally
        {
            string allowed = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("Legend fixture cleanup escaped its owned parent.");
            Directory.Delete(root, recursive: true);
        }
        Check("Z16a2 owned synthetic workbook folder is removed", !Directory.Exists(root));
    }

    private static WorksheetPart NeutralWorkbookPart(WorkbookPart book, string name) => (WorksheetPart)book.GetPartById(book.Workbook!.Sheets!
        .Elements<XL.Sheet>().Single(sheet => sheet.Name == name).Id!.Value!);
    private static XL.Cell NeutralWorkbookCell(string address, string text, uint style = 0) => new()
        { CellReference = address, DataType = XL.CellValues.InlineString, InlineString = new XL.InlineString(new XL.Text(text)), StyleIndex = style };

    private static void SetNeutralLegendMeanings(string path, IReadOnlyList<string> meanings)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var part = NeutralWorkbookPart(document.WorkbookPart!, "Data 2");
        var rows = part.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1).ToArray();
        for (int index = 0; index < meanings.Count; index++) rows[index].Elements<XL.Cell>().ElementAt(1).InlineString = new XL.InlineString(new XL.Text(meanings[index]));
        part.Worksheet.Save();
    }
}
