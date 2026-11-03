using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.Report;

/// <summary>Turns the program's invariant CSV cells into report cells without reformatting numbers.</summary>
public static class ReportCsvTable
{
    public static ReportTable Build(string id, string title, string csv, string caption, params string[] columns)
    {
        var parsed = Parse(csv);
        if (parsed.Count == 0) throw new InvalidDataException("The report source CSV has no header.");
        var header = parsed[0];
        if (header.Count != header.Distinct(StringComparer.Ordinal).Count()) throw new InvalidDataException("The report source CSV has duplicate column names.");
        var selected = columns.Length == 0 ? header.ToArray() : columns;
        int[] indexes = selected.Select(c => header.IndexOf(c)).ToArray();
        if (indexes.Any(i => i < 0)) throw new InvalidDataException("A requested report column is absent from the program export.");
        if (parsed.Skip(1).Any(row => row.Count != header.Count)) throw new InvalidDataException("The report source CSV has an inconsistent row width.");
        var rows = parsed.Skip(1).Select(row => (IReadOnlyList<string>)indexes.Select(i => row[i]).ToArray()).ToArray();
        return new(id, title, selected, rows, caption) { ListColumns = ReportListSheets.Describe(id, selected, rows) };
    }

    private static List<List<string>> Parse(string csv)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder();
        bool quoted = false, closed = false, started = false;
        for (int index = 0; index < csv.Length; index++)
        {
            char value = csv[index];
            if (quoted)
            {
                if (value == '"')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '"') { cell.Append('"'); index++; }
                    else { quoted = false; closed = true; }
                }
                else cell.Append(value);
            }
            else if (value is ',' or '\r' or '\n')
            {
                row.Add(cell.ToString()); cell.Clear(); closed = false; started = false;
                if (value != ',')
                {
                    rows.Add(row); row = new();
                    if (value == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n') index++;
                }
            }
            else if (value == '"' && !started && !closed) { quoted = true; started = true; }
            else
            {
                if (closed || value == '"') throw new InvalidDataException("Malformed quoted field in report source CSV.");
                cell.Append(value); started = true;
            }
        }
        if (quoted) throw new InvalidDataException("Unclosed quoted field in report source CSV.");
        if (cell.Length > 0 || row.Count > 0 || started || closed) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
