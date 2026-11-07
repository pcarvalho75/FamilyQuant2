using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Panels.Import;

/// <summary>Detached interpretation of the subclass sources shown in the workbook summary. Never confirms or applies.</summary>
public static partial class PanelSubclassImport
{
    public static PanelSubclassProposalResult Propose(IReadOnlyList<PanelWorkbookSubclassTable> tables,
        IReadOnlyCollection<string> summaryFamilyKeys, IReadOnlyList<FamilyPanelDomain> recognizedPfam,
        IReadOnlyList<PanelSubclassExpectedName> expectedNames)
    {
        ArgumentNullException.ThrowIfNull(tables); ArgumentNullException.ThrowIfNull(summaryFamilyKeys);
        ArgumentNullException.ThrowIfNull(recognizedPfam); ArgumentNullException.ThrowIfNull(expectedNames);
        var result = new PanelSubclassProposalResult();
        var keys = summaryFamilyKeys.ToHashSet(StringComparer.Ordinal);
        var vocabulary = new SubclassVocabulary(recognizedPfam);
        var expected = new List<(PanelSubclassExpectedName Name, PanelSubclassProposal Proposal)>();
        for (int index = 0; index < expectedNames.Count; index++)
        {
            var name = expectedNames[index];
            var proposal = new PanelSubclassProposal
            {
                ProposalId = ImportId("expected", index.ToString(CultureInfo.InvariantCulture), name.ExpectedNameId),
                ExpectedNameId = name.ExpectedNameId, FamilyKey = keys.Contains(name.FamilyKey) ? name.FamilyKey : "",
                Key = name.Key.Length > 0 ? name.Key : name.DisplayName.Trim(), DisplayName = name.DisplayName.Trim(),
                SourceRows = name.SourceRows.Select(CopyRow).ToList(),
                Reason = "The family list names this subclass.",
            };
            if (proposal.FamilyKey.Length > 0) proposal.CandidateFamilyKeys.Add(proposal.FamilyKey);
            else Uncertain(proposal, "The expected family key is not shown in the family summary: " + name.FamilyKey + ".");
            if (name.SourceRows.Count == 0) Uncertain(proposal, "The expected name has no source row; its sheet, row and original cell text are unavailable.");
            if (name.ExpectedNameId.Length == 0 || string.IsNullOrWhiteSpace(name.DisplayName))
                Uncertain(proposal, "The expected name needs an identifier and a nonempty display name.");
            proposal.Notes.Add(new() { Name = "Expected subclass", Text = name.DisplayName });
            expected.Add((name, proposal)); result.Proposals.Add(proposal);
        }
        for (int tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            var table = tables[tableIndex];
            var candidates = table.FamilyKeys.Where(keys.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (table.Table.Rows.Count == 0)
            {
                var empty = new PanelSubclassProposal { ProposalId = ImportId("table", tableIndex.ToString(CultureInfo.InvariantCulture), table.SheetName),
                    CandidateFamilyKeys = candidates, IsUncertain = true,
                    Reason = "The subclass source sheet has no class rows: " + table.SheetName + "." };
                empty.Notes.Add(new() { Name = "Source sheet", Text = table.SheetName });
                empty.Notes.Add(new() { Name = "Source headers", Text = JsonSerializer.Serialize(table.Table.Headers) });
                result.Proposals.Add(empty);
            }
            foreach (var row in table.Table.Rows)
            {
                var rowKeys = table.FamilyKeyColumn.Length == 0 ? candidates.ToList()
                    : candidates.Where(key => key == row.Cells.GetValueOrDefault(table.FamilyKeyColumn, "").Trim()).ToList();
                string family = rowKeys.Count == 1 ? rowKeys[0] : "";
                string name = row.Cells.GetValueOrDefault(table.ClassColumn, "").Trim();
                var matched = family.Length == 0 || name.Length == 0 ? new List<PanelSubclassProposal>()
                    : expected.Where(item => item.Name.FamilyKey == family && item.Name.DisplayName.Trim() == name).Select(item => item.Proposal).ToList();
                if (matched.Count == 0)
                {
                    var added = new PanelSubclassProposal
                    {
                        ProposalId = ImportId("row", tableIndex.ToString(CultureInfo.InvariantCulture), table.SheetName, row.RowNumber.ToString(CultureInfo.InvariantCulture)),
                        FamilyKey = family, Key = name, DisplayName = name,
                        CandidateFamilyKeys = rowKeys, Reason = "The subclass table row retains its source text and proposed rules for review.",
                    };
                    if (family.Length == 0) Uncertain(added, "This row has no unique associated family; choose one shown family key.");
                    if (name.Length == 0) Uncertain(added, "The subclass name is empty; enter a name before accepting this row.");
                    result.Proposals.Add(added); matched.Add(added);
                }
                foreach (var proposal in matched)
                {
                    bool repeated = proposal.HasMatchedTableRow;
                    proposal.HasMatchedTableRow = true;
                    if (!repeated && family.Length > 0 && name.Length > 0) proposal.Reason += " The subclass table row matches this family and subclass name; its rules are proposals only.";
                    if (repeated) Uncertain(proposal, "More than one architecture row names this subclass; review their rule choices rather than assuming one combined architecture.");
                    if (matched.Count > 1) Uncertain(proposal, "More than one expected name matches this row; choose which definition to retain.");
                    var source = new PanelSubclassSourceRow { SheetName = table.SheetName, RowNumber = row.RowNumber,
                        Cells = new Dictionary<string, string>(row.Cells, StringComparer.Ordinal) };
                    AddSource(proposal.SourceRows, source);
                    foreach (string column in table.DomainColumns.Distinct(StringComparer.Ordinal))
                    {
                        var parsed = vocabulary.Read(row.Cells.GetValueOrDefault(column, ""), column, source);
                        if (parsed.UnreadText) Uncertain(proposal, "Some rule-cell text was not recognized; it remains verbatim in the subclass notes.");
                        foreach (var rule in parsed.Rules)
                        {
                            rule.RuleId = ImportId(proposal.ProposalId, rule.Accession, rule.Accession.Length == 0 ? rule.Name : "", rule.Kind.ToString());
                            var existing = proposal.Rules.FirstOrDefault(item => item.RuleId == rule.RuleId);
                            if (existing == null) proposal.Rules.Add(rule);
                            else
                            {
                                foreach (var origin in rule.SourceRows) AddSource(existing.SourceRows, origin);
                                if (rule.IsUncertain)
                                {
                                    if (!existing.IsUncertain) existing.Reason = rule.Reason;
                                    else if (!existing.Reason.Contains(rule.Reason, StringComparison.Ordinal)) existing.Reason += " " + rule.Reason;
                                    existing.IsUncertain = true;
                                }
                            }
                            if (rule.IsUncertain) Uncertain(proposal, rule.Reason);
                        }
                    }
                    if (proposal.Rules.Count == 0) Uncertain(proposal, "No uniquely recognized Pfam rule was found; the subclass name and source text remain available.");
                }
            }
        }
        foreach (var proposal in result.Proposals)
        {
            // A changed certainty state must not inherit an accepted draft for the formerly certain rule.
            foreach (var rule in proposal.Rules.Where(rule => rule.IsUncertain && rule.Accession.Length > 0))
                rule.RuleId = ImportId(rule.RuleId, "uncertain");
            if (!proposal.HasMatchedTableRow && proposal.ExpectedNameId.Length > 0)
                Uncertain(proposal, "No matching subclass table row was proposed for this expected name.");
            if (proposal.IsUncertain) proposal.Notes.Add(new() { Name = "Import review", Text = proposal.Reason });
            proposal.Notes.AddRange(SourceFields(proposal.SourceRows));
        }
        return result;
    }

    private static string ImportId(params string[] parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts))));
    private static void Uncertain(PanelSubclassProposal proposal, string reason)
    {
        proposal.IsUncertain = true;
        if (!proposal.Reason.Contains(reason, StringComparison.Ordinal)) proposal.Reason = proposal.Reason.Length == 0 ? reason : proposal.Reason + " " + reason;
    }
    private static PanelSubclassSourceRow CopyRow(PanelSubclassSourceRow source) => new()
        { SheetName = source.SheetName, RowNumber = source.RowNumber, Cells = new(source.Cells, StringComparer.Ordinal) };
    private static void AddSource(List<PanelSubclassSourceRow> rows, PanelSubclassSourceRow source)
    {
        if (!rows.Any(row => row.SheetName == source.SheetName && row.RowNumber == source.RowNumber && row.Cells.Count == source.Cells.Count
            && row.Cells.All(cell => source.Cells.TryGetValue(cell.Key, out string? value) && value == cell.Value))) rows.Add(CopyRow(source));
    }
    private static IEnumerable<FamilyPanelText> SourceFields(IEnumerable<PanelSubclassSourceRow> rows) => rows.SelectMany(row => row.Cells.Select(cell =>
        new FamilyPanelText { Name = row.SheetName + "!" + row.RowNumber.ToString(CultureInfo.InvariantCulture) + ":" + cell.Key, Text = cell.Value }));
}
