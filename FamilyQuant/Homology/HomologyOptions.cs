using System;
using System.Globalization;

namespace FamilyQuant.Homology
{
    /// <summary>Which of the two equivalent scans a search runs. Both answer the same question; the
    /// seed index only avoids asking it of proteins that cannot possibly answer yes.</summary>
    public enum HomologySearchStrategy
    {
        /// <summary>Use the seed index when the database is large enough for it to pay AND the index
        /// is provably lossless for that particular query; scan every protein otherwise. This is the
        /// only setting that is safe without thinking - see
        /// <see cref="HomologyScorer.GuaranteedMatchRun"/>.</summary>
        Auto,

        /// <summary>Score every protein in the database, as PepExplorer does (ProgramEngine.cs:225).
        /// Exposed so a test can pin the index against the exhaustive answer.</summary>
        BruteForce,

        /// <summary>Always use the seed index, even for a query where a homolog could carry no shared
        /// seed. Exposed for the same equivalence test; not a setting to ship a pipeline on.</summary>
        SeedIndex,
    }

    /// <summary>
    /// Everything the homology search is allowed to decide differently. Defaults are PepExplorer's
    /// shipped GUI defaults, not its programmatic ones: MainControl.xaml:203 offers 0.80 while
    /// ProgramArgs.cs:136 defaults the same field to 0.5, and 0.5 admits proteins whose "closest
    /// peptide" is noise.
    /// </summary>
    public sealed class HomologyOptions
    {
        /// <summary>PepExplorer's interface default (MainControl.xaml:203). Deliberately not the 0.5
        /// of ProgramArgs.cs:136.</summary>
        public const double DefaultMinIdentity = 0.80;

        /// <summary>Length of the database seeds. 4 is the shortest k for which a 26-symbol
        /// alphabet gives a selective index, and the longest constraint that still lets a 12-residue
        /// tag at 80% identity be found by seed alone (see
        /// <see cref="HomologyScorer.GuaranteedMatchRun"/>).</summary>
        public const int DefaultSeedLength = 4;

        /// <summary>Below this many proteins the index costs more to consult than the scan it
        /// replaces, so <see cref="HomologySearchStrategy.Auto"/> just scans.</summary>
        public const int DefaultBruteForceProteinLimit = 500;

        /// <summary>FamilyQuant's decoy prefix (see CLAUDE.md and FamilyFilter). PepExplorer's own
        /// interface default is the shorter "Reverse" (MainControl.xaml:176).</summary>
        public const string DefaultDecoyLabel = SequenceLabels.DecoyPrefix;

        /// <summary>Fraction of the query's residues that must be identical to some window of the
        /// protein before that protein is scored at all. The prefilter of ProgramEngine.cs:225-229.</summary>
        public double MinIdentity { get; set; } = DefaultMinIdentity;

        /// <summary>Treat I and L as the same residue in the IDENTITY prefilter: a fragment spectrum
        /// cannot tell them apart (identical mass), so a de novo tag's I/L assignment is arbitrary.
        /// Ported from Aligner.cs:235. PAM30MS scoring is untouched by this - the matrix already
        /// scores I/L at 5.</summary>
        public bool ConsiderILSame { get; set; } = true;

        /// <summary>
        /// Treat K and Q as the same residue in the IDENTITY prefilter only. They differ by 0.036 Da,
        /// which a de novo engine can miscall on a low-resolution fragment. Off by default because it
        /// is a real chemical difference, not an indistinguishable one, and switching it on widens
        /// every family. PAM30MS scoring is deliberately NOT touched: K/Q there is -3, and rewriting
        /// the matrix would break the scientific contract this port exists to carry.
        /// </summary>
        public bool KQEquivalent { get; set; } = false;

        /// <summary>How many hits to keep per query, after ordering by score then protein id.</summary>
        public int TopHitsPerPeptide { get; set; } = 3;
        /// <summary>Keep only the hits tied at the best score (after the target-decoy competition), as
        /// PepExplorer does (ProgramEngine.cs:255-261), before <see cref="TopHitsPerPeptide"/> applies.
        /// For a peptide present in the database this keeps exactly the proteins that contain it: every
        /// PAM30MS diagonal strictly beats its row except L, which ties only with I (indistinguishable by
        /// mass, and the queries are I-to-L folded). Off by default; the family attribution sets it for
        /// IDENTIFIED peptides, whose search already established the sequence.</summary>
        public bool BestScoreOnly { get; set; } = false;

        /// <summary>Prefix that marks a decoy protein. Compared with
        /// <see cref="StringComparison.Ordinal"/>, always (ProgramEngine.cs:232 compares with the
        /// culture-sensitive default overload).</summary>
        public string DecoyLabel { get; set; } = DefaultDecoyLabel;

        /// <summary>Which scan to run. See <see cref="HomologySearchStrategy"/>.</summary>
        public HomologySearchStrategy Strategy { get; set; } = HomologySearchStrategy.Auto;

        /// <summary>Seed length of the database index. Must match the length the database was built
        /// with; 2 to 6, because a seed is packed into 5 bits per residue of a 32-bit key.</summary>
        public int SeedLength { get; set; } = DefaultSeedLength;

        /// <summary>Database size at or above which <see cref="HomologySearchStrategy.Auto"/> is
        /// willing to use the index.</summary>
        public int BruteForceProteinLimit { get; set; } = DefaultBruteForceProteinLimit;

        /// <summary>A copy, so a search cannot be perturbed by a caller mutating the options while it
        /// runs.</summary>
        public HomologyOptions Clone()
        {
            return new HomologyOptions
            {
                MinIdentity = MinIdentity,
                ConsiderILSame = ConsiderILSame,
                KQEquivalent = KQEquivalent,
                TopHitsPerPeptide = TopHitsPerPeptide,
                BestScoreOnly = BestScoreOnly,
                DecoyLabel = DecoyLabel,
                Strategy = Strategy,
                SeedLength = SeedLength,
                BruteForceProteinLimit = BruteForceProteinLimit,
            };
        }

        /// <summary>
        /// Refuses a configuration that cannot mean anything, the way ProgramArgs.cs:112-122 and
        /// :199-201 do. These are caller errors, so they throw; bad DATA never throws anywhere in this
        /// module.
        /// </summary>
        public void Validate()
        {
            if (MinIdentity < 0 || MinIdentity > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MinIdentity),
                    "MinIdentity must be between 0 and 1, was " + MinIdentity.ToString("R", CultureInfo.InvariantCulture) + ".");
            }

            if (TopHitsPerPeptide < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(TopHitsPerPeptide),
                    "TopHitsPerPeptide must be at least 1, was " + TopHitsPerPeptide.ToString(CultureInfo.InvariantCulture) + ".");
            }

            if (SeedLength < 2 || SeedLength > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(SeedLength),
                    "SeedLength must be between 2 and 6 (5 bits per residue in a 32-bit key), was " +
                    SeedLength.ToString(CultureInfo.InvariantCulture) + ".");
            }

            if (BruteForceProteinLimit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(BruteForceProteinLimit),
                    "BruteForceProteinLimit cannot be negative.");
            }

            if (string.IsNullOrWhiteSpace(DecoyLabel))
            {
                throw new ArgumentException("DecoyLabel cannot be null or empty.", nameof(DecoyLabel));
            }
        }
    }
}
