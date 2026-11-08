using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void WorkbookDecisionD87Tests(string root)
    {
        string[][] labels =
        [
            ["Keep", "Remove", "Merge into F01", "Report under others", "Quantify"],
            ["Manter", "Remover", "Juntar com F01", "Relatar em outras", "Quantificar"],
            ["Conservar", "Eliminar", "Unir con F01", "Reportar bajo otras", "Cuantificar"]
        ];
        string[][] meanings =
        [
            ["Optional: keep this entry", "Remove only when a condition holds", "Optional: merge into F01", "Optional: report under others", "Unspecified source description"],
            ["Opcional: manter esta entrada", "Remover apenas quando uma condicao ocorrer", "Opcional: juntar com F01", "Opcional: relatar em outras", "Descricao livre da origem"],
            ["Opcional: conservar esta entrada", "Eliminar solo cuando se cumpla una condicion", "Opcional: unir con F01", "Opcional: reportar bajo otras", "Descripcion general de origen"]
        ];
        string[][] negations =
        [
            ["No additional source text is supplied; quantify this entry", "Do not remove this entry; keep this entry", "Do not quantify this entry", "Not to quantify this entry", "Quantify this entry, no extra details"],
            ["Nao ha texto adicional da origem; quantificar esta entrada", "Nao remover esta entrada; manter esta entrada", "Nao quantificar esta entrada", "Nao deve quantificar esta entrada", "Quantificar esta entrada, sem detalhes adicionais"],
            ["No hay texto adicional de origen; cuantificar esta entrada", "No eliminar esta entrada; conservar esta entrada", "No cuantificar esta entrada", "No debe cuantificar esta entrada", "Cuantificar esta entrada, sin detalles adicionales"]
        ];
        string[] languages = ["English", "Portuguese", "Spanish"];
        string[] baseLabels = ["Core", "Central", "Principal"];
        string[] qualifiers = ["as a subcategory of X", "como subcategoria de X", "como subcategoria de X"];
        string[] distant = ["Not previously scheduled to quantify this entry", "Nao consta aqui para quantificar esta entrada", "No consta aqui para cuantificar esta entrada"];
        string[] exclusionLabels = ["Exclude", "Excluir", "Excluir"];
        string[] unrelatedMeanings = ["Does not occur in this group", "Nao ocorre neste grupo", "No aparece en este grupo"];
        string[] neutral = ["Action alpha", "Action beta", "Action gamma", "Action delta", "Action epsilon"];
        FamilyPanelDecision[] expected = [FamilyPanelDecision.Quantify, FamilyPanelDecision.NotAFamily,
            FamilyPanelDecision.MergeIntoAnotherFamily, FamilyPanelDecision.ReportUnderOthers, FamilyPanelDecision.Quantify];
        var importer = new FamilyPanelSpreadsheetImporter();
        for (int language = 0; language < languages.Length; language++)
        {
            string path = Path.Combine(root, "d87-" + language + ".xlsx");
            WriteDecisionWorkbook(path, labels[language], meanings[language]);
            var action = importer.ProposeWorkbook(path).Decisions.ToDictionary(item => item.SourceValue, StringComparer.Ordinal);
            Check("Z16a11 " + languages[language] + " action labels govern same-action and descriptive legend text",
                labels[language].Select((label, index) => action[label].ProposedMapping?.Decision == expected[index]).All(value => value)
                && action[labels[language][2]].ProposedMapping?.MergeTargetFamilyKey == "F01");
            Check("Z16a11 " + languages[language] + " label proposals retain full legend meaning and its reason",
                labels[language].Select((label, index) => action[label].LegendMeaning == meanings[language][index]
                    && action[label].Reason.Contains(meanings[language][index], StringComparison.Ordinal)
                    && action[label].ProposedMapping?.SourceValue == label).All(value => value));
            var conflictMeanings = meanings[language].ToArray(); conflictMeanings[0] = labels[language][1];
            WriteDecisionWorkbook(path, labels[language], conflictMeanings);
            Check("Z16a11 " + languages[language] + " a different legend action blocks an action label",
                importer.ProposeWorkbook(path).Decisions.Single(item => item.SourceValue == labels[language][0]).IsUncertain);
            var exclusions = labels[language].ToArray(); exclusions[1] = exclusionLabels[language];
            var unrelated = meanings[language].ToArray(); unrelated[1] = unrelatedMeanings[language];
            WriteDecisionWorkbook(path, exclusions, unrelated);
            var exclusion = importer.ProposeWorkbook(path).Decisions.Single(item => item.SourceValue == exclusions[1]);
            Check("Z16a11 " + languages[language] + " an exclusion label survives unrelated negation in its legend",
                exclusion.ProposedMapping?.Decision == FamilyPanelDecision.NotAFamily && !exclusion.IsUncertain
                && exclusion.LegendMeaning == unrelated[1] && exclusion.Reason.Contains(unrelated[1], StringComparison.Ordinal));

            WriteDecisionWorkbook(path, neutral, negations[language]);
            var local = importer.ProposeWorkbook(path).Decisions.ToDictionary(item => item.SourceValue, StringComparer.Ordinal);
            Check("Z16a11 " + languages[language] + " unrelated earlier negation leaves the later action proposed",
                local[neutral[0]].ProposedMapping?.Decision == FamilyPanelDecision.Quantify);
            Check("Z16a11 " + languages[language] + " a negated action is cancelled while another action remains",
                local[neutral[1]].ProposedMapping?.Decision == FamilyPanelDecision.Quantify);
            Check("Z16a11 " + languages[language] + " adjacent and two-word-distance negations cancel their action",
                local[neutral[2]].IsUncertain && local[neutral[3]].IsUncertain);
            Check("Z16a11 " + languages[language] + " a following negation never cancels an earlier action",
                local[neutral[4]].ProposedMapping?.Decision == FamilyPanelDecision.Quantify);
            var far = negations[language].ToArray(); far[0] = distant[language];
            WriteDecisionWorkbook(path, neutral, far);
            Check("Z16a11 " + languages[language] + " a negation beyond two word positions does not cancel an action",
                importer.ProposeWorkbook(path).Decisions.Single(item => item.SourceValue == neutral[0]).ProposedMapping?.Decision == FamilyPanelDecision.Quantify);

            foreach (var (open, close) in new[] { ('(', ')'), ('[', ']'), ('{', '}') })
            {
                string qualified = baseLabels[language] + " " + open + qualifiers[language] + close;
                var qualifiedLabels = neutral.ToArray(); qualifiedLabels[0] = qualified;
                var legendLabels = neutral.ToArray(); legendLabels[0] = baseLabels[language];
                var qualifiedMeanings = meanings[language].ToArray(); qualifiedMeanings[0] = labels[language][4];
                WriteDecisionWorkbook(path, qualifiedLabels, qualifiedMeanings, legendLabels);
                var item = importer.ProposeWorkbook(path).Decisions.Single(item => item.SourceValue == qualified);
                Check("Z16a11 " + languages[language] + " " + open + close + " qualifier keeps its legend match and requires review",
                    item.LegendEntries.Count == 1 && item.LegendEntries[0].Label == baseLabels[language]
                    && item.LegendMeaning == labels[language][4] && item.IsUncertain && !item.ExactProgramDecision
                    && item.SourceValue == qualified && item.Reason.Contains(qualifiers[language], StringComparison.Ordinal));
                var fullLegend = neutral.ToArray(); fullLegend[0] = qualified; fullLegend[1] = baseLabels[language];
                var fullMeanings = qualifiedMeanings.ToArray(); fullMeanings[1] = labels[language][1];
                WriteDecisionWorkbook(path, qualifiedLabels, fullMeanings, fullLegend);
                var full = importer.ProposeWorkbook(path).Decisions.Single(item => item.SourceValue == qualified);
                Check("Z16a11b " + languages[language] + " " + open + close + " full label takes priority without qualifier-forced uncertainty",
                    full.LegendEntries.Count == 1 && full.LegendEntries[0].Label == qualified
                    && full.LegendMeaning == fullMeanings[0] && full.ProposedMapping?.Decision == FamilyPanelDecision.Quantify
                    && !full.IsUncertain && !full.Reason.Contains("Bracketed qualifier:", StringComparison.Ordinal));
            }
            var prefixed = neutral.ToArray(); prefixed[0] = baseLabels[language] + "Extra (detail)";
            var distinct = neutral.ToArray(); distinct[0] = baseLabels[language] + " (" + qualifiers[language] + ")";
            var exactLegend = neutral.ToArray(); exactLegend[0] = baseLabels[language];
            exactLegend[1] = baseLabels[language] + " [" + qualifiers[language] + "]";
            var exactMeanings = meanings[language].ToArray(); exactMeanings[0] = labels[language][4];
            WriteDecisionWorkbook(path, distinct, exactMeanings, exactLegend);
            var exact = importer.ProposeWorkbook(path).Decisions.Single(item => item.SourceValue == distinct[0]);
            Check("Z16a11 " + languages[language] + " qualifier lookup keeps distinct legend labels separate",
                exact.LegendEntries.Count == 1 && exact.LegendEntries[0].Label == baseLabels[language]
                && exact.LegendMeaning == exactMeanings[0] && exact.IsUncertain);
            var plain = neutral.ToArray(); plain[0] = baseLabels[language];
            var onlyQualifiedLegend = neutral.ToArray(); onlyQualifiedLegend[0] = distinct[0];
            WriteDecisionWorkbook(path, plain, exactMeanings, onlyQualifiedLegend);
            Check("Z16a11 " + languages[language] + " an unqualified label does not collapse a qualified legend entry",
                importer.ProposeWorkbook(path).Decisions.Single(item => item.SourceValue == plain[0]).LegendEntries.Count == 0);
            var baseLegend = neutral.ToArray(); baseLegend[0] = baseLabels[language];
            WriteDecisionWorkbook(path, prefixed, meanings[language], baseLegend);
            Check("Z16a11 " + languages[language] + " qualifier matching never matches a longer base-label prefix",
                importer.ProposeWorkbook(path).Decisions.Single(item => item.SourceValue == prefixed[0]).LegendEntries.Count == 0);
        }
        WorkbookDecisionConfirmationTests(root);
        WorkbookDecisionLookupTests(root);
    }

    private static void WorkbookDecisionLookupTests(string root)
    {
        var importer = new FamilyPanelSpreadsheetImporter();
        string shippedPath = Path.Combine(root, "d87-full-label-shipped.xlsx"); ExampleFamilyList.Write(shippedPath);
        var shipped = importer.ProposeWorkbook(shippedPath);
        foreach (string label in new[] { "Condicional (só na fração solúvel)", "Excluir (ou só varredura)" })
        {
            var expected = shipped.Colors.SelectMany(item => item.Entries).Single(item => item.Label == label);
            var actual = shipped.Decisions.Single(item => item.SourceValue == label);
            Check("Z16a11b shipped " + label + " retains its own full-label legend meaning",
                actual.LegendEntries.Count == 1 && actual.LegendEntries[0].Label == label
                && actual.LegendEntries[0].SheetName == expected.SheetName && actual.LegendEntries[0].RowNumber == expected.RowNumber
                && actual.LegendMeaning == expected.Meaning && actual.Reason.Contains(expected.Meaning, StringComparison.Ordinal));
        }
        string path = Path.Combine(root, "d87-full-label-column.xlsx");
        string[] plain = ["Action alpha", "Action beta", "Action gamma", "Action delta", "Action epsilon"];
        string[] qualified = plain.Select(label => label + " (detail)").ToArray();
        string[] meanings = ["Quantify", "Not a family", "Quantify", "Not a family", "Quantify"];
        WriteDecisionWorkbook(path, qualified, meanings);
        var full = importer.ProposeWorkbook(path);
        Check("Z16a11b full bracketed legend labels identify the decision column",
            full.FamilyProposal.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Decision) == "h06"
            && qualified.All(label => full.Decisions.Any(item => item.SourceValue == label
                && item.LegendEntries.Count == 1 && item.LegendEntries[0].Label == label)));
        WriteDecisionWorkbook(path, qualified, meanings, plain);
        var fallback = importer.ProposeWorkbook(path);
        Check("Z16a11b base-only legend labels still identify the decision column and require qualifier review",
            fallback.FamilyProposal.Choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Decision) == "h06"
            && qualified.All(label => fallback.Decisions.Any(item => item.SourceValue == label
                && item.IsUncertain && item.LegendEntries.Count == 1 && item.Reason.Contains("(detail)", StringComparison.Ordinal))));
    }

    private static void WorkbookDecisionConfirmationTests(string root)
    {
        string path = Path.Combine(root, "d87-confirmation.xlsx");
        string[] labels = ["Quantify", "Not a family", "Merge into F01", "Report under others", "Keep"];
        string[] meanings = ["Quantify only when an additional condition holds", "Not a family",
            "Optional: merge into F01", "Optional: report under others", "Keep this entry"];
        WriteDecisionWorkbook(path, labels, meanings);
        var expected = new[] { (Source: labels[0], Decision: FamilyPanelDecision.Quantify),
            (Source: labels[2], Decision: FamilyPanelDecision.MergeIntoAnotherFamily),
            (Source: labels[3], Decision: FamilyPanelDecision.ReportUnderOthers) };
        var hints = new FamilyPanelSpreadsheetImporter().ProposeWorkbook(path).Decisions.ToDictionary(item => item.SourceValue, StringComparer.Ordinal);
        foreach (var (source, decision) in expected)
            Check("Z16a11 qualified legend keeps " + source + " proposed without marking it exact",
                hints[source].ProposedMapping?.Decision == decision && !hints[source].ExactProgramDecision);

        EnsureApplication();
        using var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())),
            workbookSource: new FamilyListWorkbookCoreSource());
        FinishReviewWork(() => model.LoadAsync(path));
        model.Columns.Single(column => column.Role == PanelImportFields.Decision).Header = model.DecisionHint;
        FinishReviewWork(async () => { await model.NextAsync(); });
        foreach (var (source, decision) in expected)
        {
            var row = model.Decisions.Single(item => item.Source == source);
            Check("Z16a11 qualified legend leaves " + source + " unconfirmed before the press",
                row.Decision == decision && !row.Confirmed && (decision != FamilyPanelDecision.MergeIntoAnotherFamily || row.MergeTarget == "F01"));
        }
        Check("Z16a11 an unconditional exact action still starts confirmed",
            hints[labels[1]].ExactProgramDecision && model.Decisions.Single(item => item.Source == labels[1]).Confirmed);
        model.ConfirmDecisions();
        Check("Z16a11 the explicit confirmation press accepts the proposed actions",
            model.Decisions.All(item => item.Confirmed) && model.CanNext);
    }

    private static void WriteDecisionWorkbook(string path, IReadOnlyList<string> labels, IReadOnlyList<string> meanings,
        IReadOnlyList<string>? legendLabels = null)
    {
        WriteNeutralWorkbook(path);
        using var document = SpreadsheetDocument.Open(path, true);
        var list = NeutralWorkbookPart(document.WorkbookPart!, "Data 9");
        string[] original = ["Action alpha", "Action beta", "Action gamma", "Action delta", "Action epsilon"];
        foreach (var row in list.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1))
        {
            var cell = row.Elements<XL.Cell>().ElementAt(5);
            int index = Array.IndexOf(original, cell.InnerText);
            if (index >= 0) cell.InlineString = new XL.InlineString(new XL.Text(labels[index]));
        }
        list.Worksheet.Save();
        var legend = NeutralWorkbookPart(document.WorkbookPart!, "Data 2");
        int number = 0;
        foreach (var row in legend.Worksheet!.GetFirstChild<XL.SheetData>()!.Elements<XL.Row>().Skip(1))
        {
            row.Elements<XL.Cell>().First().InlineString = new XL.InlineString(new XL.Text((legendLabels ?? labels)[number]));
            row.Elements<XL.Cell>().ElementAt(1).InlineString = new XL.InlineString(new XL.Text(meanings[number++]));
        }
        legend.Worksheet.Save();
    }
}
