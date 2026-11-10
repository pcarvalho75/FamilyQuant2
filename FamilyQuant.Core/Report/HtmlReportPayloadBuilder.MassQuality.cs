using System;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class HtmlReportPayloadBuilder
{
    private sealed partial class Builder
    {
        private HtmlMassQualityCharts? MassQualityCharts()
        {
            if (context.ChartAnalytics == null || !sections.ContainsKey(ReportSectionIds.Quality)) return null;
            var data = context.ChartAnalytics;
            bool Include(string file) => !excluded.Contains("figure:" + file);
            var manifest = context.StoredStudies ?? new QuantManifest();
            return new()
            {
                SignalByMass = Include("xic_by_mass.png") ? MassIsotopeChartData.ScatterPoints(data.Peptides) : null,
                Isotopes = Include("isotopes_credited.png") ? MassIsotopeChartData.IsotopeCounts(data.Peptides) : null,
                IonCurrentByMass = Include("percent_ion_current_by_mass.png")
                    ? MassRangeRecoveryData.Build(data) : null,
                Quality = QualityChartData.Build(data, manifest, context.SourceGrouping).Where(plot => Include(plot.Id)).ToArray(),
            };
        }
    }
}
