using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Captures the native Sensitivity charts without clustering, extraction or re-quantitation.</summary>
public sealed class HtmlSensitivityFigurePacket
{
    private sealed record Cell(int Index, string Text, double? Value, string Unit);
    private sealed record Part(string Label, Cell Value);
    private sealed record Row(Cell Name, Cell? X, IReadOnlyList<Part> Parts, Cell? Total = null, Cell? Note = null);
    private sealed record Marker(string Label, Cell Identity);
    private sealed record Panel(string Kind, string Title, string Basis, string XLabel, string YLabel,
        double MinimumX, double MaximumX, double MaximumY, bool ReverseX,
        IReadOnlyList<HtmlSensitivityTick> XTicks, IReadOnlyList<HtmlSensitivityTick> YTicks,
        IReadOnlyList<Row> Rows, IReadOnlyList<Marker> Markers);
    private sealed class Plot(string id, string kind, string title, string sheet)
    {
        public string Id { get; } = id;
        public string Kind { get; } = kind;
        public string Title { get; } = title;
        public string Sheet { get; } = sheet;
        public string Legend { get; set; } = "";
        public List<Cell> Cells { get; } = new();
        public List<Panel> Panels { get; } = new();
        public Cell Text(string text) { var cell = new Cell(Cells.Count, text, null, ""); Cells.Add(cell); return cell; }
        public Cell Number(double value, string unit)
        {
            if (!double.IsFinite(value)) throw new ArgumentException("A native sensitivity value is not finite.");
            var cell = new Cell(Cells.Count, value.ToString("R", CultureInfo.InvariantCulture), value, unit);
            Cells.Add(cell); return cell;
        }
    }
    private readonly IReadOnlyList<Plot> plots;
    public IReadOnlyList<ReportSupplementSheet> Sheets { get; }
    private HtmlSensitivityFigurePacket(IReadOnlyList<Plot> plots)
    {
        this.plots = plots;
        Sheets = plots.Select(plot => new ReportSupplementSheet("html-sensitivity:" + plot.Id, ReportSectionIds.Sensitivity,
            plot.Sheet, new ReportTable("html-sensitivity:" + plot.Id, "Native HTML drawing values: " + plot.Title,
                new[] { "Cell", "Text", "Value", "Unit" }, plot.Cells.Select(cell => (IReadOnlyList<string>)new[]
                { cell.Index.ToString(CultureInfo.InvariantCulture), cell.Text, cell.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "", cell.Unit }).ToArray(),
                "Exact native Sensitivity readings. Axis geometry changes no stored result.")) { SourceTableId = plot.Id }).ToArray();
    }

    public HtmlSensitivityFigures Build(Func<string, HtmlReportReference, string, double?, string> add, ReportSelection? selection)
    {
        var excluded = new HashSet<string>(selection?.ExcludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var figures = new List<HtmlSensitivityFigure>();
        foreach (var plot in plots)
        {
            if (excluded.Contains(ReportSectionIds.Sensitivity) || excluded.Contains(plot.Id) || excluded.Contains("html-sensitivity:" + plot.Id)) continue;
            var names = new Dictionary<int, string>();
            var numbers = new Dictionary<int, HtmlSensitivityNumber>();
            HtmlReportReference Source(Cell cell) => new() { Kind = "workbook-cell", Sheet = plot.Sheet,
                Row = cell.Index + 2, Column = cell.Value.HasValue ? 3 : 2 };
            string Name(Cell cell)
            {
                if (!names.TryGetValue(cell.Index, out string? id)) names[cell.Index] = id = add(cell.Text, Source(cell), "", null);
                return id;
            }
            HtmlSensitivityNumber? Number(Cell? cell)
            {
                if (cell == null) return null;
                string format = cell.Unit is "groups" or "sequences" ? "N0" : "G3";
                if (!numbers.TryGetValue(cell.Index, out var number)) numbers[cell.Index] = number = new(
                    add(cell.Text, Source(cell) with { Format = "R" }, cell.Unit, cell.Value),
                    add(cell.Value!.Value.ToString(format, CultureInfo.InvariantCulture), Source(cell) with { Format = format }, cell.Unit, cell.Value));
                return number;
            }
            figures.Add(new(plot.Id, plot.Kind, plot.Title, plot.Legend, plot.Panels.Select(panel => new HtmlSensitivityPanel(
                panel.Kind, panel.Title, panel.Basis, panel.XLabel, panel.YLabel, panel.MinimumX, panel.MaximumX, panel.MaximumY,
                panel.ReverseX, panel.XTicks, panel.YTicks,
                panel.Rows.Select(row => new HtmlSensitivityRow(Name(row.Name), Number(row.X),
                    row.Parts.Select(part => new HtmlSensitivityPart(part.Label, Number(part.Value)!)).ToArray(),
                    Number(row.Total), row.Note == null ? null : Name(row.Note))).ToArray(),
                panel.Markers.Select(marker => new HtmlSensitivityMarker(marker.Label, Number(marker.Identity)!)).ToArray())).ToArray()));
        }
        return new() { Figures = figures };
    }

    public static bool Owns(string id) => id is "figure:identity_scan.png" || id == "figure:" + PresentationFigures.IdentitySweepFile
        || id == "figure:" + PresentationFigures.DeNovoVariantComparisonFile || id == "figure:" + PresentationFigures.DeNovoTalkComparisonFile;

    public static HtmlSensitivityFigurePacket Prepare(ReportFigurePlan plan)
    {
        var plots = new List<Plot>();
        foreach (var figure in plan.Charts.Where(figure => Owns("figure:" + figure.FileName)))
        {
            var native = figure.CreateChart();
            string kind = native switch { IdentityScanChart => "identity", ReferenceIdentitySweepChart => "reference",
                DeNovoVariantComparisonChart => "variants", DeNovoTalkComparisonChart => "effects", _ => throw new ArgumentException("Unknown sensitivity chart.") };
            var plot = new Plot("figure:" + figure.FileName, kind, figure.Title, "Sensitivity native " + (plots.Count + 1).ToString(CultureInfo.InvariantCulture));
            plots.Add(plot);
            if (native is IdentityScanChart scan)
            {
                var rows = scan.Rows.OrderByDescending(row => row.Identity).ToArray();
                double maxMixing = IdentityScanPlot.MaxMixing(rows);
                bool mixing = rows.Any(row => row.Groups != row.TargetGroups || row.MixedGroups != 0) && maxMixing > 0;
                var markers = new List<Marker>();
                int? floor = mixing ? IdentityScanPlot.CleanFloor(rows) : null;
                if (floor.HasValue) markers.Add(new("Clean-range floor", plot.Number(floor.Value, "% identity")));
                if (scan.CurrentIdentity.HasValue && rows.Any(row => row.Identity == scan.CurrentIdentity))
                    markers.Add(new("This file", plot.Number(scan.CurrentIdentity.Value, "% identity")));
                plot.Panels.Add(Line("Target connected components", "Non-contaminant target connected components at each recorded identity", "Connected components",
                    rows.Select(row => LineRow(row.Identity, new Part("Target connected components", plot.Number(row.ReportedTargetGroups, "groups")))) .ToArray(),
                    IdentityScanPlot.MaxFamilies(rows), false, markers));
                if (mixing) plot.Panels.Add(Line("Target-decoy mixing", "Mixed groups divided by all connected components at the same identity, including decoy-only groups", "Mixed groups (%)",
                    rows.Select(row => LineRow(row.Identity, new Part("Target-decoy mixing", plot.Number(row.Score * 100, "%")))).ToArray(), maxMixing * 100, true, markers));
                plot.Legend = "Each point is a stored identity threshold, strictest on the left. Counts and the mixing percentage use separate labelled panels; mixing divides by all groups and is not an identification FDR. The clean-range floor marks the uninterrupted clean range from the strictest threshold, not an optimum or protection against target-target fusions. "
                    + (mixing ? "Lines connect recorded points; no run means or uncertainty whiskers are drawn." : "No mixing series or clean-range floor is drawn when the native chart has no decoys or no positive mixing score.")
                    + " Changing Normalize to does not change this grouping study.";
            }
            else if (native is ReferenceIdentitySweepChart reference)
            {
                var rows = reference.Rows;
                var markers = rows.Any(row => row.Identity == reference.SelectedIdentity)
                    ? new[] { new Marker("Selected identity", plot.Number(reference.SelectedIdentity, "% identity")) } : Array.Empty<Marker>();
                plot.Panels.Add(Line("Reference groups", "Connected components at each recorded identity; at least one qualifying sequence per group", "Connected components",
                    rows.Select(row => LineRow(row.Identity, new Part("At least one target", plot.Number(row.TargetGroups, "groups")),
                        new Part("At least one non-contaminant target", plot.Number(row.ReportedTargetGroups, "groups")))).ToArray(),
                    Math.Max(50, Math.Ceiling(rows.Select(row => row.TargetGroups).DefaultIfEmpty(0).Max() / 50.0) * 50), false, markers));
                plot.Legend = "Each point is a stored identity threshold of at least 25%, strictest on the left. One line counts groups containing a target, including those labelled contaminant; the other requires a non-contaminant target. These inclusion-based group counts are not target-decoy mixing rates. Lines join stored points, with no run averaging or whiskers. Changing Normalize to does not change the counts.";
            }
            else if (native is DeNovoVariantComparisonChart variants)
            {
                var rows = variants.Rows.Select(row => new Row(plot.Text(row.Variant), null, new[]
                { new Part("Identified measured envelope", plot.Number(row.IdentifiedFractionOfTic * 100, "%")),
                    new Part("De novo measured envelope", plot.Number(row.DeNovoModelEstimateFractionOfTic * 100, "%")) },
                    plot.Number(row.CombinedModelEstimateFractionOfTic * 100, "%"),
                    plot.Text(row.ConfidenceCutoffNote + "; " + row.DeNovoRepresentatives.ToString(CultureInfo.InvariantCulture) + " credited de novo peptide features"))).ToArray();
                double max = Math.Ceiling(Math.Max(.01, variants.Rows.Max(row => row.CombinedModelEstimateFractionOfTic) * 1.18) * 20) / 20 * 100;
                plot.Panels.Add(Bars("Measured signal by source variant", "Share of the same pooled total ion current (TIC), all runs pooled", "Share of pooled TIC (%)", rows, max, true));
                plot.Legend = "Each row is a source variant actually computed and stored in the project. Identified and de novo parts both use measured isotope envelopes and divide by the same pooled TIC; no sensitivity interval or run-to-run whisker is drawn. Confidence fields and cutoffs remain source-specific: ALC and SCORE are not a common scale. Changing Normalize to does not change this fixed TIC comparison.";
            }
            else if (native is DeNovoTalkComparisonChart effects)
            {
                var sequenceRows = effects.Rows.Skip(1).Select(row => new Row(plot.Text(row.Condition), null,
                    new[] { new Part("Carried-forward distinct sequences", plot.Number(row.CarriedForwardDistinctSequences, "sequences")) },
                    plot.Number(row.CarriedForwardDistinctSequences, "sequences"))).ToArray();
                plot.Panels.Add(Bars("Carried-forward sequence diversity", "Distinct sequences with PTM notation removed; runs and charge pooled; I/L distinct", "Distinct sequences", sequenceRows,
                    Math.Max(1, effects.Rows.Skip(1).Max(row => row.CarriedForwardDistinctSequences)), false));
                var signalRows = effects.Rows.Select(row => new Row(plot.Text(row.Condition), null, new[]
                { new Part("Observed search baseline", plot.Number(row.ObservedSearchAssignedFractionOfTic * 100, "%")),
                    new Part("De novo measured envelope", plot.Number(row.DeNovoModelEstimateFractionOfTic * 100, "%")) },
                    plot.Number(row.CombinedAssignedFractionOfTic * 100, "%"))).ToArray();
                plot.Panels.Add(Bars("Assigned signal", "Share of the same pooled TIC, using one common observed search baseline", "Share of pooled TIC (%)", signalRows,
                    Math.Max(.01, effects.Rows.Max(row => row.CombinedAssignedFractionOfTic) * 1.12) * 100, true));
                plot.Legend = "The first panel counts carried-forward distinct sequences; these counts are sequence diversity, not peptide feature signal. The second shows the same observed search baseline plus each condition's measured de novo envelope, divided by the same pooled TIC. All runs are pooled, without run means or whiskers. Both callers uses PEAKS de novo-only plus DeepNovo; the PEAKS-all union remains a separate variant. Changing Normalize to does not change either fixed base.";
            }

            Row LineRow(int identity, params Part[] parts) => new(plot.Text(identity.ToString(CultureInfo.InvariantCulture) + "% identity"), plot.Number(identity, "% identity"), parts);
            Panel Line(string title, string basis, string yLabel, IReadOnlyList<Row> rows, double maximum, bool percent, IReadOnlyList<Marker> markers)
            {
                double lo = rows.Select(row => row.X!.Value!.Value).DefaultIfEmpty(0).Min(), hi = rows.Select(row => row.X!.Value!.Value).DefaultIfEmpty(1).Max();
                if (hi == lo) { lo = Math.Max(0, lo - 5); hi = Math.Min(100, hi + 5); }
                var x = Axis(lo, hi, true, true);
                var y = Axis(0, maximum, percent, !percent);
                return new("line", title, basis, "Sequence identity, strictest on the left (%)", yLabel, x.Minimum, x.Maximum, y.Maximum, true,
                    x.Ticks, y.Ticks, rows, markers);
            }
            Panel Bars(string title, string basis, string label, IReadOnlyList<Row> rows, double maximum, bool percent)
            {
                var x = Axis(0, maximum, percent, !percent);
                return new("bars", title, basis, label, "", x.Minimum, x.Maximum, 1, false, x.Ticks,
                    Array.Empty<HtmlSensitivityTick>(), rows, Array.Empty<Marker>());
            }
        }
        return new(plots);
    }

    private static (double Minimum, double Maximum, IReadOnlyList<HtmlSensitivityTick> Ticks) Axis(double minimum, double maximum, bool percent, bool integer)
    {
        double rawStep = Math.Max(maximum - minimum, 1e-12) / 4;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep))), fraction = rawStep / magnitude;
        double step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * magnitude;
        if (integer) step = Math.Max(1, step);
        double lo = Math.Floor(minimum / step) * step, hi = Math.Ceiling(maximum / step) * step;
        if (hi <= lo) hi = lo + step;
        int intervals = (int)Math.Round((hi - lo) / step);
        var ticks = Enumerable.Range(0, intervals + 1).Select(index =>
        {
            double value = lo + index * step;
            return new HtmlSensitivityTick(value, value.ToString(integer ? "N0" : "G6", CultureInfo.InvariantCulture) + (percent ? "%" : ""));
        }).ToArray();
        return (lo, hi, ticks);
    }
}
