using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>One fully processed de novo-source variant at a common reporting grain. Representative
    /// counts are the credited post-XIC endpoint after canonical within-side deduplication and cross-side
    /// search-overlap suppression.</summary>
    public sealed class DeNovoVariantSummary
    {
        public string Variant { get; set; } = string.Empty;
        public string GenerationId { get; set; } = string.Empty;
        public string GeneratedUtc { get; set; } = string.Empty;
        public string DatasetFingerprint { get; set; } = string.Empty;
        public string DeNovoInputFingerprint { get; set; } = string.Empty;
        public string QuantitationSettings { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public string Sources { get; set; } = string.Empty;
        public string ConfidenceMetrics { get; set; } = string.Empty;
        public string ConfidenceCutoffNote { get; set; } = string.Empty;
        public double IdentifiedFractionOfTic { get; set; }
        public double DeNovoModelEstimateFractionOfTic { get; set; }
        public double CombinedModelEstimateFractionOfTic { get; set; }
        public double RelativeUplift { get; set; }
        public int SearchRepresentatives { get; set; }
        public int DeNovoRepresentatives { get; set; }
        public int TotalRepresentatives { get; set; }
        public int AcceptedDeNovoSpectrumCalls { get; set; }
        public int DistinctDeNovoSequencesNoXic { get; set; }
        public int CrossSideSuppressed { get; set; }
        public string EnvelopeModelMethod { get; set; } = string.Empty;
        public double EnvelopeModelCrossValidatedWape { get; set; }
        public double CombinedSensitivityLowerFractionOfTic { get; set; }
        public double CombinedSensitivityUpperFractionOfTic { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Variant))
                throw new InvalidOperationException("A variant summary needs a name.");
            if (IdentifiedFractionOfTic < 0 || DeNovoModelEstimateFractionOfTic < 0
                || CombinedModelEstimateFractionOfTic < 0)
                throw new InvalidOperationException("Variant TIC fractions cannot be negative.");
            if (Math.Abs(CombinedModelEstimateFractionOfTic
                    - (IdentifiedFractionOfTic + DeNovoModelEstimateFractionOfTic)) > 1e-10)
                throw new InvalidOperationException("Variant combined fraction does not equal its two parts.");
            if (SearchRepresentatives < 0 || DeNovoRepresentatives < 0 || TotalRepresentatives < 0
                || TotalRepresentatives != SearchRepresentatives + DeNovoRepresentatives)
                throw new InvalidOperationException("Variant representative counts do not close.");
            if (CombinedSensitivityLowerFractionOfTic > CombinedSensitivityUpperFractionOfTic)
                throw new InvalidOperationException("Variant sensitivity interval is reversed.");
        }
    }

    /// <summary>
    /// Writes and reads the exact table behind the FamilyQuant-native source-variant comparison figure.
    /// The table is the supplementary, machine-readable alternative to retyping values in PowerPoint.
    /// </summary>
    public static class DeNovoVariantComparison
    {
        public const int CurrentTalkVariantCount = 5;
        public const string OnlyPlusDeepNovoVariantName = "only+DeepNovo";
        public const string AllPlusDeepNovoVariantName = "all+DeepNovo";
        public const string CsvFileName = "de_novo_variant_comparison.csv";
        public const string MarkdownFileName = "de_novo_variant_comparison.md";
        public const string PerVariantFileName = "variant_summary.csv";

        private static readonly string[] Header =
        {
            "variant", "generation_id", "generated_utc", "dataset_fingerprint",
            "denovo_input_fingerprint", "quantitation_settings", "app_version",
            "sources", "confidence_metrics", "confidence_cutoff_note",
            "identified_fraction_tic", "denovo_model_estimate_fraction_tic",
            "combined_model_estimate_fraction_tic", "relative_uplift",
            "credited_search_representatives", "credited_denovo_representatives",
            "credited_total_representatives",
            "accepted_denovo_spectrum_calls", "distinct_denovo_sequences_no_xic",
            "cross_side_suppressed", "envelope_model_method", "envelope_model_cv_wape",
            "combined_sensitivity_lower_fraction_tic", "combined_sensitivity_upper_fraction_tic",
        };

        public static string ToCsv(IEnumerable<DeNovoVariantSummary> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            List<DeNovoVariantSummary> list = rows.ToList();
            foreach (DeNovoVariantSummary row in list) row.Validate();

            var b = new StringBuilder();
            b.AppendLine(string.Join(",", Header));
            foreach (DeNovoVariantSummary r in list)
            {
                b.AppendLine(string.Join(",", new[]
                {
                    Csv(r.Variant), Csv(r.GenerationId), Csv(r.GeneratedUtc),
                    Csv(r.DatasetFingerprint), Csv(r.DeNovoInputFingerprint),
                    Csv(r.QuantitationSettings), Csv(r.AppVersion),
                    Csv(r.Sources), Csv(r.ConfidenceMetrics), Csv(r.ConfidenceCutoffNote),
                    Num(r.IdentifiedFractionOfTic), Num(r.DeNovoModelEstimateFractionOfTic),
                    Num(r.CombinedModelEstimateFractionOfTic), Num(r.RelativeUplift),
                    r.SearchRepresentatives.ToString(CultureInfo.InvariantCulture),
                    r.DeNovoRepresentatives.ToString(CultureInfo.InvariantCulture),
                    r.TotalRepresentatives.ToString(CultureInfo.InvariantCulture),
                    r.AcceptedDeNovoSpectrumCalls.ToString(CultureInfo.InvariantCulture),
                    r.DistinctDeNovoSequencesNoXic.ToString(CultureInfo.InvariantCulture),
                    r.CrossSideSuppressed.ToString(CultureInfo.InvariantCulture),
                    Csv(r.EnvelopeModelMethod), Num(r.EnvelopeModelCrossValidatedWape),
                    Num(r.CombinedSensitivityLowerFractionOfTic),
                    Num(r.CombinedSensitivityUpperFractionOfTic),
                }));
            }
            return b.ToString();
        }

        public static List<DeNovoVariantSummary> ParseCsv(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0) return new List<DeNovoVariantSummary>();
            List<string> header = ParseLine(lines[0]);
            var index = header.Select((name, i) => new { name, i })
                .ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);
            foreach (string required in Header)
                if (!index.ContainsKey(required))
                    throw new InvalidDataException("Variant CSV is missing column '" + required + "'.");

            var result = new List<DeNovoVariantSummary>();
            foreach (string line in lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                List<string> f = ParseLine(line);
                string S(string name) => f[index[name]];
                double D(string name) => double.Parse(S(name), NumberStyles.Float, CultureInfo.InvariantCulture);
                int I(string name) => int.Parse(S(name), NumberStyles.Integer, CultureInfo.InvariantCulture);
                var row = new DeNovoVariantSummary
                {
                    Variant = S("variant"), Sources = S("sources"),
                    GenerationId = S("generation_id"), GeneratedUtc = S("generated_utc"),
                    DatasetFingerprint = S("dataset_fingerprint"),
                    DeNovoInputFingerprint = S("denovo_input_fingerprint"),
                    QuantitationSettings = S("quantitation_settings"),
                    AppVersion = S("app_version"),
                    ConfidenceMetrics = S("confidence_metrics"),
                    ConfidenceCutoffNote = S("confidence_cutoff_note"),
                    IdentifiedFractionOfTic = D("identified_fraction_tic"),
                    DeNovoModelEstimateFractionOfTic = D("denovo_model_estimate_fraction_tic"),
                    CombinedModelEstimateFractionOfTic = D("combined_model_estimate_fraction_tic"),
                    RelativeUplift = D("relative_uplift"),
                    SearchRepresentatives = I("credited_search_representatives"),
                    DeNovoRepresentatives = I("credited_denovo_representatives"),
                    TotalRepresentatives = I("credited_total_representatives"),
                    AcceptedDeNovoSpectrumCalls = I("accepted_denovo_spectrum_calls"),
                    DistinctDeNovoSequencesNoXic = I("distinct_denovo_sequences_no_xic"),
                    CrossSideSuppressed = I("cross_side_suppressed"),
                    EnvelopeModelMethod = S("envelope_model_method"),
                    EnvelopeModelCrossValidatedWape = D("envelope_model_cv_wape"),
                    CombinedSensitivityLowerFractionOfTic = D("combined_sensitivity_lower_fraction_tic"),
                    CombinedSensitivityUpperFractionOfTic = D("combined_sensitivity_upper_fraction_tic"),
                };
                row.Validate();
                result.Add(row);
            }
            return result;
        }

        /// <summary>Fails closed unless rows are the complete current-generation comparison over one
        /// scientific dataset and one quantitation/model configuration. De novo input fingerprints may
        /// differ by design because they define the variants.</summary>
        public static void ValidateComparableSet(IEnumerable<DeNovoVariantSummary> rows,
                                                 string? expectedGenerationId = null,
                                                 int expectedCount = CurrentTalkVariantCount)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            List<DeNovoVariantSummary> list = rows.ToList();
            if (list.Count != expectedCount)
                throw new InvalidDataException("The variant comparison requires exactly " +
                    expectedCount.ToString(CultureInfo.InvariantCulture) + " current summaries.");
            foreach (DeNovoVariantSummary row in list) row.Validate();
            if (list.Any(r => string.IsNullOrWhiteSpace(r.GenerationId)
                || string.IsNullOrWhiteSpace(r.GeneratedUtc)
                || string.IsNullOrWhiteSpace(r.DatasetFingerprint)
                || string.IsNullOrWhiteSpace(r.DeNovoInputFingerprint)
                || string.IsNullOrWhiteSpace(r.QuantitationSettings)
                || string.IsNullOrWhiteSpace(r.AppVersion)))
                throw new InvalidDataException("A variant summary lacks current-generation provenance.");
            if (!string.IsNullOrWhiteSpace(expectedGenerationId)
                && list.Any(r => !string.Equals(r.GenerationId, expectedGenerationId,
                    StringComparison.Ordinal)))
                throw new InvalidDataException("A stale variant summary belongs to another generation.");
            if (list.Select(r => r.GenerationId).Distinct(StringComparer.Ordinal).Count() != 1
                || list.Select(r => r.DatasetFingerprint).Distinct(StringComparer.Ordinal).Count() != 1
                || list.Select(r => r.QuantitationSettings).Distinct(StringComparer.Ordinal).Count() != 1
                || list.Select(r => r.AppVersion).Distinct(StringComparer.Ordinal).Count() != 1
                || list.Select(r => r.EnvelopeModelMethod).Distinct(StringComparer.Ordinal).Count() != 1)
                throw new InvalidDataException(
                    "Variant summaries do not share one generation, dataset, software/settings and model method.");
            if (list.Select(r => r.Variant).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
                throw new InvalidDataException("Variant names are not unique.");
        }

        /// <summary>
        /// Validates the five named production variants, including the two scientifically different
        /// unions.  The folder/display name is checked together with the exact source composition so a
        /// PEAKS de-novo-only + DeepNovo result cannot be relabelled as PEAKS-all + DeepNovo (or vice
        /// versa) while retaining otherwise plausible numbers.
        /// </summary>
        public static void ValidateTalkVariantSet(IEnumerable<DeNovoVariantSummary> rows,
                                                  string? expectedGenerationId = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            List<DeNovoVariantSummary> list = rows.ToList();
            ValidateComparableSet(list, expectedGenerationId, CurrentTalkVariantCount);

            var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DeNovoSource.DenovoOnly.DisplayName] = DeNovoSource.DenovoOnly.DisplayName,
                [DeNovoSource.DeNovoAll.DisplayName] = DeNovoSource.DeNovoAll.DisplayName,
                [DeNovoSource.DeepNovo.DisplayName] = DeNovoSource.DeepNovo.DisplayName,
                [OnlyPlusDeepNovoVariantName] = DeNovoSource.DenovoOnly.DisplayName + " + " +
                    DeNovoSource.DeepNovo.DisplayName,
                [AllPlusDeepNovoVariantName] = DeNovoSource.DeNovoAll.DisplayName + " + " +
                    DeNovoSource.DeepNovo.DisplayName,
            };

            foreach (KeyValuePair<string, string> required in expected)
            {
                DeNovoVariantSummary? row = list.SingleOrDefault(r => string.Equals(
                    r.Variant, required.Key, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                    throw new InvalidDataException("The talk comparison is missing variant '" +
                        required.Key + "'.");
                if (!string.Equals(row.Sources?.Trim(), required.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Variant '" + required.Key + "' must contain exactly '" +
                        required.Value + "', but its source composition is '" + row.Sources + "'.");
            }
        }

        public static string ToMarkdown(IEnumerable<DeNovoVariantSummary> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            List<DeNovoVariantSummary> list = rows.ToList();
            foreach (DeNovoVariantSummary row in list) row.Validate();
            var b = new StringBuilder();
            b.AppendLine("# De novo source variants: quantified recovery");
            b.AppendLine();
            b.AppendLine("All percentages are shares of the same pooled TIC. The de novo segment is a " +
                "reference-envelope **model estimate**, not an observed or empirically calibrated envelope. " +
                "ALC and DeepNovo SCORE are source-specific metrics and are not placed on a common confidence scale.");
            b.AppendLine();
            b.AppendLine("| Variant | Confidence rule | Search assigned | De novo model estimate | Combined | Relative uplift | Credited post-XIC representatives | Distinct de novo sequences (no XIC) | Model sensitivity |");
            b.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (DeNovoVariantSummary r in list)
            {
                b.Append("| ").Append(r.Variant).Append(" | ").Append(r.ConfidenceCutoffNote).Append(" | ")
                 .Append(Pct(r.IdentifiedFractionOfTic)).Append(" | ")
                 .Append(Pct(r.DeNovoModelEstimateFractionOfTic)).Append(" | ")
                 .Append(Pct(r.CombinedModelEstimateFractionOfTic)).Append(" | ")
                 .Append(Pct(r.RelativeUplift)).Append(" | ")
                 .Append(r.TotalRepresentatives.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                 .Append(r.DistinctDeNovoSequencesNoXic.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                 .Append(Pct(r.CombinedSensitivityLowerFractionOfTic)).Append("-")
                 .Append(Pct(r.CombinedSensitivityUpperFractionOfTic)).AppendLine(" |");
            }
            b.AppendLine();
            b.AppendLine("Representative counts use the credited run × canonical peptidoform × charge × " +
                "precursor-mass-cluster endpoint after cross-side search-overlap suppression.");
            b.AppendLine();
            b.AppendLine("Model validation column in the CSV: leave-one-representative-out weighted absolute " +
                "prediction error on the identified reference set. The pooled-vs-bin sensitivity range is " +
                "not a confidence interval.");
            return b.ToString();
        }

        public static void Write(string path, IEnumerable<DeNovoVariantSummary> rows)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A CSV path is required.", nameof(path));
            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, ToCsv(rows), new UTF8Encoding(false));
        }

        public static List<DeNovoVariantSummary> Read(string path) => ParseCsv(File.ReadAllText(path));

        private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Pct(double value) => (100.0 * value).ToString("F2", CultureInfo.InvariantCulture) + "%";
        private static string Csv(string value) => SpreadsheetSafeCsv.Encode(value, alwaysQuote: true);

        private static List<string> ParseLine(string line)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }
            values.Add(current.ToString());
            return values;
        }
    }
}
