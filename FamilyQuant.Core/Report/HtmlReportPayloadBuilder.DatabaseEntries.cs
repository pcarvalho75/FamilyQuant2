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
                int? count;
                string sourceFamily, basis;
                if (context.UsesPanel)
                {
                    if (!view.PanelMappingCurrent || !context.PanelKeys.TryGetValue(family.Id, out string? key)
                        || !view.ByPanelKey.TryGetValue(key, out count) || !count.HasValue) return family;
                    sourceFamily = key; basis = "panel-key";
                }
                else
                {
                    if (!int.TryParse(family.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                        || !view.ByUserId.TryGetValue(id, out int value)) return family;
                    count = value; sourceFamily = id.ToString(CultureInfo.InvariantCulture); basis = "UserID";
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
