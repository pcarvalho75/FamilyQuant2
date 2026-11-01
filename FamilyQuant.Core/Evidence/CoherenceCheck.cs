using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Evidence
{
    /// <summary>What the reviewed members of one family show: the ranges every check reads.</summary>
    public sealed class FamilyProfile
    {
        public string Family { get; init; } = string.Empty;

        /// <summary>Reviewed members learned from, each entry once (the file's and the panel's).</summary>
        public int Members { get; init; }

        /// <summary>Members that are not fragments; the length and cysteine ranges come from these.</summary>
        public int CompleteMembers { get; init; }
        public int MinLength { get; init; }
        public int MaxLength { get; init; }
        public int MinCysteines { get; init; }
        public int MaxCysteines { get; init; }

        /// <summary>Complete members whose transmembrane count is known, and the largest count among them. A
        /// fragment carries fewer helices than its protein, so fragments would teach a limit too low.</summary>
        public int MembersWithKnownTransmembrane { get; init; }
        public int MaxTransmembrane { get; init; }

        /// <summary>Complete members with at least one Pfam domain (the check needs enough of them), and how
        /// many members, fragments included, carry each domain: a fragment with a domain proves it belongs.</summary>
        public int MembersWithDomains { get; init; }
        public Dictionary<string, int> DomainCarriers { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>One way a protein lies outside what the reviewed members of its family ever show.</summary>
    public sealed class CoherenceFlag
    {
        public string Code { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// The coherence check: a protein put in a family is compared with what that family's reviewed
    /// members ever show (length, cysteines, transmembrane helices, Pfam domains). Each range is learned
    /// from the data, so no family, name or domain is written here. A range learned from fewer members
    /// than <see cref="DefaultMinimumMembers"/> is not used: with n members a true member falls outside
    /// their range with chance 2/(n+1), about one in five at ten.
    /// </summary>
    public static class CoherenceCheck
    {
        public const int DefaultMinimumMembers = 10;
        public const string LengthAbove = "coherence.length.above";
        public const string LengthBelow = "coherence.length.below";
        public const string CysteinesAbove = "coherence.cysteines.above";
        public const string CysteinesBelow = "coherence.cysteines.below";
        public const string Transmembrane = "coherence.transmembrane";
        public const string Domain = "coherence.domain";

        /// <summary>The reviewed members of every family: the file's reviewed targets and the stored
        /// panel's entries, each accession once (the file's copy first), grouped by every family each
        /// belongs to. Families are matched case-insensitively.</summary>
        public static Dictionary<string, FamilyProfile> Learn(FamilyClusterFile file)
        {
            var members = new List<(string Sequence, ProteinEvidence Evidence)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (FastaItemExtended item in NearestReviewed.Targets(file))
            {
                if (item.Evidence?.Reviewed != true || string.IsNullOrEmpty(item.Sequence)) continue;
                if (seen.Add(NearestReviewed.EntryAccession(item.SequenceIdentifier)))
                    members.Add((item.Sequence, item.Evidence));
            }
            foreach (PanelEntry entry in file.EvidenceSource?.ReferencePanel?.Entries ?? new List<PanelEntry>())
                if (entry.Sequence.Length > 0 && seen.Add(entry.Accession)) members.Add((entry.Sequence, entry.Evidence));
            return Learn(members);
        }

        public static Dictionary<string, FamilyProfile> Learn(IEnumerable<(string Sequence, ProteinEvidence Evidence)> reviewed)
        {
            var byFamily = new Dictionary<string, List<(string Sequence, ProteinEvidence Evidence)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in reviewed)
                foreach (string family in ReferencePanelFetcher.FamiliesOf(member.Evidence).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!byFamily.TryGetValue(family, out var list)) byFamily[family] = list = new();
                    list.Add(member);
                }
            var profiles = new Dictionary<string, FamilyProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var (family, list) in byFamily.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                profiles[family] = Profile(family, list);
            return profiles;
        }

        private static FamilyProfile Profile(string family, List<(string Sequence, ProteinEvidence Evidence)> members)
        {
            var complete = members.Where(m => m.Evidence.Fragment != true).ToList();
            var tm = complete.Where(m => m.Evidence.TransmembraneCount != null).Select(m => m.Evidence.TransmembraneCount!.Value).ToList();
            var carriers = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in members.Where(m => m.Evidence.Pfam.Count > 0))
                foreach (string accession in m.Evidence.Pfam.Select(d => d.Accession).Distinct(StringComparer.Ordinal))
                    carriers[accession] = carriers.TryGetValue(accession, out int c) ? c + 1 : 1;
            return new FamilyProfile
            {
                Family = family,
                Members = members.Count,
                CompleteMembers = complete.Count,
                MinLength = complete.Count == 0 ? 0 : complete.Min(m => m.Sequence.Length),
                MaxLength = complete.Count == 0 ? 0 : complete.Max(m => m.Sequence.Length),
                MinCysteines = complete.Count == 0 ? 0 : complete.Min(m => Cysteines(m.Sequence)),
                MaxCysteines = complete.Count == 0 ? 0 : complete.Max(m => Cysteines(m.Sequence)),
                MembersWithKnownTransmembrane = tm.Count,
                MaxTransmembrane = tm.Count == 0 ? 0 : tm.Max(),
                MembersWithDomains = complete.Count(m => m.Evidence.Pfam.Count > 0),
                DomainCarriers = carriers.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            };
        }

        /// <summary>
        /// Every way the protein lies outside the profile, in a fixed order. A fragment, or a protein whose
        /// completeness is unknown, is only judged for being too long or carrying too much: being short is
        /// what a fragment is. Facts the protein's evidence does not supply are not judged.
        /// </summary>
        public static List<CoherenceFlag> Check(FamilyProfile profile, FastaItemExtended protein,
            int minimumMembers = DefaultMinimumMembers)
        {
            var flags = new List<CoherenceFlag>();
            string sequence = protein.Sequence ?? string.Empty;
            ProteinEvidence? evidence = protein.Evidence;
            bool complete = evidence?.Fragment == false && !DescribedAsFragment(protein.Description);
            string family = profile.Family;

            if (profile.CompleteMembers >= minimumMembers && sequence.Length > 0)
            {
                string learned = " (" + Range(profile.MinLength, profile.MaxLength) + " aa in " +
                    Count(profile.CompleteMembers) + " complete reviewed members)";
                if (sequence.Length > profile.MaxLength)
                    flags.Add(Flag(LengthAbove, Count(sequence.Length) + " aa, longer than any reviewed " + family + " member" + learned));
                else if (complete && sequence.Length < profile.MinLength)
                    flags.Add(Flag(LengthBelow, Count(sequence.Length) + " aa, shorter than any complete reviewed " + family + " member" + learned));

                int cysteines = Cysteines(sequence);
                string cysLearned = " (" + Range(profile.MinCysteines, profile.MaxCysteines) + " in " +
                    Count(profile.CompleteMembers) + " complete reviewed members)";
                if (cysteines > profile.MaxCysteines)
                    flags.Add(Flag(CysteinesAbove, Count(cysteines) + " cysteines, more than any reviewed " + family + " member" + cysLearned));
                else if (complete && cysteines < profile.MinCysteines)
                    flags.Add(Flag(CysteinesBelow, Count(cysteines) + " cysteines, fewer than any complete reviewed " + family + " member" + cysLearned));
            }

            if (profile.MembersWithKnownTransmembrane >= minimumMembers && evidence?.TransmembraneCount is int helices
                && helices > profile.MaxTransmembrane)
                flags.Add(Flag(Transmembrane, Count(helices) + " transmembrane " + (helices == 1 ? "helix" : "helices") +
                    ", more than any complete reviewed " + family + " member (at most " + Count(profile.MaxTransmembrane) + " in " +
                    Count(profile.MembersWithKnownTransmembrane) + ")"));

            if (profile.MembersWithDomains >= minimumMembers && evidence != null)
                foreach (EvidenceDomain domain in evidence.Pfam
                    .GroupBy(d => d.Accession, StringComparer.Ordinal).Select(g => g.First())
                    .OrderBy(d => d.Accession, StringComparer.Ordinal))
                    if (!profile.DomainCarriers.ContainsKey(domain.Accession))
                        flags.Add(Flag(Domain, "carries " + (domain.Name.Length > 0 ? domain.Name + " (" + domain.Accession + ")" : domain.Accession) +
                            ", which no reviewed " + family + " member carries (" + Count(profile.Members) + " members)"));
            return flags;
        }

        /// <summary>The check for one protein against the family it was put in; no flag when the family has
        /// no learned profile.</summary>
        public static List<CoherenceFlag> Check(IReadOnlyDictionary<string, FamilyProfile> profiles, string family,
            FastaItemExtended protein, int minimumMembers = DefaultMinimumMembers) =>
            profiles.TryGetValue(family, out FamilyProfile? profile)
                ? Check(profile, protein, minimumMembers) : new List<CoherenceFlag>();

        /// <summary>
        /// The proposal with each assigned protein checked against its family's profile: every coherence
        /// flag is added to the protein's flags, so it is no longer suggested and starts unticked. Nothing
        /// else changes; an unassigned protein, or a family with no learned profile, is left as it was.
        /// </summary>
        public static EvidenceProposal Flag(EvidenceProposal proposal, IReadOnlyList<FastaItemExtended> proteins,
            IReadOnlyDictionary<string, FamilyProfile> profiles, int minimumMembers = DefaultMinimumMembers)
        {
            var rows = proposal.Proteins.Select(row =>
            {
                if (row.Family.Length == 0 || row.ProteinIndex < 0 || row.ProteinIndex >= proteins.Count) return row;
                List<CoherenceFlag> found = Check(profiles, row.Family, proteins[row.ProteinIndex], minimumMembers);
                if (found.Count == 0) return row;
                return new EvidenceProteinProposal
                {
                    ProteinIndex = row.ProteinIndex, Identifier = row.Identifier, PreviousFamilyId = row.PreviousFamilyId,
                    Family = row.Family, Tier = row.Tier, Reason = row.Reason, HasScopeAnnotation = row.HasScopeAnnotation,
                    Neighbour = row.Neighbour,
                    Flags = row.Flags.Concat(found.Select(f => new EvidenceFlag { Code = f.Code, Message = f.Message })).ToArray(),
                };
            }).ToArray();
            return new EvidenceProposal
            {
                SourceFingerprint = proposal.SourceFingerprint, Options = proposal.Options, Proteins = rows,
                Families = proposal.Families, Markers = proposal.Markers,
            };
        }

        public static int Cysteines(string sequence) => sequence.Count(c => c == 'C' || c == 'c');

        private static bool DescribedAsFragment(string? description) =>
            (description ?? string.Empty).IndexOf("(Fragment", StringComparison.OrdinalIgnoreCase) >= 0;

        private static CoherenceFlag Flag(string code, string message) => new CoherenceFlag { Code = code, Message = message };
        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Range(int low, int high) => low == high ? Count(low) : Count(low) + " to " + Count(high);
    }
}
