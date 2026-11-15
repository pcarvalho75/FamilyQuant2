using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant
{
    /// <summary>
    /// Builds the exported report. Kept apart from the window so the formatting - the part that broke
    /// silently on locales with a comma decimal separator - can be exercised without a UI.
    ///
    /// TWO DIFFERENT DECISIONS, BOTH DELIBERATE. On screen the numbers are percentages formatted in the
    /// machine's own language, because a person reads them. In this file they are fractions between 0
    /// and 1 written invariantly, with a dot, because a program reads them. Neither is a bug in the
    /// other's terms, and the export dialog says so in words.
    /// </summary>
    public static class CsvReport
    {
        /// <summary>
        /// The fixed columns. The two family-share columns are named separately, because their heading
        /// has to state the denominator that was actually in use - "XIC Original" named neither the
        /// quantity nor its unit, and a reader could not tell 0.121109 from 12.1109%.
        /// </summary>
        private static string[] HeadersFor(NormalizationMode? mode, string? fullXicHeader)
        {
            return new[]
            {
                "USearch ID",
                "User ID",
                "User Annotation",
                "Locus",
                "Residue mass (Da)",
                mode.CsvHeader(),
                fullXicHeader ?? NoFullXicHeader,
                "Description"
            };
        }

        /// <summary>The full-XIC column's heading when nothing was computed for this project.</summary>
        public const string NoFullXicHeader = "Full XIC family share, fraction 0-1, not computed";

        /// <summary>
        /// Exports without naming a denominator. Used when nothing has been calculated yet, and by
        /// callers that have no mode to declare; the share column then says so rather than implying
        /// one.
        /// </summary>
        public static string Build(IEnumerable<FastaItemExtended> items)
        {
            return Build(items, null);
        }

        public static string Build(IEnumerable<FastaItemExtended> items, NormalizationMode? mode,
                                   string? fullXicHeader = null)
        {
            var csv = new StringBuilder();

            csv.AppendLine(string.Join(",", HeadersFor(mode, fullXicHeader).Select(Field)));

            foreach (var item in items)
            {
                // Numbers use InvariantCulture: with the machine's own culture, a comma decimal separator
                // ("0,0123") collided with the delimiter and shifted every later column of that row.
                string[] row =
                {
                    item.USearchID.ToString(CultureInfo.InvariantCulture),
                    item.UserID.ToString(CultureInfo.InvariantCulture),
                    Field(item.UserAnnotation),
                    Field(item.SequenceIdentifier),
                    item.MonoisotopicMass.ToString(CultureInfo.InvariantCulture),
                    item.XICUser.ToString(CultureInfo.InvariantCulture),
                    item.FullXicShare?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    Field(item.Description)
                };

                csv.AppendLine(string.Join(",", row));
            }

            return csv.ToString();
        }

        /// <summary>
        /// One row per family plus the rows that make the composition close: the family composition a
        /// pie chart is drawn from, one family per slice. Under the family-unique rule the peptides shared
        /// by several families are their own row ("shared between families"), as the users asked; under
        /// split-equally they are already inside the families; under count-in-every-family the family rows
        /// overlap, which the basis column says. "Not attributed to any family" closes the total. Fractions
        /// are of <paramref name="denominator"/>, named in <paramref name="basis"/>.
        /// </summary>
        public static string BuildFamilyComposition(FamilyEnvelopeQuantResult result, SharedPeptidePolicy policy,
                                                    double denominator, string basis)
            => BuildFamilyComposition(result, policy, denominator, basis, null);

        /// <summary>New output callers may format only the name field; the historical overload keeps its bytes.</summary>
        public static string BuildFamilyComposition(FamilyEnvelopeQuantResult result, SharedPeptidePolicy policy,
                                                    double denominator, string basis, System.Func<string?, string>? formatName)
            => BuildFamilyComposition(result, policy, denominator, basis, formatName, null);

        public static string BuildFamilyComposition(FamilyEnvelopeQuantResult result, SharedPeptidePolicy policy,
            double denominator, string basis, System.Func<string?, string>? formatName, IReadOnlyDictionary<int, string>? familyLabels,
            IReadOnlySet<int>? contaminantFamilyIds = null)
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", new[]
            {
                "family_user_id", "description", "signal_primary_estimate", "fraction", "basis",
            }.Select(Field)));

            string Num(double v) => v.ToString(CultureInfo.InvariantCulture);
            string Fraction(double v) => denominator > 0 ? Num(v / denominator) : string.Empty;
            string basisField = Field(basis + "; " + policy.ShortName() +
                (policy == SharedPeptidePolicy.FamilyUnique ? ", shared ones in their own row" : string.Empty));

            double contaminants = 0;
            int contaminantCount = 0;
            foreach (FamilyEnvelopeQuantRow f in result.Families
                .OrderByDescending(f => f.Signal(policy)).ThenBy(f => f.FamilyUserId))
            {
                if (contaminantFamilyIds?.Contains(f.FamilyUserId) == true)
                {
                    contaminants += f.Signal(policy);
                    contaminantCount++;
                    continue;
                }
                csv.AppendLine(string.Join(",", f.FamilyUserId.ToString(CultureInfo.InvariantCulture),
                    Field(formatName == null ? familyLabels?.GetValueOrDefault(f.FamilyUserId, f.Description) ?? f.Description
                        : formatName(familyLabels?.GetValueOrDefault(f.FamilyUserId, f.Description) ?? f.Description)), Num(f.Signal(policy)), Fraction(f.Signal(policy)), basisField));
            }

            if (contaminantCount > 0)
                csv.AppendLine(string.Join(",", string.Empty, Field("contaminants"), Num(contaminants), Fraction(contaminants), basisField));

            if (policy == SharedPeptidePolicy.FamilyUnique)
                csv.AppendLine(string.Join(",", string.Empty, Field("shared between families"),
                    Num(result.SharedTotalEnvelopeReferenceCalibrated),
                    Fraction(result.SharedTotalEnvelopeReferenceCalibrated), basisField));

            csv.AppendLine(string.Join(",", string.Empty, Field("not attributed to any family"),
                Num(result.UnattributedTotalEnvelopeReferenceCalibrated),
                Fraction(result.UnattributedTotalEnvelopeReferenceCalibrated), basisField));

            return csv.ToString();
        }

        /// <summary>
        /// Quotes a field and escapes embedded quotes by doubling them (RFC 4180). Protein descriptions
        /// routinely contain commas and quotes, which previously broke the column layout.
        /// </summary>
        private static string Field(string value) => CsvFormatting.AlwaysQuoted(value);

    }
}
