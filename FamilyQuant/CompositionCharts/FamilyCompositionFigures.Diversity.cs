namespace FamilyQuant;

public static partial class FamilyCompositionFigures
{
    public static AnalyticsFigure BuildAbundanceDiversity(FamilyCompositionData data) =>
        new("abundance_diversity.png", new FamilyAbundanceDiversityChart(data), FamilyAbundanceDiversityChart.RecommendedHeight(data));
}
