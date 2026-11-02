using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static FamilyQuant.DeNovoScreenOperations;

namespace FamilyQuant;

/// <summary>De novo session state, presentation and legacy export contracts. No control, Dispatcher or dialog is used here.</summary>
public abstract class DeNovoSession : ScreenViewModel
{
    public DeNovoFilterViewModel Filters { get; }
    public DeNovoFilterCriteria Criteria { get; protected set; } = new();
    public DeNovoImport? LastImport { get; protected set; }
    public DeNovoReconciliation? LastReconciliation { get; protected set; }
    public DeNovoXicExtraction? LastExtraction { get; protected set; }
    public DeNovoHomology? LastHomology { get; protected set; }
    public DeNovoPartition? LastPartition { get; protected set; }
    public HomologyOptions HomologySettings { get; protected set; } = new();
    public IReadOnlyList<DeNovoRunRawMatch> RawMatches { get; protected set; } = Array.Empty<DeNovoRunRawMatch>();
    public IReadOnlyList<PeaksDeNovoRow> FilteredRows => LastImport?.Kept ?? (IReadOnlyList<PeaksDeNovoRow>)Array.Empty<PeaksDeNovoRow>();
    public IReadOnlyList<PatternLabPsm> Psms => LastReconciliation?.Psms ?? (IReadOnlyList<PatternLabPsm>)Array.Empty<PatternLabPsm>();
    public IReadOnlyList<ReconciledRow> Reconciled => LastReconciliation?.Rows ?? (IReadOnlyList<ReconciledRow>)Array.Empty<ReconciledRow>();
    public IReadOnlyList<ReviewQueueRow> ReviewQueue => LastReconciliation?.ReviewQueue ?? (IReadOnlyList<ReviewQueueRow>)Array.Empty<ReviewQueueRow>();
    public Func<string, DeNovoFilterCriteria, Task<DeNovoImport>>? ImportOverride { get; set; }
    public IProgress<string> Progress { get; set; }
    public bool IsBusy { get; protected set; }
    public bool CanEdit => !IsBusy;
    public bool? ConsiderILSame { get; set; } = true;
    public bool? KQEquivalent { get; set; }
    public string Status { get; protected set; } = "";
    public ThreeWayPartitionRow? PartitionSample { get; protected set; }
    public IReadOnlyList<ThreeWayPartitionRow>? PartitionChartRows { get; protected set; }
    public DeNovoSource ActiveDeNovoSource => Filters.Sources.FirstOrDefault() ?? DeNovoSource.DenovoOnly;
    public string HelpText()
    {
        string text = HelpContent.BuildDeNovo(Criteria, ActiveDeNovoSource);
        var selected = LastImport?.Selections;
        if (selected == null || selected.Count == 0) Filters.TryBuild(out _, out selected!, out _);
        return selected.Count <= 1 ? text : "Selected source cutoffs: " + string.Join("; ", selected.Select(s =>
            s.Source.DisplayName + " " + s.Source.ConfidenceColumn + " >= " + s.MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture))) +
            ". Inspection counts accepted source calls before union resolution and quantitation.\n\n" + text;
    }
    public string ActiveConfidenceText { get => Filters.ModeTag == "deepnovo" ? Filters.DeepNovoMinimumScore : Filters.PeaksMinimumAlc;
        set { if (Filters.ModeTag == "deepnovo") Filters.DeepNovoMinimumScore = value; else Filters.PeaksMinimumAlc = value; } }
    public string ImportDirectory { get; set; } = "";
    public string SeproPackage { get; set; } = "";
    public string RawDirectory { get; set; } = "";
    public string HomologyFcf { get; set; } = "";
    public string PartitionPlp { get; set; } = "";
    public string PartitionXic2 { get; set; } = "";
    public string MinimumIdentity { get; set; } = "";
    public string TopHits { get; set; } = "";
    public string DecoyLabel { get; set; } = "";
    public string PerRunBreakdown { get; protected set; } = "";
    public string RawMatchPreview { get; protected set; } = "";
    public string BranchNote { get; protected set; } = "";
    public object? LabelFilesFound { get; protected set; } = "Files found: 0";
    public object? LabelHomologyDecoyOnly { get; protected set; } = "Decoy-only: -";
    public object? LabelHomologyElapsed { get; protected set; } = "Elapsed: -";
    public object? LabelHomologyMapped { get; protected set; } = "Mapped to targets: -";
    public object? LabelHomologyPeptides { get; protected set; } = "Peptides searched: -";
    public object? LabelHomologyUnmatched { get; protected set; } = "Unmatched: -";
    public object? LabelMinimumSourceConfidence { get; protected set; } = "Minimum ALC (%):";
    public object? LabelOverlapDivergent { get; protected set; } = "Same scan + charge, alternative sequence: -";
    public object? LabelOverlapExact { get; protected set; } = "Same scan + charge, exact sequence: -";
    public object? LabelOverlapPrecursor { get; protected set; } = "Same precursor (20 ppm): -";
    public object? LabelOverlapScan { get; protected set; } = "Same file + scan: -";
    public object? LabelReviewQueueCount { get; protected set; } = "Review queue: -";
    public object? LabelRowsKept { get; protected set; } = "Rows kept: 0";
    public object? LabelRowsRead { get; protected set; } = "Rows read: 0";
    public object? LabelVerdictCoIsolated { get; protected set; } = "Different precursor context: -";
    public object? LabelVerdictConflict { get; protected set; } = "Overlapping alternative sequence: -";
    public object? LabelVerdictDeNovoOnly { get; protected set; } = "De novo only: -";
    public object? LabelVerdictRedundant { get; protected set; } = "Redundant equal: -";
    public object? LabelVerdictWeakPsm { get; protected set; } = "Equal but weak PSM: -";
    public object? LabelXicElapsed { get; protected set; } = "Elapsed: -";
    public object? LabelXicRunsExtracted { get; protected set; } = "Runs extracted: -";
    public object? LabelXicTotalM0 { get; protected set; } = "Total M+0: -";
    public object? ReviewQueueItems { get; protected set; }
    public System.Collections.IEnumerable? XicStatsItems { get; protected set; }
    public object? HomologyItems { get; protected set; }
    public System.Collections.IEnumerable? PartitionItems { get; protected set; }
    protected DeNovoSession(DeNovoFilterViewModel? filters = null)
    {
        Filters = filters ?? new();
        Progress = new Progress<string>(message => { if (IsBusy) SetStatus(message); });
        Filters.TryBuild(out var initial, out _, out _);
        Criteria = initial;
        WriteHomologySettingsToBoxes();
        ClearReconciliation();
        RawMatchPreview = "No reconciliation yet. Reconcile before matching RAW files.";
        ObserveChild(Filters, nameof(ActiveConfidenceText));
        SetStatus("Step 1: choose a source and export folder, then press Load and filter.");
    }
    public void ShowStatus(string message) => SetStatus(message);
    protected void SetStatus(string message) { Status = message; Changed(null); }
    protected void SetScreenEnabled(bool enabled) { IsBusy = !enabled; Changed(null); }
    public void UpdateLayerStates() => Changed(null);
    public void RefreshRawMatches() { RefreshRawMatchPreview(); Changed(null); }
    public void SourceChanged()
    {
        LastImport = null; ClearReconciliation(); RefreshRawMatchPreview();
        LabelRowsRead = "Rows read: 0"; LabelRowsKept = "Rows kept: 0";
        PerRunBreakdown = "No import yet. The breakdown per run appears here.";
        SetStatus("The source changed to " + ActiveDeNovoSource.DisplayName + ". The previous source's rows and every downstream result were cleared. Press Load and filter.");
    }
    public bool ReadCriteriaFromBoxes()
    {
        if (Filters.TryBuild(out var next, out _, out string error)) { Criteria = next; WriteCriteriaToBoxes(); return true; }
        WriteCriteriaToBoxes();
        SetStatus("Nothing was changed. " + error.Replace("minimum ALC", "Minimum ALC").Replace("Stretch confidence", "Stretch floor") + " The previous settings are still in force.");
        return false;
    }
        protected void WriteCriteriaToBoxes()
        {
            // InvariantCulture, and deliberately: what is written here has to parse again through the
            // same reader, and the reader is invariant because the criteria are also carried in files.
            Filters.MinimumLength = Criteria.MinLength.ToString(CultureInfo.InvariantCulture);
            ActiveConfidenceText = Criteria.MinAverageLocalConfidence.ToString(CultureInfo.InvariantCulture);
            Filters.StretchLength = Criteria.StretchLength.ToString(CultureInfo.InvariantCulture);
            Filters.StretchFloor = Criteria.StretchFloor.ToString(CultureInfo.InvariantCulture);
        }

        protected string RestoreCriteria(DeNovoFilterCriteria previous)
        {
            if (previous == null || SameCriteria(previous, Criteria)) return string.Empty;

            Criteria = previous;
            WriteCriteriaToBoxes();

            return " The criteria have been put back to " + CriteriaSentence() +
                   ", which is what the rows now on screen were kept by.";
        }

        protected static bool SameCriteria(DeNovoFilterCriteria a, DeNovoFilterCriteria b) =>
            a.MinLength == b.MinLength
            && a.MinAverageLocalConfidence.Equals(b.MinAverageLocalConfidence)
            && a.StretchLength == b.StretchLength
            && a.StretchFloor == b.StretchFloor;

        protected string CriteriaSentence()
        {
            return "length " + Criteria.MinLength.ToString("N0", CultureInfo.CurrentCulture) +
                   " or more, " + ActiveDeNovoSource.ConfidenceColumn + " " +
                   Criteria.MinAverageLocalConfidence.ToString("N0", CultureInfo.CurrentCulture) +
                   "% or more, and " + Criteria.StretchLength.ToString("N0", CultureInfo.CurrentCulture) +
                   " consecutive residues at " + Criteria.StretchFloor.ToString("N0", CultureInfo.CurrentCulture) +
                   "% or better";
        }

        protected bool TryReadInt(string text, string name, int low, int high, Action putBack, out int value)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                && value >= low && value <= high)
            {
                return true;
            }

            Refuse(name, text,
                "a whole number between " + low.ToString(CultureInfo.CurrentCulture) + " and " +
                high.ToString(CultureInfo.CurrentCulture), putBack);
            value = 0;
            return false;
        }

        protected bool TryReadDouble(string text, string name, double low, double high, Action putBack, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && value >= low && value <= high)
            {
                return true;
            }

            Refuse(name, text,
                "a number between " + low.ToString(CultureInfo.CurrentCulture) + " and " +
                high.ToString(CultureInfo.CurrentCulture) + ", written with a dot for the decimal separator",
                putBack);
            value = 0;
            return false;
        }

        protected void Refuse(string name, string typed, string expected, Action putBack)
        {
            // Every box goes back to what is actually in force, not just the offending one: the pass is
            // all or none, so leaving edited values on screen would misdescribe the settings.
            putBack();

            SetStatus(
                "Nothing was read: \"" + typed.Trim() + "\" is not " + expected + " for " + name +
                ", so the previous settings are still in force and the boxes have been put back to them. " +
                "Next step: correct that box and press the button again.");
        }

        public void ApplyImport(DeNovoImport import)
        {
            if (import == null) throw new ArgumentNullException(nameof(import));

            LastImport = import;
            ClearReconciliation();

            LabelFilesFound = "Files found: " +
                import.FilesFound.ToString("N0", CultureInfo.CurrentCulture);
            LabelRowsRead = "Rows read: " +
                import.RowsRead.ToString("N0", CultureInfo.CurrentCulture);

            double keptShare = import.RowsRead > 0 ? (double)import.RowsKept / import.RowsRead : 0;
            LabelRowsKept = "Rows kept: " +
                import.RowsKept.ToString("N0", CultureInfo.CurrentCulture) +
                " (" + keptShare.ToString("P1", CultureInfo.CurrentCulture) + " of rows read)";

            PerRunBreakdown = import.PerRun.Count == 0
                ? "No run could be read from that folder."
                : string.Join("\n", import.PerRun.Select(t =>
                    t.Run + " · " + t.RowsRead.ToString("N0", CultureInfo.CurrentCulture) + " read · " +
                    t.RowsKept.ToString("N0", CultureInfo.CurrentCulture) + " kept"));

            SuggestRawDirectory(import.Directory);
            RefreshRawMatchPreview();
            UpdateLayerStates();

            SetStatus(import.RowsKept == 0
                ? "Showing an import of " + import.RowsRead.ToString("N0", CultureInfo.CurrentCulture) +
                  " de novo row(s) from " + import.FilesFound.ToString("N0", CultureInfo.CurrentCulture) +
                  " file(s), of which NONE cleared " + CriteriaSentence() + ". Step 2 stays blocked, " +
                  "because there is nothing to reconcile. Next step: loosen a criterion and press " +
                  "\"Load and filter\" again, knowing that a weaker de novo sequence is a weaker claim."
                : "Step 2 of 2 · Showing " + import.RowsKept.ToString("N0", CultureInfo.CurrentCulture) +
                  " kept row(s) of " + import.RowsRead.ToString("N0", CultureInfo.CurrentCulture) +
                  " read, from " + import.FilesFound.ToString("N0", CultureInfo.CurrentCulture) +
                  " file(s) across " + import.PerRun.Count.ToString("N0", CultureInfo.CurrentCulture) +
                  " run(s), at " + CriteriaSentence() + ". Next step: choose the SEPro package of the " +
                  "SAME runs in Step 2 and press Reconcile.");
        }

        protected void ClearReconciliation()
        {
            LastReconciliation = null;

            ReviewQueueItems = null;

            LabelVerdictDeNovoOnly = "De novo only: -";
            LabelVerdictCoIsolated = "Different precursor context: -";
            LabelVerdictRedundant = "Redundant equal: -";
            LabelVerdictWeakPsm = "Equal but weak PSM: -";
            LabelVerdictConflict = "Overlapping alternative sequence: -";
            LabelReviewQueueCount = "Review queue: -";
            LabelOverlapScan = "Same file + scan: -";
            LabelOverlapPrecursor = "Comparable precursor (same charge, 20 ppm): -";
            LabelOverlapExact = "Same scan + charge exact sequence, PTMs ignored: -";
            LabelOverlapDivergent = "Same scan + charge alternative sequence: -";

            // Step 3 and Step 5 describe the rows the reconciliation kept, so they go with it. A
            // stats table left standing beside a fresh reconciliation would report rows that are no
            // longer the ones on screen. Step 4 goes with it too: PeptidesForHomology reads the
            // filtered rows when there is no reconciliation and the kept verdicts when there is, so
            // both a re-import and a re-reconciliation change the set that was searched, and its
            // counts carry no record of which set produced them.
            ClearHomology();
            ClearExtraction();
        }

        protected void ClearExtraction()
        {
            LastExtraction = null;
            RawMatches = Array.Empty<DeNovoRunRawMatch>();

            XicStatsItems = null;
            LabelXicRunsExtracted = "Runs extracted: -";
            LabelXicTotalM0 = "Total M+0: -";
            LabelXicElapsed = "Elapsed: -";

            ClearPartition();
        }

        protected void ClearPartition()
        {
            LastPartition = null;

            PartitionItems = null;
            PartitionChartRows = null;
                        {
                BranchNote =
                    "No partition yet. Which source supplied the PatternLab numbers, in its own words, " +
                    "appears here.";
            }

            PartitionSample = null;
        }

        protected void ClearHomology()
        {
            LastHomology = null;

            HomologyItems = null;

            LabelHomologyPeptides = "Peptides searched: -";
            LabelHomologyMapped = "Mapped to targets: -";
            LabelHomologyDecoyOnly = "Decoy-only: -";
            LabelHomologyUnmatched = "Unmatched: -";
            LabelHomologyElapsed = "Elapsed: -";
        }

        public void ApplyReconciliation(DeNovoReconciliation outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));

            // A second reconciliation kept a different set of rows, so the areas measured for the first
            // one no longer describe what is on screen. The homology pass goes for the same reason: a
            // reconciliation switches PeptidesForHomology from the filtered rows to the kept verdicts,
            // so the very first one already changes the set Step 4 would search.
            ClearHomology();
            ClearExtraction();

            LastReconciliation = outcome;

            LabelVerdictDeNovoOnly = "De novo only: " + Count(outcome, ReconciliationVerdict.DeNovoOnly);
            LabelVerdictCoIsolated = "Different precursor context: " + Count(outcome, ReconciliationVerdict.CoIsolatedExtra);
            LabelVerdictRedundant = "Redundant equal: " + Count(outcome, ReconciliationVerdict.RedundantEqual);
            LabelVerdictWeakPsm = "Equal but weak PSM: " + Count(outcome, ReconciliationVerdict.EqualButWeakPsm);
            LabelVerdictConflict = "Overlapping alternative sequence: " + Count(outcome, ReconciliationVerdict.Conflict);

            LabelReviewQueueCount = "Review queue: " +
                outcome.ReviewQueue.Count.ToString("N0", CultureInfo.CurrentCulture) + " to decide";

            DeNovoOverlapSummary overlap = outcome.Overlap
                ?? DeNovoOverlapStudy.Build(FilteredRows.Count, outcome.Rows, Criteria);
            LabelOverlapScan = "Same file + scan: " + N(overlap.SharedFileAndScan);
            LabelOverlapPrecursor = "Comparable precursor (same charge, m/z within " +
                overlap.PrecursorTolerancePpm.ToString("0.##", CultureInfo.CurrentCulture) + " ppm): " +
                N(overlap.SamePrecursorWithinTolerance);
            LabelOverlapExact = "Same scan + charge exact sequence, PTMs ignored: " +
                N(overlap.ScanLevelStrictSequenceMatchesIgnoringPtm);
            LabelOverlapDivergent = "Same scan + charge alternative sequence: " +
                N(overlap.ScanLevelSequenceDivergencesIgnoringPtmStrict);

            ReviewQueueItems = (object?)outcome.SourceReviewQueue ?? outcome.ReviewQueue;

            RefreshRawMatchPreview();
            UpdateLayerStates();

            int alternatives = outcome.Counts.TryGetValue(ReconciliationVerdict.Conflict, out int c) ? c : 0;
            int weak = outcome.Counts.TryGetValue(ReconciliationVerdict.EqualButWeakPsm, out int w) ? w : 0;

            SetStatus(
                "Showing " + FilteredRows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " kept de novo row(s) reconciled against " +
                outcome.Psms.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " PatternLab identification(s) from " + Path.GetFileName(outcome.Sepr2Path) + ": " +
                Count(outcome, ReconciliationVerdict.DeNovoOnly) + " new, " +
                Count(outcome, ReconciliationVerdict.CoIsolatedExtra) + " different-precursor-context rows, " +
                Count(outcome, ReconciliationVerdict.RedundantEqual) + " redundant, and " +
                (alternatives + weak).ToString("N0", CultureInfo.CurrentCulture) +
                " in the review queue. Of " + N(overlap.SharedFileAndScan) + " rows sharing file + scan, " +
                N(overlap.DifferentChargeInSharedScan) + " report a different charge and " +
                N(overlap.SameChargeButNoMzMatch) + " share charge but not precursor m/z. Among " +
                N(overlap.SamePrecursorWithinTolerance) + " rows passing file + scan + charge + 20-ppm m/z, " +
                N(overlap.StrictSequenceMatchesIgnoringPtm) + " are exact after PTM removal (" +
                N(overlap.IleLeuEquivalentSequenceMatches) + " after I/L folding) and " +
                N(overlap.SequenceDivergencesAfterIleLeuFold) +
                " remain alternative sequences. For the broader same-file/scan/charge audit, " +
                N(overlap.ScanLevelStrictSequenceMatchesIgnoringPtm) +
                " are exact after PTM removal (" + N(overlap.ScanLevelIleLeuEquivalentSequenceMatches) +
                " when I/L are folded) and " + N(overlap.ScanLevelSequenceDivergencesAfterIleLeuFold) +
                " differ after I/L folding. These are de-novo/search reconciliation rates, not a " +
                "search-vs-search accuracy estimate. Comparable-precursor alternatives are excluded from additive de novo quantification.");
        }

        protected static string Count(DeNovoReconciliation outcome, ReconciliationVerdict verdict)
        {
            int n = outcome.Counts.TryGetValue(verdict, out int value) ? value : 0;
            return n.ToString("N0", CultureInfo.CurrentCulture);
        }

        protected static string N(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

        public void ExportSummaryCsv(string path)
        {
            if (LastReconciliation == null) throw new InvalidOperationException(
                "There is no reconciliation to export; run one in Step 2 first.");

            DeNovoReconciliationReport.WriteSummaryCsv(path, LastReconciliation.Rows);
        }

        public void ExportReviewQueueCsv(string path)
        {
            if (LastReconciliation == null) throw new InvalidOperationException(
                "There is no reconciliation to export; run one in Step 2 first.");

            if (LastReconciliation.SourceReviewQueue is { } sourceRows) DeNovoInspectionAudit.WriteReviewQueueCsv(path, sourceRows);
            else DeNovoReconciliationReport.WriteReviewQueueCsv(path, LastReconciliation.Rows);
        }

        public void ExportOverlapStudyCsv(string path)
        {
            if (LastReconciliation?.Overlap == null) throw new InvalidOperationException(
                "There is no overlap study to export; run a reconciliation in Step 2 first.");
            File.WriteAllText(path, DeNovoOverlapStudy.BuildSummaryCsv(LastReconciliation.Overlap),
                              new UTF8Encoding(false));
        }

        public List<string> KeptRunNames() => KeptRowsByRun().Keys
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

        public Dictionary<string, List<PeaksDeNovoRow>> KeptRowsByRun()
        {
            var byRun = new Dictionary<string, List<PeaksDeNovoRow>>(StringComparer.OrdinalIgnoreCase);
            if (LastReconciliation == null) return byRun;

            foreach (ReconciledRow row in LastReconciliation.Rows)
            {
                if (row.Verdict != ReconciliationVerdict.DeNovoOnly
                    && row.Verdict != ReconciliationVerdict.CoIsolatedExtra) continue;

                string run = DeNovoReconciler.NormalizeRunName(row.DeNovo.RunName);
                if (run.Length == 0) continue;

                if (!byRun.TryGetValue(run, out List<PeaksDeNovoRow>? rows))
                    byRun[run] = rows = new List<PeaksDeNovoRow>();

                rows.Add(row.DeNovo);
            }

            return byRun;
        }

        protected void SuggestRawDirectory(string peaksDirectory)
        {
            if (RawDirectory == null || RawDirectory.Trim().Length > 0) return;
            if (string.IsNullOrWhiteSpace(peaksDirectory)) return;

            try
            {
                DirectoryInfo? parent = Directory.GetParent(peaksDirectory);
                if (parent != null && parent.Exists) RawDirectory = parent.FullName;
            }
            catch (Exception)
            {
                // A path with no parent is not an error: the box simply stays empty and the preview
                // says no run found its acquisition.
            }
        }

        protected void RefreshRawMatchPreview()
        {
            if (RawMatchPreview == null) return;

            List<string> runs = KeptRunNames();

            if (runs.Count == 0)
            {
                RawMatches = Array.Empty<DeNovoRunRawMatch>();
                RawMatchPreview =
                    "No reconciliation yet. Which kept run found which .raw appears here.";
                return;
            }

            RawMatches = MatchRunsToRaw(runs, RawDirectory.Trim());

            RawMatchPreview = string.Join("\n", RawMatches.Select(m =>
                m.Found
                    ? m.Run + " · found " + Path.GetFileName(m.RawPath!)
                    : m.Run + " · NOT FOUND, looked for " + m.ExpectedFileName));
        }

        public string MissingRunsSentence()
        {
            var missing = RawMatches.Where(m => !m.Found).Select(m => m.ExpectedFileName).ToList();
            return missing.Count == 0 ? "none." : string.Join(", ", missing) + ".";
        }

        public void ApplyExtraction(DeNovoXicExtraction outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));

            // A new extraction invalidates any partition built on the previous one.
            ClearPartition();

            LastExtraction = outcome;

            XicStatsItems = outcome.Stats;

            LabelXicRunsExtracted = "Runs extracted: " +
                outcome.Stats.Count.ToString("N0", CultureInfo.CurrentCulture) + " of " +
                KeptRunNames().Count.ToString("N0", CultureInfo.CurrentCulture);
            LabelXicTotalM0 = "Total M+0: " +
                outcome.TotalAreaM0.ToString("E4", CultureInfo.CurrentCulture);
            LabelXicElapsed = "Elapsed: " +
                outcome.ElapsedSeconds.ToString("N1", CultureInfo.CurrentCulture) + " s";

            UpdateLayerStates();

            int redundant = outcome.Stats.Sum(s => s.RedundantAnchors);

            SetStatus(
                "Step 5 of 5 · Extracted " + outcome.Stats.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " run(s) in " + outcome.ElapsedSeconds.ToString("N1", CultureInfo.CurrentCulture) +
                " s: " + outcome.Stats.Sum(s => s.DistinctPeaks).ToString("N0", CultureInfo.CurrentCulture) +
                " distinct chromatographic peak(s) carrying " +
                outcome.TotalAreaM0.ToString("E4", CultureInfo.CurrentCulture) +
                " of monoisotopic ion current, after collapsing " +
                redundant.ToString("N0", CultureInfo.CurrentCulture) +
                " anchor(s) that were the same peak sampled by another MS2 scan. Next step: choose a " +
                "PatternLab project in Step 5 and press \"Build partition\".");
        }

        protected void WriteHomologySettingsToBoxes()
        {
            // InvariantCulture, and deliberately: the identity is read back through an invariant
            // reader, so a comma-decimal machine must not be shown a value its own reader would refuse.
            MinimumIdentity = HomologySettings.MinIdentity.ToString("0.##", CultureInfo.InvariantCulture);
            TopHits = HomologySettings.TopHitsPerPeptide.ToString(CultureInfo.InvariantCulture);
            ConsiderILSame = HomologySettings.ConsiderILSame;
            KQEquivalent = HomologySettings.KQEquivalent;
            DecoyLabel = HomologySettings.DecoyLabel;
        }

        public bool ReadHomologySettingsFromBoxes()
        {
            if (!TryReadDouble(MinimumIdentity, "Minimum identity", 0, 1,
                               WriteHomologySettingsToBoxes, out double minIdentity)) return false;
            if (!TryReadInt(TopHits, "Top hits per peptide", 1, 1000,
                            WriteHomologySettingsToBoxes, out int topHits)) return false;

            string decoy = DecoyLabel ?? string.Empty;
            if (decoy.Trim().Length == 0)
            {
                Refuse("Decoy prefix", decoy, "a non-empty prefix, because a search with no decoy label " +
                       "has no control to read its mapped fraction against", WriteHomologySettingsToBoxes);
                return false;
            }

            HomologySettings = new HomologyOptions
            {
                MinIdentity = minIdentity,
                TopHitsPerPeptide = topHits,
                ConsiderILSame = ConsiderILSame == true,
                KQEquivalent = KQEquivalent == true,
                DecoyLabel = decoy.Trim(),
            };

            WriteHomologySettingsToBoxes();
            return true;
        }

        public List<string> PeptidesForHomology()
        {
            IEnumerable<PeaksDeNovoRow> rows = LastReconciliation == null
                ? FilteredRows
                : LastReconciliation.Rows
                    .Where(r => r.Verdict == ReconciliationVerdict.DeNovoOnly
                             || r.Verdict == ReconciliationVerdict.CoIsolatedExtra)
                    .Select(r => r.DeNovo);

            return rows
                .Select(r => DeNovoReconciler.NormalizeSequence(r.Peptide))
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        public void ApplyHomology(DeNovoHomology outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));

            LastHomology = outcome;

            double share = outcome.PeptidesSearched > 0
                ? (double)outcome.MappedToTargets / outcome.PeptidesSearched
                : 0;

            LabelHomologyPeptides = "Peptides searched: " +
                outcome.PeptidesSearched.ToString("N0", CultureInfo.CurrentCulture);
            LabelHomologyMapped = "Mapped to targets: " +
                outcome.MappedToTargets.ToString("N0", CultureInfo.CurrentCulture) +
                " (" + share.ToString("P2", CultureInfo.CurrentCulture) + ")";
            LabelHomologyDecoyOnly = "Decoy-only: " +
                outcome.DecoyOnly.ToString("N0", CultureInfo.CurrentCulture);
            LabelHomologyUnmatched = "Unmatched: " +
                outcome.Unmatched.ToString("N0", CultureInfo.CurrentCulture);
            LabelHomologyElapsed = "Elapsed: " +
                outcome.ElapsedSeconds.ToString("N1", CultureInfo.CurrentCulture) + " s";

            HomologyItems = outcome.Families;

            UpdateLayerStates();

            SetStatus(
                "Showing " + outcome.PeptidesSearched.ToString("N0", CultureInfo.CurrentCulture) +
                " distinct peptide(s) searched against " + Path.GetFileName(outcome.FcfPath) + ": " +
                outcome.MappedToTargets.ToString("N0", CultureInfo.CurrentCulture) + " reached a target (" +
                share.ToString("P2", CultureInfo.CurrentCulture) + "), " +
                outcome.DecoyOnly.ToString("N0", CultureInfo.CurrentCulture) +
                " reached decoys only, and " +
                outcome.Unmatched.ToString("N0", CultureInfo.CurrentCulture) +
                " reached nothing, across " +
                outcome.Families.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " target family(ies). Read the mapped count against the decoy-only count beside it: that " +
                "is the rate at which this threshold invents a family. Export the homology snapshot from Results.");
        }

        public void ExportHomologyCsv(string path)
        {
            if (LastHomology == null) throw new InvalidOperationException(
                "There is no homology search to export; run one in Step 4 first.");

            HomologyCsv.Write(path, LastHomology);
        }

        protected static string CsvField(string? value) =>
            "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        public void ApplyPartition(DeNovoPartition outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));

            LastPartition = outcome;

            // Verbatim, refusals included. Reducing a refusal to "unavailable" would hide which source
            // could not answer, and that is the fact the reader needs.
            BranchNote = outcome.BranchNote;

            if (!outcome.IsAvailable)
            {
                PartitionItems = null;
                PartitionChartRows = null;
                PartitionSample = null;
                UpdateLayerStates();

                SetStatus("No partition was drawn, because no source could give the PatternLab quants " +
                          "one run at a time. What the sources said is shown in Step 5 word for word. " +
                          "Next step: give the .xic2 of the same extraction, which still holds every " +
                          "run's quants separately.");
                return;
            }

            var display = new List<ThreeWayPartitionRow>(outcome.Rows);
            if (outcome.Grouped != null) display.Add(outcome.Grouped);

            PartitionItems = display;
            PartitionSample = outcome.Rows.Count > 0 ? outcome.Rows[0] : null;

            PartitionChartRows = outcome.Rows;

            UpdateLayerStates();

            ThreeWayPartitionRow summary = outcome.Grouped ?? outcome.Rows[0];
            bool anyNegative = outcome.Rows.Any(r => r.NegativeRemainder);

            SetStatus(
                "Showing the ion current of " + outcome.Rows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                " run(s) split three ways. Over all of them together, PatternLab explains " +
                summary.SharePatternLabPercent.ToString("N4", CultureInfo.CurrentCulture) +
                "%, the de novo set adds " +
                summary.ShareDeNovoPercent.ToString("N4", CultureInfo.CurrentCulture) +
                "%, and " + summary.ShareRemainderPercent.ToString("N4", CultureInfo.CurrentCulture) +
                "% is still without an owner. " + (anyNegative
                    ? "At least one run has a NEGATIVE remainder: there the two numerators together " +
                      "claim more signal than the run holds, which means signal is being counted twice " +
                      "or the totals do not belong to the same acquisition. It is reported with its " +
                      "sign and never clamped. "
                    : string.Empty) +
                "Next step: save the CSV, or open the chart in a window of its own.");
        }

        public void SavePartitionCsv(string path)
        {
            if (LastPartition == null || !LastPartition.IsAvailable) throw new InvalidOperationException(
                "There is no partition to save; build one in Step 5 first.");

            // The per-run rows only: BuildCsv computes the aggregate itself, so a file can never carry
            // one that does not belong to the rows above it.
            ThreeWayPartition.WriteCsv(path, LastPartition.Rows);
        }

}
