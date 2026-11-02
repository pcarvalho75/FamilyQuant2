using System;
using FamilyQuant.Operations;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant
{
    internal static class FamilyClusterScoring
    {
        /// <summary>
        /// Splits the current families into target-only, decoy-only and mixed.
        /// </summary>
        public static GroupingScore CalculateGroupingScore(FamilyClusterFile document, string decoyTag = SequenceLabels.DecoyPrefix)
        {
            var score = new GroupingScore();

            foreach (var group in document.GroupedFastaItems)
            {
                int decoys = group.Count(a => (a.SequenceIdentifier ?? string.Empty).StartsWith(decoyTag));

                score.TotalGroups++;

                if (decoys == 0)
                    score.TargetOnlyGroups++;
                else if (decoys == group.Count())
                    score.DecoyOnlyGroups++;
                else
                {
                    score.MixedGroups++;
                    score.MixedFamilies.Add(MixedFamily.From(group.Key, group, decoyTag));
                }
            }

            // Smallest first: the family that holds a floor up on its own is usually one short peptide.
            score.MixedFamilies = score.MixedFamilies
                .OrderBy(f => f.Targets.Count + f.Decoys.Count)
                .ThenBy(f => f.Targets.FirstOrDefault() ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            return score;
        }

        /// <summary>
        /// Fraction of families that mix target and decoy sequences.
        ///
        /// This is NOT a statistical FDR and it does NOT measure over-merging. A reversed database
        /// reproduces the target database's internal redundancy - if A and B are similar then Reverse_A
        /// and Reverse_B are similar too - so decoy families collapse at exactly the same rate as target
        /// families and putting two decoys together is not an error. The only event this can see is a
        /// target aligning to a decoy, which marks the alignment twilight zone: the identity below which
        /// alignments are no better than noise. Fusions of two unrelated TARGET families are invisible
        /// to it. Treat it as a floor ("do not cluster below this identity"), never as a budget to spend.
        /// </summary>
        public static double CalculateFDR(FamilyClusterFile document, string decoyTag = SequenceLabels.DecoyPrefix)
        {
            GroupingScore score = CalculateGroupingScore(document, decoyTag);

            Console.WriteLine("Total groups = {0}, Incorrect Grouping = {1}", score.TotalGroups, score.MixedGroups);

            return score.Score;
        }

        /// <summary>
        /// Clusters the database across a range of identities and reports the groups and the mixing at
        /// each one, so a threshold can be chosen from the whole curve instead of a single optimiser
        /// answer. Runs USEARCH once per step, from the strictest identity down to the loosest.
        /// </summary>
        public static List<IdentityScanRow> ScanIdentities(string fileName, int from = 95, int to = 10, int step = 5, string decoyTag = SequenceLabels.DecoyPrefix, bool exhaustive = true, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Step must be positive.");
            if (from < to) throw new ArgumentException("'from' must be greater than or equal to 'to'.", nameof(from));

            // USEARCH takes an identity in (0, 100]; -id 0 is not a clustering, it is an error.
            if (from > 100) throw new ArgumentOutOfRangeException(nameof(from), "Identity cannot exceed 100%.");
            if (to < 1) throw new ArgumentOutOfRangeException(nameof(to), "Identity must be at least 1%.");

            var fi = new FileInfo(fileName);
            var rows = new List<IdentityScanRow>();

            for (int identity = from; identity >= to; identity -= step)
            {
                operation?.Report("identity-scan", "Clustering at " + identity + "% identity");
                FamilyClusterFile clustered = USearchTools.GenerateFCF(fi, identity, exhaustive, operation);
                GroupingScore score = clustered.CalculateGroupingScore(decoyTag);
                int nonContaminantTargets = clustered.GroupedFastaItems.Count(g => g.Any(i =>
                    !(i.SequenceIdentifier ?? string.Empty).StartsWith(decoyTag, StringComparison.Ordinal)
                    && !SequenceLabels.IsContaminant(i.SequenceIdentifier)));

                rows.Add(new IdentityScanRow()
                {
                    Identity = identity,
                    Groups = score.TotalGroups,
                    TargetGroups = score.TargetOnlyGroups + score.MixedGroups,
                    NonContaminantTargetGroups = nonContaminantTargets,
                    MixedGroups = score.MixedGroups,
                    Score = score.Score,
                    TargetRelativeScore = score.TargetRelativeScore,
                    MixedFamilies = score.MixedFamilies
                });

                Console.WriteLine("Identity {0}% -> {1} groups, {2} mixed", identity, score.TotalGroups, score.MixedGroups);
            }

            return rows;
        }

        /// <summary>
        /// Finds the LOWEST USEARCH sequence-identity threshold (0-100) whose family grouping keeps the
        /// target/decoy mixing score at or below <paramref name="acceptableScore"/>. Lower identity
        /// yields fewer, larger families but more mixing, so the constraint holds over an upper interval
        /// of identities and we binary-search for its lower edge (one USEARCH run per probe).
        ///
        /// The default of 0 stops at the last identity showing NO target/decoy mixing at all, i.e. just
        /// above the alignment twilight zone. Any budget above 0 walks the search straight INTO that
        /// zone: the score only leaves zero once alignments stop being better than noise, so a non-zero
        /// allowance is spent buying biologically meaningless merges. See <see cref="CalculateFDR"/> for
        /// why this score cannot see the merges that actually matter, and use <see cref="ScanIdentities"/>
        /// to inspect the whole curve before committing to a threshold.
        ///
        /// With <paramref name="acceptableScore"/> above 0 the answer is a heuristic: the score is a
        /// RATIO, and lowering the identity can merge two already-mixed families into one, dropping
        /// numerator and denominator together so the score falls (2/100 -> 1/99). The satisfying set is
        /// then not a single upper interval and the bisection can settle on either edge. At the default
        /// of 0 the predicate reduces to "no mixed family at all", which has no such hole.
        /// </summary>
        public static double DivideAndConquer(string fileName, double acceptableScore = 0.0, string decoyTag = SequenceLabels.DecoyPrefix, bool exhaustive = true, OperationContext? operation = null)
        {
            return DivideAndConquerDetailed(fileName, acceptableScore, decoyTag, exhaustive, operation).Identity;
        }

        /// <summary>
        /// <see cref="DivideAndConquer"/>, also returning the score of the identity just below the answer:
        /// the families that mix there are WHY the search stopped where it did. One short peptide matching
        /// its own reversed decoy can set the floor on its own, and a bare number cannot show that.
        /// </summary>
        public static DivideAndConquerResult DivideAndConquerDetailed(string fileName, double acceptableScore = 0.0, string decoyTag = SequenceLabels.DecoyPrefix, bool exhaustive = true, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            var fi = new FileInfo(fileName);

            int lo = 0;     // assumed to FAIL the constraint (loosest clustering)
            int hi = 100;   // strictest clustering, expected to SATISFY the constraint

            // Every failing probe, so the one at the final lo (always hi - 1, unless lo never moved off
            // the unprobed 0) can be handed back without another USEARCH run.
            var failed = new Dictionary<int, GroupingScore>();

            GroupingScore strictest = Probe(fi, hi, decoyTag, exhaustive, operation);

            // A database with no decoys scores 0 at EVERY identity, so the search would walk all the way
            // down to 1% and report it as "no mixing found". That is a missing measurement, not a result.
            if (strictest.DecoyOnlyGroups + strictest.MixedGroups == 0)
            {
                throw new InvalidOperationException(
                    "This database contains no decoy sequences (identifiers starting with \"" + decoyTag +
                    "\"), so the target/decoy score is 0 at every identity and cannot single one out.\n\n" +
                    "Use a target-decoy database (.T-R), or choose the threshold from 'Scan identity range' " +
                    "and the biology of your proteins.");
            }

            // If even the strictest clustering can't meet the target, nothing will.
            if (strictest.Score > acceptableScore)
            {
                throw new InvalidOperationException(
                    "Clustering at 100% identity already mixes target and decoy sequences (score " +
                    strictest.Score.ToString("P2") + "), so no identity threshold can reach the requested " +
                    "limit of " + acceptableScore.ToString("P2") + ". Check that the decoy prefix is \"" +
                    decoyTag + "\" and that the database is not duplicated.");
            }

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                GroupingScore probe = Probe(fi, mid, decoyTag, exhaustive, operation);

                if (probe.Score <= acceptableScore)
                    hi = mid;   // satisfied: try to go lower (fewer groups)
                else
                {
                    lo = mid;   // failed: need stricter clustering
                    failed[mid] = probe;
                }
            }

            Console.WriteLine("Optimum identity: {0}% (lowest identity with score <= {1:P2})", hi, acceptableScore);

            failed.TryGetValue(lo, out GroupingScore? below);
            return new DivideAndConquerResult
            {
                Identity = hi,
                IdentityBelow = below != null ? lo : (int?)null,
                ScoreBelow = below,
            };
        }

        private static GroupingScore Probe(FileInfo fi, int identity, string decoyTag, bool exhaustive = true, OperationContext? operation = null)
        {
            operation?.Report("identity-search", "Clustering at " + identity + "% identity");
            GroupingScore score = USearchTools.GenerateFCF(fi, identity, exhaustive, operation).CalculateGroupingScore(decoyTag);

            Console.WriteLine(
                "Identity {0}% -> {1} groups ({2} with targets, {3} mixed), score {4:P2} (of target families {5:P2})",
                identity, score.TotalGroups, score.TargetOnlyGroups + score.MixedGroups, score.MixedGroups,
                score.Score, score.TargetRelativeScore);

            return score;
        }
    }
}
