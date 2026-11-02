using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant.DeNovo
{
    /// <summary>Preserves the historical extraction-cache JSON shape without changing the serializers
    /// used by project files or other consumers of the result models.</summary>
    internal static class ExtractionCacheJson
    {
        internal static readonly JsonSerializerOptions Options = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info =>
            {
                if (info.Type != typeof(DeNovoXicResult)) return;
                for (int i = info.Properties.Count - 1; i >= 0; i--)
                {
                    string name = info.Properties[i].Name;
                    if (name is nameof(DeNovoXicResult.AreaM0) or nameof(DeNovoXicResult.AreaM1)
                        or nameof(DeNovoXicResult.AreaM2) or nameof(DeNovoXicResult.EnvelopeSum))
                        info.Properties.RemoveAt(i);
                }
            });
            return new JsonSerializerOptions
            {
                TypeInfoResolver = resolver,
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.AllowReadingFromString,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
        }
    }
}
