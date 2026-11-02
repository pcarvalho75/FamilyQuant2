using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>
    /// The display filters behind the results grid. These decide the "Total Families" counter, which is
    /// why they live here and not inline in the window: the counter reflects what is VISIBLE, so it
    /// legitimately reads lower than the number of families in the file whenever a filter is active.
    /// </summary>
    public static class FamilyFilter
    {
        public const string DecoyPrefix = SequenceLabels.DecoyPrefix;

        /// <summary>
        /// The database marker is retained for compatibility. Classification uses the exact database
        /// prefix after unwrapping one exact decoy prefix, through SequenceLabels.IsContaminant.
        /// </summary>
        public const string ContaminantMarker = SequenceLabels.ContaminantMarker;

        public static bool IsContaminant(FastaItemExtended item)
        {
            return SequenceLabels.IsContaminant(item.SequenceIdentifier);
        }

        /// <summary>
        /// The Search box's terms: the text split at commas and semicolons, each trimmed of spaces and of
        /// straight or curly quotes, empty pieces dropped. One term reproduces the old single-text search
        /// exactly; several are alternatives (Ana, 2026-10-24: "metalloprotease, SVMP" listed nothing).
        /// </summary>
        public static List<string> SearchTerms(string? searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return new List<string>();
            return searchText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().Trim('"', '“', '”').Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static List<FastaItemExtended> Apply(IEnumerable<FastaItemExtended> items,
                                                    string searchText,
                                                    bool onlyWithXic,
                                                    bool hideDecoys,
                                                    bool hideContaminants = false)
        {
            IEnumerable<FastaItemExtended> result = items;

            List<string> terms = SearchTerms(searchText);
            if (terms.Count > 0)
            {
                // ANY of the terms: "metalloprotease, SVMP" keeps the rows carrying either. A term keeps
                // its inner spaces, so "ribosomal protein" is still searched as that text.
                result = result.Where(a => terms.Any(needle =>
                    (a.Description ?? string.Empty).IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (a.Domains ?? string.Empty).IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (a.SequenceIdentifier ?? string.Empty).IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0));
            }

            if (onlyWithXic)
            {
                // Either quantitation counts: a project quantified only by Compute full XIC has no .plp
                // share, and the filter hid every row of it.
                result = result.Where(a => a.XICUser > 0 || (a.FullXicShare ?? 0) > 0);
            }

            if (hideDecoys)
            {
                result = result.Where(a => !SequenceLabels.IsDecoy(a.SequenceIdentifier ?? string.Empty));
            }

            if (hideContaminants)
            {
                result = result.Where(a => !IsContaminant(a));
            }

            return result.ToList();
        }

        /// <summary>
        /// Distinct families among the given rows, counted on the curated grouping.
        ///
        /// This counted USearchID, which stopped being the working grouping the moment the user renumbered
        /// anything by hand: a family split in two still counted as one, so the "X families became Y"
        /// messages reported a number the table did not show.
        /// </summary>
        public static int CountFamilies(IEnumerable<FastaItemExtended> items)
        {
            return items.Select(a => a.UserID).Distinct().Count();
        }
    }
}
