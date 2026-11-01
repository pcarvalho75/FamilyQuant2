using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// Describes ONE de novo export family the pipeline can read: the filename glob its files are found
    /// by, the header column its per-spectrum confidence lives in, and whether it is the strict
    /// "de novo only" category (spectra the database search could not identify) or a fuller export that
    /// also includes identified spectra.
    ///
    /// The three presets cover the sources of the same three Bjararaca replicates:
    ///   DenovoOnly reproduces the ORIGINAL behavior exactly (same glob "*.denovoOnly.csv", same column
    ///     "ALC (%)"), so wiring a source through the pipeline does not move any baseline number.
    ///   DeNovoAll is PEAKS' full de novo export (glob "*.denovo.csv"), still confidence "ALC (%)".
    ///   DeepNovo is the DeepNovo export (glob "*.deepnovo.csv"), confidence "SCORE (%)". SCORE is NOT
    ///     the same scale as ALC, so a figure that shows both must label each with its own field.
    /// </summary>
    public sealed class DeNovoSource
    {
        public DeNovoSource(string displayName, string fileGlob, string confidenceColumn, bool isDenovoOnly)
            : this(MakeId(displayName), displayName, fileGlob, confidenceColumn, isDenovoOnly)
        {
        }

        public DeNovoSource(string id, string displayName, string fileGlob, string confidenceColumn,
                            bool isDenovoOnly)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A stable source id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A display name is required.", nameof(displayName));
            if (string.IsNullOrWhiteSpace(fileGlob))
                throw new ArgumentException("A file glob is required.", nameof(fileGlob));
            if (string.IsNullOrWhiteSpace(confidenceColumn))
                throw new ArgumentException("A confidence column is required.", nameof(confidenceColumn));
            Id = id.Trim();
            DisplayName = displayName;
            FileGlob = fileGlob;
            ConfidenceColumn = confidenceColumn;
            IsDenovoOnly = isDenovoOnly;
        }

        /// <summary>A stable, machine-readable identity persisted in the project. Display names are not
        /// identities: changing UI wording must not make a previously saved source unknowable.</summary>
        public string Id { get; }

        /// <summary>The filename glob the source's files are discovered by, for example "*.denovoOnly.csv".</summary>
        public string FileGlob { get; }

        /// <summary>The header column the per-spectrum confidence is read from into
        /// <see cref="PeaksDeNovoRow.Alc"/>: "ALC (%)" for PEAKS de novo, "SCORE (%)" for DeepNovo.</summary>
        public string ConfidenceColumn { get; }

        /// <summary>Semantic alias used by source-aware reports. Kept beside ConfidenceColumn so older
        /// call sites and newer manifest/figure code can share the model without renaming DeepNovo SCORE.</summary>
        public string ConfidenceField => ConfidenceColumn;

        /// <summary>True only for the strict de-novo-only category. Drives labelling: text that asserts the
        /// input is de-novo-only is accurate only for this source.</summary>
        public bool IsDenovoOnly { get; }

        /// <summary>A short, human-readable name used for status lines and per-variant output folders.</summary>
        public string DisplayName { get; }

        /// <summary>The confidence field exactly as the source names it. In particular, DeepNovo's
        /// SCORE is never called ALC.</summary>
        public string ConfidenceLabel => ConfidenceColumn;

        /// <summary>The original, unchanged baseline: strict de-novo-only files, confidence "ALC (%)".</summary>
        public static DeNovoSource DenovoOnly { get; } =
            new DeNovoSource("peaks-denovo-only", "de novo only", "*.denovoOnly.csv", "ALC (%)",
                isDenovoOnly: true);

        /// <summary>PEAKS' full de novo export (identified spectra included), confidence "ALC (%)".</summary>
        public static DeNovoSource DeNovoAll { get; } =
            new DeNovoSource("peaks-denovo-all", "de novo all", "*.denovo.csv", "ALC (%)",
                isDenovoOnly: false);

        /// <summary>The DeepNovo export, confidence "SCORE (%)" (a different scale from ALC).</summary>
        public static DeNovoSource DeepNovo { get; } =
            new DeNovoSource("deepnovo", "DeepNovo", "*.deepnovo.csv", "SCORE (%)", isDenovoOnly: false);

        public static IReadOnlyList<DeNovoSource> Presets { get; } =
            new[] { DenovoOnly, DeNovoAll, DeepNovo };

        public static DeNovoSource? FromId(string? id) => Presets.FirstOrDefault(s =>
            string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        private static string MakeId(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;
            var chars = displayName.Trim().ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                .ToArray();
            return new string(chars).Trim('-');
        }
    }

    /// <summary>
    /// One source selected for a computation. The minimum applies to that source's own confidence field;
    /// a PEAKS ALC cutoff and a DeepNovo SCORE cutoff are deliberately two different settings even when
    /// their numeric values happen to be equal. Priority is an explicit tie breaker for incompatible
    /// sequence calls on one precursor, not a claim that either score scale is superior.
    /// </summary>
    public sealed class DeNovoSourceSelection
    {
        public DeNovoSourceSelection(DeNovoSource source, double minimumConfidence, int priority = 0)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (minimumConfidence < 0 || minimumConfidence > 100 || double.IsNaN(minimumConfidence))
                throw new ArgumentOutOfRangeException(nameof(minimumConfidence),
                    "A source confidence cutoff must be in [0, 100].");
            if (priority < 0) throw new ArgumentOutOfRangeException(nameof(priority));

            MinimumConfidence = minimumConfidence;
            Priority = priority;
        }

        public DeNovoSource Source { get; }
        public double MinimumConfidence { get; }
        public int Priority { get; }

        public string CutoffLabel => Source.ConfidenceLabel + " >= " +
            MinimumConfidence.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Reproducible input provenance persisted in manifest.json, one row per source file.</summary>
    public sealed class DeNovoInputManifest
    {
        public string SourceId { get; set; } = string.Empty;
        public string SourceDisplayName { get; set; } = string.Empty;
        public string ConfidenceField { get; set; } = string.Empty;
        public double MinimumConfidence { get; set; }
        public int SourcePriority { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public int RawRows { get; set; }
        public int RowsPassingSourceFilter { get; set; }

        public string CutoffDescription => ConfidenceField + " >= " +
            MinimumConfidence.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One fail-closed discovery path for configured de novo sources. Selecting a caller is an explicit
    /// scientific configuration choice: if its filename glob matches nothing, silently continuing would
    /// relabel a one-source computation as a union. Callers that truly want no de novo input pass a null
    /// de novo directory before reaching this helper.
    /// </summary>
    public static class DeNovoSourceFileDiscovery
    {
        public static IReadOnlyList<string> RequireFiles(string rootDirectory, DeNovoSource source)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A de novo export directory is required.",
                    nameof(rootDirectory));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!Directory.Exists(rootDirectory))
                throw new DirectoryNotFoundException(
                    "The de novo export directory does not exist: " + rootDirectory);

            List<string> files = Directory.GetFiles(rootDirectory, source.FileGlob,
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0)
                throw new FileNotFoundException(
                    "The configured de novo source '" + source.DisplayName + "' matched no files " +
                    "with glob '" + source.FileGlob + "' under " + rootDirectory + ". Nothing was " +
                    "computed because silently omitting a configured caller would mislabel the result.");

            return files;
        }
    }
}
