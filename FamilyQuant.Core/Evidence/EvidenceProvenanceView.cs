using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant.Evidence;

public static partial class EvidenceApply
{
    private static readonly JsonSerializerOptions ProvenanceMetadataOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { info =>
            {
                if (info.Type != typeof(EvidenceProvenance)) return;
                foreach (var property in info.Properties)
                    if (property.Name is nameof(EvidenceProvenance.ReferencePanel) or nameof(EvidenceProvenance.FamilyWorld)
                        or nameof(EvidenceProvenance.NearestReviewed)) property.ShouldSerialize = (_, _) => false;
            } },
        },
    };

    // A private staging/hash view borrows the live large tables, which these paths only read.
    // Mutable provenance metadata stays detached, including any fields added in another partial.
    private static EvidenceProvenance ProvenanceView(EvidenceProvenance source)
    {
        var view = JsonStreamCopy.ReadWritten(stream => JsonSerializer.Serialize(stream, source, ProvenanceMetadataOptions), open =>
        {
            using var stream = open();
            return JsonSerializer.Deserialize<EvidenceProvenance>(stream, ProvenanceMetadataOptions)!;
        });
        view.ReferencePanel = source.ReferencePanel;
        view.FamilyWorld = source.FamilyWorld;
        view.NearestReviewed = source.NearestReviewed;
        return view;
    }
}
