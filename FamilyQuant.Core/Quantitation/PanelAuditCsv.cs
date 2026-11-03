using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;

namespace FamilyQuant
{
    /// <summary>Actual family-assignment evidence and reason supplied by the assignment producer.</summary>
    public sealed class PanelFamilyAuditEvidence
    {
        public int ProteinIndex { get; set; }
        public string Identifier { get; set; } = string.Empty;
        public string EvidenceUsed { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>One row for every target, including unassigned proteins. Recorded evidence is not
    /// described as used unless the family-assignment producer supplies that fact.</summary>
    public static class PanelAuditCsv
    {
        public static string BuildCsv(FamilyClusterFile file) => BuildCsv(file, Array.Empty<PanelFamilyAuditEvidence>());

        public static string BuildCsv(FamilyClusterFile file, IReadOnlyList<PanelFamilyAuditEvidence> assignments)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(assignments);
            bool subclassCurrent = SubclassAssignment.IsCurrent(file);
            var labels = (subclassCurrent ? file.ProteinSubclassLabels! : new List<ProteinSubclassLabel>()).ToDictionary(x => x.ProteinIndex);
            var currentAssignments = PanelAssignmentEvidenceStore.Current(file);
            var currentByIndex = currentAssignments?.ToDictionary(x => x.ProteinIndex) ?? new Dictionary<int, PanelProteinAssignment>();
            var conflicts = currentAssignments == null || file.FamilyPanel == null ? new HashSet<int>()
                : PanelAssignmentEvidenceStore.BuildFamilyMap(file.FamilyPanel, currentAssignments).ConflictingUserIds.ToHashSet();
            var audit = assignments.ToDictionary(x => x.ProteinIndex);
            foreach (var pair in labels.Select(x => (x.Key, x.Value.Identifier)).Concat(audit.Select(x => (x.Key, x.Value.Identifier))))
            {
                if (pair.Key < 0 || pair.Key >= file.MyExtendedFastaItems.Count
                    || file.MyExtendedFastaItems[pair.Key].SequenceIdentifier != pair.Identifier
                    || SequenceLabels.IsDecoy(pair.Identifier))
                    throw new InvalidOperationException("Protein audit data does not match the current target list.");
            }
            var csv = new StringBuilder();
            void Append(params string?[] cells) => csv.Append(string.Join(",", cells.Select(x => SpreadsheetSafeCsv.Encode(x)))).Append("\r\n");
            Append("Accession", "Protein name", "Panel family", "Subclass", "Family evidence used", "Family reason", "Subclass reason", "Evidence recorded");
            for (int i = 0; i < file.MyExtendedFastaItems.Count; i++)
            {
                var item = file.MyExtendedFastaItems[i];
                if (SequenceLabels.IsDecoy(item.SequenceIdentifier ?? string.Empty)) continue;
                labels.TryGetValue(i, out var label);
                audit.TryGetValue(i, out var assignment);
                currentByIndex.TryGetValue(i, out var currentFamily);
                if ((label != null && label.Identifier != item.SequenceIdentifier)
                    || (assignment != null && assignment.Identifier != item.SequenceIdentifier))
                    throw new InvalidOperationException("Protein audit data does not match the current protein list.");
                FamilyPanelFamily? family = null;
                string? familyKey = conflicts.Contains(item.UserID) ? null : label?.PanelFamilyKey
                    ?? (currentFamily?.IsApplied == true ? currentFamily.PanelFamilyKey : null);
                if (familyKey != null && file.FamilyPanel != null)
                    familyKey = FamilyPanelValidation.ResolveFamilyKey(file.FamilyPanel, familyKey);
                if (familyKey != null)
                    family = file.FamilyPanel?.Families.SingleOrDefault(x => x.Key == familyKey)
                        ?? throw new InvalidOperationException("A protein label refers to a missing panel family.");
                FamilyPanelSubclass? subclass = null;
                if (label?.SubclassKey != null)
                    subclass = family?.Subclasses.SingleOrDefault(x => x.Key == label.SubclassKey)
                        ?? throw new InvalidOperationException("A protein label refers to a missing panel subclass.");
                var evidence = item.Evidence;
                var recorded = new List<string>();
                if (!string.IsNullOrEmpty(evidence?.FamilyAnnotation)) recorded.Add("Family: " + evidence.FamilyAnnotation);
                if (!string.IsNullOrEmpty(evidence?.Subfamily)) recorded.Add("Subfamily: " + evidence.Subfamily);
                if (evidence?.Pfam != null && evidence.Pfam.Count > 0)
                    recorded.Add("Pfam: " + string.Join("; ", evidence.Pfam.Select(x => x.Accession)));
                Append(FamilyNaming.Accession(item.SequenceIdentifier), ProteinNameFormatting.ForNewOutput(item.Description),
                    family?.DisplayName, subclass?.DisplayName, assignment?.EvidenceUsed, assignment?.Reason,
                    conflicts.Contains(item.UserID) ? "The current User ID has conflicting panel family or outside members."
                        : label?.Reason ?? (subclassCurrent ? "" : "Subclass labels are unavailable or out of date."), string.Join(" | ", recorded));
            }
            return csv.ToString();
        }
    }
}

