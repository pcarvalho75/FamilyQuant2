using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// One normalized acquisition / MS2 scan / compatible reported precursor hypothesis. Charge must
    /// agree and available source m/z values must fall within the non-chaining, isotope-error-aware
    /// 20-ppm opening-m/z cluster: the M, M+-1 and M+-2 monoisotopic-peak selections of one precursor
    /// share this row, while a genuinely different co-isolated neighbour stays separate. RAW-selected
    /// charge/m/z are diagnostic columns rather than a forced join key: reaction m/z may describe the
    /// isolation centre and one scan can contain alternative/co-isolated reported hypotheses.
    /// Quantification is shared only inside this compatible precursor row.
    /// </summary>
    public sealed class SpectrumComparisonRow
    {
        public string FileName { get; set; } = string.Empty;
        public string NormalizedRunName { get; set; } = string.Empty;
        public int ScanNumber { get; set; }
        public int PrecursorHypothesisIndex { get; set; }
        public int Charge { get; set; }
        public double PrecursorMz { get; set; }
        public double PrecursorMzMin { get; set; }
        public double PrecursorMzMax { get; set; }
        public int? RawPrecursorCharge { get; set; }
        public double? RawPrecursorMz { get; set; }
        public bool RawChargeKnown { get; set; }
        public bool RawPrecursorMzKnown { get; set; }
        public bool RawPrecursorKnown => RawChargeKnown || RawPrecursorMzKnown;
        public int CanonicalCharge { get; set; }
        public double CanonicalPrecursorMz { get; set; }
        public string PrecursorKeyBasis { get; set; } = string.Empty;

        public string PeaksDeNovoSequence { get; set; } = string.Empty;
        public string PeaksDeNovoCleanSequence { get; set; } = string.Empty;
        public double? PeaksDeNovoScore { get; set; }
        public string PeaksDeNovoScoreField { get; set; } = string.Empty;
        public string PeaksDeNovoSource { get; set; } = string.Empty;
        public double? PeaksDeNovoMz { get; set; }
        public int? PeaksDeNovoCharge { get; set; }
        public bool? PeaksDeNovoMatchesRawPrecursor { get; set; }
        public bool? PeaksDeNovoAccepted { get; set; }
        public int PeaksDeNovoCandidateCount { get; set; }
        public int PeaksDeNovoAcceptedCandidateCount { get; set; }
        public string PeaksDeNovoAlternateSequences { get; set; } = string.Empty;

        public string DeepNovoSequence { get; set; } = string.Empty;
        public string DeepNovoCleanSequence { get; set; } = string.Empty;
        public double? DeepNovoScore { get; set; }
        public string DeepNovoScoreField { get; set; } = string.Empty;
        public double? DeepNovoMz { get; set; }
        public int? DeepNovoCharge { get; set; }
        public bool? DeepNovoMatchesRawPrecursor { get; set; }
        public bool? DeepNovoAccepted { get; set; }
        public int DeepNovoCandidateCount { get; set; }
        public int DeepNovoAcceptedCandidateCount { get; set; }
        public string DeepNovoAlternateSequences { get; set; } = string.Empty;

        public string PatternLabSequence { get; set; } = string.Empty;
        public string PatternLabCleanSequence { get; set; } = string.Empty;
        public double? PatternLabXScore { get; set; }
        public double? PatternLabMz { get; set; }
        public int? PatternLabCharge { get; set; }
        public bool? PatternLabMatchesRawPrecursor { get; set; }
        public int PatternLabCandidateCount { get; set; }
        public string PatternLabAlternateSequences { get; set; } = string.Empty;

        /// <summary>Direct, observed monoisotopic XIC for the one selected quant record. Null means that
        /// this source-level precursor hypothesis did not yield a stored quant row.</summary>
        public double? QuantM0Observed { get; set; }

        /// <summary>Directly credited measured envelope, retained as a diagnostic beside the primary
        /// assigned value. It is not substituted for the reference-model value on a de novo row.</summary>
        public double? QuantEnvelopeObserved { get; set; }

        /// <summary>The value on the presentation's primary assignment basis: observed credited
        /// envelope for search identifications; de novo M0 multiplied by the identified-reference ratio
        /// in the same fixed neutral-mass bin for de novo-only precursors.</summary>
        public double? QuantPrimaryAssigned { get; set; }

        /// <summary>Alias carrying the user-requested CSV field name. It is exactly
        /// <see cref="QuantPrimaryAssigned"/>, never PatternLab's PrimaryScore (which is XCorr).</summary>
        public double? PrimaryQuantValue
        {
            get => QuantPrimaryAssigned;
            set => QuantPrimaryAssigned = value;
        }

        public string QuantPrimaryBasis { get; set; } = string.Empty;
        public string QuantSide { get; set; } = string.Empty;
        public string QuantSequence { get; set; } = string.Empty;
        public string QuantCleanSequence { get; set; } = string.Empty;
        public double? QuantMz { get; set; }
        public int? QuantCharge { get; set; }
        public int QuantCandidateCount { get; set; }
        public bool QuantValuesAgree { get; set; }

        /// <summary>Legacy single-family convenience field. It is populated only when the peptide maps
        /// to exactly one curated User family; null avoids choosing an arbitrary first family for a
        /// genuinely many-to-many attribution.</summary>
        public int? FamilyUserId { get; set; }

        /// <summary>Every curated User family reached by the quantified peptide, ascending and separated
        /// by semicolons. This is the machine-readable many-to-many field used by the CSV/DataGrid.</summary>
        public string FamilyUserIds { get; set; } = string.Empty;

        /// <summary>Every family id plus its target-derived description, in the same order as
        /// <see cref="FamilyUserIds"/>.</summary>
        public string FamilyAssignments { get; set; } = string.Empty;

        /// <summary>Backward-compatible display alias. It now carries the complete joined assignment,
        /// not an arbitrary single family.</summary>
        public string FamilyAssignment { get; set; } = string.Empty;
        public double? FamilyMappingMinimumIdentity { get; set; }
        public string FamilyMappingStatus { get; set; } = string.Empty;
        public double? RunTic { get; set; }
        public double? PooledTic { get; set; }
        public bool QuantCreditedInPrimaryTotals { get; set; }
        public string QuantCreditReason { get; set; } = string.Empty;

        public string PrecursorAgreement { get; set; } = string.Empty;
        public bool SameScanDifferentPrecursorHypothesis { get; set; }
        public string JoinStatus { get; set; } = string.Empty;

        /// <summary>The largest-magnitude signed C13 isotope-selection offset that any member of this
        /// precursor cluster needed to join it: 0 when every reported m/z agreed directly within tolerance,
        /// negative or positive when a +-1/+-2 monoisotopic-peak selection error was corrected first. The
        /// reported m/z themselves are never overwritten; this is the auditable record of the correction.</summary>
        public int PrecursorIsotopeOffsetSelected { get; set; }

        /// <summary>"direct" when the precursor cluster was formed on the reported m/z within tolerance,
        /// "isotope-corrected" when at least one member joined only after a +-1/+-2 selection error was
        /// removed.</summary>
        public string PrecursorMatchBasis { get; set; } = string.Empty;
    }

    /// <summary>
    /// Builds and exports the spectrum-level comparison table. The join is deliberately not a relational
    /// all-against-all join: observations are partitioned by normalized run, scan, equal reported charge,
    /// and the same non-transitive, isotope-error-aware 20-ppm opening-m/z rule as the de novo union, so
    /// the M, M+-1 and M+-2 selections of one precursor share a row and a real co-isolated neighbour does
    /// not. RAW precursor metadata annotate those rows but never merge incompatible source hypotheses. One
    /// deterministic top call per source is displayed and alternate calls are counted/listed. When PEAKS
    /// and DeepNovo report different sequences for one isotope-equivalent precursor both sequence claims
    /// are kept and shown, yet exactly one precursor/XIC/quant value is shared, and the exported
    /// PrecursorIsotopeOffsetSelected / PrecursorMatchBasis columns keep the correction auditable.
    /// </summary>
    public static partial class SpectrumComparisonTable
    {
        public const string DefaultFileName = "spectrum_evidence_comparison.csv";

        private const string CsvHeader =
            "ScanNumber,FileName,PeaksDeNovoSequence,DeepNovoSequence,PatternLabSequence,PeaksDeNovoScore,DeepNovoScore,PatternLabXScore,PrimaryQuantValue,Charge,PrecursorMz,PrecursorHypothesisIndex,PrecursorMzMin,PrecursorMzMax,RawPrecursorCharge,RawPrecursorMz,RawChargeKnown,RawPrecursorMzKnown,CanonicalCharge,CanonicalPrecursorMz,PrecursorKeyBasis,PeaksDeNovoCleanSequence,DeepNovoCleanSequence,PatternLabCleanSequence,PeaksDeNovoScoreField,DeepNovoScoreField,PeaksDeNovoSource,PeaksDeNovoCharge,PeaksDeNovoMz,PeaksDeNovoMatchesRawPrecursor,PeaksDeNovoAccepted,DeepNovoCharge,DeepNovoMz,DeepNovoMatchesRawPrecursor,DeepNovoAccepted,PatternLabCharge,PatternLabMz,PatternLabMatchesRawPrecursor,PeaksDeNovoCandidateCount,PeaksDeNovoAcceptedCandidateCount,DeepNovoCandidateCount,DeepNovoAcceptedCandidateCount,PatternLabCandidateCount,PeaksDeNovoAlternateSequences,DeepNovoAlternateSequences,PatternLabAlternateSequences,QuantM0Observed,QuantEnvelopeObserved,QuantPrimaryAssigned,QuantPrimaryBasis,QuantSide,QuantSequence,QuantCleanSequence,QuantCharge,QuantMz,QuantCandidateCount,QuantValuesAgree,FamilyUserId,FamilyUserIds,FamilyAssignments,FamilyAssignment,FamilyMappingMinimumIdentity,FamilyMappingStatus,RunTic,PooledTic,QuantCreditedInPrimaryTotals,QuantCreditReason,PrecursorAgreement,SameScanDifferentPrecursorHypothesis,JoinStatus,PrecursorIsotopeOffsetSelected,PrecursorMatchBasis";
        public const string Grain =
            "normalized-run + MS2-scan + equal-reported-charge + " +
            "non-chaining isotope-error-aware 20-ppm opening-m/z cluster (M, M+-1, M+-2 of one precursor " +
            "share a row); RAW precursor metadata are diagnostic";
        public const string PatternLabScoreDefinition =
            "PatternLab XScore is SEPro PSMSearchResult.PrimaryScore (XCorr); it is not quantitation";

        private enum ClaimKind { Peaks, DeepNovo, PatternLab, Quant }

        private sealed class Claim
        {
            public ClaimKind Kind { get; init; }
            public string Run { get; init; } = string.Empty;
            public int Scan { get; init; }
            public int Charge { get; init; }
            public double Mz { get; init; }
            public string Sequence { get; init; } = string.Empty;
            public double? Score { get; init; }
            public string ScoreField { get; init; } = string.Empty;
            public string SourceId { get; init; } = string.Empty;
            public string SourceDisplayName { get; init; } = string.Empty;
            public QuantRow? Quant { get; init; }
            public bool IsExternalSourceObservation { get; init; }
            public bool? AcceptedByConfiguredFilters { get; init; }
        }

        private sealed class Cluster
        {
            public string Run { get; init; } = string.Empty;
            public int Scan { get; init; }
            public int Charge { get; init; }
            public double OpeningMz { get; init; }
            public List<Claim> Claims { get; } = new List<Claim>();

            /// <summary>The largest-magnitude signed C13 isotope-selection offset any member needed to join
            /// this precursor cluster relative to its opening m/z. Zero when every member agreed on the
            /// reported m/z directly within tolerance.</summary>
            public int SelectedIsotopeOffset { get; set; }

            /// <summary>True when at least one member joined only after a +-1/+-2 monoisotopic-peak
            /// selection error was removed, so the row can label its precursor match basis.</summary>
            public bool IsotopeCorrected { get; set; }
        }

        /// <summary>Builds what a reopened .fqproj can show from its persisted quant rows. New projects
        /// retain PatternLab XCorr and all source evidence; older projects show blanks for fields they did
        /// not store rather than guessing them.</summary>
        public static List<SpectrumComparisonRow> Build(IEnumerable<QuantRow> quantRows) =>
            Build(Array.Empty<PeaksDeNovoRow>(), Array.Empty<PeaksDeNovoRow>(),
                Array.Empty<PatternLabPsm>(), quantRows);

        /// <summary>
        /// Rebuilds the complete table from a stored quant table plus its original scientific inputs,
        /// without reopening XICs or re-extracting signal. Each caller is filtered on its own configured
        /// confidence scale; RAW files are opened only for selected-precursor charge/m/z metadata.
        /// </summary>
        public static List<SpectrumComparisonRow> BuildFromInputs(
            string rawDirectory, string sepr2Path, string deNovoDirectory,
            IEnumerable<QuantRow> quantRows, IReadOnlyList<DeNovoSourceSelection> selections,
            DeNovoFilterCriteria criteria, Action<string>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(rawDirectory))
                throw new ArgumentException("A RAW/data directory is required.", nameof(rawDirectory));
            if (string.IsNullOrWhiteSpace(sepr2Path))
                throw new ArgumentException("A SEPro package is required.", nameof(sepr2Path));
            if (string.IsNullOrWhiteSpace(deNovoDirectory))
                throw new ArgumentException("A de novo export directory is required.", nameof(deNovoDirectory));
            if (quantRows == null) throw new ArgumentNullException(nameof(quantRows));
            if (selections == null || selections.Count == 0)
                throw new ArgumentException("At least one de novo source selection is required.",
                    nameof(selections));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            var peaks = new List<PeaksDeNovoRow>();
            var deep = new List<PeaksDeNovoRow>();
            var acceptedRows = new List<PeaksDeNovoRow>();
            foreach (DeNovoSourceSelection selection in selections.OrderBy(s => s.Priority))
            {
                foreach (string path in DeNovoSourceFileDiscovery.RequireFiles(
                    deNovoDirectory, selection.Source))
                {
                    progress?.Invoke("Reading " + path);
                    List<PeaksDeNovoRow> read = PeaksCsvReader.Read(path, selection.Source);
                    List<PeaksDeNovoRow> accepted = DeNovoFilter.Apply(read, criteria,
                        selection.MinimumConfidence);
                    acceptedRows.AddRange(accepted);
                    if (string.Equals(selection.Source.Id, DeNovoSource.DeepNovo.Id,
                        StringComparison.OrdinalIgnoreCase)) deep.AddRange(read);
                    else peaks.AddRange(read);
                }
            }

            progress?.Invoke("Reading PatternLab identifications from " + sepr2Path);
            List<PatternLabPsm> psms = SeproPsmAdapter.Load(sepr2Path);
            var scansByRun = peaks.Cast<PeaksDeNovoRow>().Concat(deep)
                .Select(r => (run: DeNovoReconciler.NormalizeRunName(r.RunName), scan: r.Scan))
                .Concat(psms.Select(p => (run: DeNovoReconciler.NormalizeRunName(p.FileName),
                                          scan: p.ScanNumber)))
                .GroupBy(x => x.run, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.scan).Distinct().ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var raw = new List<RawScanPrecursorMetadata>();
            foreach (string path in Directory.GetFiles(rawDirectory, "*.raw",
                SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string run = DeNovoReconciler.NormalizeRunName(path);
                if (!scansByRun.TryGetValue(run, out List<int>? scans)) continue;
                try
                {
                    progress?.Invoke("Reading RAW precursor metadata from " + path);
                    raw.AddRange(RawScanChargeReader.ReadMetadata(path, scans).Values);
                }
                catch (Exception ex)
                {
                    progress?.Invoke("RAW metadata unavailable for " + run + ": " + ex.Message);
                }
            }

            return Build(peaks, deep, psms, quantRows, rawPrecursors: raw,
                acceptedDeNovoRows: acceptedRows);
        }

        public static string SaveFromInputs(
            string outputPath, string rawDirectory, string sepr2Path, string deNovoDirectory,
            IEnumerable<QuantRow> quantRows, IReadOnlyList<DeNovoSourceSelection> selections,
            DeNovoFilterCriteria criteria, Action<string>? progress = null) =>
            SaveCsv(outputPath, BuildFromInputs(rawDirectory, sepr2Path, deNovoDirectory,
                quantRows, selections, criteria, progress));

        /// <summary>
        /// Builds the complete compute-time table. The two de novo inputs should be the source-filtered
        /// observations before union winner selection; this preserves alternative caller sequences while
        /// still assigning no more than one quantity to their precursor cluster.
        /// </summary>
        public static List<SpectrumComparisonRow> Build(
            IEnumerable<PeaksDeNovoRow> peaksDeNovoRows,
            IEnumerable<PeaksDeNovoRow> deepNovoRows,
            IEnumerable<PatternLabPsm> patternLabPsms,
            IEnumerable<QuantRow> quantRows,
            IReadOnlyDictionary<int, string>? familyLabels = null,
            FamilyQuant.PeptideFamilyAttribution? familyAttribution = null,
            IEnumerable<RawScanPrecursorMetadata>? rawPrecursors = null,
            IReadOnlyDictionary<string, double>? perRunTic = null,
            IEnumerable<PeaksDeNovoRow>? acceptedDeNovoRows = null,
            FamilyQuant.Operations.OperationContext? operation = null)
        {
            if (peaksDeNovoRows == null) throw new ArgumentNullException(nameof(peaksDeNovoRows));
            if (deepNovoRows == null) throw new ArgumentNullException(nameof(deepNovoRows));
            if (patternLabPsms == null) throw new ArgumentNullException(nameof(patternLabPsms));
            if (quantRows == null) throw new ArgumentNullException(nameof(quantRows));

            List<PeaksDeNovoRow> peaks = peaksDeNovoRows.Where(r => r != null).ToList();
            List<PeaksDeNovoRow> deep = deepNovoRows.Where(r => r != null).ToList();
            List<PatternLabPsm> psms = patternLabPsms.Where(p => p != null).ToList();
            List<QuantRow> quants = quantRows.Where(q => q != null).ToList();
            HashSet<string>? acceptedSourceKeys = acceptedDeNovoRows == null ? null
                : new HashSet<string>(acceptedDeNovoRows.Where(r => r != null)
                    .Select(SourceObservationKey), StringComparer.Ordinal);
            Dictionary<string, RawScanPrecursorMetadata> rawByScan = (rawPrecursors
                    ?? Array.Empty<RawScanPrecursorMetadata>())
                .Where(r => r != null)
                .GroupBy(r => ScanKey(DeNovoReconciler.NormalizeRunName(r.RunName ?? string.Empty),
                    r.ScanNumber), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ChargeKnown)
                    .ThenByDescending(r => r.PrecursorMzKnown).First(), StringComparer.OrdinalIgnoreCase);
            var claims = CreateClaims(peaks, deep, psms, quants, acceptedSourceKeys);

            List<Cluster> clusters = BuildClusters(claims);
            Dictionary<string, int> hypothesesPerScan = clusters
                .GroupBy(c => ScanKey(c.Run, c.Scan), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // A row's measured value remains at precursor grain; presentation totals apply the
            // documented peptide representative collapse before summing it.
            List<NativePeptideQuant> peptideRepresentatives =
                NativeQuantFractions.DedupToPeptides(quants);
            Dictionary<string, List<NativePeptideQuant>> identifiedByIdentity = peptideRepresentatives
                .Where(p => string.Equals(p.Side, NativeQuantFractions.SideIdentified,
                    StringComparison.Ordinal))
                .GroupBy(NativeQuantFractions.CrossSideKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            List<NativePeptideQuant> creditedRepresentatives = peptideRepresentatives.Where(p =>
                string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal)
                || !identifiedByIdentity.TryGetValue(NativeQuantFractions.CrossSideKey(p),
                    out List<NativePeptideQuant>? identifiedMatches)
                || !identifiedMatches.Any(i => NativeQuantFractions.IsCrossSideEquivalent(i, p))).ToList();
            Dictionary<string, List<NativePeptideQuant>> creditedByIdentity = creditedRepresentatives
                .GroupBy(CreditedKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var ticByRun = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (perRunTic != null)
                foreach (KeyValuePair<string, double> kv in perRunTic)
                    ticByRun[DeNovoReconciler.NormalizeRunName(kv.Key)] = kv.Value;
            double? pooledTic = ticByRun.Count > 0 ? ticByRun.Values.Sum() : null;

            var output = new List<SpectrumComparisonRow>(clusters.Count);
            operation?.Report("spectrum-rows", "Preparing spectrum comparison rows", 0, clusters.Count);
            foreach (IGrouping<string, Cluster> scanGroup in clusters
                .GroupBy(c => ScanKey(c.Run, c.Scan), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                List<Cluster> ordered = scanGroup.OrderBy(c => c.Charge)
                    .ThenBy(c => SortableMz(c.OpeningMz)).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    operation?.ThrowIfCancellationRequested();
                    output.Add(ToRow(ordered[i], i + 1, hypothesesPerScan[scanGroup.Key],
                        familyLabels, familyAttribution,
                        rawByScan.TryGetValue(scanGroup.Key, out RawScanPrecursorMetadata? raw)
                            ? raw : null,
                        creditedByIdentity,
                        ticByRun.TryGetValue(ordered[i].Run, out double runTic) ? runTic : null,
                        pooledTic));
                    if (output.Count % 128 == 0 || output.Count == clusters.Count)
                        operation?.Report("spectrum-rows", "Spectrum comparison rows " + output.Count.ToString(CultureInfo.InvariantCulture)
                            + " of " + clusters.Count.ToString(CultureInfo.InvariantCulture), output.Count, clusters.Count);
                }
            }
            return output;
        }

        public static string ToCsv(IEnumerable<SpectrumComparisonRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var b = new StringBuilder();
            b.AppendLine(CsvHeader);
            foreach (SpectrumComparisonRow r in rows)
                b.Append(CsvLine(r)).Append('\n');
            return b.ToString();
        }

        public static string SaveCsv(string path, IEnumerable<SpectrumComparisonRow> rows)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A CSV output path is required.", nameof(path));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // This table can contain hundreds of thousands of wide rows. Write one row at a time so
            // exporting it does not allocate a second, full-file StringBuilder beside the in-memory
            // table that the project and DataGrid already need.
            using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(CsvHeader);
            writer.Write(Environment.NewLine);
            foreach (SpectrumComparisonRow row in rows)
            {
                writer.Write(CsvLine(row));
                writer.Write('\n');
            }
            return fullPath;
        }

        /// <summary>The family columns of one row, from its (distinct, ordered) family ids. One place, so
        /// a row built at Compute and a row refreshed after a regrouping cannot word them differently.</summary>
        private static (string ids, string assignments, double? identity, string status) DescribeFamilies(
            IReadOnlyList<int> familyIds, IReadOnlyDictionary<int, string>? labels,
            FamilyQuant.PeptideFamilyAttribution? attribution)
        {
            string ids = string.Join(";", familyIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            string assignments = string.Join(" | ", familyIds.Select(id =>
            {
                string prefix = "F" + id.ToString(CultureInfo.InvariantCulture);
                return labels != null && labels.TryGetValue(id, out string? label) && !string.IsNullOrWhiteSpace(label)
                    ? prefix + ": " + label : prefix;
            }));
            string status = attribution == null
                ? "family mapping not supplied; use the project curation/evidence-pack family tables"
                : attribution.Database == null
                    ? "curation supplied but contains no searchable protein rows"
                    : familyIds.Count > 0
                        ? "mapped to every target family meeting the configured homology rule"
                        : "no target family met the configured homology rule (unattributed)";
            return (ids, assignments, attribution?.IdentityThreshold, status);
        }

        /// <summary>
        /// Re-derives the family columns of rows built earlier from a CURRENT attribution, leaving every
        /// source, precursor and quant field as measured. The table is built at Compute against the
        /// curation of that moment; after a merge or a renumbering its family columns no longer match the
        /// project's families, and a saved project or an evidence pack would carry both readings side by
        /// side. Rows are matched to families through their stored quant side and sequence, exactly as
        /// they were at Compute.
        /// </summary>
        public static void RefreshFamilyAssignments(IEnumerable<SpectrumComparisonRow> rows,
                                                    FamilyQuant.PeptideFamilyAttribution attribution)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (attribution == null) throw new ArgumentNullException(nameof(attribution));

            foreach (SpectrumComparisonRow row in rows)
            {
                if (row == null) continue;
                IReadOnlyList<int> ids = string.IsNullOrEmpty(row.QuantSide)
                    ? Array.Empty<int>()
                    : attribution.Resolve(row.QuantSide, row.QuantSequence ?? string.Empty)
                        .Distinct().OrderBy(id => id).ToArray();
                var (text, assignments, identity, status) = DescribeFamilies(ids, attribution.FamilyDescriptions, attribution);
                row.FamilyUserId = ids.Count == 1 ? ids[0] : null;
                row.FamilyUserIds = text;
                row.FamilyAssignments = assignments;
                row.FamilyAssignment = assignments;
                row.FamilyMappingMinimumIdentity = identity;
                row.FamilyMappingStatus = status;
            }
        }

        private const string KeptMarker = "; KEPT AS COMPUTED: ";

        /// <summary>Leaves the family columns as computed and says, once per row, why they were not
        /// refreshed for the current families.</summary>
        public static void MarkFamilyAssignmentsKept(IEnumerable<SpectrumComparisonRow> rows, string reason)
        {
            foreach (SpectrumComparisonRow row in rows)
            {
                if (row == null) continue;
                string status = row.FamilyMappingStatus ?? string.Empty;
                int at = status.IndexOf(KeptMarker, StringComparison.Ordinal);
                if (at >= 0) status = status.Substring(0, at);
                row.FamilyMappingStatus = status + KeptMarker + reason +
                    "; the family numbers may predate later regroupings";
            }
        }

        /// <summary>Empties the family columns and says they are unavailable, so a failed refresh can never
        /// leave an assignment that looks current.</summary>
        public static void MarkFamilyAssignmentsUnavailable(IEnumerable<SpectrumComparisonRow> rows, string reason)
        {
            foreach (SpectrumComparisonRow row in rows)
            {
                if (row == null) continue;
                row.FamilyUserId = null;
                row.FamilyUserIds = string.Empty;
                row.FamilyAssignments = string.Empty;
                row.FamilyAssignment = string.Empty;
                row.FamilyMappingStatus = "family mapping unavailable: it could not be refreshed for the current " +
                    "families (" + reason + ")";
            }
        }

        private static SpectrumComparisonRow ToRow(Cluster cluster, int hypothesisIndex,
            int hypothesesInScan,
            IReadOnlyDictionary<int, string>? familyLabels,
            FamilyQuant.PeptideFamilyAttribution? familyAttribution,
            RawScanPrecursorMetadata? raw,
            IReadOnlyDictionary<string, List<NativePeptideQuant>> creditedByIdentity,
            double? runTic, double? pooledTic)
        {
            List<Claim> peaks = Ranked(cluster.Claims.Where(c => c.Kind == ClaimKind.Peaks));
            List<Claim> deep = Ranked(cluster.Claims.Where(c => c.Kind == ClaimKind.DeepNovo));
            List<Claim> pattern = Ranked(cluster.Claims.Where(c => c.Kind == ClaimKind.PatternLab));
            List<QuantRow> quants = cluster.Claims.Where(c => c.Kind == ClaimKind.Quant && c.Quant != null)
                .Select(c => c.Quant!).Distinct().ToList();
            QuantRow? quant = quants.OrderBy(q =>
                    string.Equals(q.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal) ? 0 : 1)
                .ThenByDescending(q => q.PatternLabXScore ?? double.NegativeInfinity)
                .ThenByDescending(q => q.QuantifiedAreaM0)
                .ThenByDescending(q => q.EnvelopeSum)
                .ThenBy(q => q.Sequence, StringComparer.Ordinal)
                .ThenBy(q => q.PrecursorMz).FirstOrDefault();

            bool quantValuesAgree = quants.Count <= 1 || quants.All(q =>
                SameDoubleBits(q.QuantifiedAreaM0, quant!.QuantifiedAreaM0)
                && SameDoubleBits(q.EnvelopeSum, quant.EnvelopeSum));
            Claim? p = peaks.FirstOrDefault(), d = deep.FirstOrDefault(), s = pattern.FirstOrDefault();
            bool? peaksMatchesRaw = MatchesRaw(p, raw);
            bool? deepMatchesRaw = MatchesRaw(d, raw);
            bool? patternMatchesRaw = MatchesRaw(s, raw);

            double? primary = null;
            string primaryBasis = string.Empty;
            if (quant != null)
            {
                primary = quant.EnvelopeSum;
                primaryBasis = string.Equals(quant.Side, NativeQuantFractions.SideDeNovo, StringComparison.Ordinal)
                    ? "observed:de novo measured envelope"
                    : "observed:identified measured envelope";
            }

            IReadOnlyList<int> familyIds = quant == null
                ? Array.Empty<int>()
                : familyAttribution != null
                    ? familyAttribution.Resolve(quant.Side ?? string.Empty, quant.Sequence ?? string.Empty)
                    : quant.FamilyUserId is int legacyFamilyId
                        ? new[] { legacyFamilyId }
                        : Array.Empty<int>();
            familyIds = familyIds.Distinct().OrderBy(id => id).ToArray();
            var (familyIdsText, familyAssignments, familyMappingIdentity, familyMappingStatus) =
                DescribeFamilies(familyIds, familyLabels ?? familyAttribution?.FamilyDescriptions, familyAttribution);

            int sourceKinds = (p == null ? 0 : 1) + (d == null ? 0 : 1) + (s == null ? 0 : 1);
            bool rawChargeKnown = raw?.ChargeKnown == true;
            bool rawMzKnown = raw?.PrecursorMzKnown == true;
            string agreement = sourceKinds >= 2
                ? "same reported charge and non-chaining isotope-error-aware 20-ppm precursor hypothesis " +
                  "(M, M+-1, M+-2 of one precursor share a row)"
                : sourceKinds == 1 ? "single sequence source in this precursor cluster"
                : "quant-only precursor cluster";
            if (cluster.IsotopeCorrected)
                agreement += "; precursor match basis: isotope-corrected (selected offset " +
                    cluster.SelectedIsotopeOffset.ToString(CultureInfo.InvariantCulture) + ")";
            if (hypothesesInScan > 1)
                agreement += "; SameScanDifferentPrecursorHypothesis: this file+scan contains " +
                    "multiple incompatible reported charge/mz hypotheses";

            string status = quant == null
                ? "source evidence present; no stored XIC for this precursor hypothesis"
                : !quantValuesAgree
                    ? "quant-value conflict inside one precursor cluster; deterministic primary shown"
                    : sourceKinds == 0
                        ? "stored XIC; no persisted sequence-source call"
                        : "one shared XIC for this precursor hypothesis";
            if (rawChargeKnown || rawMzKnown)
            {
                bool anyMismatch = peaksMatchesRaw == false || deepMatchesRaw == false
                    || patternMatchesRaw == false;
                status += anyMismatch
                    ? "; SourceVsRawPrecursorMismatch: caller-reported charge/mz disagrees with " +
                      "RAW-selected diagnostic metadata"
                    : "; SourceVsRawPrecursorCompatible";
            }
            else
                status += "; RAW precursor metadata unavailable; source hypothesis is not RAW-validated";
            if (p?.AcceptedByConfiguredFilters == false)
                status += "; top PEAKS de novo call rejected by configured source filters";
            if (d?.AcceptedByConfiguredFilters == false)
                status += "; top DeepNovo call rejected by configured source filters";

            bool credited = quant != null
                && creditedByIdentity.TryGetValue(CreditedKey(quant),
                    out List<NativePeptideQuant>? creditedCandidates)
                && creditedCandidates.Any(rep => QuantMatchesRepresentative(quant, rep));
            string creditReason = quant == null ? "no stored XIC"
                : credited ? "credited peptide representative after within-side dedup and search-over-de-novo suppression"
                : "not credited after peptide representative dedup or search-over-de-novo suppression";

            return new SpectrumComparisonRow
            {
                FileName = cluster.Run.Length == 0 ? string.Empty : cluster.Run + ".raw",
                NormalizedRunName = cluster.Run,
                ScanNumber = cluster.Scan,
                PrecursorHypothesisIndex = hypothesisIndex,
                Charge = cluster.Charge,
                PrecursorMz = cluster.OpeningMz,
                PrecursorMzMin = cluster.Claims.Where(c => c.Mz > 0).Select(c => c.Mz).DefaultIfEmpty(0).Min(),
                PrecursorMzMax = cluster.Claims.Where(c => c.Mz > 0).Select(c => c.Mz).DefaultIfEmpty(0).Max(),
                RawPrecursorCharge = rawChargeKnown ? raw!.Charge : null,
                RawPrecursorMz = rawMzKnown ? raw!.PrecursorMz : null,
                RawChargeKnown = rawChargeKnown,
                RawPrecursorMzKnown = rawMzKnown,
                CanonicalCharge = cluster.Charge,
                CanonicalPrecursorMz = cluster.OpeningMz,
                PrecursorKeyBasis = "source-reported equal charge + non-chaining isotope-error-aware " +
                    "20-ppm opening-m/z cluster (M, M+-1, M+-2 share a precursor); RAW metadata diagnostic only",
                PeaksDeNovoSequence = p?.Sequence ?? string.Empty,
                PeaksDeNovoCleanSequence = Clean(p?.Sequence),
                PeaksDeNovoScore = p?.Score,
                PeaksDeNovoScoreField = p?.ScoreField ?? string.Empty,
                PeaksDeNovoSource = p?.SourceDisplayName ?? string.Empty,
                PeaksDeNovoMz = p?.Mz,
                PeaksDeNovoCharge = p == null ? null : p.Charge,
                PeaksDeNovoMatchesRawPrecursor = peaksMatchesRaw,
                PeaksDeNovoAccepted = p?.AcceptedByConfiguredFilters,
                PeaksDeNovoCandidateCount = peaks.Count,
                PeaksDeNovoAcceptedCandidateCount = peaks.Count(c =>
                    c.AcceptedByConfiguredFilters == true),
                PeaksDeNovoAlternateSequences = Alternates(peaks),
                DeepNovoSequence = d?.Sequence ?? string.Empty,
                DeepNovoCleanSequence = Clean(d?.Sequence),
                DeepNovoScore = d?.Score,
                DeepNovoScoreField = d?.ScoreField ?? string.Empty,
                DeepNovoMz = d?.Mz,
                DeepNovoCharge = d == null ? null : d.Charge,
                DeepNovoMatchesRawPrecursor = deepMatchesRaw,
                DeepNovoAccepted = d?.AcceptedByConfiguredFilters,
                DeepNovoCandidateCount = deep.Count,
                DeepNovoAcceptedCandidateCount = deep.Count(c =>
                    c.AcceptedByConfiguredFilters == true),
                DeepNovoAlternateSequences = Alternates(deep),
                PatternLabSequence = s?.Sequence ?? string.Empty,
                PatternLabCleanSequence = Clean(s?.Sequence),
                PatternLabXScore = s?.Score,
                PatternLabMz = s?.Mz,
                PatternLabCharge = s == null ? null : s.Charge,
                PatternLabMatchesRawPrecursor = patternMatchesRaw,
                PatternLabCandidateCount = pattern.Count,
                PatternLabAlternateSequences = Alternates(pattern),
                QuantM0Observed = quant?.QuantifiedAreaM0,
                QuantEnvelopeObserved = quant?.EnvelopeSum,
                QuantPrimaryAssigned = primary,
                QuantPrimaryBasis = primaryBasis,
                QuantSide = quant?.Side ?? string.Empty,
                QuantSequence = quant?.Sequence ?? string.Empty,
                QuantCleanSequence = Clean(quant?.Sequence),
                QuantMz = quant?.PrecursorMz,
                QuantCharge = quant == null ? null : quant.Charge,
                QuantCandidateCount = quants.Count,
                QuantValuesAgree = quantValuesAgree,
                FamilyUserId = familyIds.Count == 1 ? familyIds[0] : null,
                FamilyUserIds = familyIdsText,
                FamilyAssignments = familyAssignments,
                FamilyAssignment = familyAssignments,
                FamilyMappingMinimumIdentity = familyMappingIdentity,
                FamilyMappingStatus = familyMappingStatus,
                RunTic = runTic,
                PooledTic = pooledTic,
                QuantCreditedInPrimaryTotals = credited,
                QuantCreditReason = creditReason,
                PrecursorAgreement = agreement,
                SameScanDifferentPrecursorHypothesis = hypothesesInScan > 1,
                JoinStatus = status,
                PrecursorIsotopeOffsetSelected = cluster.SelectedIsotopeOffset,
                PrecursorMatchBasis = PeptidoformIdentity.MatchBasis(cluster.SelectedIsotopeOffset),
            };
        }

        private static List<Cluster> BuildClusters(IEnumerable<Claim> input)
        {
            var clusters = new List<Cluster>();
            foreach (IGrouping<string, Claim> scan in input
                .GroupBy(c => ScanKey(c.Run, c.Scan), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                List<Claim> allClaims = scan.OrderBy(c => c.Charge)
                    .ThenBy(c => SortableMz(c.Mz))
                    .ThenBy(c => c.Kind).ThenByDescending(c => c.Score ?? double.NegativeInfinity)
                    .ThenBy(c => c.Sequence, StringComparer.Ordinal).ToList();

                // Reported precursor compatibility defines the join. RAW selected metadata remain a
                // diagnostic annotation because reaction m/z may be the isolation centre and callers
                // can report alternative/co-isolated hypotheses for one scan.
                foreach (IGrouping<int, Claim> spectrum in allClaims.GroupBy(c => c.Charge)
                    .OrderBy(g => g.Key))
                {
                    List<Claim> claims = spectrum.OrderBy(c => SortableMz(c.Mz))
                        .ThenBy(c => c.Kind)
                        .ThenByDescending(c => c.Score ?? double.NegativeInfinity)
                        .ThenBy(c => c.Sequence, StringComparer.Ordinal).ToList();
                    Cluster? current = null;
                    double opening = 0;
                    foreach (Claim claim in claims)
                    {
                        bool outside;
                        int offset = 0;
                        if (current == null) outside = true;
                        else if (opening <= 0 || claim.Mz <= 0)
                            outside = !(opening <= 0 && claim.Mz <= 0);
                        // Direct 20-ppm agreement OR agreement after removing a +-1/+-2 monoisotopic-peak
                        // selection error at the cluster's shared charge. The offset is recorded so the row
                        // can export it and the match basis without overwriting the reported m/z.
                        else outside = !PeptidoformIdentity.PrecursorMzEquivalent(opening, claim.Mz,
                                current.Charge, out offset, PeptidoformIdentity.MaxIsotopeOffset,
                                DeNovoUnionResolver.PrecursorTolerancePpm);

                        if (outside)
                        {
                            current = new Cluster
                            {
                                Run = claim.Run,
                                Scan = claim.Scan,
                                Charge = claim.Charge,
                                OpeningMz = claim.Mz,
                            };
                            clusters.Add(current);
                            opening = claim.Mz;
                        }
                        else if (offset != 0)
                        {
                            current!.IsotopeCorrected = true;
                            if (Math.Abs(offset) > Math.Abs(current.SelectedIsotopeOffset))
                                current.SelectedIsotopeOffset = offset;
                        }
                        current!.Claims.Add(claim);
                    }
                }
            }
            return clusters;
        }

        private static List<Claim> CreateClaims(IEnumerable<PeaksDeNovoRow> peaks, IEnumerable<PeaksDeNovoRow> deep,
            IEnumerable<PatternLabPsm> psms, IEnumerable<QuantRow> quants, HashSet<string>? acceptedSourceKeys)
        {
            var claims = new List<Claim>();

            foreach (PeaksDeNovoRow row in peaks)
                claims.Add(SourceClaim(row, IsDeepNovo(row) ? ClaimKind.DeepNovo : ClaimKind.Peaks,
                    true, acceptedSourceKeys == null || acceptedSourceKeys.Contains(SourceObservationKey(row))));
            foreach (PeaksDeNovoRow row in deep)
                claims.Add(SourceClaim(row, IsDeepNovo(row) ? ClaimKind.DeepNovo : ClaimKind.Peaks,
                    true, acceptedSourceKeys == null || acceptedSourceKeys.Contains(SourceObservationKey(row))));
            foreach (PatternLabPsm psm in psms) claims.Add(PatternLabClaim(psm, true));
            var externalClaimKeys = new HashSet<string>(claims
                .Where(c => c.IsExternalSourceObservation)
                .Select(EquivalentClaimKey), StringComparer.Ordinal);

            // Persisted quant rows supply their own sequence claims when the original CSV/.sepr2 inputs
            // are unavailable. When those inputs ARE supplied, identical embedded evidence is skipped so
            // a source does not count twice merely because it was persisted beside its XIC.
            foreach (QuantRow quant in quants)
            {
                string run = DeNovoReconciler.NormalizeRunName(quant.Run ?? string.Empty);
                claims.Add(new Claim
                {
                    Kind = ClaimKind.Quant,
                    Run = run,
                    Scan = quant.Scan,
                    Charge = quant.Charge,
                    Mz = quant.PrecursorMz,
                    Sequence = quant.Sequence ?? string.Empty,
                    Quant = quant,
                });

                if (string.Equals(quant.Side, NativeQuantFractions.SideIdentified,
                    StringComparison.Ordinal))
                {
                    var embedded = new Claim
                    {
                        Kind = ClaimKind.PatternLab,
                        Run = run,
                        Scan = quant.Scan,
                        Charge = quant.Charge,
                        Mz = quant.PrecursorMz,
                        Sequence = quant.Sequence ?? string.Empty,
                        Score = quant.PatternLabXScore,
                        ScoreField = "XCorr",
                        SourceId = "patternlab",
                        SourceDisplayName = "PatternLab",
                    };
                    if (!externalClaimKeys.Contains(EquivalentClaimKey(embedded))) claims.Add(embedded);
                }

                foreach (DeNovoEvidence evidence in quant.DeNovoEvidence ?? new List<DeNovoEvidence>())
                {
                    ClaimKind kind = string.Equals(evidence.SourceId, DeNovoSource.DeepNovo.Id,
                        StringComparison.OrdinalIgnoreCase) ? ClaimKind.DeepNovo : ClaimKind.Peaks;
                    var embedded = new Claim
                    {
                        Kind = kind,
                        Run = run,
                        Scan = quant.Scan,
                        Charge = quant.Charge,
                        Mz = quant.PrecursorMz,
                        Sequence = evidence.Sequence ?? string.Empty,
                        Score = evidence.ConfidenceValue,
                        ScoreField = evidence.ConfidenceField ?? string.Empty,
                        SourceId = evidence.SourceId ?? string.Empty,
                        SourceDisplayName = evidence.SourceDisplayName ?? string.Empty,
                        AcceptedByConfiguredFilters = true,
                    };
                    if (!externalClaimKeys.Contains(EquivalentClaimKey(embedded))) claims.Add(embedded);
                }
            }

            return claims;
        }

        private static Claim SourceClaim(PeaksDeNovoRow row, ClaimKind kind, bool external,
            bool accepted) => new Claim
        {
            Kind = kind,
            Run = DeNovoReconciler.NormalizeRunName(row.RunName ?? string.Empty),
            Scan = row.Scan,
            Charge = row.Charge,
            Mz = row.Mz,
            Sequence = row.Peptide ?? string.Empty,
            Score = row.ConfidenceValue,
            ScoreField = row.ConfidenceField ?? string.Empty,
            SourceId = row.SourceId ?? string.Empty,
            SourceDisplayName = row.SourceDisplayName ?? string.Empty,
            IsExternalSourceObservation = external,
            AcceptedByConfiguredFilters = accepted,
        };

        private static Claim PatternLabClaim(PatternLabPsm psm, bool external) => new Claim
        {
            Kind = ClaimKind.PatternLab,
            Run = DeNovoReconciler.NormalizeRunName(psm.FileName ?? string.Empty),
            Scan = psm.ScanNumber,
            Charge = psm.ChargeState,
            Mz = psm.PrecursorMz,
            Sequence = psm.Sequence ?? string.Empty,
            Score = psm.XCorr,
            ScoreField = "XCorr",
            SourceId = "patternlab",
            SourceDisplayName = "PatternLab",
            IsExternalSourceObservation = external,
        };

        private static string EquivalentClaimKey(Claim claim) =>
            claim.Kind.ToString() + "\u001f" + claim.Run.ToUpperInvariant() + "\u001f" +
            claim.Scan.ToString(CultureInfo.InvariantCulture) + "\u001f" +
            claim.Charge.ToString(CultureInfo.InvariantCulture) + "\u001f" +
            Clean(claim.Sequence) + "\u001f" + Num(claim.Score);

        private static List<Claim> Ranked(IEnumerable<Claim> claims) => claims
            .GroupBy(c => c.Kind.ToString() + "\u001f" + (c.SourceId ?? string.Empty) + "\u001f" +
                Clean(c.Sequence) + "\u001f" + Num(c.Score) + "\u001f" +
                c.Mz.ToString("R", CultureInfo.InvariantCulture) + "\u001f" +
                (c.AcceptedByConfiguredFilters == true ? "accepted" : "rejected"),
                StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(c => c.AcceptedByConfiguredFilters == true)
            .ThenByDescending(c => c.Score ?? double.NegativeInfinity)
            .ThenBy(c => Clean(c.Sequence), StringComparer.Ordinal)
            .ThenBy(c => c.Sequence, StringComparer.Ordinal)
            .ThenBy(c => c.Mz).ToList();

        private static string Alternates(IReadOnlyList<Claim> ranked) => ranked.Count <= 1
            ? string.Empty
            : string.Join(" | ", ranked.Skip(1).Select(c => c.Sequence + " [" +
                (c.Score.HasValue ? c.ScoreField + "=" +
                    c.Score.Value.ToString("0.###", CultureInfo.InvariantCulture) + "; " : string.Empty) +
                "filter=" + (c.AcceptedByConfiguredFilters == true ? "accepted" : "rejected") +
                "; z=" + c.Charge.ToString(CultureInfo.InvariantCulture) + "; m/z=" +
                c.Mz.ToString("0.#####", CultureInfo.InvariantCulture) + "]"));

        private static string SourceObservationKey(PeaksDeNovoRow row) =>
            (row.SourceId ?? string.Empty) + "\u001f" +
            DeNovoReconciler.NormalizeRunName(row.RunName ?? string.Empty).ToUpperInvariant() + "\u001f" +
            row.Scan.ToString(CultureInfo.InvariantCulture) + "\u001f" +
            row.Charge.ToString(CultureInfo.InvariantCulture) + "\u001f" +
            row.Mz.ToString("R", CultureInfo.InvariantCulture) + "\u001f" +
            (row.Peptide ?? string.Empty) + "\u001f" +
            row.ConfidenceValue.ToString("R", CultureInfo.InvariantCulture) + "\u001f" +
            (row.ConfidenceField ?? string.Empty) + "\u001f" +
            row.Length.ToString(CultureInfo.InvariantCulture) + "\u001f" +
            string.Join(",", row.LocalConfidence ?? Array.Empty<int>()) + "\u001f" +
            (row.InputFilePath ?? string.Empty).ToUpperInvariant() + "\u001f" +
            (row.InputFileSha256 ?? string.Empty).ToUpperInvariant();

        private static bool IsDeepNovo(PeaksDeNovoRow row) => string.Equals(
            row.SourceId, DeNovoSource.DeepNovo.Id, StringComparison.OrdinalIgnoreCase);

        private static bool? MatchesRaw(Claim? claim, RawScanPrecursorMetadata? raw)
        {
            if (claim == null || raw == null || !(raw.ChargeKnown || raw.PrecursorMzKnown)) return null;
            bool charge = !raw.ChargeKnown || claim.Charge == raw.Charge;
            bool mz = !raw.PrecursorMzKnown || (claim.Mz > 0 &&
                Math.Abs(claim.Mz - raw.PrecursorMz) / raw.PrecursorMz * 1e6 <=
                    DeNovoUnionResolver.PrecursorTolerancePpm);
            return charge && mz;
        }

        private static bool QuantMatchesRepresentative(QuantRow quant, NativePeptideQuant rep) =>
            string.Equals(DeNovoReconciler.NormalizeRunName(quant.Run ?? string.Empty), rep.Run,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(quant.Side ?? string.Empty, rep.Side ?? string.Empty, StringComparison.Ordinal)
            && quant.Scan == rep.RepresentativeScan
            && string.Equals(PeptidoformIdentity.Canonicalize(quant.Sequence ?? string.Empty,
                    foldIsoleucineLeucine: false),
                PeptidoformIdentity.Canonicalize(rep.Sequence ?? string.Empty,
                    foldIsoleucineLeucine: false), StringComparison.Ordinal)
            && quant.Charge == rep.Charge
            && PeptidoformIdentity.PrecursorMassEquivalent(
                AnalyticsBuilder.MonoisotopicMass(quant.PrecursorMz, quant.Charge),
                rep.MonoisotopicMass, out _)
            && SameDoubleBits(quant.QuantifiedAreaM0, rep.AreaM0)
            && SameDoubleBits(quant.EnvelopeSum, rep.EnvelopeSum);

        private static string CreditedKey(QuantRow quant) =>
            DeNovoReconciler.NormalizeRunName(quant.Run ?? string.Empty) + "\u001f" +
            (quant.Side ?? string.Empty) + "\u001f" +
            PeptidoformIdentity.Canonicalize(quant.Sequence ?? string.Empty,
                foldIsoleucineLeucine: false) + "\u001f" +
            quant.Charge.ToString(CultureInfo.InvariantCulture);

        private static string CreditedKey(NativePeptideQuant quant) =>
            quant.Run + "\u001f" + quant.Side + "\u001f" +
            PeptidoformIdentity.Canonicalize(quant.Sequence ?? string.Empty,
                foldIsoleucineLeucine: false) + "\u001f" +
            quant.Charge.ToString(CultureInfo.InvariantCulture);

        private static string Clean(string? sequence) => DeNovoReconciler.NormalizeSequenceIgnoringPtm(
            sequence ?? string.Empty, foldIsoleucineLeucine: false);

        private static string ScanKey(string run, int scan) => run + "\u001f" +
            scan.ToString(CultureInfo.InvariantCulture);

        private static string ChargeKey(string run, int scan, int charge) => ScanKey(run, scan) + "\u001f" +
            charge.ToString(CultureInfo.InvariantCulture);

        private static double SortableMz(double mz) => mz > 0 ? mz : double.PositiveInfinity;

        private static bool SameDoubleBits(double first, double second) =>
            BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);

        private static bool NullableDoubleBitsEqual(double? first, double? second) =>
            first.HasValue == second.HasValue && (!first.HasValue || SameDoubleBits(first.Value, second!.Value));

        private static string Num(double? value) => value.HasValue
            ? value.Value.ToString("R", CultureInfo.InvariantCulture) : string.Empty;

        private static string CsvLine(SpectrumComparisonRow r)
        {
            object?[] values =
            {
                r.ScanNumber, r.FileName, r.PeaksDeNovoSequence, r.DeepNovoSequence,
                r.PatternLabSequence, Num(r.PeaksDeNovoScore), Num(r.DeepNovoScore),
                Num(r.PatternLabXScore), Num(r.PrimaryQuantValue), r.Charge, Num(r.PrecursorMz),
                r.PrecursorHypothesisIndex, Num(r.PrecursorMzMin), Num(r.PrecursorMzMax),
                r.RawPrecursorCharge, Num(r.RawPrecursorMz), r.RawChargeKnown,
                r.RawPrecursorMzKnown, r.CanonicalCharge, Num(r.CanonicalPrecursorMz),
                r.PrecursorKeyBasis,
                r.PeaksDeNovoCleanSequence, r.DeepNovoCleanSequence, r.PatternLabCleanSequence,
                r.PeaksDeNovoScoreField, r.DeepNovoScoreField, r.PeaksDeNovoSource,
                r.PeaksDeNovoCharge, Num(r.PeaksDeNovoMz), r.PeaksDeNovoMatchesRawPrecursor,
                r.PeaksDeNovoAccepted,
                r.DeepNovoCharge, Num(r.DeepNovoMz), r.DeepNovoMatchesRawPrecursor,
                r.DeepNovoAccepted,
                r.PatternLabCharge, Num(r.PatternLabMz), r.PatternLabMatchesRawPrecursor,
                r.PeaksDeNovoCandidateCount, r.PeaksDeNovoAcceptedCandidateCount,
                r.DeepNovoCandidateCount, r.DeepNovoAcceptedCandidateCount,
                r.PatternLabCandidateCount,
                r.PeaksDeNovoAlternateSequences, r.DeepNovoAlternateSequences,
                r.PatternLabAlternateSequences, Num(r.QuantM0Observed),
                Num(r.QuantEnvelopeObserved), Num(r.QuantPrimaryAssigned), r.QuantPrimaryBasis,
                r.QuantSide, r.QuantSequence, r.QuantCleanSequence, r.QuantCharge, Num(r.QuantMz),
                r.QuantCandidateCount, r.QuantValuesAgree, r.FamilyUserId, r.FamilyUserIds,
                r.FamilyAssignments, r.FamilyAssignment, Num(r.FamilyMappingMinimumIdentity),
                r.FamilyMappingStatus,
                Num(r.RunTic), Num(r.PooledTic), r.QuantCreditedInPrimaryTotals,
                r.QuantCreditReason,
                r.PrecursorAgreement, r.SameScanDifferentPrecursorHypothesis, r.JoinStatus,
                r.PrecursorIsotopeOffsetSelected, r.PrecursorMatchBasis,
            };
            return string.Join(",", values.Select(v => Csv(Convert.ToString(v,
                CultureInfo.InvariantCulture) ?? string.Empty)));
        }

        private static string Csv(string value) => SpreadsheetSafeCsv.Encode(value);
    }
}
