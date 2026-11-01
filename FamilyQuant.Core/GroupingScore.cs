using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>
    /// Breakdown of how a clustering distributes target and decoy sequences across families.
    /// </summary>
    public class GroupingScore
    {
        public int TotalGroups { get; set; }
        public int TargetOnlyGroups { get; set; }
        public int DecoyOnlyGroups { get; set; }
        public int MixedGroups { get; set; }

        /// <summary>
        /// The families behind <see cref="MixedGroups"/>, so a report can name WHICH family set the floor
        /// and not only how many did. With the exhaustive pass a single short, near-palindromic peptide
        /// aligning to its own reversed decoy can hold the floor high on its own, and only its name tells
        /// that apart from genuine noise.
        /// </summary>
        public List<MixedFamily> MixedFamilies { get; set; } = new List<MixedFamily>();

        /// <summary>
        /// Mixed families over ALL families. Pure-decoy families inflate the denominator (a target-decoy
        /// database has roughly one decoy family per target family), so this reads about half of
        /// <see cref="TargetRelativeScore"/>.
        /// </summary>
        public double Score
        {
            get { return TotalGroups == 0 ? 0 : (double)MixedGroups / TotalGroups; }
        }

        /// <summary>
        /// Mixed families over the families that actually contain target sequences - the rate a user
        /// means when asking "how many of my families are contaminated?".
        /// </summary>
        public double TargetRelativeScore
        {
            get
            {
                int denominator = TargetOnlyGroups + MixedGroups;
                return denominator == 0 ? 0 : (double)MixedGroups / denominator;
            }
        }

        /// <summary>One line per mixed family, smallest first, at most <paramref name="maxFamilies"/>
        /// lines plus a count of the rest.</summary>
        public List<string> DescribeMixedFamilies(int maxFamilies = 5)
        {
            var lines = MixedFamilies.Take(maxFamilies).Select(f => f.Label).ToList();
            if (MixedFamilies.Count > maxFamilies)
                lines.Add("... and " + (MixedFamilies.Count - maxFamilies).ToString(CultureInfo.InvariantCulture) +
                          " more mixed famil" + (MixedFamilies.Count - maxFamilies == 1 ? "y" : "ies"));
            return lines;
        }
    }

    /// <summary>What <see cref="FamilyClusterFile.DivideAndConquerDetailed"/> settled on, and the mixing just
    /// below it that made it stop there.</summary>
    public class DivideAndConquerResult
    {
        public int Identity { get; set; }

        /// <summary>The identity one step below <see cref="Identity"/> that failed, or null when the
        /// search never probed below the answer (it reached 1%).</summary>
        public int? IdentityBelow { get; set; }

        public GroupingScore? ScoreBelow { get; set; }

        /// <summary>The sentence the panel shows, naming the families that mix just below the answer.
        /// Empty when nothing below was probed.</summary>
        public string StopReason(int maxFamilies = 5)
        {
            if (IdentityBelow == null || ScoreBelow == null || ScoreBelow.MixedFamilies.Count == 0)
                return string.Empty;

            int n = ScoreBelow.MixedFamilies.Count;
            return "It stopped here because at " + IdentityBelow.Value.ToString(CultureInfo.InvariantCulture) +
                   "% " + (n == 1 ? "one family mixes" : n.ToString(CultureInfo.InvariantCulture) + " families mix") +
                   " a real sequence with a decoy:\n" +
                   string.Join("\n", ScoreBelow.DescribeMixedFamilies(maxFamilies).Select(l => "  - " + l));
        }
    }

    /// <summary>One family holding both target and decoy sequences.</summary>
    public class MixedFamily
    {
        /// <summary>At most this many targets are named in <see cref="Label"/>; the rest are counted.</summary>
        public const int MaxTargetsNamed = 3;

        public int FamilyId { get; set; }

        /// <summary>"identifier description (N aa)" for every target member, shortest sequence first.</summary>
        public List<string> Targets { get; set; } = new List<string>();

        public List<string> Decoys { get; set; } = new List<string>();

        public static MixedFamily From(int familyId, IEnumerable<FastaItemExtended> members, string decoyTag)
        {
            var list = members.ToList();
            bool IsDecoy(FastaItemExtended i) =>
                (i.SequenceIdentifier ?? string.Empty).StartsWith(decoyTag, StringComparison.Ordinal);

            return new MixedFamily
            {
                FamilyId = familyId,
                Targets = list.Where(i => !IsDecoy(i))
                    .OrderBy(i => (i.Sequence ?? string.Empty).Length)
                    .ThenBy(i => i.SequenceIdentifier, StringComparer.Ordinal)
                    .Select(Describe)
                    .ToList(),
                Decoys = list.Where(IsDecoy)
                    .Select(i => i.SequenceIdentifier ?? string.Empty)
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList(),
            };
        }

        /// <summary>"2 target(s) + 1 decoy(s): sp|P85161|BPPBD_BOTJA Bradykinin-potentiating peptide 11d
        /// (11 aa); ..." The residue count is there because a short peptide is the usual culprit.</summary>
        public string Label
        {
            get
            {
                string named = string.Join("; ", Targets.Take(MaxTargetsNamed));
                if (Targets.Count > MaxTargetsNamed)
                    named += "; and " + (Targets.Count - MaxTargetsNamed).ToString(CultureInfo.InvariantCulture) + " more";
                return Targets.Count.ToString(CultureInfo.InvariantCulture) + " target(s) + " +
                       Decoys.Count.ToString(CultureInfo.InvariantCulture) + " decoy(s): " + named;
            }
        }

        private static string Describe(FastaItemExtended item)
        {
            string description = (item.Description ?? string.Empty).Split(new[] { " OS=" }, StringSplitOptions.None)[0].Trim();
            return (item.SequenceIdentifier ?? string.Empty) +
                   (description.Length > 0 ? " " + description : string.Empty) +
                   " (" + (item.Sequence ?? string.Empty).Length.ToString(CultureInfo.InvariantCulture) + " aa)";
        }
    }
}
