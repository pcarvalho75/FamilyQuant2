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
        /// <see cref="SpectrumComparisonTable.DefaultFileName"/> into per-user output storage.</summary>
        public string? SpectrumComparisonCsvPath { get; set; }

        /// <summary>Optional cache/output root outside the acquisition folder. Null uses per-user
        /// application data, scoped by the acquisition folder to avoid same-name run collisions.</summary>
        public string? CacheDirectory { get; set; }

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
        public bool TryExportSpectrumComparisonCsv(string path) => TryExportSpectrumComparisonCsvOutsideRaw(path, null);

        /// <summary>Automatic Compute export protects the acquisition directory, including an explicit
        /// path that points back into it. A refused CSV never loses the computed table.</summary>
        public bool TryExportSpectrumComparisonCsvOutsideRaw(string path, string? rawDirectory)
        {
            SpectrumComparisonCsvPath = string.Empty;
            SpectrumComparisonCsvError = string.Empty;
            try
            {
                if (rawDirectory != null && FullXicStoragePaths.IsInRawDirectory(path, rawDirectory))
                    throw new ArgumentException("Choose a CSV output path outside the RAW acquisition folder.", nameof(path));
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
}
