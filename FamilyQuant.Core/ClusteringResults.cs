using System.Collections.Generic;

namespace FamilyQuant
{
    public sealed record ClusteringResult(FamilyClusterFile Document, bool RequestedExhaustive,
        bool UsedExhaustive, IReadOnlyList<string> Warnings);

    public sealed record RedundancyResult(int Before, int After, string OutputFile, double IdentityPercent);
}
