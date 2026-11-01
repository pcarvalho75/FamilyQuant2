using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant
{
    internal sealed class PatternLabAttribution
    {
        Dictionary<string, List<string>> ProteinPeptideDictionary { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// Peptide name -> sparse-matrix index id. Replaces a linear Find over every index entry that
        /// used to run once per peptide per family (O(families x peptides x indexes)).
        /// </summary>
        Dictionary<string, int> PeptideIndexIds { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Per-peptide XIC totals memoised for the currently loaded PLP. Peptides shared between families
        /// - the normal case in a redundant venom database - were otherwise re-extracted from the sparse
        /// matrix once per family, and again for each of the two groupings (USearch and User).
        /// </summary>
        Dictionary<int, double> PeptideXicTotals { get; set; } = new Dictionary<int, double>();

        /// <summary>
        /// Per-peptide totals with each RUN rescaled to a different denominator (Phase 4). Separate from
        /// <see cref="PeptideXicTotals"/> on purpose: that cache holds the file's own values and must
        /// stay untouched, so the unrescaled path keeps producing exactly the numbers it always did.
        /// </summary>
        Dictionary<int, double> PeptideRescaledTotals { get; set; } = new Dictionary<int, double>();

        /// <summary>Which denominator <see cref="PeptideRescaledTotals"/> was built for; null when unbuilt.</summary>
        string? RescaledTotalsTag { get; set; }

        /// <summary>
        /// The project the lookups and caches above were built from. Everything cached is only valid for
        /// that project, so it is compared before use rather than trusting callers to re-run
        /// <see cref="SetLookupDictionary"/> after switching PLPs.
        /// </summary>
        PatternLabProject? LookupSource { get; set; }

        /// <summary>
        /// Rebuilds the lookups if this is not the project they were built from, so a stale cache can
        /// never survive a change of PLP.
        /// </summary>
        private void EnsureLookupsFor(PatternLabProject MyPLP)
        {
            if (!ReferenceEquals(LookupSource, MyPLP))
            {
                SetLookupDictionary(MyPLP);
            }
        }

        /// <summary>
        /// Non-redundant peptides mapping to any protein of the family. A peptide shared between the
        /// family's members must be counted once, not once per protein.
        /// </summary>
        private List<string> GetPeptidesInGroup(IGrouping<int, FastaItemExtended> theGroup)
        {
            List<string> peptidesInGroup = new List<string>();

            foreach (var item in theGroup)
            {
                if (ProteinPeptideDictionary.TryGetValue(item.SequenceIdentifier, out var mapped))
                {
                    peptidesInGroup.AddRange(mapped);
                }
            }

            // Distinct() keeps first-occurrence order, so the summation order - and with it the exact
            // floating-point total - is the same as before this was factored out.
            return peptidesInGroup.Distinct().ToList();
        }

        /// <summary>
        /// How many families of the given grouping each peptide maps to. A peptide counted in every
        /// family it touches makes the families overlap, so this is what the shared-peptide policy uses
        /// to keep the column additive.
        /// </summary>
        private Dictionary<string, int> CountFamiliesPerPeptide(List<IGrouping<int, FastaItemExtended>> groupedItems)
        {
            var counts = new Dictionary<string, int>();

            foreach (var group in groupedItems)
            {
                foreach (string pep in GetPeptidesInGroup(group))
                {
                    counts[pep] = counts.TryGetValue(pep, out int c) ? c + 1 : 1;
                }
            }

            return counts;
        }

        /// <summary>
        /// Builds the protein -> peptides map and the peptide -> index-id map for a freshly loaded PLP,
        /// and drops any memoised XIC values from a previous project.
        /// </summary>
        public void SetLookupDictionary(PatternLabProject MyPLP)
        {
            Console.WriteLine("=== Setting up lookup dictionaries ===");

            ProteinPeptideDictionary = new Dictionary<string, List<string>>();
            PeptideIndexIds = new Dictionary<string, int>();
            PeptideXicTotals = new Dictionary<int, double>();
            PeptideRescaledTotals = new Dictionary<int, double>();
            RescaledTotalsTag = null;
            LookupSource = MyPLP;

            Console.WriteLine($"PLP has {MyPLP.MyIndex.TheIndexes.Count} index entries");

            int duplicateNames = 0;

            foreach (var item in MyPLP.MyIndex.TheIndexes)
            {
                string[] locci;

                if (item.Description.Contains("Mappable proteins : "))
                {
                    string mappable = Regex.Split(item.Description, "Mappable proteins : ")[1];
                    locci = Regex.Split(mappable, ", ");
                }
                else
                {
                    locci = Regex.Split(item.Description, " ").Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                }

                // Keep the FIRST entry for a repeated peptide name, matching the old List.Find behaviour.
                // A duplicate used to throw out of Add() and abort the whole PLP load.
                if (!PeptideIndexIds.ContainsKey(item.Name))
                {
                    PeptideIndexIds.Add(item.Name, item.ID);
                }
                else
                {
                    duplicateNames++;
                }

                foreach (string locus in locci)
                {
                    if (ProteinPeptideDictionary.TryGetValue(locus, out var peptides))
                    {
                        peptides.Add(item.Name);
                    }
                    else
                    {
                        ProteinPeptideDictionary.Add(locus, new List<string>() { item.Name });
                    }
                }
            }

            Console.WriteLine($"ProteinPeptideDictionary created with {ProteinPeptideDictionary.Count} proteins");
            Console.WriteLine($"PeptideIndexIds created with {PeptideIndexIds.Count} peptides");

            if (duplicateNames > 0)
            {
                Console.WriteLine($"Note: {duplicateNames} repeated peptide name(s) in the index; the first occurrence was kept.");
            }
        }

        /// <summary>
        /// Fills the XIC column for every family.
        ///
        /// <see cref="NormalizationMode.IdentifiedSignal"/> divides each family by the total over all
        /// families, so the column adds up to 1. <see cref="NormalizationMode.TotalChromatogram"/> leaves
        /// each family as its own share of the run's chromatogram, so the column adds up to the part of
        /// the chromatogram that was identified AND assigned - never to 1, the remainder being the
        /// unidentified sample plus, under <see cref="SharedPeptidePolicy.FamilyUnique"/>, the shared
        /// signal that no single family owns.
        ///
        /// The policy matters more than it looks: counting a shared peptide in every family it touches
        /// makes the families overlap, and on a redundant venom database that pushes the column's total
        /// well past 100% of the chromatogram. Only the additive policies may be read as a composition.
        /// </summary>
        public void AttributeXICToGroups(FamilyClusterFile document, PatternLabProject MyPLP,
                                         NormalizationMode mode, SharedPeptidePolicy sharedPolicy)
        {
            EnsureLookupsFor(MyPLP);

            // PHASE 4. A mode that names a denominator may need the file's values re-expressed under it.
            //
            // When the file is ALREADY under that denominator the plan requires no rescaling and every
            // number below is computed exactly as it was before Phase 4 - that is what keeps the
            // historical TIC behaviour bit-identical. Only a genuine conversion touches the values.
            //
            // An unavailable plan is NOT silently downgraded here: the UI greys the item out, and
            // anything that reaches this point with an impossible mode falls back to unscaled values
            // rather than inventing a conversion.
            NormalizationPlan? plan = null;
            string? targetTag = mode.ToDenominatorTag();

            if (targetTag != null)
            {
                plan = NormalizationPlan.For(MyPLP, targetTag);
                if (!plan.IsAvailable || !plan.RequiresRescaling) plan = null;
            }

            var groupedItems = document.GroupedFastaItems;

            var familiesPerPeptide = CountFamiliesPerPeptide(groupedItems);

            // First pass: calculate all XICs
            var groupXICs = new Dictionary<int, double>();
            double totalXIC = 0;

            foreach (var group in groupedItems)
            {
                double groupXIC = GetGroupXIC(group, MyPLP, familiesPerPeptide, sharedPolicy, plan);
                groupXICs[group.Key] = groupXIC;
                totalXIC += groupXIC;
            }

            if (totalXIC <= 0)
            {
                Console.WriteLine("Warning: no peptide of this database was found in the project - every family scored 0.");
            }

            // In any denominator mode the value is already a fraction of one run's signal, so the only
            // adjustment is averaging over the runs: a peptide seen in three runs contributed three
            // per-run fractions, and a run where it was absent contributed a true zero.
            //
            // `targetTag != null` is exactly "this mode names a denominator", which now covers the two
            // Phase 4 modes as well. The run count is still QuantitationProfile.FileCount, unchanged, so
            // the TIC mode divides by the same number it always did.
            bool denominatorMode = targetTag != null;

            int runs = denominatorMode
                ? Math.Max(1, QuantitationProfile.Describe(MyPLP).FileCount)
                : 1;

            // Second pass: normalize and assign. A zero total means nothing matched (a protein-level PLP,
            // or a database unrelated to the project); zero the column instead of leaving the previous
            // project's abundances in place, which then got saved into the .fcf as if they were current.
            foreach (var group in groupedItems)
            {
                double value;

                if (totalXIC <= 0)
                    value = 0;
                else if (denominatorMode)
                    value = groupXICs[group.Key] / runs;
                else
                    value = groupXICs[group.Key] / totalXIC;

                foreach (FastaItemExtended item in group)
                {
                    item.XICUser = value;
                }
            }
        }

        /// <summary>
        /// Total quantitation of one family. <paramref name="familiesPerPeptide"/> is required by every
        /// policy except <see cref="SharedPeptidePolicy.CountInEveryFamily"/>, which needs no weighting.
        /// A non-null <paramref name="plan"/> re-expresses each run under its requested denominator.
        /// </summary>
        private double GetGroupXIC(IGrouping<int, FastaItemExtended> theGroup, PatternLabProject MyPLP,
                                  Dictionary<string, int>? familiesPerPeptide, SharedPeptidePolicy sharedPolicy,
                                  NormalizationPlan? plan)
        {
            EnsureLookupsFor(MyPLP);

            if (plan != null) EnsureRescaledTotals(MyPLP, plan);

            double xic = 0;

            foreach (string pep in GetPeptidesInGroup(theGroup))
            {
                if (!PeptideIndexIds.TryGetValue(pep, out int indexId)) continue;

                double weight = 1;

                if (sharedPolicy != SharedPeptidePolicy.CountInEveryFamily && familiesPerPeptide != null)
                {
                    int families = familiesPerPeptide.TryGetValue(pep, out int f) ? f : 1;

                    if (families > 1)
                    {
                        if (sharedPolicy == SharedPeptidePolicy.FamilyUnique) continue;
                        weight = 1.0 / families;   // SplitEqually
                    }
                }

                double peptideXic;

                if (plan != null)
                {
                    // Rescaled totals are precomputed for every dim in one pass over the matrix, so a
                    // peptide absent from the table genuinely contributed nothing.
                    PeptideRescaledTotals.TryGetValue(indexId, out peptideXic);
                }
                else if (!PeptideXicTotals.TryGetValue(indexId, out peptideXic))
                {
                    peptideXic = MyPLP.MySparseMatrix.ExtractDimValues(indexId, 0, true).Sum();
                    PeptideXicTotals[indexId] = peptideXic;
                }

                xic += weight * peptideXic;
            }

            return xic;
        }

        /// <summary>
        /// Builds, once per denominator, the per-peptide totals with each RUN scaled by the plan.
        ///
        /// ONE PASS OVER THE ROWS, not one extraction per peptide. The scale belongs to a ROW, so the
        /// natural loop is rows-then-values, and it visits each stored value exactly once. Doing it the
        /// other way round - per peptide, per row - would reintroduce the O(dims x rows) sweep that
        /// SparseMatrix.ExtractDimValues was optimised to avoid.
        ///
        /// Rows are visited in matrix order and each dim accumulates in that same order, which is the
        /// order ExtractDimValues(...).Sum() would have used. With a scale of 1 the result is therefore
        /// identical down to the floating-point rounding, not merely close.
        /// </summary>
        private void EnsureRescaledTotals(PatternLabProject MyPLP, NormalizationPlan plan)
        {
            if (RescaledTotalsTag == plan.TargetTag && PeptideRescaledTotals.Count > 0) return;

            var totals = new Dictionary<int, double>();
            var rows = MyPLP.MySparseMatrix.theMatrixInRows;
            double[]? scales = plan.ScaleByRow;

            for (int r = 0; r < rows.Count; r++)
            {
                // A plan built for a different matrix would index out of range; treat a short scale
                // array as "no scaling for this row" rather than throwing inside a recalculation.
                double s = scales != null && r < scales.Length ? scales[r] : 1.0;

                foreach (var cell in rows[r].TheData)
                {
                    totals.TryGetValue(cell.Dim, out double running);
                    totals[cell.Dim] = running + cell.Value * s;
                }
            }

            PeptideRescaledTotals = totals;
            RescaledTotalsTag = plan.TargetTag;
        }

    }
}
