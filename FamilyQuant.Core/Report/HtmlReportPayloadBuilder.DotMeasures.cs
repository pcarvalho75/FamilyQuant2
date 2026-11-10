using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class HtmlReportPayloadBuilder
{
    private sealed partial class Builder
    {
        private IReadOnlyList<HtmlDotMeasure> DotMeasures(IReadOnlyList<HtmlReportFamily> families, bool databaseOnly = false)
        {
            var result = new List<HtmlDotMeasure>();
            foreach (var measure in new (string Id, Func<HtmlReportFamily, string?> Field)[]
            {
                ("mean", family => family.Mean), ("peptides", family => family.Peptides),
                ("proteins", family => family.Proteins), ("uniqueProteins", family => family.UniqueProteins),
                ("sd", family => family.Sd), ("cv", family => family.Cv),
                ("databaseEntries", family => family.DatabaseEntries),
            })
            {
                if ((measure.Id == "databaseEntries") != databaseOnly) continue;
                var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var family in families.Where(f => !f.IsOthers))
                {
                    string? id = measure.Field(family);
                    if (id != null && fields.TryGetValue(id, out var field) && field.Value is double value
                        && double.IsFinite(value) && value >= 0) values.Add(family.Id, id);
                }
                if (values.Count == 0) continue;
                var definition = HtmlReportAxisTicks.Definition(measure.Id);
                string label = AxisField(new() { Kind = "axis-label", Property = measure.Id });
                var ticks = DotTicks(measure.Id, values.Values.Select(id => fields[id].Value!.Value).ToArray());
                var bases = new Dictionary<string, HtmlDotBasis>(StringComparer.Ordinal);
                if (measure.Id is "mean" or "sd")
                    foreach (string basis in new[] { "identifiedOnly", "identifiedAndDeNovo", "detectorEnvelopes", "tic" })
                    {
                        var sourceValues = new SortedDictionary<string, string>(StringComparer.Ordinal);
                        foreach (var family in families.Where(family => !family.IsOthers))
                        {
                            var statistics = basis switch
                            {
                                "identifiedOnly" => family.IdentifiedOnly, "identifiedAndDeNovo" => family.IdentifiedAndDeNovo,
                                "detectorEnvelopes" => family.DetectorEnvelopes, _ => family.Tic,
                            };
                            string? value = measure.Id == "mean" ? statistics.Mean : statistics.Sd;
                            if (value != null && fields[value].Value is double number && double.IsFinite(number) && number >= 0)
                                sourceValues.Add(family.Id, value);
                        }
                        string baseLabel = AxisField(new() { Kind = "basis-axis-label", Property = measure.Id + "/" + basis });
                        bases.Add(basis, new(baseLabel, DotTicks(measure.Id, sourceValues.Values.Select(id => fields[id].Value!.Value).ToArray()), sourceValues));
                    }
                result.Add(new HtmlDotMeasure(measure.Id, label, definition.Unit, definition.Scale, ticks, values) { Bases = bases });
            }
            return result;
        }

        private IReadOnlyList<string> DotTicks(string id, double[] numbers)
        {
            var definition = HtmlReportAxisTicks.Definition(id);
            var ticks = new List<string>();
            void Tick(string recipe)
            {
                var source = new HtmlReportReference { Kind = "axis-tick", Property = id + "/" + recipe, Format = "axis-plain" };
                if (HtmlReportAxisTicks.TryResolve(source, out _)) ticks.Add(AxisField(source));
            }
            string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
            if (definition.Scale == "log")
            {
                var positive = numbers.Where(v => v > 0).ToArray();
                int lower = positive.Length == 0 ? 0 : Math.Clamp((int)Math.Floor(Math.Log10(positive.Min())), -323, 308);
                int upper = positive.Length == 0 ? 1 : Math.Clamp((int)Math.Ceiling(Math.Log10(positive.Max())), -323, 308);
                if (lower == upper) { if (upper < 308) upper++; else lower--; }
                for (int exponent = lower; exponent <= upper; exponent++) Tick("log10/" + Integer(exponent));
            }
            else
            {
                double maximum = numbers.Length == 0 ? 0 : numbers.Max();
                int exponent = maximum > 0 ? Math.Clamp((int)Math.Floor(Math.Log10(maximum)), -323, 308) : 0;
                int last = maximum > 0 ? Math.Clamp((int)Math.Ceiling(maximum / Math.Pow(10, exponent)), 1, 10) : 1;
                for (int multiple = 0; multiple <= last; multiple++) Tick("linear/" + Integer(exponent) + "/" + Integer(multiple));
            }
            return ticks;
        }

        private string AxisField(HtmlReportReference source)
        {
            if (!HtmlReportAxisTicks.TryResolve(source, out var field)) throw new ArgumentException("Invalid axis presentation source.", nameof(source));
            return Add(field.Text, source, field.Unit, field.Value);
        }
    }
}
