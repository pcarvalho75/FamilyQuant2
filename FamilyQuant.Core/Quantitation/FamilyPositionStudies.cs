using System;
using System.Collections.Generic;

namespace FamilyQuant;

public static partial class FamilyCompositionStudy
{
    public const string OutsidePositionKey = "$outside";

    public static FamilyPositionStudiesResult BuildPositions(FamilyPeptideAttribution attribution,
        Func<string, string, int, IReadOnlyList<PeptideProteinPlacement>> placements,
        SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique) => BuildPositionsCore(attribution, placements, policy);

    public static void StorePositions(QuantManifest manifest, FamilyPeptideAttribution attribution,
        Func<string, string, int, IReadOnlyList<PeptideProteinPlacement>> placements,
        SharedPeptidePolicy policy = SharedPeptidePolicy.FamilyUnique)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        FamilyPositionStudiesResult result = BuildPositions(attribution, placements, policy);
        manifest.FamilyDomainParts = result.DomainParts;
        manifest.FamilyPeptideLocalization = result.Localization;
    }
}
