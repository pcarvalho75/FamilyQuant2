using FamilyQuant.DeNovo;
using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant;

public static partial class FamilyCompositionStudy
{
    private sealed record PositionInfo(PeptideProteinPlacement Source, List<PeptideResidueInterval> Intervals, bool Valid);
    private sealed record PositionChoice(PositionInfo Info, double Weight);
    private const string PositionUnresolved = "Position not resolved";

    private static string IntervalKey(IEnumerable<PeptideResidueInterval> intervals) => string.Join(";", intervals.Select(i =>
        i.Start.ToString(CultureInfo.InvariantCulture) + ":" + i.End.ToString(CultureInfo.InvariantCulture)));

    private static FamilyPositionStudiesResult BuildPositionsCore(FamilyPeptideAttribution attribution,
        Func<string, string, int, IReadOnlyList<PeptideProteinPlacement>> placements, SharedPeptidePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(placements);
        var cache = new Dictionary<(string Side, string Sequence, int Family), List<PositionChoice>>();
        var domainWeights = new Dictionary<(string Side, string Sequence, int Family), IReadOnlyList<PartWeight>>();
        var productWeights = new Dictionary<(string Side, string Sequence, int Family), IReadOnlyList<PartWeight>>();
        List<PositionChoice> Resolve(FamilyAttributedPeptide assigned, int family)
        {
            string sequence = DeNovoReconciler.NormalizeSequence(assigned.Peptide.Sequence);
            var key = (assigned.Peptide.Side, sequence, family);
            if (cache.TryGetValue(key, out var known)) return known;
            var infos = placements(key.Side, sequence, family)
                .Select(p => PositionInformation(p, sequence)).OrderBy(p => p.Source.ProteinIdentifier, StringComparer.Ordinal)
                .ThenBy(p => p.Source.MatchKind).ThenBy(p => IntervalKey(p.Intervals), StringComparer.Ordinal).ToList();
            var groups = infos.GroupBy(p => p.Source.ProteinIdentifier, StringComparer.Ordinal).ToList();
            var weighted = groups.SelectMany(g => g.Select(p => new PositionChoice(p, 1.0 / groups.Count / g.Count()))).ToList();
            cache[key] = weighted;
            return weighted;
        }
        IReadOnlyList<PartWeight> Weights(FamilyAttributedPeptide assigned, int family, bool products)
        {
            string sequence = DeNovoReconciler.NormalizeSequence(assigned.Peptide.Sequence);
            var key = (assigned.Peptide.Side, sequence, family);
            var profiles = products ? productWeights : domainWeights;
            if (!profiles.TryGetValue(key, out var weights))
                profiles[key] = weights = ConservativePositionWeights(Resolve(assigned, family), sequence.Length, products);
            return weights;
        }
        var result = new FamilyPositionStudiesResult
        {
            DomainParts = new()
            {
                MembershipSha256 = attribution.MembershipSha256,
                Policy = policy,
                Method = "All runs pooled. Peptide signal is apportioned by covered residue length. Only the " +
                    "overlap common to every candidate placement is resolved; remaining signal is position not resolved. " +
                    "Verified source-sequence hashes are required for annotations. Domain fragments and alignment gaps " +
                    "are respected. Nested products use the narrowest exact feature; conflicting or uncertain boundaries " +
                    "remain unresolved. Outside is reported only with fetched annotations. Final-part rounding preserves family totals.",
                Domains = BuildParts(attribution, policy, (a, f) => Weights(a, f, false), PositionUnresolved),
                Products = BuildParts(attribution, policy, (a, f) => Weights(a, f, true), PositionUnresolved),
            },
        };
        List<FamilySignalBreakdown> ranking = BuildParts(attribution, policy, (a, f) =>
        {
            var choices = Resolve(a, f);
            return choices.Count == 0 ? UnknownPositionWeights() : choices.Select(p => new PartWeight(
                p.Info.Valid ? p.Info.Source.ProteinIdentifier : null,
                p.Info.Valid ? p.Info.Source.ProteinIdentifier : PositionUnresolved, p.Weight)).ToArray();
        }, PositionUnresolved);
        result.Localization = BuildLocalization(attribution, policy, Resolve, ranking);
        return result;
    }

    private static PositionInfo PositionInformation(PeptideProteinPlacement source, string sequence)
    {
        var intervals = (source.Intervals ?? new()).OrderBy(i => i.Start).ThenBy(i => i.End)
            .Select(i => new PeptideResidueInterval { Start = i.Start, End = i.End }).ToList();
        bool valid = !string.IsNullOrWhiteSpace(source.ProteinIdentifier) && !string.IsNullOrEmpty(source.ProteinSequence)
            && sequence.Length > 0 && Enum.IsDefined(source.MatchKind) && intervals.Count > 0;
        int previous = 0;
        foreach (var interval in intervals)
        {
            valid &= interval.Start > previous && interval.End >= interval.Start && interval.Start >= 1
                && interval.End <= (source.ProteinSequence?.Length ?? 0);
            previous = interval.End;
        }
        if (valid) valid = intervals.Sum(i => i.End - i.Start + 1) <= sequence.Length;
        if (valid && source.MatchKind == PeptideLocalizationMatchKind.ExactContainment)
        {
            valid = intervals.Count == 1 && intervals[0].End - intervals[0].Start + 1 == sequence.Length
                && string.Equals(source.ProteinSequence!.Substring(intervals[0].Start - 1, sequence.Length)
                    .ToUpperInvariant().Replace('I', 'L'), sequence, StringComparison.Ordinal);
        }
        return new(source, intervals, valid);
    }

    private static bool VerifiedPositions(PositionInfo info) => info.Source.Positions != null
        && !string.IsNullOrEmpty(info.Source.Positions.SequenceSha256)
        && ProteinPositionFetcher.MatchesSequence(info.Source.Positions, info.Source.ProteinSequence);

    private static PartWeight[] UnknownPositionWeights() => new[] { new PartWeight(null, PositionUnresolved, 1) };

    private static IReadOnlyList<PartWeight> ConservativePositionWeights(List<PositionChoice> choices, int queryLength, bool products)
    {
        if (choices.Count == 0) return UnknownPositionWeights();
        var profiles = choices.Select(p => PositionProfile(p.Info, queryLength, products)
            .ToDictionary(w => w.Key ?? string.Empty, StringComparer.Ordinal)).ToList();
        var resolved = new List<PartWeight>();
        foreach (string key in profiles.SelectMany(p => p.Keys).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            double minimum = profiles.Min(p => p.TryGetValue(key, out var value) ? value.Weight : 0);
            if (minimum <= 0) continue;
            string label = profiles.Where(p => p.ContainsKey(key)).Select(p => p[key].Label).OrderBy(s => s, StringComparer.Ordinal).First();
            resolved.Add(new(key, label, minimum));
        }
        resolved.Add(new(null, PositionUnresolved, Math.Max(0, 1 - resolved.Sum(w => w.Weight))));
        return resolved;
    }

    private static IReadOnlyList<PartWeight> PositionProfile(PositionInfo info, int queryLength, bool products)
    {
        if (!info.Valid || !VerifiedPositions(info)) return UnknownPositionWeights();
        ProteinPositionEvidence positions = info.Source.Positions!;
        if (products ? positions.FeatureSource == null : positions.DomainSource == null) return UnknownPositionWeights();
        bool Range(int start, int end) => start >= 1 && end >= start && end <= info.Source.ProteinSequence.Length;
        if (!products && positions.Pfam.Any(d => string.IsNullOrEmpty(d.Accession) || d.Accession == OutsidePositionKey
            || (d.Fragments.Count > 0 ? d.Fragments.Any(r => !Range(r.Start, r.End)) : !Range(d.Start, d.End))))
            return UnknownPositionWeights();
        if (products && positions.Features.Any(f => !f.Start.HasValue || !f.End.HasValue
            || !Range(f.Start.Value, f.End.Value) || f.StartModifier != "EXACT" || f.EndModifier != "EXACT"))
            return UnknownPositionWeights();
        var counts = new Dictionary<string, (string Label, int Count)>(StringComparer.Ordinal);
        void Add(string key, string label, int count)
        {
            if (counts.TryGetValue(key, out var previous)) counts[key] = (previous.Label, previous.Count + count);
            else counts[key] = (label, count);
        }
        foreach (var interval in info.Intervals)
        {
            for (int residue = interval.Start; residue <= interval.End; residue++)
            {
                if (!products)
                {
                    var domains = positions.Pfam.Where(d => d.Fragments.Count > 0
                        ? d.Fragments.Any(r => residue >= r.Start && residue <= r.End) : residue >= d.Start && residue <= d.End)
                        .GroupBy(d => d.Accession, StringComparer.Ordinal).ToList();
                    if (domains.Count == 0) Add(OutsidePositionKey, "Outside annotated domains", 1);
                    else if (domains.Count == 1)
                    {
                        var domain = domains[0].OrderBy(d => d.Name, StringComparer.Ordinal).First();
                        Add(domain.Accession, string.IsNullOrEmpty(domain.Name) ? domain.Accession : domain.Name, 1);
                    }
                    else Add(string.Empty, PositionUnresolved, 1);
                }
                else
                {
                    var features = positions.Features.Where(f => residue >= f.Start && residue <= f.End).ToList();
                    if (features.Count == 0) { Add(OutsidePositionKey, "Outside named products", 1); continue; }
                    int narrowest = features.Min(f => f.End!.Value - f.Start!.Value);
                    var leaves = features.Where(f => f.End!.Value - f.Start!.Value == narrowest)
                        .GroupBy(f => "feature:" + JsonSerializer.Serialize(new[] { f.Type, f.Description }), StringComparer.Ordinal).ToList();
                    if (leaves.Count != 1) Add(string.Empty, PositionUnresolved, 1);
                    else
                    {
                        var feature = leaves[0].First();
                        Add(leaves[0].Key, feature.Type + (feature.Description.Length > 0 ? ": " + feature.Description : string.Empty), 1);
                    }
                }
            }
        }
        int missingQuery = queryLength - info.Intervals.Sum(i => i.End - i.Start + 1);
        if (missingQuery > 0) Add(string.Empty, PositionUnresolved, missingQuery);
        return counts.Select(p => new PartWeight(p.Key.Length == 0 ? null : p.Key, p.Value.Label, p.Value.Count / (double)queryLength)).ToArray();
    }

    private static FamilyPeptideLocalizationResult BuildLocalization(FamilyPeptideAttribution attribution, SharedPeptidePolicy policy,
        Func<FamilyAttributedPeptide, int, List<PositionChoice>> resolve, List<FamilySignalBreakdown> ranking)
    {
        var totals = FamilyEnvelopeQuant.BuildFromAttributedPeptides(attribution).Families.ToDictionary(f => f.FamilyUserId);
        var segments = new Dictionary<(int Family, string Protein, string Side, string Sequence, PeptideLocalizationMatchKind Kind, string Intervals), LocalizedPeptideSegment>();
        var metadata = new Dictionary<(int Family, string Protein), FamilyRankedProtein>();
        foreach (var assigned in attribution.Peptides)
        {
            double factor = CreditFactor(assigned, policy);
            if (assigned.FamilyUserIds.Count == 0) continue;
            string sequence = DeNovoReconciler.NormalizeSequence(assigned.Peptide.Sequence);
            foreach (int family in assigned.FamilyUserIds)
            {
                double credit = PrimaryCredit(assigned, totals[family], policy, factor);
                foreach (var choice in resolve(assigned, family).Where(c => c.Info.Valid))
                {
                    PositionInfo info = choice.Info;
                    var source = info.Source;
                    var key = (family, source.ProteinIdentifier, assigned.Peptide.Side, sequence, source.MatchKind, IntervalKey(info.Intervals));
                    if (!segments.TryGetValue(key, out var segment)) segments[key] = segment = new()
                    {
                        FamilyUserId = family, ProteinIdentifier = source.ProteinIdentifier, Side = assigned.Peptide.Side,
                        NormalizedSequence = sequence, MatchKind = source.MatchKind,
                        FamilyMembershipCount = assigned.FamilyUserIds.Count,
                        Intervals = info.Intervals.Select(i => new PeptideResidueInterval { Start = i.Start, End = i.End }).ToList(),
                    };
                    segment.Signal += credit * choice.Weight;
                    string hash = ProteinPositionFetcher.SequenceHash(source.ProteinSequence);
                    var proteinKey = (family, source.ProteinIdentifier);
                    if (metadata.TryGetValue(proteinKey, out var prior))
                    {
                        if (prior.ProteinSequenceSha256 != hash) throw new ArgumentException("One protein identifier has inconsistent source sequences.");
                        prior.PositionEvidenceVerified &= VerifiedPositions(info);
                    }
                    else metadata[proteinKey] = new()
                    {
                        ProteinIdentifier = source.ProteinIdentifier, ProteinLength = source.ProteinSequence.Length,
                        ProteinSequenceSha256 = hash, PositionEvidenceVerified = VerifiedPositions(info),
                    };
                }
            }
        }
        var result = new FamilyPeptideLocalizationResult
        {
            MembershipSha256 = attribution.MembershipSha256,
            Policy = policy,
            Method = "All runs pooled. Exact containment and retained alignments are distinguished. Intervals cover only " +
                "aligned protein residues, excluding gaps. Signal is divided equally among candidate proteins and their " +
                "placements for display ranking, not an independent protein amount. Unlocalized allocations are retained. " +
                "Ranks use descending allocated signal then ordinal protein identifiers. Same side/sequence/position repeats are pooled.",
        };
        foreach (var family in ranking)
        {
            var row = new FamilyProteinRanking
            {
                FamilyUserId = family.FamilyUserId, FamilySignal = family.FamilySignal,
                UnlocalizedSignal = family.Parts.Single(p => p.IsUnresolved).Signal,
            };
            foreach (var part in family.Parts.Where(p => p.PartKey != null && p.Signal > 0)
                .OrderByDescending(p => p.Signal).ThenBy(p => p.PartKey, StringComparer.Ordinal))
            {
                var protein = metadata[(family.FamilyUserId, part.PartKey!)];
                var proteinSegments = segments.Values.Where(s => s.FamilyUserId == family.FamilyUserId && s.ProteinIdentifier == part.PartKey).ToList();
                var last = proteinSegments.OrderByDescending(s => s.Signal).ThenBy(s => s.NormalizedSequence, StringComparer.Ordinal)
                    .ThenBy(s => s.Side, StringComparer.Ordinal).ThenBy(s => IntervalKey(s.Intervals), StringComparer.Ordinal).First();
                double remainder = part.Signal - proteinSegments.Where(s => !ReferenceEquals(s, last)).Sum(s => s.Signal);
                if (remainder < 0 || Math.Abs(remainder - last.Signal) > Math.Max(part.Signal, 1) * 1e-12)
                    throw new InvalidOperationException("Localized signal does not close to its protein allocation.");
                last.Signal = remainder;
                protein.Rank = row.Proteins.Count + 1;
                protein.CreditedSignal = part.Signal;
                protein.ExactSignal = proteinSegments.Where(s => s.MatchKind == PeptideLocalizationMatchKind.ExactContainment).Sum(s => s.Signal);
                protein.AlignedSignal = proteinSegments.Where(s => s.MatchKind == PeptideLocalizationMatchKind.Aligned).Sum(s => s.Signal);
                row.Proteins.Add(protein);
            }
            foreach (var pair in metadata.Where(p => p.Key.Family == family.FamilyUserId
                && row.Proteins.All(r => r.ProteinIdentifier != p.Key.Protein)).OrderBy(p => p.Key.Protein, StringComparer.Ordinal))
            {
                pair.Value.Rank = row.Proteins.Count + 1;
                row.Proteins.Add(pair.Value);
            }
            result.Families.Add(row);
        }
        result.Segments = segments.Values.OrderBy(s => s.FamilyUserId).ThenBy(s => s.ProteinIdentifier, StringComparer.Ordinal)
            .ThenBy(s => s.Side, StringComparer.Ordinal).ThenBy(s => s.NormalizedSequence, StringComparer.Ordinal)
            .ThenBy(s => s.MatchKind).ThenBy(s => IntervalKey(s.Intervals), StringComparer.Ordinal).ToList();
        return result;
    }
}
