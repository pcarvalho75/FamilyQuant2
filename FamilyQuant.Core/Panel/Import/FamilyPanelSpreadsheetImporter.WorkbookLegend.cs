using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyQuant.Panels.Import;

public sealed partial class FamilyPanelSpreadsheetImporter
{
    private void PopulateWorkbookLegend(PanelWorkbookProposal proposal)
    {
        proposal.Colors.Clear(); proposal.Decisions.Clear();
        proposal.ColorsState = proposal.DecisionsState = PanelWorkbookReadState.NotRead;
        if (proposal.FamilyTable is not { } table || proposal.Problems.Any(problem => problem.Severity == PanelImportSeverity.Blocking)) return;
        var choices = proposal.FamilyProposal.Choices;
        string keyHeader = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.FamilyKey, "");
        var rows = WorkbookDataRows(table);
        var colors = rows.Where(row => row.Fills.ContainsKey(keyHeader)).Select(row => row.Fills[keyHeader])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var legends = proposal.Workbook.Sheets.Where(sheet => sheet.Role == PanelWorkbookSheetRole.ColorLegend).ToArray();
        var entries = new List<PanelWorkbookLegendEntry>();
        bool read = true;
        foreach (var sheet in legends)
        {
            if (sheet.Table is not { Headers.Count: >= 2 } legend)
            {
                read = false;
                proposal.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.ColumnNotChosen, Severity = PanelImportSeverity.Note,
                    SheetName = sheet.SheetName, Message = "The chosen legend needs an item column and a meaning column before its hints can be read." });
                continue;
            }
            foreach (var row in WorkbookDataRows(legend))
            {
                string color = row.Fills.GetValueOrDefault(legend.Headers[0], "");
                if (!colors.Contains(color, StringComparer.OrdinalIgnoreCase)) continue;
                entries.Add(new PanelWorkbookLegendEntry { SheetName = sheet.SheetName, RowNumber = row.RowNumber,
                    Color = color, Label = Value(row, legend.Headers[0]), Meaning = Value(row, legend.Headers[1]) });
            }
        }
        if (legends.Length > 0)
        foreach (string color in colors)
        {
            var matched = entries.Where(entry => string.Equals(entry.Color, color, StringComparison.OrdinalIgnoreCase)).ToList();
            var names = matched.Select(entry => entry.Label.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            var meanings = matched.Select(entry => entry.Meaning.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            proposal.Colors.Add(new PanelWorkbookColorHint { Color = color, Entries = matched,
                Name = names.Length == 1 ? names[0] : "", Meaning = meanings.Length == 1 ? meanings[0] : "",
                IsUncertain = names.Length != 1 || meanings.Length != 1 });
        }
        proposal.ColorsState = read ? PanelWorkbookReadState.Read : PanelWorkbookReadState.NotRead;
        // A proposed display category names source fills; it never makes a program decision.
        if (legends.Length > 0 && proposal.Workbook.Sheets.Count > 1 && proposal.Colors.Count > 0)
        {
            choices.ColumnHeaders[PanelImportFields.ColorCategory] = keyHeader;
            choices.ColorLabels = proposal.Colors.Where(color => !color.IsUncertain)
                .Select(color => new FamilyPanelColorLabel { Color = color.Color, Label = color.Name }).ToList();
            proposal.FamilyProposal.Reasons[PanelImportFields.ColorCategory] = "Names are proposed from source legend cells whose fills match the abbreviation cells.";
        }

        string decisionHeader = choices.ColumnHeaders.GetValueOrDefault(PanelImportFields.Decision, "");
        bool ambiguous = false;
        if (!table.Headers.Contains(decisionHeader, StringComparer.Ordinal) && entries.Count > 0)
        {
            var labels = entries.Select(entry => WorkbookLabel(entry.Label)).Where(label => label.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var excluded = new[] { PanelImportFields.FamilyKey, PanelImportFields.DisplayName, PanelImportFields.Pfam }
                .Select(role => choices.ColumnHeaders.GetValueOrDefault(role, "")).ToHashSet(StringComparer.Ordinal);
            var ranked = table.Headers.Where(header => !excluded.Contains(header)).Select(header =>
            {
                var values = rows.Select(row => Value(row, header)).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
                return new { Header = header, Filled = values.Count, Matched = values.Count(value => labels.Contains(WorkbookDecisionLabel(value).Label)) };
            }).Where(column => column.Matched >= Math.Min(2, column.Filled) && column.Matched > 0 && column.Matched * 2 >= column.Filled)
                .OrderByDescending(column => column.Matched).ThenByDescending(column => (double)column.Matched / column.Filled).ToArray();
            ambiguous = ranked.Length > 1 && ranked[0].Matched == ranked[1].Matched
                && (long)ranked[0].Matched * ranked[1].Filled == (long)ranked[1].Matched * ranked[0].Filled;
            if (ambiguous)
                proposal.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.ColumnNotChosen, Severity = PanelImportSeverity.Note,
                    SheetName = table.SheetName, Message = "Several columns match the legend labels equally. Choose the decision column: "
                        + string.Join(", ", ranked.Where(column => column.Matched == ranked[0].Matched).Select(column => "\"" + OneLine(column.Header) + "\"")) + "." });
            else if (ranked.Length > 0)
            {
                decisionHeader = ranked[0].Header;
                proposal.FamilyProposal.DecisionHint = decisionHeader;
                proposal.FamilyProposal.DecisionHintReason = "The column's values match item labels recorded in the workbook's fill legend.";
                foreach (var role in choices.ColumnHeaders.Where(pair => pair.Value == decisionHeader
                    && pair.Key is not (PanelImportFields.FamilyKey or PanelImportFields.DisplayName or PanelImportFields.Pfam)).Select(pair => pair.Key).ToArray())
                    choices.ColumnHeaders.Remove(role);
                choices.ColumnHeaders[PanelImportFields.Decision] = decisionHeader;
            }
        }
        if (ambiguous) return;
        if (!table.Headers.Contains(decisionHeader, StringComparer.Ordinal)) decisionHeader = proposal.FamilyProposal.DecisionHint;
        if (!table.Headers.Contains(decisionHeader, StringComparer.Ordinal))
        {
            proposal.DecisionsState = read ? PanelWorkbookReadState.Read : PanelWorkbookReadState.NotRead;
            return;
        }
        var keys = rows.Select(row => Value(row, keyHeader).Trim()).Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var group in rows.Where(row => !string.IsNullOrWhiteSpace(Value(row, decisionHeader)))
            .GroupBy(row => Value(row, decisionHeader), StringComparer.Ordinal))
        {
            string baseLabel = WorkbookDecisionLabel(group.Key).Label;
            var matched = entries.Where(entry => baseLabel.Length > 0
                && string.Equals(WorkbookLabel(entry.Label), baseLabel, StringComparison.OrdinalIgnoreCase)).ToList();
            var meanings = matched.Select(entry => entry.Meaning).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToList();
            var evidence = WorkbookDecisionEvidence(group.Key, meanings, keys);
            var action = evidence.Mapping;
            proposal.Decisions.Add(new PanelWorkbookDecisionHint { SourceValue = group.Key,
                RowNumbers = group.Select(row => row.RowNumber).ToList(), LegendEntries = matched, LegendMeaning = string.Join("\n", meanings),
                ProposedMapping = action == null ? null : new FamilyPanelDecisionMapping
                    { SourceValue = group.Key, Decision = action.Decision, MergeTargetFamilyKey = action.MergeTargetFamilyKey },
                ExactProgramDecision = action != null && WorkbookKnownDecision(group.Key, keys) != null
                    && !meanings.Any(meaning => WorkbookDecisionSignals(meaning, keys).NeedsReview),
                Reason = evidence.Reason });
        }
        proposal.DecisionsState = read ? PanelWorkbookReadState.Read : PanelWorkbookReadState.NotRead;
    }

    private static string WorkbookLabel(string value) => value.Trim().Normalize(NormalizationForm.FormC);

    private static (string Label, string Qualifier) WorkbookDecisionLabel(string value)
    {
        string normalized = value.Trim().Normalize(NormalizationForm.FormC);
        int bracket = normalized.IndexOfAny(['(', '[', '{']);
        return bracket < 0 ? (normalized, "") : (normalized[..bracket].TrimEnd(), normalized[bracket..]);
    }

    private enum WorkbookActionSignal { Quantify, Exclude, Merge, Others, Optional, Conditional, Negation }

    // D84: general program-action vocabulary, in English, Portuguese and Spanish. These words only
    // propose UI actions from decision/legend text; they never classify proteins or identify sheets.
    // D87: action labels take precedence over same-action/descriptive legend text. Nearby preceding
    // negation cancels its action only; bracketed source qualifiers always require explicit review.
    private static readonly IReadOnlyDictionary<WorkbookActionSignal, string[]> WorkbookActionVocabulary = new Dictionary<WorkbookActionSignal, string[]>
    {
        [WorkbookActionSignal.Quantify] = ["quantify", "keep", "include", "quantificar", "manter", "incluir", "cuantificar", "conservar", "mantener"],
        [WorkbookActionSignal.Exclude] = ["exclude", "remove", "discard", "excluir", "remover", "descartar", "eliminar", "quitar",
            "not a family", "nao e familia", "nao e uma familia", "no es familia", "no es una familia"],
        [WorkbookActionSignal.Merge] = ["merge", "join", "fundir", "juntar", "unir", "mesclar", "fusionar", "combinar"],
        [WorkbookActionSignal.Others] = ["report under others", "report as others", "relatar em outras", "reportar em outras", "reportar bajo otras"],
        [WorkbookActionSignal.Optional] = ["optional", "optionally", "opcional", "opcionais", "opcionales"],
        [WorkbookActionSignal.Conditional] = ["conditional", "conditionally", "if", "when", "only", "unless", "except",
            "condicional", "quando", "se", "so", "apenas", "exceto", "cuando", "si", "solo", "solamente", "excepto"],
        [WorkbookActionSignal.Negation] = ["not", "no", "nao", "cannot", "never", "nunca", "without", "sem", "sin"],
    };

    private static (FamilyPanelDecisionMapping? Mapping, string Reason) WorkbookDecisionEvidence(string label,
        IReadOnlyList<string> meanings, IReadOnlySet<string> keys)
    {
        var source = WorkbookDecisionLabel(label);
        var labelSignals = WorkbookDecisionSignals(source.Label, keys);
        var legendSignals = meanings.Select(meaning => WorkbookDecisionSignals(meaning, keys)).ToArray();
        var all = new[] { labelSignals }.Concat(legendSignals).ToArray();
        var matchedWords = all.SelectMany(item => item.Words).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string evidence = matchedWords.Length == 0 ? "No program action word matched."
            : "Matched program words: " + string.Join(", ", matchedWords) + ".";
        if (meanings.Count > 0) evidence += " Legend meaning: " + string.Join(" | ", meanings) + ".";
        if (source.Qualifier.Length > 0)
            return (null, evidence + " Bracketed qualifier: " + source.Qualifier + ". Review its action.");
        bool actionLabel = labelSignals.Actions.Count == 1 && !labelSignals.NeedsReview;
        if (labelSignals.NeedsReview || !actionLabel && legendSignals.Any(item => item.NeedsReview))
            return (null, evidence + " The text is optional or conditional; review its action.");
        var actions = all.SelectMany(item => item.Actions).Distinct().ToArray();
        if (actions.Length != 1) return (null, evidence + (actions.Length == 0 ? " Choose a program action." : " Conflicting actions need a choice."));
        var targets = all.SelectMany(item => item.Targets).ToHashSet(StringComparer.Ordinal);
        if (actions[0] == WorkbookActionSignal.Merge && targets.Count != 1)
            return (null, evidence + " A merge needs exactly one valid source abbreviation.");
        var decision = actions[0] switch
        {
            WorkbookActionSignal.Quantify => FamilyPanelDecision.Quantify,
            WorkbookActionSignal.Exclude => FamilyPanelDecision.NotAFamily,
            WorkbookActionSignal.Others => FamilyPanelDecision.ReportUnderOthers,
            _ => FamilyPanelDecision.MergeIntoAnotherFamily
        };
        return (new FamilyPanelDecisionMapping { Decision = decision, MergeTargetFamilyKey = decision == FamilyPanelDecision.MergeIntoAnotherFamily ? targets.Single() : "" },
            evidence + " This is a proposal; the user confirms it before import.");
    }

    private static (HashSet<WorkbookActionSignal> Actions, HashSet<string> Words, string[] Targets, bool NeedsReview)
        WorkbookDecisionSignals(string text, IReadOnlySet<string> keys)
    {
        string normalized = Regex.Replace(Fold(text), @"\s+", " ");
        var hits = new List<(WorkbookActionSignal Signal, string Word, int Index, int Length)>();
        foreach (var bucket in WorkbookActionVocabulary)
        foreach (string word in bucket.Value)
        foreach (Match match in Regex.Matches(normalized, @"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"(?![\p{L}\p{N}])", RegexOptions.CultureInvariant))
            hits.Add((bucket.Key, word, match.Index, match.Length));
        var named = keys.Where(key => Regex.IsMatch(text, @"(?<![\p{L}\p{N}_\-])" + Regex.Escape(key) + @"(?![\p{L}\p{N}_\-])", RegexOptions.CultureInvariant)).ToArray();
        int firstMerge = hits.Where(hit => hit.Signal == WorkbookActionSignal.Merge).Select(hit => hit.Index).DefaultIfEmpty(int.MaxValue).Min();
        // A source key following a merge verb is data, even if it happens to spell an action word.
        var keySpans = named.SelectMany(key => Regex.Matches(normalized, @"(?<![\p{L}\p{N}_\-])" + Regex.Escape(Fold(key)) + @"(?![\p{L}\p{N}_\-])", RegexOptions.CultureInvariant)
            .Cast<Match>()).Where(match => match.Index > firstMerge).ToArray();
        var active = hits.Where(hit => !keySpans.Any(span => hit.Index >= span.Index && hit.Index + hit.Length <= span.Index + span.Length))
            .Where(hit => hit.Signal != WorkbookActionSignal.Negation || !hits.Any(phrase => phrase.Signal == WorkbookActionSignal.Exclude
                && phrase.Word.Contains(' ') && hit.Index >= phrase.Index && hit.Index + hit.Length <= phrase.Index + phrase.Length)).ToArray();
        var actions = new HashSet<WorkbookActionSignal>();
        foreach (var hit in active.Where(hit => hit.Signal is WorkbookActionSignal.Quantify or WorkbookActionSignal.Exclude
            or WorkbookActionSignal.Merge or WorkbookActionSignal.Others))
        {
            bool negated = active.Any(negative => negative.Signal == WorkbookActionSignal.Negation
                && negative.Index + negative.Length <= hit.Index
                && WorkbookNearbyNegation(normalized[(negative.Index + negative.Length)..hit.Index]));
            if (!negated) actions.Add(hit.Signal);
        }
        return (actions, active.Select(hit => hit.Word).ToHashSet(StringComparer.Ordinal), named,
            active.Any(hit => hit.Signal is WorkbookActionSignal.Optional or WorkbookActionSignal.Conditional));
    }

    private static bool WorkbookNearbyNegation(string between) =>
        // The action is at most two word positions after the negation, within the same clause.
        !Regex.IsMatch(between, @"[^\p{L}\p{N}\s]", RegexOptions.CultureInvariant)
        && Regex.Matches(between, @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant).Count <= 1;

    private static FamilyPanelDecisionMapping? WorkbookKnownDecision(string text, IReadOnlySet<string> keys)
    {
        string value = text.Trim().TrimEnd('.', ';', ':').Trim();
        foreach (var (label, decision) in PanelImportTemplate.DecisionNames)
            if (string.Equals(value, label, StringComparison.OrdinalIgnoreCase)) return new FamilyPanelDecisionMapping { Decision = decision };
        if (value.StartsWith(PanelImportTemplate.MergePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string target = value[PanelImportTemplate.MergePrefix.Length..].Trim();
            if (keys.Contains(target)) return new FamilyPanelDecisionMapping { Decision = FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey = target };
        }
        return null;
    }
}
