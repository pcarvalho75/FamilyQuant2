using PatternTools.PLP;

namespace FamilyQuant
{
    /// <summary>
    /// What the XIC column reports.
    ///
    /// THE NUMERIC VALUES ARE LOAD-BEARING. The "Report XIC as" ComboBox lists its items in exactly this
    /// order, so ComboBox.SelectedIndex == (int)NormalizationMode with no lookup table. The first two
    /// members keep the values they have always had, so the default (index 0) and the TIC item (index 1)
    /// mean exactly what they meant before Phase 4. Never reorder or renumber; only append.
    /// </summary>
    public enum NormalizationMode
    {
        /// <summary>
        /// Share of the IDENTIFIED signal: the family's total divided by the total over all families,
        /// so the families add up to 100%. Always available, but the denominator depends on how much of
        /// the sample the database managed to identify, so the same family shifts when the database does.
        /// </summary>
        IdentifiedSignal = 0,

        /// <summary>
        /// Share of the WHOLE chromatogram: the family measured against the run's total ion current.
        /// Families then do NOT add up to 100% - the remainder is the part of the chromatogram nothing
        /// was identified for.
        ///
        /// Historically this required a project exported with "Normalize XIC by TIC", because the values
        /// had to already BE fractions of the TIC. From Phase 4 it is also available for a project
        /// exported under a different denominator, as long as the file carries each run's TIC.
        /// </summary>
        TotalChromatogram = 1,

        /// <summary>
        /// Share of the EXTRACTED PEPTIDE signal: the family measured against the sum of that run's own
        /// extracted peptide areas (D1). Sits between the two above - the denominator is narrower than
        /// the whole chromatogram but, unlike IdentifiedSignal, it does not depend on which families the
        /// database happens to define.
        /// </summary>
        IdentifiedPeptideSignal = 2,

        /// <summary>
        /// Share of the CHARGE-RESTRICTED peptide-like MS1 signal (D3): the whole chromatogram minus the
        /// singly-charged and otherwise non-peptide-like material. XQuant computes it with YADA at
        /// extraction time, but only when asked - it costs an extra MS1 pass per raw file - so the item
        /// stays greyed out for any project extracted without it, with the reason in its tooltip.
        /// </summary>
        ChargeRestrictedSignal = 3
    }

    public static class NormalizationModeExtensions
    {
        /// <summary>
        /// The .plp wire tag a mode needs the values to be expressed under, or null for
        /// <see cref="NormalizationMode.IdentifiedSignal"/>, which is not a denominator at all: it is a
        /// ratio computed WITHIN the file, so it works whatever divisor was applied.
        /// </summary>
        public static string? ToDenominatorTag(this NormalizationMode mode)
        {
            switch (mode)
            {
                case NormalizationMode.TotalChromatogram: return NormalizationInfo.TagTotalTic;
                case NormalizationMode.IdentifiedPeptideSignal: return NormalizationInfo.TagIdentified;
                case NormalizationMode.ChargeRestrictedSignal: return NormalizationInfo.TagChargeRestricted;
                default: return null;
            }
        }

        /// <summary>
        /// The exact wording used in the "Report XIC as" list. This is the ONE place the phrase is
        /// written: the ComboBox items are filled from here at startup, so a column header, a status
        /// line or a dialog can quote the mode and be guaranteed to quote something the user can also
        /// see in the list. A name that appears in a message but nowhere in the interface is precisely
        /// the failure this phase exists to remove.
        /// </summary>
        public static string DisplayName(this NormalizationMode mode)
        {
            switch (mode)
            {
                case NormalizationMode.TotalChromatogram: return "% of whole chromatogram (TIC)";
                case NormalizationMode.IdentifiedPeptideSignal: return "% of extracted peptide signal";
                case NormalizationMode.ChargeRestrictedSignal: return "% of charge-restricted signal";
                default: return "% of identified signal (families sum to 100%)";
            }
        }

        /// <summary>The same name without the parenthetical, for places with little room.</summary>
        public static string ShortName(this NormalizationMode mode)
        {
            switch (mode)
            {
                case NormalizationMode.TotalChromatogram: return "% of whole chromatogram";
                case NormalizationMode.IdentifiedPeptideSignal: return "% of extracted peptide signal";
                case NormalizationMode.ChargeRestrictedSignal: return "% of charge-restricted signal";
                default: return "% of identified signal";
            }
        }

        /// <summary>
        /// What the grid column shows, naming the denominator and the curated grouping.
        /// </summary>
        public static string ColumnHeader(this NormalizationMode mode)
        {
            return mode.ShortName() + " (User families)";
        }

        /// <summary>The same for the CSV, where the values stay fractions rather than percentages.</summary>
        public static string CsvHeader(this NormalizationMode? mode)
        {
            if (mode == null)
            {
                return "Family share, fraction 0-1, denominator unknown (User families)";
            }

            string what = mode.Value == NormalizationMode.IdentifiedSignal
                ? "of identified signal"
                : mode.Value.ShortName().Replace("% of", "of");

            return "Family share " + what + ", fraction 0-1 (User families)";
        }

        /// <summary>
        /// One paragraph answering "what changes in the result if I pick this". Used by the mode
        /// tooltips and by the embedded help, so the two can never say different things.
        /// </summary>
        public static string Explanation(this NormalizationMode mode)
        {
            switch (mode)
            {
                case NormalizationMode.TotalChromatogram:
                    return "Each family is measured against the run's total ion current, so the column does " +
                           "NOT add up to 100%: what is missing is the part of the chromatogram nothing was " +
                           "identified for. Use it when you want to know how much of the sample a family is.";

                case NormalizationMode.IdentifiedPeptideSignal:
                    return "Each family is measured against the sum of that run's extracted peptide areas. " +
                           "Narrower than the whole chromatogram, but it does not depend on which families " +
                           "your database happens to define. Use it to compare runs of the same sample.";

                case NormalizationMode.ChargeRestrictedSignal:
                    return "Each family is measured against the peptide-like part of the chromatogram, with " +
                           "singly-charged and non-peptide-like signal removed. Available only for projects " +
                           "extracted with that extra pass switched on in XQuant.";

                default:
                    return "Each family is divided by the sum of all families, so the column adds up to 100%. " +
                           "Always available, but the denominator moves with your database: identify more " +
                           "protein and every family's percentage changes. This is the default because it " +
                           "needs nothing recorded in the file.";
            }
        }

        /// <summary>
        /// A one- or two-sentence version of <see cref="Explanation"/> for the list's tooltips. The help
        /// keeps the full paragraph; this is what fits on screen under the mouse.
        /// </summary>
        public static string ShortExplanation(this NormalizationMode mode)
        {
            switch (mode)
            {
                case NormalizationMode.TotalChromatogram:
                    return "Each family as a share of the run's total ion current, so the column does not add " +
                           "up to 100%. Use it to see how much of the sample a family is.";

                case NormalizationMode.IdentifiedPeptideSignal:
                    return "Each family as a share of the run's extracted peptide areas. Use it to compare runs " +
                           "of the same sample.";

                case NormalizationMode.ChargeRestrictedSignal:
                    return "Each family as a share of the peptide-like part of the chromatogram. Needs a project " +
                           "extracted with that extra pass switched on in XQuant.";

                default:
                    return "Each family divided by the sum of all families, so the column adds up to 100%. " +
                           "Always available, and the default.";
            }
        }
    }
}
