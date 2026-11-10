using System;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>The fixed neutral-mass ranges shared by detector storage and recovery views.</summary>
public static class NeutralMassRanges
{
    public const double Width = 500;
    public const int Count = 40;

    public static int Index(double mass) => !double.IsFinite(mass) || mass < 0 ? -1
        : (int)Math.Min(Count - 1, Math.Floor(mass / Width));

    public static bool IsRecorded(double[]? areas, double? width) => width == Width
        && areas is { Length: Count } && areas.All(area => double.IsFinite(area) && area >= 0);

    public static string Label(int index) => (index * Width).ToString("N0", CultureInfo.InvariantCulture)
        + (index == Count - 1 ? " Da and above" : " to under "
            + ((index + 1) * Width).ToString("N0", CultureInfo.InvariantCulture) + " Da");
}
