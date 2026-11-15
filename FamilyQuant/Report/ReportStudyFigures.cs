using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Report.Captions;
using System;
using FamilyQuant.Operations;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace FamilyQuant.Report;

public sealed record ReportStudyFigure(string SectionId, string Title, AnalyticsFigure Plot, FigureCaptionInput Caption);
public sealed record ReportStudyFigurePlan(IReadOnlyList<ReportStudyFigure> Figures,
    IReadOnlyList<ReportFigureAvailability> Unavailable);
public sealed record ReportStudyFigurePacket(IReadOnlyList<ReportPlacedFigure> Figures,
    IReadOnlyList<ReportFigureAvailability> Unavailable, IReadOnlyDictionary<string, FigureCaptionInput> CaptionBindings)
{
    public string FigureDetailsCsv { get; init; } = "";
    public IReadOnlyList<ReportSupplementSheet> FigureDetailSheets { get; init; } = Array.Empty<ReportSupplementSheet>();
    public IReadOnlyList<ReportStudyFigure> NativeFigures { get; init; } = Array.Empty<ReportStudyFigure>(); // U3c retains the exact drawing inputs.
}

/// <summary>Stored-study report figures. No extraction, sequence search, evidence refresh or quantitation is run.</summary>
public static partial class ReportStudyFigures
{
    private const double ReportPlotHeight = 710;

    /// <summary>The optional map is the authoritative current map supplied by the assignment producer.
    /// With no supplied map, the saved evidence/currentness accessor is always used for a panel project.</summary>
    public static ReportStudyFigurePlan Plan(QuantManifest manifest, FamilyClusterFile project,
        int topProteinsPerPanelFamily = 1, PanelFamilyMap? panelMap = null, FamilyReportThresholdResult? reportGrouping = null)
        => PlanCore(manifest, project, topProteinsPerPanelFamily, panelMap, reportGrouping, null);

    public static ReportStudyFigurePlan PlanForBasis(QuantManifest manifest, FamilyClusterFile project, ReportCompositionBasis basis,
        int topProteinsPerPanelFamily = 1, PanelFamilyMap? panelMap = null, FamilyReportThresholdResult? reportGrouping = null)
        => PlanCore(manifest, project, topProteinsPerPanelFamily, panelMap, reportGrouping, basis);

    private static ReportStudyFigurePlan PlanCore(QuantManifest manifest, FamilyClusterFile project,
        int topProteinsPerPanelFamily, PanelFamilyMap? panelMap, FamilyReportThresholdResult? reportGrouping, ReportCompositionBasis? basis)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(project);
        if (topProteinsPerPanelFamily < 1) throw new ArgumentOutOfRangeException(nameof(topProteinsPerPanelFamily));
        if (project.FamilyPanel != null)
        {
            panelMap ??= PanelAssignmentEvidenceStore.GetCurrentFamilyMap(project);
            if (!panelMap.IsCurrent) throw new InvalidOperationException(panelMap.Message);
        }
        var figures = new List<ReportStudyFigure>();
        var unavailable = new List<ReportFigureAvailability>();
        void Missing(string title, string reason, string section = ReportSectionIds.Results) => unavailable.Add(new(section, title, reason));
        var composition = basis?.Studies ?? manifest;
        var labels = basis?.Labels ?? FamilyLabels(project, panelMap);
        FamilyReportThresholdResult? sourceThreshold = reportGrouping ?? (manifest.FamilyRunShares == null ? null : panelMap == null
            ? FamilyReportThreshold.Build(manifest.FamilyRunShares, project)
            : FamilyReportThreshold.Build(manifest.FamilyRunShares, project, panelMap));
        FamilyReportThresholdResult? threshold = basis?.Grouping ?? sourceThreshold;
        void Add(AnalyticsFigure plot, string title, string section = ReportSectionIds.Results)
        {
            var caption = CompositionChartCaptions.Describe(plot);
            if (plot.Chart is FamilySubclassChart or FamilyRingChart && threshold?.OtherFamilies.Count > 0)
                caption = caption with { Notes = (caption.Notes ?? Array.Empty<string>()).Append(
                    "Others member and part names are retained in the companion workbook. Generic member and part labels preserve the stored pooled amounts in this figure").ToArray() };
            if (plot.Chart is FamilyCompositionChart or FamilySubclassChart or FamilyAbundanceDiversityChart)
            {
                var notes = (caption.Notes ?? Array.Empty<string>()).ToList();
                notes.Add("Conflicting groups and groups that are not a panel family are outside family rows and Others; their signal remains in the original denominator");
                if (plot.Chart is FamilyAbundanceDiversityChart && threshold?.OtherFamilies.Count > 0)
                    notes.Add("Others diversity is the union of stored member-family greedy protein selections, not a new pooled fit; missing selected identifiers make that count unavailable");
                caption = caption with { Notes = notes };
            }
            figures.Add(new(section, title, plot, caption));
        }

        FamilyCompositionData? means = null;
        if (composition.FamilyRunShares != null)
        {
            var stored = StoredCompositionChartData.FromRunShares(composition.FamilyRunShares, composition.FamilyProteinCounts, labels);
            stored = StoredCompositionChartData.WithContaminants(stored, threshold!.Contaminants);
            means = CanonicalRows(stored, threshold!, composition.FamilyProteinCounts);
            if (means.Families.Count > 0)
            {
                var pages = Pages(means.Families, _ => 52);
                for (int i = 0; i < pages.Count; i++)
                {
                    var page = Copy(means, pages[i]);
                    Add(new(PageFile("family_composition", i, pages.Count), new FamilyCompositionChart(page), Math.Max(540, 190 + page.Families.Count * 52)),
                        PageTitle("Family composition with run error bars", i, pages.Count));
                }
            }
            else Missing("Family composition with run error bars", "No stored mean family shares are available.");
        }
        else Missing("Family composition with run error bars", "Per-run family shares have not been computed.");

        FamilyCompositionData? pooled = null;
        FamilyCompositionData? ringData = null;
        if (composition.FamilySubclassParts != null && threshold != null)
        {
            if (composition.FamilyRunShares != null && composition.FamilyRunShares.Policy != composition.FamilySubclassParts.Policy)
                throw new InvalidOperationException("Run shares and subclass parts use different shared-peptide policies.");
            if (!composition.FamilySubclassParts.Families.Select(f => f.FamilyUserId).ToHashSet()
                .SetEquals(composition.FamilyRunShares!.Families.Select(f => f.FamilyUserId)))
                throw new InvalidOperationException("Pooled subclass parts and the canonical report partition have different family memberships.");
            // Adapt the complete stored partition first: filtering must never change the pooled denominator.
            ringData = WordOthersNames(StoredCompositionChartData.FromSubclassParts(composition.FamilySubclassParts, labels), threshold);
            pooled = CanonicalRows(ringData, threshold, null);
            if (pooled.Families.Count > 0)
            {
                var pages = Pages(pooled.Families, FamilySubclassChart.RowHeight);
                for (int i = 0; i < pages.Count; i++)
                {
                    var page = Copy(pooled, pages[i]);
                    Add(new(PageFile("family_subclasses", i, pages.Count), new FamilySubclassChart(page),
                        FamilySubclassChart.RecommendedHeight(page)),
                        PageTitle("Family subclass contributions, all runs pooled", i, pages.Count));
                }
            }
            else Missing("Subclass contributions", "No pooled credited family signal is stored.");
        }
        else Missing("Subclass contributions", "Stored subclass parts and the canonical mean-share report partition are required.");
        if (ringData is { Families.Count: > 0 } && threshold != null)
            Add(FamilyCompositionFigures.BuildRing(ringData, FamilyRingGrouping.FromReportThreshold(threshold)), "Family and subclass ring, all runs pooled");
        else Missing("Family and subclass ring", "Stored pooled subclass parts and mean per-run shares are required for the ring and Others membership.");
        if (means is { Families.Count: > 0 } && composition.FamilyProteinCounts != null)
            Add(FamilyCompositionFigures.BuildAbundanceDiversity(Copy(means, means.Families.Where(row => row.Key != "contaminants"))),
                "Abundance against database-protein diversity");
        else Missing("Abundance against diversity", "Stored per-run family shares and database-protein counts are required.");

        var world = manifest.FamilyWorldResult?.ForLength(manifest.DatabaseGapMinimumTagLength ?? FamilyWorldOptions.DefaultMinimumTagLength);
        if (world is { CompositionRules.Length: > 0 })
        {
            var rows = CanonicalSources(world.Composition, sourceThreshold, unavailable).OrderByDescending(r => r.WorldDeNovoEnvelope).ThenBy(r => r.Kind, StringComparer.Ordinal)
                .ThenBy(r => r.Family, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Family, StringComparer.Ordinal)
                .ThenBy(r => r.CuratedUserIds.FirstOrDefault()).ToArray();
            var pages = rows.Chunk(12).ToArray();
            if (pages.Length == 0) Missing("Family signal sources", "No family-specific source rows are stored.");
            for (int i = 0; i < pages.Length; i++)
            {
                var chart = new FamilySignalOriginChart(pages[i]);
                Add(new(PageFile("family_signal_sources", i, pages.Length), chart, chart.Height), PageTitle("Family signal by source, all runs pooled", i, pages.Length),
                    basis is { UsesPanel: true, IsIdentity: false } ? ReportSectionIds.Supplements : ReportSectionIds.Results);
            }
        }
        else Missing("Family signal sources", "The selected UniProt families source composition has not been stored; its missing parts are not zero.");
        int coverageStart = figures.Count;
        AddCoverage(manifest, project, panelMap, sourceThreshold, topProteinsPerPanelFamily, figures, unavailable);
        if (basis is { UsesPanel: true, IsIdentity: false })
            for (int i = coverageStart; i < figures.Count; i++) figures[i] = figures[i] with { SectionId = ReportSectionIds.Supplements };
        if (figures.Select(f => f.Plot.FileName).Distinct(StringComparer.Ordinal).Count() != figures.Count)
            throw new InvalidOperationException("Report study figure filenames collide.");
        return new(figures.AsReadOnly(), unavailable.AsReadOnly());
    }

    public static ReportStudyFigurePacket Build(QuantManifest manifest, FamilyClusterFile project, string pngFolder,
        int topProteinsPerPanelFamily = 1, PanelFamilyMap? panelMap = null, FamilyReportThresholdResult? reportGrouping = null,
        OperationContext? operation = null,
        int otherFigureCount = 0)
        => BuildCore(manifest, project, pngFolder, topProteinsPerPanelFamily, panelMap, reportGrouping, operation, otherFigureCount, null);

    public static ReportStudyFigurePacket BuildForBasis(QuantManifest manifest, FamilyClusterFile project, string pngFolder, ReportCompositionBasis basis,
        int topProteinsPerPanelFamily = 1, PanelFamilyMap? panelMap = null, FamilyReportThresholdResult? reportGrouping = null,
        OperationContext? operation = null, int otherFigureCount = 0)
        => BuildCore(manifest, project, pngFolder, topProteinsPerPanelFamily, panelMap, reportGrouping, operation, otherFigureCount, basis);

    private static ReportStudyFigurePacket BuildCore(QuantManifest manifest, FamilyClusterFile project, string pngFolder,
        int topProteinsPerPanelFamily, PanelFamilyMap? panelMap, FamilyReportThresholdResult? reportGrouping,
        OperationContext? operation, int otherFigureCount, ReportCompositionBasis? basis)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var plan = PlanCore(manifest, project, topProteinsPerPanelFamily, panelMap, reportGrouping, basis);
            int total = plan.Figures.Count + otherFigureCount;
            if (plan.Figures.Count > 0 || total == 0) operation?.Report("report-prepare-figure", "Preparing report figures", 0, total);
            var placed = plan.Figures.Select((item, index) =>
            {
                operation?.Report("report-prepare-figure", "Figure " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of "
                    + total.ToString(CultureInfo.InvariantCulture) + ": " + item.Title, index + 1, total);
                return new ReportPlacedFigure(item.SectionId,
                    new("figure:" + item.Plot.FileName, item.Title, ExportPng(item), ReportCaptionBuilder.Figure(item.Caption),
                        (int)(960 * ChartImageExporter.DefaultScale), (int)Math.Round(item.Plot.ScreenHeight * ChartImageExporter.DefaultScale)));
            }).ToArray();
            Directory.CreateDirectory(pngFolder);
            for (int i = 0; i < placed.Length; i++) File.WriteAllBytes(Path.Combine(pngFolder, plan.Figures[i].Plot.FileName), placed[i].Figure.PngBytes);
            string details = global::FamilyQuant.FigureDetailsCsv.Build(plan.Figures.Select(f => f.Plot));
            File.WriteAllText(Path.Combine(pngFolder, FigureLayout.DetailFileName), details, new System.Text.UTF8Encoding(false));
            var sheets = plan.Figures.Where(f => f.Plot.Chart is IFigureDetails d && d.DetailNotice.Length > 0).Select((f, i) =>
            {
                string id = "figure:" + f.Plot.FileName;
                var table = ReportCsvTable.Build(id, "Complete figure data: " + f.Title, global::FamilyQuant.FigureDetailsCsv.Build(new[] { f.Plot }),
                    "Complete native drawing input; the overview's reference/track limit does not change these values or their denominator.");
                return new ReportSupplementSheet(id, f.SectionId, "Figure data " + (i + 1).ToString(CultureInfo.InvariantCulture), table);
            }).ToArray();
            return new(placed, plan.Unavailable, plan.Figures.ToDictionary(f => "figure:" + f.Plot.FileName, f => f.Caption, StringComparer.Ordinal))
                { FigureDetailsCsv = details, FigureDetailSheets = sheets, NativeFigures = plan.Figures };
        }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }

    /// <summary>The original native program export, also used as the exact bytes embedded in Word.</summary>
    public static byte[] ExportPng(ReportStudyFigure figure)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("Report study figures require an STA thread.");
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            return figure.Plot.Chart is FamilySignalOriginChart source ? FamilySignalOriginChart.RenderReportPng(source.Rows)
                : FamilyCompositionFigures.RenderPng(figure.Plot);
        }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }

    private static FamilyCompositionData Copy(FamilyCompositionData source, IEnumerable<FamilyCompositionValue> rows) =>
        new(rows, source.Basis, source.IsMeanOfRuns, source.UnavailableFamilies);
    private static List<FamilyCompositionValue[]> Pages(IReadOnlyList<FamilyCompositionValue> rows, Func<FamilyCompositionValue, double> rowHeight)
    {
        var pages = new List<FamilyCompositionValue[]>(); var current = new List<FamilyCompositionValue>(); double height = 190;
        foreach (var row in rows)
        {
            double next = rowHeight(row);
            if (current.Count > 0 && height + next > ReportPlotHeight) { pages.Add(current.ToArray()); current.Clear(); height = 190; }
            current.Add(row); height += next;
        }
        if (current.Count > 0) pages.Add(current.ToArray()); return pages;
    }
    private static string PageFile(string stem, int page, int count) => stem + (count > 1 ? "_" + (page + 1).ToString("D2", CultureInfo.InvariantCulture) : "") + ".png";
    private static string PageTitle(string title, int page, int count) => title + (count > 1 ? " (" + (page + 1).ToString(CultureInfo.InvariantCulture) + "/" + count.ToString(CultureInfo.InvariantCulture) + ")" : "");
    private static Dictionary<int, string> FamilyLabels(FamilyClusterFile project, PanelFamilyMap? map)
    {
        var labels = new Dictionary<int, string>();
        if (project.FamilyPanel == null || map == null) return labels;
        var panel = project.FamilyPanel.Families.ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (var item in map.UserFamilies.Values)
        {
            if (item.PanelFamilyKey != null && panel.TryGetValue(item.PanelFamilyKey, out var family))
                labels[item.FamilyUserId] = ReportFamilyNames.DisplayName(project.FamilyPanel, family, project.ReportFamilyNames) + (map.UserFamilies.Values.Count(m => m.PanelFamilyKey == item.PanelFamilyKey) > 1
                    ? " (group " + item.FamilyUserId.ToString(CultureInfo.InvariantCulture) + ")" : "");
            else if (item.HasConflict) labels[item.FamilyUserId] = "Family " + item.FamilyUserId.ToString(CultureInfo.InvariantCulture) + " (conflicting panel membership)";
        }
        return labels;
    }
    private static FamilyWorldCompositionRow Snapshot(FamilyWorldCompositionRow row)
    {
        if (new[] { row.IdentifiedEnvelope, row.DatabaseDeNovoEnvelope, row.WorldDeNovoEnvelope }.Any(v => !double.IsFinite(v) || v < 0))
            throw new InvalidOperationException("Stored source contributions must be finite and nonnegative.");
        return new FamilyWorldCompositionRow { Family = row.Family, Kind = row.Kind, CuratedUserIds = row.CuratedUserIds.ToList(),
            IdentifiedEnvelope = row.IdentifiedEnvelope, IdentifiedPeptides = row.IdentifiedPeptides,
            DatabaseDeNovoEnvelope = row.DatabaseDeNovoEnvelope, DatabaseDeNovoPeptides = row.DatabaseDeNovoPeptides,
            WorldDeNovoEnvelope = row.WorldDeNovoEnvelope, WorldDeNovoPeptides = row.WorldDeNovoPeptides,
            WorldDecoyRate = row.WorldDecoyRate, WorldKnownCredited = row.WorldKnownCredited,
            WorldKnownCreditedTrue = row.WorldKnownCreditedTrue, WorldRightRate = row.WorldRightRate, WorldEvidence = row.WorldEvidence };
    }
}
