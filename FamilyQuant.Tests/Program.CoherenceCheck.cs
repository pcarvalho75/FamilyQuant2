using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static FastaItemExtended CoherenceProtein(string identifier, int length, int cysteines, bool? fragment,
            int? helices, params string[] domains)
        {
            string body = new string('C', cysteines) + new string('A', Math.Max(0, length - cysteines));
            return new FastaItemExtended
            {
                SequenceIdentifier = identifier,
                Description = identifier + (fragment == true ? " test protein (Fragment) OS=Testus" : " test protein OS=Testus"),
                Sequence = body,
                Evidence = fragment == null && helices == null && domains.Length == 0 ? null : new ProteinEvidence
                {
                    Accession = NearestReviewed.EntryAccession(identifier),
                    Reviewed = false,
                    Fragment = fragment,
                    TransmembraneCount = helices,
                    Pfam = domains.Select(d => new EvidenceDomain { Accession = d, Name = "Name" + d }).ToList(),
                },
            };
        }

        private static void CoherenceCheckTests()
        {
            var members = new List<(string, ProteinEvidence)>();
            for (int i = 0; i < 10; i++)
                members.Add((new string('C', 6 + i % 3) + new string('A', 100 + i - (6 + i % 3)), new ProteinEvidence
                {
                    Accession = "P4000" + i, Reviewed = true, Family = "Alpha family", Fragment = false, TransmembraneCount = 0,
                    Pfam = i < 3 ? new List<EvidenceDomain> { new() { Accession = "PF00001" }, new() { Accession = "PF00002" } }
                                 : new List<EvidenceDomain> { new() { Accession = "PF00001" } },
                }));
            members.Add((new string('A', 20), new ProteinEvidence
            {
                Accession = "P40010", Reviewed = true, Family = "Alpha family", Fragment = true, TransmembraneCount = 0,
            }));
            for (int i = 0; i < 9; i++)
                members.Add((new string('A', 50), new ProteinEvidence
                {
                    Accession = "P4100" + i, Reviewed = true, Family = "Beta family", Fragment = false, TransmembraneCount = 0,
                    Pfam = new List<EvidenceDomain> { new() { Accession = "PF00005" } },
                }));
            Dictionary<string, FamilyProfile> profiles = CoherenceCheck.Learn(members);
            FamilyProfile alpha = profiles["alpha FAMILY"];
            Check("a profile learns its ranges from the complete reviewed members only",
                alpha.Members == 11 && alpha.CompleteMembers == 10 && alpha.MinLength == 100 && alpha.MaxLength == 109
                && alpha.MinCysteines == 6 && alpha.MaxCysteines == 8 && alpha.MaxTransmembrane == 0
                && alpha.DomainCarriers["PF00001"] == 10 && alpha.DomainCarriers["PF00002"] == 3,
                $"{alpha.Members} {alpha.CompleteMembers} {alpha.MinLength}-{alpha.MaxLength} {alpha.MinCysteines}-{alpha.MaxCysteines}");

            string Codes(FastaItemExtended p, string family = "Alpha family") =>
                string.Join(",", CoherenceCheck.Check(profiles, family, p).Select(f => f.Code));
            Check("a protein inside every range carries no flag",
                Codes(CoherenceProtein("tr|A0A301|X", 104, 7, false, 0, "PF00001", "PF00002")) == "");
            Check("longer and richer in cysteines than any reviewed member, with a helix and a foreign domain: four flags in order",
                Codes(CoherenceProtein("tr|A0A302|X", 130, 12, false, 1, "PF00009", "PF00001")) ==
                "coherence.length.above,coherence.cysteines.above,coherence.transmembrane,coherence.domain",
                Codes(CoherenceProtein("tr|A0A302|X", 130, 12, false, 1, "PF00009", "PF00001")));
            Check("a complete protein shorter and poorer in cysteines than any complete member is flagged",
                Codes(CoherenceProtein("tr|A0A303|X", 90, 3, false, 0)) == "coherence.length.below,coherence.cysteines.below",
                Codes(CoherenceProtein("tr|A0A303|X", 90, 3, false, 0)));
            Check("a fragment is never flagged for being short",
                Codes(CoherenceProtein("tr|A0A304|X", 40, 2, true, 0)) == "");
            Check("a protein whose completeness is unknown is judged only for being too long",
                Codes(CoherenceProtein("tr|A0A305|X", 40, 2, null, null)) == ""
                && Codes(CoherenceProtein("tr|A0A306|X", 140, 2, null, null)) == "coherence.length.above");
            Check("a fact the evidence does not supply is not judged (no helix count, no domains)",
                Codes(CoherenceProtein("tr|A0A307|X", 104, 7, false, null)) == "");
            Check("a range learned from fewer complete members than the minimum is not used",
                Codes(CoherenceProtein("tr|A0A308|X", 500, 40, false, 3, "PF00009"), "Beta family") == ""
                && Codes(CoherenceProtein("tr|A0A308|X", 500, 40, false, 3, "PF00009"), "Gamma family") == "");
            List<CoherenceFlag> flags = CoherenceCheck.Check(alpha, CoherenceProtein("tr|A0A302|X", 130, 12, false, 2, "PF00009"));
            Check("each flag gives the protein's value and what the reviewed members show",
                flags[0].Message.Contains("130 amino acids", StringComparison.Ordinal) && flags[0].Message.Contains("100 to 109 amino acids", StringComparison.Ordinal)
                && flags[0].Message.Contains("10 members: source counts not recorded", StringComparison.Ordinal)
                && flags[2].Message.Contains("2 transmembrane helices", StringComparison.Ordinal) && flags[2].Message.Contains("at most 0", StringComparison.Ordinal)
                && flags[3].Message.Contains("NamePF00009 (PF00009)", StringComparison.Ordinal) && flags[3].Message.Contains("11 members: source counts not recorded", StringComparison.Ordinal),
                string.Join(" | ", flags.Select(f => f.Message)));
            var partial = Enumerable.Range(0, 12).Select(i => (new string('A', 30), new ProteinEvidence
            {
                Accession = "P4400" + i, Reviewed = true, Family = "Partial family", Fragment = true, TransmembraneCount = 1,
                Pfam = new List<EvidenceDomain> { new() { Accession = "PF00006" } },
            })).ToList();
            Check("fragments teach no limits: a family known only from fragments flags nothing, helices and domains included",
                CoherenceCheck.Check(CoherenceCheck.Learn(partial)["Partial family"],
                    CoherenceProtein("tr|A0A309|X", 380, 9, false, 8, "PF00007")).Count == 0);
            Check("a member in two families is learned in each, and a family is matched in any case",
                CoherenceCheck.Learn(new[] { ("MAAAC", new ProteinEvidence
                {
                    Accession = "P42000", Reviewed = true, FamilyMemberships = new List<EvidenceFamilyMembership>
                    { new() { Family = "One family" }, new() { Family = "Two family" } },
                }) }).Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(new[] { "One family", "Two family" }));

            var file = new FamilyClusterFile();
            file.MyExtendedFastaItems.Add(new FastaItemExtended
            {
                SequenceIdentifier = "sp|P43000|IN_FILE", Sequence = "MAAAAAAAAC",
                Evidence = new ProteinEvidence { Accession = "P43000", Reviewed = true, Family = "Delta family", Fragment = false },
            });
            file.EvidenceSource = new EvidenceProvenance
            {
                ReferencePanel = new ReferencePanel
                {
                    Entries = new List<PanelEntry>
                    {
                        new() { Accession = "P43000", Sequence = "MAAAAAAAAAAAAAAAAAAAAAAAAAC",
                            Evidence = new ProteinEvidence { Accession = "P43000", Reviewed = true, Family = "Delta family", Fragment = false } },
                        new() { Accession = "P43001", Sequence = "MAAAC",
                            Evidence = new ProteinEvidence { Accession = "P43001", Reviewed = true, Family = "Delta family", Fragment = false } },
                    },
                },
            };
            FamilyProfile delta = CoherenceCheck.Learn(file)["Delta family"];
            Check("the file's reviewed proteins and the panel are learned together, each entry once",
                delta.Members == 2 && delta.MinLength == 5 && delta.MaxLength == 10, $"{delta.Members} {delta.MinLength}-{delta.MaxLength}");
            Check("the same profile and protein always give the same flags",
                System.Text.Json.JsonSerializer.Serialize(CoherenceCheck.Check(alpha, CoherenceProtein("tr|A0A302|X", 130, 12, false, 2, "PF00009")))
                == System.Text.Json.JsonSerializer.Serialize(flags));

            var proposed = new List<FastaItemExtended>
            {
                CoherenceProtein("tr|A0A310|X", 104, 7, false, 0, "PF00001"),
                CoherenceProtein("tr|A0A311|X", 130, 7, false, 0, "PF00001"),
                CoherenceProtein("tr|A0A312|X", 130, 7, false, 0, "PF00001"),
            };
            var proposal = new EvidenceProposal
            {
                SourceFingerprint = "fingerprint",
                Proteins = new[]
                {
                    new EvidenceProteinProposal { ProteinIndex = 0, Identifier = "tr|A0A310|X", Family = "Alpha family", Tier = EvidenceTier.UniProtFamily, Reason = "r0" },
                    new EvidenceProteinProposal { ProteinIndex = 1, Identifier = "tr|A0A311|X", Family = "alpha family", Tier = EvidenceTier.LearnedMarker, Reason = "r1",
                        PreviousFamilyId = 7, HasScopeAnnotation = true, Neighbour = new NearestNeighbour { Query = "tr|A0A311|X", Accession = "P1" },
                        Flags = new[] { new EvidenceFlag { Code = "earlier", Message = "kept" } } },
                    new EvidenceProteinProposal { ProteinIndex = 2, Identifier = "tr|A0A312|X", Family = "", Tier = EvidenceTier.Unassigned, Reason = "r2" },
                },
            };
            EvidenceProposal flagged = CoherenceCheck.Flag(proposal, proposed, profiles);
            Check("an assigned protein outside its family's ranges gains the coherence flag after its own and starts unticked",
                flagged.Proteins[1].Flags.Select(f => f.Code).SequenceEqual(new[] { "earlier", CoherenceCheck.LengthAbove })
                && !flagged.Proteins[1].SuggestedSelected && flagged.Proteins[0].SuggestedSelected,
                string.Join(",", flagged.Proteins[1].Flags.Select(f => f.Code)));
            Check("an unassigned protein and a coherent one are left exactly as they were",
                ReferenceEquals(flagged.Proteins[0], proposal.Proteins[0]) && ReferenceEquals(flagged.Proteins[2], proposal.Proteins[2])
                && flagged.SourceFingerprint == "fingerprint" && ReferenceEquals(flagged.Markers, proposal.Markers));
            var changed = typeof(EvidenceProteinProposal).GetProperties()
                .Where(p => p.CanWrite && p.Name != nameof(EvidenceProteinProposal.Flags)
                    && !Equals(p.GetValue(flagged.Proteins[1]), p.GetValue(proposal.Proteins[1]))).Select(p => p.Name).ToList();
            Check("flagging copies every other property of the proposal row, so none is lost when the type grows",
                changed.Count == 0, string.Join(",", changed));
        }
    }
}
