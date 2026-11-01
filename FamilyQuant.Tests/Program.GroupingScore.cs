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

        // ============================================================== scores

        private static void ScoreTests()
        {
            var fcf = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "A", USearchID = 0 , UserID = 0},           // target only
                    new FastaItemExtended { SequenceIdentifier = "B", USearchID = 0 , UserID = 0},
                    new FastaItemExtended { SequenceIdentifier = "Reverse_A", USearchID = 1 , UserID = 1},   // decoy only
                    new FastaItemExtended { SequenceIdentifier = "C", USearchID = 2 , UserID = 2},           // mixed
                    new FastaItemExtended { SequenceIdentifier = "Reverse_C", USearchID = 2 , UserID = 2},
                }
            };

            var score = fcf.CalculateGroupingScore();

            Check("total families", score.TotalGroups == 3);
            Check("target-only families", score.TargetOnlyGroups == 1);
            Check("decoy-only families", score.DecoyOnlyGroups == 1);
            Check("mixed families", score.MixedGroups == 1);
            Check("score over all families is 1/3", Math.Abs(score.Score - 1.0 / 3.0) < 1e-12);
            Check("score over target-bearing families is 1/2 - the ~2x the plain score hides",
                Math.Abs(score.TargetRelativeScore - 0.5) < 1e-12);
            Check("CalculateFDR keeps returning the all-families ratio",
                Math.Abs(fcf.CalculateFDR() - 1.0 / 3.0) < 1e-12);

            var empty = new FamilyClusterFile().CalculateGroupingScore();
            Check("an empty file does not divide by zero", empty.Score == 0 && empty.TargetRelativeScore == 0);
        }

        /// <summary>
        /// The reported case: with the exhaustive pass, Divide and Conquer on the Bjararaca database
        /// answered 73% where the fast clustering had answered 24%, and the number alone could not say
        /// why. One family set it: bradykinin-potentiating peptide 11d (QGRPPGPPIPP, 11 residues), nearly
        /// a palindrome, aligns to its own reversed decoy at 75% identity over 8 of 11 residues. The score,
        /// the scan report and Divide and Conquer now name the families that mix.
        /// </summary>
        private static void MixedFamilyTests(string workDir)
        {
            var fcf = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|P85161|BPPBD_BOTJA", UserID = 4,
                        Description = "Bradykinin-potentiating peptide 11d OS=Bothrops jararaca OX=8724",
                        Sequence = "QGRPPGPPIPP" },
                    new FastaItemExtended { SequenceIdentifier = "Reverse_sp|P85161|BPPBD_BOTJA", UserID = 4,
                        Sequence = "PPIPPGPPRGQ" },
                    new FastaItemExtended { SequenceIdentifier = "sp|BIG1|X", UserID = 7, Description = "Big one",
                        Sequence = new string('A', 300) },
                    new FastaItemExtended { SequenceIdentifier = "sp|BIG2|X", UserID = 7, Description = "Big two",
                        Sequence = new string('A', 280) },
                    new FastaItemExtended { SequenceIdentifier = "Reverse_sp|BIG1|X", UserID = 7,
                        Sequence = new string('A', 300) },
                    new FastaItemExtended { SequenceIdentifier = "sp|CLEAN|X", UserID = 9, Sequence = "MKV" },
                }
            };

            GroupingScore score = fcf.CalculateGroupingScore();
            Check("the score keeps one entry per mixed family", score.MixedFamilies.Count == score.MixedGroups
                && score.MixedGroups == 2, score.MixedFamilies.Count + " vs " + score.MixedGroups);
            Check("mixed families are ordered smallest first, so a lone short peptide leads",
                score.MixedFamilies[0].FamilyId == 4 && score.MixedFamilies[1].FamilyId == 7,
                string.Join(",", score.MixedFamilies.Select(f => f.FamilyId)));
            string bpp = score.MixedFamilies[0].Label;
            Check("a family label names the target, its description without OS=, and its length",
                bpp == "1 target(s) + 1 decoy(s): sp|P85161|BPPBD_BOTJA Bradykinin-potentiating peptide 11d (11 aa)", bpp);
            Check("targets are listed shortest first and decoys are counted, not named in the label",
                score.MixedFamilies[1].Label.StartsWith("2 target(s) + 1 decoy(s): sp|BIG2|X Big two (280 aa); sp|BIG1|X",
                    StringComparison.Ordinal), score.MixedFamilies[1].Label);
            Check("a long list of mixed families is cut and the rest are counted",
                score.DescribeMixedFamilies(1).Count == 2 && score.DescribeMixedFamilies(1)[1] == "... and 1 more mixed family",
                string.Join(" | ", score.DescribeMixedFamilies(1)));

            var rows = new List<IdentityScanRow>
            {
                new IdentityScanRow { Identity = 75, MixedGroups = 0 },
                new IdentityScanRow { Identity = 70, MixedGroups = 1, MixedFamilies = score.MixedFamilies.Take(1).ToList() },
                new IdentityScanRow { Identity = 65, MixedGroups = 2, MixedFamilies = score.MixedFamilies },
            };
            List<string> section = IdentityScanRow.MixedFamiliesSection(rows, perIdentity: 1);
            Check("the scan report names the mixed families at each identity, strictest first",
                section.Count == 3 && section[0].StartsWith(" 70%  1 target(s)", StringComparison.Ordinal)
                && section[1].StartsWith(" 65%  1 target(s)", StringComparison.Ordinal)
                && section[2] == "      ... and 1 more mixed family", string.Join(" | ", section));

            var stopped = new DivideAndConquerResult { Identity = 73, IdentityBelow = 72, ScoreBelow = score };
            string reason = stopped.StopReason();
            Check("Divide and Conquer says why it stopped, naming the families just below the answer",
                reason.StartsWith("It stopped here because at 72% 2 families mix", StringComparison.Ordinal)
                && reason.Contains("BPPBD_BOTJA Bradykinin-potentiating peptide 11d (11 aa)", StringComparison.Ordinal), reason);
            Check("with nothing probed below the answer there is no reason to give",
                new DivideAndConquerResult { Identity = 1 }.StopReason().Length == 0);

            // End to end with USEARCH: the BPP and its decoy, plus two homologous long proteins and their
            // decoys so the database has target-only and decoy-only families as well.
            const string longA = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHDQ";
            const string longB = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHDE";
            static string Rev(string s) { char[] c = s.ToCharArray(); Array.Reverse(c); return new string(c); }
            string fasta = Path.Combine(workDir, "bpp_floor_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".fasta");
            File.WriteAllText(fasta,
                ">sp|P85161|BPPBD_BOTJA Bradykinin-potentiating peptide 11d\nQGRPPGPPIPP\n" +
                ">Reverse_sp|P85161|BPPBD_BOTJA\nPPIPPGPPRGQ\n" +
                ">sp|L1|A Long one\n" + longA + "\n>sp|L2|B Long two\n" + longB + "\n" +
                ">Reverse_sp|L1|A\n" + Rev(longA) + "\n>Reverse_sp|L2|B\n" + Rev(longB) + "\n");

            DivideAndConquerResult outcome;
            try
            {
                outcome = FamilyClusterFile.DivideAndConquerDetailed(fasta, 0.0, "Reverse_", exhaustive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  Divide and Conquer end to end - USEARCH could not run here (" + ex.Message + ")");
                skipped++;
                return;
            }

            Check("on the BPP fixture the exhaustive search stops at 73%, as on the full database",
                outcome.Identity == 73, outcome.Identity.ToString(CultureInfo.InvariantCulture));
            Check("and reports the identity just below it with the BPP family as the one that mixes",
                outcome.IdentityBelow == 72 && outcome.ScoreBelow != null
                && outcome.ScoreBelow.MixedFamilies.Count == 1
                && outcome.ScoreBelow.MixedFamilies[0].Targets.Single().StartsWith("sp|P85161|BPPBD_BOTJA", StringComparison.Ordinal),
                outcome.StopReason());
        }
    }
}
