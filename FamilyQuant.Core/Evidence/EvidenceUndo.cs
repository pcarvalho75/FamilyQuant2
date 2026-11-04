using System;
using System.Linq;

namespace FamilyQuant
{
    public sealed partial class GroupingSnapshot
    {
        private (FastaItemExtended Protein, FamilyAssignmentState Assignment, int SearchId, string Annotation)[]? evidenceRows;
        private Evidence.EvidenceRecipe? evidenceRecipe;
        private FamilyClusterFile? evidenceFile;
        private Evidence.EvidenceProvenance? evidenceSource;
        private System.Collections.Generic.List<Evidence.MotifPattern>? evidenceMotifs;
        private Panels.FamilyPanel? evidencePanel;
        private System.Collections.Generic.List<Panels.PanelProteinAssignment>? panelAssignments;
        private string? panelAssignmentsFingerprint;
        private string? panelAssignmentsPanelFingerprint;
        private System.Collections.Generic.List<Panels.ProteinSubclassLabel>? subclassLabels;
        private string? subclassLabelsPanelFingerprint;
        private string? subclassLabelsFingerprint;

        public static GroupingSnapshot CaptureEvidence(FamilyClusterFile file)
        {
            ArgumentNullException.ThrowIfNull(file);
            GroupingSnapshot snapshot = Capture(file);
            snapshot.evidenceFile = file;
            snapshot.evidenceRows = file.MyExtendedFastaItems.Select(p => (p, p.AssignmentState, p.USearchID, p.UserAnnotation)).ToArray();
            snapshot.evidenceRecipe = file.EvidenceRecipe == null ? null : Evidence.EvidenceApply.Clone(file.EvidenceRecipe);
            snapshot.evidenceSource = file.EvidenceSource == null ? null : Evidence.EvidenceApply.Clone(file.EvidenceSource);
            snapshot.evidenceMotifs = file.Motifs == null ? null : Evidence.EvidenceApply.Clone(file.Motifs);
            snapshot.evidencePanel = file.FamilyPanel == null ? null : Panels.FamilyPanelValidation.Clone(file.FamilyPanel);
            snapshot.panelAssignments = file.PanelProteinAssignments == null ? null : Evidence.EvidenceApply.Clone(file.PanelProteinAssignments);
            snapshot.panelAssignmentsFingerprint = file.PanelProteinAssignmentsFingerprint;
            snapshot.panelAssignmentsPanelFingerprint = file.PanelProteinAssignmentsPanelFingerprint;
            snapshot.subclassLabels = file.ProteinSubclassLabels == null ? null : Evidence.EvidenceApply.Clone(file.ProteinSubclassLabels);
            snapshot.subclassLabelsPanelFingerprint = file.ProteinSubclassLabelsPanelFingerprint;
            snapshot.subclassLabelsFingerprint = file.ProteinSubclassLabelsFingerprint;
            return snapshot;
        }

        private bool RestoreEvidence(FamilyClusterFile file)
        {
            if (evidenceRows == null) return false;
            if (!ReferenceEquals(file, evidenceFile) || file.MyExtendedFastaItems.Count != evidenceRows.Length
                || evidenceRows.Where((row, index) => !ReferenceEquals(row.Protein, file.MyExtendedFastaItems[index])).Any())
                throw new InvalidOperationException("The proteins changed since the evidence transaction; its undo cannot be restored.");
            Evidence.EvidenceRecipe? restored = evidenceRecipe == null ? null : Evidence.EvidenceApply.Clone(evidenceRecipe);
            Evidence.EvidenceProvenance? restoredSource = evidenceSource == null ? null : Evidence.EvidenceApply.Clone(evidenceSource);
            var restoredMotifs = evidenceMotifs == null ? null : Evidence.EvidenceApply.Clone(evidenceMotifs);
            var restoredPanel = evidencePanel == null ? null : Panels.FamilyPanelValidation.Clone(evidencePanel);
            var restoredAssignments = panelAssignments == null ? null : Evidence.EvidenceApply.Clone(panelAssignments);
            var restoredSubclassLabels = subclassLabels == null ? null : Evidence.EvidenceApply.Clone(subclassLabels);
            // Position and object identity preserve repeated identifiers, including decoys, exactly.
            foreach (var row in evidenceRows)
            {
                row.Protein.AssignmentState = row.Assignment;
                row.Protein.USearchID = row.SearchId;
                row.Protein.UserAnnotation = row.Annotation;
            }
            file.LockedUserIds.Clear();
            foreach (int id in lockedUserIds) file.LockedUserIds.Add(id);
            file.WordSimilarity = wordSimilarity;
            file.IsKeywordSuperCluster = isKeywordSuperCluster;
            file.EvidenceRecipe = restored;
            file.EvidenceSource = restoredSource;
            file.Motifs = restoredMotifs;
            file.FamilyPanel = restoredPanel;
            file.PanelProteinAssignments = restoredAssignments;
            file.PanelProteinAssignmentsFingerprint = panelAssignmentsFingerprint;
            file.PanelProteinAssignmentsPanelFingerprint = panelAssignmentsPanelFingerprint;
            file.ProteinSubclassLabels = restoredSubclassLabels;
            file.ProteinSubclassLabelsPanelFingerprint = subclassLabelsPanelFingerprint;
            file.ProteinSubclassLabelsFingerprint = subclassLabelsFingerprint;
            return true;
        }
    }
}
