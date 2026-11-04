using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class HtmlReportPayloadBuilder
{
    private sealed partial class Builder
    {
        private IReadOnlyList<HtmlReportFamily> DatabaseEntryFamilies(IReadOnlyList<HtmlReportFamily> families)
        {
            var view = context.DatabaseEntryCounts;
            if (view == null) return families;
            return families.Select(family =>
            {
                if (family.IsOthers) return family;
                if (!int.TryParse(family.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int familyId)) return family;
                if (context.UsesPanel && (!view.PanelMappingCurrent || context.ConflictingFamilyIds.Contains(familyId))) return family;
                int? count;
                string sourceFamily, basis;
                if (context.UsesPanel && context.PanelKeys.TryGetValue(family.Id, out string? key))
                {
                    if (!view.ByPanelKey.TryGetValue(key, out count) || !count.HasValue) return family;
                    sourceFamily = key; basis = "panel-key";
                }
                else
                {
                    int sourceId = familyId;
                    if (context.UsesPanel && !context.IsIdentity)
                    {
                        if (!context.SourceUserIds.TryGetValue(familyId, out var sourceIds) || sourceIds.Count != 1) return family;
                        sourceId = sourceIds[0];
                    }
                    if (!view.ByUserId.TryGetValue(sourceId, out int value)) return family;
                    count = value; sourceFamily = sourceId.ToString(CultureInfo.InvariantCulture); basis = "UserID";
                }
                string field = Add(count.Value.ToString(CultureInfo.InvariantCulture), new()
                {
                    Kind = "database-entry-count", Property = "/project/MyExtendedFastaItems",
                    Study = basis, Family = sourceFamily,
                }, "", count.Value);
                return family with { DatabaseEntries = field };
            }).ToArray();
        }
    }
}
