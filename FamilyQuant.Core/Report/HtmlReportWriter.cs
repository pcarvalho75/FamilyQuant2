using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant.Report;

/// <summary>Fills the offline template with one safely encoded payload. No analysis or clock.
/// Field keys and object properties have fixed ordinal order; array order belongs to the producer.</summary>
public static class HtmlReportWriter
{
    private const string Marker = "<script id=\"fq-data\" type=\"application/json\"></script>";
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string PathForReport(string wordPath) => Path.ChangeExtension(wordPath, ".html");

    public static byte[] BuildBytes(HtmlReportPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var resource = typeof(HtmlReportWriter).Assembly.GetManifestResourceStream("FamilyQuant.Report.Html.Template")
            ?? throw new InvalidOperationException("The HTML report template is unavailable.");
        using var reader = new StreamReader(resource, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string template = reader.ReadToEnd().ReplaceLineEndings("\n");
        int position = template.IndexOf(Marker, StringComparison.Ordinal);
        if (position < 0 || template.IndexOf(Marker, position + Marker.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("The HTML report template must contain one data insertion point.");
        var ordered = payload with { Fields = new SortedDictionary<string, HtmlReportField>(
            payload.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal) };
        // Default encoding escapes HTML-sensitive characters in every string, including dictionary keys.
        // Serialize once; never interpolate data into executable script, markup or attributes.
        string json = JsonSerializer.Serialize(ordered, Options);
        int insertion = position + Marker.IndexOf('>') + 1;
        return new UTF8Encoding(false).GetBytes(template.Insert(insertion, json));
    }

    public static void Write(string htmlPath, HtmlReportPayload payload, OperationContext? operation = null)
    {
        operation?.ThrowIfCancellationRequested();
        byte[] bytes = BuildBytes(payload);
        operation?.ThrowIfCancellationRequested();
        File.WriteAllBytes(htmlPath, bytes);
        operation?.ThrowIfCancellationRequested();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            var properties = info.Properties.OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            info.Properties.Clear();
            foreach (var property in properties) info.Properties.Add(property);
        });
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            TypeInfoResolver = resolver,
            WriteIndented = false,
        };
    }
}
