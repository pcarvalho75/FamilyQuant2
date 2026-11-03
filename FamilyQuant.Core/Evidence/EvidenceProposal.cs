using System;
using System.Collections.Generic;

namespace FamilyQuant.Evidence
{
    public enum EvidenceTier
    {
        Unassigned,
        UniProtFamily,
        ReviewedNeighbour,
        LearnedMarker,
        UnannotatedNeighbour,
    }

    public sealed class EvidenceClassificationOptions
    {
        public IReadOnlyList<string> ScopeTerms { get; init; } = Array.Empty<string>();
        public double AnnotatedIdentityPercent { get; init; } = 60;
        public double UnannotatedIdentityPercent { get; init; } = 70;
        public int MinQueryAlignedPercent { get; init; } = 70;
        public double MarkerVoteFraction { get; init; } = 0.8;
    }

    public sealed class EvidenceFlag
    {
        public string Code { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public sealed class EvidenceProteinProposal
    {
        public int ProteinIndex { get; init; }
        public string Identifier { get; init; } = string.Empty;
        public int PreviousFamilyId { get; init; }
        public string Family { get; init; } = string.Empty;
        public EvidenceTier Tier { get; init; }
        public string Reason { get; init; } = string.Empty;
        public bool HasScopeAnnotation { get; init; }
        public NearestNeighbour? Neighbour { get; init; }
        public IReadOnlyList<EvidenceFlag> Flags { get; init; } = Array.Empty<EvidenceFlag>();
        public bool SuggestedSelected => Tier != EvidenceTier.Unassigned && Flags.Count == 0;
    }

    public sealed class EvidenceFamilyProposal
    {
        public string Family { get; init; } = string.Empty;
        public string SuggestedName { get; init; } = string.Empty;
        public int Proteins { get; init; }
        public int ReviewedProteins { get; init; }
    }

    public sealed class EvidenceDomainMarker
    {
        public string Accession { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Family { get; init; } = string.Empty;
        public int FamilyCarriers { get; init; }
        public int AllCarriers { get; init; }
        public int LabelledCarriers { get; init; }
        public double VoteFraction => LabelledCarriers == 0 ? 0 : (double)FamilyCarriers / LabelledCarriers;
    }

    public sealed class EvidenceProposal
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public PanelEvidenceContext? PanelContext { get; init; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<Panels.PanelProteinAssignment>? PanelAssignments { get; init; }
        public string SourceFingerprint { get; init; } = string.Empty;
        public EvidenceClassificationOptions Options { get; init; } = new EvidenceClassificationOptions();
        public IReadOnlyList<EvidenceProteinProposal> Proteins { get; init; } = Array.Empty<EvidenceProteinProposal>();
        public IReadOnlyList<EvidenceFamilyProposal> Families { get; init; } = Array.Empty<EvidenceFamilyProposal>();
        public IReadOnlyList<EvidenceDomainMarker> Markers { get; init; } = Array.Empty<EvidenceDomainMarker>();
    }
}
