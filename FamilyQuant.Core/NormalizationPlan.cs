using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>
    /// Works out whether a project's values can be expressed under a given denominator, and by what
    /// factor each RUN has to be scaled to get there.
    ///
    /// WHY THIS IS POSSIBLE AT ALL
    /// ---------------------------
    /// From Phase 4 the .plp carries, per run, both the divisor that was actually applied (Applied=) and
    /// all three candidate divisors (D1=/D2=/D3=). A value in the matrix is `area / Applied`, so
    /// expressing it under a different divisor D is just
    ///
    ///     value * (Applied / D)
    ///
    /// per run. That is the whole arithmetic, and it means a raw export can be shown as a percentage,
    /// and a TIC-normalized one as a fraction of identified signal, WITHOUT re-exporting from XQuant.
    ///
    /// WHY IT SCALES ROWS AND NOT VALUES
    /// ---------------------------------
    /// This is the exact place an earlier attempt went wrong, and the reason it was removed: the old
    /// code took SparseMatrix.ExtractDimValues, which returns a peptide's values only for the runs where
    /// it was OBSERVED, and tried to line those up with the stored per-run totals by position. A peptide
    /// missing from run 2 silently shifted every later value onto the wrong run's divisor.
    ///
    /// So the scale is computed per ROW - and a row carries its own run identity in FileName - never per
    /// extracted value. Rows are matched to their ###SecondaryLabels entry BY NAME, and if any row
    /// cannot be matched to exactly one label the whole plan is declared unavailable. Refusing is
    /// correct here: a plausible-looking number derived from the wrong run's divisor is far worse than a
    /// greyed-out menu item.
    ///
    /// WHEN IT REFUSES
    /// ---------------
    ///  - the file records no denominator at all (a legacy export);
    ///  - the values are NIAF, i.e. already an IAF/SumIAF ratio - rescaling a ratio by an area is
    ///    arithmetically defined and scientifically meaningless;
    ///  - the divisors are AVERAGES over merged technical replicates (Agg=mean). A mean divisor is a
    ///    summary and is not invertible: multiplying back by it does not recover the areas. This is
    ///    precisely what the Aggregate stamp exists to prevent;
    ///  - any run is missing either its applied divisor or the target one;
    ///  - any row cannot be matched to exactly one SecondaryLabel.
    /// </summary>
    public sealed class NormalizationPlan
    {
        private NormalizationPlan() { }

        /// <summary>The denominator this plan targets (a NormalizationInfo.Tag* value).</summary>
        public string? TargetTag { get; private set; }

        /// <summary>True when the project's values can be expressed under <see cref="TargetTag"/>.</summary>
        public bool IsAvailable { get; private set; }

        /// <summary>Why not, in words fit for a tooltip. Empty when available.</summary>
        public string UnavailableReason { get; private set; } = "";

        /// <summary>
        /// False when the values are ALREADY under the target denominator, so nothing has to be touched.
        /// This is the backward-compatibility path: a TIC-normalized file asked to report TIC does the
        /// identical arithmetic it did before Phase 4, bit for bit.
        /// </summary>
        public bool RequiresRescaling { get; private set; }

        /// <summary>
        /// Per-row multiplier, indexed by position in SparseMatrix.theMatrixInRows.
        /// null when <see cref="RequiresRescaling"/> is false.
        /// </summary>
        public double[]? ScaleByRow { get; private set; }

        private static NormalizationPlan No(string? tag, string reason)
        {
            return new NormalizationPlan { TargetTag = tag, IsAvailable = false, UnavailableReason = reason };
        }

        /// <summary>
        /// Builds the plan for expressing <paramref name="plp"/> under <paramref name="targetTag"/>.
        /// Never throws: an unusable project yields an unavailable plan with a reason.
        /// </summary>
        public static NormalizationPlan For(PatternLabProject? plp, string targetTag)
        {
            if (plp == null || plp.MySparseMatrix == null)
            {
                return No(targetTag, "No project is loaded.");
            }

            string? recorded = plp.RecordedDenominatorTag;

            // Safety gates precede the no-op path. A denominator tag does not make NIAF an area, and an
            // aggregate divisor does not become invertible merely because its tag matches the request.
            NormalizationInfo? info = plp.NormalizationInfo;

            if (info != null && string.Equals(info.Quantity, "NIAF", StringComparison.OrdinalIgnoreCase))
            {
                return No(targetTag,
                    "These values are NIAF, which is already a normalized ratio. Dividing a ratio by a " +
                    "signal total would not mean anything.");
            }

            if (info != null && info.IsAggregated)
            {
                return No(targetTag,
                    "This project merged technical replicates, so each stored divisor is an AVERAGE of the " +
                    "merged runs. An averaged divisor cannot be undone, so the values cannot be converted " +
                    "to another denominator.");
            }

            // ---- Already there? Then this is a no-op and always available. -----------------------
            if (!string.IsNullOrEmpty(recorded) && string.Equals(recorded, targetTag, StringComparison.OrdinalIgnoreCase))
            {
                return new NormalizationPlan
                {
                    TargetTag = targetTag,
                    IsAvailable = true,
                    RequiresRescaling = false
                };
            }

            if (string.IsNullOrEmpty(recorded))
            {
                return No(targetTag,
                    "This project does not record which divisor it was normalized by. Re-export it from " +
                    "XQuant to convert between denominators.");
            }

            // ---- Safety gates before any arithmetic ----------------------------------------------
            var rows = plp.MySparseMatrix.theMatrixInRows;
            var labels = plp.SecondaryLabels;

            if (rows == null || rows.Count == 0)
            {
                return No(targetTag, "This project has no quantitation rows.");
            }

            if (labels == null || labels.Count == 0)
            {
                // The Protein Export Browser path writes no SecondaryLabels at all, so its per-run
                // divisors have nowhere to live. Nothing to convert with.
                return No(targetTag,
                    "This project carries no per-run information, so its values cannot be converted to " +
                    "another denominator.");
            }

            // The NIAF refusal above reads the section-level record, which a hand-edited or partially
            // written file can be missing while still carrying per-run divisors. Without this backstop
            // such a file would be re-expressed - dividing an already-normalized ratio by a signal
            // total, which means nothing. The aggregate rule below has the same shape for the same
            // reason: refuse when the section-level claim is absent but the per-run detail is present
            // and we cannot tell what the values are.
            if (info == null && labels.Any(l => l.IdentifiedAreaSum.HasValue
                                             || l.TotalTic.HasValue
                                             || l.ChargeRestrictedArea.HasValue))
            {
                return No(targetTag,
                    "This project carries per-run divisors but does not record which quantity they " +
                    "belong to, so converting it could silently rescale values that are already a ratio.");
            }

            // Any label that is itself an average disqualifies the file, even if the section-level
            // Aggregate key is missing (an older or partial writer).
            if (labels.Any(l => l.IsAggregated))
            {
                return No(targetTag,
                    "At least one run's divisors are averages over merged technical replicates and cannot " +
                    "be undone.");
            }

            // ---- Match every row to exactly one label, BY NAME ------------------------------------
            double[] scale = new double[rows.Count];

            for (int r = 0; r < rows.Count; r++)
            {
                SecondaryLabel? label = MatchLabel(labels, rows[r].FileName);

                if (label == null)
                {
                    return No(targetTag,
                        "A quantitation row could not be matched to a run ('" + (rows[r].FileName ?? "") +
                        "'), so its divisor is unknown.");
                }

                double? applied = label.AppliedDivisor;
                double? target = label.DivisorForTag(targetTag);

                if (!applied.HasValue || applied.Value <= 0)
                {
                    return No(targetTag, "Run '" + label.Name + "' does not record the divisor that was applied to it.");
                }

                if (!target.HasValue || target.Value <= 0)
                {
                    return No(targetTag,
                        "Run '" + label.Name + "' does not carry a " +
                        NormalizationInfo.DisplayNameFor(targetTag) + " total, so it cannot be expressed that way.");
                }

                // value_target = value_applied * Applied / Target
                scale[r] = applied.Value / target.Value;
            }

            return new NormalizationPlan
            {
                TargetTag = targetTag,
                IsAvailable = true,
                RequiresRescaling = true,
                ScaleByRow = scale
            };
        }

        /// <summary>
        /// Finds the one SecondaryLabel describing a matrix row, or null when there is not EXACTLY one.
        ///
        /// Two spellings have to be reconciled, both of them real: the peptide export writes a bare file
        /// name into the label but a full path into the row, while the protein export and the
        /// technical-replicate merge write the full path into both. So an exact match is tried first and
        /// a leaf-name match second.
        ///
        /// Ambiguity returns null rather than picking the first candidate. Position is never used as a
        /// tie-break - that is the failure mode this whole class exists to avoid.
        /// </summary>
        private static SecondaryLabel? MatchLabel(List<SecondaryLabel> labels, string? rowFileName)
        {
            if (string.IsNullOrEmpty(rowFileName)) return null;

            var exact = labels.Where(l => string.Equals(l.Name, rowFileName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1) return null;

            string rowLeaf = LeafName(rowFileName);
            if (rowLeaf.Length == 0) return null;

            var byLeaf = labels.Where(l => string.Equals(LeafName(l.Name), rowLeaf, StringComparison.OrdinalIgnoreCase)).ToList();
            return byLeaf.Count == 1 ? byLeaf[0] : null;
        }

        /// <summary>
        /// The file name part of a path. Hand-rolled rather than Path.GetFileName because these strings
        /// come out of a file and may carry separators this OS does not use; GetFileName would also throw
        /// on the invalid characters a corrupted export could contain.
        /// </summary>
        private static string LeafName(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            int cut = value!.LastIndexOfAny(new[] { '\\', '/' });
            return cut >= 0 ? value.Substring(cut + 1) : value;
        }
    }
}
