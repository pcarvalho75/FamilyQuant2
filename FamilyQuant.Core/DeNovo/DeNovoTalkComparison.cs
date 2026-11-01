using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>One row in the deliberately compact talk comparison.  The search-only row has no de
    /// novo source and therefore zero carried-forward de novo sequences and zero model-estimated added
    /// signal.  The other rows all use the same observed search baseline.</summary>
    public sealed class DeNovoTalkComparisonRow
    {
        public string Condition { get; set; } = string.Empty;
        public string Sources { get; set; } = string.Empty;
        public int CarriedForwardDistinctSequences { get; set; }
        public double ObservedSearchAssignedFractionOfTic { get; set; }
        public double DeNovoModelEstimateFractionOfTic { get; set; }
        public double CombinedAssignedFractionOfTic { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Condition))
                throw new InvalidDataException("A compact de novo comparison row needs a condition name.");
            if (CarriedForwardDistinctSequences < 0
                || ObservedSearchAssignedFractionOfTic < 0
                || DeNovoModelEstimateFractionOfTic < 0
                || CombinedAssignedFractionOfTic < 0)
                throw new InvalidDataException("Compact de novo comparison values cannot be negative.");
            if (Math.Abs(CombinedAssignedFractionOfTic -
                    (ObservedSearchAssignedFractionOfTic + DeNovoModelEstimateFractionOfTic)) > 1e-10)
                throw new InvalidDataException(
                    "Compact de novo comparison combined signal does not equal search plus de novo.");
        }
    }

    /// <summary>
    /// Builds the simplified result requested for the talk from the validated five-variant computation.
    /// It intentionally selects PEAKS de-novo-only, DeepNovo, and their exact union by SOURCE
    /// composition, not by a fuzzy display-name match, so PEAKS-all + DeepNovo can never stand in for the
    /// scientifically different de-novo-only union.
    /// </summary>
    public static class DeNovoTalkComparison
    {
        public const string CsvFileName = "de_novo_source_effect_comparison.csv";
        public const string MarkdownFileName = "de_novo_source_effect_comparison.md";

        public static List<DeNovoTalkComparisonRow> Build(
            IEnumerable<DeNovoVariantSummary> variants,
            bool requireCompleteTalkSet = true)
        {
            if (variants == null) throw new ArgumentNullException(nameof(variants));
            List<DeNovoVariantSummary> list = variants.ToList();
            if (requireCompleteTalkSet) DeNovoVariantComparison.ValidateTalkVariantSet(list);
            else
            {
                foreach (DeNovoVariantSummary row in list) row.Validate();
            }

            DeNovoVariantSummary peaks = FindExactSources(list,
                DeNovoSource.DenovoOnly.DisplayName);
            DeNovoVariantSummary deep = FindExactSources(list,
                DeNovoSource.DeepNovo.DisplayName);
            DeNovoVariantSummary union = FindExactSources(list,
                DeNovoSource.DenovoOnly.DisplayName + " + " + DeNovoSource.DeepNovo.DisplayName);

            double baseline = peaks.IdentifiedFractionOfTic;
            foreach (DeNovoVariantSummary row in new[] { deep, union })
                if (Math.Abs(row.IdentifiedFractionOfTic - baseline) > 1e-10)
                    throw new InvalidDataException(
                        "The simplified comparison requires one common observed search baseline.");

            var result = new List<DeNovoTalkComparisonRow>
            {
                new DeNovoTalkComparisonRow
                {
                    Condition = "Search only",
                    Sources = "database search",
                    ObservedSearchAssignedFractionOfTic = baseline,
                    CombinedAssignedFractionOfTic = baseline,
                },
                FromVariant("PEAKS de novo-only", peaks),
                FromVariant("DeepNovo", deep),
                FromVariant("PEAKS de novo-only + DeepNovo", union),
            };
            foreach (DeNovoTalkComparisonRow row in result) row.Validate();
            return result;
        }

        public static string ToCsv(IEnumerable<DeNovoTalkComparisonRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var b = new StringBuilder();
            b.AppendLine("condition,sources,carried_forward_distinct_ptm_cleaned_sequences_no_xic," +
                "observed_search_assigned_fraction_tic,denovo_model_estimate_fraction_tic," +
                "combined_assigned_fraction_tic");
            foreach (DeNovoTalkComparisonRow row in rows)
            {
                row.Validate();
                b.Append(Csv(row.Condition)).Append(',').Append(Csv(row.Sources)).Append(',')
                 .Append(row.CarriedForwardDistinctSequences.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(Num(row.ObservedSearchAssignedFractionOfTic)).Append(',')
                 .Append(Num(row.DeNovoModelEstimateFractionOfTic)).Append(',')
                 .Append(Num(row.CombinedAssignedFractionOfTic)).Append('\n');
            }
            return b.ToString();
        }

        public static string ToMarkdown(IEnumerable<DeNovoTalkComparisonRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var b = new StringBuilder();
            b.AppendLine("# Effect of PEAKS de novo-only and DeepNovo").AppendLine();
            b.AppendLine("Distinct-sequence counts are carried-forward accepted calls collapsed after " +
                "PTM notation removal, pooled over runs and charge states, with I/L kept distinct; they " +
                "do not require an XIC. Signal values use one pooled TIC denominator. Search is observed; " +
                "the added de novo signal is a reference-based model estimate.").AppendLine();
            b.AppendLine("| Condition | Carried-forward distinct sequences | Observed search | De novo model estimate | Combined assigned |");
            b.AppendLine("|---|---:|---:|---:|---:|");
            foreach (DeNovoTalkComparisonRow row in rows)
            {
                row.Validate();
                b.Append("| ").Append(row.Condition).Append(" | ")
                 .Append(row.CarriedForwardDistinctSequences.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                 .Append(Pct(row.ObservedSearchAssignedFractionOfTic)).Append(" | ")
                 .Append(Pct(row.DeNovoModelEstimateFractionOfTic)).Append(" | ")
                 .Append(Pct(row.CombinedAssignedFractionOfTic)).AppendLine(" |");
            }
            return b.ToString();
        }

        private static DeNovoVariantSummary FindExactSources(
            IEnumerable<DeNovoVariantSummary> rows, string sources)
        {
            List<DeNovoVariantSummary> matches = rows.Where(r => string.Equals(
                r.Sources?.Trim(), sources, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1)
                throw new InvalidDataException("Expected exactly one variant with source composition '" +
                    sources + "', found " + matches.Count.ToString(CultureInfo.InvariantCulture) + ".");
            return matches[0];
        }

        private static DeNovoTalkComparisonRow FromVariant(
            string condition, DeNovoVariantSummary variant) => new DeNovoTalkComparisonRow
        {
            Condition = condition,
            Sources = variant.Sources,
            CarriedForwardDistinctSequences = variant.DistinctDeNovoSequencesNoXic,
            ObservedSearchAssignedFractionOfTic = variant.IdentifiedFractionOfTic,
            DeNovoModelEstimateFractionOfTic = variant.DeNovoModelEstimateFractionOfTic,
            CombinedAssignedFractionOfTic = variant.CombinedModelEstimateFractionOfTic,
        };

        private static string Csv(string value) => SpreadsheetSafeCsv.Encode(value, alwaysQuote: true);
        private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Pct(double value) =>
            (100.0 * value).ToString("F2", CultureInfo.InvariantCulture) + "%";
    }
}
