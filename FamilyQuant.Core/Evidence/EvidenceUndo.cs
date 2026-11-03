using System;
using System.Linq;

namespace FamilyQuant
{
    public sealed partial class GroupingSnapshot
    {
        private (FastaItemExtended Protein, int UserId, int SearchId, string Annotation)[]? evidenceRows;
        private Evidence.EvidenceRecipe? evidenceRecipe;
        private FamilyClusterFile? evidenceFile;
        private Evidence.EvidenceProvenance? evidenceSource;
        private System.Collections.Generic.List<Evidence.MotifPattern>? evidenceMotifs;
        private Panels.FamilyPanel? evidencePanel;

        public static GroupingSnapshot CaptureEvidence(FamilyClusterFile file)
        {
            ArgumentNullException.ThrowIfNull(file);
            GroupingSnapshot snapshot = Capture(file);
            snapshot.evidenceFile = file;
            snapshot.evidenceRows = file.MyExtendedFastaItems.Select(p => (p, p.UserID, p.USearchID, p.UserAnnotation)).ToArray();
            snapshot.evidenceRecipe = file.EvidenceRecipe == null ? null : Evidence.EvidenceApply.Clone(file.EvidenceRecipe);
            snapshot.evidenceSource = file.EvidenceSource == null ? null : Evidence.EvidenceApply.Clone(file.EvidenceSource);
            snapshot.evidenceMotifs = file.Motifs == null ? null : Evidence.EvidenceApply.Clone(file.Motifs);
            snapshot.evidencePanel = file.FamilyPanel == null ? null : Panels.FamilyPanelValidation.Clone(file.FamilyPanel);
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
            // Position and object identity preserve repeated identifiers, including decoys, exactly.
            foreach (var row in evidenceRows)
            {
                row.Protein.UserID = row.UserId;
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
            return true;
        }
    }
}
