using System;
using System.Globalization;

namespace FamilyQuant.Report;

/// <summary>Presentation constants, not scientific observations. The producer and number trace
/// share this strict resolver; neither browser formatting nor pretend Word sources are needed.</summary>
public static class HtmlReportAxisTicks
{
    internal static bool Known(string id) => id is "mean" or "peptides" or "proteins" or "uniqueProteins"
        or "sd" or "cv" or "databaseEntries" or "databaseGap";

    internal static (string Label, string Unit, string Scale) Definition(string id) => id switch
    {
        "mean" => ("Mean share of credited family signal", "%", "log"),
        "peptides" => ("Distinct observed sequences", "", "log"),
        "proteins" => ("Protein estimate (greedy)", "", "log"),
        "uniqueProteins" => ("Proteins with own sequence", "", "log"),
        "sd" => ("Run SD of credited-family share (pp)", "pp", "linear"),
        "cv" => ("CV of credited-family mean share", "%", "linear"),
        "databaseEntries" => ("Database entries", "", "log"),
        "databaseGap" => ("Database gap fraction", "fraction", "log"),
        _ => throw new ArgumentException("Unknown dot measure.", nameof(id)),
    };

    public static bool TryResolve(HtmlReportReference source, out HtmlReportField field)
    {
        field = null!;
        if (source == null || source.Scale != 1 || source.Property == null
            || source.Table != null || source.Row != null || source.Column != null || source.Sheet != null
            || source.Figure != null || source.Family != null || source.Study != null || source.Run != null
            || source.ParagraphText != null || source.Occurrence != 0) return false;
        string[] path = source.Property.Split('/');
        if (!Known(path[0])) return false;
        var definition = Definition(path[0]);
        if (source.Kind == "basis-axis-label")
        {
            if (path.Length != 2 || path[0] is not ("mean" or "sd") || source.Format != null) return false;
            string? name = path[1] switch
            {
                "identifiedOnly" => "PSM only", "identifiedAndDeNovo" => "PSM and de novo",
                "detectorEnvelopes" => "detector envelopes", "tic" => "TIC", _ => null,
            };
            if (name == null) return false;
            field = new(path[0] == "mean" ? "Mean share of " + name + " (%)" : "Run SD of " + name + " (pp)", null, "", source);
            return true;
        }
        if (source.Kind == "axis-label")
        {
            if (path.Length != 1 || source.Format != null) return false;
            field = new(definition.Label, null, "", source);
            return true;
        }
        if (source.Kind != "axis-tick" || source.Format != "axis-plain" || path.Length is < 3 or > 4
            || !CanonicalInteger(path[2], out int exponent) || exponent is < -323 or > 308) return false;
        double value = Math.Pow(10, exponent);
        if (!(value > 0) || !double.IsFinite(value)) return false;
        int multiple = 1;
        if (definition.Scale == "log")
        {
            if (path.Length != 3 || path[1] != "log10") return false;
        }
        else
        {
            if (path.Length != 4 || path[1] != "linear" || !CanonicalInteger(path[3], out multiple)
                || multiple is < 0 or > 10) return false;
            value *= multiple;
            if (!double.IsFinite(value)) return false;
        }
        field = new(PlainDecimal(exponent, multiple), value, definition.Unit, source);
        return true;
    }

    private static string PlainDecimal(int exponent, int multiple)
    {
        if (multiple == 0) return "0";
        string digits = multiple.ToString(CultureInfo.InvariantCulture);
        if (exponent >= 0) return digits + new string('0', exponent);
        int point = digits.Length + exponent;
        // Render the exact decimal tick recipe, not a binary product's rounding noise.
        string text = point > 0 ? digits.Insert(point, ".") : "0." + new string('0', -point) + digits;
        return text.TrimEnd('0').TrimEnd('.');
    }

    private static bool CanonicalInteger(string text, out int value) => int.TryParse(text,
        NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
        && value.ToString(CultureInfo.InvariantCulture) == text;
}
