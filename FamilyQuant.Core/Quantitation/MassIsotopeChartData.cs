using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>Program-owned projections shared by native and HTML mass/isotope charts.</summary>
public static class MassIsotopeChartData
{
    public sealed record Point(double Mass, double Envelope, double LogEnvelope, string Side,
        string MassText, string EnvelopeText);
    public sealed record Scatter(IReadOnlyList<Point> Points, double MassMaximum, double LogMinimum, double LogMaximum);
    public sealed record IsotopeBin(int Isotopes, int[] MassBandCounts, int Total, string Label, string ShareText)
    {
        public IReadOnlyList<string> MassBandShares { get; init; } = Array.Empty<string>();
    }
    public sealed record Isotopes(IReadOnlyList<IsotopeBin> Bins, int FeatureCount, double AxisMaximum,
        IReadOnlyList<string> MassBands);

    public static Scatter ScatterPoints(IEnumerable<AnalyticsPeptide> source)
    {
        var points = source.Where(p => p != null && p.Envelope > 0 && p.Mass > 0).Select(p => new Point(
            p.Mass, p.Envelope, Math.Log10(p.Envelope), p.Side, p.Mass.ToString("G17", CultureInfo.InvariantCulture) + " Da",
            p.Envelope.ToString("G17", CultureInfo.InvariantCulture) + " intensity x minutes")).ToArray();
        double mass = points.Length == 0 ? 500 : Math.Ceiling(points.Max(p => p.Mass) / 500) * 500;
        double low = points.Length == 0 ? 0 : Math.Floor(points.Min(p => p.LogEnvelope));
        double high = points.Length == 0 ? 1 : Math.Ceiling(points.Max(p => p.LogEnvelope));
        return new(points, Math.Max(500, mass), low, high > low ? high : low + 1);
    }

    public static Isotopes IsotopeCounts(IEnumerable<AnalyticsPeptide> source)
    {
        var counts = new int[AnalyticsBuilder.IsotopeBasis + 1, 3];
        int features = 0;
        foreach (var peptide in source.Where(p => p != null && p.IsotopesCredited >= 1))
        {
            int count = Math.Min(AnalyticsBuilder.IsotopeBasis, peptide.IsotopesCredited);
            int band = peptide.Mass < 1500 ? 0 : peptide.Mass < 3000 ? 1 : 2;
            counts[count, band]++; features++;
        }
        var bins = Enumerable.Range(1, AnalyticsBuilder.IsotopeBasis).Select(i =>
        {
            int[] bands = { counts[i, 0], counts[i, 1], counts[i, 2] };
            int total = bands.Sum();
            return new IsotopeBin(i, bands, total, i.ToString(CultureInfo.InvariantCulture),
                features > 0 ? ((double)total / features).ToString("P4", CultureInfo.InvariantCulture) + " of displayed peptide features" : "No peptide features")
            { MassBandShares = bands.Select(n => features > 0 ? ((double)n / features).ToString("P4", CultureInfo.InvariantCulture)
                + " of displayed peptide features" : "No peptide features").ToArray() };
        }).ToArray();
        double top = Math.Max(4, Math.Ceiling(bins.Max(b => Math.Max(1, b.Total)) / 4.0) * 4);
        return new(bins, features, top, new[] { "Under 1500 Da", "1500 to under 3000 Da", "3000 Da and above" });
    }

}
