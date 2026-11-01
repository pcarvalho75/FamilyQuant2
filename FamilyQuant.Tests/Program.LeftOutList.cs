using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void LeftOutListTests()
        {
            FastaItemExtended P(string id, params string[] domains) => new FastaItemExtended
            {
                SequenceIdentifier = id, Description = id + " protein OS=Testus", Sequence = "MAAAA",
                Evidence = new ProteinEvidence
                {
                    Accession = NearestReviewed.EntryAccession(id), Reviewed = false, Fragment = false,
                    Pfam = domains.Select(d => new EvidenceDomain { Accession = d, Name = "Name" + d }).ToList(),
                },
            };
            var proteins = new List<FastaItemExtended>
            {
                P("tr|A0A701|IN", "PF00001"),
                P("tr|A0A702|ELSEWHERE", "PF00001"),
                P("tr|A0A703|NOSCOPE", "PF00001"),
                P("tr|A0A704|TWO", "PF00001", "PF00002"),
                P("Reverse_tr|A0A705|DECOY", "PF00001"),
                P("contaminant_P02768|ALBU_HUMAN", "PF00001"),
                P("tr|A0A707|NOMARKER", "PF00003"),
            };
            EvidenceProteinProposal Row(int i, string family, bool scope, string reason, params string[] flags) => new EvidenceProteinProposal
            {
                ProteinIndex = i, Identifier = proteins[i].SequenceIdentifier, Family = family, HasScopeAnnotation = scope, Reason = reason,
                Tier = family.Length > 0 ? EvidenceTier.UniProtFamily : EvidenceTier.Unassigned,
                Flags = flags.Select(f => new EvidenceFlag { Code = "x", Message = f }).ToArray(),
            };
            var proposal = new EvidenceProposal
            {
                Markers = new[]
                {
                    new EvidenceDomainMarker { Accession = "PF00001", Name = "One", Family = "Alpha family", FamilyCarriers = 9, LabelledCarriers = 10, AllCarriers = 12 },
                    new EvidenceDomainMarker { Accession = "PF00002", Name = "Two", Family = "Beta family", FamilyCarriers = 5, LabelledCarriers = 5, AllCarriers = 5 },
                },
                Proteins = new[]
                {
                    Row(0, "alpha FAMILY", true, "UniProt supplies one family."),
                    Row(1, "Beta family", true, "A reviewed neighbour."),
                    Row(2, "", false, "No qualifying reviewed neighbour supports an unannotated protein."),
                    Row(3, "", true, "Learned domains support different families; review the conflict.", "Learned domains support different families; review the conflict."),
                    Row(4, "", false, "decoy"), Row(5, "", false, "contaminant"), Row(6, "", true, "none"),
                },
            };
            List<LeftOutRow> rows = LeftOutList.Build(proteins, proposal);
            Check("the list holds the marker carriers left out of the marker's family, by family then file order",
                rows.Select(r => r.Family + ":" + r.Identifier).SequenceEqual(new[]
                {
                    "Alpha family:tr|A0A702|ELSEWHERE", "Alpha family:tr|A0A703|NOSCOPE", "Alpha family:tr|A0A704|TWO",
                    "Beta family:tr|A0A704|TWO",
                }), string.Join(" | ", rows.Select(r => r.Family + ":" + r.Identifier)));
            Check("a protein in its marker's family, a decoy, a contaminant and a protein with no marker are not listed",
                rows.All(r => r.Identifier is not ("tr|A0A701|IN" or "Reverse_tr|A0A705|DECOY" or "contaminant_P02768|ALBU_HUMAN" or "tr|A0A707|NOMARKER")));
            Check("each row says why: the vote, then where it went or why it went nowhere",
                rows[0].Why == "Carries One, which 9 of 10 labelled carriers in scope place in Alpha family. Assigned to Beta family instead: A reviewed neighbour."
                && rows[1].Why.EndsWith("Outside the scope words. Not assigned: No qualifying reviewed neighbour supports an unannotated protein.", StringComparison.Ordinal)
                && rows[2].Why.EndsWith("Flagged: Learned domains support different families; review the conflict.", StringComparison.Ordinal)
                && rows[0].AssignedFamily == "Beta family" && rows[1].AssignedFamily == "",
                string.Join(" | ", rows.Select(r => r.Why)));
            string[] lines = LeftOutList.BuildCsv(rows).Split("\r\n");
            Check("the left-out CSV has a header and one RFC 4180 row per left-out pair",
                lines[0] == "Family,Accession,Locus,Description,Length,Reviewed,Fragment,MarkerDomain,MarkerName,AssignedInstead,Why"
                && lines[1].StartsWith("Alpha family,A0A702,tr|A0A702|ELSEWHERE,tr|A0A702|ELSEWHERE protein OS=Testus,5,no,no,PF00001,One,Beta family,", StringComparison.Ordinal)
                && lines.Length == 6, lines.Length + " " + lines[1]);
        }
    }
}
