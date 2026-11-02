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

    /// <summary>Headless workflows moved from the De novo view without scientific changes.</summary>
    public static class DeNovoScreenOperations
    {
        public static FamilyClusterFile LoadHomologyCuration(string path) => ProjectStore.IsProjectPath(path)
            ? ProjectStore.ReadFqproj(path).fcf : FamilyClusterFile.Load(path);

        public static int CountExports(string directory, DeNovoSource? source = null)
        {
            source ??= DeNovoSource.DenovoOnly;
            try { return Directory.GetFiles(directory, source.FileGlob, SearchOption.AllDirectories).Length; }
            catch (Exception) { return 0; }
        }
        public const string CacheFolderName = "familyquant_denovo_cache";
        public static Func<string, string>? CachePathForRunOverride;

        public static DeNovoImport ImportAndFilter(string directory, DeNovoFilterCriteria criteria,
                                                   DeNovoSource? source = null)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            source ??= DeNovoSource.DenovoOnly;

            var import = new DeNovoImport
            {
                Directory = directory,
                SourceId = source.Id,
                SourceDisplayName = source.DisplayName,
                ConfidenceField = source.ConfidenceColumn,
            };

            string[] files = Directory.GetFiles(directory, source.FileGlob, SearchOption.AllDirectories);
            import.FilesFound = files.Length;

            var byRun = new Dictionary<string, DeNovoRunTally>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                var rows = PeaksCsvReader.Read(file, source);
                var kept = DeNovoFilter.Apply(rows, criteria);

                import.RowsRead += rows.Count;
                import.Kept.AddRange(kept);

                // Keyed by the run the READER stamped on each row, so the breakdown is keyed the same
                // way the reconciliation joins. A file holding rows of more than one run is counted
                // under each of them rather than under its own name.
                foreach (var group in rows.GroupBy(r => r.RunName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!byRun.TryGetValue(group.Key, out var tally))
                        byRun[group.Key] = tally = new DeNovoRunTally { Run = group.Key };

                    tally.RowsRead += group.Count();
                }

                foreach (var group in kept.GroupBy(r => r.RunName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!byRun.TryGetValue(group.Key, out var tally))
                        byRun[group.Key] = tally = new DeNovoRunTally { Run = group.Key };

                    tally.RowsKept += group.Count();
                }
            }

            import.PerRun = byRun.Values.OrderBy(t => t.Run, StringComparer.Ordinal).ToList();

            return import;
        }

        public static DeNovoReconciliation Reconcile(IReadOnlyList<PeaksDeNovoRow> filtered, string sepr2Path,
                                                      int? rawDeNovoRows = null,
                                                      DeNovoFilterCriteria? criteria = null)
        {
            if (sepr2Path == null) throw new ArgumentNullException(nameof(sepr2Path));

            return Reconcile(filtered, SeproPsmAdapter.Load(sepr2Path), sepr2Path, rawDeNovoRows, criteria);
        }

        public static DeNovoReconciliation Reconcile(IReadOnlyList<PeaksDeNovoRow> filtered,
                                                     List<PatternLabPsm> psms, string source,
                                                     int? rawDeNovoRows = null,
                                                     DeNovoFilterCriteria? criteria = null)
        {
            if (filtered == null) throw new ArgumentNullException(nameof(filtered));
            if (psms == null) throw new ArgumentNullException(nameof(psms));

            var rows = DeNovoReconciler.Reconcile(filtered, psms);

            return new DeNovoReconciliation
            {
                Sepr2Path = source ?? string.Empty,
                Psms = psms,
                Rows = rows,
                Counts = DeNovoReconciliationReport.CountByVerdict(rows),
                ReviewQueue = DeNovoReconciliationReport.BuildReviewQueue(rows),
                Overlap = DeNovoOverlapStudy.Build(rawDeNovoRows ?? filtered.Count, rows, criteria),
            };
        }

        public static string CachePathFor(string runName, string rawDirectory)
        {
            Func<string, string>? seam = CachePathForRunOverride;
            if (seam != null) return seam(runName);

            return Path.Combine(rawDirectory, CacheFolderName, "denovo_xic_" + SafeFileName(runName) + ".json");
        }

        private static string SafeFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();

            foreach (char c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            return sb.Length == 0 ? "run" : sb.ToString();
        }

        public static List<DeNovoRunRawMatch> MatchRunsToRaw(IEnumerable<string> runs, string rawDirectory)
        {
            if (runs == null) throw new ArgumentNullException(nameof(runs));

            var byRun = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!string.IsNullOrWhiteSpace(rawDirectory) && Directory.Exists(rawDirectory))
                {
                    foreach (string file in Directory.GetFiles(rawDirectory, "*.raw", SearchOption.TopDirectoryOnly))
                    {
                        string run = DeNovoReconciler.NormalizeRunName(file);
                        if (run.Length > 0 && !byRun.ContainsKey(run)) byRun.Add(run, file);
                    }
                }
            }
            catch (Exception)
            {
                // An unreadable folder is an empty one here. The caller reports it as runs not found,
                // which is the same sentence and the same consequence.
            }

            var matches = new List<DeNovoRunRawMatch>();

            foreach (string run in runs.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            {
                byRun.TryGetValue(run, out string? path);
                matches.Add(new DeNovoRunRawMatch
                {
                    Run = run,
                    ExpectedFileName = run + ".raw",
                    RawPath = path,
                });
            }

            return matches;
        }

        public static DeNovoXicExtraction ExtractXic(IReadOnlyList<DeNovoRunRawMatch> matches,
                                                     IReadOnlyDictionary<string, List<PeaksDeNovoRow>> keptByRun,
                                                     string rawDirectory,
                                                     Action<string>? progress)
        {
            if (matches == null) throw new ArgumentNullException(nameof(matches));
            if (keptByRun == null) throw new ArgumentNullException(nameof(keptByRun));

            var extraction = new DeNovoXicExtraction { RawDirectory = rawDirectory ?? string.Empty };

            foreach (DeNovoRunRawMatch match in matches)
            {
                if (!match.Found)
                {
                    throw new InvalidOperationException(
                        "Run \"" + match.Run + "\" has no acquisition in the chosen folder (looked for " +
                        match.ExpectedFileName + "). Extracting the other runs and leaving this one out " +
                        "would draw a de novo slice with a whole run silently missing.");
                }

                if (!keptByRun.TryGetValue(match.Run, out List<PeaksDeNovoRow>? rows) || rows.Count == 0)
                    continue;

                var options = new DeNovoXicOptions
                {
                    CachePath = CachePathFor(match.Run, extraction.RawDirectory),
                    Progress = progress,
                };

                DeNovoBatchStats stats = ExtractOneRun(match.RawPath!, rows, options);

                extraction.Stats.Add(stats);
                extraction.ByRun[match.Run] = stats;

                // The MS1 list of the run just finished must be gone before the next RAW is opened.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            return extraction;
        }

        private static DeNovoBatchStats ExtractOneRun(string rawPath, IReadOnlyList<PeaksDeNovoRow> rows,
                                                      DeNovoXicOptions options)
        {
            var (_, stats) = DeNovoBatchExtractor.ExtractForRun(rawPath, rows, options);
            return stats;
        }

        public static DeNovoHomology SearchHomology(IReadOnlyList<string> peptides, FamilyClusterFile fcf,
                                                    HomologyOptions options, string fcfPath, FamilyQuant.Operations.OperationContext? operation = null)
        {
            if (peptides == null) throw new ArgumentNullException(nameof(peptides));
            if (fcf == null) throw new ArgumentNullException(nameof(fcf));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var database = HomologyDatabase.Build(fcf.MyExtendedFastaItems, operation: operation);

            DateTime started = DateTime.UtcNow;
            List<HomologyResult> results = HomologyMapper.Search(peptides, database, options, operation: operation);
            double seconds = (DateTime.UtcNow - started).TotalSeconds;

            List<FamilyAssignment> assignments = HomologyMapper.MapToFamilies(results, fcf.MyExtendedFastaItems);

            // The engine's own aggregation, not a copy of it: the suite pins the family census against
            // this very method, so the number on screen and the number that is pinned cannot diverge.
            List<DeNovoFamilyRow> rows =
                HomologyMapper.CountByFamily(results, fcf.MyExtendedFastaItems, options.DecoyLabel);

            return new DeNovoHomology
            {
                FcfPath = fcfPath ?? string.Empty,
                PeptidesSearched = results.Count,
                MappedToTargets = results.Count(r => r.Hits.Any(h => !h.IsDecoy)),
                DecoyOnly = results.Count(r => r.Hits.Count > 0 && r.Hits.All(h => h.IsDecoy)),
                Unmatched = results.Count(r => !r.IsRejected && r.Hits.Count == 0),
                Rejected = results.Count(r => r.IsRejected),
                ElapsedSeconds = Math.Round(seconds, 1),
                Results = results,
                Assignments = assignments,
                Families = rows,
            };
        }

        public static DeNovoPartition BuildPartition(IReadOnlyList<DeNovoRunRawMatch> matches,
                                                     IReadOnlyDictionary<string, DeNovoBatchStats> xicByRun,
                                                     string? plpPath, string? xic2Path,
                                                     Action<string>? progress)
        {
            if (matches == null) throw new ArgumentNullException(nameof(matches));
            if (xicByRun == null) throw new ArgumentNullException(nameof(xicByRun));

            PatternLabProject? plp = null;
            if (!string.IsNullOrWhiteSpace(plpPath) && File.Exists(plpPath))
            {
                progress?.Invoke("reading " + Path.GetFileName(plpPath!) + "...");
                plp = new PatternLabProject(new FileInfo(plpPath!));
            }

            PatternLabRunQuants patternLab = PatternLabRunQuants.Preferred(plp, xic2Path, out string branchNote);

            if (!patternLab.IsAvailable) return new DeNovoPartition { BranchNote = branchNote };

            var ticByRun = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var deNovoByRun = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            var envelopeByRun = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (DeNovoRunRawMatch match in matches)
            {
                if (!match.Found || !xicByRun.TryGetValue(match.Run, out DeNovoBatchStats? stats)) continue;

                progress?.Invoke("measuring the total ion current of " + match.Run + "...");

                // (0,0) is the whole run, both ends: the trim proved correct for this data. Both
                // parameters are required by the extractor precisely so that no divisor is a default
                // nobody chose.
                double tic = DeNovoXicExtractor.GetTotalIonCurrentArea(match.RawPath!, 0, 0);

                ticByRun[match.Run] = PartitionValue.Measured(tic, ThreeWayPartition.SourceRawTic);
                deNovoByRun[match.Run] = PartitionValue.Measured(stats.TotalAreaM0, ThreeWayPartition.SourceDeNovoM0);
                envelopeByRun[match.Run] = stats.TotalEnvelope;
            }

            var patternLabByRun = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in patternLab.AreaByRun)
                patternLabByRun[kv.Key] = PartitionValue.Recorded(kv.Value, patternLab.Source);

            // Only the runs that were extracted: a .xic2 may describe runs this de novo set has no rows
            // for, and Build refuses a run that is present on one side and absent from another rather
            // than filling the gap with a zero that would draw as a component that merely looks small.
            var patternLabForExtracted = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);
            foreach (string run in ticByRun.Keys)
            {
                if (patternLabByRun.TryGetValue(run, out PartitionValue value)) patternLabForExtracted[run] = value;
            }

            List<ThreeWayPartitionRow> rows =
                ThreeWayPartition.Build(ticByRun, patternLabForExtracted, deNovoByRun, envelopeByRun);

            return new DeNovoPartition
            {
                BranchNote = branchNote,
                Rows = rows,
                Grouped = ThreeWayPartition.Grouped(rows),
            };
        }
    }
}
