using FamilyQuant.DeNovo;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

// The same source is linked into the frozen host. Only product serializers create cache bytes.
internal static class AssayCacheSnapshot
{
    internal const string Argument = "--assay-cache-snapshot";
    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply a fresh output folder.");
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string xic = Path.Combine(output, "xic.json");
        var cache = DeNovoXicCache.Load(xic, "synthetic-cache", 10.25, 2, 3, 3, "synthetic-content-sha");
        cache.Put(new DeNovoXicResult { RunName = "synthetic-cache", ScanNoMS2 = 12, PrecursorMz = 500.12345,
            Charge = 2, Tag = "quoted \"tag\", with <markup>", IsotopeAreas = new[] { 1.2345678901234567, double.Epsilon, -1d },
            IsotopePointCounts = new[] { 4, 3, 0 }, UnderSampledArea = 0.125, UnderSampledIsEstimate = true,
            UnderSampledIsotopes = 1, AreaMMinus1 = -1 });
        cache.Save();
        var again = DeNovoXicCache.Load(xic, "synthetic-cache", 10.25, 2, 3, 3, "synthetic-content-sha");
        if (!again.TryGet(12, 500.12345, 2, out var recovered) || recovered == null
            || BitConverter.DoubleToInt64Bits(recovered.IsotopeAreas[0]) != BitConverter.DoubleToInt64Bits(1.2345678901234567))
            throw new InvalidDataException("Product cache did not round-trip the fixture.");
        var detection = new Ms1EnvelopeDetectionResult { RunName = "synthetic-cache", DetectedEnvelopeCount = 3,
            DetectedPeptideLikeArea = 0.375, Ms1ScanCount = 19, DetectedAreaMultiplyCharged = 0.25,
            DetectedAreaSinglyCharged = 0.125, DetectedCountMultiplyCharged = 2, DetectedCountSinglyCharged = 1 };
        detection.DetectedAreaByCharge[1] = 0.125; detection.DetectedAreaByCharge[2] = 0.25;
        detection.DetectedCountByCharge[1] = 1; detection.DetectedCountByCharge[2] = 2;
        string ms1 = Path.Combine(output, "ms1.json");
        typeof(Ms1EnvelopeDetector).GetMethod("SaveCache", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { ms1, detection, "synthetic-content-sha" });
        object?[] readArguments = { ms1, "synthetic-cache", "synthetic-content-sha", null };
        if (!(bool)typeof(Ms1EnvelopeDetector).GetMethod("TryLoadCache", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, readArguments)!)
            throw new InvalidDataException("Product MS1 cache did not round-trip the fixture.");
        string blockedXic = Path.Combine(output, "blocked-xic.json");
        Directory.CreateDirectory(blockedXic);
        var blocked = DeNovoXicCache.Load(blockedXic, "synthetic-cache", 10.25, 2, 3, 3, "synthetic-content-sha");
        blocked.Put(recovered);
        bool refused = false;
        try { blocked.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { refused = true; }
        string blockedMs1 = Path.Combine(output, "blocked-ms1.json");
        Directory.CreateDirectory(blockedMs1);
        typeof(Ms1EnvelopeDetector).GetMethod("SaveCache", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { blockedMs1, detection, "synthetic-content-sha" });
        if (!refused || !File.Exists(blockedXic + ".tmp") || !File.Exists(blockedMs1 + ".tmp")
            || !File.ReadAllBytes(blockedXic + ".tmp").SequenceEqual(File.ReadAllBytes(xic))
            || !File.ReadAllBytes(blockedMs1 + ".tmp").SequenceEqual(File.ReadAllBytes(ms1)))
            throw new InvalidDataException("Product cache staging did not retain the full payload at the temporary path.");
        File.WriteAllText(Path.Combine(output, "staging.json"), JsonSerializer.Serialize(new
        { xicSuffix = Path.GetExtension(blockedXic + ".tmp"), ms1Suffix = Path.GetExtension(blockedMs1 + ".tmp"), xicRefused = refused }));
        foreach (string file in new[] { xic, ms1 })
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) Canonical(writer, json.RootElement);
            File.WriteAllBytes(Path.Combine(output, Path.GetFileNameWithoutExtension(file) + ".canonical.json"), stream.ToArray());
        }
        Console.WriteLine("PASS native cache fixture written and reopened by " + typeof(DeNovoXicCache).Assembly.GetName().Name);
        return 0;
    }

    private static void Canonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Canonical(writer, property.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Canonical(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.Number: writer.WriteNumberValue(value.GetDouble()); break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new InvalidDataException("Unsupported cache JSON value.");
        }
    }
}
