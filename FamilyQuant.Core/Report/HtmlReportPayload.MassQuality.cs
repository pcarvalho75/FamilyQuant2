using System;
using System.Collections.Generic;

namespace FamilyQuant.Report;

/// <summary>U3b program projections; the browser only positions and displays these values.</summary>
public sealed record HtmlMassQualityCharts
{
    public MassIsotopeChartData.Scatter? SignalByMass { get; init; }
    public MassIsotopeChartData.Isotopes? Isotopes { get; init; }
    public MassRangeRecoveryData? IonCurrentByMass { get; init; }
    public IReadOnlyList<QualityChartData.Plot> Quality { get; init; } = Array.Empty<QualityChartData.Plot>();
}
