using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant
{
    /// <summary>Manifest JSON keeps legacy embedded spectrum rows readable while omitting them on writes.</summary>
    internal static class ProjectManifestCodec
    {
        private static readonly JsonSerializerOptions ManifestJsonOptions = CreateManifestJsonOptions();

        /// <summary>
        /// Keeps the legacy SpectrumComparisonRows JSON property DESERIALISABLE while suppressing it on
        /// writes. A JsonIgnore attribute would also prevent old embedded version-1 rows from loading;
        /// ShouldSerialize affects only output, which is exactly the one-way migration needed here.
        /// </summary>
        private static JsonSerializerOptions CreateManifestJsonOptions()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(typeInfo =>
            {
                if (typeInfo.Type != typeof(QuantManifest)) return;

                JsonPropertyInfo? rowsProperty = typeInfo.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, nameof(QuantManifest.SpectrumComparisonRows),
                        StringComparison.Ordinal));
                if (rowsProperty != null) rowsProperty.ShouldSerialize = (_, _) => false;
            });

            return new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = resolver,
            };
        }

        internal static string Write(QuantManifest manifest) =>
            JsonSerializer.Serialize(manifest, ManifestJsonOptions);

        internal static QuantManifest? Read(string text) =>
            JsonSerializer.Deserialize<QuantManifest>(text, ManifestJsonOptions);
    }
}
