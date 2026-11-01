namespace FamilyQuant
{
    /// <summary>
    /// What to do with a peptide whose sequence maps to proteins in more than one family.
    ///
    /// This is not a detail: in a redundant venom database most of the signal sits on shared peptides,
    /// and counting each one in every family it touches makes the families add up to far more than the
    /// sample - up to 4x the whole chromatogram on the most redundant project measured here.
    /// </summary>
    public enum SharedPeptidePolicy
    {
        /// <summary>
        /// Count a peptide only for the family it belongs to uniquely; drop it if it is shared.
        /// The families are then additive and can be read as a composition. Conservative: signal that
        /// cannot be assigned to one family is left out rather than attributed to several.
        /// </summary>
        FamilyUnique,

        /// <summary>
        /// Divide a shared peptide's signal equally among the families it maps to. Keeps the total
        /// intact (the families still add up to the identified signal) at the cost of assuming the
        /// shared signal is spread evenly.
        /// </summary>
        SplitEqually,

        /// <summary>
        /// Count a shared peptide in full for every family it maps to. Answers "how much signal touches
        /// this family", but the families overlap, so the values must NOT be summed or read as
        /// percentages of the sample.
        /// </summary>
        CountInEveryFamily
    }

    public static class SharedPeptidePolicyExtensions
    {
        /// <summary>
        /// The exact wording used in the "Shared peptides" list. Single source of truth, for the same
        /// reason as <see cref="NormalizationModeExtensions.DisplayName"/>: nothing may be named in a
        /// message that cannot also be found in the interface.
        /// </summary>
        public static string DisplayName(this SharedPeptidePolicy policy)
        {
            switch (policy)
            {
                case SharedPeptidePolicy.SplitEqually: return "Split equally between families";
                case SharedPeptidePolicy.CountInEveryFamily: return "Count in every family (overlaps)";
                default: return "Family-unique only (recommended)";
            }
        }

        /// <summary>The same, lower case and short, for the status line.</summary>
        public static string ShortName(this SharedPeptidePolicy policy)
        {
            switch (policy)
            {
                case SharedPeptidePolicy.SplitEqually: return "shared peptides split equally";
                case SharedPeptidePolicy.CountInEveryFamily: return "shared peptides counted in every family";
                default: return "family-unique peptides";
            }
        }

        /// <summary>What changes in the result, for tooltips and for the embedded help.</summary>
        public static string Explanation(this SharedPeptidePolicy policy)
        {
            switch (policy)
            {
                case SharedPeptidePolicy.SplitEqually:
                    return "A shared peptide's signal is divided equally among the families it touches. The " +
                           "families still add up to the identified total, at the cost of assuming the signal " +
                           "is spread evenly between them.";

                case SharedPeptidePolicy.CountInEveryFamily:
                    return "A shared peptide is counted in full for every family it touches. This answers " +
                           "'how much signal touches this family', but the families then OVERLAP: their " +
                           "percentages must not be added up, and on a redundant database they can total " +
                           "several times the whole sample.";

                default:
                    return "A peptide found in more than one family is counted for none of them. The families " +
                           "stay additive and can be read as a composition, at the cost of leaving out signal " +
                           "that cannot be assigned to a single family. This is the default and the right " +
                           "choice for anything you report.";
            }
        }

        /// <summary>
        /// A one- or two-sentence version of <see cref="Explanation"/> for the list's tooltips. The help
        /// keeps the full paragraph; this is what fits on screen under the mouse.
        /// </summary>
        public static string ShortExplanation(this SharedPeptidePolicy policy)
        {
            switch (policy)
            {
                case SharedPeptidePolicy.SplitEqually:
                    return "A shared peptide's signal is divided equally among its families. The total is kept, " +
                           "assuming the signal is spread evenly.";

                case SharedPeptidePolicy.CountInEveryFamily:
                    return "A shared peptide counts in full for every family it touches. The families then " +
                           "overlap, so their percentages must not be added up.";

                default:
                    return "A peptide found in more than one family is counted for none of them, so the " +
                           "families can be read as a composition. The default, and right for reporting.";
            }
        }
    }
}
