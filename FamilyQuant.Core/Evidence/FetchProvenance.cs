using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence
{
    public sealed class EvidenceRecordSource
    {
        public string Provider { get; set; } = string.Empty;
        public string Release { get; set; } = string.Empty;
        public DateTimeOffset RetrievedAtUtc { get; set; }
    }

    public sealed partial class ProteinEvidence
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public EvidenceRecordSource? Source { get; set; }
    }

    public sealed partial class EvidenceProvenance
    {
        // This provenance describes the latest fetch; unresolved entries keep their own earlier source.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? UnresolvedAccessions { get; set; }
    }
}
