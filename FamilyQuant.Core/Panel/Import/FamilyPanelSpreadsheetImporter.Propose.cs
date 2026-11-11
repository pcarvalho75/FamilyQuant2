using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyQuant.Panels.Import;

public sealed partial class FamilyPanelSpreadsheetImporter
{
    /// <summary>Proposes roles from the shape of each column's values, never from header words: short distinct
    /// codes for the abbreviation, then the first and second columns of names, then other text as notes. A column
    /// whose cells mostly carry Pfam accessions is proposed for Pfam. Decision and colour are never proposed.</summary>
    public PanelImportProposal Propose(PanelSpreadsheetTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var proposal = new PanelImportProposal
        {
            Choices = new FamilyPanelImport
            {
                SourceName = table.SourceName, SourceSha256 = table.SourceSha256, FamilySheet = table.SheetName,
                HeaderRowNumber = table.HeaderRowNumber, ImportVersion = PanelImportRules.Current,
            },
        };
        if (table.IsTemplate && PanelImportTemplate.Columns.Any(c => c.Role == PanelImportFields.FamilyKey && table.Headers.Contains(c.Header, StringComparer.Ordinal)))
        {
            foreach (var (header, role) in PanelImportTemplate.ImportColumns.Where(c => table.Headers.Contains(c.Header, StringComparer.Ordinal)))
            {
                proposal.Choices.ColumnHeaders[role] = header;
                proposal.Reasons[role] = "\"" + header + "\" is a column of the FamilyQuant template.";
            }
            proposal.IsTemplate = true;
            proposal.Decisions = ProposeDecisions(table, proposal.Choices).Where(d => d.Exact).Select(d => d.Mapping).ToList();
        }
        if (table.Headers.Contains(PanelImportTemplate.UniProtFamily, StringComparer.Ordinal))
        {
            proposal.Choices.ColumnHeaders[PanelImportFields.UniProtFamilies] = PanelImportTemplate.UniProtFamily;
            proposal.Reasons[PanelImportFields.UniProtFamilies] = "The UniProt family column contains exact source family names.";
        }
        var rows = table.Rows.Where(r => DecorativeReason(table, r) == null).ToList();
        var stats = table.Headers.Select(h => new ColumnShape(h, rows)).ToList();
        proposal.ColorColumns = table.Headers.Where(h => table.Rows.Where(r => r.Fills.ContainsKey(h))
            .Select(r => r.Fills[h]).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2).ToList();
        if (rows.Count == 0 || proposal.IsTemplate) return proposal;
        var taken = proposal.Choices.ColumnHeaders.Values.ToHashSet(StringComparer.Ordinal);
        void Take(string role, ColumnShape? column, string reason)
        {
            if (column == null) return;
            taken.Add(column.Header);
            proposal.Choices.ColumnHeaders[role] = column.Header;
            proposal.Reasons[role] = reason.Replace("{0}", "\"" + OneLine(column.Header) + "\"", StringComparison.Ordinal);
        }
        var pfam = stats.FirstOrDefault(c => c.Count > 0 && c.PfamShare >= 0.5);
        Take(PanelImportFields.Pfam, pfam, "Most cells in {0} hold Pfam accessions such as " + pfam?.FirstAccession + ", so it is proposed for Pfam domains.");
        var code = stats.FirstOrDefault(c => !taken.Contains(c.Header) && c.Filled >= 0.5 && c.IsCode);
        if (code != null) Take(PanelImportFields.FamilyKey, code, "{0} holds short, distinct codes, so it is proposed as the abbreviation.");
        else Take(PanelImportFields.FamilyKey, stats.FirstOrDefault(c => !taken.Contains(c.Header) && c.Filled >= 0.5),
            "{0} is the first column filled in most rows, so it is proposed as the abbreviation. Check it.");
        Take(PanelImportFields.DisplayName, stats.FirstOrDefault(c => !taken.Contains(c.Header) && c.IsNames),
            "{0} is the first column of names, so it is proposed as the name.");
        Take(PanelImportFields.OtherName, stats.FirstOrDefault(c => !taken.Contains(c.Header) && c.IsNames),
            "{0} is a second column of names, so it is proposed as the name in another language.");
        Take(PanelImportFields.Notes, stats.Where(c => !taken.Contains(c.Header) && c.Count > 0 && c.LetterShare >= 0.8)
                .OrderByDescending(c => c.MeanLength).FirstOrDefault(),
            "{0} holds the longest other text, so it is proposed as notes. Every column is kept as text either way.");
        (proposal.DecisionHint, proposal.DecisionHintReason) = DecisionHint(table, rows, proposal.Choices);
        return proposal;
    }

    /// <summary>Z12p5b: names a column that may say what to do with each family, never maps it. A candidate is any column
    /// other than the abbreviation, the names and Pfam that holds a few short labels repeated over at least half the rows
    /// (at most half as many labels as filled cells). One candidate is named; among several, the one whose labels the
    /// decision proposal reads as excluding or merging most often, and none when that does not single one out.</summary>
    private (string Header, string Reason) DecisionHint(PanelSpreadsheetTable table, List<PanelSpreadsheetRow> rows, FamilyPanelImport choices)
    {
        var mapped = new[] { PanelImportFields.FamilyKey, PanelImportFields.DisplayName, PanelImportFields.OtherName, PanelImportFields.Pfam }
            .Select(role => choices.ColumnHeaders.GetValueOrDefault(role)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var candidates = new List<(string Header, List<string> Labels, int Filled, int Signals)>();
        foreach (string header in table.Headers.Where(h => !mapped.Contains(h)))
        {
            var values = rows.Select(r => Value(r, header).Trim()).Where(v => v.Length > 0).ToList();
            var labels = values.Distinct(StringComparer.Ordinal).ToList();
            if (values.Count * 2 < rows.Count || labels.Count < 2 || labels.Count * 2 > values.Count
                || values.Select(v => v.Length).Order().ElementAt(values.Count / 2) > 40 || values.Count(v => v.Any(char.IsLetter)) * 5 < values.Count * 4) continue;
            var probe = Copy(choices);
            probe.ColumnHeaders[PanelImportFields.Decision] = header;
            int signals = ProposeDecisions(table, probe).Count(d => d.Exact || d.Mapping.Decision != FamilyPanelDecision.Quantify);
            candidates.Add((header, labels, values.Count, signals));
        }
        var ranked = candidates.OrderByDescending(c => c.Signals).ToList();
        if (ranked.Count == 0 || ranked.Count > 1 && (ranked[0].Signals == 0 || ranked[0].Signals == ranked[1].Signals)) return (string.Empty, string.Empty);
        var hint = ranked[0];
        var examples = hint.Labels.Take(3).Select(l => "\"" + OneLine(l) + "\"").ToList();
        string such = examples.Count == 1 ? examples[0] : string.Join(", ", examples.Take(examples.Count - 1)) + " and " + examples[^1];
        return (hint.Header, "\"" + OneLine(hint.Header) + "\" holds " + N(hint.Labels.Count) + " short labels repeated over " + N(hint.Filled)
            + " rows, such as " + such + ". Is it the column that says what to do with each family? Without one, every family is quantified.");
    }

    // Proposal words in English, Portuguese and Spanish, compared without accents or case. They only propose a
    // meaning that the user sees with its rows and confirms; the stored mapping is always the user's.
    private static readonly HashSet<string> ExcludeWords = new(StringComparer.Ordinal)
    {
        "exclude", "excluded", "exclusion", "remove", "removed", "discard", "drop", "excluir", "excluido", "excluida",
        "exclusao", "remover", "removido", "removida", "descartar", "eliminar", "retirar", "quitar",
    };
    private static readonly HashSet<string> MergeWords = new(StringComparer.Ordinal)
    {
        "merge", "merged", "join", "joined", "combine", "fuse", "fundir", "fundido", "fundida", "fusao", "juntar",
        "unir", "mesclar", "combinar", "incorporar", "agrupar",
    };

    /// <summary>Merge into a family when the label has a merging word and names exactly one abbreviation of the
    /// table; Not a family when it has an excluding word; otherwise Quantify.</summary>
    public IReadOnlyList<PanelDecisionProposal> ProposeDecisions(PanelSpreadsheetTable table, FamilyPanelImport choices)
    {
        ArgumentNullException.ThrowIfNull(table); ArgumentNullException.ThrowIfNull(choices);
        if (!choices.ColumnHeaders.TryGetValue(PanelImportFields.Decision, out var header) || !table.Headers.Contains(header, StringComparer.Ordinal))
            return Array.Empty<PanelDecisionProposal>();
        bool graceful = choices.ImportVersion >= PanelImportRules.Graceful;
        var keys = choices.ColumnHeaders.TryGetValue(PanelImportFields.FamilyKey, out var keyHeader) && table.Headers.Contains(keyHeader, StringComparer.Ordinal)
            ? table.Rows.Select(r => graceful ? Value(r, keyHeader).Trim() : Value(r, keyHeader)).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        var result = new List<PanelDecisionProposal>();
        foreach (var group in table.Rows.Where(r => !Skipped(table, r, choices)).GroupBy(r => Value(r, header), StringComparer.Ordinal).Where(g => !string.IsNullOrWhiteSpace(g.Key)))
        {
            string label = group.Key, quoted = "\"" + OneLine(label) + "\"";
            var words = Regex.Split(Fold(label), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);
            var named = keys.Where(k => Regex.IsMatch(label, @"(?<![\p{L}\p{N}\-])" + Regex.Escape(k) + @"(?![\p{L}\p{N}\-])")).ToArray();
            var proposal = new PanelDecisionProposal { SourceValue = label, RowNumbers = group.Select(r => r.RowNumber).ToList(),
                Mapping = new FamilyPanelDecisionMapping { SourceValue = label, Decision = FamilyPanelDecision.Quantify } };
            string trimmed = label.Trim();
            var exact = PanelImportTemplate.DecisionNames.Where(d => string.Equals(trimmed, d.Label, StringComparison.OrdinalIgnoreCase)).ToArray();
            string mergeKey = trimmed.StartsWith(PanelImportTemplate.MergePrefix, StringComparison.OrdinalIgnoreCase) ? trimmed[PanelImportTemplate.MergePrefix.Length..].Trim() : string.Empty;
            if (exact.Length == 1)
            {
                proposal.Mapping.Decision = exact[0].Decision; proposal.Exact = true;
                proposal.Reason = quoted + " is the FamilyQuant decision " + exact[0].Label + ".";
            }
            else if (mergeKey.Length > 0 && keys.Contains(mergeKey, StringComparer.Ordinal))
            {
                proposal.Mapping.Decision = FamilyPanelDecision.MergeIntoAnotherFamily; proposal.Mapping.MergeTargetFamilyKey = mergeKey; proposal.Exact = true;
                proposal.Reason = quoted + " is the FamilyQuant decision Merge into " + OneLine(mergeKey) + ".";
            }
            else if (words.Overlaps(MergeWords) && named.Length == 1)
            {
                proposal.Mapping.Decision = FamilyPanelDecision.MergeIntoAnotherFamily; proposal.Mapping.MergeTargetFamilyKey = named[0];
                proposal.Reason = quoted + " names the family " + OneLine(named[0]) + " and a word for merging, so it is proposed as Merge into " + OneLine(named[0]) + ".";
            }
            else if (words.Overlaps(ExcludeWords))
            {
                proposal.Mapping.Decision = FamilyPanelDecision.NotAFamily;
                proposal.Reason = quoted + " has a word for excluding, so it is proposed as Not a family.";
            }
            else proposal.Reason = words.Overlaps(MergeWords)
                ? quoted + " has a word for merging but does not name exactly one abbreviation of the table, so it is proposed as Quantify. Choose the family to merge into if that is meant."
                : quoted + " is proposed as Quantify, the meaning used when no other is chosen.";
            result.Add(proposal);
        }
        return result;
    }

    private static string Fold(string text) => string.Concat(text.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();

    private sealed class ColumnShape
    {
        public string Header { get; }
        public int Count { get; }
        public double Filled { get; }
        public double PfamShare { get; }
        public double LetterShare { get; }
        public double MeanLength { get; }
        public string FirstAccession { get; } = string.Empty;
        public bool IsCode { get; }
        public bool IsNames { get; }

        public ColumnShape(string header, IReadOnlyList<PanelSpreadsheetRow> rows)
        {
            Header = header;
            var values = rows.Select(r => Value(r, header).Trim()).Where(v => v.Length > 0).ToList();
            Count = values.Count;
            if (Count == 0) return;
            Filled = (double)Count / rows.Count;
            PfamShare = values.Count(v => PfamAccession.IsMatch(v)) / (double)Count;
            FirstAccession = values.Select(v => PfamAccession.Match(v)).FirstOrDefault(m => m.Success)?.Value ?? string.Empty;
            LetterShare = values.Count(v => v.Any(char.IsLetter)) / (double)Count;
            MeanLength = values.Average(v => v.Length);
            double distinct = values.Distinct(StringComparer.Ordinal).Count() / (double)Count;
            int median = values.Select(v => v.Length).Order().ElementAt(Count / 2);
            double spaced = values.Count(v => v.Any(char.IsWhiteSpace)) / (double)Count;
            IsCode = median <= 12 && spaced <= 0.25 && distinct >= 0.8 && LetterShare >= 0.8;
            IsNames = Filled >= 0.5 && LetterShare >= 0.8 && distinct >= 0.6 && PfamShare < 0.5;
        }
    }
}
