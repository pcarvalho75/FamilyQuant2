using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant;

public sealed record FamilySensitivityTableRow(int FamilyUserId, string Description, SharedPeptidePolicy Policy,
    double? FirstShare, double? SecondShare);

/// <summary>Reads the stored study only. It never runs extraction, changes a cutoff or fills an
/// uncomputed variant with zero. A computed variant can establish zero for an absent family.</summary>
public static class FamilySensitivityTable
{
    public const string Explanation = "Shares of measured peptide signal. Full-count columns overlap and must not be summed; unavailable means not computed.";

    public static IReadOnlyList<FamilySensitivityTableRow> Rows(FamilySensitivityResult? result)
    {
        if (result == null) return Array.Empty<FamilySensitivityTableRow>();
        Validate(result);
        var families = result.Variants.SelectMany(v => v.Policies).SelectMany(p => p.Families)
            .GroupBy(f => f.FamilyUserId).OrderBy(g => g.Key).Select(g => (Id: g.Key, Description: g.First().Description)).ToArray();
        return families.SelectMany(f => Enum.GetValues<SharedPeptidePolicy>().Select(policy => new FamilySensitivityTableRow(
            f.Id, f.Description, policy, Share(0, policy, f.Id), Share(1, policy, f.Id)))).ToArray();

        double? Share(int index, SharedPeptidePolicy policy, int family)
        {
            if (index >= result.Variants.Count) return null;
            var column = result.Variants[index].Policies.Single(p => p.Policy == policy);
            return column.MeasuredSignal > 0 ? column.Families.SingleOrDefault(f => f.FamilyUserId == family)?.ShareOfMeasuredSignal ?? 0 : null;
        }
    }

    public static string BuildCsv(FamilySensitivityResult result)
    {
        var rows = Rows(result);
        var text = new StringBuilder("family_id,family,policy,variant,source_sha256,signal,measured_signal,share_of_measured_signal,status\n");
        foreach (var row in rows)
            for (int i = 0; i < 2; i++)
            {
                var variant = i < result.Variants.Count ? result.Variants[i] : null;
                var policy = variant?.Policies.Single(p => p.Policy == row.Policy);
                var family = policy?.Families.SingleOrDefault(f => f.FamilyUserId == row.FamilyUserId);
                double? share = i == 0 ? row.FirstShare : row.SecondShare;
                string[] fields = { row.FamilyUserId.ToString(CultureInfo.InvariantCulture), row.Description, row.Policy.ToString(),
                    variant?.Label ?? "Comparison unavailable", variant?.SourceSha256 ?? string.Empty,
                    policy == null ? "" : (family?.Signal ?? 0).ToString("R", CultureInfo.InvariantCulture),
                    policy?.MeasuredSignal.ToString("R", CultureInfo.InvariantCulture) ?? "",
                    share?.ToString("R", CultureInfo.InvariantCulture) ?? "", share.HasValue ? "computed" : variant == null ? "not computed" : "zero measured denominator" };
                text.AppendJoin(',', fields.Select(Quote)).Append('\n');
            }
        return text.ToString();
    }

    public static void Validate(FamilySensitivityResult result)
    {
        if (result.Variants.Count is < 1 or > 2 || result.Variants.Select(v => v.Key).Distinct(StringComparer.Ordinal).Count() != result.Variants.Count)
            throw new InvalidDataException("A sensitivity table needs one or two distinct computed variants.");
        foreach (var variant in result.Variants)
        {
            if (string.IsNullOrWhiteSpace(variant.Key) || variant.SourceSha256.Length != 64 || !variant.SourceSha256.All(Uri.IsHexDigit)
                || !variant.Policies.Select(p => p.Policy).OrderBy(p => p).SequenceEqual(Enum.GetValues<SharedPeptidePolicy>().OrderBy(p => p)))
                throw new InvalidDataException("The stored sensitivity variant is incomplete.");
            foreach (var policy in variant.Policies)
            {
                if (!double.IsFinite(policy.MeasuredSignal) || policy.MeasuredSignal < 0
                    || policy.Families.Select(f => f.FamilyUserId).Distinct().Count() != policy.Families.Count)
                    throw new InvalidDataException("The stored sensitivity denominator or family list is invalid.");
                foreach (var family in policy.Families)
                    if (!double.IsFinite(family.Signal) || family.Signal < 0 || family.ShareOfMeasuredSignal !=
                        (policy.MeasuredSignal > 0 ? family.Signal / policy.MeasuredSignal : (double?)null))
                        throw new InvalidDataException("A stored sensitivity share disagrees with its signal and denominator.");
            }
        }
    }

    private static string Quote(string value) => value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
        ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
