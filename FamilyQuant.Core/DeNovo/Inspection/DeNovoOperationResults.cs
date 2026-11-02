using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant
{
    /// <summary>One acquisition run of the selected de novo export, as the breakdown reports it.</summary>
    public class DeNovoRunTally
    {
        public string Run { get; set; } = string.Empty;
        public int RowsRead { get; set; }
        public int RowsKept { get; set; }
    }

    /// <summary>
    /// The result of one import pass: what was found, what was read, what survived the criteria.
    ///
    /// A value object, produced off the UI thread and applied on it. The screen holds no half-built
    /// state: an import that throws leaves the previous one on screen untouched.
    /// </summary>
    public class DeNovoImport
    {
        public IReadOnlyList<DeNovoSourceSelection> Selections { get; set; } = Array.Empty<DeNovoSourceSelection>();
        public DeNovoFilterCriteria? AppliedCriteria { get; set; }
        public string Directory { get; set; } = string.Empty;
        public string SourceId { get; set; } = DeNovoSource.DenovoOnly.Id;
        public string SourceDisplayName { get; set; } = DeNovoSource.DenovoOnly.DisplayName;
        public string ConfidenceField { get; set; } = DeNovoSource.DenovoOnly.ConfidenceColumn;
        public int FilesFound { get; set; }
        public int RowsRead { get; set; }

        /// <summary>The rows that cleared every criterion, in file order.</summary>
        public List<PeaksDeNovoRow> Kept { get; set; } = new List<PeaksDeNovoRow>();

        /// <summary>One entry per run, ordered by run name so the same export always reports the same
        /// way. Runs are named by file name, never by position: see <see cref="DeNovoReconciler"/>.</summary>
        public List<DeNovoRunTally> PerRun { get; set; } = new List<DeNovoRunTally>();

        public int RowsKept => Kept.Count;
    }

    /// <summary>The result of one reconciliation pass, likewise built off the UI thread.</summary>
    public class DeNovoReconciliation
    {
        public List<SourceAwareReviewQueueRow>? SourceReviewQueue { get; set; }
        public string Sepr2Path { get; set; } = string.Empty;
        public List<PatternLabPsm> Psms { get; set; } = new List<PatternLabPsm>();
        public List<ReconciledRow> Rows { get; set; } = new List<ReconciledRow>();
        public Dictionary<ReconciliationVerdict, int> Counts { get; set; }
            = new Dictionary<ReconciliationVerdict, int>();
        public List<ReviewQueueRow> ReviewQueue { get; set; } = new List<ReviewQueueRow>();
        public DeNovoOverlapSummary? Overlap { get; set; }
    }

    /// <summary>
    /// One kept acquisition run and the .raw that was found for it, or was not.
    ///
    /// Matched BY FILE NAME through <see cref="DeNovoReconciler.NormalizeRunName"/>, never by position
    /// in a folder listing: a de novo export and a directory need not use the same order, and extracting a
    /// precursor against another run's acquisition returns a plausible wrong area.
    /// </summary>
    public class DeNovoRunRawMatch
    {
        public string Run { get; set; } = string.Empty;

        /// <summary>What was looked for, so a missing acquisition can be named rather than merely
        /// counted.</summary>
        public string ExpectedFileName { get; set; } = string.Empty;

        /// <summary>The acquisition found for this run, or null when none was.</summary>
        public string? RawPath { get; set; }

        public bool Found => RawPath != null;
    }

    /// <summary>What one extraction pass produced, built off the UI thread and applied on it.</summary>
    public class DeNovoXicExtraction
    {
        public string RawDirectory { get; set; } = string.Empty;

        /// <summary>One entry per extracted run, in run-name order.</summary>
        public List<DeNovoBatchStats> Stats { get; set; } = new List<DeNovoBatchStats>();

        /// <summary>The same entries keyed by normalized run name, which is how Layer 5 joins them.</summary>
        public Dictionary<string, DeNovoBatchStats> ByRun { get; set; }
            = new Dictionary<string, DeNovoBatchStats>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sum of M+0 over the DISTINCT PEAKS of every run. The de novo numerator.</summary>
        public double TotalAreaM0 => Stats.Sum(s => s.TotalAreaM0);

        public double ElapsedSeconds => Stats.Sum(s => s.ElapsedSeconds);
    }

    /// <summary>What one homology pass produced. Counts partition the searched set: mapped, decoy
    /// only, unmatched and rejected are exclusive and exhaustive.</summary>
    public class DeNovoHomology
    {
        public string FcfPath { get; set; } = string.Empty;
        public int PeptidesSearched { get; set; }
        public int MappedToTargets { get; set; }
        public int DecoyOnly { get; set; }
        public int Unmatched { get; set; }
        public int Rejected { get; set; }
        public double ElapsedSeconds { get; set; }

        public List<HomologyResult> Results { get; set; } = new List<HomologyResult>();

        /// <summary>One per result, positionally joinable with <see cref="Results"/>.</summary>
        public List<FamilyAssignment> Assignments { get; set; } = new List<FamilyAssignment>();

        /// <summary>Target families only, most peptides first.</summary>
        public List<DeNovoFamilyRow> Families { get; set; } = new List<DeNovoFamilyRow>();
    }

    /// <summary>What one partition pass produced, including the case where no source could answer per
    /// run: the refusal is a first-class result and is shown word for word.</summary>
    public class DeNovoPartition
    {
        /// <summary>Which source supplied the PatternLab segment, in <see cref="PatternLabRunQuants"/>'
        /// own words, refusals included. Displayed verbatim.</summary>
        public string BranchNote { get; set; } = string.Empty;

        /// <summary>The per-run rows, or empty when the PatternLab side refused.</summary>
        public List<ThreeWayPartitionRow> Rows { get; set; } = new List<ThreeWayPartitionRow>();

        /// <summary>The aggregate over <see cref="Rows"/>, or null when there are none.</summary>
        public ThreeWayPartitionRow? Grouped { get; set; }

        public bool IsAvailable => Rows.Count > 0;
    }

}
