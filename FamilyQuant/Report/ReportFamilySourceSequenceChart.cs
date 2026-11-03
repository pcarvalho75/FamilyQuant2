using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant.Report;

/// <summary>Report pages of the existing source census. Counts and disjoint source sets are unchanged.</summary>
public sealed class ReportFamilySourceSequenceChart : ReportPrintChart
{
    public const int SelectionLimit = 8;
    public IReadOnlyList<FamilySourceSequenceRow> Rows { get; }
    public ReportFamilySourceSequenceChart(IEnumerable<FamilySourceSequenceRow> rows) => Rows = rows.ToArray();

    private static FormattedText NameLabel(FamilySourceSequenceRow row)
    {
        var name = Text(FamilyFigureLabel.Format(row.FamilyUserId, row.Description, int.MaxValue), ChartPalette.Light.Ink, 16, true);
        name.MaxTextWidth = LayoutWidth - 40;
        if (name.Height <= 90) return name;
        // Pathological descriptions do not make a page unbounded. The supplement retains the full name.
        name = Text("Family " + row.FamilyUserId.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (full name in companion workbook)", ChartPalette.Light.Ink, 16, true);
        name.MaxTextWidth = LayoutWidth - 40;
        return name;
    }

    public static double RequiredHeight(IReadOnlyList<FamilySourceSequenceRow> rows) =>
        Math.Max(360, 112 + rows.Sum(row => NameLabel(row).Height + 74) + 84);

    public static IReadOnlyList<IReadOnlyList<FamilySourceSequenceRow>> Pages(FamilySourceSequenceSummary summary)
    {
        var pages = new List<IReadOnlyList<FamilySourceSequenceRow>>();
        var page = new List<FamilySourceSequenceRow>();
        foreach (var row in summary.Families.Where(r => r.DeNovoUnionDistinctSequences > 0)
            .OrderByDescending(r => r.DeNovoUnionDistinctSequences).ThenBy(r => r.FamilyUserId).Take(SelectionLimit))
        {
            if (page.Count > 0 && (page.Count == 3 || RequiredHeight(page.Append(row).ToArray()) > 560))
            {
                pages.Add(page.ToArray()); page.Clear();
            }
            page.Add(row);
        }
        if (page.Count > 0 || pages.Count == 0) pages.Add(page.ToArray());
        return pages;
    }

    protected override void OnRender(DrawingContext dc)
    {
        Ground(dc, "Family source sequence evidence", "Pooled PTM-cleaned sequences; search is separate context.");
        if (Rows.Count == 0)
        {
            Label(dc, "No source-resolved de novo family sequences are stored.", 20, 130);
            return;
        }
        int maximum = Math.Max(1, Rows.Max(row => row.DeNovoUnionDistinctSequences));
        double y = 112;
        foreach (var row in Rows)
        {
            var name = NameLabel(row); name.SetForegroundBrush(palette.Ink);
            dc.DrawText(name, new Point(20, y));
            y += name.Height + 8;
            double x = 20;
            var pieces = new[] { (row.PeaksDeNovoOnlyExclusiveDistinctSequences, palette.Identified),
                (row.SharedPeaksDeepNovoDistinctSequences, palette.Mixing), (row.DeepNovoExclusiveDistinctSequences, palette.DeNovo) };
            foreach (var (count, ink) in pieces)
            {
                double width = (ActualWidth - 155) * count / maximum;
                if (width > 0) dc.DrawRectangle(ink, null, new Rect(x, y, width, 22));
                x += width;
            }
            Label(dc, Count(row.DeNovoUnionDistinctSequences), x + 12, y, bold: true);
            Label(dc, "Search: " + Count(row.SearchDistinctSequences) + " (separate sequence set)", 20, y + 30, palette.Muted);
            y += 66;
        }
        var legend = new[] { (palette.Identified, "PEAKS de-novo-only exclusive"),
            (palette.Mixing, "Shared exact sequence"), (palette.DeNovo, "DeepNovo exclusive") };
        foreach (var (ink, text) in legend)
        {
            dc.DrawRectangle(ink, null, new Rect(20, y + 3, 16, 16));
            Label(dc, text, 48, y); y += 25;
        }
    }
}
