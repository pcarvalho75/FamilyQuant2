using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Keeps extensive per-group diagnostics complete in the workbook, with one Word reference.</summary>
public static class ReportFigureAvailabilityBlocks
{
    public static void Append(List<ReportBlock> blocks, string sectionId, IEnumerable<ReportFigureAvailability> source)
    {
        var entries = source.Where(item => item.SectionId == sectionId).ToArray();
        var notices = blocks.OfType<ReportParagraph>().Select(p => p.Text.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < entries.Length; index++)
        {
            var missing = entries[index];
            if (missing.SupplementOnly) continue;
            string message = missing.Title + ": " + missing.Reason;
            if (notices.Add(message.Trim()))
                blocks.Add(new ReportParagraph(sectionId + ":unavailable-figure:" + index.ToString(CultureInfo.InvariantCulture), message));
        }
        var details = entries.Where(item => item.SupplementOnly).ToArray();
        if (details.Length > 0)
        {
            string count = details.Length.ToString(CultureInfo.InvariantCulture);
            var rows = new List<IReadOnlyList<string>> { new[] { "Summary", "Unplaced groups", count } };
            rows.AddRange(details.Select(item => (IReadOnlyList<string>)new[] { "Group", item.Title, item.Reason }));
            blocks.Add(new ReportTable(sectionId + ":figure-availability", "Figure availability details",
                new[] { "Record", "Requested figure or measure", "Stored detail" }, rows,
                "Protein coverage placement is unavailable for " + count + " groups. "
                    + string.Join(" ", details.Select(item => item.Reason).Distinct(StringComparer.Ordinal))
                    + " Complete group identifiers and reasons are retained in this sheet. Missing panel placement is not a missing peptide measurement")
                { SupplementOnly = true });
        }
    }
}
