using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.Homology
{
    /// <summary>
    /// One protein a de novo sequence was attributed to, with everything needed to judge the
    /// attribution by eye: where it landed, what it scored, how much of it was literally identical,
    /// and the stretch of database sequence it was compared against.
    /// </summary>
    public sealed class HomologyHit
    {
        internal HomologyHit(string proteinId, int offset, int score, double identityFraction,
            double gateIdentityFraction, string matchedSubsequence, bool isDecoy)
        {
            ProteinId = proteinId;
            Offset = offset;
            Score = score;
            IdentityFraction = identityFraction;
            GateIdentityFraction = gateIdentityFraction;
            MatchedSubsequence = matchedSubsequence;
            IsDecoy = isDecoy;
        }

        /// <summary>The protein's FASTA identifier, unmodified.</summary>
        public string ProteinId { get; }

        /// <summary>Zero-based residue where the peptide starts in the protein. Always leaves room
        /// for the whole peptide: <c>Offset + peptide.Length &lt;= protein.Length</c> (W1).</summary>
        public int Offset { get; }

        /// <summary>PAM30MS score of the full window at <see cref="Offset"/>.</summary>
        public int Score { get; }

        /// <summary>Identical residues over the peptide's length, measured AT <see cref="Offset"/> -
        /// the same window as <see cref="Score"/> and <see cref="MatchedSubsequence"/>, so the three
        /// numbers always describe one alignment. I/L and K/Q count as identical exactly as the
        /// options asked.</summary>
        public double IdentityFraction { get; }

        /// <summary>
        /// The identity that got this protein past the prefilter: the best any full window gave,
        /// which is what <c>MinIdentity</c> is compared against (ProgramEngine.cs:227-228).
        ///
        /// It can exceed <see cref="IdentityFraction"/>, because the window with the most identical
        /// residues need not be the window PAM30MS scores highest - conservative substitutions score
        /// well without being identities. Both are reported rather than one being quietly chosen.
        /// With the experimental tag-error gate, this is the matched confidence-weight fraction
        /// of the winning permitted variant window. ScienceAudit records that variant and its
        /// gate alignment separately; Offset, Score and MatchedSubsequence still use the original tag.
        /// </summary>
        public double GateIdentityFraction { get; }

        /// <summary>The database residues the peptide was compared against: exactly the peptide's
        /// length, never the 1-2 character stub the unbounded scan could return (W1).</summary>
        public string MatchedSubsequence { get; }

        /// <summary>Whether the identifier starts with the decoy label (Ordinal).</summary>
        public bool IsDecoy { get; }
    }

    /// <summary>What one de novo sequence found. One of these per DISTINCT query.</summary>
    public sealed class HomologyResult
    {
        internal HomologyResult(string peptide, string normalizedPeptide, string? rejection,
            IReadOnlyList<HomologyHit> hits, bool decoysSuppressed, bool targetsSuppressedByDecoyCompetition,
            int candidatesExamined, bool usedSeedIndex, HomologyScienceAudit? scienceAudit = null)
        {
            Peptide = peptide;
            NormalizedPeptide = normalizedPeptide;
            Rejection = rejection;
            Hits = hits;
            DecoysSuppressed = decoysSuppressed;
            TargetsSuppressedByDecoyCompetition = targetsSuppressedByDecoyCompetition;
            CandidatesExamined = candidatesExamined;
            UsedSeedIndex = usedSeedIndex;
            ScienceAudit = scienceAudit;
        }

        /// <summary>The query as it was supplied.</summary>
        public string Peptide { get; }

        /// <summary>The query as it was searched: trimmed and uppercased. Empty when rejected.</summary>
        public string NormalizedPeptide { get; }

        /// <summary>Why the query could not be searched, naming the offending characters; null when
        /// it was searched. A rejected query still gets a result - it is a row of the report, not an
        /// exception (W2).</summary>
        public string? Rejection { get; }

        /// <summary>True when <see cref="Rejection"/> explains why there are no hits.</summary>
        public bool IsRejected
        {
            get { return Rejection != null; }
        }

        /// <summary>The kept hits, ordered by score descending then protein identifier ordinal
        /// ascending, truncated to <c>TopHitsPerPeptide</c>. The order never depends on how the
        /// parallel search was scheduled.</summary>
        public IReadOnlyList<HomologyHit> Hits { get; }

        /// <summary>True when decoy hits were dropped because target hits were also found
        /// (ProgramEngine.cs:231-239). A peptide that hit ONLY decoys keeps them, which is the whole
        /// point of having decoys.</summary>
        public bool DecoysSuppressed { get; }

        /// <summary>True when a decoy tied or beat the best target and target hits were removed by
        /// target-decoy competition. A tie is awarded to the decoy conservatively.</summary>
        public bool TargetsSuppressedByDecoyCompetition { get; }

        /// <summary>How many proteins were actually scored. Equal to the database size on the brute
        /// force path; the seed index is only worth having while this is much smaller.</summary>
        public int CandidatesExamined { get; }

        /// <summary>Whether this particular query went through the seed index.</summary>
        public bool UsedSeedIndex { get; }

        /// <summary>Optional experimental gate/tier provenance. Null and omitted when science options are OFF.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public HomologyScienceAudit? ScienceAudit { get; }
    }

    /// <summary>
    /// Which curated families a de novo sequence belongs to.
    ///
    /// The grouping is <see cref="FastaItemExtended.UserID"/>, never <c>USearchID</c>: UserID is the
    /// working, editable family id that quantitation, reclustering, the report and the CSV all read,
    /// while USearchID is the frozen record of what USEARCH produced at import (see CLAUDE.md, and
    /// the split-survives-reclustering regression it documents).
    /// </summary>
    public sealed class FamilyAssignment
    {
        internal FamilyAssignment(string peptide, IReadOnlyList<int> familyIds,
            IReadOnlyList<string> proteinIds, IReadOnlyList<string> unknownProteinIds)
        {
            Peptide = peptide;
            FamilyIds = familyIds;
            ProteinIds = proteinIds;
            UnknownProteinIds = unknownProteinIds;
        }

        /// <summary>The query these families were assigned to, as searched.</summary>
        public string Peptide { get; }

        /// <summary>Distinct UserID values of the hit proteins, ascending.</summary>
        public IReadOnlyList<int> FamilyIds { get; }

        /// <summary>The hit proteins that the supplied family file knows, ordinal ascending.</summary>
        public IReadOnlyList<string> ProteinIds { get; }

        /// <summary>Hit proteins with no row in the family file: searched against one database and
        /// mapped against another. Reported instead of dropped, because it means the two inputs
        /// disagree.</summary>
        public IReadOnlyList<string> UnknownProteinIds { get; }

        /// <summary>True when the peptide cannot be attributed to a single family. Whether that
        /// disqualifies it is the caller's policy (see SharedPeptidePolicy), not this module's.</summary>
        public bool IsAmbiguous
        {
            get { return FamilyIds.Count > 1; }
        }
    }

    /// <summary>
    /// One family the de novo peptides reached, as <see cref="HomologySearch.CountByFamily"/> counts it
    /// and as the de novo screen's table shows it.
    ///
    /// It lives here rather than beside the screen because the count is engine work: the screen and the
    /// suite read the SAME aggregation, so a pinned family count checks the engine instead of checking
    /// a second copy of it.
    /// </summary>
    public class DeNovoFamilyRow
    {
        public int FamilyId { get; set; }

        /// <summary>Peptides attributed to this family. A peptide attributed to two families is
        /// counted under both, so the column does not add up to the peptides searched.</summary>
        public int PeptideCount { get; set; }

        public string Description { get; set; } = string.Empty;
    }
}
