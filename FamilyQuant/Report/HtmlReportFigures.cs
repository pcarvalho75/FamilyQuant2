using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Copies selected report images and native signal snapshots without a second analysis.</summary>
public static class HtmlReportFigures
{
    public static HtmlReportPayload Build(HtmlReportPayload payload, ReportDocument document, ReportSelection? selection = null)
    {
        var excluded = new HashSet<string>(selection?.ExcludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var fields = new SortedDictionary<string, HtmlReportField>(payload.Fields.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal);
        var results = new List<HtmlReportFigure>();
        var quality = new List<HtmlReportFigure>();
        var sensitivity = new List<HtmlReportFigure>();
        var signalCharts = new List<HtmlSignalChart>(); // U3a: selected native signal/gap snapshots.
        var analyticsIds = AnalyticsFigures.FileNames.Select(name => "figure:" + name).ToHashSet(StringComparer.Ordinal);
        foreach (var section in document.Sections.Where(section => !excluded.Contains(section.Id)))
            foreach (var figure in section.Blocks.OfType<ReportFigure>().Where(figure => !excluded.Contains(figure.Id)))
            {
                if (HtmlSignalRecoveryData.Owns(figure.Id))
                {
                    signalCharts.Add(figure.SignalChart is { } chart ? chart with { Id = figure.Id, Title = figure.Title }
                        : new() { Id = figure.Id, Title = figure.Title, Kind = figure.Id.Contains("database_gap", StringComparison.Ordinal) ? "gap" : "unavailable",
                            Legend = "Interactive chart data are not included in this report. Export again with the current program; no picture or estimated value is substituted." });
                    continue;
                }
                // U3b: Word retains its pictures; these HTML tabs draw program-projected values.
                if (payload.MassQualityCharts != null && (figure.Id is "figure:xic_by_mass.png"
                    or "figure:isotopes_credited.png" or "figure:percent_ion_current_by_mass.png"
                    || section.Id == ReportSectionIds.Quality && !analyticsIds.Contains(figure.Id))) continue;
                if (figure.Id.StartsWith("figure:protein_coverage_", StringComparison.Ordinal)
                    || HtmlFamilyFigurePacket.Owns(figure.Id) // U3c Families are native page charts; Word retains these images.
                    || figure.Id.StartsWith("figure:family_contribution", StringComparison.Ordinal)) continue;
                List<HtmlReportFigure>? target = section.Id == ReportSectionIds.Sensitivity ? sensitivity
                    : analyticsIds.Contains(figure.Id) || section.Id is ReportSectionIds.Results or ReportSectionIds.DatabaseGap ? results
                    : section.Id == ReportSectionIds.Quality ? quality : null;
                if (target == null) continue;
                string title = "figure-title:" + figure.Id, caption = "figure-caption:" + figure.Id;
                fields.Add(title, new(figure.Title, null, "", new() { Kind = "figure-title", Figure = figure.Id }));
                fields.Add(caption, new(figure.Caption, null, "", new() { Kind = "figure-caption", Figure = figure.Id }));
                target.Add(new(title, caption, "data:image/png;base64," + Convert.ToBase64String(figure.PngBytes), figure.Id));
            }
        return payload with { Fields = fields, Results = results, Quality = quality, Sensitivity = sensitivity,
            SignalCharts = signalCharts };
    }
}
