using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Panels.Import;

public sealed partial class FamilyPanelSpreadsheetImporter
{
    private static List<FamilyPanelDomain> ReadSheetPfam(string text)
    {
        var names = NamedPfam.Matches(text).ToDictionary(match => match.Groups["accession"].Index, match => match.Groups["name"].Value.Trim());
        return PfamAccession.Matches(text).Select(match => (Accession: match.Value, Name: names.GetValueOrDefault(match.Index, string.Empty)))
            .Distinct().Select(domain => new FamilyPanelDomain { Accession = domain.Accession, Name = domain.Name }).ToList();
    }

    private static void PopulateWorkbookPfam(PanelWorkbookProposal proposal)
    {
        if (proposal.NeedsFamilySheetChoice || proposal.FamilyTable is not { } table) return;
        var columns = proposal.FamilyProposal.Choices.ColumnHeaders;
        if (!columns.TryGetValue(PanelImportFields.Pfam, out var domainColumn) || !table.Headers.Contains(domainColumn)) return;
        string keyColumn = columns.GetValueOrDefault(PanelImportFields.FamilyKey, "");
        foreach (var row in WorkbookDataRows(table))
        {
            var domains = ReadSheetPfam(Value(row, domainColumn));
            if (domains.Count == 0) continue;
            proposal.Pfam.Add(new PanelWorkbookPfamHint { RowNumber = row.RowNumber, FamilyKey = Value(row, keyColumn).Trim(), Column = domainColumn, Domains = domains });
        }
    }
}
