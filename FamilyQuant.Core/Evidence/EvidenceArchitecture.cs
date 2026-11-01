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
    }

    public sealed class EvidenceArchitectureProposal
    {
        public string Family { get; init; } = string.Empty;
        public bool CanSplit { get; init; }
        public IReadOnlyList<EvidenceArchitectureGroup> Groups { get; init; } = Array.Empty<EvidenceArchitectureGroup>();
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
        public static IReadOnlyList<EvidenceArchitectureProposal> Suggest(IReadOnlyList<FastaItemExtended> proteins,
            EvidenceProposal proposal)
        {
            ArgumentNullException.ThrowIfNull(proteins);
            ArgumentNullException.ThrowIfNull(proposal);
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
                // Cross-references give domain presence, so the stable key describes a combination.
                // No positional order is inferred from the provider's list order.
                var combinations = members.GroupBy(i => JsonSerializer.Serialize(Domains(proteins[i].Evidence)), StringComparer.Ordinal)
                    .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                string DomainName(string domain) => domainNames[domain].GroupBy(n => n, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => g.Key).FirstOrDefault() ?? domain;
                var groupNames = combinations.ToDictionary(g => g.Key, g =>
                {
                    string[] domains = Domains(proteins[g.First()].Evidence);
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
                        string[] domains = Domains(proteins[group.First()].Evidence);
                        string dominant = domains.OrderByDescending(d => reviewedSupport.GetValueOrDefault(d))
                            .ThenByDescending(d => allSupport.GetValueOrDefault(d)).ThenBy(d => d, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty;
                        return new EvidenceArchitectureGroup
                        {
                            Key = group.Key, SuggestedName = groupNames[group.Key], DominantDomainAccession = dominant,
                            Domains = domains, ProteinIndices = group.ToArray(),
                            ReviewedProteins = group.Count(i => proteins[i].Evidence?.Reviewed == true),
                        };
                    }).ToList();
                result.Add(new EvidenceArchitectureProposal
                {
                    Family = family.Key, CanSplit = groups.Count(g => g.Domains.Count > 0) > 1, Groups = groups,
                });
            }
            return result;
        }

        private static string[] Domains(ProteinEvidence? evidence) => evidence?.Pfam == null ? Array.Empty<string>()
            : evidence.Pfam.Select(d => d.Accession).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.Ordinal)
                .OrderBy(d => d, StringComparer.Ordinal).ToArray();
    }
}
