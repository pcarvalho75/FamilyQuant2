using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyQuant.Panels
{
    public enum PanelDerivedDataStatus { NotComputed, Current, OutOfDate }
    public sealed record PanelDerivedDataCheck(PanelDerivedDataStatus FamilyAssignments,
        PanelDerivedDataStatus SubclassLabels, string Message)
    {
        public bool IsOutOfDate => FamilyAssignments == PanelDerivedDataStatus.OutOfDate || SubclassLabels == PanelDerivedDataStatus.OutOfDate;
    }

    /// <summary>One check after a panel edit. Family keys, decisions, domains, subclass rules and
    /// confirmed links are scientific inputs; names and named report text do not change membership.</summary>
    public static class PanelDerivedDataCurrentness
    {
        public static string Fingerprint(FamilyPanel panel) => FingerprintCore(panel, includeSubclasses: true);
        public static string FamilyFingerprint(FamilyPanel panel) => FingerprintCore(panel, includeSubclasses: false);

        private static string FingerprintCore(FamilyPanel panel, bool includeSubclasses)
        {
            ArgumentNullException.ThrowIfNull(panel);
            var data = new
            {
                Families = panel.Families.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new
                {
                    f.Key, f.Decision, f.MergeTargetFamilyKey,
                    Pfam = f.Pfam.Select(d => d.Accession).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                    Subclasses = f.Subclasses.Where(_ => includeSubclasses).OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new
                    {
                        s.Key, s.ReportAsSubclassKey,
                        Required = s.RequiredPfam.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                        Absent = s.AbsentPfam.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                        SourceText = s.UniProtSubfamilies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                    }),
                }),
                Links = panel.EvidenceLinks.OrderBy(l => l.SourceFamily, StringComparer.Ordinal).ThenBy(l => l.ArchitectureKey, StringComparer.Ordinal),
            };
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
        }

        public static PanelDerivedDataCheck Check(FamilyClusterFile file)
        {
            string? current = file.FamilyPanel == null ? null : Fingerprint(file.FamilyPanel);
            string? familyCurrent = file.FamilyPanel == null ? null : FamilyFingerprint(file.FamilyPanel);
            PanelDerivedDataStatus Status(bool present, string? computed, string? expected) => !present ? PanelDerivedDataStatus.NotComputed
                : expected != null && computed == expected ? PanelDerivedDataStatus.Current : PanelDerivedDataStatus.OutOfDate;
            var families = Status(file.PanelProteinAssignments != null, file.PanelProteinAssignmentsPanelFingerprint, familyCurrent);
            var subclasses = Status(file.ProteinSubclassLabels != null, file.ProteinSubclassLabelsPanelFingerprint, current);
            string message = families == PanelDerivedDataStatus.OutOfDate
                ? "Panel family rules changed. Rebuild family assignments and subclass labels."
                : subclasses == PanelDerivedDataStatus.OutOfDate ? "Subclass rules changed. Recompute subclass labels."
                : families == PanelDerivedDataStatus.NotComputed || subclasses == PanelDerivedDataStatus.NotComputed
                    ? "Some panel assignments or subclass labels have not been computed."
                    : "Family assignments and subclass labels match the current panel rules.";
            return new(families, subclasses, message);
        }
    }
}

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PanelProteinAssignmentsPanelFingerprint { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProteinSubclassLabelsPanelFingerprint { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProteinSubclassLabelsFingerprint { get; set; }
    }
}

namespace FamilyQuant.Evidence
{
    public sealed partial class EvidenceRecipe
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PanelProteinAssignmentsPanelFingerprint { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProteinSubclassLabelsPanelFingerprint { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProteinSubclassLabelsFingerprint { get; set; }
    }
}
