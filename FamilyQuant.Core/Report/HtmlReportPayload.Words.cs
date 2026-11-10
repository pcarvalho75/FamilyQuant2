using System.Collections.Generic;

namespace FamilyQuant.Report;

/// <summary>Program-prepared axis labels, ticks and field IDs for a dot-plot normalization base.</summary>
public sealed record HtmlDotBasis(string Label, IReadOnlyList<string> Ticks, IReadOnlyDictionary<string, string> Values);
