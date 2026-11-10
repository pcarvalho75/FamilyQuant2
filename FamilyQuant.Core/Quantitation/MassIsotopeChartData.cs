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
    public sealed record SignalBin(double LowerMass, double UpperMass, double Envelope, double Share,
        double? TicShare, string Label, string EnvelopeText, string ShareText, string TicShareText);
    public sealed record Signal(IReadOnlyList<SignalBin> Bins, double AxisMaximum, double Envelope, double BinWidth);

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

    public static Signal SignalByMass(IEnumerable<AnalyticsPeptide> source, double tic = 0, double binWidth = 500)
    {
        binWidth = binWidth > 0 ? binWidth : 500;
        var peptides = source.Where(p => p != null && p.Envelope > 0 && p.Mass > 0).ToList();
        int count = peptides.Count == 0 ? 1 : Math.Min(40, Math.Max(1, (int)Math.Ceiling(peptides.Max(p => p.Mass) / binWidth)));
        var found = new double[count];
        double total = 0;
        foreach (var peptide in peptides)
        {
            found[Math.Min(count - 1, (int)(peptide.Mass / binWidth))] += peptide.Envelope;
            total += peptide.Envelope;
        }
        double denominator = total > 0 ? total : 1;
        var bins = Enumerable.Range(0, count).Select(i => new SignalBin(i * binWidth, (i + 1) * binWidth,
            found[i], found[i] / denominator, tic > 0 ? found[i] / tic : null,
            (i * binWidth).ToString("N0", CultureInfo.InvariantCulture) + " to " + ((i + 1) * binWidth).ToString("N0", CultureInfo.InvariantCulture) + " Da",
            found[i].ToString("G17", CultureInfo.InvariantCulture) + " intensity x minutes",
            (found[i] / denominator).ToString("P4", CultureInfo.InvariantCulture) + " of displayed peptide signal",
            tic > 0 ? (found[i] / tic).ToString("P4", CultureInfo.InvariantCulture) + " of pooled TIC" : "TIC not recorded")).ToArray();
        return new(bins, Math.Max(.05, Math.Ceiling(bins.Max(b => b.Share) * 20) / 20), total, binWidth);
    }
}
