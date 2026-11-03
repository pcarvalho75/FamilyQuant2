using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant;

public enum CoveragePeptideSource { Identified, DeNovo }
public enum CoveragePlacementKind { Exact, Aligned }
public enum CoverageAnnotationKind { Domain, Feature }
public sealed record CoverageInterval(int Start, int End);
public sealed record CoveragePeptidePlacement(string Key, string Label, CoveragePeptideSource Source,
    CoveragePlacementKind Kind, IReadOnlyList<CoverageInterval> Segments);
public sealed record CoverageAnnotation(string Label, CoverageAnnotationKind Kind,
    IReadOnlyList<CoverageInterval> Segments, string Note);
public sealed record CoveragePeptideLane(CoveragePeptideSource Source, IReadOnlyList<CoveragePeptidePlacement> Peptides);

/// <summary>Drawing snapshot of source annotations and localized peptide blocks. No sequence search or signal allocation occurs here.</summary>
public sealed class ProteinCoverageMapData
{
    public int FamilyUserId { get; }
    public string ProteinId { get; }
    public string Label { get; }
    public int SequenceLength { get; }
    public string SequenceSha256 { get; }
    public double RankingSignal { get; }
    public string RankingMethod { get; }
    public string PositionState { get; }
    public string PeptideState { get; }
    public string Provenance { get; }
    public IReadOnlyList<CoverageAnnotation> Annotations { get; }
    public IReadOnlyList<CoveragePeptidePlacement> Peptides { get; }
    public IReadOnlyList<CoveragePeptidePlacement> UnplacedPeptides { get; }
    public IReadOnlyList<CoveragePeptideLane> Lanes { get; }

    public static string SequenceHash(string sequence) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(sequence.ToUpperInvariant())));

    public ProteinCoverageMapData(int familyUserId, string proteinId, string label, string sequence,
        ProteinPositionEvidence? positions, string localizationSequenceSha256,
        IEnumerable<CoveragePeptidePlacement> peptides, double rankingSignal,
        string rankingMethod = "All runs pooled; display allocation across candidate proteins and placements")
    {
        if (string.IsNullOrWhiteSpace(proteinId) || string.IsNullOrEmpty(sequence)
            || !double.IsFinite(rankingSignal) || rankingSignal < 0)
            throw new ArgumentException("A protein, sequence and finite nonnegative ranking signal are required.");
        FamilyUserId = familyUserId; ProteinId = proteinId; Label = label; SequenceLength = sequence.Length;
        SequenceSha256 = SequenceHash(sequence); RankingSignal = rankingSignal;
        RankingMethod = rankingMethod;
        var annotations = new List<CoverageAnnotation>();
        void Validate(CoverageInterval range)
        {
            if (range.Start < 1 || range.End < range.Start || range.End > SequenceLength)
                throw new ArgumentException("Coverage coordinates must be one-based, inclusive and within the protein.");
        }
        if (positions == null)
            PositionState = "Domain and feature positions have not been fetched.";
        else if (positions.SequenceLength != SequenceLength
            || !string.Equals(positions.SequenceSha256, SequenceSha256, StringComparison.OrdinalIgnoreCase))
            PositionState = "Domain and feature positions unavailable: source sequence identity differs.";
        else
        {
            PositionState = "Domain and feature positions match the stored protein sequence.";
            foreach (var domain in positions.Pfam)
            {
                var ranges = domain.Fragments.Count > 0
                    ? domain.Fragments.Select(f => new CoverageInterval(f.Start, f.End)).ToArray()
                    : new[] { new CoverageInterval(domain.Start, domain.End) };
                foreach (var range in ranges) Validate(range);
                annotations.Add(new CoverageAnnotation((domain.Name.Length > 0 ? domain.Name + " " : "") + domain.Accession,
                    CoverageAnnotationKind.Domain, Array.AsReadOnly(ranges), ranges.Length > 1 ? "Discontinuous annotation" : ""));
            }
            foreach (var feature in positions.Features)
            {
                bool exact = feature.Start.HasValue && feature.End.HasValue
                    && string.Equals(feature.StartModifier, "EXACT", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(feature.EndModifier, "EXACT", StringComparison.OrdinalIgnoreCase);
                var ranges = exact ? new[] { new CoverageInterval(feature.Start!.Value, feature.End!.Value) } : Array.Empty<CoverageInterval>();
                foreach (var range in ranges) Validate(range);
                annotations.Add(new CoverageAnnotation(feature.Type + (feature.Description.Length > 0 ? ": " + feature.Description : ""),
                    CoverageAnnotationKind.Feature, Array.AsReadOnly(ranges), exact ? "" : "Boundary uncertain; interval not drawn"));
            }
        }
        Provenance = positions == null ? "No stored position sources" : string.Join("; ",
            new[] { positions.DomainSource, positions.FeatureSource }.Where(s => s != null)
                .Select(s => s!.Provider + (s.Release.Length > 0 ? " " + s.Release : "")));
        if (Provenance.Length == 0) Provenance = "Stored position provenance unavailable";
        Annotations = annotations.AsReadOnly();
        var input = peptides.Select(p => p with { Segments = Array.AsReadOnly(p.Segments.ToArray()) }).ToArray();
        if (input.Any(p => string.IsNullOrWhiteSpace(p.Key) || !Enum.IsDefined(p.Source) || !Enum.IsDefined(p.Kind)))
            throw new ArgumentException("Peptide placements need a key and supported source/match kind.", nameof(peptides));
        if (input.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != input.Length)
            throw new ArgumentException("Peptide placement keys must be unique.", nameof(peptides));
        bool peptideSequenceMatches = string.Equals(localizationSequenceSha256, SequenceSha256, StringComparison.OrdinalIgnoreCase);
        if (peptideSequenceMatches)
        {
            foreach (var peptide in input)
            {
                foreach (var range in peptide.Segments) Validate(range);
                if (peptide.Segments.Zip(peptide.Segments.Skip(1), (a, b) => a.End >= b.Start).Any(overlap => overlap))
                    throw new ArgumentException("Peptide blocks must be ordered and non-overlapping.", nameof(peptides));
            }
        }
        PeptideState = peptideSequenceMatches ? "Peptide blocks use the stored protein sequence."
            : "Peptide positions unavailable: localization sequence identity differs.";
        Peptides = Array.AsReadOnly(input.Where(p => peptideSequenceMatches && p.Segments.Count > 0).ToArray());
        UnplacedPeptides = Array.AsReadOnly(input.Where(p => !peptideSequenceMatches || p.Segments.Count == 0).ToArray());
        var lanes = new List<CoveragePeptideLane>();
        foreach (var source in new[] { CoveragePeptideSource.Identified, CoveragePeptideSource.DeNovo })
        {
            var packed = new List<List<CoveragePeptidePlacement>>();
            foreach (var peptide in Peptides.Where(p => p.Source == source).OrderBy(p => p.Segments[0].Start)
                .ThenBy(p => p.Key, StringComparer.Ordinal))
            {
                var lane = packed.FirstOrDefault(l => l[^1].Segments[^1].End < peptide.Segments[0].Start);
                if (lane == null) { lane = new List<CoveragePeptidePlacement>(); packed.Add(lane); }
                lane.Add(peptide);
            }
            lanes.AddRange(packed.Select(l => new CoveragePeptideLane(source, l.AsReadOnly())));
        }
        Lanes = lanes.AsReadOnly();
    }
}
