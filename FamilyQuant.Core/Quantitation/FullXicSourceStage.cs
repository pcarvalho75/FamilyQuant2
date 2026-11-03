using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant
{
    internal sealed class FullXicSourceInput
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

    /// <summary>Source-aware import and studies before any RAW extraction. Keeps audit calls,
    /// accepted calls and reconciled extraction requests as separate populations.</summary>
    internal static class FullXicSourceStage
    {
        internal static FullXicSourceInput Read(
            string deNovoDirectory, List<PatternLabPsm> psms, DeNovoFilterCriteria criteria,
            IReadOnlyList<DeNovoSourceSelection> selections, Action<string>? progress, OperationContext? operation = null)
        {
            var result = new FullXicSourceInput();
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
            int sourceIndex = 0;
            operation?.Report("denovo-source", "Reading de novo sources", 0, selections.Count);

            // Each source is read with its own glob and its own confidence column, then concatenated: a
            // single source reproduces the baseline; a union (de novo all + DeepNovo) reads both globs
            // under the same tree, each with the right column, and pools the rows.
            foreach (DeNovoSourceSelection selection in selections)
            {
                DeNovoSource source = selection.Source;
                operation?.Report("denovo-source", "Source " + (++sourceIndex).ToString(CultureInfo.InvariantCulture) + " of "
                    + selections.Count.ToString(CultureInfo.InvariantCulture) + ": " + source.DisplayName, sourceIndex, selections.Count);
                progress?.Invoke("Reading the " + source.DisplayName + " export (" + source.FileGlob +
                    ") under " + deNovoDirectory + "; " + selection.CutoffLabel + "...");

                if (!rowsBySource.TryGetValue(source.Id, out List<PeaksDeNovoRow>? sourceRows))
                    rowsBySource[source.Id] = sourceRows = new List<PeaksDeNovoRow>();

                var files = DeNovoSourceFileDiscovery.RequireFiles(deNovoDirectory, source);
                int fileIndex = 0;
                operation?.Report("denovo-file:" + source.Id, "Reading source files: " + source.DisplayName, 0, files.Count);
                foreach (string csv in files)
                {
                    operation?.Report("denovo-file:" + source.Id, "File " + (++fileIndex).ToString(CultureInfo.InvariantCulture) + " of "
                        + files.Count.ToString(CultureInfo.InvariantCulture) + ": " + Path.GetFileName(csv), fileIndex, files.Count);
                    List<PeaksDeNovoRow> read = PeaksCsvReader.Read(csv, source);
                    List<PeaksDeNovoRow> accepted = DeNovoFilter.Apply(
                        read, criteria, selection.MinimumConfidence, operation, "denovo-rows:" + source.Id + ":" + csv);
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

                operation?.Report("source-score:" + source.Id, "Preparing source score distribution: " + source.DisplayName, 0, 1);
                result.SourceScoreDistributions.Add(DeNovoSourceScoreDistributionStudy.Build(
                    sourceRows, selection, criteria));
                operation?.Report("source-score:" + source.Id, "Source score distribution prepared: " + source.DisplayName, 1, 1);
            }

            // Canonicalize file provenance from the parsed rows themselves. The inline records above
            // retain empty-file provenance; this pass makes non-empty files authoritative and prevents a
            // partially populated input list from travelling beside a complete multi-source distribution.
            // The presentation validator remains fail-closed and will still reject a configured source
            // whose export is genuinely absent.
            operation?.Report("source-study", "Preparing source studies", 0, 10);
            void Study(int index, string title) => operation?.Report("source-study", "Source study "
                + index.ToString(CultureInfo.InvariantCulture) + " of 10: " + title, index, 10);
            Study(1, "Input provenance");
            RebuildObservedInputManifests(result, rowsBySource, selections, criteria);

            // A single source may use the legacy presentation record. In a union it remains the PRIMARY
            // source's distribution only: ALC and SCORE are never pooled into one histogram.
            DeNovoSourceSelection primary = selections.OrderBy(s => s.Priority).First();
            Study(2, "Primary-source score distribution");
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
            Study(3, "Caller agreement");
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
            Study(4, "Precursor union resolution");
            filtered = DeNovoUnionResolver.Resolve(filtered, selections);

            Study(5, "Database-search exports");
            foreach (string csv in Directory
                .GetFiles(deNovoDirectory, "db.psms.csv", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal))
                peaksDatabase.AddRange(PeaksDatabasePsmReader.Read(csv));

            Study(6, "Reconciliation with identifications");
            List<ReconciledRow> reconciliation = DeNovoReconciler.Reconcile(filtered, psms);
            Study(7, "Source overlap");
            result.Overlap = DeNovoOverlapStudy.Build(rawRows, reconciliation, criteria);
            Study(8, "De novo identification status");
            result.IdentificationSummary = DeNovoIdentificationStudy.Build(filtered, reconciliation);
            Study(9, "Identification concordance");
            result.IdentificationConcordance = IdentificationConcordanceStudy.Build(
                psms, peaksDatabase, filtered);

            // The two-source Venn is derived from this exact computation, never by subtracting the
            // independently run source variants. Tier A uses each caller's accepted source rows before
            // precursor union; tier B follows the source provenance attached to union-resolved rows that
            // survived PatternLab reconciliation as additional hypotheses.
            Study(10, "Source membership");
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

            foreach (var run in DeNovoInspectionSelection.GroupRetainedByRun(reconciliation))
                result.ByRun.Add(run.Key, run.Value);

            return result;
        }

        private static void RebuildObservedInputManifests(FullXicSourceInput result,
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

    }
}
