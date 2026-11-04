using FamilyQuant.Report;
using System;
using System.Globalization;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class ReportNumberTrace
{
    private sealed class DatabaseEntryBindings(FamilyClusterFile? project, QuantManifest? manifest)
    {
        private FamilyDatabaseEntryCountView? counts;
        private ReportCompositionBasis? basis;

        internal HtmlBound Resolve(HtmlReportReference source, HtmlReportField field)
        {
            if (project == null) throw new InvalidDataException("Database population requires its stored source project.");
            if (source.Property != "/project/MyExtendedFastaItems" || source.Scale != 1 || source.Format != null
                || source.Table != null || source.Row != null || source.Column != null || source.Sheet != null
                || source.Figure != null || source.Run != null || source.ParagraphText != null || source.Occurrence != 0
                || field.Unit != "") throw new InvalidDataException("Invalid database-entry source descriptor or unit.");
            counts ??= FamilyDatabaseEntryCounts.Read(project);
            int value;
            if (source.Study == "UserID")
            {
                if (!CanonicalId(source.Family, out int id) || !counts.ByUserId.TryGetValue(id, out value))
                    throw new InvalidDataException("Recorded source family is unavailable.");
            }
            else if (source.Study == "panel-key")
            {
                if (!counts.PanelMappingCurrent || source.Family == null || !counts.ByPanelKey.TryGetValue(source.Family, out int? panel)
                    || !panel.HasValue) throw new InvalidDataException("Effective panel population is unavailable or conflicting.");
                value = panel.Value;
            }
            else throw new InvalidDataException("Unknown database population basis.");
            return new(value.ToString(CultureInfo.InvariantCulture), value, "project", source.Study + " " + source.Family);
        }

        internal void CheckFamily(HtmlReportFamily family, HtmlReportField field)
        {
            if (family.IsOthers || field.Source.Kind != "database-entry-count" || !CanonicalId(family.Id, out int id))
                throw new InvalidDataException("Database population must belong to a non-Others headline family.");
            if (project == null) throw new InvalidDataException("Database population requires its stored source project.");
            if (project.FamilyPanel == null)
            {
                if (field.Source.Study != "UserID" || field.Source.Family != family.Id)
                    throw new InvalidDataException("Database count belongs to another source family.");
                return;
            }
            if (manifest == null) throw new InvalidDataException("Panel headline association requires the saved study manifest.");
            basis ??= ReportCompositionBasis.Select(project, FamilyUnobservedStudies.View(project, manifest));
            if (basis.Map == null || !basis.Map.UserFamilies.TryGetValue(id, out var mapped) || mapped.HasConflict)
                throw new InvalidDataException("Headline panel association is unavailable or conflicting.");
            if (mapped.PanelFamilyKey is string key)
            {
                if (field.Source.Study != "panel-key" || field.Source.Family != key)
                    throw new InvalidDataException("Database count belongs to another effective panel key.");
            }
            else
            {
                int sourceId = id;
                if (!basis.IsIdentity)
                {
                    if (!basis.SourceUserIds.TryGetValue(id, out var ids) || ids.Count != 1)
                        throw new InvalidDataException("Unkeyed slot has no canonical single source family.");
                    sourceId = ids[0];
                }
                if (field.Source.Study != "UserID" || field.Source.Family != sourceId.ToString(CultureInfo.InvariantCulture))
                    throw new InvalidDataException("Database count does not match the unkeyed slot's source family.");
            }
        }

        private static bool CanonicalId(string? text, out int id) => int.TryParse(text, NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out id) && id.ToString(CultureInfo.InvariantCulture) == text;
    }
}
