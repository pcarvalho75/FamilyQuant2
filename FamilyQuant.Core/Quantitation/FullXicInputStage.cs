using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>Validated acquisitions and source calls shared by the extraction and completion stages.
    /// Run order and input row order are retained from the production import.</summary>
    internal sealed class FullXicInput
    {
        internal required string RawDirectory { get; init; }
        internal required string Sepr2Path { get; init; }
        internal required Dictionary<string, string> RawByRun { get; init; }
        internal required List<PatternLabPsm> Psms { get; init; }
        internal required Dictionary<string, List<PatternLabPsm>> PsmsByRun { get; init; }
        internal required IReadOnlyList<DeNovoSourceSelection> Selections { get; init; }
        internal required FullXicSourceInput Sources { get; init; }
        internal required IReadOnlyList<string> Runs { get; init; }
        internal required FullXicRunMatch Match { get; init; }
    }

    /// <summary>Reads inputs, applies source rules and refuses unmatched acquisitions before extraction.</summary>
    internal static class FullXicInputStage
    {
        internal static FullXicInput Prepare(string rawDirectory, string sepr2Path,
            string? deNovoDirectory, FullXicOptions options, Action<string>? progress,
            IReadOnlyList<string>? restrictToRuns)
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

            FullXicSourceInput deNovoImport = deNovoDirectory == null
                ? new FullXicSourceInput()
                : FullXicSourceStage.Read(deNovoDirectory, psms, options.DeNovoCriteria, deNovoSelections, progress);
            Dictionary<string, List<PeaksDeNovoRow>> deNovoByRun = deNovoImport.ByRun;

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

            return new FullXicInput
            {
                RawDirectory = rawDirectory, Sepr2Path = sepr2Path, RawByRun = rawByRun,
                Psms = psms, PsmsByRun = psmsByRun, Selections = deNovoSelections,
                Sources = deNovoImport, Runs = runs, Match = match,
            };
        }

        internal static FullXicComputeResult CreateResult(FullXicInput input, FullXicOptions options)
        {
            string rawDirectory = input.RawDirectory;
            string sepr2Path = input.Sepr2Path;
            string identificationsName = Path.GetFileName(sepr2Path);
            FullXicRunMatch match = input.Match;
            FullXicSourceInput deNovoImport = input.Sources;
            IReadOnlyList<DeNovoSourceSelection> deNovoSelections = input.Selections;
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

            return result;
        }
    }
}
