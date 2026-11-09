using FamilyQuant.Report.Captions;
using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

/// <summary>Caption bindings read the chart's own drawing snapshot. They never infer measurements from pixels.</summary>
public static class CompositionChartCaptions
{
    public static string Build(AnalyticsFigure figure, AnalyticsData? analytics = null) =>
        ReportCaptionBuilder.Figure(Describe(figure, analytics));

    public static FigureCaptionInput Describe(AnalyticsFigure figure, AnalyticsData? analytics = null)
    {
        var caption = figure.Chart switch
        {
        FamilyCompositionChart chart => Composition(new(chart.Rows, chart.Data.Basis, chart.Data.IsMeanOfRuns, chart.Data.UnavailableFamilies)),
        FamilySubclassChart chart => Subclasses(new(chart.Rows, chart.Data.Basis, false, chart.Data.UnavailableFamilies)),
        FamilyAbundanceDiversityChart chart => Diversity(chart),
        FamilyRingChart chart => Ring(chart),
        ProteinCoverageMapChart chart => Coverage(chart),
        FamilySignalOriginChart chart => SignalSources(chart),
        FamilyContributionChart chart => LegacyFamilies(chart),
        _ when analytics != null => Analytics(figure, analytics),
        _ => throw new NotSupportedException("Caption data is required for figure: " + figure.FileName)
        };
        return figure.Chart is IFigureDetails detail && detail.DetailNotice.Length > 0
            ? caption with { Notes = (caption.Notes ?? Array.Empty<string>()).Append(detail.DetailNotice).ToArray() } : caption;
    }

    private static FigureCaptionInput SignalSources(FamilySignalOriginChart chart) => new(
        "Family-specific envelope signal by sequence source, all runs pooled",
        "Each stacked row shows identified, de novo supported by the starting database, and family-world-only contributions. Shared and unexplained global signal are separate. Family-world-only signal is not added to starting database family composition",
        new CaptionNumber[] { new("Family rows shown", chart.Rows.Count, "families", "0"),
            new("Identified signal", chart.Rows.Sum(r => r.IdentifiedEnvelope), "area units", "G5"),
            new("Starting-database de novo signal", chart.Rows.Sum(r => r.DatabaseDeNovoEnvelope), "area units", "G5"),
            new("Family-world-only de novo signal", chart.Rows.Sum(r => r.WorldDeNovoEnvelope), "area units", "G5") });

    private static FigureCaptionInput Composition(FamilyCompositionData data)
    {
        var numbers = new List<CaptionNumber> { new("Families shown", data.Families.Count, "families", "0") };
        var top = data.Families.OrderByDescending(r => r.MeanPercent).ThenBy(r => r.Key, StringComparer.Ordinal).FirstOrDefault();
        if (top != null)
        {
            numbers.Add(new("Largest displayed " + (data.IsMeanOfRuns ? "mean " : "pooled ") + "share, " + top.Label, top.MeanPercent, "%"));
            if (top.SdPercent is double sd) numbers.Add(new(top.Label + " sample SD", sd, "percentage points"));
            if (data.IsMeanOfRuns) numbers.Add(new(top.Label + " available runs", top.RunCount, "runs", "0"));
        }
        var notes = new List<string> { "Basis: " + data.Basis };
        if (data.UnavailableFamilies.Count > 0) notes.Add("Family shares unavailable for: " + string.Join(", ", data.UnavailableFamilies));
        return new(data.IsMeanOfRuns ? "Mean family shares of credited signal" : "Pooled family shares of credited signal",
            data.IsMeanOfRuns ? "Bar length gives mean per-run share; whiskers show one sample SD across available runs, not a confidence interval. Missing SD is not zero"
                : "Bar length gives the share with all runs pooled; run-to-run uncertainty is not shown on this basis", numbers, notes);
    }

    private static FigureCaptionInput Subclasses(FamilyCompositionData data)
    {
        var numbers = new List<CaptionNumber> { new("Families shown", data.Families.Count, "families", "0") };
        double unresolved = data.Families.SelectMany(r => r.Subclasses).Where(p => p.Key.Length == 0).Sum(p => p.Percent);
        if (data.Families.Any(r => r.Subclasses.Count > 0))
            numbers.Add(new("Explicit unresolved share within available breakdowns", unresolved, "%"));
        var top = data.Families.OrderByDescending(r => r.MeanPercent).ThenBy(r => r.Key, StringComparer.Ordinal).FirstOrDefault();
        if (top != null) numbers.Add(new("Largest displayed pooled family share, " + top.Label, top.MeanPercent, "%"));
        var notes = new List<string> { "Basis: " + data.Basis };
        var missing = data.Families.Where(r => r.Subclasses.Count == 0).Select(r => r.Label).ToArray();
        if (missing.Length > 0) notes.Add("Subclass breakdown unavailable for: " + string.Join(", ", missing));
        return new("Subclass contributions within families, all runs pooled",
            "Each stacked bar uses the pooled credited-signal denominator; its parts add to the family. Grey denotes unresolved or unavailable parts as labelled", numbers, notes);
    }

    private static FigureCaptionInput Diversity(FamilyAbundanceDiversityChart chart)
    {
        var numbers = new List<CaptionNumber> { new("Families plotted", chart.Points.Count, "families", "0") };
        var top = chart.Points.OrderByDescending(p => p.SharePercent).ThenBy(p => p.Key, StringComparer.Ordinal).FirstOrDefault();
        if (top != null)
        {
            numbers.Add(new("Largest plotted mean share, " + top.Label, top.SharePercent, "%"));
            numbers.Add(new(top.Label + " proteins needed to explain the peptides (greedy estimate)", top.ProteinCount, "proteins", "0"));
        }
        return new("Family abundance against database-protein diversity",
            "Horizontal position gives mean per-run family share; vertical position gives proteins needed to explain the peptides (greedy estimate). Counts depend on the database and do not count genes or proteoforms",
            numbers, new[] { "Coincident points keep their coordinates; unavailable shares or protein counts are listed rather than plotted",
                "Basis: " + chart.Data.Basis });
    }

    private static FigureCaptionInput Ring(FamilyRingChart chart)
    {
        var numbers = new List<CaptionNumber>();
        if (chart.Families.Count > 0)
        {
            numbers.Add(new("Inner-ring total", chart.Families.Sum(f => f.Percent), "%"));
            numbers.Add(new("Outer-ring total", chart.Families.SelectMany(f => f.Parts).Sum(p => p.Percent), "%"));
            foreach (var category in chart.Families.Where(f => f.IsUnassigned))
                numbers.Add(new(category.Label + " pooled share", category.Percent, "%"));
            var others = chart.Families.SingleOrDefault(f => f.IsOthers);
            if (others != null)
            {
                numbers.Add(new("Others pooled share", others.Percent, "%"));
                numbers.Add(new("Families retained inside Others", others.Members.Count, "families", "0"));
            }
        }
        return new("Family shares, unassigned categories and subclass parts, all runs pooled",
            "Inner arcs show report groups and outer arcs split the same pooled signal by subclass. Others uses the report's mean-of-runs membership; the ring never re-decides membership from pooled shares",
            numbers, new[] { "Grey slices are conflicting families, not a panel family, or unresolved or unavailable parts, as labelled. The first two are outside families and Others. Original Others members and parts remain in the reference",
                "Basis: " + chart.Data.Basis });
    }

    private static FigureCaptionInput Coverage(ProteinCoverageMapChart chart)
    {
        var data = chart.Data;
        return new(
        "Domain, feature and peptide positions on " + data.Label,
        "Read residues from left to right. Blue peptide blocks are identified sequences and green blocks are de novo sequences. Filled peptide blocks are exact placements; outlined blocks are aligned placements. Gaps and uncertain feature intervals are not painted. A de novo alignment is not an exact sequence identification",
        new CaptionNumber[]
        {
            new("Protein length", data.SequenceLength, "residues", "0"),
            new("Allocated display signal", data.RankingSignal, "area units"),
            new("Identified placements drawn", chart.VisiblePeptides.Count(p => p.Source == CoveragePeptideSource.Identified), "placements", "0"),
            new("De novo placements drawn", chart.VisiblePeptides.Count(p => p.Source == CoveragePeptideSource.DeNovo), "placements", "0"),
            new("Domain hits drawn", chart.VisibleAnnotations.Count(a => a.Kind == CoverageAnnotationKind.Domain), "hits", "0"),
            new("Placements unavailable", data.UnplacedPeptides.Count, "placements", "0")
        }, new[] { data.PositionState, data.PeptideState, "Stored position sources: " + data.Provenance,
            "Protein ordering uses the supplied pooled display allocation of peptide signal. " + data.RankingMethod });
    }

    private static FigureCaptionInput LegacyFamilies(FamilyContributionChart chart)
    {
        var numbers = new List<CaptionNumber> { new("Rows shown", chart.Bars.Count, "rows", "0") };
        var top = chart.Bars.Where(b => b.Kind == FamilyContributionBarKind.Family).OrderByDescending(b => b.Total).FirstOrDefault();
        if (top != null)
            numbers.Add(chart.PercentApplies ? new("Largest displayed family share, " + top.Label,
                top.Total / chart.MeasuredEnvelope * 100, "%") : new("Largest displayed family signal, " + top.Label,
                    top.Total, "area units", "0.###E+0"));
        return new("Attributed peptide signal by family",
            "Each peptide counts once under the stated shared-peptide rule; other families, shared and unattributed signal remain explicit", numbers);
    }

    private static FigureCaptionInput Analytics(AnalyticsFigure figure, AnalyticsData data)
    {
        var eligible = data.Peptides.Where(p => p.Envelope > 0 && p.Mass > 0).ToArray();
        if (figure.Chart is XicMassChart)
            return new("Directly observed isotope-envelope signal against peptide mass",
                "Each point is a peptide representative. Mass is horizontal and observed envelope area is logarithmic vertically; colors distinguish identified and de novo sequences",
                new CaptionNumber[] { new("Identified points", eligible.Count(p => p.Side == NativeQuantFractions.SideIdentified), "points", "0"),
                    new("De novo points", eligible.Count(p => p.Side == NativeQuantFractions.SideDeNovo), "points", "0") });
        if (figure.Chart is IsotopeCountChart)
        {
            var peptides = data.Peptides.Where(p => p.IsotopesCredited >= 1).ToArray();
            var mode = peptides.GroupBy(p => p.IsotopesCredited).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
            var numbers = new List<CaptionNumber> { new("Peptide representatives counted", peptides.Length, "peptides", "0") };
            if (mode != null) { numbers.Add(new("A largest bin", mode.Key, "credited isotopes", "0")); numbers.Add(new("Peptides in that bin", mode.Count(), "peptides", "0")); }
            return new("Number of credited isotopes per peptide", "Bar height counts peptide representatives; stacked colors identify mass bands. An isotope is credited only where signal was found", numbers);
        }
        if (figure.Chart is PercentIonCurrentChart)
        {
            double total = eligible.Sum(p => p.Envelope);
            var numbers = new List<CaptionNumber> { new("Credited signal represented", total, "area units", "0.###E+0") };
            if (data.Grouped is { Tic: > 0 } grouped) numbers.Add(new("Represented share of recorded total ion current", total / grouped.Tic * 100, "%"));
            return new("Directly observed credited signal by peptide mass", "Filled bars give each mass bin's share of the displayed credited signal; outlined bars use recorded total ion current when available. This observed-envelope diagnostic is separate from the primary calibrated estimate", numbers);
        }
        if (figure.Chart is RecoveryChart)
        {
            var numbers = new List<CaptionNumber> { new("Acquisition runs", data.Fractions.Count, "runs", "0") };
            if (data.Grouped is { Tic: > 0 } grouped) numbers.Add(new("Pooled primary recovery estimate", grouped.RecoveredFractionReferenceCalibrated * 100, "%", "0.0"));
            var reference = data.Fractions.Concat(data.Grouped == null ? Array.Empty<NativeRunFraction>() : new[] { data.Grouped })
                .LastOrDefault(row => row.HasDetectedChargeSplit && row.Tic > 0);
            if (reference != null)
            {
                string scope = reference.IsGrouped ? "all runs pooled" : reference.Run;
                numbers.Add(new("Detector z >= 2 share of TIC, " + scope, reference.DetectedFractionMultiplyCharged * 100, "%"));
                numbers.Add(new("Detector z = 1 share of TIC, " + scope, reference.DetectedFractionSinglyCharged * 100, "%"));
            }
            return new("Recovery relative to each run's recorded total ion current", "The top compares M+0 sensitivity, the primary calibrated estimate and the observed-envelope diagnostic; these are different readings, not confidence limits. The lower levels are independent comparisons, not a biological partition", numbers,
                new[] { "Independent MS1 detector: z >= 2: " + Ms1EnvelopeDetector.StrongEvidenceLabel + "; z = 1: " + Ms1EnvelopeDetector.WeakEvidenceLabel,
                    Ms1EnvelopeDetector.SinglyChargedCaveat });
        }
        throw new NotSupportedException("Supply an explicit data-bound caption for figure: " + figure.FileName);
    }
}
