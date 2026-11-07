using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        // ============================================ what the numbers are called

        private static void ReportViewTests()
        {
            // --- the family counter. It used to report only the visible rows, so it fell when a filter
            // was ticked and disagreed with the console, which counts the file.
            string counter = ReportView.FamilyCounter(232, 365);
            Check("the family counter reports the visible rows", counter.Contains("232"), counter);
            Check("and the number in the file, in the same label", counter.Contains("365"), counter);
            Check("and says which is which", counter.Contains("visible") && counter.Contains("in file"), counter);

            string unfiltered = ReportView.FamilyCounter(365, 365);
            Check("with no filter on, both halves agree and the label still reads correctly",
                unfiltered.Contains("365 visible") && unfiltered.Contains("365 in file"), unfiltered);

            // --- the status line above the table
            string none = ReportView.StatusLine(null, SharedPeptidePolicy.FamilyUnique, 0, 365);
            Check("before a project is loaded the status line says so rather than naming a denominator",
                none.Contains("no quantitation"), none);
            Check("and it does not claim a percentage of anything", !none.Contains("%"), none);

            string line = ReportView.StatusLine(NormalizationMode.TotalChromatogram,
                                                SharedPeptidePolicy.FamilyUnique, 41, 365);
            Check("the status line names the denominator in use",
                line.Contains(NormalizationMode.TotalChromatogram.ShortName()), line);
            Check("the status line names the shared-peptide rule",
                line.Contains(SharedPeptidePolicy.FamilyUnique.ShortName()), line);
            Check("the status line says how many families were quantified",
                line.Contains("41") && line.Contains("365"), line);
            string groups = ReportView.StatusLine(NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique, 2, 7);
            Check("Z12p5y2 the status denominator names groups without changing either count",
                groups.Contains("2 of 7 groups quantified", StringComparison.Ordinal) && !groups.Contains("families quantified", StringComparison.Ordinal), groups);

            // --- the column headings. "XIC Original" named neither the quantity nor the unit.
            foreach (NormalizationMode mode in Enum.GetValues<NormalizationMode>())
            {
                string head = mode.ColumnHeader();
                Check($"the {mode} column heading names its denominator",
                    head.Contains(mode.ShortName()), head);
                Check($"the {mode} column heading says which grouping it belongs to",
                    head.Contains("User families"), head);
                Check($"the {mode} column names only the curated grouping",
                    !head.Contains("USearch", StringComparison.Ordinal));

                // Every name the interface uses must be findable in the interface itself.
                Check($"the {mode} short name is part of the name shown in the list",
                    mode.DisplayName().StartsWith(mode.ShortName()), mode.DisplayName());
            }

            // The CSV says "fraction 0-1" because that is what it writes - the screen shows percentages.
            NormalizationMode? tic = NormalizationMode.TotalChromatogram;
            Check("the CSV heading warns that its values are fractions, not percentages",
                tic.CsvHeader().Contains("0-1"), tic.CsvHeader());

            NormalizationMode? nothing = null;
            Check("with no denominator established the CSV heading says unknown rather than guessing",
                nothing.CsvHeader().Contains("unknown"), nothing.CsvHeader());

            string csv = CsvReport.Build(
                new List<FastaItemExtended> { new FastaItemExtended { SequenceIdentifier = "A", XIC = 0.5 } },
                NormalizationMode.TotalChromatogram);
            var header = ParseCsvLine(csv.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)[0]);
            Check("the exported file has exactly 8 columns (the .plp share and the full-XIC share)",
                header.Count == 8, header.Count.ToString());
            Check("and its share column names the denominator",
                header[5].Contains("whole chromatogram"), header[5]);
            Check("and the mass column carries its unit", header[4].Contains("Da"), header[4]);

            // --- layer states
            Check("a usable layer says so", ReportView.LayerState(true, null) == "READY");
            Check("a layer waiting on you says what for",
                ReportView.LayerState(false, "Layer 2 first").Contains("Layer 2 first"));

            // --- sorting must be reproducible. Every member of a family carries the family's share, so
            // this data is mostly ties, which is exactly where an unstable sort shows.
            var rows = new List<FastaItemExtended>();
            for (int i = 0; i < 60; i++)
            {
                rows.Add(new FastaItemExtended
                {
                    SequenceIdentifier = "sp|P" + i.ToString("00") + "|X",
                    Description = "protein " + (i % 3),
                    USearchID = i % 5,
                    UserID = i % 5,
                    XIC = (i % 5) * 0.1,
                    XICUser = (i % 5) * 0.1     // 12 rows share every value
                });
            }

            var first = ReportView.Sort(rows, "XIC", true);
            var again = ReportView.Sort(rows.AsEnumerable().Reverse().ToList(), "XIC", true);

            Check("sorting a column full of ties gives the same order every time",
                first.Select(r => r.SequenceIdentifier).SequenceEqual(again.Select(r => r.SequenceIdentifier)));
            Check("and the sort actually sorts", first.First().XIC >= first.Last().XIC,
                first.First().XIC + " -> " + first.Last().XIC);

            var ascending = ReportView.Sort(rows, "XIC", false);
            Check("ascending is the other direction", ascending.First().XIC <= ascending.Last().XIC);
            Check("both directions keep every row",
                ascending.Count == rows.Count && first.Count == rows.Count);

            var byText = ReportView.Sort(rows, "SequenceIdentifier", false);
            Check("text columns sort too",
                byText.First().SequenceIdentifier == "sp|P00|X", byText.First().SequenceIdentifier);

            Check("no sort column leaves the rows alone rather than throwing",
                ReportView.Sort(rows, null, false).Count == rows.Count);
            Check("an unknown sort column degrades to the family id instead of throwing",
                ReportView.Sort(rows, "NoSuchColumn", false).Count == rows.Count);

            Check("the quantified-family count ignores families that scored zero",
                ReportView.QuantifiedFamilies(rows) == 4,
                ReportView.QuantifiedFamilies(rows).ToString());

            // --- the ascending/descending toggle (Ana's stuck-ascending bug). NextSort is the pure
            // decision the header handler makes; it must read the PERSISTED state, so re-clicking the same
            // column flips direction. The old handler read e.Column.SortDirection, which the ItemsSource
            // swap wipes to null every rebuild, so descending never happened; these pin the fix.
            var freshClick = ReportView.NextSort("UserID", null, false);
            Check("clicking a fresh column sorts it ascending",
                freshClick.path == "UserID" && !freshClick.descending);

            var secondClick = ReportView.NextSort("UserID", freshClick.path, freshClick.descending);
            Check("clicking the SAME column again toggles to descending",
                secondClick.path == "UserID" && secondClick.descending);

            var thirdClick = ReportView.NextSort("UserID", secondClick.path, secondClick.descending);
            Check("a third click on the same column goes back to ascending",
                thirdClick.path == "UserID" && !thirdClick.descending);

            var otherColumn = ReportView.NextSort("XIC", secondClick.path, secondClick.descending);
            Check("clicking a DIFFERENT column resets to ascending",
                otherColumn.path == "XIC" && !otherColumn.descending);

            // --- Fix 2: the USearch ID column must sort by its own value, not by UserID. Build a
            // hand-renumbered family where USearchID order differs from UserID order.
            var renumbered = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|A|X", USearchID = 5,  UserID = 500 },
                new FastaItemExtended { SequenceIdentifier = "sp|B|X", USearchID = 20, UserID = 20 },
                new FastaItemExtended { SequenceIdentifier = "sp|C|X", USearchID = 12, UserID = 12 },
            };

            var byUSearchAsc = ReportView.Sort(renumbered, "USearchID", false);
            Check("USearch ID sorts by its own value ascending (5 first, not UserID 500 last)",
                byUSearchAsc.First().USearchID == 5 && byUSearchAsc.Last().USearchID == 20,
                byUSearchAsc.First().USearchID + " -> " + byUSearchAsc.Last().USearchID);

            var byUSearchDesc = ReportView.Sort(renumbered, "USearchID", true);
            Check("and descending reverses the USearch ID order",
                byUSearchDesc.First().USearchID == 20 && byUSearchDesc.Last().USearchID == 5,
                byUSearchDesc.First().USearchID + " -> " + byUSearchDesc.Last().USearchID);

            Check("USearch ID descending is the reverse of ascending",
                byUSearchDesc.Select(r => r.USearchID).SequenceEqual(
                    byUSearchAsc.AsEnumerable().Reverse().Select(r => r.USearchID)));

            // --- Fix 1's data path: sorting by UserID descending genuinely reverses ascending.
            var byUserAsc = ReportView.Sort(renumbered, "UserID", false);
            var byUserDesc = ReportView.Sort(renumbered, "UserID", true);
            Check("UserID descending reverses UserID ascending",
                byUserDesc.Select(r => r.UserID).SequenceEqual(
                    byUserAsc.AsEnumerable().Reverse().Select(r => r.UserID)) &&
                byUserDesc.First().UserID == 500 && byUserAsc.First().UserID == 12,
                byUserAsc.First().UserID + " asc / " + byUserDesc.First().UserID + " desc");
        }

        // ==================================================== embedded help

        /// <summary>Whitespace collapsed to single spaces, so an assertion is about words and not layout.</summary>
        private static string Flatten(string text)
        {
            return string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static void HelpContentTests()
        {
            string help = Flatten(HelpContent.Build(null, NormalizationMode.TotalChromatogram, SharedPeptidePolicy.FamilyUnique));

            Check("the help names the denominator currently chosen",
                help.Contains(NormalizationMode.TotalChromatogram.DisplayName()));
            Check("the help explains why the families do not add up to 100%",
                help.Contains("100%") && help.Contains("never identified"));
            Check("the help says what a shared peptide is",
                help.Contains("more than one family"));
            Check("the help says the Divide and Conquer identity is a floor",
                help.Contains("floor") && help.Contains("not a recommendation"),
                "the word 'floor' and the denial both have to be there");
            Check("the help gives the order of work",
                help.Contains("FASTA") || help.Contains("Run USearch"));
            Check("the help explains the residue-mass column, which is not the protonated mass",
                help.Contains("18.011") && help.Contains("MH+"));
            Check("the help says the CSV holds fractions and the screen holds percentages",
                help.Contains("fractions"));

            // The one term the roadmap names as having outlived the interface.
            Check("no interface text still says 'unbiased', a term that no longer exists in the program",
                help.IndexOf("unbiased", StringComparison.OrdinalIgnoreCase) < 0);

            // The three curation features of 1.3.0.0 are documented on this page.
            Check("the help documents Undo and says there is no redo",
                help.Contains("Undo steps back") && help.Contains("no redo"));
            Check("the help documents the super-cluster match-all/match-any choice and the exclude box",
                help.Contains("match all words") && help.Contains("match any word")
                && help.Contains("Exclude words"));
            Check("the help documents that Search now scopes reclustering by the search text only",
                help.Contains("limits both Recluster by words and Group by keywords") && help.Contains("SEARCH TEXT only"));

            // Every mode and policy the help mentions must be a name the user can find in the lists.
            foreach (NormalizationMode mode in Enum.GetValues<NormalizationMode>())
            {
                Check($"the help can be built for {mode} without losing the mode's own words",
                    Flatten(HelpContent.Build(null, mode, SharedPeptidePolicy.FamilyUnique))
                        .Contains(Flatten(mode.DisplayName())));
            }

            foreach (SharedPeptidePolicy policy in Enum.GetValues<SharedPeptidePolicy>())
            {
                // The help hard-wraps its paragraphs, so compare with whitespace collapsed - otherwise
                // this asserts the line width rather than the wording.
                string text = Flatten(HelpContent.Build(null, NormalizationMode.IdentifiedSignal, policy));

                Check($"the help states the consequence of the {policy} rule",
                    text.Contains(Flatten(policy.DisplayName()))
                    && text.Contains(Flatten(policy.Explanation())),
                    policy.DisplayName());
            }
        }

        /// <summary>
        /// The second help page, in the same shape as the four-questions test above: one assertion per
        /// question the de novo feature was actually asked, so a rewrite that drops an answer fails here
        /// rather than being noticed by a reader months later.
        /// </summary>
        private static void DeNovoHelpContentTests()
        {
            string help = Flatten(HelpContent.BuildDeNovo());

            Check("the de novo page says what it never touches, which is the whole point of a separate screen",
                help.Contains("changes no family, no User ID and no quantitation"));

            // 1. What the de novo only list IS.
            Check("the de novo page says where the de novo only list comes from",
                help.Contains("denovoOnly.csv") && help.Contains("PEAKS database search did not identify"));

            // 2. What the filters mean, with the numbers taken from the criteria object rather than typed.
            Check("the de novo page explains ALC as an average, and says it is the blunt criterion",
                help.Contains("average local confidence") && help.Contains("blunt filter"));
            Check("the de novo page explains the stretch as consecutive residues over a floor",
                help.Contains("CONSECUTIVE residues") && help.Contains("breaks the run"));

            var defaults = new DeNovoFilterCriteria();
            Check("and it quotes the criteria in force, not a hand-typed copy of them",
                help.Contains("Minimum length (" + defaults.MinLength + " residues)")
                && help.Contains("Stretch length (" + defaults.StretchLength + " residues)"),
                help.Length.ToString());

            string custom = Flatten(HelpContent.BuildDeNovo(new DeNovoFilterCriteria
            {
                MinLength = 11, MinAverageLocalConfidence = 55, StretchLength = 3, StretchFloor = 77
            }));

            Check("so a screen filtering at other values gets a page describing those values",
                custom.Contains("Minimum length (11 residues)")
                && custom.Contains("Minimum ALC (55%)")
                && custom.Contains("Stretch length (3 residues) and stretch floor (77%)")
                && !custom.Contains("Minimum length (7 residues)")
                && !custom.Contains("35,686"));

            // 3. Why a same-scan alternative is not automatically a duplicate or a conflict.
            Check("the de novo page says a co-isolated spectrum is not automatically a duplicate",
                help.Contains("chimeric spectrum") && help.Contains("cannot establish duplication"));
            Check("and says why charge and precursor m/z follow the scan gate",
                help.Contains("Charge and m/z are") && help.Contains("explicit gates"));
            string reconcileJoin = System.Text.RegularExpressions.Regex.Match(help,
                @"Reconcile first joins.*?Each filtered row").Value;
            Check("Reconcile Help describes its direct 20-ppm reported-m/z join",
                reconcileJoin.Contains("direct precursor m/z match within 20 ppm", StringComparison.Ordinal)
                && reconcileJoin.Contains("reported m/z values", StringComparison.Ordinal)
                && !reconcileJoin.Contains("isotope-error-aware", StringComparison.Ordinal)
                && !reconcileJoin.Contains("M+-1", StringComparison.Ordinal));
            Check("Reconcile Help identifies Quantify's isotope-aware matching rules",
                reconcileJoin.Contains("Quantify's PatternLab-wins rule and cross-side suppression use isotope-aware matching", StringComparison.Ordinal));

            // 4. What the review queue decides, and why it is manual.
            Check("the de novo page says which two verdicts reach the review queue",
                help.Contains("overlapping alternatives, and")
                && help.Contains("PatternLab's own evidence was weak"));
            Check("and says why nothing arbitrates them for you",
                help.Contains("presents the evidence and decides nothing")
                && help.Contains("not on the same scale"));
            Check("the de novo page explains the current comparable-precursor counters without a sample census",
                help.Contains("The loaded data determines how many rows share a file and scan")
                && help.Contains("strict or I/L-folded sequence comparison")
                && help.Contains("on-screen counters and comparison table for the current source and cutoffs")
                && !HistoricalSampleCopy.IsMatch(help),
                help.Length.ToString());
            Check("and still says why the review is not arbitrated automatically",
                help.Contains("invented threshold dressed as a result"));

            // 5. What the XIC numbers are.
            Check("the de novo page says M+0 is the monoisotopic peak alone",
                help.Contains("M+0 is the monoisotopic peak alone"));
            Check("the de novo page distinguishes anchors from distinct peaks, and says which is summed",
                help.Contains("TWO GRAINS") && help.Contains("summed over DISTINCT PEAKS")
                && help.Contains("several anchors, one peak"));
            Check("and says the shared-window column is a bound rather than a correction",
                help.Contains("is a BOUND, not a correction"));

            // 6. What homology maps, and what the decoy-only count means.
            Check("the de novo page says the homology hit is attributed to a User ID family",
                help.Contains("attributed to the User ID"));
            Check("the de novo page says the decoy-only count is a control and NOT an FDR",
                help.Contains("It is a CONTROL, and it is not an FDR")
                && help.Contains("no q-value is computed or implied"));
            Check("and says what to read it against",
                help.Contains("if the two are of the same size, the threshold is too low"));

            // 7. What the three-way chart partitions, and the provenance vocabulary.
            Check("the de novo page names the three parts of the bar",
                help.Contains("PatternLab the ion current") && help.Contains("De novo what the kept de novo set adds")
                && help.Contains("Remainder what is left"));
            Check("and says the shares add to 100% by construction rather than by agreement",
                help.Contains("add up to 100% by construction"));
            Check("and says a negative remainder is a finding rather than something to clamp",
                help.Contains("NEGATIVE remainder is never clamped"));

            foreach (PartitionProvenance provenance in Enum.GetValues<PartitionProvenance>())
            {
                Check($"the de novo page defines the provenance word '{provenance}'",
                    help.Contains(provenance.ToString().ToLowerInvariant() + " "),
                    provenance.ToString());
            }

            Check("the provenance words are defined by what they mean, not merely listed",
                help.Contains("measured computed here, from the RAW file")
                && help.Contains("recorded read out of a file that had written it down")
                && help.Contains("derived neither measured nor read"));

            Check("and it says the source token is printed with the word, because the word is not enough",
                help.Contains("does not distinguish a divisor read from a .plp"));

            // The same stale-term rule the family page is held to.
            Check("no de novo help text says 'unbiased', a term that no longer exists in the program",
                help.IndexOf("unbiased", StringComparison.OrdinalIgnoreCase) < 0);
        }
    }
}
