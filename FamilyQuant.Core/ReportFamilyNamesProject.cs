using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace FamilyQuant
{
    public enum ReportFamilyNameChoice { Name, OtherLanguageName }

    public partial class FamilyClusterFile
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public ReportFamilyNameChoice ReportFamilyNames { get; set; }
    }
}

namespace FamilyQuant.Report
{
    public static class ReportFamilyNames
    {
        public static string DisplayName(FamilyPanel panel, FamilyPanelFamily family, ReportFamilyNameChoice choice)
        {
            string other = choice == ReportFamilyNameChoice.OtherLanguageName ? PanelImportFields.OtherNameText(panel, family) : "";
            return string.IsNullOrWhiteSpace(other) ? family.DisplayName : other;
        }

        public static Dictionary<int, string> SourceLabels(FamilyClusterFile project, IReadOnlyDictionary<int, string>? source = null)
        {
            var labels = new Dictionary<int, string>(source ?? FamilyNaming.Names(project.MyExtendedFastaItems
                .Where(protein => !SequenceLabels.IsDecoy(protein.SequenceIdentifier))));
            if (project.FamilyPanel == null || project.ReportFamilyNames != ReportFamilyNameChoice.OtherLanguageName) return labels;
            var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(project);
            if (!map.IsCurrent) return labels;
            var families = project.FamilyPanel.Families.ToDictionary(family => family.Key, StringComparer.Ordinal);
            foreach (var member in map.UserFamilies.Values.Where(member => !member.HasConflict && member.PanelFamilyKey != null))
                if (families.TryGetValue(member.PanelFamilyKey!, out var family))
                    labels[member.FamilyUserId] = DisplayName(project.FamilyPanel, family, project.ReportFamilyNames);
            return labels;
        }
    }
}
