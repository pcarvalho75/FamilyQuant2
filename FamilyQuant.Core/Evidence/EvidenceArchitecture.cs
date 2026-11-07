using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Evidence
{
    public sealed class EvidenceArchitectureGroup
    {
        public string Key { get; init; } = string.Empty;
        public string SuggestedName { get; init; } = string.Empty;
        public string DominantDomainAccession { get; init; } = string.Empty;
        public IReadOnlyList<string> Domains { get; init; } = Array.Empty<string>();
        public IReadOnlyList<int> ProteinIndices { get; init; } = Array.Empty<int>();
        public int ReviewedProteins { get; init; }

        /// <summary>Members with no domain call placed here because their length and cysteines fall inside
        /// the ranges of this combination's complete reviewed members, and of no other combination's.</summary>
        public IReadOnlyList<int> InferredProteinIndices { get; init; } = Array.Empty<int>();
    }

    /// <summary>A member with no domain call the split could not place, and why: shown to the user, because
    /// keeping a split apart decides which side it lands on.</summary>
    public sealed class EvidenceArchitectureUndetermined
    {
        public int ProteinIndex { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    public sealed class EvidenceArchitectureProposal
    {
        public string Family { get; init; } = string.Empty;
        public bool CanSplit { get; init; }
        public IReadOnlyList<EvidenceArchitectureGroup> Groups { get; init; } = Array.Empty<EvidenceArchitectureGroup>();

        /// <summary>When the family can split: its members with no domain call that no learned range placed.</summary>
        public IReadOnlyList<EvidenceArchitectureUndetermined> Undetermined { get; init; } = Array.Empty<EvidenceArchitectureUndetermined>();
    }

    public sealed class EvidenceArchitectureChoice
    {
        public string Family { get; set; } = string.Empty;
        public string ArchitectureKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Split { get; set; }
    }

    public static class EvidenceArchitecture
    {
        /// <summary>
        /// The architecture groups of each proposed family. A complete member with no domain call joins the one
        /// domain combination whose complete reviewed members (the file's and the panel's, at least
        /// <paramref name="minimumMembers"/>) span its length and its cysteine count; when none or several do,
        /// it stays among the members with no domain calls. A fragment is never placed this way: being short is
        /// what a fragment is.
        /// </summary>
        public static IReadOnlyList<EvidenceArchitectureProposal> Suggest(IReadOnlyList<FastaItemExtended> proteins,
            EvidenceProposal proposal, ReferencePanel? panel = null, int minimumMembers = CoherenceCheck.DefaultMinimumMembers)
        {
            ArgumentNullException.ThrowIfNull(proteins);
            ArgumentNullException.ThrowIfNull(proposal);
            ReferencePanelFetcher.RequireScope(panel, proposal.Options.ScopeTerms);
            if (EvidenceClassifier.Fingerprint(proteins) != proposal.SourceFingerprint)
                throw new InvalidOperationException("The evidence proposal no longer matches the current proteins.");
            var result = new List<EvidenceArchitectureProposal>();
            foreach (var family in proposal.Proteins.Where(p => p.Family.Length > 0).GroupBy(p => p.Family, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<int> members = family.Select(p => p.ProteinIndex).OrderBy(i => i).ToList();
                var reviewedSupport = new Dictionary<string, int>(StringComparer.Ordinal);
                var allSupport = new Dictionary<string, int>(StringComparer.Ordinal);
                var domainNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (int index in members)
                {
                    ProteinEvidence? evidence = proteins[index].Evidence;
                    foreach (string domain in Domains(evidence))
                    {
                        allSupport[domain] = allSupport.GetValueOrDefault(domain) + 1;
                        if (evidence!.Reviewed == true) reviewedSupport[domain] = reviewedSupport.GetValueOrDefault(domain) + 1;
                        if (!domainNames.TryGetValue(domain, out List<string>? names)) domainNames[domain] = names = new List<string>();
                        names.AddRange(evidence.Pfam.Where(d => d.Accession == domain && !string.IsNullOrWhiteSpace(d.Name))
                            .Select(d => d.Name).Distinct(StringComparer.Ordinal));
                    }
                }
                var ranges = LearnRanges(family.Key, proteins, panel, minimumMembers, domainNames);
                var inferred = new HashSet<int>();
                var unplaced = new List<EvidenceArchitectureUndetermined>();
                string KeyOf(int index)
                {
                    FastaItemExtended protein = proteins[index];
                    string own = JsonSerializer.Serialize(Domains(protein.Evidence));
                    if (Domains(protein.Evidence).Length > 0) return own;
                    string? why = protein.Evidence?.Fragment == true || !MotifCheck.IsComplete(protein)
                            ? "A fragment with no domain call: its length cannot tell which group it belongs to."
                        : protein.Evidence?.Fragment == null ? "No domain call, and its source does not say whether it is complete."
                        : null;
                    if (why == null)
                    {
                        int length = (protein.Sequence ?? string.Empty).Length, cysteines = CoherenceCheck.Cysteines(protein.Sequence ?? string.Empty);
                        List<string> fits = ranges.Where(r => length >= r.Value.MinLength && length <= r.Value.MaxLength
                            && cysteines >= r.Value.MinCysteines && cysteines <= r.Value.MaxCysteines).Select(r => r.Key).ToList();
                        if (fits.Count == 1)
                        {
                            inferred.Add(index);
                            return fits[0];
                        }
                        why = fits.Count == 0
                            ? "No domain call, and its length and cysteines lie outside every learned group."
                            : "No domain call, and its length and cysteines fit " + fits.Count.ToString(CultureInfo.InvariantCulture) + " learned groups.";
                    }
                    unplaced.Add(new EvidenceArchitectureUndetermined { ProteinIndex = index, Reason = why });
                    return own;
                }
                // Cross-references give domain presence, so the stable key describes a combination.
                // No positional order is inferred from the provider's list order.
                var combinations = members.GroupBy(KeyOf, StringComparer.Ordinal)
                    .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                string DomainName(string domain) => (domainNames.TryGetValue(domain, out List<string>? known) ? known : new List<string>())
                    .GroupBy(n => n, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => g.Key).FirstOrDefault() ?? domain;
                var groupNames = combinations.ToDictionary(g => g.Key, g =>
                {
                    string[] domains = DomainsOfKey(g.Key);
                    return domains.Length == 0 ? family.Key + " (no domain calls)" : string.Join(" + ", domains.Select(DomainName));
                }, StringComparer.Ordinal);
                var ambiguousNames = groupNames.Values.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in combinations)
                {
                    string name = groupNames[group.Key];
                    if (ambiguousNames.Contains(name)) name += " [" + group.Key + "]";
                    string unique = name;
                    int suffix = 2;
                    while (!usedNames.Add(unique)) unique = name + " (" + (suffix++).ToString(CultureInfo.InvariantCulture) + ")";
                    groupNames[group.Key] = unique;
                }
                List<EvidenceArchitectureGroup> groups = combinations.Select(group =>
                    {
                        string[] domains = DomainsOfKey(group.Key);
                        string dominant = domains.OrderByDescending(d => reviewedSupport.GetValueOrDefault(d))
                            .ThenByDescending(d => allSupport.GetValueOrDefault(d)).ThenBy(d => d, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty;
                        return new EvidenceArchitectureGroup
                        {
                            Key = group.Key, SuggestedName = groupNames[group.Key], DominantDomainAccession = dominant,
                            Domains = domains, ProteinIndices = group.ToArray(),
                            ReviewedProteins = group.Count(i => proteins[i].Evidence?.Reviewed == true),
                            InferredProteinIndices = group.Where(inferred.Contains).ToArray(),
                        };
                    }).ToList();
                bool canSplit = groups.Count(g => g.Domains.Count > 0) > 1;
                result.Add(new EvidenceArchitectureProposal
                {
                    Family = family.Key, CanSplit = canSplit, Groups = groups,
                    Undetermined = canSplit ? unplaced.OrderBy(u => u.ProteinIndex).ToArray() : Array.Empty<EvidenceArchitectureUndetermined>(),
                });
            }
            return result;
        }

        private static string[] DomainsOfKey(string key) => JsonSerializer.Deserialize<string[]>(key) ?? Array.Empty<string>();

        /// <summary>Length and cysteine ranges of each domain combination among the family's complete reviewed
        /// members (the file's targets and the panel's entries, each accession once), for combinations with at
        /// least <paramref name="minimumMembers"/> such members. Their domain names are added to
        /// <paramref name="names"/>, so a combination only the panel shows can still be named.</summary>
        private static Dictionary<string, (int MinLength, int MaxLength, int MinCysteines, int MaxCysteines)> LearnRanges(
            string family, IReadOnlyList<FastaItemExtended> proteins, ReferencePanel? panel, int minimumMembers,
            Dictionary<string, List<string>> names)
        {
            var reviewed = new List<(string Sequence, ProteinEvidence Evidence)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (FastaItemExtended protein in proteins)
            {
                if (NearestReviewed.IsDecoy(protein) || protein.Evidence?.Reviewed != true || string.IsNullOrEmpty(protein.Sequence)) continue;
                if (SequenceLabels.IsContaminant(protein.SequenceIdentifier)) continue;
                if (seen.Add(NearestReviewed.EntryAccession(protein.SequenceIdentifier))) reviewed.Add((protein.Sequence, protein.Evidence));
            }
            foreach (PanelEntry entry in panel?.Entries ?? new List<PanelEntry>())
                if (entry.Sequence.Length > 0 && seen.Add(entry.Accession)) reviewed.Add((entry.Sequence, entry.Evidence));

            var ranges = new Dictionary<string, (int, int, int, int)>(StringComparer.Ordinal);
            foreach (var combination in reviewed
                .Where(m => m.Evidence.Fragment == false && Domains(m.Evidence).Length > 0
                    && ReferencePanelFetcher.FamiliesOf(m.Evidence).Contains(family, StringComparer.OrdinalIgnoreCase))
                .GroupBy(m => JsonSerializer.Serialize(Domains(m.Evidence)), StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (combination.Count() < minimumMembers) continue;
                ranges[combination.Key] = (combination.Min(m => m.Sequence.Length), combination.Max(m => m.Sequence.Length),
                    combination.Min(m => CoherenceCheck.Cysteines(m.Sequence)), combination.Max(m => CoherenceCheck.Cysteines(m.Sequence)));
                foreach (EvidenceDomain domain in combination.SelectMany(m => m.Evidence.Pfam).Where(d => !string.IsNullOrWhiteSpace(d.Name)))
                {
                    if (!names.TryGetValue(domain.Accession, out List<string>? list)) names[domain.Accession] = list = new List<string>();
                    if (list.Count == 0) list.Add(domain.Name);
                }
            }
            return ranges;
        }

        private static string[] Domains(ProteinEvidence? evidence) => EvidenceDomains.Ordered(evidence);
    }
}
