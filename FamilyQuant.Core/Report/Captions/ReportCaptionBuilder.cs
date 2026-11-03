using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report.Captions;

/// <summary>A measured value supplied by the same snapshot that draws a figure. Missing values are notes, not zero.</summary>
public sealed record CaptionNumber(string Label, double Value, string Unit = "", string? Format = null);
public sealed record FigureCaptionInput(string What, string How, IReadOnlyList<CaptionNumber> Numbers,
    IReadOnlyList<string>? Notes = null);
public sealed record TableCaptionInput(string What, string How, int LabelColumn = 0,
    IReadOnlyList<int>? SummaryColumns = null);

/// <summary>Deterministic, general explanations. Numbers are provided data or exact cells; PNG bytes are never interpreted.</summary>
public static class ReportCaptionBuilder
{
    public static string Number(double value, IFormatProvider? culture = null, string? format = null)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("A caption number must be finite.", nameof(value));
        format ??= value != 0 && Math.Abs(value) < .01 ? "0.###E+0" : "0.##";
        return value.ToString(format, culture ?? CultureInfo.InvariantCulture);
    }

    public static string Figure(FigureCaptionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sentences = new List<string> { Sentence(input.What), Sentence(input.How) };
        foreach (var fact in input.Numbers)
        {
            if (string.IsNullOrWhiteSpace(fact.Label)) throw new ArgumentException("A caption fact needs a label.", nameof(input));
            string unit = fact.Unit.Length == 0 ? "" : (fact.Unit == "%" ? "%" : " " + fact.Unit);
            sentences.Add(Sentence(fact.Label + ": " + Number(fact.Value, format: fact.Format) + unit));
        }
        if (input.Notes != null) sentences.AddRange(input.Notes.Where(s => !string.IsNullOrWhiteSpace(s)).Select(Sentence));
        return string.Join(" ", sentences);
    }

    public static string Table(ReportTable table, TableCaptionInput? input = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Rows.Any(row => row.Count != table.Columns.Count))
            throw new ArgumentException("Caption table rows must match their column headings.", nameof(table));
        var sentences = new List<string>
        {
            Sentence(input?.What ?? table.Title),
            Sentence(input?.How ?? "Read across each row; column headings identify the measures and units")
        };
        // A workbook-only detail table contributes a Word reference, not a displayed grid.
        // Its dimensions are structural metadata, not source measurements for the number trace.
        if (!table.SupplementOnly)
            sentences.Add("The table shows " + table.Rows.Count.ToString(CultureInfo.InvariantCulture) + (table.Rows.Count == 1 ? " data row" : " data rows")
                + " across " + table.Columns.Count.ToString(CultureInfo.InvariantCulture) + (table.Columns.Count == 1 ? " column." : " columns."));
        if (table.Columns.Count == 0 || table.Rows.Count == 0) return string.Join(" ", sentences);
        int labelColumn = input?.LabelColumn ?? 0;
        if (labelColumn < 0 || labelColumn >= table.Columns.Count) throw new ArgumentOutOfRangeException(nameof(input));
        bool Numeric(string cell, out double value)
        {
            string text = cell.Trim();
            if (text.EndsWith('%')) text = text[..^1].TrimEnd();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        }
        var columns = input?.SummaryColumns ?? Enumerable.Range(0, table.Columns.Count)
            .Where(c => c != labelColumn && table.Rows.Any(r => Numeric(r[c], out _))).Take(1).ToArray();
        foreach (int column in columns)
        {
            if (column < 0 || column >= table.Columns.Count) throw new ArgumentOutOfRangeException(nameof(input));
            var values = table.Rows.Select((row, i) => Numeric(row[column], out double value)
                ? (Index: i, Value: (double?)value) : (Index: i, Value: (double?)null)).Where(p => p.Value.HasValue).ToArray();
            if (values.Length == 0) continue;
            double largest = values.Max(p => p.Value!.Value);
            var tied = values.Where(p => p.Value == largest).ToArray();
            var row = table.Rows[tied[0].Index];
            sentences.Add(Sentence("The largest displayed value in " + table.Columns[column] + " is " + row[column]
                + " for " + row[labelColumn] + (tied.Length > 1
                    ? " (one of " + tied.Length.ToString(CultureInfo.InvariantCulture) + " tied rows)" : "")));
        }
        return string.Join(" ", sentences);
    }

    /// <summary>Fills every table and figure from data bindings; a figure without its binding is refused.</summary>
    public static ReportDocument WithCaptions(ReportDocument document,
        IReadOnlyDictionary<string, FigureCaptionInput> figures,
        IReadOnlyDictionary<string, TableCaptionInput>? tables = null) => document with
    {
        Sections = document.Sections.Select(section => section with
        {
            Blocks = section.Blocks.Select(block => block switch
            {
                ReportFigure figure => figure with { Caption = Figure(figures.TryGetValue(figure.Id, out var input)
                    ? input : throw new InvalidOperationException("Caption data is unavailable for figure: " + figure.Id)) },
                ReportTable table => table with { Caption = Table(table, tables != null && tables.TryGetValue(table.Id, out var input) ? input : null) },
                _ => block
            }).ToArray()
        }).ToArray()
    };

    private static string Sentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Captions need both a description and reading guidance.");
        text = text.Trim(); return text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') ? text : text + ".";
    }
}
