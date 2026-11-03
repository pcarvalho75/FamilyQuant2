using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>Canonical positions and actual localized blocks. Gaps and uncertain feature boundaries receive no fill.</summary>
public sealed class ProteinCoverageMapChart : CompositionDrawingBase, IFigureDetails
{
    public ProteinCoverageMapData Data { get; }
    public IReadOnlyList<CoverageAnnotation> VisibleAnnotations { get; }
    public IReadOnlyList<CoveragePeptideLane> VisibleLanes { get; }
    public IReadOnlyList<CoveragePeptidePlacement> VisiblePeptides { get; }
    public string DetailNotice => VisibleAnnotations.Count < Data.Annotations.Count || VisibleLanes.Count < Data.Lanes.Count
        ? (VisibleAnnotations.Count < Data.Annotations.Count ? "Showing " + VisibleAnnotations.Count + " of " + Data.Annotations.Count + " annotation tracks. " : "")
            + (VisibleLanes.Count < Data.Lanes.Count ? "Showing " + VisibleLanes.Count + " of " + Data.Lanes.Count + " peptide lanes, first within each source. " : "")
            + FigureLayout.FullReference : "";
    private static double FullHeight(ProteinCoverageMapData data) => 284 + Math.Max(1, data.Annotations.Count) * 28
        + Math.Max(1, data.Lanes.Count) * 24 + (data.UnplacedPeptides.Count > 0 ? 20 : 0);
    public static double RecommendedHeight(ProteinCoverageMapData data) => Math.Max(540, FullHeight(data) <= FigureLayout.MaximumUncappedHeight ? FullHeight(data) :
        284 + Math.Max(1, Math.Min(6, data.Annotations.Count)) * 28 + Math.Max(1, Math.Min(8, data.Lanes.Count)) * 24
            + (data.UnplacedPeptides.Count > 0 ? 20 : 0) + (data.Annotations.Count > 6 || data.Lanes.Count > 8 ? 22 : 0));

    public ProteinCoverageMapChart(ProteinCoverageMapData data)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        bool limit = FullHeight(data) > FigureLayout.MaximumUncappedHeight;
        VisibleAnnotations = limit ? Array.AsReadOnly(data.Annotations.Take(6).ToArray()) : data.Annotations;
        var lanes = data.Lanes.GroupBy(l => l.Source).SelectMany(g => g.Take(4)).ToList();
        lanes.AddRange(data.Lanes.Where(l => !lanes.Contains(l)).Take(8 - lanes.Count));
        VisibleLanes = limit ? Array.AsReadOnly(lanes.OrderBy(l => l.Source).ToArray()) : data.Lanes;
        VisiblePeptides = Array.AsReadOnly(VisibleLanes.SelectMany(l => l.Peptides).ToArray());
        ToolTip = "Blue blocks are identified and green blocks are de novo; filled blocks are exact placements, outlined blocks are aligned, and gaps are not painted. " + data.PositionState;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Heading(dc, "Protein coverage: " + (Data.Label.Length > 0 ? Data.Label : Data.ProteinId),
            Data.ProteinId + " | Family " + Data.FamilyUserId + " | " + Data.SequenceLength + " residues | all runs pooled");
        Label(dc, Data.PositionState, 26, 82, ActualWidth - 52, 11, brush: palette.Muted);
        double left = Math.Min(260, ActualWidth * .29), right = ActualWidth - 36;
        if (right <= left) return;
        double X(double residueBoundary) => left + (right - left) * residueBoundary / Data.SequenceLength;
        Label(dc, "Protein", 26, 106, left - 42, 12, true);
        dc.DrawRoundedRectangle(palette.Grid, null, new Rect(left, 110, right - left, 12), 2, 2);
        foreach (int tick in new[] { 1, (int)Math.Ceiling(Data.SequenceLength / 4.0),
            (int)Math.Ceiling(Data.SequenceLength / 2.0), (int)Math.Ceiling(Data.SequenceLength * .75), Data.SequenceLength }.Distinct())
        {
            double x = X(tick - .5);
            dc.DrawLine(new Pen(palette.Axis, .7), new Point(x, 123), new Point(x, 127));
            Label(dc, tick.ToString(System.Globalization.CultureInfo.CurrentCulture), x - (tick == 1 ? 0 : 12), 131, 55, 10, brush: palette.Muted);
        }
        double y = 160;
        foreach (var annotation in VisibleAnnotations)
        {
            Label(dc, annotation.Label, 26, y - 2, left - 42, 11);
            Brush fill = annotation.Kind == CoverageAnnotationKind.Domain ? palette.NotQuantified : palette.Quantified;
            foreach (var segment in annotation.Segments)
                dc.DrawRectangle(fill, null, new Rect(X(segment.Start - 1), y, X(segment.End) - X(segment.Start - 1), 12));
            if (annotation.Segments.Count == 0) Label(dc, annotation.Note, left, y - 2, right - left, 11, brush: palette.Muted);
            y += 28;
        }
        if (VisibleAnnotations.Count == 0)
        {
            Label(dc, "Domain and feature tracks are unavailable or empty.", left, y - 2, right - left, 11, brush: palette.Muted);
            y += 28;
        }
        Label(dc, "Peptide placements", 26, y + 10, ActualWidth - 52, 13, true);
        y += 42;
        foreach (var lane in VisibleLanes)
        {
            Brush fill = lane.Source == CoveragePeptideSource.Identified ? palette.Identified : palette.DeNovo;
            Label(dc, (lane.Source == CoveragePeptideSource.Identified ? "Identified" : "De novo") + " (" + lane.Peptides.Count
                + (lane.Peptides.Count == 1 ? " placement)" : " placements)"),
                26, y - 2, left - 42, 11);
            foreach (var peptide in lane.Peptides)
                foreach (var block in peptide.Segments)
                    dc.DrawRectangle(peptide.Kind == CoveragePlacementKind.Exact ? fill : null,
                        peptide.Kind == CoveragePlacementKind.Aligned ? new Pen(fill, 1.7) : null,
                        new Rect(X(block.Start - 1), y, X(block.End) - X(block.Start - 1), 11));
            y += 24;
        }
        if (VisibleLanes.Count == 0) { Label(dc, Data.PeptideState, left, y, right - left, 11, brush: palette.Muted); y += 24; }
        if (Data.UnplacedPeptides.Count > 0)
            Label(dc, Data.UnplacedPeptides.Count + (Data.UnplacedPeptides.Count == 1 ? " peptide placement unavailable" : " peptide placements unavailable")
                + "; no coordinates guessed.", 26, y + 6, ActualWidth - 52, 11, brush: palette.Muted);
        if (DetailNotice.Length > 0) Label(dc, DetailNotice, 26, ActualHeight - 91, ActualWidth - 52, 11, brush: palette.Muted);
        Label(dc, "Display ranking signal: " + Number(Data.RankingSignal) + " area units, all runs pooled.",
            26, ActualHeight - 70, ActualWidth - 52, 11, brush: palette.Muted);
        Label(dc, "Positions are one-based and inclusive. Separate blocks leave annotation and alignment gaps unpainted.",
            26, ActualHeight - 49, ActualWidth - 52, 11, brush: palette.Muted);
        Label(dc, "Filled peptides: exact placements. Outlined peptides: aligned placements. " + Data.Provenance,
            26, ActualHeight - 28, ActualWidth - 52, 11, brush: palette.Muted);
    }
}
