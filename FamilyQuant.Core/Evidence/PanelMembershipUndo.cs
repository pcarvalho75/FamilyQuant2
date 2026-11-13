using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    public sealed partial class GroupingSnapshot
    {
        // EX2: only the facts a family change moved, by list position, and the fingerprint they had.
        private Dictionary<int, Panels.PanelProteinAssignment>? membershipRows;
        private string? membershipFingerprint;
        private FamilyClusterFile? membershipFile;

        internal void KeepMembershipFacts(FamilyClusterFile file, Dictionary<int, Panels.PanelProteinAssignment> rows)
        {
            membershipFile = file; membershipRows = rows; membershipFingerprint = file.PanelProteinAssignmentsFingerprint;
        }

        private void RestoreMembershipFacts(FamilyClusterFile file)
        {
            var rows = file.PanelProteinAssignments;
            var recipe = file.EvidenceRecipe?.PanelProteinAssignments;
            if (membershipRows == null || !ReferenceEquals(file, membershipFile) || rows == null || recipe == null
                || membershipRows.Keys.Any(position => position >= rows.Count || position >= recipe.Count)) return;
            foreach (var (position, row) in membershipRows)
            {
                rows[position] = Evidence.EvidenceApply.Clone(row);
                recipe[position] = Evidence.EvidenceApply.Clone(row);
            }
            file.PanelProteinAssignmentsFingerprint = file.EvidenceRecipe!.PanelProteinAssignmentsFingerprint = membershipFingerprint;
        }
    }
}
