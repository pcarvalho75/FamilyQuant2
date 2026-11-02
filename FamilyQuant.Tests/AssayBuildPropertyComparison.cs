using System.Collections.Generic;
using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static class AssayBuildPropertyComparison
{
    internal static AssaySpectrumPropertyComparison.Result Compare(FeatureLedger.Observation row, string source,
        IReadOnlyDictionary<string, string> frozen, IReadOnlyDictionary<string, string> current)
    {
        AssaySpectrumPropertyComparison.Result No(string reason) => new(row.Id, "", "", "", "", false, reason);
        if (row.Source != "FamilyQuant/USearch.xaml" || row.Kind is not ("control" or "option/copy"))
            return No("Only named build controls and explicit scalar declarations are supported; no handler claim.");
        var xml = XDocument.Parse(source);
        string name, property, expected;
        if (row.Kind == "control")
        {
            int space = row.Detail.IndexOf(' '); if (space < 0) return No("No named control anchor.");
            expected = row.Detail[..space]; name = row.Detail[(space + 1)..]; property = "type";
        }
        else
        {
            int equals = row.Detail.IndexOf(" = ", StringComparison.Ordinal); if (equals < 0) return No("No property anchor.");
            string left = row.Detail[..equals]; int dot = left.LastIndexOf('.'); if (dot < 1) return No("No named property anchor.");
            name = left[..dot]; property = left[(dot + 1)..]; expected = row.Detail[(equals + 3)..];
            if (property is not ("Value" or "Minimum" or "Maximum" or "Increment" or "IsChecked" or "IsEnabled"))
                return No("Changed prose needs its own functional copy check.");
        }
        var controls = xml.Root!.DescendantsAndSelf().Where(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name)).ToArray();
        if (controls.Length != 1) return No("No unique named frozen control; no text fallback.");
        var control = controls[0];
        if (property == "type")
        {
            if (control.Name.LocalName != expected) return No("Frozen control type differs from its observation.");
        }
        else
        {
            if (control.Attribute(property)?.Value != expected || expected.StartsWith("{", StringComparison.Ordinal))
                return No("Frozen scalar property is missing, markup or differs.");
            if (bool.TryParse(expected, out bool boolean)) expected = boolean.ToString();
            else if (decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)) expected = number.ToString(CultureInfo.InvariantCulture);
            else return No("Unsupported scalar type.");
        }
        string key = "named/" + name + "/" + (property == "IsEnabled" ? "enabled" : property);
        bool hasBefore = frozen.TryGetValue(key, out string? before), hasAfter = current.TryGetValue(key, out string? after);
        return new(row.Id, key, expected, before ?? "<missing>", after ?? "<missing>",
            hasBefore && hasAfter && before == expected && after == before,
            property == "type" ? "Actual matching named control is constructed in both product views; no handler/navigation claim."
                : "Frozen explicit scalar equals both native bound control values.");
    }
}
