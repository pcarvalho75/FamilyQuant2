using System;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Rates and evidence states read from the same stored composition rows as the sources plot.</summary>
public static class ReportSourceSupport
{
    public static ReportTable Build(FamilyWorldSummary result, FamilyReportThresholdResult? grouping = null)
    {
        var table = ReportCsvTable.Build("source-world-support", "Support beside the source contributions",
            ReportStudyExports.SourceSupport(result),
            "Stored family-world decoy signal / credited signal and correct known credits / known credits. The stored minimum support is "
                + result.MinimumKnownCredits.ToString(CultureInfo.InvariantCulture)
                + " known credits. Missing rates stay unavailable; too-little-evidence states remain explicit. The sources figure alone is not a validation claim."
                + (grouping?.OtherFamilies.Count > 0 ? " Others has no combined quality rate; its individual stored support remains in the companion workbook." : ""),
            "family", "decoy_ratio_pct", "right_rate_pct", "known_credits", "correct_known_credits", "evidence");
        return ReportWordMembership.FilterWorld(table with
        {
            Columns = new[] { "Stored family", "Decoy ratio (%)", "Right rate (%)", "Known credits", "Correct credits", "Evidence" },
            Rows = table.Rows.Select(row => (System.Collections.Generic.IReadOnlyList<string>)row.Select((cell, index) => index is 1 or 2 && cell.Length == 0 ? "Unavailable" : cell).ToArray()).ToArray(),
        }, result, grouping);
    }
}
