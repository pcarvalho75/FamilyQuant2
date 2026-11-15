using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant;

internal static class FamiliesOnlyJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(ExcludeResults);
        return new(FamilyClusterFile.CurationJsonOptions) { TypeInfoResolver = resolver };
    }

    internal static void ExcludeResults(JsonTypeInfo info)
    {
        if (info.Type != typeof(FastaItemExtended) && info.Type != typeof(FamilyClusterFile)) return;
        foreach (var property in info.Properties)
            if (info.Type == typeof(FastaItemExtended) && property.Name == nameof(FastaItemExtended.XICUser)
                || info.Type == typeof(FamilyClusterFile) && property.Name is nameof(FamilyClusterFile.ReportDetails) or nameof(FamilyClusterFile.ReportInputProvenance))
                property.ShouldSerialize = (_, _) => false;
    }
}
