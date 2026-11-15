using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Captures the same native chart inputs used by Word. No extraction, attribution or
/// browser-side statistics are used; selected four-base means use the stored share reader.</summary>
public sealed class HtmlFamilyFigurePacket
{
    private sealed record Cell(int Index, string Label, double? Value, string Unit);
    private sealed record Part(Cell Name, Cell Value, bool Unresolved, Cell? FamilyPercent = null);
    private sealed record Row(string Key, Cell Name, Cell? X, Cell? Y, Cell? Lower, Cell? Upper, string Runs, IReadOnlyList<Part> Parts);
    private sealed record View(string Basis, string Legend, string XLabel, string YLabel, double MinimumX,
        double MaximumX, double MaximumY, IReadOnlyList<HtmlFamilyFigureTick> XTicks,
        IReadOnlyList<HtmlFamilyFigureTick> YTicks, IReadOnlyList<Row> Rows)
    {
        public double MinimumY { get; init; }
    }
    private sealed class Plot(string id, string kind, string title, string section, bool normalized, string sheet)
    {
        public string Id { get; } = id;
        public string Kind { get; } = kind;
        public string Title { get; } = title;
        public string Section { get; } = section;
        public bool Normalized { get; } = normalized;
        public string Sheet { get; } = sheet;
        public List<Cell> Cells { get; } = new();
        public Dictionary<string, View> Views { get; } = new(StringComparer.Ordinal);
        public Cell Text(string text) { var cell = new Cell(Cells.Count, text, null, ""); Cells.Add(cell); return cell; }
        public Cell? Number(double? value, string unit)
        {
            if (!value.HasValue) return null;
            if (!double.IsFinite(value.Value)) throw new ArgumentException("A native figure value is not finite.");
            var cell = new Cell(Cells.Count, value.Value.ToString("R", CultureInfo.InvariantCulture), value, unit);
            Cells.Add(cell); return cell;
        }
    }
    private readonly IReadOnlyList<Plot> plots;
    private readonly string? pieUnavailable;
    public IReadOnlyList<ReportSupplementSheet> Sheets { get; }
    private HtmlFamilyFigurePacket(IReadOnlyList<Plot> plots, string? pieUnavailable)
    {
        this.plots = plots;
        this.pieUnavailable = pieUnavailable;
        Sheets = plots.Select(plot => new ReportSupplementSheet("html-families:" + plot.Id, plot.Section, plot.Sheet,
            new ReportTable("html-families:" + plot.Id, "Native HTML drawing values: " + plot.Title,
                new[] { "Cell", "Text", "Value", "Unit" }, plot.Cells.Select(cell => (IReadOnlyList<string>)new[]
                { cell.Index.ToString(CultureInfo.InvariantCulture), cell.Label, cell.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "", cell.Unit }).ToArray(),
                "Exact native drawing values. Display formatting does not change the stored geometry.")) { SourceTableId = plot.Id }).ToArray();
    }

    public HtmlFamilyFigures Build(Func<string, HtmlReportReference, string, double?, string> add, ReportSelection? selection)
    {
        var excluded = new HashSet<string>(selection?.ExcludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var figures = new List<HtmlFamilyFigure>();
        HtmlFamilyFigure? pie = null;
        foreach (var plot in plots)
        {
            if (excluded.Contains(plot.Section) || excluded.Contains(plot.Id) || excluded.Contains("html-families:" + plot.Id)) continue;
            var names = new Dictionary<int, string>();
            var numbers = new Dictionary<int, HtmlFamilyFigureNumber>();
            HtmlReportReference Source(Cell cell) => new() { Kind = "workbook-cell", Sheet = plot.Sheet,
                Row = cell.Index + 2, Column = cell.Value.HasValue ? 3 : 2 };
            string Name(Cell cell)
            {
                if (!names.TryGetValue(cell.Index, out string? id)) names[cell.Index] = id = add(cell.Label, Source(cell), "", null);
                return id;
            }
            HtmlFamilyFigureNumber? Number(Cell? cell)
            {
                if (cell == null) return null;
                string format = cell.Unit == "area" ? "G5" : "G3";
                if (!numbers.TryGetValue(cell.Index, out var number)) numbers[cell.Index] = number = new(
                    add(cell.Label, Source(cell) with { Format = "R" }, cell.Unit, cell.Value),
                    add(cell.Value!.Value.ToString(format, CultureInfo.InvariantCulture), Source(cell) with { Format = format }, cell.Unit, cell.Value));
                return number;
            }
            var views = plot.Views.ToDictionary(pair => pair.Key, pair =>
            {
                var view = pair.Value;
                return new HtmlFamilyFigureView(view.Basis, view.Legend, view.XLabel, view.YLabel,
                    view.MinimumX, view.MaximumX, view.MaximumY, view.XTicks, view.YTicks,
                    view.Rows.Select(row => new HtmlFamilyFigureRow(row.Key, Name(row.Name), Number(row.X), Number(row.Y),
                        Number(row.Lower), Number(row.Upper), row.Runs,
                        row.Parts.Select(part => new HtmlFamilyFigurePart(Name(part.Name), Number(part.Value)!, part.Unresolved, Number(part.FamilyPercent))).ToArray())).ToArray())
                    { MinimumY = view.MinimumY };
            }, StringComparer.Ordinal);
            var figure = new HtmlFamilyFigure(plot.Id, plot.Kind, plot.Title, plot.Normalized, views);
            if (plot.Kind == "pie") pie = figure;
            else figures.Add(figure);
        }
        return new() { Figures = figures, Pie = new(pie, pie == null
            ? pieUnavailable ?? "Include Family signal contributions in the report choices to enable Pie." : null) };
    }

    public static bool Owns(string id) => new[] { "family_composition", "family_signal_sources", "family_signal_vs_denovo_share",
        "family_subclass_ring", "family_subclasses", "abundance_diversity", "family_source_sequence_counts" }
        .Any(stem => id.StartsWith("figure:" + stem, StringComparison.Ordinal));

    public static HtmlFamilyFigurePacket Prepare(ReportStudyFigurePacket studies, ReportFigurePlan legacy,
        ReportCompositionBasis basis, QuantManifest manifest, FamilyEnvelopeQuantResult? families = null,
        IReadOnlyDictionary<int, string>? familyLabels = null, IReadOnlySet<int>? contaminantFamilyIds = null)
    {
        var plots = new List<Plot>();
        foreach (var figure in studies.NativeFigures)
        {
            string id = "figure:" + figure.Plot.FileName;
            if (!Owns(id)) continue;
            Add(id, figure.SectionId, figure.Title, figure.Plot.Chart);
        }
        foreach (var figure in legacy.Charts.Where(figure => Owns("figure:" + figure.FileName)))
            Add("figure:" + figure.FileName, figure.SectionId, figure.Title, figure.CreateChart());
        var pie = FamilyPieData.Build(families, policy: manifest.FamilyRunShares?.Policy == SharedPeptidePolicy.SplitEqually
            ? SharedPeptidePolicy.SplitEqually : SharedPeptidePolicy.FamilyUnique, familyLabels: familyLabels, contaminantFamilyIds: contaminantFamilyIds);
        if (pie.Unavailable == null)
        {
            var plot = new Plot("figure:" + AnalyticsFigures.FamilyContributionFile, "pie", "Family contribution, all runs pooled",
                ReportSectionIds.Results, false, "Native family pie");
            var rows = pie.Slices.Select(slice => new Row(slice.Key, plot.Text(slice.Label), plot.Number(slice.Percent, "%"), null, null, null,
                "All runs pooled" + (slice.Description.Length > 0 ? "; " + slice.Description : ""), new[] { new Part(plot.Text("Identified"), plot.Number(slice.IdentifiedPercent, "%")!, false),
                    new Part(plot.Text("De novo"), plot.Number(slice.DeNovoPercent, "%")!, false) })).ToArray();
            plot.Views["fixed"] = ViewOf(rows, FamilyPieData.Basis, FamilyPieData.Legend, "Share of PSM and de novo (%)", "Source parts on the same base", true);
            plots.Add(plot);
        }
        return new(plots, pie.Unavailable);

        void Add(string id, string section, string title, System.Windows.FrameworkElement native)
        {
            string kind = native switch
            {
                FamilyCompositionChart => "composition", FamilySubclassChart => "subclasses", FamilyRingChart => "ring",
                FamilyAbundanceDiversityChart => "diversity", FamilySignalOriginChart => "sources", FamilySignalShareChart => "scatter",
                ReportFamilySourceSequenceChart => "counts", _ => throw new ArgumentException("Unsupported native Families figure."),
            };
            var plot = new Plot(id, kind, kind == "composition" ? title.Replace("Family composition with run error bars", "Run-to-run spread of family shares", StringComparison.Ordinal) : title, section, kind is "composition" or "diversity", "Native family " + (plots.Count + 1).ToString(CultureInfo.InvariantCulture));
            plots.Add(plot);
            if (native is FamilyCompositionChart composition) MeanViews(plot, composition.Rows, composition.Data.Basis, false);
            else if (native is FamilyAbundanceDiversityChart diversity) MeanViews(plot, diversity.Data.Families, diversity.Data.Basis, true);
            else if (native is FamilySubclassChart subclasses)
            {
                var rows = subclasses.Rows.Select(row => new Row(row.Key, plot.Text(row.Label), plot.Number(row.MeanPercent, "%"), null, null, null,
                    "All runs pooled", row.Subclasses.Select(part => new Part(plot.Text(part.Label), plot.Number(part.Percent, "%")!, part.Key.Length == 0)).ToArray())).ToArray();
                plot.Views["fixed"] = ViewOf(rows, "Pooled credited family signal", "All runs pooled. Subclass parts divide by all pooled signal credited to families, and add to each family's pooled share. Grey parts are unresolved. No run-to-run whiskers. Changing Normalize to does not change this pooled subclass allocation.", "Share of pooled credited family signal (%)", "", true);
            }
            else if (native is FamilyRingChart ring)
            {
                var rows = ring.Families.Select((group, index) => new Row(group.IsOthers ? "others" : group.Members.First().Key,
                    plot.Text(group.Label), plot.Number(group.Percent, "%"), null, null, null, "All runs pooled",
                    group.Parts.Select(part => new Part(plot.Text(part.Label), plot.Number(part.Percent, "%")!, part.IsUnresolved || group.IsUnassigned)).ToArray())).ToArray();
                plot.Views["fixed"] = ViewOf(rows, "Pooled credited family signal", "All runs pooled. Inner arcs show families and outer arcs their recorded subclasses; both divide by pooled signal credited to families. Grey arcs are unresolved or outside reported families. No whiskers. Changing Normalize to does not reallocate these pooled parts.", "Share of pooled credited family signal (%)", "", true);
            }
            else if (native is FamilySignalOriginChart sources)
            {
                var rows = sources.Rows.Select((row, index) => new Row(row.CuratedUserIds.Count == 1 ? row.CuratedUserIds[0].ToString(CultureInfo.InvariantCulture) : "source-" + index,
                    plot.Text(row.Family), plot.Number(row.TotalEnvelope, "area"), null, null, null, "All runs pooled", new[]
                    {
                        new Part(plot.Text("Identified"), plot.Number(row.IdentifiedEnvelope, "area")!, false,
                            plot.Number(row.TotalEnvelope > 0 ? 100 * row.IdentifiedEnvelope / row.TotalEnvelope : null, "%")),
                        new Part(plot.Text("De novo in database"), plot.Number(row.DatabaseDeNovoEnvelope, "area")!, false,
                            plot.Number(row.TotalEnvelope > 0 ? 100 * row.DatabaseDeNovoEnvelope / row.TotalEnvelope : null, "%")),
                        new Part(plot.Text("De novo only in UniProt families"), plot.Number(row.WorldDeNovoEnvelope, "area")!, false,
                            plot.Number(row.TotalEnvelope > 0 ? 100 * row.WorldDeNovoEnvelope / row.TotalEnvelope : null, "%")),
                    })).ToArray();
                plot.Views["fixed"] = ViewOf(rows, "Measured envelope area, all runs pooled", "All runs pooled. Bar parts are identified, database-mapped de novo and envelope areas found only in UniProt families. These are areas, not percentages; shared and unattributed signal stay outside family bars. Changing Normalize to does not change measured areas.", "Measured envelope area", "", false);
            }
            else if (native is FamilySignalShareChart scatter)
            {
                var rows = scatter.Rows.Select(row =>
                {
                    var value = FamilySignalShareChart.Value(row);
                    return new Row(row.FamilyUserId.ToString(CultureInfo.InvariantCulture), plot.Text(FamilyFigureLabel.Format(row.FamilyUserId, scatter.FamilyLabels.GetValueOrDefault(row.FamilyUserId, row.Description), int.MaxValue)),
                        plot.Number(value.Signal, "area"), plot.Number(value.DeNovoShare * 100, "%"), null, null, "All runs pooled", Array.Empty<Part>());
                }).ToArray();
                plot.Views["fixed"] = ViewOf(rows, "Pooled family area; de novo percent of that family's area", "All runs pooled. Horizontal position is total attributed family envelope area; vertical position is de novo as a percent of that same family's area. This is a within-family evidence share, with no run-to-run whiskers. Changing Normalize to does not change either axis.", "Total attributed family envelope area", "De novo share of this family's area (%)", false, true);
            }
            else if (native is ReportFamilySourceSequenceChart census)
            {
                var rows = census.Rows.Select(row => new Row(row.FamilyUserId.ToString(CultureInfo.InvariantCulture),
                    plot.Text(FamilyFigureLabel.Format(row.FamilyUserId, census.FamilyLabels.GetValueOrDefault(row.FamilyUserId, row.Description), int.MaxValue)), plot.Number(row.DeNovoUnionDistinctSequences, "sequences"),
                    plot.Number(row.SearchDistinctSequences, "search sequences"), null, null, "All runs pooled", new[]
                    {
                        new Part(plot.Text("PEAKS de novo only"), plot.Number(row.PeaksDeNovoOnlyExclusiveDistinctSequences, "sequences")!, false),
                        new Part(plot.Text("Shared exact sequence"), plot.Number(row.SharedPeaksDeepNovoDistinctSequences, "sequences")!, false),
                        new Part(plot.Text("DeepNovo only"), plot.Number(row.DeepNovoExclusiveDistinctSequences, "sequences")!, false),
                    })).ToArray();
                plot.Views["fixed"] = ViewOf(rows, "Distinct normalized sequences, all runs pooled", "All runs pooled. Bars count distinct normalized de novo sequences, split into the two caller-only sets and their exact shared set. Database-search sequence counts are separate context. These are sequence counts, not signal shares. Changing Normalize to does not change them.", "Distinct de novo sequences", "", false);
            }
        }

        void MeanViews(Plot plot, IReadOnlyList<FamilyCompositionValue> nativeRows, string nativeBasis, bool diversity)
        {
            plot.Views["credited"] = MeanView("credited", nativeBasis);
            foreach (string key in new[] { "identifiedOnly", "identifiedAndDeNovo", "detectorEnvelopes", "tic" })
                plot.Views[key] = MeanView(key, BaseName(key));
            View MeanView(string key, string baseName)
            {
                var rows = nativeRows.Select(row =>
                {
                    double? mean = row.MeanPercent, sd = row.SdPercent;
                    if (key != "credited")
                    {
                        string sourceKey = row.Key == "others" ? "others:" + string.Join(",", basis.Grouping!.OtherFamilies.Select(f => f.FamilyUserId).Order())
                            : row.Key == "contaminants" ? "contaminants:" + string.Join(",", basis.Grouping!.Contaminants.Families.Select(f => f.FamilyUserId).Order()) : row.Key;
                        var source = new HtmlReportReference { Kind = "family-basis", Family = sourceKey,
                            Study = basis.UsesPanel && !basis.IsIdentity ? "effective" : "source", Property = SourceBasis(key) + "/mean" };
                        mean = HtmlReportPayloadBuilder.ReadFamilyShare(source, manifest) * 100;
                        sd = HtmlReportPayloadBuilder.ReadFamilyShare(source with { Property = SourceBasis(key) + "/sd" }, manifest) * 100;
                    }
                    int runCount = key == "credited" ? row.RunCount : basis.Studies.FamilyRunShares!.Runs.Count;
                    string runs = mean.HasValue ? "mean of " + runCount.ToString(CultureInfo.InvariantCulture) + (runCount == 1 ? " run" : " runs") : "Share unavailable";
                    return new Row(row.Key, plot.Text(row.Label), plot.Number(mean, "%"), diversity ? plot.Number(row.GreedyProteinCount, "proteins") : null,
                        !diversity && mean.HasValue && sd.HasValue ? plot.Number(mean.Value - sd.Value, "%") : null,
                        !diversity && mean.HasValue && sd.HasValue ? plot.Number(mean.Value + sd.Value, "%") : null, runs, Array.Empty<Part>());
                }).ToArray();
                string legend = diversity
                    ? "Horizontal position is the mean of each family's per-run shares of " + baseName + "; vertical position counts stored database proteins needed to explain its peptides, pooled across runs. Both axes are logarithmic, so equal distances mean equal fold changes; hollow diamonds mark zero values at the axis floor, with exact values on hover. These database-dependent greedy estimates are not proteoform counts; no whiskers are drawn."
                    : "Bars show the mean of per-run family shares of " + baseName + ". Each row states its run count. Whiskers extend one sample SD below and above the mean, including below zero when the spread reaches there; they describe run-to-run disagreement, not a confidence interval.";
                return ViewOf(rows, baseName, legend, "Mean family share (%)", diversity ? "Database proteins needed (greedy estimate)" : "", true, diversity, diversity);
            }
        }
    }

    private static string SourceBasis(string key) => key switch { "identifiedOnly" => "IdentifiedOnly", "identifiedAndDeNovo" => "IdentifiedAndDeNovo", "detectorEnvelopes" => "DetectorEnvelopes", _ => "Tic" };
    private static string BaseName(string key) => key switch { "identifiedOnly" => "PSM only", "identifiedAndDeNovo" => "PSM and de novo", "detectorEnvelopes" => "Isotope envelopes (measured)", _ => "TIC" };
    private static View ViewOf(IReadOnlyList<Row> rows, string basis, string legend, string xLabel, string yLabel, bool percent, bool scatter = false, bool logarithmic = false)
    {
        if (logarithmic)
        {
            (double Min, double Max, HtmlFamilyFigureTick[] Ticks) Axis(IEnumerable<double?> values, bool pct)
            {
                var positive = values.Where(value => value > 0).Select(value => value!.Value).ToArray();
                int lo = positive.Length == 0 ? 0 : (int)Math.Floor(Math.Log10(positive.Min()));
                int hi = positive.Length == 0 ? 1 : Math.Max(lo + 1, (int)Math.Ceiling(Math.Log10(positive.Max())));
                int step = Math.Max(1, (int)Math.Ceiling((hi - lo) / 5d));
                var ticks = Enumerable.Range(0, (hi - lo) / step + 1).Select(index => lo + index * step).Append(hi).Distinct()
                    .Select(power => Math.Pow(10, power)).Select(value => new HtmlFamilyFigureTick(value,
                        value.ToString("0." + new string('#', 324), CultureInfo.InvariantCulture) + (pct ? "%" : ""))).ToArray();
                return (Math.Pow(10, lo), Math.Pow(10, hi), ticks);
            }
            var x = Axis(rows.Select(row => row.X?.Value), true);
            var y = Axis(rows.Select(row => row.Y?.Value), false);
            return new(basis, legend, xLabel + " (log scale)", yLabel + " (log scale)", x.Min, x.Max, y.Max, x.Ticks, y.Ticks, rows)
                { MinimumY = y.Min };
        }
        double minimum = Math.Min(0, Math.Floor(rows.Select(row => row.Lower?.Value ?? 0).DefaultIfEmpty(0).Min() / 10) * 10);
        double maximum = rows.Select(row => Math.Max(row.X?.Value ?? 0, row.Upper?.Value ?? 0)).DefaultIfEmpty(0).Max();
        maximum = percent ? Math.Max(10, Math.Ceiling(maximum / 10) * 10) : NiceMaximum(maximum);
        double largestY = rows.Select(row => row.Y?.Value ?? 0).DefaultIfEmpty(0).Max();
        double ymax = scatter ? NiceMaximum(largestY) : 1;
        IReadOnlyList<HtmlFamilyFigureTick> Ticks(double lo, double hi, bool pct)
        {
            double step = NiceMaximum((hi - lo) / 4);
            var values = Enumerable.Range(0, (int)Math.Floor((hi - Math.Ceiling(lo / step) * step) / step) + 1)
                .Select(i => Math.Ceiling(lo / step) * step + i * step).Append(lo).Append(hi).Distinct().Order();
            return values.Select(value => new HtmlFamilyFigureTick(value,
                value.ToString(pct ? "G3" : "G5", CultureInfo.InvariantCulture) + (pct ? "%" : ""))).ToArray();
        }
        var yTicks = scatter ? Ticks(0, ymax, yLabel.Contains("(%)", StringComparison.Ordinal)) : Array.Empty<HtmlFamilyFigureTick>();
        return new(basis, legend, xLabel, yLabel, minimum, maximum, ymax, Ticks(minimum, maximum, percent), yTicks, rows);
    }
    private static double NiceMaximum(double value)
    {
        if (value <= 0) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value))), ratio = value / magnitude;
        return (ratio <= 1 ? 1 : ratio <= 2 ? 2 : ratio <= 5 ? 5 : 10) * magnitude;
    }
}
