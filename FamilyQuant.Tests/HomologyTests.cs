using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// The homology core: PepExplorer's two-stage gapless search (PL51 MAligner/Aligner.cs, driven by
    /// PepExplorer/ProgramEngine.cs:185-293) as ported into FamilyQuant.Homology.
    ///
    /// Three of these sections exist because the port fixes something. Each of those first shows the
    /// defect is real - by computing, with the module's own primitives, what the unported logic would
    /// have returned - and only then asserts the corrected answer. A test that merely asserts the new
    /// behaviour proves the code does what it does; these assert that the OLD behaviour was wrong.
    ///
    /// No file is read or written: every fixture is a handful of sequences written out here, so the
    /// section runs anywhere and its arithmetic can be checked by hand against the PAM30MS literal.
    /// </summary>
    internal static class HomologyTests
    {
        public static void Run(Action<string, bool, string?> check)
        {
            Checker c = new Checker(check);

            MatrixTests(c);
            TruncatedTailTests(c);
            UndefinedCharacterTests(c);
            EquivalenceOptionTests(c);
            SeedIndexTests(c);
            DecoyTests(c);
            DeterminismTests(c);
            FamilyMappingTests(c);
        }

        // ============================================================== matrix

        /// <summary>
        /// PAM30MS is the scientific contract of the port: every cell decides which protein a de novo
        /// tag is attributed to. These spot-checks are read off the literal in
        /// PL51/MAligner/SubstitutionMatrixProvider.cs:11-37 by eye, so a transcription slip in the
        /// copy cannot pass.
        /// </summary>
        private static void MatrixTests(Checker c)
        {
            // The parse happens once, lazily. Force it under a culture whose NegativeSign is not '-'
            // (sv-SE is U+2212 under ICU): int.Parse without InvariantCulture throws FormatException
            // on the very first cell, which is the whole of the W5 concern in this file.
            CultureInfo previous = CultureInfo.CurrentCulture;
            int underForeignCulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                underForeignCulture = PamMatrix.Score('A', 'A');
            }
            catch (Exception ex)
            {
                underForeignCulture = int.MinValue;
                c.Is("the matrix parses without throwing under a foreign culture", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }

            c.Is("the matrix parses identically under a culture whose negative sign is not '-'",
                underForeignCulture == 6, underForeignCulture.ToString(CultureInfo.InvariantCulture));

            c.Is("the alphabet is the 26 symbols of the header",
                PamMatrix.Alphabet == "ARNDCQEGHILKMFPSTWYVBZJXU*", PamMatrix.Alphabet);

            c.Is("I/L = 5 - a substitution the matrix barely penalises", PamMatrix.Score('I', 'L') == 5,
                PamMatrix.Score('I', 'L').ToString(CultureInfo.InvariantCulture));
            c.Is("L/L = 5 - leucine against itself is NOT the 8 one would expect",
                PamMatrix.Score('L', 'L') == 5, PamMatrix.Score('L', 'L').ToString(CultureInfo.InvariantCulture));
            c.Is("I/I = 8", PamMatrix.Score('I', 'I') == 8,
                PamMatrix.Score('I', 'I').ToString(CultureInfo.InvariantCulture));
            c.Is("B/R = 5 - B is repurposed as the {R,K} wildcard, not the usual N/D",
                PamMatrix.Score('B', 'R') == 5 && PamMatrix.Score('B', 'K') == 5,
                PamMatrix.Score('B', 'R') + "/" + PamMatrix.Score('B', 'K'));
            c.Is("B/N and B/D are penalties, confirming B is not the IUPAC ambiguity here",
                PamMatrix.Score('B', 'N') == -4 && PamMatrix.Score('B', 'D') == -7,
                PamMatrix.Score('B', 'N') + "/" + PamMatrix.Score('B', 'D'));
            c.Is("Z/Q = 4 - Z is the {Q,K} wildcard",
                PamMatrix.Score('Z', 'Q') == 4 && PamMatrix.Score('Z', 'K') == 4,
                PamMatrix.Score('Z', 'Q') + "/" + PamMatrix.Score('Z', 'K'));
            c.Is("the stop symbol scores -17 against a residue", PamMatrix.Score('*', 'A') == -17,
                PamMatrix.Score('*', 'A').ToString(CultureInfo.InvariantCulture));
            c.Is("and -17 in the other direction too", PamMatrix.Score('A', '*') == -17,
                PamMatrix.Score('A', '*').ToString(CultureInfo.InvariantCulture));
            c.Is("stop against stop is the lone +1", PamMatrix.Score('*', '*') == 1,
                PamMatrix.Score('*', '*').ToString(CultureInfo.InvariantCulture));
            c.Is("W/W = 13 is the largest cell - which is what makes a one-residue window dangerous",
                PamMatrix.Score('W', 'W') == 13, PamMatrix.Score('W', 'W').ToString(CultureInfo.InvariantCulture));
            c.Is("X scores 0 against every residue, which is why it is the fallback for undefined ones",
                PamMatrix.Score('X', 'A') == 0 && PamMatrix.Score('X', 'W') == 0 && PamMatrix.Score('X', 'X') == 0);

            // The source writes the matrix transposed (SubstitutionMatrixProvider.cs:106 stores
            // [column, row]); this port stores [row, column]. That is only harmless if PAM30MS really
            // is symmetric, so it is measured rather than assumed.
            bool symmetric = true;
            string asymmetry = "";

            for (int i = 0; i < PamMatrix.Alphabet.Length && symmetric; i++)
            {
                for (int j = 0; j < PamMatrix.Alphabet.Length; j++)
                {
                    if (PamMatrix.Scores[i, j] != PamMatrix.Scores[j, i])
                    {
                        symmetric = false;
                        asymmetry = PamMatrix.Alphabet[i] + "/" + PamMatrix.Alphabet[j];
                        break;
                    }
                }
            }

            c.Is("the matrix is symmetric, so the port's row/column orientation cannot change a score",
                symmetric, asymmetry);
        }

        // ================================================== W1: truncated tail

        /// <summary>
        /// W1. Align scans start offsets to protein.Length - 1 and breaks out of the inner loop when
        /// the window runs off the end (Aligner.cs:128-139), so the final offsets score TRUNCATED
        /// windows. PAM30MS off-diagonals reach -17 while its diagonal reaches +13, so a one-residue
        /// tail can out-score every honest window and win Array.IndexOf(res, max) outright - after
        /// which Aligner.cs:64 and :97 report a one-character "closest peptide in the database".
        /// </summary>
        private static void TruncatedTailTests(Checker c)
        {
            // W then seven A. The protein ends in W, so the last offset scores W/W = 13 alone.
            const string peptide = "WAAAAAAA";
            const string protein = "AAADDDDDW";

            // What the unported scan would have chosen, computed with the ported primitive itself.
            int worstFull = int.MinValue;
            int truncatedBest = int.MinValue;
            int truncatedBestStart = -1;

            for (int start = 0; start < protein.Length; start++)
            {
                int score = HomologyScorer.ScoreWindow(peptide, protein, start);

                if (start + peptide.Length <= protein.Length)
                {
                    worstFull = Math.Max(worstFull, score);
                }
                else if (score > truncatedBest)
                {
                    truncatedBest = score;
                    truncatedBestStart = start;
                }
            }

            c.Is("the defect is real: a truncated C-terminal window out-scores every full window",
                truncatedBest > worstFull,
                "truncated " + truncatedBest.ToString(CultureInfo.InvariantCulture) +
                " at offset " + truncatedBestStart.ToString(CultureInfo.InvariantCulture) +
                " vs best full " + worstFull.ToString(CultureInfo.InvariantCulture));
            c.Is("and the window it would have won with is a single residue",
                truncatedBestStart == protein.Length - 1,
                truncatedBestStart.ToString(CultureInfo.InvariantCulture));

            HomologyDatabase db = HomologyDatabase.Build(new[] { ("sp|W1|TAIL", protein) });

            // 0.25 is a deliberately loose gate: the defect is only reachable for a protein whose
            // best honest alignment is poor, and PepExplorer's PROGRAMMATIC default was 0.5
            // (ProgramArgs.cs:136) while its interface offers 0.80 (MainControl.xaml:203).
            HomologyOptions options = new HomologyOptions { MinIdentity = 0.25 };
            HomologyResult result = HomologyMapper.SearchOne(peptide, db, options);

            c.Is("the protein is still found", result.Hits.Count == 1, result.Hits.Count.ToString(CultureInfo.InvariantCulture));

            if (result.Hits.Count == 1)
            {
                HomologyHit hit = result.Hits[0];

                c.Is("the reported offset leaves room for the whole peptide",
                    hit.Offset >= 0 && hit.Offset + peptide.Length <= protein.Length,
                    "offset " + hit.Offset.ToString(CultureInfo.InvariantCulture));
                c.Is("the matched database subsequence is the peptide's length, not a 1-2 character stub",
                    hit.MatchedSubsequence.Length == peptide.Length, hit.MatchedSubsequence);
                c.Is("the matched subsequence really is what sits at that offset",
                    hit.MatchedSubsequence == protein.Substring(hit.Offset, peptide.Length), hit.MatchedSubsequence);
                c.Is("the score is the best FULL window, below the tail that would have won before",
                    hit.Score == worstFull && hit.Score < truncatedBest,
                    hit.Score.ToString(CultureInfo.InvariantCulture));
            }

            // The fix must not move a normal alignment. A tag that is present verbatim still lands on
            // it, and the protein here also ends in W so the tail window exists and must lose.
            HomologyDatabase good = HomologyDatabase.Build(new[] { ("sp|G1|GOOD", "MKAWLLPEPTIDEKGGSW") });
            HomologyResult exact = HomologyMapper.SearchOne("PEPTIDEK", good);

            c.Is("an unambiguous tag still lands where it belongs",
                exact.Hits.Count == 1 && exact.Hits[0].Offset == 6 && exact.Hits[0].MatchedSubsequence == "PEPTIDEK",
                exact.Hits.Count == 1
                    ? exact.Hits[0].Offset.ToString(CultureInfo.InvariantCulture) + " " + exact.Hits[0].MatchedSubsequence
                    : "hits " + exact.Hits.Count.ToString(CultureInfo.InvariantCulture));
            c.Is("and reports full identity for it",
                exact.Hits.Count == 1 && Math.Abs(exact.Hits[0].IdentityFraction - 1.0) < 1e-12);

            // A protein shorter than the peptide has no window at all: the source's null
            // (Aligner.cs:218-221), here simply no hit and no exception.
            HomologyDatabase tiny = HomologyDatabase.Build(new[] { ("sp|S1|SHORT", "PEPT") });
            c.Is("a protein shorter than the peptide yields no hit rather than a partial one",
                HomologyMapper.SearchOne("PEPTIDEK", tiny, new HomologyOptions { MinIdentity = 0 }).Hits.Count == 0);
        }

        // ============================================ W2: undefined characters

        /// <summary>
        /// W2. MAligner looks each residue up in a 128-entry table of -1 defaults (Aligner.cs:45-55)
        /// and indexes the matrix with the answer unguarded (Aligner.cs:141-143). A '(' from PEAKS
        /// modification notation throws IndexOutOfRangeException; a character at or above 128 reads
        /// past the end of the table itself. Either one, inside the Parallel.ForEach of
        /// ProgramEngine.cs:216, takes down the alignment of every other peptide with it.
        /// </summary>
        private static void UndefinedCharacterTests(Checker c)
        {
            HomologyDatabase db = HomologyDatabase.Build(new[] { ("sp|P1|PROT", "MKAWLLPEPTIDEKGG") });

            Exception? thrown = null;
            List<HomologyResult> results = new List<HomologyResult>();

            try
            {
                // "peptidek" and "AWLLPEPT" are distinct queries on purpose: a lowercase copy of
                // AWLLPEPT would normalise to the same sequence and be deduplicated away, which is
                // correct but would leave nothing to compare against.
                results = HomologyMapper.Search(
                    new[] { "C(+57.02)VLLHEK", "PEPTOK", "peptidek", "AWLLPEPT", "PEPTÏDEK", "" },
                    db);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            c.Is("an undefined character does not throw out of the search",
                thrown == null, thrown == null ? null : thrown.GetType().Name + ": " + thrown.Message);

            if (thrown != null)
            {
                return;
            }

            HomologyResult modification = results.First(r => r.Peptide == "C(+57.02)VLLHEK");
            c.Is("PEAKS modification notation is refused rather than scored",
                modification.IsRejected && modification.Hits.Count == 0);
            c.Is("and the refusal names the offending character",
                modification.Rejection != null && modification.Rejection.Contains("'('"), modification.Rejection);

            HomologyResult pyrrolysine = results.First(r => r.Peptide == "PEPTOK");
            c.Is("a residue with no column in the matrix is refused - O is not one of the 26",
                pyrrolysine.IsRejected && pyrrolysine.Rejection != null && pyrrolysine.Rejection.Contains("'O'"),
                pyrrolysine.Rejection);

            HomologyResult accented = results.First(r => r.Peptide == "PEPTÏDEK");
            c.Is("a character above the 128-entry lookup table is refused, not read past the table",
                accented.IsRejected && accented.Rejection != null && accented.Rejection.Contains("Ï"),
                accented.Rejection);

            HomologyResult empty = results.First(r => r.Peptide.Length == 0);
            c.Is("an empty query is refused with a reason", empty.IsRejected && empty.Rejection != null, empty.Rejection);

            // Lowercase is normalised, not refused: the fix is "uppercase, then reject what is still
            // undefined", so a tag typed in lower case searches as itself.
            HomologyResult lower = results.First(r => r.Peptide == "peptidek");
            c.Is("a lowercase query is uppercased rather than refused",
                !lower.IsRejected && lower.NormalizedPeptide == "PEPTIDEK", lower.NormalizedPeptide);
            c.Is("and finds the same protein the uppercase query does",
                lower.Hits.Count == 1 && lower.Hits[0].ProteinId == "sp|P1|PROT",
                lower.Hits.Count.ToString(CultureInfo.InvariantCulture));

            // The decisive one: the good peptides in the same batch are unaffected by the bad ones.
            HomologyResult good = results.First(r => r.Peptide == "AWLLPEPT");
            c.Is("one bad character does not cost the whole batch its results",
                good.Hits.Count == 1 && good.Hits[0].Offset == 2,
                good.Hits.Count.ToString(CultureInfo.InvariantCulture) +
                (good.Hits.Count == 1 ? " at " + good.Hits[0].Offset.ToString(CultureInfo.InvariantCulture) : ""));

            c.Is("the scorer itself never throws on an undefined residue - it scores it as X",
                PamMatrix.Score('O', 'A') == 0 && PamMatrix.Score('Ï', 'W') == 0 && PamMatrix.Score('A', 'O') == 0);

            // A database is not a query: real FASTA files carry O and other undefined residues, and a
            // database entry must not be able to kill a search.
            HomologyDatabase odd = HomologyDatabase.Build(new[] { ("sp|O1|ODD", "MKAWLLPEPTOOEKGG") });
            Exception? fromDatabase = null;

            try
            {
                HomologyMapper.SearchOne("PEPTIDEK", odd, new HomologyOptions { MinIdentity = 0.5 });
            }
            catch (Exception ex)
            {
                fromDatabase = ex;
            }

            c.Is("an undefined residue in the DATABASE is scored as X rather than throwing",
                fromDatabase == null, fromDatabase == null ? null : fromDatabase.GetType().Name);
        }

        // ================================================ I/L and K/Q identity

        /// <summary>
        /// The identity prefilter's two equivalences. I/L is ported (Aligner.cs:235) and on by
        /// default: a fragment spectrum cannot separate them. K/Q is new, off by default, and near
        /// enough in mass (0.036 Da) to be miscalled - but it is a real chemical difference, so it is
        /// a decision the caller makes.
        ///
        /// Both are prefilter-only. The last check of each pair is the one that matters: PAM30MS must
        /// score the alignment identically whether the flag is set or not.
        /// </summary>
        private static void EquivalenceOptionTests(Checker c)
        {
            HomologyDatabase il = HomologyDatabase.Build(new[] { ("sp|I1|ILE", "AAAPEPTIDEKAAA") });

            HomologyResult folded = HomologyMapper.SearchOne("PEPTLDEK", il,
                new HomologyOptions { ConsiderILSame = true });
            HomologyResult strict = HomologyMapper.SearchOne("PEPTLDEK", il,
                new HomologyOptions { ConsiderILSame = false });

            c.Is("with I/L equivalence an L-for-I mismatch is full identity",
                folded.Hits.Count == 1 && Math.Abs(folded.Hits[0].IdentityFraction - 1.0) < 1e-12,
                folded.Hits.Count == 1 ? folded.Hits[0].IdentityFraction.ToString("R", CultureInfo.InvariantCulture) : "no hit");
            c.Is("without it the same alignment is 7 of 8",
                strict.Hits.Count == 1 && Math.Abs(strict.Hits[0].IdentityFraction - 0.875) < 1e-12,
                strict.Hits.Count == 1 ? strict.Hits[0].IdentityFraction.ToString("R", CultureInfo.InvariantCulture) : "no hit");
            c.Is("the PAM30MS score is the same either way - the flag is prefilter-only",
                folded.Hits.Count == 1 && strict.Hits.Count == 1 && folded.Hits[0].Score == strict.Hits[0].Score,
                folded.Hits.Count == 1 && strict.Hits.Count == 1
                    ? folded.Hits[0].Score + " vs " + strict.Hits[0].Score : "no hit");

            // At a gate of 1.0 the flag decides whether the protein is seen at all.
            c.Is("at 100% identity the flag decides whether the protein is admitted",
                HomologyMapper.SearchOne("PEPTLDEK", il, new HomologyOptions { MinIdentity = 1.0, ConsiderILSame = true }).Hits.Count == 1 &&
                HomologyMapper.SearchOne("PEPTLDEK", il, new HomologyOptions { MinIdentity = 1.0, ConsiderILSame = false }).Hits.Count == 0);

            HomologyDatabase kq = HomologyDatabase.Build(new[] { ("sp|K1|GLN", "AAAPEPTIDEQAAA") });

            HomologyResult kqOn = HomologyMapper.SearchOne("PEPTIDEK", kq,
                new HomologyOptions { KQEquivalent = true });
            HomologyResult kqOff = HomologyMapper.SearchOne("PEPTIDEK", kq,
                new HomologyOptions { KQEquivalent = false });

            c.Is("with K/Q equivalence a K-for-Q mismatch is full identity",
                kqOn.Hits.Count == 1 && Math.Abs(kqOn.Hits[0].IdentityFraction - 1.0) < 1e-12,
                kqOn.Hits.Count == 1 ? kqOn.Hits[0].IdentityFraction.ToString("R", CultureInfo.InvariantCulture) : "no hit");
            c.Is("without it - the default - the same alignment is 7 of 8",
                kqOff.Hits.Count == 1 && Math.Abs(kqOff.Hits[0].IdentityFraction - 0.875) < 1e-12,
                kqOff.Hits.Count == 1 ? kqOff.Hits[0].IdentityFraction.ToString("R", CultureInfo.InvariantCulture) : "no hit");
            c.Is("K/Q leaves PAM30MS alone as well: the matrix still scores K against Q at -3",
                kqOn.Hits.Count == 1 && kqOff.Hits.Count == 1 && kqOn.Hits[0].Score == kqOff.Hits[0].Score &&
                PamMatrix.Score('K', 'Q') == -3,
                PamMatrix.Score('K', 'Q').ToString(CultureInfo.InvariantCulture));
            c.Is("K/Q is off unless asked for", !new HomologyOptions().KQEquivalent);
            c.Is("I/L is on, and the identity gate defaults to the interface's 0.80 rather than 0.5",
                new HomologyOptions().ConsiderILSame &&
                Math.Abs(new HomologyOptions().MinIdentity - 0.80) < 1e-12 &&
                new HomologyOptions().TopHitsPerPeptide == 3 &&
                new HomologyOptions().DecoyLabel == "Reverse_");
        }

        // =========================================================== seed index

        /// <summary>
        /// The k-mer prefilter. It is only an optimisation if it cannot change the answer, so the
        /// first check is that it does not, on a database large enough for the question to be
        /// non-trivial, with both paths forced.
        ///
        /// The rest pins the rule that decides when it may be used at all: a window passing the
        /// identity gate carries at most e mismatches, which cut it into at most e+1 runs of identity,
        /// so its longest run is at least ceil((L-e)/(e+1)). Where that reaches the seed length the
        /// index is provably lossless; where it does not, it is a heuristic, and Auto declines it.
        /// </summary>
        private static void SeedIndexTests(Checker c)
        {
            List<(string Id, string Sequence)> entries = TwentyProteins();
            HomologyDatabase db = HomologyDatabase.Build(entries);
            List<string> queries = QueriesFrom(entries);

            c.Is("the fixture is 20 proteins and 20 queries", entries.Count == 20 && queries.Count == 20,
                entries.Count.ToString(CultureInfo.InvariantCulture) + "/" + queries.Count.ToString(CultureInfo.InvariantCulture));
            c.Is("the database was indexed", db.HasSeedIndex && db.SeedCount > 0,
                db.SeedCount.ToString(CultureInfo.InvariantCulture));

            List<HomologyResult> brute = HomologyMapper.Search(queries, db,
                new HomologyOptions { Strategy = HomologySearchStrategy.BruteForce });
            List<HomologyResult> seeded = HomologyMapper.Search(queries, db,
                new HomologyOptions { Strategy = HomologySearchStrategy.SeedIndex });

            int bruteHits = brute.Sum(r => r.Hits.Count);

            c.Is("the fixture actually produces hits, so the comparison is not vacuous",
                bruteHits > 0, bruteHits.ToString(CultureInfo.InvariantCulture));
            c.Is("the seed index returns exactly the brute force hits, field for field",
                Render(seeded) == Render(brute), FirstDifference(Render(brute), Render(seeded)));
            c.Is("and it did so while looking at fewer proteins",
                seeded.Sum(r => r.CandidatesExamined) < brute.Sum(r => r.CandidatesExamined),
                seeded.Sum(r => r.CandidatesExamined).ToString(CultureInfo.InvariantCulture) + " vs " +
                brute.Sum(r => r.CandidatesExamined).ToString(CultureInfo.InvariantCulture));
            c.Is("the brute force path really did scan everything",
                brute.All(r => r.CandidatesExamined == entries.Count && !r.UsedSeedIndex));
            c.Is("and the seeded path really did use the index", seeded.All(r => r.UsedSeedIndex));

            // Auto on a 20-protein database is brute force: the index costs more to consult than the
            // scan it replaces.
            List<HomologyResult> auto = HomologyMapper.Search(queries, db, new HomologyOptions());
            c.Is("Auto scans a small database rather than indexing it", auto.All(r => !r.UsedSeedIndex));
            c.Is("and gives the same answer", Render(auto) == Render(brute));

            // The pigeonhole bound, by hand.
            c.Is("a 12-mer at 80% identity must contain a run of 4 identities",
                HomologyScorer.GuaranteedMatchRun(12, 0.80) == 4,
                HomologyScorer.GuaranteedMatchRun(12, 0.80).ToString(CultureInfo.InvariantCulture));
            c.Is("an 8-mer at 80% too", HomologyScorer.GuaranteedMatchRun(8, 0.80) == 4,
                HomologyScorer.GuaranteedMatchRun(8, 0.80).ToString(CultureInfo.InvariantCulture));
            c.Is("a 10-mer does not: the extra allowed mismatch buys the run nothing",
                HomologyScorer.GuaranteedMatchRun(10, 0.80) == 3,
                HomologyScorer.GuaranteedMatchRun(10, 0.80).ToString(CultureInfo.InvariantCulture));
            c.Is("nor a 15-mer", HomologyScorer.GuaranteedMatchRun(15, 0.80) == 3,
                HomologyScorer.GuaranteedMatchRun(15, 0.80).ToString(CultureInfo.InvariantCulture));
            c.Is("at 100% identity the whole peptide is one run",
                HomologyScorer.GuaranteedMatchRun(12, 1.0) == 12,
                HomologyScorer.GuaranteedMatchRun(12, 1.0).ToString(CultureInfo.InvariantCulture));

            // And the bound is not academic: here is a homolog that shares no 4-mer with its own
            // query. Two mismatches, three residues apart, in a 10-mer at exactly 80% identity.
            HomologyDatabase lossy = HomologyDatabase.Build(new[] { ("sp|L1|SPREAD", "CCCCAAADAAADAACCCC") });
            const string spread = "AAAGAAAGAA";

            // BruteForceProteinLimit = 0 removes the size rule, so what Auto does here is decided by
            // the guarantee alone.
            HomologyOptions autoNoSizeRule = new HomologyOptions { BruteForceProteinLimit = 0 };
            HomologyResult autoSpread = HomologyMapper.SearchOne(spread, lossy, autoNoSizeRule);
            HomologyResult forcedSpread = HomologyMapper.SearchOne(spread, lossy,
                new HomologyOptions { Strategy = HomologySearchStrategy.SeedIndex });

            c.Is("the forced index loses a real 80%-identity homolog - the loss is not hypothetical",
                forcedSpread.UsedSeedIndex && forcedSpread.Hits.Count == 0,
                forcedSpread.Hits.Count.ToString(CultureInfo.InvariantCulture));
            c.Is("Auto declines the index for that query and finds the homolog",
                !autoSpread.UsedSeedIndex && autoSpread.Hits.Count == 1 && autoSpread.Hits[0].Offset == 4,
                autoSpread.Hits.Count.ToString(CultureInfo.InvariantCulture));
            c.Is("while for a length where the bound holds, Auto does use the index",
                HomologyMapper.ShouldUseSeedIndex("AAAGAAAGAAAG", db, autoNoSizeRule));
            c.Is("Auto also declines the index when K/Q equivalence is on: the seeds fold I into L, "
                 + "which the gate does too, but they do not fold K into Q",
                !HomologyMapper.ShouldUseSeedIndex("AAAGAAAGAAAG", db,
                    new HomologyOptions { BruteForceProteinLimit = 0, KQEquivalent = true }));

            // A database built without an index still searches; forcing the index on it is refused.
            HomologyDatabase unindexed = HomologyDatabase.Build(entries, HomologyOptions.DefaultSeedLength, false);
            c.Is("a database with no index still gives the brute force answer",
                Render(HomologyMapper.Search(queries, unindexed, new HomologyOptions())) == Render(brute));
            c.Is("but forcing the index on it is refused rather than silently ignored",
                Throws<InvalidOperationException>(() => HomologyMapper.Search(queries, unindexed,
                    new HomologyOptions { Strategy = HomologySearchStrategy.SeedIndex })));
        }

        // ================================================================ decoys

        /// <summary>
        /// Best-score target-decoy competition. A target must beat the best decoy strictly; a tie goes
        /// to the decoy conservatively. A peptide that matched only decoys keeps them.
        /// </summary>
        private static void DecoyTests(Checker c)
        {
            HomologyDatabase db = HomologyDatabase.Build(new[]
            {
                ("sp|T1|TARGET", "AAAPEPTIDEKAAA"),
                ("Reverse_sp|T1|TARGET", "GGGPEPTIDEKGGG"),
                ("Reverse_sp|T2|TARGET", "GGGWQSMYHFCGGG"),
            });

            HomologyResult mixed = HomologyMapper.SearchOne("PEPTIDEK", db);

            c.Is("an exact target-decoy tie goes to the decoy conservatively",
                mixed.Hits.Count == 1 && mixed.Hits[0].IsDecoy
                && mixed.TargetsSuppressedByDecoyCompetition && !mixed.DecoysSuppressed,
                string.Join(", ", mixed.Hits.Select(h => h.ProteinId)));

            HomologyResult decoyOnly = HomologyMapper.SearchOne("WQSMYHFC", db);

            c.Is("a peptide matching only decoys keeps them - that is what decoys are for",
                decoyOnly.Hits.Count == 1 && decoyOnly.Hits[0].IsDecoy && !decoyOnly.DecoysSuppressed,
                string.Join(", ", decoyOnly.Hits.Select(h => h.ProteinId)));

            // The label is a parameter and is compared Ordinal, so case is not negotiable: a protein
            // named "reverse_..." is a target as far as this module is concerned.
            HomologyDatabase cased = HomologyDatabase.Build(new[]
            {
                ("sp|T1|TARGET", "AAAPEPTIDEKAAA"),
                ("reverse_sp|T1|TARGET", "GGGPEPTIDEKGGG"),
            });

            HomologyResult ordinal = HomologyMapper.SearchOne("PEPTIDEK", cased);

            c.Is("the decoy label is matched Ordinal, so a lowercase prefix is not a decoy",
                ordinal.Hits.Count == 2 && !ordinal.DecoysSuppressed,
                string.Join(", ", ordinal.Hits.Select(h => h.ProteinId)));

            HomologyResult customLabel = HomologyMapper.SearchOne("PEPTIDEK", cased,
                new HomologyOptions { DecoyLabel = "reverse_" });

            c.Is("and the label really is a parameter (the newly marked decoy wins the tie)",
                customLabel.Hits.Count == 1 && customLabel.Hits[0].ProteinId == "reverse_sp|T1|TARGET"
                && customLabel.TargetsSuppressedByDecoyCompetition,
                string.Join(", ", customLabel.Hits.Select(h => h.ProteinId)));
        }

        // ========================================================== determinism

        /// <summary>
        /// The source collects into a ConcurrentBag (ProgramEngine.cs:213) and reports whatever order
        /// the threads produced. Here the parallelism is over queries, each writing its own slot, and
        /// hits are ordered by score then identifier - so the same input gives the same bytes, which
        /// is what makes a re-run comparable to a previous one.
        /// </summary>
        private static void DeterminismTests(Checker c)
        {
            List<(string Id, string Sequence)> entries = TwentyProteins();
            HomologyDatabase db = HomologyDatabase.Build(entries);

            // Enough queries to guarantee the work is spread over several tasks.
            List<string> queries = new List<string>();
            List<string> fromFixture = QueriesFrom(entries);

            for (int i = 0; i < 4; i++)
            {
                foreach (string q in fromFixture)
                {
                    char[] chars = q.ToCharArray();
                    chars[(i * 5) % chars.Length] = "ADEKLRSTVG"[(i * 3) % 10];
                    queries.Add(new string(chars));
                }
            }

            string first = Render(HomologyMapper.Search(queries, db, new HomologyOptions()));
            string second = Render(HomologyMapper.Search(queries, db, new HomologyOptions()));

            c.Is("two runs of the same search are identical", first == second, FirstDifference(first, second));

            // Ties are the interesting case: two proteins with the same sequence score the same, so
            // only the ordinal rule decides. The database lists them in the wrong order on purpose.
            HomologyDatabase ties = HomologyDatabase.Build(new[]
            {
                ("sp|ZZ|SAME", "AAAPEPTIDEKAAA"),
                ("sp|AA|SAME", "AAAPEPTIDEKAAA"),
                ("sp|MM|SAME", "AAAPEPTIDEKAAA"),
            });

            HomologyResult tied = HomologyMapper.SearchOne("PEPTIDEK", ties);

            c.Is("equal scores are broken by identifier, ordinal ascending, not by database order",
                string.Join(",", tied.Hits.Select(h => h.ProteinId)) == "sp|AA|SAME,sp|MM|SAME,sp|ZZ|SAME",
                string.Join(",", tied.Hits.Select(h => h.ProteinId)));

            // Duplicate queries collapse the way ProgramEngine.cs:201-203 collapses registries.
            List<HomologyResult> deduplicated = HomologyMapper.Search(
                new[] { "PEPTIDEK", "peptidek", "PEPTIDEK ", "OTHERPEP" }, ties);

            c.Is("queries that normalise to the same sequence are searched once",
                deduplicated.Count == 2, deduplicated.Count.ToString(CultureInfo.InvariantCulture));

            HomologyResult top = HomologyMapper.SearchOne("PEPTIDEK", ties,
                new HomologyOptions { TopHitsPerPeptide = 2 });

            c.Is("TopHitsPerPeptide truncates after the ordering, keeping the best",
                top.Hits.Count == 2 && top.Hits[0].ProteinId == "sp|AA|SAME",
                string.Join(",", top.Hits.Select(h => h.ProteinId)));

            c.Is("hits arrive by descending score",
                tied.Hits.Select(h => h.Score).SequenceEqual(tied.Hits.Select(h => h.Score).OrderByDescending(s => s)));
        }

        // ============================================================= families

        /// <summary>
        /// The grouping is UserID, the curated family id, never USearchID. The fixture makes them
        /// disagree exactly as a manual split does: three proteins that USEARCH put in one family, two
        /// of which the curator has moved out. Reading USearchID here would report one family and undo
        /// the split - the same regression CLAUDE.md records for the word reclustering.
        /// </summary>
        private static void FamilyMappingTests(Checker c)
        {
            List<FastaItemExtended> family = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|A1|TOXIN", Sequence = "AAAPEPTIDEKAAA",
                    Description = "Phospholipase A2", USearchID = 2, UserID = 2 },
                new FastaItemExtended { SequenceIdentifier = "sp|B1|INHIB", Sequence = "GGGPEPTIDEKGGG",
                    Description = "Phospholipase A2 inhibitor", USearchID = 2, UserID = 500 },
                new FastaItemExtended { SequenceIdentifier = "sp|C1|INHIB", Sequence = "SSSPEPTIDEKSSS",
                    Description = "Phospholipase A2 inhibitor", USearchID = 2, UserID = 500 },
            };

            HomologyDatabase db = HomologyDatabase.Build(family);
            List<HomologyResult> results = HomologyMapper.Search(new[] { "PEPTIDEK" }, db);
            List<FamilyAssignment> assignments = HomologyMapper.MapToFamilies(results, family);

            c.Is("every protein is hit", results[0].Hits.Count == 3,
                results[0].Hits.Count.ToString(CultureInfo.InvariantCulture));
            c.Is("one assignment per query", assignments.Count == 1,
                assignments.Count.ToString(CultureInfo.InvariantCulture));
            c.Is("the peptide is attributed to the CURATED families, ascending",
                string.Join(",", assignments[0].FamilyIds) == "2,500",
                string.Join(",", assignments[0].FamilyIds));
            c.Is("which is two families, not the one USearchID would have reported",
                assignments[0].FamilyIds.Count == 2 && family.Select(f => f.USearchID).Distinct().Count() == 1);
            c.Is("a peptide spanning two families is flagged ambiguous", assignments[0].IsAmbiguous);
            c.Is("the contributing proteins travel with the assignment",
                string.Join(",", assignments[0].ProteinIds) == "sp|A1|TOXIN,sp|B1|INHIB,sp|C1|INHIB",
                string.Join(",", assignments[0].ProteinIds));

            // Searched against one database, mapped against another: reported, not silently dropped.
            List<FastaItemExtended> partial = family.Take(1).ToList();
            List<FamilyAssignment> mismatched = HomologyMapper.MapToFamilies(results, partial);

            c.Is("a hit protein the family file does not know is reported as unknown",
                string.Join(",", mismatched[0].UnknownProteinIds) == "sp|B1|INHIB,sp|C1|INHIB",
                string.Join(",", mismatched[0].UnknownProteinIds));
            c.Is("and does not contribute a family", string.Join(",", mismatched[0].FamilyIds) == "2",
                string.Join(",", mismatched[0].FamilyIds));

            // A single-family peptide is the unambiguous case, and a rejected query still gets a row.
            List<HomologyResult> mixedQueries = HomologyMapper.Search(new[] { "AAAPEPT", "C(+57)K" }, db,
                new HomologyOptions { MinIdentity = 1.0 });
            List<FamilyAssignment> mixedAssignments = HomologyMapper.MapToFamilies(mixedQueries, family);

            c.Is("a tag unique to one protein maps to one family",
                mixedAssignments[0].FamilyIds.Count == 1 && mixedAssignments[0].FamilyIds[0] == 2 &&
                !mixedAssignments[0].IsAmbiguous,
                string.Join(",", mixedAssignments[0].FamilyIds));
            c.Is("a rejected query still gets a row, with no families",
                mixedAssignments.Count == 2 && mixedAssignments[1].FamilyIds.Count == 0 &&
                mixedAssignments[1].Peptide == "C(+57)K",
                mixedAssignments[1].Peptide);
        }

        // ============================================================== helpers

        /// <summary>Twenty proteins in four families of five, each family drifting by single residues
        /// from one seed, so homology is real and graded rather than exact.</summary>
        private static List<(string Id, string Sequence)> TwentyProteins()
        {
            string[] seeds =
            {
                "MIQVLLVTICLAVFPYQGSSIILESGNVNDYEVVYPQKVTALPKGAVQQPEQKYEDAMQYEFKVNGEPVVLHLEKNKGLF",
                "MRALLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHDQMFVEGRQFAAPDLKVIQKTFYDQLGGQ",
                "MKTLLLTLVVVTIVCLDLGYTRICFNHQSSQPQTTKTCSPGESSCYNKQWRDHRGTIIERGCGCPTVKPGIKLSCCESEV",
                "MNSAHLLVLLTCLSVSAFAQKSSELVIPKHLQMGSDSHVHNRIVSNQEWQTLLQGKHMYVFEDQEGHRWNIYADSKPTLE",
            };

            List<(string Id, string Sequence)> database = new List<(string, string)>();

            for (int f = 0; f < seeds.Length; f++)
            {
                for (int m = 0; m < 5; m++)
                {
                    char[] chars = seeds[f].ToCharArray();

                    for (int k = 0; k < m; k++)
                    {
                        chars[((k * 11) + 5) % chars.Length] = "ADEKLRSTVG"[(f + k) % 10];
                    }

                    database.Add(("sp|F" + f.ToString(CultureInfo.InvariantCulture) +
                        "M" + m.ToString(CultureInfo.InvariantCulture) + "|SEQ", new string(chars)));
                }
            }

            return database;
        }

        /// <summary>One 12-residue tag per protein, each carrying a single sequencing error, which is
        /// the shape a de novo tag arrives in.</summary>
        private static List<string> QueriesFrom(List<(string Id, string Sequence)> database)
        {
            List<string> queries = new List<string>();

            for (int i = 0; i < database.Count; i++)
            {
                string sequence = database[i].Sequence;
                int start = (i * 7) % (sequence.Length - 12);
                char[] tag = sequence.Substring(start, 12).ToCharArray();
                tag[(i * 3) % 12] = "ADEKLRSTVG"[i % 10];
                queries.Add(new string(tag));
            }

            return queries;
        }

        /// <summary>
        /// A search rendered as text, for comparing two runs. Deliberately excludes
        /// CandidatesExamined and UsedSeedIndex: those are how the answer was reached, and the whole
        /// point of the index is that they may differ while nothing else does.
        /// </summary>
        private static string Render(IEnumerable<HomologyResult> results)
        {
            StringBuilder sb = new StringBuilder();

            foreach (HomologyResult r in results)
            {
                sb.Append(r.NormalizedPeptide).Append('|')
                  .Append(r.Rejection ?? "-").Append('|')
                  .Append(r.DecoysSuppressed ? '1' : '0').Append(" >");

                foreach (HomologyHit h in r.Hits)
                {
                    sb.Append(' ').Append(h.ProteinId).Append(',')
                      .Append(h.Offset.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(h.Score.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(h.IdentityFraction.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                      .Append(h.GateIdentityFraction.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                      .Append(h.MatchedSubsequence).Append(',')
                      .Append(h.IsDecoy ? '1' : '0');
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>The first line where two renderings differ, so a failure says what changed.</summary>
        private static string FirstDifference(string a, string b)
        {
            if (a == b)
            {
                return "identical";
            }

            string[] left = a.Split('\n');
            string[] right = b.Split('\n');

            for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                string l = i < left.Length ? left[i] : "<missing>";
                string r = i < right.Length ? right[i] : "<missing>";

                if (l != r)
                {
                    return "line " + i.ToString(CultureInfo.InvariantCulture) + ": '" + l + "' vs '" + r + "'";
                }
            }

            return "lengths differ";
        }

        private static bool Throws<T>(Action action) where T : Exception => Program.Throws<T>(action);

        /// <summary>Wraps the runner's Check so the assertions below read as assertions. The runner
        /// owns all output; nothing here writes to the console.</summary>
        private sealed class Checker
        {
            private readonly Action<string, bool, string?> check;

            internal Checker(Action<string, bool, string?> check)
            {
                this.check = check;
            }

            internal void Is(string name, bool condition, string? detail = null)
            {
                check(name, condition, detail);
            }
        }
    }
}
