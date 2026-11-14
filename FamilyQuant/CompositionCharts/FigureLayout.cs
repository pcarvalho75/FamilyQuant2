using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public interface IFigureDetails
{
    string DetailNotice { get; }
}

/// <summary>Readable native images have a fixed height budget. Full drawing inputs stay in the data supplement.</summary>
public static class FigureLayout
{
    public const double MaximumHeight = 710;
    public const double MaximumUncappedHeight = 1600;
    public const int SubclassReferences = 14;
    public const int DiversityReferences = 16;
    public const string DetailFileName = "figure_details.csv";
    public const string FullReference = "Full data: figure_details.csv / report supplement.";

    internal static IReadOnlyList<FamilyCompositionValue> Rows(FamilyCompositionData data,
        Func<FamilyCompositionValue, double> height, bool forceLimit = false)
    {
        if (!forceLimit && data.Families.Sum(height) <= MaximumUncappedHeight - 190) return data.Families;
        var selected = new List<FamilyCompositionValue>(); double used = 190;
        foreach (var row in data.Families.OrderBy(r => r.Key == "contaminants").ThenByDescending(r => r.MeanPercent).ThenBy(r => r.Key, StringComparer.Ordinal))
        {
            double next = height(row);
            if (selected.Count > 0 && used + next > MaximumHeight) break;
            selected.Add(row); used += next;
        }
        return selected.AsReadOnly();
    }

    internal static string FamilyNotice(int shown, int total) =>
        "First " + shown + " of " + total + " families by share. " + FullReference;
}
