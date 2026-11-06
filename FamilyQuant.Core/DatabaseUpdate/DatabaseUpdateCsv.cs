using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DatabaseUpdate;

/// <summary>Exports the supplied preview or saved changes without matching, placement or mutation.</summary>
public static class DatabaseUpdateCsv
{
    private static readonly string[] Columns = ["Kind", "Step", "Picked", "Ambiguous case", "Decoy", "Contaminant", "Old index", "New index",
        "Old identifier", "New identifier", "Family ID", "USearch ID", "Family name", "Placement", "Placed by", "Identity (%)", "Aligned length",
        "Joined locked family", "Other linked family IDs", "Note"];
    public static string Build(IReadOnlyList<DatabaseUpdateRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var text = new StringBuilder();
        void Line(IEnumerable<string?> cells) => text.Append(string.Join(",", cells.Select(CsvFormatting.Guarded))).Append("\r\n");
        string Number(IFormattable? value) => value?.ToString(null, CultureInfo.InvariantCulture) ?? "";
        string Bool(bool value) => value ? "true" : "false";
        Line(Columns);
        foreach (var row in rows)
            Line(new[] { row.Kind.ToString(), row.Step?.ToString() ?? "", Bool(row.Picked), Number(row.AmbiguousCase), Bool(row.Decoy), Bool(row.Contaminant),
                Number(row.OldIndex), Number(row.NewIndex), row.OldIdentifier, row.NewIdentifier, Number(row.FamilyUserId), Number(row.FamilySearchId), row.FamilyName,
                row.Placement.ToString(), row.PlacedBy, row.PlacedByIdentityPercent?.ToString("R", CultureInfo.InvariantCulture) ?? "", Number(row.PlacedByAlignmentLength),
                Bool(row.JoinedLockedFamily), string.Join(";", (row.OtherLinkedFamilies ?? new()).Select(i => i.ToString(CultureInfo.InvariantCulture))), row.Note });
        return text.ToString();
    }
    public static void Write(string path, IReadOnlyList<DatabaseUpdateRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Build(rows), new UTF8Encoding(false));
    }
}
