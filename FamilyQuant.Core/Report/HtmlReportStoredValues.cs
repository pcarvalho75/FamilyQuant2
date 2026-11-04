using FamilyQuant.Evidence.Positions;
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace FamilyQuant.Report;

/// <summary>Display-only bindings to persisted, writable model properties. No searches,
/// derived getters, interval bounding/merging or missing values are evaluated.</summary>
public static class HtmlReportStoredValues
{
    public static string Display(HtmlReportReference source, FamilyClusterFile project, QuantManifest manifest)
    {
        if (source.Kind != "stored-value" || source.Property == null || !source.Property.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("A stored-value JSON pointer is required.");
        string[] path = source.Property.Split('/').Skip(1).Select(p => p.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        object? value = path.FirstOrDefault() switch { "project" => project, "manifest" => manifest, _ => throw new ArgumentException("Unknown stored root.") };
        foreach (string part in path.Skip(1))
        {
            if (value is IList list)
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= list.Count)
                    throw new ArgumentException("Stored source index is unavailable.");
                value = list[index];
            }
            else
            {
                var property = value?.GetType().GetProperty(part, BindingFlags.Instance | BindingFlags.Public);
                if (property == null || property.SetMethod == null || property.GetIndexParameters().Length != 0
                    || property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
                    throw new ArgumentException("Stored source property is unavailable or derived.");
                value = property.GetValue(value);
            }
            if (value == null) throw new ArgumentException("Stored source value is unavailable.");
        }
        return source.Format switch
        {
            "range" => Range(value!),
            "domain-label" when value is PfamDomainPosition p => (p.Name.Length > 0 ? p.Name + " " : "") + p.Accession,
            "feature-label" when value is ProteinFeaturePosition p => p.Type + (p.Description.Length > 0 ? ": " + p.Description : ""),
            _ when value is string text && source.Format == null && source.Scale == 1 => text,
            _ when value is IFormattable number && value is not Enum && value is not DateTime && value is not DateTimeOffset
                => source.Scale == 1 ? number.ToString(source.Format, CultureInfo.InvariantCulture)
                    : (Convert.ToDouble(value, CultureInfo.InvariantCulture) * source.Scale).ToString(source.Format ?? "G6", CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("Unsupported stored display conversion."),
        };
    }

    private static string Range(object value)
    {
        int start = value.GetType().GetProperty("Start")?.GetValue(value) is int a ? a : throw new ArgumentException("Stored start is unavailable.");
        int end = value.GetType().GetProperty("End")?.GetValue(value) is int b ? b : throw new ArgumentException("Stored end is unavailable.");
        return start.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture);
    }
}
