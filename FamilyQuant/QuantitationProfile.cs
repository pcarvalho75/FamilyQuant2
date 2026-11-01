using PatternTools.PLP;
using System;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>
    /// How the denominator behind a project's values was established.
    /// </summary>
    public enum DenominatorEvidence
    {
        /// <summary>Nothing to go on: the project carries no quantitation at all.</summary>
        NoQuantitation,

        /// <summary>
        /// The exporter RECORDED which divisor it used - the ###Normalization line, or the [Norm=TAG]
        /// token in the description. This is a fact read out of the file.
        /// </summary>
        Recorded,

        /// <summary>
        /// Nothing was recorded, so the denominator was INFERRED from the magnitude of the values.
        /// This is a guess, and it must be labelled as one wherever it is shown.
        /// </summary>
        Inferred
    }

    /// <summary>
    /// What kind of quantitation a project carries.
    ///
    /// HISTORY, AND WHAT CHANGED
    /// -------------------------
    /// This class used to work the answer out from the values themselves, because XQuant recorded a
    /// number in SecondaryLabel.NormalizationFactor but never said WHAT that number was: the peptide
    /// export writes the run's total ion current there whether or not it divided by it, and the
    /// protein/NIAF export writes something else entirely. Reading that field alone therefore could not
    /// distinguish a raw export from a normalized one.
    ///
    /// (The comment that used to sit here claimed XQuant "leaves SecondaryLabel.NormalizationFactor at
    /// 0". That was factually wrong - XICCoreManager has always written it unconditionally, and the real
    /// files confirm it. What was missing was never the VALUE, only its meaning.)
    ///
    /// XQuant2 now records the denominator by name, so the guess is no longer needed for new files. The
    /// value-shape heuristic survives ONLY as a fallback for files exported before that, and
    /// <see cref="Evidence"/> always says which of the two produced the answer. Nothing displayed to the
    /// user may present an inference as if it were a record.
    /// </summary>
    public class QuantitationProfile
    {
        /// <summary>Number of runs in the project: one per row of the sparse matrix.</summary>
        public int FileCount { get; private set; }

        /// <summary>Sum of every value in the project.</summary>
        public double GrandTotal { get; private set; }

        /// <summary>
        /// The denominator tag the exporter recorded (NONE / ID / TIC / ZRANGE), or null for a file
        /// written before denominators were recorded. null is NOT the same as "NONE": null means
        /// unknown, NONE means positively raw.
        /// </summary>
        public string? RecordedDenominatorTag { get; private set; }

        /// <summary>The structured record, when the file carries one. null on a legacy file.</summary>
        public NormalizationInfo? Info { get; private set; }

        /// <summary>Whether the denominator was read out of the file or guessed from the numbers.</summary>
        public DenominatorEvidence Evidence
        {
            get
            {
                if (GrandTotal <= 0 && string.IsNullOrEmpty(RecordedDenominatorTag)) return DenominatorEvidence.NoQuantitation;
                return string.IsNullOrEmpty(RecordedDenominatorTag) ? DenominatorEvidence.Inferred : DenominatorEvidence.Recorded;
            }
        }

        /// <summary>Mean identified signal per run. Below 1 when the values are fractions of a total.</summary>
        public double MeanPerFile
        {
            get { return FileCount > 0 ? GrandTotal / FileCount : 0; }
        }

        /// <summary>
        /// The value-shape guess, kept SEPARATE from the recorded answer so the two can never be
        /// confused. A raw export sums to the millions (absolute areas or spectral counts); a
        /// TIC-normalized one cannot exceed 1 per run, because every peptide is a fraction of that run
        /// and the identified ones are only a part.
        ///
        /// This is a heuristic over a quantity with no guaranteed range. It is only consulted when the
        /// file records nothing.
        /// </summary>
        public bool InferredTicNormalized
        {
            get { return FileCount > 0 && GrandTotal > 0 && MeanPerFile <= 1.0; }
        }

        /// <summary>
        /// True when the values are already fractions of each run's total chromatogram.
        /// Prefers the recorded tag; falls back to <see cref="InferredTicNormalized"/> only for a file
        /// that records nothing. <see cref="Evidence"/> reports which applied.
        /// </summary>
        public bool IsTicNormalized
        {
            get
            {
                if (!string.IsNullOrEmpty(RecordedDenominatorTag))
                {
                    if (Info != null && string.Equals(
                            Info.Quantity, "NIAF", StringComparison.OrdinalIgnoreCase))
                        return false;
                    return string.Equals(RecordedDenominatorTag, NormalizationInfo.TagTotalTic,
                        StringComparison.OrdinalIgnoreCase);
                }

                return InferredTicNormalized;
            }
        }

        /// <summary>
        /// Whether a family can be expressed as a share of the whole chromatogram.
        ///
        /// Two ways to qualify now. Either the values already ARE fractions of the TIC, or the file
        /// carries enough per-run detail to re-express them as such without a re-export (see
        /// <see cref="NormalizationPlan"/>). The second is new in Phase 4 and is what lets a raw export
        /// be reported as a percentage.
        ///
        /// The old alternative - keeping raw values and dividing by SecondaryLabel.NormalizationFactor -
        /// was removed because the matrix returns a peptide's values only for the runs where it was
        /// observed, so those values could not be matched to a run's stored total and every attempt to
        /// pair them positionally divided some peptides by the wrong run. NormalizationPlan does not
        /// repeat that mistake: it scales whole ROWS, which carry their own run identity, and it matches
        /// them to labels BY NAME, refusing outright when a row cannot be matched.
        /// </summary>
        public bool CanReportTotalChromatogram
        {
            get { return IsTicNormalized || CanRescaleToTic; }
        }

        /// <summary>Set by <see cref="Describe"/>: the file carries what is needed to rescale to TIC.</summary>
        public bool CanRescaleToTic { get; private set; }

        /// <summary>Fraction of the chromatogram that was identified at all, averaged over the runs.</summary>
        public double IdentifiedFractionOfTic
        {
            get { return IsTicNormalized ? MeanPerFile : 0; }
        }

        public static QuantitationProfile Describe(PatternLabProject MyPLP)
        {
            var profile = new QuantitationProfile();

            foreach (var index in MyPLP.MyIndex.TheIndexes)
            {
                var values = MyPLP.MySparseMatrix.ExtractDimValues(index.ID, 0, true);

                // A peptide seen in only some runs yields fewer values, so this is a lower bound only;
                // the matrix row count below is what settles it.
                if (values.Count > profile.FileCount) profile.FileCount = values.Count;

                profile.GrandTotal += values.Sum();
            }

            // The widest peptide is only a LOWER BOUND on the run count, and it is short whenever no
            // single peptide was observed in every run - which is the normal case in a real project. The
            // matrix itself is authoritative: one row per run, each carrying its own run identity in
            // FileName (see the note in NormalizationPlan).
            //
            // This matters beyond the profile box. Every denominator mode divides the summed per-run
            // fractions by this number in AttributeXICToGroups, so a divisor short by half doubled every
            // reported share, silently, and could carry one past 100%.
            var matrixRows = MyPLP.MySparseMatrix?.theMatrixInRows;
            if (matrixRows != null && matrixRows.Count > profile.FileCount)
            {
                profile.FileCount = matrixRows.Count;
            }

            if (MyPLP.SecondaryLabels != null && MyPLP.SecondaryLabels.Count > profile.FileCount)
            {
                profile.FileCount = MyPLP.SecondaryLabels.Count;
            }

            // PHASE 4: read the denominator instead of guessing it. RecordedDenominatorTag prefers the
            // structured ###Normalization line and falls back to the [Norm=TAG] description token, which
            // is the layer that survives an older PatternTools.dll.
            profile.Info = MyPLP.NormalizationInfo;
            profile.RecordedDenominatorTag = MyPLP.RecordedDenominatorTag;

            profile.CanRescaleToTic = NormalizationPlan.For(MyPLP, NormalizationInfo.TagTotalTic).IsAvailable;

            return profile;
        }

        /// <summary>
        /// One line naming the denominator and stating how it was established. This is the sentence that
        /// replaces "re-export from XQuant": the user is told what the file says, not what to go and do.
        /// </summary>
        public string DenominatorSentence()
        {
            switch (Evidence)
            {
                case DenominatorEvidence.Recorded:
                    string name = NormalizationInfo.DisplayNameFor(RecordedDenominatorTag);
                    string how = Info != null ? "recorded by the exporter" : "recorded in the export description";
                    string agg = Info != null && Info.IsAggregated
                        ? " Divisors are averages over merged technical replicates, so they cannot be undone."
                        : "";
                    return $"Normalized by: {name} ({how})." + agg;

                case DenominatorEvidence.Inferred:
                    return InferredTicNormalized
                        ? "Normalized by: total ion current (TIC) - NOT recorded in the file, inferred from the size of the values."
                        : "Normalized by: nothing detectable - NOT recorded in the file, inferred from the size of the values.";

                default:
                    return "Normalized by: not recorded (this project carries no quantitation values).";
            }
        }

        public string Summary()
        {
            if (GrandTotal <= 0) return "This project carries no quantitation values.";

            string head = DenominatorSentence();

            if (!IsTicNormalized)
            {
                if (CanRescaleToTic)
                {
                    // The Phase 4 payoff: the file carries every run's divisors, so a raw (or
                    // differently-normalized) export can still be shown as a share of the chromatogram
                    // without going back to XQuant.
                    return head + $" {FileCount} run(s), values summing to {GrandTotal:G6}. " +
                           "This file also carries each run's total ion current, so percentages of the whole " +
                           "chromatogram are available without re-exporting.";
                }

                return head + $" {FileCount} run(s), values summing to {GrandTotal:G6}. " +
                       "Percentages of the whole chromatogram are not available: the file records neither " +
                       "TIC-normalized values nor the per-run totals needed to convert them.";
            }

            return head + $" {FileCount} run(s). " +
                   $"Identified peptides account for {IdentifiedFractionOfTic * 100:F2}% of the total " +
                   "chromatogram; the rest was never identified.";
        }
    }
}
