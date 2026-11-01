using System;
using System.Collections.Generic;

namespace FamilyQuant.Evidence
{
    public sealed class EvidenceDomain
    {
        public string Accession { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public sealed partial class ProteinEvidence
    {
        public string Accession { get; set; } = string.Empty;

        // A missing value means the source did not supply the fact; false and zero are observations.
        public bool? Reviewed { get; set; }
        public string Family { get; set; } = string.Empty;
        public string Subfamily { get; set; } = string.Empty;
        public string FamilyAnnotation { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new List<string>();
        public List<string> TissueSpecificity { get; set; } = new List<string>();
        public List<EvidenceDomain> Pfam { get; set; } = new List<EvidenceDomain>();
        public List<EvidenceDomain> Panther { get; set; } = new List<EvidenceDomain>();
        public int? TransmembraneCount { get; set; }
        public bool? Fragment { get; set; }
    }

    public sealed partial class EvidenceProvenance
    {
        public string Provider { get; set; } = string.Empty;
        public string Release { get; set; } = string.Empty;
        public DateTimeOffset? RetrievedAtUtc { get; set; }
    }
}
