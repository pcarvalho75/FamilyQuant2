using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>
    /// The words and the ordering behind what the grid shows.
    ///
    /// This is deliberately UI-free: every sentence the window displays about the numbers is built here,
    /// so it can be asserted by the test suite without a window, and so a phrase cannot drift between the
    /// status line, a column header and a dialog. Two things went wrong before and are fixed here:
    ///
    ///  - the family counter reported only the VISIBLE rows, so it fell when "Hide Reverse" was ticked
    ///    and disagreed with the console. It now always reports both numbers;
    ///  - the share columns were headed "XIC Original" / "XIC User", which named neither the denominator
    ///    nor the unit.
    /// </summary>
    public static class ReportView
    {
        /// <summary>
        /// "232 visible · 365 in file". Both numbers, always - the single change that removes the
        /// question. When no filter is active the two are equal and the line still reads correctly.
        /// </summary>
        public static string FamilyCounter(int visible, int inFile)
        {
            return visible.ToString("N0", CultureInfo.CurrentCulture) + " visible · " +
                   inFile.ToString("N0", CultureInfo.CurrentCulture) + " in file";
        }

        /// <summary>
        /// The permanent line above the table: what the share column is a percentage OF, how
        /// shared peptides were treated, and how many families ended up with a number at all.
        ///
        /// <paramref name="mode"/> is null until a project has been loaded and the columns calculated.
        /// It is the mode the DISPLAYED numbers were produced with, never the one currently picked in
        /// the list - those differ between choosing a mode and pressing Recalculate, and reporting the
        /// pending one would label the old numbers with the new denominator.
        /// </summary>
        public static string StatusLine(NormalizationMode? mode, SharedPeptidePolicy policy,
                                        int quantifiedFamilies, int familiesInFile)
        {
            if (mode == null)
            {
                return "showing: family clustering only · no quantitation loaded · the share column " +
                       "stays empty until Load PatternLab project is used under PatternLab (optional)";
            }

            return "showing: " + mode.Value.ShortName() +
                   " · " + policy.ShortName() +
                   " · " + quantifiedFamilies.ToString("N0", CultureInfo.CurrentCulture) + " of " +
                   familiesInFile.ToString("N0", CultureInfo.CurrentCulture) + " groups quantified";
        }

        /// <summary>Names the displayed full-XIC basis and rule, retaining any separately loaded PatternLab status.</summary>
        public static string FullXicStatusLine(string basis, SharedPeptidePolicy policy, string? patternLabStatus = null)
        {
            string line = "showing: Full XIC · " + basis + " · " + policy.ShortName();
            if (!string.IsNullOrEmpty(patternLabStatus))
                line += " · PatternLab: " + (patternLabStatus.StartsWith("showing: ", StringComparison.Ordinal)
                    ? patternLabStatus["showing: ".Length..] : patternLabStatus);
            return line;
        }

        /// <summary>Families that ended up with a number, for the status line.</summary>
        public static int QuantifiedFamilies(IEnumerable<FastaItemExtended> items)
        {
            return items.Where(i => i.XICUser > 0).Select(i => i.UserID).Distinct().Count();
        }

        /// <summary>
        /// The state badge on a Layer's header. The three Layers suggested an order but never said where
        /// you were or what was missing, so the sequence FASTA -> .fcf -> PLP -> recalculate -> export
        /// could only be found by trying things.
        /// </summary>
        public static string LayerState(bool ready, string? blockedBecause)
        {
            if (ready) return "READY";
            return string.IsNullOrEmpty(blockedBecause) ? "PENDING" : "BLOCKED · " + blockedBecause;
        }

        /// <summary>
        /// A total order over the rows for a given column, so a sort is reproducible.
        ///
        /// WPF's own column sorting goes through ListCollectionView, whose comparison sort is not
        /// stable: rows that tie on the sorted column - and on this data most of a family ties, because
        /// every member of a family carries the same share - came back in a different order each time
        /// the same header was clicked. Sorting here with explicit tie-breaks makes the order a
        /// function of the data alone.
        /// </summary>
        /// <summary>
        /// Which column to sort by and which way, given the column just clicked and the state currently in
        /// effect. Re-clicking the SAME column flips the direction (this is the ascending/descending toggle);
        /// clicking a DIFFERENT column starts it ascending. Pure and UI-free so the toggle can be asserted
        /// headless: the handler must decide from this persisted state, not from the DataGrid column adorner,
        /// which the ItemsSource swap wipes to null on every rebuild.
        /// </summary>
        public static (string path, bool descending) NextSort(string clickedPath, string? currentPath,
                                                              bool currentDescending)
        {
            return clickedPath == currentPath
                ? (clickedPath, !currentDescending)
                : (clickedPath, false);
        }

        public static List<FastaItemExtended> Sort(IEnumerable<FastaItemExtended> rows, string? sortMemberPath,
                                                   bool descending)
        {
            var list = rows.ToList();

            if (string.IsNullOrEmpty(sortMemberPath)) return list;

            IOrderedEnumerable<FastaItemExtended> ordered;

            switch (sortMemberPath)
            {
                case "USearchID": ordered = Order(list, i => (double)i.USearchID, descending); break;
                case "UserID": ordered = Order(list, i => (double)i.UserID, descending); break;
                case "MonoisotopicMass": ordered = Order(list, i => i.MonoisotopicMass, descending); break;
                case "XIC": ordered = Order(list, i => i.XIC, descending); break;
                case "XICUser": ordered = Order(list, i => i.XICUser, descending); break;
                case "FullXicShare": ordered = Order(list, i => i.FullXicShare ?? -1, descending); break;

                case "UserAnnotation": ordered = OrderText(list, i => i.UserAnnotation, descending); break;
                case "SequenceIdentifier": ordered = OrderText(list, i => i.SequenceIdentifier, descending); break;
                case "Description": ordered = OrderText(list, i => i.Description, descending); break;
                case "Domains": ordered = OrderText(list, i => i.Domains, descending); break;

                // USearchID now sorts by its own value (above), which matters only for hand-renumbered
                // families where it no longer equals UserID. Anything unrecognised falls through to the
                // primary key, which the tie-break below sorts anyway.
                default: ordered = Order(list, i => (double)i.UserID, descending); break;
            }

            // The tie-break is always ascending, whichever way the primary key points: it exists to make
            // the answer unique, not to be a second sort the user asked for. It breaks on the CURATED
            // family: on USearchID, which no longer follows the grouping, sorting any column where
            // family members tie scattered a hand-curated family across the table.
            return ordered
                .ThenBy(i => i.UserID)
                .ThenBy(i => i.SequenceIdentifier ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        private static IOrderedEnumerable<FastaItemExtended> Order(List<FastaItemExtended> rows,
                                                                   Func<FastaItemExtended, double> key,
                                                                   bool descending)
        {
            return descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
        }

        private static IOrderedEnumerable<FastaItemExtended> OrderText(List<FastaItemExtended> rows,
                                                                       Func<FastaItemExtended, string> key,
                                                                       bool descending)
        {
            // The machine's own culture, because this is what a person reads on screen; the CSV is a
            // separate decision and stays invariant.
            Func<FastaItemExtended, string> safe = i => key(i) ?? string.Empty;

            return descending
                ? rows.OrderByDescending(safe, StringComparer.CurrentCulture)
                : rows.OrderBy(safe, StringComparer.CurrentCulture);
        }
    }
}
