using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Evidence
{
    public static class EvidenceClassifier
    {
        private static readonly Regex Words = new Regex(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static EvidenceProposal Propose(IReadOnlyList<FastaItemExtended> proteins,
            EvidenceClassificationOptions options, IReadOnlyDictionary<string, NearestNeighbour>? neighbours = null,
            Func<NearestNeighbour, ProteinEvidence?>? referenceEvidence = null, ISet<int>? maskedLabels = null)
        {
            ArgumentNullException.ThrowIfNull(proteins);
            ArgumentNullException.ThrowIfNull(options);
            Validate(options);
            var scope = new ScopeMatcher(options.ScopeTerms);
            if (maskedLabels != null && maskedLabels.Any(i => i < 0 || i >= proteins.Count))
                throw new ArgumentOutOfRangeException(nameof(maskedLabels));
            List<int> targets = Enumerable.Range(0, proteins.Count).Where(i => !NearestReviewed.IsDecoy(proteins[i])).ToList();
            if (targets.Select(i => proteins[i].SequenceIdentifier).Distinct(StringComparer.Ordinal).Count() != targets.Count)
                throw new ArgumentException("Protein identifiers must be unique to associate reviewed neighbours.", nameof(proteins));
            var inScope = targets.ToDictionary(i => i, i => scope.Matches(proteins[i].Evidence));
            var families = targets.ToDictionary(i => i, i => maskedLabels?.Contains(i) == true ? string.Empty : UniqueFamily(proteins[i].Evidence));
            var canonical = families.Values.Where(f => f.Length > 0).GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(f => f, StringComparer.Ordinal).First(), StringComparer.OrdinalIgnoreCase);
            string Label(string family) => canonical.TryGetValue(family, out string? name) ? name : family;
            // A contaminant of the database is treated like a decoy: it teaches nothing and joins nothing.
            List<int> candidates = targets.Where(i => !IsContaminant(proteins[i])).ToList();
            List<EvidenceDomainMarker> markers = LearnMarkers(proteins, candidates, families, inScope, options.MarkerVoteFraction, maskedLabels);
            var markerByDomain = markers.ToDictionary(m => m.Accession, StringComparer.Ordinal);
            var familyDomains = candidates.Where(i => inScope[i] && families[i].Length > 0 && maskedLabels?.Contains(i) != true)
                .GroupBy(i => families[i], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.SelectMany(i => Domains(proteins[i].Evidence)).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
            var sourceByIdentifier = targets.ToDictionary(i => proteins[i].SequenceIdentifier, i => proteins[i].Evidence, StringComparer.Ordinal);
            referenceEvidence ??= n => sourceByIdentifier.TryGetValue(n.Reference, out ProteinEvidence? e) ? e : null;
            var maskedIdentifiers = maskedLabels == null ? new HashSet<string>(StringComparer.Ordinal)
                : maskedLabels.Select(i => proteins[i].SequenceIdentifier).ToHashSet(StringComparer.Ordinal);
            var rows = new List<EvidenceProteinProposal>();
            foreach (int index in targets)
            {
                FastaItemExtended item = proteins[index];
                ProteinEvidence? evidence = item.Evidence;
                bool annotated = inScope[index];
                var flags = new List<EvidenceFlag>();
                NearestNeighbour? neighbour = neighbours != null && neighbours.TryGetValue(item.SequenceIdentifier, out NearestNeighbour? hit) ? hit : null;
                string assigned = string.Empty;
                EvidenceTier tier = EvidenceTier.Unassigned;
                string reason;
                if (IsContaminant(item))
                {
                    reason = "A contaminant of the database is never a candidate for a family.";
                }
                else if (HasMultipleFamilies(evidence))
                {
                    reason = "The source lists distinct family memberships; choose a family after reviewing their contexts.";
                    Flag("multiple_source_families", reason);
                }
                else if (annotated && families[index].Length > 0)
                {
                    assigned = Label(families[index]);
                    tier = EvidenceTier.UniProtFamily;
                    reason = "The user scope matches a keyword or tissue annotation, and UniProt supplies one family.";
                }
                else
                {
                    bool validNeighbour = ValidNeighbour(item, neighbour, referenceEvidence, maskedIdentifiers, flags);
                    if (annotated && validNeighbour && Passes(neighbour!, options.AnnotatedIdentityPercent, options.MinQueryAlignedPercent))
                    {
                        assigned = Label(neighbour!.Family);
                        tier = EvidenceTier.ReviewedNeighbour;
                        reason = NeighbourReason(neighbour, "Scope annotation and a reviewed neighbour");
                    }
                    else if (annotated)
                    {
                        List<EvidenceDomainMarker> matching = Domains(evidence).Where(markerByDomain.ContainsKey).Select(d => markerByDomain[d]).ToList();
                        List<string> voted = matching.Select(m => m.Family).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (voted.Count == 1)
                        {
                            EvidenceDomainMarker marker = matching.OrderByDescending(m => m.VoteFraction).ThenByDescending(m => m.FamilyCarriers)
                                .ThenBy(m => m.Accession, StringComparer.Ordinal).First();
                            assigned = Label(voted[0]);
                            tier = EvidenceTier.LearnedMarker;
                            reason = "Scope annotation and learned domain " + marker.Name + ": "
                                + marker.FamilyCarriers.ToString(CultureInfo.InvariantCulture) + " of "
                                + marker.LabelledCarriers.ToString(CultureInfo.InvariantCulture) + " labelled scope carriers vote for this family.";
                        }
                        else
                        {
                            reason = voted.Count > 1 ? "Learned domains support different families; review the conflict."
                                : "No source family, qualifying reviewed neighbour or learned marker supports a family.";
                            if (voted.Count > 1) Flag("conflicting_markers", reason);
                        }
                    }
                    else if (validNeighbour && Passes(neighbour!, options.UnannotatedIdentityPercent, options.MinQueryAlignedPercent)
                        && !scope.Matches(referenceEvidence(neighbour!)))
                    {
                        // Out of scope on both sides: nothing says the protein belongs to what the user asked for.
                        reason = "The neighbour qualifies, but it carries none of the scope words either, so it cannot bring in a protein without them.";
                        Flag("neighbour_out_of_scope", reason);
                    }
                    else if (validNeighbour && Passes(neighbour!, options.UnannotatedIdentityPercent, options.MinQueryAlignedPercent))
                    {
                        string family = Label(neighbour!.Family);
                        HashSet<string> expected = familyDomains.TryGetValue(family, out HashSet<string>? profile)
                            ? new HashSet<string>(profile, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
                        expected.UnionWith(Domains(referenceEvidence(neighbour)));
                        HashSet<string> observed = Domains(evidence);
                        bool conflict = observed.Where(markerByDomain.ContainsKey)
                            .Any(d => !string.Equals(markerByDomain[d].Family, family, StringComparison.OrdinalIgnoreCase));
                        if (observed.Overlaps(expected) && !conflict)
                        {
                            assigned = family;
                            tier = EvidenceTier.UnannotatedNeighbour;
                            reason = NeighbourReason(neighbour, "Reviewed neighbour and compatible observed domains; no scope annotation");
                        }
                        else
                        {
                            reason = observed.Count == 0 ? "The neighbour qualifies, but no observed domains establish compatibility."
                                : "The neighbour qualifies, but observed domains do not establish a compatible family.";
                            Flag("domain_compatibility", reason);
                        }
                    }
                    else reason = "No qualifying reviewed neighbour supports an unannotated protein.";
                }
                rows.Add(new EvidenceProteinProposal
                {
                    ProteinIndex = index, Identifier = item.SequenceIdentifier, PreviousFamilyId = item.UserID,
                    Family = assigned, Tier = tier, Reason = reason, HasScopeAnnotation = annotated,
                    Neighbour = neighbour, Flags = flags.ToArray(),
                });
                void Flag(string code, string message) => flags.Add(new EvidenceFlag { Code = code, Message = message });
            }
            return new EvidenceProposal
            {
                SourceFingerprint = Fingerprint(proteins),
                Options = new EvidenceClassificationOptions
                {
                    ScopeTerms = options.ScopeTerms.Select(s => s.Trim()).ToArray(),
                    AnnotatedIdentityPercent = options.AnnotatedIdentityPercent, UnannotatedIdentityPercent = options.UnannotatedIdentityPercent,
                    MinQueryAlignedPercent = options.MinQueryAlignedPercent, MarkerVoteFraction = options.MarkerVoteFraction,
                },
                Proteins = rows.ToArray(), Markers = markers,
                Families = rows.Where(r => r.Family.Length > 0).GroupBy(r => r.Family, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new EvidenceFamilyProposal
                    {
                        Family = g.Key, SuggestedName = g.Key, Proteins = g.Count(),
                        ReviewedProteins = g.Count(r => proteins[r.ProteinIndex].Evidence?.Reviewed == true),
                    }).ToArray(),
            };
        }

        public static bool IsInScope(ProteinEvidence? evidence, IReadOnlyList<string> terms) => new ScopeMatcher(terms).Matches(evidence);

        public static string Fingerprint(IReadOnlyList<FastaItemExtended> proteins)
        {
            using var hash = new LengthPrefixedSha256();
            foreach (FastaItemExtended item in proteins)
            {
                Append(item.SequenceIdentifier); Append(item.Sequence); Append(item.UserID.ToString(CultureInfo.InvariantCulture));
                Append(item.USearchID.ToString(CultureInfo.InvariantCulture)); Append(item.UserAnnotation);
                Append(JsonSerializer.Serialize(item.Evidence));
            }
            return hash.Finish();
            void Append(string? value) => hash.Append(value ?? string.Empty);
        }

        private static List<EvidenceDomainMarker> LearnMarkers(IReadOnlyList<FastaItemExtended> proteins, List<int> targets,
            Dictionary<int, string> families, Dictionary<int, bool> inScope, double fraction, ISet<int>? masked)
        {
            var all = new Dictionary<string, int>(StringComparer.Ordinal);
            var votes = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (int i in targets)
            {
                if (masked?.Contains(i) == true) continue;
                foreach (string domain in Domains(proteins[i].Evidence))
                {
                    all[domain] = all.GetValueOrDefault(domain) + 1;
                    if (!names.TryGetValue(domain, out List<string>? domainNames)) names[domain] = domainNames = new List<string>();
                    domainNames.AddRange(proteins[i].Evidence!.Pfam.Where(p => p.Accession == domain && !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.Name));
                    if (!inScope[i] || families[i].Length == 0) continue;
                    if (!votes.TryGetValue(domain, out Dictionary<string, int>? byFamily))
                        votes[domain] = byFamily = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    byFamily[families[i]] = byFamily.GetValueOrDefault(families[i]) + 1;
                }
            }
            var result = new List<EvidenceDomainMarker>();
            foreach (var domain in votes.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var ranked = domain.Value.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
                var best = ranked[0];
                if (ranked.Count > 1 && ranked[1].Value == best.Value) continue;
                int labelled = domain.Value.Values.Sum();
                if ((double)best.Value / labelled + 1e-12 < fraction) continue;
                string name = names[domain.Key].GroupBy(n => n, StringComparer.Ordinal).OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault() ?? domain.Key;
                result.Add(new EvidenceDomainMarker
                {
                    Accession = domain.Key, Name = name, Family = best.Key, FamilyCarriers = best.Value,
                    AllCarriers = all[domain.Key], LabelledCarriers = labelled,
                });
            }
            return result;
        }

        private static HashSet<string> Domains(ProteinEvidence? evidence) => EvidenceDomains.Ordered(evidence).ToHashSet(StringComparer.Ordinal);

        private static bool IsContaminant(FastaItemExtended protein) =>
            SequenceLabels.IsContaminant(protein.SequenceIdentifier);

        private static bool HasMultipleFamilies(ProteinEvidence? evidence) => evidence?.FamilyMemberships != null
            && evidence.FamilyMemberships.Select(m => m.Family).Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();

        private static string UniqueFamily(ProteinEvidence? evidence)
        {
            if (evidence == null || HasMultipleFamilies(evidence)) return string.Empty;
            return (evidence.Family ?? string.Empty).Trim();
        }

        private static bool ValidNeighbour(FastaItemExtended item, NearestNeighbour? neighbour,
            Func<NearestNeighbour, ProteinEvidence?> referenceEvidence, HashSet<string> maskedReferences, List<EvidenceFlag> flags)
        {
            if (neighbour == null) return false;
            string? refusal = null;
            if (neighbour.Query != item.SequenceIdentifier || maskedReferences.Contains(neighbour.Reference))
                refusal = "The neighbour does not name an eligible query/reference pair.";
            else if (neighbour.Reference == item.SequenceIdentifier
                || string.Equals(NearestReviewed.EntryAccession(item.SequenceIdentifier), neighbour.Accession, StringComparison.Ordinal))
                refusal = "A protein cannot provide its own reviewed label.";
            else if (neighbour.Family.Length == 0 || neighbour.TiedFamilies.Count > 0)
                refusal = "The nearest reviewed evidence does not provide one unambiguous family.";
            else
            {
                ProteinEvidence? reference = referenceEvidence(neighbour);
                if (reference != null && (reference.Reviewed != true || HasMultipleFamilies(reference)
                    || !string.Equals(reference.Family, neighbour.Family, StringComparison.OrdinalIgnoreCase)))
                    refusal = "The neighbour's reference evidence does not support its reviewed family label.";
            }
            if (refusal == null) return true;
            flags.Add(new EvidenceFlag { Code = "neighbour_refused", Message = refusal });
            return false;
        }

        private static bool Passes(NearestNeighbour neighbour, double identity, int coverage) =>
            double.IsFinite(neighbour.IdentityPercent) && neighbour.IdentityPercent <= 100
            && neighbour.QueryAlignedResidues is int covered
            && NearestReviewed.PassesGate(neighbour.IdentityPercent, covered, neighbour.QueryLength, identity, coverage);

        private static string NeighbourReason(NearestNeighbour neighbour, string start) => start + ": "
            + neighbour.IdentityPercent.ToString("0.##", CultureInfo.InvariantCulture) + "% identity, "
            + neighbour.QueryCoveragePercent!.Value.ToString("0.##", CultureInfo.InvariantCulture) + "% of query residues aligned ("
            + neighbour.QueryAlignedResidues!.Value.ToString(CultureInfo.InvariantCulture) + "/"
            + neighbour.QueryLength.ToString(CultureInfo.InvariantCulture) + "); "
            + neighbour.AlignmentLength.ToString(CultureInfo.InvariantCulture) + " alignment columns; query length "
            + neighbour.QueryLength.ToString(CultureInfo.InvariantCulture) + " residues.";

        internal static void Validate(EvidenceClassificationOptions options)
        {
            if (options.ScopeTerms == null || options.ScopeTerms.Count == 0 || options.ScopeTerms.All(t => string.IsNullOrWhiteSpace(t) || Words.Matches(t).Count == 0))
                throw new ArgumentException("Provide at least one scope word or phrase.", nameof(options));
            if (!double.IsFinite(options.AnnotatedIdentityPercent) || options.AnnotatedIdentityPercent <= 0 || options.AnnotatedIdentityPercent > 100
                || !double.IsFinite(options.UnannotatedIdentityPercent) || options.UnannotatedIdentityPercent <= 0 || options.UnannotatedIdentityPercent > 100
                || options.MinQueryAlignedPercent < 0 || options.MinQueryAlignedPercent > 100
                || !double.IsFinite(options.MarkerVoteFraction) || options.MarkerVoteFraction <= 0 || options.MarkerVoteFraction > 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Evidence thresholds must be finite and within their percentage/fraction ranges.");
        }

        private sealed class ScopeMatcher
        {
            private readonly List<string[]> phrases;
            public ScopeMatcher(IReadOnlyList<string> terms) => phrases = terms.Select(Tokens).Where(t => t.Length > 0).ToList();
            public bool Matches(ProteinEvidence? evidence)
            {
                if (evidence == null) return false;
                foreach (string text in (evidence.Keywords ?? new List<string>()).Concat(evidence.TissueSpecificity ?? new List<string>()))
                {
                    string[] words = Tokens(text);
                    foreach (string[] phrase in phrases)
                        for (int start = 0; start + phrase.Length <= words.Length; start++)
                        {
                            bool matches = true;
                            for (int j = 0; j < phrase.Length; j++) if (words[start + j] != phrase[j]) { matches = false; break; }
                            if (matches) return true;
                        }
                }
                return false;
            }
            private static string[] Tokens(string text) => Words.Matches(text ?? string.Empty).Select(m => m.Value.ToUpperInvariant()).ToArray();
        }
    }
}
