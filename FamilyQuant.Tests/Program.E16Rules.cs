using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void E16RuleTests()
        {
            ProteinEvidence E(string accession, bool reviewed, string family, string keyword = "", params string[] domains) => new ProteinEvidence
            {
                Accession = accession, Reviewed = reviewed, Family = family, Fragment = false,
                Keywords = keyword.Length > 0 ? new List<string> { keyword } : new List<string>(),
                Pfam = domains.Select(d => new EvidenceDomain { Accession = d, Name = "Name" + d }).ToList(),
            };
            FastaItemExtended P(string id, ProteinEvidence? e, string sequence = "MAAAAAAAAAAAAAAAAAAAC") =>
                new FastaItemExtended { SequenceIdentifier = id, Description = id + " OS=Testus", Sequence = sequence, Evidence = e };
            NearestNeighbour N(string query, string reference, string accession, string family) => new NearestNeighbour
            {
                Query = query, Reference = reference, Accession = accession, Family = family,
                IdentityPercent = 90, AlignmentLength = 20, QueryLength = 20, ReferenceLength = 20,
            };

            // F1 and F2, through the classifier.
            var proteins = new List<FastaItemExtended>
            {
                P("sp|P80001|IN_SCOPE", E("P80001", true, "Alpha family", "Chosen", "PF00001")),
                P("sp|P80002|OUT_OF_SCOPE", E("P80002", true, "Alpha family", "", "PF00001")),
                P("contaminant_P80003|CONT", E("P80003", true, "Alpha family", "Chosen", "PF00001")),
                P("tr|A0A801|JOINS", E("A0A801", false, "", "", "PF00001")),
                P("tr|A0A802|STAYS_OUT", E("A0A802", false, "", "", "PF00001")),
            };
            var neighbours = new Dictionary<string, NearestNeighbour>
            {
                ["tr|A0A801|JOINS"] = N("tr|A0A801|JOINS", "sp|P80001|IN_SCOPE", "P80001", "Alpha family"),
                ["tr|A0A802|STAYS_OUT"] = N("tr|A0A802|STAYS_OUT", "sp|P80002|OUT_OF_SCOPE", "P80002", "Alpha family"),
            };
            EvidenceProposal proposal = EvidenceClassifier.Propose(proteins,
                new EvidenceClassificationOptions { ScopeTerms = new[] { "Chosen" } }, neighbours);
            EvidenceProteinProposal Row(int i) => proposal.Proteins.Single(r => r.ProteinIndex == i);
            Check("F1: a contaminant of the database never joins a family, even in scope with a UniProt family",
                Row(2).Family == "" && Row(2).Tier == EvidenceTier.Unassigned
                && Row(2).Reason == "A contaminant of the database is never a candidate for a family.", Row(2).Family);
            Check("F2: an unannotated protein joins through a neighbour that carries the scope words",
                Row(3).Family == "Alpha family" && Row(3).Tier == EvidenceTier.UnannotatedNeighbour, Row(3).Reason);
            Check("F2: and stays out, flagged, when the neighbour carries none of them either",
                Row(4).Family == "" && Row(4).Flags.Any(f => f.Code == "neighbour_out_of_scope"), Row(4).Reason);

            // F2 at the search: with a scope, the panel offers only entries that carry the scope words.
            var scoped = new FamilyClusterFile();
            scoped.MyExtendedFastaItems.Add(P("sp|P80010|FILE", E("P80010", true, "Alpha family", "Chosen")));
            scoped.EvidenceSource = new EvidenceProvenance
            {
                ReferencePanel = new ReferencePanel
                {
                    Entries = new List<PanelEntry>
                    {
                        new() { Accession = "P80011", Sequence = "MAAAC", Evidence = E("P80011", true, "Alpha family", "Chosen") },
                        new() { Accession = "P80012", Sequence = "MAAAC", Evidence = E("P80012", true, "Alpha family", "Other") },
                    },
                },
            };
            Check("F2: with a scope the panel offers only entries that carry the scope words; without one, all",
                NearestReviewed.References(scoped, e => e.Keywords.Contains("Chosen")).Select(r => r.Accession).SequenceEqual(new[] { "P80010", "P80011" })
                && NearestReviewed.References(scoped).Count == 3);

            // F3, through the architecture split.
            string Seq(int length, int cysteines) => new string('C', cysteines) + new string('A', length - cysteines);
            var family = new List<FastaItemExtended>();
            for (int i = 0; i < 10; i++)
                family.Add(P("sp|P81" + i.ToString("000") + "|SHORT", E("P81" + i.ToString("000"), true, "Alpha family", "Chosen", "PF00011"), Seq(40 + i, 6 + i % 3)));
            for (int i = 0; i < 10; i++)
                family.Add(P("sp|P82" + i.ToString("000") + "|LONG", E("P82" + i.ToString("000"), true, "Alpha family", "Chosen", "PF00011", "PF00012"), Seq(400 + 5 * i, 20 + i % 5)));
            ProteinEvidence Bare(string accession, bool? fragment) => new ProteinEvidence { Accession = accession, Reviewed = false, Family = "Alpha family", Fragment = fragment };
            family.Add(P("tr|A0A811|FITS_SHORT", Bare("A0A811", false), Seq(45, 7)));
            family.Add(P("tr|A0A812|FRAGMENT", Bare("A0A812", true), Seq(45, 7)));
            family.Add(P("tr|A0A813|FITS_NONE", Bare("A0A813", false), Seq(200, 7)));
            family.Add(P("tr|A0A814|UNKNOWN", Bare("A0A814", null), Seq(45, 7)));
            var archProposal = new EvidenceProposal
            {
                SourceFingerprint = EvidenceClassifier.Fingerprint(family),
                Proteins = family.Select((p, i) => new EvidenceProteinProposal
                {
                    ProteinIndex = i, Identifier = p.SequenceIdentifier, Family = "Alpha family", Tier = EvidenceTier.UniProtFamily,
                }).ToArray(),
            };
            EvidenceArchitectureProposal arch = EvidenceArchitecture.Suggest(family, archProposal).Single();
            EvidenceArchitectureGroup GroupOf(int index) => arch.Groups.Single(g => g.ProteinIndices.Contains(index));
            Check("F3: a complete member with no domain call joins the one combination whose reviewed ranges hold it",
                GroupOf(20).Domains.SequenceEqual(new[] { "PF00011" }) && GroupOf(20).InferredProteinIndices.SequenceEqual(new[] { 20 })
                && GroupOf(20).ProteinIndices.Count == 11, GroupOf(20).SuggestedName);
            Check("F3: a fragment, a member whose completeness is unknown, and one no range holds stay without domain calls",
                GroupOf(21).Domains.Count == 0 && GroupOf(22).Domains.Count == 0 && GroupOf(23).Domains.Count == 0
                && GroupOf(21).InferredProteinIndices.Count == 0);
            Check("F3: members with domain calls keep exactly the groups they had",
                GroupOf(0).Key == "[\"PF00011\"]" && GroupOf(10).Key == "[\"PF00011\",\"PF00012\"]" && GroupOf(10).ProteinIndices.Count == 10);
            EvidenceArchitectureProposal tooFew = EvidenceArchitecture.Suggest(family, archProposal, minimumMembers: 11).Single();
            Check("F3: a combination with fewer complete reviewed members than the minimum places nobody",
                tooFew.Groups.All(g => g.InferredProteinIndices.Count == 0));
        }
    }
}
