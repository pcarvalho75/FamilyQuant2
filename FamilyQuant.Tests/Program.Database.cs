using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        /// <summary>
        /// First coverage for RedundancyTools, which arrived from another machine with none. Exercises
        /// the exact-subset path (identity 100), which is the default and runs no aligner: subsets are
        /// dropped, the input order survives, the surviving superset is annotated with what it absorbed,
        /// and the file on disk round-trips.
        /// </summary>
        private static void RedundancyTests(string workDir)
        {
            // ALPHA contains BETA's whole sequence; GAMMA is unrelated; DELTA duplicates BETA exactly.
            // Input order is deliberately not alphabetical and not by length, so order-keeping is visible.
            string input = Path.Combine(workDir, "redundant.fasta");
            File.WriteAllText(input,
                ">sp|G1|GAMMA venom myoglobin\nMKAWLLVLGL\n" +
                ">sp|B1|BETA metalloproteinase fragment\nQEEHRSPHIV\n" +
                ">sp|A1|ALPHA metalloproteinase\nSSQEEHRSPHIVATS\n" +
                ">sp|D1|DELTA another fragment\nQEEHRSPHIV\n");

            string output = Path.Combine(workDir, "redundant_out.fasta");
            var (before, after) = RedundancyTools.RemoveRedundancy(new FileInfo(input), 100, output);

            Check("all four sequences were read", before == 4, before.ToString());
            Check("both subset sequences were dropped", after == 2, after.ToString());

            var lines = File.ReadAllLines(output);
            var heads = lines.Where(l => l.StartsWith(">")).ToList();

            Check("the survivors keep the input order, unrelated first",
                heads.Count == 2 && heads[0].Contains("GAMMA") && heads[1].Contains("ALPHA"),
                string.Join(" | ", heads));
            Check("the surviving superset says what it absorbed",
                heads[1].Contains("B1") && heads[1].Contains("D1"), heads[1]);
            Check("the sequences themselves are intact",
                lines.Contains("MKAWLLVLGL") && lines.Contains("SSQEEHRSPHIVATS"));
            Check("the input file was not touched",
                File.ReadAllText(input).Contains(">sp|D1|DELTA"));

            // A file with nothing redundant must come back whole.
            string clean = Path.Combine(workDir, "clean.fasta");
            File.WriteAllText(clean, ">A one\nAAAA\n>B two\nCCCC\n");
            var (b2, a2) = RedundancyTools.RemoveRedundancy(new FileInfo(clean),
                100, Path.Combine(workDir, "clean_out.fasta"));
            Check("a non-redundant file loses nothing", b2 == 2 && a2 == 2, $"{b2} -> {a2}");
        }

        private static void DatabaseTests(string workDir)
        {
            RealDataInputTests();
            // A repeated identifier must stop the run: it would otherwise vanish from the .fcf and its
            // peptides would never be attributed to any family.
            string duplicates = Path.Combine(workDir, "duplicate.fasta");
            File.WriteAllText(duplicates,
                ">sp|P1|A first\nMKAWLLVLGLVALSECQEEHR\n" +
                ">sp|P2|B second\nMKALWVLGLVALSECAAEEHR\n" +
                ">sp|P1|A repeated\nMKAWLLVLGLVALSECQEEHR\n");

            string message = "";
            Check("a repeated identifier is refused rather than silently dropped",
                Throws<InvalidOperationException>(() => USearchTools.GenerateFCF(new FileInfo(duplicates), 90), m => message = m),
                message);
            Check("the refusal names the offending identifier", message.Contains("sp|P1|A"), message);

            // A synthetic target/decoy database with a non-default decoy prefix.
            string tagged = Path.Combine(workDir, "custom_tag.fasta");
            File.WriteAllText(tagged, BuildTargetDecoyFasta("XXX_"));

            var fcf = USearchTools.GenerateFCF(new FileInfo(tagged), 70);
            var withCustomTag = fcf.CalculateGroupingScore("XXX_");
            var withDefaultTag = fcf.CalculateGroupingScore();

            Check("descriptions survive parsing",
                fcf.MyExtendedFastaItems.Any(i => i.Description.Contains("metalloproteinase")));
            Check("the custom decoy tag finds the decoy families", withCustomTag.DecoyOnlyGroups > 0);
            Check("the default tag finds none, so the tag genuinely matters",
                withDefaultTag.DecoyOnlyGroups == 0 && withDefaultTag.MixedGroups == 0);
            Check("both tags agree on the number of families", withCustomTag.TotalGroups == withDefaultTag.TotalGroups);

            message = "";
            Check("tuning refuses a database with no matching decoys instead of walking down to 1%",
                Throws<InvalidOperationException>(() => FamilyClusterFile.DivideAndConquer(tagged), m => message = m),
                message);
            Check("the refusal explains why", message.Contains("no decoy sequences"), message);

            double optimum = FamilyClusterFile.DivideAndConquer(tagged, 0.0, "XXX_");
            Check("with the right tag it returns an identity in range", optimum > 0 && optimum <= 100, optimum.ToString());
        }

        /// <summary>
        /// The exhaustive second pass, tested where it is decidable without USEARCH: the per-edge gate and
        /// the single-linkage union-find, on Ana's four venom pairs with their real (id, alnlen, ql, tl).
        ///
        /// The gate keeps a full-length match at or above the identity and drops a short local motif. Pair
        /// 4's DIRECT edge is below the identity (68.1 &lt; 70), exactly as in the venom database, so it can
        /// only be reached transitively: a third metalloproteinase links its two members through two
        /// full-length matches. That is the single-linkage behaviour the feature exists to produce, and it
        /// is what puts pairs 1, 3 and 4 together while keeping pair 2 apart.
        /// </summary>
        private static void ExhaustiveGateTests()
        {
            // Real values measured from usearch -allpairs_global on the venom T-R.
            var pair1 = new USearchTools.PairwiseEdge("A1", "B1", 87.5, 8, 10, 8);    // full-length
            var pair2 = new USearchTools.PairwiseEdge("A2", "B2", 90.0, 10, 10, 256); // short motif in a long protein
            var pair3 = new USearchTools.PairwiseEdge("A3", "B3", 72.5, 612, 607, 610); // full-length
            var pair4 = new USearchTools.PairwiseEdge("A4", "B4", 68.1, 486, 478, 601); // sub-threshold identity

            Check("gate keeps pair 1 (full-length, 87.5% over the whole sequence)",
                USearchTools.PassesExhaustiveGate(pair1, 70));
            Check("gate drops pair 2 (10 aligned residues against a 256-long protein is a motif)",
                !USearchTools.PassesExhaustiveGate(pair2, 70));
            Check("gate keeps pair 3 (full-length metalloproteinases, 72.5%)",
                USearchTools.PassesExhaustiveGate(pair3, 70));
            Check("gate drops pair 4's DIRECT edge, whose identity 68.1% is below 70%",
                !USearchTools.PassesExhaustiveGate(pair4, 70));

            // Nine sequences, each its own first-pass cluster. M4 is the intermediate metalloproteinase
            // that links pair 4 transitively (two full-length matches at or above 70%), reproducing what
            // the venom superfamily does in the real database.
            var edges = new List<USearchTools.PairwiseEdge>
            {
                pair1, pair2, pair3, pair4,
                new USearchTools.PairwiseEdge("A4", "M4", 75.0, 470, 478, 472),
                new USearchTools.PairwiseEdge("M4", "B4", 74.0, 470, 472, 601),
            };

            var firstPass = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["A1"] = 1, ["B1"] = 2, ["A2"] = 3, ["B2"] = 4, ["A3"] = 5,
                ["B3"] = 6, ["A4"] = 7, ["B4"] = 8, ["M4"] = 9,
            };

            var merged = USearchTools.MergeFirstPassClusters(firstPass, edges, 70);

            Check("pair 1 ends in one family", merged["A1"] == merged["B1"]);
            Check("pair 3 ends in one family", merged["A3"] == merged["B3"]);
            Check("pair 4 ends in one family, reached only through the intermediate metalloproteinase",
                merged["A4"] == merged["B4"] && merged["B4"] == merged["M4"]);
            Check("pair 2 is kept apart, its short motif never merges the two",
                merged["A2"] != merged["B2"]);
            Check("the exhaustive merge includes pairs 1, 3 and 4 and excludes 2",
                merged["A1"] == merged["B1"] && merged["A3"] == merged["B3"]
                && merged["A4"] == merged["B4"] && merged["A2"] != merged["B2"]);
            Check("a merged family keeps its smallest member id as the survivor",
                merged["A1"] == 1 && merged["A3"] == 5 && merged["A4"] == 7, merged["A4"].ToString());
        }

        /// <summary>
        /// The all-pairs alignment is memoised per FASTA: two requests for the same file align once, and a
        /// changed file (different length/last-write) is re-aligned rather than answered from a stale
        /// entry. Needs USEARCH; if it cannot run here, the section skips rather than failing.
        /// </summary>
        private static void ExhaustiveMemoTests(string workDir)
        {
            string fasta = Path.Combine(workDir, "memo_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".fasta");
            File.WriteAllText(fasta,
                ">s1\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHDQ\n" +
                ">s2\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHDE\n" +
                ">s3\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAADDQ\n" +
                ">s4\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHKQ\n");

            long before = USearchTools.PairwiseAlignmentRunCount;
            var e1 = USearchTools.GetPairwiseEdgesForTest(new FileInfo(fasta));
            var e2 = USearchTools.GetPairwiseEdgesForTest(new FileInfo(fasta));
            long after = USearchTools.PairwiseAlignmentRunCount;

            if (e1 == null)
            {
                Console.WriteLine("  SKIP  exhaustive memo - USEARCH could not run the all-pairs alignment here");
                skipped++;
                return;
            }

            Check("the same FASTA aligns exactly once across two calls", after - before == 1,
                (after - before).ToString());
            Check("both calls return the same edge set from the memo",
                e2 != null && e1.Count == e2.Count, e1.Count + " vs " + (e2?.Count.ToString() ?? "null"));

            // A different file (a fifth sequence, so both length and last-write change) must NOT answer
            // from the first file's cached edges.
            File.WriteAllText(fasta,
                ">s1\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHDQ\n" +
                ">s2\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHDE\n" +
                ">s3\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAADDQ\n" +
                ">s4\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHKQ\n" +
                ">s5\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHRQ\n");

            long beforeChange = USearchTools.PairwiseAlignmentRunCount;
            var e3 = USearchTools.GetPairwiseEdgesForTest(new FileInfo(fasta));
            Check("a changed FASTA is re-aligned, not answered from the stale memo",
                e3 != null && USearchTools.PairwiseAlignmentRunCount - beforeChange == 1,
                (USearchTools.PairwiseAlignmentRunCount - beforeChange).ToString());

            try { File.Delete(fasta); } catch { /* best-effort */ }
        }

        /// <summary>
        /// The whole two-stage clustering on the real venom T-R at 70% identity, the case Ana reported.
        /// Exhaustive TRUE closes the holes (pair 1 and pair 3, split by the fast pass, are merged) while
        /// keeping unrelated proteins apart (pair 2, and a serine protease vs a metalloproteinase).
        /// Exhaustive FALSE reproduces the old fast-only result (pair 1 and pair 3 SEPARATE, more
        /// clusters), which pins that the checkbox genuinely switches behaviour. The cluster counts are
        /// pinned; they are measured from this database and this bundled USEARCH, both deterministic.
        /// Runs only when the real-data directory is supplied as the fourth suite argument.
        /// </summary>
        private const string ExhaustiveDatabaseFileName =
            "uniprotkb_taxonomy_id_8724_2026_06_25_ORDENADO_CONT123_NO-Redundancy.T-R";

        private static string? ExhaustiveDatabasePath(string? desktopDir) =>
            string.IsNullOrWhiteSpace(desktopDir) || desktopDir == "."
                ? null : Path.Combine(desktopDir, ExhaustiveDatabaseFileName);

        private static void RealDataInputTests()
        {
            Check("real-data alignment has no implicit input for an absent root",
                new string?[] { null, "", " ", "." }.All(root => ExhaustiveDatabasePath(root) == null));
            string supplied = Path.Combine(Path.GetTempPath(), "explicit real-data input");
            Check("a supplied full-suite root still selects the same real database",
                ExhaustiveDatabasePath(supplied) == Path.Combine(supplied, ExhaustiveDatabaseFileName));
        }

        private static void ExhaustiveRealDataTests(string? desktopDir)
        {
            string? tr = ExhaustiveDatabasePath(desktopDir);

            if (tr == null)
            {
                Console.WriteLine("  SKIP  exhaustive clustering on the venom T-R - supply the real-data directory as the fourth argument");
                skipped++;
                return;
            }

            if (!File.Exists(tr))
            {
                Console.WriteLine("  SKIP  exhaustive clustering on the venom T-R - not found at " + tr);
                skipped++;
                return;
            }

            var fi = new FileInfo(tr);

            // The target family a given accession lands in, ignoring the Reverse_ decoy of the same name.
            static int FamilyOf(FamilyClusterFile f, string accession)
            {
                var item = f.MyExtendedFastaItems.First(i =>
                    !i.SequenceIdentifier.StartsWith("Reverse_", StringComparison.Ordinal)
                    && i.SequenceIdentifier.IndexOf(accession, StringComparison.Ordinal) >= 0);
                return item.UserID;
            }

            FamilyClusterFile exhaustive;
            FamilyClusterFile fastOnly;
            try
            {
                exhaustive = USearchTools.GenerateFCF(fi, 70, true);
                fastOnly = USearchTools.GenerateFCF(fi, 70, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  exhaustive clustering on the venom T-R - USEARCH failed: " + ex.Message);
                skipped++;
                return;
            }

            // Exhaustive TRUE: the holes are closed.
            Check("exhaustive merges Q10581 and Q10582 (pair 1, angiotensin fragments the fast pass split)",
                FamilyOf(exhaustive, "Q10581") == FamilyOf(exhaustive, "Q10582"));
            Check("exhaustive merges A0A8T1N158 and O93523 (pair 3, full-length metalloproteinases)",
                FamilyOf(exhaustive, "A0A8T1N158") == FamilyOf(exhaustive, "O93523"));
            Check("exhaustive keeps P01022 and Q6LEM5 apart (pair 2, only a short motif is shared)",
                FamilyOf(exhaustive, "P01022") != FamilyOf(exhaustive, "Q6LEM5"));
            Check("exhaustive keeps A0A8T1N2V9 and A0A8T1N356 together (pair 4)",
                FamilyOf(exhaustive, "A0A8T1N2V9") == FamilyOf(exhaustive, "A0A8T1N356"));
            Check("exhaustive does not fuse a serine protease with a metalloproteinase",
                FamilyOf(exhaustive, "Q5W960") != FamilyOf(exhaustive, "A0A8T1N158"));

            int exhaustiveCount = FamilyFilter.CountFamilies(exhaustive.MyExtendedFastaItems);
            // Measured on this T-R with the bundled USEARCH 11.0.667. Update WITH a re-measurement if the
            // database or the aligner ever changes.
            Check("exhaustive at 70% yields the pinned 312 families", exhaustiveCount == 312,
                exhaustiveCount.ToString());

            // Exhaustive FALSE: the old fast-only result, holes and all.
            Check("fast-only leaves Q10581 and Q10582 in separate families (pair 1 unfixed)",
                FamilyOf(fastOnly, "Q10581") != FamilyOf(fastOnly, "Q10582"));
            Check("fast-only leaves A0A8T1N158 and O93523 in separate families (pair 3 unfixed)",
                FamilyOf(fastOnly, "A0A8T1N158") != FamilyOf(fastOnly, "O93523"));

            int fastCount = FamilyFilter.CountFamilies(fastOnly.MyExtendedFastaItems);
            Check("fast-only leaves more families than exhaustive, because the holes are unclosed",
                fastCount > exhaustiveCount, fastCount + " vs " + exhaustiveCount);
            Check("fast-only at 70% yields the pinned 365 families", fastCount == 365, fastCount.ToString());
        }

        /// <summary>
        /// Three families of related sequences plus a reversed copy of each, deterministically generated.
        /// </summary>
        private static string BuildTargetDecoyFasta(string decoyTag)
        {
            string[] seeds =
            {
                "MIQVLLVTICLAVFPYQGSSIILESGNVNDYEVVYPQKVTALPKGAVQQPEQKYEDAMQYEFKVNGEPVVLHLEKNKGLF",
                "MRALLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHDQMFVEGRQFAAPDLKVIQKTFYDQLGGQ",
                "MKTLLLTLVVVTIVCLDLGYTRICFNHQSSQPQTTKTCSPGESSCYNKQWRDHRGTIIERGCGCPTVKPGIKLSCCESEV",
            };

            var sb = new StringBuilder();
            string[] descriptions = { "metalloproteinase variant", "phospholipase variant", "three finger toxin variant" };

            for (int family = 0; family < seeds.Length; family++)
            {
                for (int member = 0; member < 4; member++)
                {
                    // Deterministic single-residue drift, so members stay clearly homologous.
                    var chars = seeds[family].ToCharArray();
                    for (int m = 0; m < member; m++)
                    {
                        chars[(m * 7 + 3) % chars.Length] = "ADEKLR"[(family + m) % 6];
                    }

                    string sequence = new string(chars);
                    string id = $"sp|F{family}M{member}|SEQ";

                    sb.AppendLine($">{id} {descriptions[family]} {member}");
                    sb.AppendLine(sequence);
                    sb.AppendLine($">{decoyTag}{id}");
                    sb.AppendLine(new string(sequence.Reverse().ToArray()));
                }
            }

            return sb.ToString();
        }
    }
}
