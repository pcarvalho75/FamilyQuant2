namespace FamilyQuant;

public static partial class FamilyCompositionFigures
{
    public static AnalyticsFigure BuildRing(FamilyCompositionData pooled, FamilyRingGrouping grouping) =>
        new("family_subclass_ring.png", new FamilyRingChart(pooled, grouping), FamilyRingChart.RecommendedHeight(pooled, grouping));
}
