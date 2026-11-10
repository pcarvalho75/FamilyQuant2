using System;
using System.Collections.Generic;

namespace FamilyQuant.Report;

public static partial class HtmlReportPayloadBuilder
{
    private sealed partial class Builder
    {
        private IReadOnlyList<HtmlContributionRow> ContributionRows(IReadOnlyList<HtmlReportFamily> families)
        {
            if (families.Count == 0) return Array.Empty<HtmlContributionRow>();
            HtmlContributionShares? Shares(HtmlFamilyShareStatistics statistics, HtmlFamilyMeanParts? parts) => statistics.Mean == null
                ? null : new(statistics.Mean, parts?.Identified, parts?.DeNovo);
            var rows = new List<HtmlContributionRow>();
            foreach (var family in families)
                rows.Add(new(family.Id, family.IsOthers ? "others" : "family", family.Name)
                {
                    IdentifiedOnly = Shares(family.IdentifiedOnly, family.IdentifiedOnlyParts),
                    IdentifiedAndDeNovo = Shares(family.IdentifiedAndDeNovo, family.IdentifiedAndDeNovoParts),
                    DetectorEnvelopes = Shares(family.DetectorEnvelopes, family.DetectorEnvelopesParts),
                    Tic = Shares(family.Tic, family.TicParts),
                });
            // Stored per-run allocation buckets are absent. Keep both rows with unavailable bases,
            // rather than treating missing buckets as zero or using pooled values for run means.
            foreach (string kind in new[] { "shared", "unattributed" })
            {
                string name = Add(kind == "shared" ? "shared between families" : "unattributed",
                    new() { Kind = "contribution-label", Property = kind });
                rows.Add(new(kind, kind, name));
            }
            return rows;
        }
    }
}
