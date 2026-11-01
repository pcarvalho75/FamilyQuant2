using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>What the full-XIC computation is parameterised by. Everything here legitimately varies
    /// between projects; the spectrum-loading constants that MUST reproduce the stored numbers stay on
    /// <see cref="DeNovoXicExtractor"/>.</summary>
    public sealed class FullXicOptions
    {
        /// <summary>How many isotopes to credit. The go-forward basis is
        /// <see cref="DeNovoXicExtractor.DefaultEnvelopeIsotopeCount"/> (6), a ceiling: an absent
        /// isotope contributes 0 and no averagine fills it in.</summary>
        public int IsotopeCount { get; set; } = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount;

        /// <summary>The minimum points per peak a cluster must carry to be quantified, applied per
        /// isotope. <see cref="DeNovoXicExtractor.DefaultMinPointsForQuant"/> (3) is the production rule;
        /// <see cref="DeNovoXicExtractor.LegacyMinPointsForQuant"/> (1) restores the legacy XQuant2 rule
        /// bit for bit. It is stamped into the manifest, so a stored quant says what rule produced it.</summary>
        public int MinPointsForQuant { get; set; } = DeNovoXicExtractor.DefaultMinPointsForQuant;

        /// <summary>The extraction window, ppm. Must match what the stored quants used.</summary>
        public double Ppm { get; set; } = DeNovoXicExtractor.ProductionPpm;

        public int Ms1Tolerance { get; set; } = DeNovoXicExtractor.ProductionMs1Tolerance;

        /// <summary>The FamilyQuant version stamped into the manifest.</summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>Optional curated family space used to enrich the persisted spectrum table with the
        /// same many-to-many peptide-to-User-family attribution as the family plots. Quant extraction is
        /// still valid without it; rows then state explicitly that family mapping was not supplied.</summary>
        public FamilyClusterFile? FamilyCuration { get; set; }

        /// <summary>Write the spectrum-level source/search/quant comparison CSV after a successful
        /// computation. On by default; this is the inspectable table behind the DataGrid and the
        /// de-novo-effect plots.</summary>
        public bool ExportSpectrumComparisonCsv { get; set; } = true;

        /// <summary>Optional explicit output path for the spectrum comparison CSV. Null/empty writes
        /// <see cref="SpectrumComparisonTable.DefaultFileName"/> into the RAW/data directory.</summary>
        public string? SpectrumComparisonCsvPath { get; set; }

        /// <summary>The confidence criteria the de novo export is filtered by before extraction. Ignored
        /// when no de novo directory is given.</summary>
        public DeNovoFilterCriteria DeNovoCriteria { get; set; } = new DeNovoFilterCriteria();

        /// <summary>Which de novo export family to read (filename glob + confidence column). Defaults to
        /// the original de-novo-only source, so an unset value reproduces the baseline exactly. Ignored
        /// when no de novo directory is given.</summary>
        public DeNovoSource DeNovoSource { get; set; } = DeNovoSource.DenovoOnly;

        /// <summary>Extra de novo sources read alongside <see cref="DeNovoSource"/> and concatenated with
        /// it, for a union variant (for example de novo all + DeepNovo, whose two globs are searched under
        /// the same directory tree, each read with its own confidence column). Null or empty means only
        /// the primary source is read, which is the baseline. Ignored when no de novo directory is given.</summary>
        public IReadOnlyList<DeNovoSource>? AdditionalDeNovoSources { get; set; }

        /// <summary>Preferred source-aware configuration. Each source carries its OWN cutoff and an
        /// explicit priority for resolving competing sequence labels on one precursor. When empty, the
        /// legacy primary/additional properties above are converted to selections using
        /// <see cref="DeNovoCriteria"/>'s PEAKS cutoff, preserving old callers exactly.</summary>
        public IReadOnlyList<DeNovoSourceSelection>? DeNovoSelections { get; set; }

        public IReadOnlyList<DeNovoSourceSelection> ResolveDeNovoSelections()
        {
            IEnumerable<DeNovoSourceSelection> requested;
            if (DeNovoSelections != null && DeNovoSelections.Count > 0)
            {
                requested = DeNovoSelections;
            }
            else
            {
                var sources = new List<DeNovoSource> { DeNovoSource ?? FamilyQuant.DeNovo.DeNovoSource.DenovoOnly };
                if (AdditionalDeNovoSources != null)
                    sources.AddRange(AdditionalDeNovoSources.Where(s => s != null));
                requested = sources.Select((s, i) => new DeNovoSourceSelection(
                    s, DeNovoCriteria.MinAverageLocalConfidence, i));
            }

            // One configuration per stable source id. A duplicate source would read the same files twice
            // and make its priority ambiguous, so first explicit priority wins deterministically.
            return requested
                .OrderBy(s => s.Priority)
                .ThenBy(s => s.Source.Id, StringComparer.Ordinal)
                .GroupBy(s => s.Source.Id, StringComparer.OrdinalIgnoreCase)
                .Select((g, i) => new DeNovoSourceSelection(g.First().Source,
                    g.First().MinimumConfidence, i))
                .ToList();
        }
    }

    /// <summary>The product of one full-XIC computation: the stored rows, the manifest, and a note per
    /// run and side for the status line.</summary>
    public sealed class FullXicComputeResult
    {
        public List<QuantRow> Rows { get; set; } = new List<QuantRow>();
        public QuantManifest Manifest { get; set; } = new QuantManifest();
        public List<SpectrumComparisonRow> SpectrumComparisonRows { get; set; } =
            new List<SpectrumComparisonRow>();
        public string SpectrumComparisonCsvPath { get; set; } = string.Empty;

        /// <summary>Empty after a successful or disabled export. When the optional automatic CSV could
        /// not be written, this contains the actionable warning while <see cref="Rows"/>,
        /// <see cref="Manifest"/> and <see cref="SpectrumComparisonRows"/> remain a completed result.</summary>
        public string SpectrumComparisonCsvError { get; private set; } = string.Empty;

        /// <summary>Human-readable, one line per run and side, for the status line and the log. Never
        /// parsed.</summary>
        public List<string> Notes { get; set; } = new List<string>();

        /// <summary>
        /// The headline the Compute window shows FIRST: per run, the share of the TIC the quantified
        /// peptides explain, then one sentence placing the isotope-series diagnostic beside it. Without
        /// it the only percentage on screen was the diagnostic (about 60% on the jararaca runs), and it
        /// was read as "the quantified peptides explain 60% of the TIC". Built by
        /// <see cref="FullXicComputer.DescribeTicShares"/>; never parsed.
        /// </summary>
        public List<string> TicShareSummary { get; set; } = new List<string>();

        /// <summary>Q6: the per-run resolved isotope-series diagnostic, keyed by normalized run name and
        /// computed independently of identified/de novo assignment. It is not peptide-FDR calibrated.</summary>
        public Dictionary<string, Ms1EnvelopeDetectionResult> Detected { get; set; } =
            new Dictionary<string, Ms1EnvelopeDetectionResult>(StringComparer.OrdinalIgnoreCase);

        public int IdentifiedRows => Rows.Count(r => r.Side == "identified");
        public int DeNovoRows => Rows.Count(r => r.Side == "denovo");

        /// <summary>Attempts the optional, convenience CSV export without turning an output-path failure
        /// into a failed quantitation. The complete spectrum table remains in the result and will be
        /// persisted by the next .fqproj save, so the user can retry/export it from the DataGrid.</summary>
        public bool TryExportSpectrumComparisonCsv(string path)
        {
            SpectrumComparisonCsvPath = string.Empty;
            SpectrumComparisonCsvError = string.Empty;
            try
            {
                SpectrumComparisonCsvPath = SpectrumComparisonTable.SaveCsv(
                    path, SpectrumComparisonRows);
                Notes.Add("Spectrum evidence table: " + SpectrumComparisonCsvPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is ArgumentException
                || ex is NotSupportedException
                || ex is System.Security.SecurityException)
            {
                SpectrumComparisonCsvError =
                    "WARNING: the spectrum comparison CSV could not be written to \"" +
                    (path ?? string.Empty) + "\" (" + ex.GetType().Name + ": " + ex.Message +
                    "). Quantitation completed and the complete table remains available in the " +
                    "project/DataGrid; save the .fqproj and choose another CSV path to retry.";
                Notes.Add(SpectrumComparisonCsvError);
                return false;
            }
        }
    }

    /// <summary>
    /// How the acquisitions of a RAW folder pair with the runs the identifications and the kept de novo
    /// calls name, BY FILE NAME through <see cref="DeNovoReconciler.NormalizeRunName"/>. Every list holds
    /// normalized run names in name order.
    ///
    /// It exists so a folder whose RAWs match nothing is REFUSED rather than computed. A renamed
    /// acquisition ("x_01-003.raw" holding run "x_01") used to finish as a success with zero rows and
    /// zero runs, and that empty result then replaced the project's quantitation. Nothing about the
    /// pairing is relaxed here: the names still have to match exactly, the mismatch is only reported.
    /// </summary>
    public sealed class FullXicRunMatch
    {
        /// <summary>At most this many names are spelled out per list; the rest are counted.</summary>
        public const int MaxNamesListed = 8;

        /// <summary>Runs with a RAW and something to extract: the runs a computation processes.</summary>
        public IReadOnlyList<string> Matched { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<string> RawRuns { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> IdentifiedRuns { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> DeNovoRuns { get; private set; } = Array.Empty<string>();

        /// <summary>RAWs that no identification and no kept de novo call names.</summary>
        public IReadOnlyList<string> RawWithoutEvidence { get; private set; } = Array.Empty<string>();

        /// <summary>Runs the identifications name that have no RAW in the folder.</summary>
        public IReadOnlyList<string> IdentifiedWithoutRaw { get; private set; } = Array.Empty<string>();

        /// <summary>Runs the kept de novo calls name that have no RAW in the folder.</summary>
        public IReadOnlyList<string> DeNovoWithoutRaw { get; private set; } = Array.Empty<string>();

        public static FullXicRunMatch Build(IEnumerable<string> rawRuns, IEnumerable<string> identifiedRuns,
                                            IEnumerable<string> deNovoRuns)
        {
            List<string> Distinct(IEnumerable<string> names) => (names ?? Enumerable.Empty<string>())
                .Select(n => n ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<string> raw = Distinct(rawRuns);
            List<string> identified = Distinct(identifiedRuns);
            List<string> deNovo = Distinct(deNovoRuns);

            var rawSet = new HashSet<string>(raw, StringComparer.OrdinalIgnoreCase);
            var evidence = new HashSet<string>(identified.Concat(deNovo), StringComparer.OrdinalIgnoreCase);

            return new FullXicRunMatch
            {
                RawRuns = raw,
                IdentifiedRuns = identified,
                DeNovoRuns = deNovo,
                Matched = raw.Where(evidence.Contains).ToList(),
                RawWithoutEvidence = raw.Where(r => !evidence.Contains(r)).ToList(),
                IdentifiedWithoutRaw = identified.Where(r => !rawSet.Contains(r)).ToList(),
                DeNovoWithoutRaw = deNovo.Where(r => !rawSet.Contains(r)).ToList(),
            };
        }

        /// <summary>
        /// One line per unmatched RAW whose name extends, or is extended by, an unmatched run name, which
        /// is what a copy or download suffix produces ("x_01-003" for run "x_01"). Only a hint for the
        /// person renaming files: the computation never pairs on it.
        /// </summary>
        public IReadOnlyList<string> RenameHints()
        {
            var unmatchedEvidence = IdentifiedWithoutRaw.Concat(DeNovoWithoutRaw)
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var hints = new List<string>();
            foreach (string raw in RawWithoutEvidence)
            {
                string? run = unmatchedEvidence
                    .Where(e => raw.StartsWith(e, StringComparison.OrdinalIgnoreCase)
                             || e.StartsWith(raw, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.Length)
                    .ThenBy(e => e, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (run != null)
                    hints.Add("\"" + raw + ".raw\" looks like run \"" + run + "\": rename it to \"" + run + ".raw\".");
            }
            return hints;
        }

        /// <summary>The refusal shown when <see cref="Matched"/> is empty, or when
        /// <paramref name="restrictedTo"/> excluded every matched run.</summary>
        public string RefusalMessage(string rawDirectory, string identificationsName, bool deNovoGiven,
                                     IReadOnlyCollection<string>? restrictedTo = null)
        {
            if (Matched.Count > 0 && restrictedTo != null)
                return "Nothing was computed: none of the matched runs (" + Names(Matched) +
                       ") is among the runs this computation was restricted to (" + Names(restrictedTo) + ").";

            var sb = new System.Text.StringBuilder();
            sb.Append("Nothing was computed, so the project keeps the quantitation it already had: no .raw " +
                      "file in the RAW folder has the name of a run in the identifications" +
                      (deNovoGiven ? " or the kept de novo calls" : string.Empty) +
                      ". Runs are paired by file name (extensions such as .raw, .sqt and .yada are " +
                      "ignored), never by position.\n");
            sb.Append("\n.raw files in " + rawDirectory + ": " + Names(RawRuns));
            sb.Append("\nRuns in " + identificationsName + ": " + Names(IdentifiedRuns));
            if (deNovoGiven) sb.Append("\nRuns with kept de novo calls: " + Names(DeNovoRuns));

            IReadOnlyList<string> hints = RenameHints();
            if (hints.Count > 0) sb.Append("\n\n" + string.Join("\n", hints));

            sb.Append("\n\nNext step: " + (RawRuns.Count == 0
                ? "choose the folder that holds the .raw acquisitions themselves."
                : "give each .raw the name of its run (the name the search and the de novo export used), " +
                  "or choose the folder holding the acquisitions these identifications came from, then " +
                  "press Compute again."));
            return sb.ToString();
        }

        /// <summary>Warnings for a computation that does go ahead but leaves names unpaired, so a run
        /// missing from the result is never silent. Empty when every name pairs.</summary>
        public IReadOnlyList<string> MismatchNotes(string rawDirectory, string identificationsName)
        {
            var notes = new List<string>();
            if (IdentifiedWithoutRaw.Count > 0)
                notes.Add("WARNING: " + IdentifiedWithoutRaw.Count.ToString(CultureInfo.InvariantCulture) +
                    " run(s) in " + identificationsName + " have no .raw of the same name in " + rawDirectory +
                    " and were not quantified: " + Names(IdentifiedWithoutRaw));
            if (DeNovoWithoutRaw.Count > 0)
                notes.Add("WARNING: " + DeNovoWithoutRaw.Count.ToString(CultureInfo.InvariantCulture) +
                    " run(s) with kept de novo calls have no .raw of the same name in " + rawDirectory +
                    " and were not quantified: " + Names(DeNovoWithoutRaw));
            if (RawWithoutEvidence.Count > 0)
                notes.Add("WARNING: " + RawWithoutEvidence.Count.ToString(CultureInfo.InvariantCulture) +
                    " .raw file(s) match no run in the identifications or the kept de novo calls and were " +
                    "skipped: " + Names(RawWithoutEvidence));
            notes.AddRange(RenameHints());
            return notes;
        }

        private static string Names(IEnumerable<string> names)
        {
            List<string> list = names.Select(n => n.Length == 0 ? "(no file name)" : n).ToList();
            if (list.Count == 0) return "none";
            string shown = string.Join(", ", list.Take(MaxNamesListed));
            return list.Count <= MaxNamesListed
                ? shown
                : shown + " and " + (list.Count - MaxNamesListed).ToString(CultureInfo.InvariantCulture) + " more";
        }
    }

    /// <summary>Thrown by <see cref="FullXicComputer.ComputeFullXic"/> when no RAW pairs with anything to
    /// extract. Raised before any acquisition is opened, so nothing is computed and nothing is written; the
    /// message names the RAWs and runs on both sides.</summary>
    public sealed class FullXicNoMatchedRunsException : InvalidOperationException
    {
        public FullXicRunMatch Match { get; }

        public FullXicNoMatchedRunsException(FullXicRunMatch match, string message) : base(message)
        {
            Match = match;
        }
    }

    /// <summary>
    /// Re-extracts the full isotope envelope of every identified peptide (and, when a PEAKS export is
    /// given, every kept de novo precursor) of each run straight from its RAW, and collects the numbers
    /// into a per-peptide quant table plus a manifest. This is the compute half of phase Q2: the storage
    /// half is <see cref="ProjectStore"/>.
    ///
    /// IT ONLY MEASURES AND STORES. The fractions, partitions and family rollups that read these numbers
    /// are a later Q-phase and are deliberately NOT computed here: Q2's job is the raw per-peptide,
    /// per-isotope quant, the TIC and the parameters, nothing derived.
    ///
    /// ONE RUN AT A TIME. The MS1 list of a 3 GB RAW dominates memory, so runs are strictly sequential
    /// and the per-run result lists are released and collected before the next acquisition is opened.
    ///
    /// RUNS ARE MATCHED BY FILE NAME, never by position, through
    /// <see cref="DeNovoReconciler.NormalizeRunName"/>: a PSM list, a PEAKS export and a directory of
    /// RAWs are not in the same order, and extracting a precursor against the wrong acquisition returns
    /// a plausible wrong area.
    /// </summary>
    public static class FullXicComputer
    {
        /// <summary>The subfolder, beside the RAW files, the per-run extraction caches are written into
        /// when no cache-path override is given. Beside the acquisitions on purpose: the cache belongs to
        /// them, so a second computation over the same runs is answered without opening a 3 GB file
        /// again.</summary>
        public const string CacheFolderName = "familyquant_fullxic_cache";

        /// <summary>
        /// Computes the full XIC of every run that has both a RAW and something to extract.
        ///
        /// <paramref name="cachePathFor"/> maps (side, run name) to a cache file path, or is null for the
        /// default beside the RAW files. A SEAM for the harness (which already holds the caches it needs)
        /// and a legitimate hook for a project that keeps its caches elsewhere; the identified and de novo
        /// sides get SEPARATE cache files, because they credit a different isotope count and a cache is
        /// keyed by it.
        ///
        /// <paramref name="restrictToRuns"/>, when non-null, limits the computation to those normalized
        /// run names. Null processes every matched run, which is what the UI does; the harness restricts
        /// to one run so a real-data gate stays fast.
        /// </summary>
        public static FullXicComputeResult ComputeFullXic(
            string rawDirectory,
            string sepr2Path,
            string? deNovoDirectory,
            FullXicOptions options,
            Func<string, string, string>? cachePathFor = null,
            Action<string>? progress = null,
            IReadOnlyList<string>? restrictToRuns = null,
            Func<string, string>? detectedCachePathFor = null)
        {
            if (rawDirectory == null) throw new ArgumentNullException(nameof(rawDirectory));
            if (sepr2Path == null) throw new ArgumentNullException(nameof(sepr2Path));
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (!Directory.Exists(rawDirectory))
                throw new DirectoryNotFoundException("The RAW folder does not exist: " + rawDirectory);
            if (!File.Exists(sepr2Path))
                throw new FileNotFoundException("The SEPro package was not found: " + sepr2Path, sepr2Path);

            HashSet<string>? only = restrictToRuns == null
                ? null
                : new HashSet<string>(restrictToRuns.Select(DeNovoReconciler.NormalizeRunName),
                                      StringComparer.OrdinalIgnoreCase);

            // The RAWs, keyed by normalized run name. First one wins for a duplicated name, matching the
            // de novo screen's matcher.
            var rawByRun = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(rawDirectory, "*.raw", SearchOption.TopDirectoryOnly))
            {
                string run = DeNovoReconciler.NormalizeRunName(file);
                if (run.Length > 0 && !rawByRun.ContainsKey(run)) rawByRun[run] = file;
            }

            // The identified PSMs, grouped by run.
            progress?.Invoke("Reading identifications from " + Path.GetFileName(sepr2Path) + "...");
            List<PatternLabPsm> psms = SeproPsmAdapter.Load(sepr2Path);

            var psmsByRun = new Dictionary<string, List<PatternLabPsm>>(StringComparer.OrdinalIgnoreCase);
            foreach (PatternLabPsm p in psms)
            {
                string run = DeNovoReconciler.NormalizeRunName(p.FileName);
                if (!psmsByRun.TryGetValue(run, out List<PatternLabPsm>? list))
                    psmsByRun[run] = list = new List<PatternLabPsm>();
                list.Add(p);
            }

            // The kept de novo rows, grouped by run, when a PEAKS export was given. Imported, filtered
            // and reconciled ONCE, exactly as the de novo screen does it, then split by run.
            IReadOnlyList<DeNovoSourceSelection> deNovoSelections = options.ResolveDeNovoSelections();

            KeptDeNovoResult deNovoImport = deNovoDirectory == null
                ? new KeptDeNovoResult()
                : KeptDeNovoByRun(deNovoDirectory, psms, options.DeNovoCriteria, deNovoSelections, progress);
            Dictionary<string, List<PeaksDeNovoRow>> deNovoByRun = deNovoImport.ByRun;
            var rawPrecursorMetadata = new List<RawScanPrecursorMetadata>();

            // The runs to process: any run with a RAW and either identifications or kept de novo rows,
            // in name order so a run is never paired by position and two computations report the same way.
            // None at all is a refusal, raised before any RAW is opened or any file is written: a "success"
            // with zero rows would replace the project's quantitation with nothing.
            string identificationsName = Path.GetFileName(sepr2Path);
            FullXicRunMatch match = FullXicRunMatch.Build(rawByRun.Keys, psmsByRun.Keys, deNovoByRun.Keys);
            var runs = match.Matched
                .Where(r => only == null || only.Contains(r))
                .ToList();

            if (runs.Count == 0)
                throw new FullXicNoMatchedRunsException(match,
                    match.RefusalMessage(rawDirectory, identificationsName, deNovoDirectory != null, only));

            var result = new FullXicComputeResult();
            result.Notes.AddRange(match.MismatchNotes(rawDirectory, identificationsName));
            DateTime now = DateTime.UtcNow;

            var manifest = new QuantManifest
            {
                FormatVersion = ProjectStore.CurrentFormatVersion,
                IsotopeCount = options.IsotopeCount,
                MinPointsForQuant = options.MinPointsForQuant,
                Ppm = options.Ppm,
                Ms1Tolerance = options.Ms1Tolerance,
                AppVersion = options.AppVersion ?? string.Empty,
                CreatedUtc = now,
                UpdatedUtc = now,
                Provenance = QuantManifest.DefaultProvenance(),
                DeNovoOverlap = deNovoImport.Overlap,
                DeNovoScoreDistribution = deNovoImport.ScoreDistribution,
                DeNovoIdentification = deNovoImport.IdentificationSummary,
                IdentificationConcordance = deNovoImport.IdentificationConcordance,
                DeNovoInputs = deNovoImport.Inputs,
                DeNovoSourceScoreDistributions = deNovoImport.SourceScoreDistributions,
                DeNovoCallerAgreement = deNovoImport.CallerAgreement,
                DeNovoSourceVenn = deNovoImport.SourceVenn,
                DeNovoUnionResolutionMethod = deNovoSelections.Count > 1
                    ? DeNovoUnionResolver.Method : string.Empty,
            };
            manifest.ScientificInputFingerprints[Path.GetFullPath(sepr2Path)] =
                FileFingerprint.Sha256(sepr2Path);
            result.Manifest = manifest;

            foreach (string run in runs)
            {
                string rawPath = rawByRun[run];
                progress?.Invoke(run + ": fingerprinting RAW input...");
                string rawFingerprint = FileFingerprint.Sha256(rawPath);
                manifest.ScientificInputFingerprints[Path.GetFullPath(rawPath)] = rawFingerprint;
                bool ticRecorded = false;

                // Instrument-selected precursor metadata is the audit reference. Source-reported charge
                // and m/z remain in their own columns and are never relabelled as RAW truth.
                try
                {
                    IEnumerable<int> scans = (psmsByRun.TryGetValue(run,
                            out List<PatternLabPsm>? metadataPsms)
                            ? metadataPsms.Select(p => p.ScanNumber) : Enumerable.Empty<int>())
                        .Concat(deNovoImport.AllSourceRows.Where(r => string.Equals(
                            DeNovoReconciler.NormalizeRunName(r.RunName), run,
                            StringComparison.OrdinalIgnoreCase)).Select(r => r.Scan));
                    rawPrecursorMetadata.AddRange(
                        RawScanChargeReader.ReadMetadata(rawPath, scans).Values);
                }
                catch (Exception ex)
                {
                    result.Notes.Add(run + " RAW precursor metadata could not be read (" +
                        ex.GetType().Name + ": " + ex.Message +
                        "); the spectrum table labels source-reported charge/mz as a fallback.");
                }

                // ---- identified side ----------------------------------------------------------------
                if (psmsByRun.TryGetValue(run, out List<PatternLabPsm>? runPsms) && runPsms.Count > 0)
                {
                    var opt = new DeNovoXicOptions
                    {
                        IsotopeCount = options.IsotopeCount,
                        MinPointsForQuant = options.MinPointsForQuant,
                        Ppm = options.Ppm,
                        Ms1Tolerance = options.Ms1Tolerance,
                        CachePath = CachePath(cachePathFor, "identified", run, rawDirectory),
                        SourceFingerprint = rawFingerprint,
                        Progress = progress,
                    };

                    progress?.Invoke(run + ": extracting " +
                        runPsms.Count.ToString("N0", CultureInfo.InvariantCulture) + " identified PSM(s)...");

                    var (results, stats) = IdentifiedQuantExtractor.ExtractForRun(rawPath, runPsms, opt);

                    List<QuantRow> rows = QuantRow.FromResults("identified", run, results);
                    QuantRow.AttachPatternLabScores(rows, runPsms);
                    result.Rows.AddRange(rows);

                    manifest.PerRunTic[run] = stats.TicArea;
                    ticRecorded = true;

                    result.Notes.Add(run + " identified: " +
                        rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " row(s) with signal of " +
                        stats.UniquePrecursors.ToString("N0", CultureInfo.InvariantCulture) + " unique precursor(s)");
                }

                // ---- de novo side -------------------------------------------------------------------
                if (deNovoByRun.TryGetValue(run, out List<PeaksDeNovoRow>? runDeNovo) && runDeNovo.Count > 0)
                {
                    var opt = new DeNovoXicOptions
                    {
                        IsotopeCount = options.IsotopeCount,
                        MinPointsForQuant = options.MinPointsForQuant,
                        Ppm = options.Ppm,
                        Ms1Tolerance = options.Ms1Tolerance,
                        CachePath = CachePath(cachePathFor, "denovo", run, rawDirectory),
                        SourceFingerprint = rawFingerprint,
                        Progress = progress,
                    };

                    progress?.Invoke(run + ": extracting " +
                        runDeNovo.Count.ToString("N0", CultureInfo.InvariantCulture) + " kept de novo row(s)...");

                    var (results, _) = DeNovoBatchExtractor.ExtractForRun(rawPath, runDeNovo, opt);

                    List<QuantRow> rows = QuantRow.FromResults("denovo", run, results);
                    QuantRow.AttachDeNovoEvidence(rows, runDeNovo);
                    result.Rows.AddRange(rows);

                    result.Notes.Add(run + " de novo: " +
                        rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " row(s) with signal");
                }

                // A run with de novo rows but no identifications still needs its TIC recorded, so the
                // denominator every fraction is taken against is present for every run in the store.
                if (!ticRecorded)
                    manifest.PerRunTic[run] = DeNovoXicExtractor.GetTotalIonCurrentArea(rawPath, 0, 0);

                // ---- Q6: independent resolved isotope-series diagnostic ------------------------------
                // Walk the run's MS1 map once more to compute a detector diagnostic. It is not a peptide-
                // FDR-calibrated biological fraction and therefore does not close a physical TIC partition.
                // It loads the RAW again, so the result is cached using content and parameter provenance.
                // A failure here must not lose the per-peptide quant already measured, so it is caught and
                // noted rather than thrown.
                try
                {
                    string detectedCache = detectedCachePathFor != null
                        ? detectedCachePathFor(run)
                        : Path.Combine(rawDirectory, CacheFolderName, "ms1envelope_" + SafeFileName(run) + ".json");

                    Ms1EnvelopeDetectionResult det = Ms1EnvelopeDetector.DetectRun(
                        rawPath, detectedCache, progress, rawFingerprint);
                    result.Detected[run] = det;

                    manifest.PerRunTic.TryGetValue(run, out double runTic);
                    result.Notes.Add(run + " resolved isotope-series diagnostic, identified or not (NOT the share " +
                        "explained; not peptide-FDR calibrated): " +
                        det.DetectedEnvelopeCount.ToString("N0", CultureInfo.InvariantCulture) + " envelope(s), area " +
                        det.DetectedPeptideLikeArea.ToString("E4", CultureInfo.InvariantCulture) +
                        (runTic > 0
                            ? " (" + (det.DetectedPeptideLikeArea / runTic).ToString("P2", CultureInfo.InvariantCulture) + " of TIC)"
                            : string.Empty));
                }
                catch (Exception ex)
                {
                    result.Notes.Add(run + " resolved isotope-series diagnostic did not run (" +
                        ex.GetType().Name + ": " + ex.Message + "); the per-peptide quant is unaffected.");
                }

                // The MS1 list of the run just finished is the biggest allocation in the process. Let it
                // go before the next run's is loaded, so peak memory is one run and not two.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            // Q3: derive the fractions of the TIC from the raw per-peptide quant and record them in the
            // manifest, so the paper numbers persist in the .fqproj. The raw rows stay the source of
            // truth; this only reads them. The dedup collapse (one max-M+0 area per sequence, charge and
            // run) is what keeps every fraction below 100%.
            NativeQuantFractions.PopulateManifest(manifest, result.Rows);

            // Persist the exact reference-envelope model diagnostics beside the derived recovery
            // fractions. A reopened project can therefore reproduce the diagnostics PNG/CSV without
            // reopening RAW files or silently refitting a different reference set.
            manifest.ReferenceEnvelopeModelDiagnostics = ReferenceEnvelopeEstimator.Evaluate(
                NativeQuantFractions.DedupToPeptides(result.Rows));

            // The complete table is built while every source call and the RAW-selected precursor
            // metadata are still available, then persisted in the project. Rebuilding from QuantRow
            // alone would lose rejected audit calls and accepted calls that produced no stored XIC.
            PeptideFamilyAttribution? familyAttribution = null;
            if (options.FamilyCuration != null)
            {
                try
                {
                    familyAttribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(
                        result.Rows, options.FamilyCuration);
                    manifest.FamilyMapping = FamilyEnvelopeQuant.ProvenanceFor(options.FamilyCuration,
                        familyAttribution.IdentityThreshold);
                    result.Notes.Add("Spectrum-table family attribution: every matching User family at " +
                        familyAttribution.IdentityThreshold.ToString("P0", CultureInfo.InvariantCulture) +
                        " homology identity; shared peptides retain all memberships.");
                }
                catch (Exception ex)
                {
                    result.Notes.Add("WARNING: family attribution for the spectrum table did not run (" +
                        ex.GetType().Name + ": " + ex.Message + "). Quantitation completed; the table " +
                        "marks family mapping as unavailable and the project curation remains intact.");
                }
            }
            result.SpectrumComparisonRows = SpectrumComparisonTable.Build(
                deNovoImport.AllSourceRows.Where(r => !string.Equals(r.SourceId,
                    DeNovoSource.DeepNovo.Id, StringComparison.OrdinalIgnoreCase)),
                deNovoImport.AllSourceRows.Where(r => string.Equals(r.SourceId,
                    DeNovoSource.DeepNovo.Id, StringComparison.OrdinalIgnoreCase)),
                psms, result.Rows, familyAttribution: familyAttribution,
                rawPrecursors: rawPrecursorMetadata,
                perRunTic: manifest.PerRunTic,
                acceptedDeNovoRows: deNovoImport.AcceptedSourceRows);
            manifest.SpectrumComparisonRows = result.SpectrumComparisonRows;
            manifest.DeNovoAcceptedScoreScatter = DeNovoAcceptedScoreScatterStudy.Build(
                result.SpectrumComparisonRows, manifest.DeNovoSourceScoreDistributions);
            if (options.ExportSpectrumComparisonCsv)
            {
                string tablePath = string.IsNullOrWhiteSpace(options.SpectrumComparisonCsvPath)
                    ? Path.Combine(rawDirectory, SpectrumComparisonTable.DefaultFileName)
                    : options.SpectrumComparisonCsvPath!;
                result.TryExportSpectrumComparisonCsv(tablePath);
            }

            // Q6: fold the detected peptide-like estimate into the manifest's derived section (measured
            // area, derived fraction), so the .fqproj carries the middle level of the decomposition beside
            // the assigned fractions it sits above.
            NativeQuantFractions.ApplyDetectedToManifest(manifest, result.Detected);

            // The identified-but-not-quantified block, named in the status line even when it is zero: a
            // reader has to be able to tell "nothing was excluded" from "exclusions were not reported".
            foreach (DerivedRunFraction d in manifest.DerivedFractions)
            {
                if (d.Grouped) continue;
                double under = d.IdentifiedUnderSampled + d.DeNovoUnderSampled;
                result.Notes.Add(d.Run + " identified but not quantified (minimum " +
                    options.MinPointsForQuant.ToString(CultureInfo.InvariantCulture) +
                    " point(s) per peak): " +
                    (d.IdentifiedUnderSampledPeptides + d.DeNovoUnderSampledPeptides)
                        .ToString("N0", CultureInfo.InvariantCulture) + " peptide(s), area " +
                    under.ToString("E4", CultureInfo.InvariantCulture) +
                    (d.Tic > 0
                        ? " (" + d.UnderSampledFraction.ToString("P4", CultureInfo.InvariantCulture) + " of TIC)"
                        : string.Empty) +
                    (d.UnderSampledIsEstimate ? " - includes an ESTIMATE for single-point clusters" : string.Empty));
            }

            result.TicShareSummary = DescribeTicShares(manifest.DerivedFractions);

            manifest.UpdatedUtc = DateTime.UtcNow;
            return result;
        }

        /// <summary>
        /// The share of the TIC explained, per run (and over all runs when there are several), in the
        /// language of SCIENTIFIC_METHOD.md: the identified observed envelope, plus, when de novo rows
        /// were quantified, the de novo reference-calibrated estimate and their sum (the primary
        /// estimate). The last line says what the resolved isotope-series diagnostic is, so the two
        /// percentages cannot be confused. Empty when there is no run to describe.
        /// </summary>
        public static List<string> DescribeTicShares(IEnumerable<DerivedRunFraction>? fractions)
        {
            var lines = new List<string>();
            List<DerivedRunFraction> all = (fractions ?? Enumerable.Empty<DerivedRunFraction>())
                .Where(d => d != null).ToList();
            List<DerivedRunFraction> runs = all.Where(d => !d.Grouped).ToList();
            if (runs.Count == 0) return lines;

            static string P(double v) => v.ToString("P2", CultureInfo.InvariantCulture);
            bool deNovo = all.Any(d => d.DeNovoPeptides > 0 || d.DeNovoEnvelopeReferenceCalibrated > 0);

            lines.Add(deNovo
                ? "Share of the TIC explained by the quantified peptides (identified observed envelope + " +
                  "de novo reference-calibrated estimate, one area per peptide):"
                : "Share of the TIC explained by the identified peptides (full isotope envelope, one area " +
                  "per peptide):");

            IEnumerable<DerivedRunFraction> shown = runs.Count > 1 ? runs.Concat(all.Where(d => d.Grouped)) : runs;
            foreach (DerivedRunFraction d in shown)
            {
                string name = d.Grouped ? "all runs" : d.Run;
                if (d.Tic <= 0)
                {
                    lines.Add("  " + name + ": no TIC recorded, so no share can be given");
                    continue;
                }
                lines.Add("  " + name + ": " + (deNovo
                    ? "identified " + P(d.IdentifiedFractionEnvelope) + " + de novo " +
                      P(d.DeNovoFractionReferenceCalibrated) + " = " + P(d.RecoveredFractionReferenceCalibrated)
                    : "identified " + P(d.IdentifiedFractionEnvelope)));
            }

            List<double> detected = runs.Where(d => d.Tic > 0 && d.DetectedFractionPeptideLike > 0)
                .Select(d => d.DetectedFractionPeptideLike).ToList();
            if (detected.Count > 0)
            {
                string range = detected.Min() == detected.Max()
                    ? P(detected.Min())
                    : P(detected.Min()) + " to " + P(detected.Max());
                lines.Add("The resolved isotope-series diagnostic further down (" + range + " of the TIC) is how " +
                    "much of the TIC looks like peptide isotope series, identified or not. It is a reference " +
                    "for what could still be explained, not signal already explained.");
            }

            return lines;
        }

        /// <summary>Imports, filters and reconciles the PEAKS export once, keeps DeNovoOnly and
        /// CoIsolatedExtra (a redundant equality adds no ion current the identified side does not already
        /// hold), and groups the survivors by run. Mirrors the de novo screen exactly.</summary>
        private sealed class KeptDeNovoResult
        {
            public Dictionary<string, List<PeaksDeNovoRow>> ByRun { get; } =
                new Dictionary<string, List<PeaksDeNovoRow>>(StringComparer.OrdinalIgnoreCase);
            public DeNovoOverlapSummary? Overlap { get; set; }
            public DeNovoScoreDistribution? ScoreDistribution { get; set; }
            public DeNovoIdentificationSummary? IdentificationSummary { get; set; }
            public IdentificationConcordanceSummary? IdentificationConcordance { get; set; }
            public DeNovoAgreementResult? CallerAgreement { get; set; }
            public DeNovoSourceVennSummary? SourceVenn { get; set; }
            public List<DeNovoInputManifest> Inputs { get; } = new List<DeNovoInputManifest>();
            public List<DeNovoSourceScoreDistribution> SourceScoreDistributions { get; } =
                new List<DeNovoSourceScoreDistribution>();
            public List<PeaksDeNovoRow> AllSourceRows { get; } = new List<PeaksDeNovoRow>();
            public List<PeaksDeNovoRow> AcceptedSourceRows { get; } = new List<PeaksDeNovoRow>();
        }

        private static KeptDeNovoResult KeptDeNovoByRun(
            string deNovoDirectory, List<PatternLabPsm> psms, DeNovoFilterCriteria criteria,
            IReadOnlyList<DeNovoSourceSelection> selections, Action<string>? progress)
        {
            var result = new KeptDeNovoResult();
            if (selections == null || selections.Count == 0)
                selections = new[]
                {
                    new DeNovoSourceSelection(DeNovoSource.DenovoOnly,
                        criteria.MinAverageLocalConfidence, 0),
                };

            var filtered = new List<PeaksDeNovoRow>();
            var peaksDatabase = new List<PeaksDatabasePsm>();
            var rowsBySource = new Dictionary<string, List<PeaksDeNovoRow>>(StringComparer.OrdinalIgnoreCase);
            int rawRows = 0;

            // Each source is read with its own glob and its own confidence column, then concatenated: a
            // single source reproduces the baseline; a union (de novo all + DeepNovo) reads both globs
            // under the same tree, each with the right column, and pools the rows.
            foreach (DeNovoSourceSelection selection in selections)
            {
                DeNovoSource source = selection.Source;
                progress?.Invoke("Reading the " + source.DisplayName + " export (" + source.FileGlob +
                    ") under " + deNovoDirectory + "; " + selection.CutoffLabel + "...");

                if (!rowsBySource.TryGetValue(source.Id, out List<PeaksDeNovoRow>? sourceRows))
                    rowsBySource[source.Id] = sourceRows = new List<PeaksDeNovoRow>();

                foreach (string csv in DeNovoSourceFileDiscovery.RequireFiles(
                    deNovoDirectory, source))
                {
                    List<PeaksDeNovoRow> read = PeaksCsvReader.Read(csv, source);
                    List<PeaksDeNovoRow> accepted = DeNovoFilter.Apply(
                        read, criteria, selection.MinimumConfidence);
                    rawRows += read.Count;
                    sourceRows.AddRange(read);
                    result.AllSourceRows.AddRange(read);
                    filtered.AddRange(accepted);
                    result.Inputs.Add(new DeNovoInputManifest
                    {
                        SourceId = source.Id,
                        SourceDisplayName = source.DisplayName,
                        ConfidenceField = source.ConfidenceColumn,
                        MinimumConfidence = selection.MinimumConfidence,
                        SourcePriority = selection.Priority,
                        FilePath = Path.GetFullPath(csv),
                        Sha256 = read.FirstOrDefault()?.InputFileSha256 ?? FileFingerprint.Sha256(csv),
                        RawRows = read.Count,
                        RowsPassingSourceFilter = accepted.Count,
                    });
                }

                result.SourceScoreDistributions.Add(DeNovoSourceScoreDistributionStudy.Build(
                    sourceRows, selection, criteria));
            }

            // Canonicalize file provenance from the parsed rows themselves. The inline records above
            // retain empty-file provenance; this pass makes non-empty files authoritative and prevents a
            // partially populated input list from travelling beside a complete multi-source distribution.
            // The presentation validator remains fail-closed and will still reject a configured source
            // whose export is genuinely absent.
            RebuildObservedInputManifests(result, rowsBySource, selections, criteria);

            // A single source may use the legacy presentation record. In a union it remains the PRIMARY
            // source's distribution only: ALC and SCORE are never pooled into one histogram.
            DeNovoSourceSelection primary = selections.OrderBy(s => s.Priority).First();
            if (rowsBySource.TryGetValue(primary.Source.Id, out List<PeaksDeNovoRow>? primaryRows))
            {
                var primaryCriteria = new DeNovoFilterCriteria
                {
                    MinLength = criteria.MinLength,
                    MinAverageLocalConfidence = primary.MinimumConfidence,
                    StretchLength = criteria.StretchLength,
                    StretchFloor = criteria.StretchFloor,
                };
                result.ScoreDistribution = DeNovoScoreDistributionStudy.Build(primaryRows, primaryCriteria);
                result.ScoreDistribution.ScoreName = primary.Source.DisplayName + " " +
                    primary.Source.ConfidenceColumn;
            }

            // Caller agreement is deliberately computed from all rows present in the two source
            // exports, before the >=80 quantitation filters and before union resolution. That population
            // is stored with the result and labelled explicitly by the chart/export; it is a caller-
            // consistency audit, not an accuracy estimate and not the quantified-tier denominator.
            if (rowsBySource.TryGetValue(DeNovoSource.DeNovoAll.Id,
                    out List<PeaksDeNovoRow>? peaksAllRows)
                && rowsBySource.TryGetValue(DeNovoSource.DeepNovo.Id,
                    out List<PeaksDeNovoRow>? deepNovoRows))
            {
                rowsBySource.TryGetValue(DeNovoSource.DenovoOnly.Id,
                    out List<PeaksDeNovoRow>? deNovoOnlyRows);
                result.CallerAgreement = DeNovoAgreementStudy.Build(
                    peaksAllRows, deepNovoRows,
                    deNovoOnlyRows ?? new List<PeaksDeNovoRow>());
            }

            // Resolve after every source has passed its own cutoff. This yields exactly one sequence label
            // per extraction key while retaining all alternate source evidence on the winner.
            result.AcceptedSourceRows.AddRange(filtered);
            filtered = DeNovoUnionResolver.Resolve(filtered, selections);

            foreach (string csv in Directory
                .GetFiles(deNovoDirectory, "db.psms.csv", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal))
                peaksDatabase.AddRange(PeaksDatabasePsmReader.Read(csv));

            List<ReconciledRow> reconciliation = DeNovoReconciler.Reconcile(filtered, psms);
            result.Overlap = DeNovoOverlapStudy.Build(rawRows, reconciliation, criteria);
            result.IdentificationSummary = DeNovoIdentificationStudy.Build(filtered, reconciliation);
            result.IdentificationConcordance = IdentificationConcordanceStudy.Build(
                psms, peaksDatabase, filtered);

            // The two-source Venn is derived from this exact computation, never by subtracting the
            // independently run source variants. Tier A uses each caller's accepted source rows before
            // precursor union; tier B follows the source provenance attached to union-resolved rows that
            // survived PatternLab reconciliation as additional hypotheses.
            if (rowsBySource.TryGetValue(DeNovoSource.DenovoOnly.Id,
                    out List<PeaksDeNovoRow>? peaksOnlyRows)
                && rowsBySource.TryGetValue(DeNovoSource.DeepNovo.Id,
                    out List<PeaksDeNovoRow>? deepRows))
            {
                DeNovoSourceSelection? peaksSelection = selections.FirstOrDefault(s =>
                    string.Equals(s.Source.Id, DeNovoSource.DenovoOnly.Id,
                        StringComparison.OrdinalIgnoreCase));
                DeNovoSourceSelection? deepSelection = selections.FirstOrDefault(s =>
                    string.Equals(s.Source.Id, DeNovoSource.DeepNovo.Id,
                        StringComparison.OrdinalIgnoreCase));
                if (peaksSelection != null && deepSelection != null)
                {
                    result.SourceVenn = DeNovoSourceVennStudy.Build(peaksOnlyRows, deepRows,
                        criteria, peaksSelection.MinimumConfidence,
                        deepSelection.MinimumConfidence, reconciliation);
                }
            }

            string sourceScope = string.Join(" + ", selections.OrderBy(s => s.Priority)
                .Select(s => s.Source.DisplayName + " (" + s.Source.ConfidenceColumn + ")"));
            string filterDescription = "source-aware acceptance: " + string.Join("; ",
                selections.OrderBy(s => s.Priority).Select(s => s.Source.DisplayName + " " +
                    s.Source.ConfidenceColumn + " >= " +
                    s.MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture))) +
                "; peptide length >= " + criteria.MinLength.ToString(CultureInfo.InvariantCulture) +
                "; >= " + criteria.StretchLength.ToString(CultureInfo.InvariantCulture) +
                " consecutive local-confidence residues >= " +
                criteria.StretchFloor.ToString(CultureInfo.InvariantCulture) + "%";
            bool strictDeNovoOnly = selections.Count == 1
                && string.Equals(selections[0].Source.Id, DeNovoSource.DenovoOnly.Id,
                    StringComparison.OrdinalIgnoreCase);
            result.Overlap.SourceScopeLabel = sourceScope;
            result.Overlap.SourceFilterDescription = filterDescription;
            result.Overlap.IsStrictDeNovoOnlyScope = strictDeNovoOnly;
            result.IdentificationConcordance.DeNovoSourceScopeLabel = sourceScope;
            result.IdentificationConcordance.DeNovoFilterDescription = filterDescription;
            result.IdentificationConcordance.IsStrictDeNovoOnlyScope = strictDeNovoOnly;

            foreach (ReconciledRow row in reconciliation)
            {
                if (row.Verdict != ReconciliationVerdict.DeNovoOnly
                    && row.Verdict != ReconciliationVerdict.CoIsolatedExtra) continue;

                string run = DeNovoReconciler.NormalizeRunName(row.DeNovo.RunName);
                if (run.Length == 0) continue;

                if (!result.ByRun.TryGetValue(run, out List<PeaksDeNovoRow>? rows))
                    result.ByRun[run] = rows = new List<PeaksDeNovoRow>();
                rows.Add(row.DeNovo);
            }

            return result;
        }

        private static void RebuildObservedInputManifests(KeptDeNovoResult result,
            IReadOnlyDictionary<string, List<PeaksDeNovoRow>> rowsBySource,
            IReadOnlyList<DeNovoSourceSelection> selections, DeNovoFilterCriteria criteria)
        {
            List<DeNovoInputManifest> emptyFiles = result.Inputs
                .Where(i => i.RawRows == 0 && !string.IsNullOrWhiteSpace(i.FilePath))
                .ToList();
            var rebuilt = new List<DeNovoInputManifest>();

            foreach (DeNovoSourceSelection selection in selections.OrderBy(s => s.Priority))
            {
                if (!rowsBySource.TryGetValue(selection.Source.Id,
                        out List<PeaksDeNovoRow>? sourceRows))
                    continue;

                foreach (IGrouping<string, PeaksDeNovoRow> fileRows in sourceRows
                    .Where(r => !string.IsNullOrWhiteSpace(r.InputFilePath))
                    .GroupBy(r => Path.GetFullPath(r.InputFilePath),
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    List<PeaksDeNovoRow> rows = fileRows.ToList();
                    if (rows.Any(r => !string.Equals(r.SourceId, selection.Source.Id,
                        StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("Parsed de novo rows from " + fileRows.Key +
                            " do not all carry the configured source id " + selection.Source.Id + ".");

                    rebuilt.Add(new DeNovoInputManifest
                    {
                        SourceId = selection.Source.Id,
                        SourceDisplayName = selection.Source.DisplayName,
                        ConfidenceField = selection.Source.ConfidenceColumn,
                        MinimumConfidence = selection.MinimumConfidence,
                        SourcePriority = selection.Priority,
                        FilePath = fileRows.Key,
                        Sha256 = rows.Select(r => r.InputFileSha256)
                            .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h))
                            ?? FileFingerprint.Sha256(fileRows.Key),
                        RawRows = rows.Count,
                        RowsPassingSourceFilter = rows.Count(r => DeNovoFilter.Passes(
                            r, criteria, selection.MinimumConfidence)),
                    });
                }

                rebuilt.AddRange(emptyFiles.Where(i => string.Equals(i.SourceId,
                    selection.Source.Id, StringComparison.OrdinalIgnoreCase)));
            }

            result.Inputs.Clear();
            result.Inputs.AddRange(rebuilt
                .GroupBy(i => (i.SourceId ?? string.Empty) + "\u001f" +
                    Path.GetFullPath(i.FilePath), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(i => i.SourcePriority)
                .ThenBy(i => i.FilePath, StringComparer.OrdinalIgnoreCase));
        }

        private static string CachePath(Func<string, string, string>? cachePathFor, string side, string run,
            string rawDirectory)
        {
            if (cachePathFor != null) return cachePathFor(side, run);

            string safe = SafeFileName(run);
            return Path.Combine(rawDirectory, CacheFolderName, "fullxic_" + side + "_" + safe + ".json");
        }

        private static string SafeFileName(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "run" : sb.ToString();
        }
    }
}
