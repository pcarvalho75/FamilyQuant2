using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private const string NearestAlphabet = "ACDEFGHIKLMNPQRSTVWY";

        /// <summary>A fixed pseudo-random protein, so the fixture is the same on every machine.</summary>
        private static string NearestBase(int length, uint seed)
        {
            var text = new StringBuilder(length);
            uint state = seed;
            for (int i = 0; i < length; i++)
            {
                state = state * 1664525u + 1013904223u;
                text.Append(NearestAlphabet[(int)((state >> 16) % 20)]);
            }
            return text.ToString();
        }

        /// <summary>The sequence with <paramref name="run"/> residues changed in every <paramref name="step"/>,
        /// so its identity to the original is (1 - run/step) with no gap. The unchanged stretches stay
        /// longer than USEARCH's five-residue word, which it needs to consider a pair at all.</summary>
        private static string NearestMutate(string sequence, int step, int run = 1, int offset = 0)
        {
            char[] residues = sequence.ToCharArray();
            for (int i = offset; i < residues.Length; i += step)
                for (int j = i; j < i + run && j < residues.Length; j++)
                    residues[j] = NearestAlphabet[(NearestAlphabet.IndexOf(residues[j]) + 7) % 20];
            return new string(residues);
        }

        private static FastaItemExtended NearestItem(string identifier, string sequence, bool? reviewed = null,
            string family = "")
        {
            return new FastaItemExtended
            {
                SequenceIdentifier = identifier,
                Description = identifier + " test protein OS=Testus",
                Sequence = sequence,
                Evidence = reviewed == null ? null : new ProteinEvidence
                {
                    Accession = NearestReviewed.EntryAccession(identifier),
                    Reviewed = reviewed,
                    Family = family,
                },
            };
        }

        private static void NearestReviewedTests(string workDir)
        {
            NearestReviewedChoiceTests();

            string a = NearestBase(200, 11);
            string tieSource = NearestBase(180, 23);
            var file = new FamilyClusterFile();
            file.MyExtendedFastaItems.AddRange(new[]
            {
                NearestItem("sp|P10001|ALPHA_TEST", a, true, "Alpha family"),
                NearestItem("tr|A0A001|A0A001_TEST", NearestMutate(a, 10), false),
                NearestItem("sp|P10002|ALPHB_TEST", NearestMutate(a, 10, 2, 5), true, "Alpha family"),
                NearestItem("tr|A0A002|A0A002_TEST", a.Substring(0, 150), false),
                NearestItem("sp|P10004|ISO_TEST", NearestBase(160, 31), true, "Gamma family"),
                NearestItem("sp|P10004-2|ISO_TEST", NearestBase(160, 31), true, "Gamma family"),
                NearestItem("tr|A0A003|A0A003_TEST", NearestBase(170, 47), false),
                NearestItem("sp|P10006|BETA_TEST", tieSource, true, "Beta family"),
                NearestItem("sp|P10005|ALPHT_TEST", tieSource, true, "Alpha family"),
                NearestItem("tr|A0A004|A0A004_TEST", NearestMutate(tieSource, 6), false),
                NearestItem("Reverse_sp|P10001|ALPHA_TEST", a, null),
            });

            List<FastaItemExtended> targets = NearestReviewed.Targets(file);
            List<NeighbourReference> references = NearestReviewed.ReviewedReferences(file);
            Check("the reviewed references are the reviewed targets, the decoy excluded",
                references.Count == 6 && targets.Count == 10, references.Count + " references, " + targets.Count + " targets");

            NearestReviewed.ForgetMemo();
            long runsBefore = NearestReviewed.SearchRunCount;
            NeighbourSearch first;
            try { first = NearestReviewed.Search(targets, references); }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  nearest reviewed protein - USEARCH could not run here (" +
                    ex.GetBaseException().Message.Split('\n')[0] + ")");
                skipped++;
                return;
            }
            Dictionary<string, NearestNeighbour> by = first.ByQuery();
            NearestNeighbour? Of(string id) => by.TryGetValue(id, out NearestNeighbour? n) ? n : null;
            Func<NearestNeighbour, ProteinEvidence?> ReferenceOf = NearestReviewed.ReferenceEvidence(file);

            Check("a reviewed protein is never its own neighbour: P10001 finds P10002, not itself",
                Of("sp|P10001|ALPHA_TEST")?.Accession == "P10002", Of("sp|P10001|ALPHA_TEST")?.Accession);
            Check("the neighbour names its reference in the file and opens that reference's evidence",
                Of("sp|P10001|ALPHA_TEST")?.Reference == "sp|P10002|ALPHB_TEST"
                && ReferenceOf(Of("sp|P10001|ALPHA_TEST")!) == file.MyExtendedFastaItems[2].Evidence,
                Of("sp|P10001|ALPHA_TEST")?.Reference);
            Check("the coverage the gate read is exposed (whole query aligned)",
                Of("sp|P10001|ALPHA_TEST")?.QueryAlignedPercent == 100.0,
                Of("sp|P10001|ALPHA_TEST")?.QueryAlignedPercent.ToString());
            Check("its identity is the substitution count USEARCH sees (80%)",
                Math.Abs((Of("sp|P10001|ALPHA_TEST")?.IdentityPercent ?? 0) - 80.0) < 0.6,
                Of("sp|P10001|ALPHA_TEST")?.IdentityPercent.ToString());
            Check("an unreviewed protein takes its closest reviewed protein (90% to P10001 over 80% to P10002)",
                Of("tr|A0A001|A0A001_TEST")?.Accession == "P10001", Of("tr|A0A001|A0A001_TEST")?.Accession);
            Check("a fragment spanned by its alignment finds the whole protein at 100%",
                Of("tr|A0A002|A0A002_TEST")?.Accession == "P10001"
                && Of("tr|A0A002|A0A002_TEST")?.IdentityPercent == 100.0, Of("tr|A0A002|A0A002_TEST")?.Accession);
            Check("an isoform is the same entry: P10004-2 never takes P10004, and P10004 never takes P10004-2",
                Of("sp|P10004-2|ISO_TEST") == null && Of("sp|P10004|ISO_TEST") == null,
                Of("sp|P10004-2|ISO_TEST")?.Accession + " / " + Of("sp|P10004|ISO_TEST")?.Accession);
            Check("an unrelated protein has no neighbour",
                Of("tr|A0A003|A0A003_TEST") == null, Of("tr|A0A003|A0A003_TEST")?.Accession);
            Check("the decoy is never searched", first.Queries == 10 && Of("Reverse_sp|P10001|ALPHA_TEST") == null,
                first.Queries.ToString());
            NearestNeighbour? tie = Of("tr|A0A004|A0A004_TEST");
            Check("a tie across families is settled by accession and the other family is reported",
                tie?.Accession == "P10005" && tie.Family == "Alpha family"
                && tie.TiedFamilies.SequenceEqual(new[] { "Beta family" }),
                tie == null ? "none" : tie.Accession + " " + string.Join(",", tie.TiedFamilies));
            Check("the neighbours are listed in file order",
                first.Neighbours.Select(n => targets.FindIndex(t => t.SequenceIdentifier == n.Query))
                    .SequenceEqual(first.Neighbours.Select(n => targets.FindIndex(t => t.SequenceIdentifier == n.Query)).OrderBy(i => i)));

            long runsAfterFirst = NearestReviewed.SearchRunCount;
            NeighbourSearch again = NearestReviewed.Search(targets, references);
            Check("the same search answers from the memo without running USEARCH again",
                runsAfterFirst - runsBefore == 1 && NearestReviewed.SearchRunCount == runsAfterFirst,
                (NearestReviewed.SearchRunCount - runsBefore).ToString());
            Check("the memo answers the same neighbours",
                System.Text.Json.JsonSerializer.Serialize(again) == System.Text.Json.JsonSerializer.Serialize(first));

            NearestReviewed.ForgetMemo();
            NeighbourSearch rerun = NearestReviewed.Search(targets, references);
            Check("two USEARCH runs give byte-identical answers",
                NearestReviewed.SearchRunCount == runsAfterFirst + 1
                && System.Text.Json.JsonSerializer.Serialize(rerun) == System.Text.Json.JsonSerializer.Serialize(first));

            NeighbourSearch stored = NearestReviewed.SearchAndStore(file);
            Check("a stored search is current for the same targets and references",
                NearestReviewed.IsCurrent(file.EvidenceSource?.NearestReviewed, targets, references));
            string path = System.IO.Path.Combine(workDir, "nearest-reviewed.fcf");
            file.Save(path);
            FamilyClusterFile reopened = FamilyClusterFile.Load(path);
            Check("the stored neighbours survive a save and reopen, offline",
                reopened.ToJsonText() == file.ToJsonText()
                && reopened.EvidenceSource?.NearestReviewed?.Neighbours.Count == stored.Neighbours.Count);
            Check("a reopened search is still current, so nothing has to run again",
                NearestReviewed.IsCurrent(reopened.EvidenceSource?.NearestReviewed,
                    NearestReviewed.Targets(reopened), NearestReviewed.ReviewedReferences(reopened)));

            file.MyExtendedFastaItems[2].Evidence!.Family = "Delta family";
            List<NeighbourReference> relabelled = NearestReviewed.ReviewedReferences(file);
            Check("a changed reference label makes the stored search stale",
                !NearestReviewed.IsCurrent(file.EvidenceSource?.NearestReviewed, targets, relabelled));
            long beforeRelabel = NearestReviewed.SearchRunCount;
            NeighbourSearch afterRelabel = NearestReviewed.Search(targets, relabelled);
            Check("and is searched again rather than answered from the memo, with the new label",
                NearestReviewed.SearchRunCount == beforeRelabel + 1
                && afterRelabel.ByQuery().TryGetValue("sp|P10001|ALPHA_TEST", out NearestNeighbour? relabelledHit)
                && relabelledHit.Family == "Delta family");

            List<NeighbourReference> scoped = NearestReviewed.ReviewedReferences(file,
                e => string.Equals(e.Family, "Gamma family", StringComparison.Ordinal));
            Check("a scope narrows the references to what the caller chose",
                scoped.Count == 2 && scoped.All(r => r.Family == "Gamma family"), scoped.Count.ToString());
        }

        private static void NearestReviewedChoiceTests()
        {
            NeighbourReference R(string accession, string family) =>
                new NeighbourReference { Accession = accession, Family = family, Sequence = "M" };

            Check("the gate keeps an alignment exactly at 70% of the query (7 of 10)",
                NearestReviewed.PassesGate(60, 7, 10, 50, 70));
            Check("the gate drops an alignment one residue short of it (6 of 10)",
                !NearestReviewed.PassesGate(60, 6, 10, 50, 70));
            Check("the gate keeps identity exactly 50% and drops 49.9%",
                NearestReviewed.PassesGate(50.0, 10, 10, 50, 70) && !NearestReviewed.PassesGate(49.9, 10, 10, 50, 70));

            var hits = new List<(NeighbourReference, double, int, int, int)>
            {
                (R("P00001", "One"), 99.0, 100, 100, 100),      // itself, by accession
                (R("P00009", "Two"), 95.0, 60, 100, 100),       // too little of the query
                (R("P00008", "Two"), 90.0, 95, 100, 100),
                (R("P00007", "One"), 90.0, 98, 100, 100),
                (R("P00006", "Three"), 90.0, 98, 100, 100),
                (R("P00005", "One"), 80.0, 100, 100, 100),
            };
            NearestNeighbour? best = NearestReviewed.Choose("sp|P00001|X", "P00001", hits);
            Check("the choice skips the protein itself and an alignment under the gate",
                best?.Accession == "P00006" || best?.Accession == "P00007", best?.Accession);
            Check("equal identity: the longer alignment wins, then the smaller accession",
                best?.Accession == "P00006" && best.IdentityPercent == 90.0, best?.Accession);
            Check("the other families at the best identity are reported, sorted",
                best != null && best.TiedFamilies.SequenceEqual(new[] { "One", "Two" }),
                best == null ? "none" : string.Join(",", best.TiedFamilies));
            NearestNeighbour? reversed = NearestReviewed.Choose("sp|P00001|X", "P00001", Enumerable.Reverse(hits));
            Check("the choice does not depend on the order USEARCH wrote the hits in",
                System.Text.Json.JsonSerializer.Serialize(reversed) == System.Text.Json.JsonSerializer.Serialize(best));
            Check("nothing past the gate gives no neighbour, never a weaker guess",
                NearestReviewed.Choose("q", "P00001", hits.Where(h => h.Item1.Accession is "P00001" or "P00009")) == null);
            Check("an isoform names its entry and a non-UniProt identifier names itself",
                NearestReviewed.EntryAccession("sp|P12345-2|X_HUMAN") == "P12345"
                && NearestReviewed.EntryAccession("contaminant_P02768|ALBU_HUMAN") == "P02768"
                && NearestReviewed.EntryAccession("my_protein_7") == "my_protein_7");
        }
    }
}
