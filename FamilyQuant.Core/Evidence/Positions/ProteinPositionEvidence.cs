using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Evidence.Positions;

/// <summary>Coordinates on the canonical source protein, 1-based and inclusive. A consumer must
/// verify SequenceSha256 against its protein before placing peptides; equal length alone is insufficient.
/// Null on ProteinEvidence means positions have not been fetched. Empty lists on this record mean
/// the sources were queried and returned no matching annotations.</summary>
public sealed class ProteinPositionEvidence
{
    public string Accession { get; set; } = string.Empty;
    public int SequenceLength { get; set; }
    public string SequenceSha256 { get; set; } = string.Empty;
    public List<PfamDomainPosition> Pfam { get; set; } = new();
    public List<ProteinFeaturePosition> Features { get; set; } = new();
    public PositionEvidenceSource? DomainSource { get; set; }
    public PositionEvidenceSource? FeatureSource { get; set; }
}

/// <summary>One Pfam hit, including each fragment of a discontinuous hit. Repeated hits remain
/// separate records, even when their accessions are equal. Start/End enclose the fragments.</summary>
public sealed class PfamDomainPosition
{
    public string Accession { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Start { get; set; }
    public int End { get; set; }
    public List<ProteinPositionRange> Fragments { get; set; } = new();
}

public sealed class ProteinPositionRange
{
    public int Start { get; set; }
    public int End { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>UniProt Chain, Peptide, Propeptide or Signal annotation. Unknown coordinates remain null;
/// modifiers such as EXACT, OUTSIDE and UNKNOWN are retained, never converted into exact boundaries.</summary>
public sealed class ProteinFeaturePosition
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FeatureId { get; set; } = string.Empty;
    public int? Start { get; set; }
    public int? End { get; set; }
    public string StartModifier { get; set; } = string.Empty;
    public string EndModifier { get; set; } = string.Empty;
    public List<PositionEvidenceReference> Evidences { get; set; } = new();
}

public sealed class PositionEvidenceReference
{
    public string EvidenceCode { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public sealed class PositionEvidenceSource
{
    public string Provider { get; set; } = string.Empty;
    public string Release { get; set; } = string.Empty;
    public DateTimeOffset RetrievedAtUtc { get; set; }
    public List<PositionEvidenceResponse> Responses { get; set; } = new();
}

public sealed class PositionEvidenceResponse
{
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}
