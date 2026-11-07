using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Panels
{
    [Flags]
    public enum PanelAssignmentEvidenceKind
    {
        None = 0,
        UniProtFamily = 1,
        NearestReviewed = 2,
        DomainArchitecture = 4,
        ConfirmedLink = 8,
        AddedByHand = 16,
    }

    /// <summary>One target's family assignment and its own evidence, separate from subclass evidence.
    /// PanelFamilyKey is null when outside or not selected. IsApplied is true only for an applied assignment.
    /// Neighbour facts may describe a refused candidate; EvidenceKind and Reason say what was accepted.</summary>
    public sealed class PanelProteinAssignment
    {
        public int ProteinIndex { get; set; }
        public string Identifier { get; set; } = string.Empty;
        public int? FamilyUserId { get; set; }
        public string? PanelFamilyKey { get; set; }
        public bool IsApplied { get; set; }
        public PanelAssignmentEvidenceKind EvidenceKind { get; set; }
        public List<string> SourceFamilies { get; set; } = new();
        public string? NeighbourAccession { get; set; }
        public double? IdentityPercent { get; set; }
        public double? QueryCoveragePercent { get; set; }
        public List<string> ObservedPfam { get; set; } = new();
        public List<string> ReferencePfam { get; set; } = new();
        public List<string> LinkKeys { get; set; } = new();
        public string Reason { get; set; } = string.Empty;
    }
}

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Panels.PanelProteinAssignment>? PanelProteinAssignments { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PanelProteinAssignmentsFingerprint { get; set; }
    }
}

namespace FamilyQuant.Evidence
{
    public sealed partial class EvidenceRecipe
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Panels.PanelProteinAssignment>? PanelProteinAssignments { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PanelProteinAssignmentsFingerprint { get; set; }
    }
}
