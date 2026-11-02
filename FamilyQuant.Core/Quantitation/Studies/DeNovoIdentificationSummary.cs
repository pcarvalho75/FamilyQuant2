using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// Identification-only consolidation of accepted PEAKS de novo calls. No XIC result participates:
    /// repeated spectra, charge states and technical-run repeats are collapsed directly from the filtered
    /// sequence calls after PTM notation is removed.
    /// </summary>
    public sealed class DeNovoIdentificationSummary
    {
        public int FilteredSpectrumCalls { get; set; }
        public int CarriedForwardSpectrumCalls { get; set; }

        public int FilteredRunSequenceChargeGroups { get; set; }
        public int FilteredRunSequenceGroups { get; set; }
        public int FilteredDistinctSequencesIgnoringPtm { get; set; }
        public int FilteredDistinctSequencesAfterIleLeuFold { get; set; }

        public int CarriedForwardRunSequenceChargeGroups { get; set; }
        public int CarriedForwardRunSequenceGroups { get; set; }
        public int CarriedForwardDistinctSequencesIgnoringPtm { get; set; }
        public int CarriedForwardDistinctSequencesAfterIleLeuFold { get; set; }

        public List<DeNovoIdentificationRunSummary> Runs { get; set; } =
            new List<DeNovoIdentificationRunSummary>();

        public void Validate()
        {
            int[] counts =
            {
                FilteredSpectrumCalls, CarriedForwardSpectrumCalls,
                FilteredRunSequenceChargeGroups, FilteredRunSequenceGroups,
                FilteredDistinctSequencesIgnoringPtm, FilteredDistinctSequencesAfterIleLeuFold,
                CarriedForwardRunSequenceChargeGroups, CarriedForwardRunSequenceGroups,
                CarriedForwardDistinctSequencesIgnoringPtm, CarriedForwardDistinctSequencesAfterIleLeuFold,
            };
            if (counts.Any(v => v < 0))
                throw new InvalidOperationException("De novo identification-consolidation counts cannot be negative.");
            if (CarriedForwardSpectrumCalls > FilteredSpectrumCalls)
                throw new InvalidOperationException("Carried-forward de novo calls cannot exceed accepted calls.");
            if (FilteredRunSequenceGroups > FilteredRunSequenceChargeGroups
                || FilteredRunSequenceChargeGroups > FilteredSpectrumCalls)
                throw new InvalidOperationException("Accepted de novo identification grains are inconsistent.");
            if (CarriedForwardRunSequenceGroups > CarriedForwardRunSequenceChargeGroups
                || CarriedForwardRunSequenceChargeGroups > CarriedForwardSpectrumCalls)
                throw new InvalidOperationException("Carried-forward de novo identification grains are inconsistent.");
            if (FilteredDistinctSequencesIgnoringPtm > FilteredRunSequenceGroups
                || CarriedForwardDistinctSequencesIgnoringPtm > CarriedForwardRunSequenceGroups)
                throw new InvalidOperationException("Pooled de novo sequence counts cannot exceed run-level sequence counts.");
            if (FilteredDistinctSequencesAfterIleLeuFold > FilteredDistinctSequencesIgnoringPtm
                || CarriedForwardDistinctSequencesAfterIleLeuFold > CarriedForwardDistinctSequencesIgnoringPtm)
                throw new InvalidOperationException("I/L folding cannot increase the number of distinct sequences.");
            if (Runs.Sum(r => r.FilteredSpectrumCalls) != FilteredSpectrumCalls
                || Runs.Sum(r => r.CarriedForwardSpectrumCalls) != CarriedForwardSpectrumCalls)
                throw new InvalidOperationException("Per-run de novo calls do not close to the pooled identification totals.");
            foreach (DeNovoIdentificationRunSummary run in Runs) run.Validate();
        }
    }

    public sealed class DeNovoIdentificationRunSummary
    {
        public string Run { get; set; } = string.Empty;
        public int FilteredSpectrumCalls { get; set; }
        public int CarriedForwardSpectrumCalls { get; set; }
        public int FilteredSequenceChargeGroups { get; set; }
        public int FilteredDistinctSequencesIgnoringPtm { get; set; }
        public int CarriedForwardSequenceChargeGroups { get; set; }
        public int CarriedForwardDistinctSequencesIgnoringPtm { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Run))
                throw new InvalidOperationException("A de novo identification run has no name.");
            if (FilteredSpectrumCalls < 0 || CarriedForwardSpectrumCalls < 0
                || FilteredSequenceChargeGroups < 0 || FilteredDistinctSequencesIgnoringPtm < 0
                || CarriedForwardSequenceChargeGroups < 0 || CarriedForwardDistinctSequencesIgnoringPtm < 0)
                throw new InvalidOperationException("Per-run de novo identification counts cannot be negative.");
            if (CarriedForwardSpectrumCalls > FilteredSpectrumCalls
                || FilteredSequenceChargeGroups > FilteredSpectrumCalls
                || CarriedForwardSequenceChargeGroups > CarriedForwardSpectrumCalls
                || FilteredDistinctSequencesIgnoringPtm > FilteredSequenceChargeGroups
                || CarriedForwardDistinctSequencesIgnoringPtm > CarriedForwardSequenceChargeGroups)
                throw new InvalidOperationException("Per-run de novo identification grains are inconsistent.");
        }
    }

    public static class DeNovoIdentificationStudy
    {
        /// <summary>
        /// Builds an identification census from the accepted PEAKS rows and the reconciliation result.
        /// "Carried forward" means DeNovoOnly or CoIsolatedExtra: a sequence hypothesis not excluded as
        /// the same comparable precursor already represented by database search. PTM notation is removed;
        /// I and L remain distinct in the primary count and an I/L-folded sensitivity count is stored too.
        /// </summary>
        public static DeNovoIdentificationSummary Build(
            IReadOnlyList<PeaksDeNovoRow> filtered,
            IReadOnlyList<ReconciledRow> reconciliation)
        {
            if (filtered == null) throw new ArgumentNullException(nameof(filtered));
            if (reconciliation == null) throw new ArgumentNullException(nameof(reconciliation));
            if (filtered.Count != reconciliation.Count)
                throw new InvalidOperationException("The filtered de novo rows and reconciliation rows must have the same grain.");

            List<PeaksDeNovoRow> carried = reconciliation
                .Where(r => r.Verdict == ReconciliationVerdict.DeNovoOnly
                         || r.Verdict == ReconciliationVerdict.CoIsolatedExtra)
                .Select(r => r.DeNovo).ToList();

            var result = new DeNovoIdentificationSummary
            {
                FilteredSpectrumCalls = filtered.Count,
                CarriedForwardSpectrumCalls = carried.Count,
                FilteredRunSequenceChargeGroups = CountRunSequenceCharge(filtered),
                FilteredRunSequenceGroups = CountRunSequence(filtered),
                FilteredDistinctSequencesIgnoringPtm = CountSequences(filtered, foldIleLeu: false),
                FilteredDistinctSequencesAfterIleLeuFold = CountSequences(filtered, foldIleLeu: true),
                CarriedForwardRunSequenceChargeGroups = CountRunSequenceCharge(carried),
                CarriedForwardRunSequenceGroups = CountRunSequence(carried),
                CarriedForwardDistinctSequencesIgnoringPtm = CountSequences(carried, foldIleLeu: false),
                CarriedForwardDistinctSequencesAfterIleLeuFold = CountSequences(carried, foldIleLeu: true),
            };

            IEnumerable<string> runs = filtered.Select(r => Run(r.RunName))
                .Union(carried.Select(r => Run(r.RunName)), StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase);
            foreach (string run in runs)
            {
                List<PeaksDeNovoRow> acceptedRun = filtered.Where(r =>
                    string.Equals(Run(r.RunName), run, StringComparison.OrdinalIgnoreCase)).ToList();
                List<PeaksDeNovoRow> carriedRun = carried.Where(r =>
                    string.Equals(Run(r.RunName), run, StringComparison.OrdinalIgnoreCase)).ToList();
                result.Runs.Add(new DeNovoIdentificationRunSummary
                {
                    Run = run,
                    FilteredSpectrumCalls = acceptedRun.Count,
                    CarriedForwardSpectrumCalls = carriedRun.Count,
                    FilteredSequenceChargeGroups = CountRunSequenceCharge(acceptedRun),
                    FilteredDistinctSequencesIgnoringPtm = CountSequences(acceptedRun, foldIleLeu: false),
                    CarriedForwardSequenceChargeGroups = CountRunSequenceCharge(carriedRun),
                    CarriedForwardDistinctSequencesIgnoringPtm = CountSequences(carriedRun, foldIleLeu: false),
                });
            }

            result.Validate();
            return result;
        }

        private static int CountRunSequenceCharge(IEnumerable<PeaksDeNovoRow> rows) => rows
            .Select(r => Run(r.RunName) + "\u0001" + Sequence(r.Peptide, false) + "\u0001" +
                         r.Charge.ToString(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal).Count();

        private static int CountRunSequence(IEnumerable<PeaksDeNovoRow> rows) => rows
            .Select(r => Run(r.RunName) + "\u0001" + Sequence(r.Peptide, false))
            .Distinct(StringComparer.Ordinal).Count();

        private static int CountSequences(IEnumerable<PeaksDeNovoRow> rows, bool foldIleLeu) => rows
            .Select(r => Sequence(r.Peptide, foldIleLeu))
            .Distinct(StringComparer.Ordinal).Count();

        private static string Run(string runName)
        {
            string run = DeNovoReconciler.NormalizeRunName(runName ?? string.Empty);
            return run.Length == 0 ? "(unknown run)" : run;
        }

        private static string Sequence(string peptide, bool foldIleLeu)
        {
            string sequence = DeNovoReconciler.NormalizeSequenceIgnoringPtm(peptide ?? string.Empty, foldIleLeu);
            if (sequence.Length == 0)
                throw new InvalidOperationException("An accepted de novo row has no residue sequence after PTM notation removal.");
            return sequence;
        }
    }
}
