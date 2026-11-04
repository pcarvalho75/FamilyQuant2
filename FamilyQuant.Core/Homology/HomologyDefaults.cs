namespace FamilyQuant.Homology;

/// <summary>Current Compute policy versus explicitly recorded legacy policy. A missing Science
/// field in an old saved project continues to mean OFF, never today's default.</summary>
public static class HomologyDefaults
{
    public static HomologyOptions ForNewCompute() => new()
    {
        Science = new()
        {
            QueryRole = HomologyQueryRole.DeNovo,
            ScoreTier = new() { Enabled = true, MaximumPamScoreDrop = 0 },
        },
    };

    public static HomologyOptions ForStoredMapping(FamilyMappingProvenance? mapping) => new()
    {
        MinIdentity = mapping?.MinIdentity ?? HomologyOptions.DefaultMinIdentity,
        Science = mapping?.Science?.AnyEnabled == true ? mapping.Science.Clone() : null,
    };
}
