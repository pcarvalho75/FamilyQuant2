using System.Collections.Generic;

namespace FamilyQuant
{
    /// <summary>
    /// One reversible step of the curation state, captured before a mutating operation so Undo can put
    /// it back without reloading the file.
    ///
    /// A grouping is defined by three things, and all three are captured: every protein's family ids
    /// (<see cref="FastaItemExtended.UserID"/> AND <see cref="FastaItemExtended.USearchID"/>, keyed by
    /// its <see cref="FastaItemExtended.SequenceIdentifier"/>), the lock set
    /// (<see cref="FamilyClusterFile.LockedUserIds"/>), and the two grouping-provenance flags
    /// (<see cref="FamilyClusterFile.WordSimilarity"/> and
    /// <see cref="FamilyClusterFile.IsKeywordSuperCluster"/>). A restore is a LITERAL step back: it
    /// writes the recorded ids straight onto every item whether or not the family is locked, because the
    /// point is to reproduce the prior state exactly, not to re-apply the lock rules.
    ///
    /// This type is pure - it touches no control and no disk - so the whole undo mechanism can be
    /// exercised headless.
    /// </summary>
    public sealed partial class GroupingSnapshot
    {
        // Keyed by SequenceIdentifier, which is the protein's stable identity across a reclustering (the
        // family ids move, the identifier does not). A repeated identifier keeps its last value, matching
        // how the file itself would read after a save/load.
        private readonly Dictionary<string, (int UserId, int USearchId)> ids;
        private readonly HashSet<string> unassignedUserIds = new();
        private readonly HashSet<int> lockedUserIds;
        private readonly double? wordSimilarity;
        private readonly bool isKeywordSuperCluster;

        // Annotations are NOT part of the grouping and are normally left alone, so a hand-typed note made
        // after this step survives undoing it. Only an operation that rewrites annotations in bulk
        // records them, and only for the rows it touches.
        private readonly Dictionary<string, string>? annotations;

        private GroupingSnapshot(Dictionary<string, (int, int)> ids, HashSet<int> lockedUserIds,
                                 double? wordSimilarity, bool isKeywordSuperCluster,
                                 Dictionary<string, string>? annotations)
        {
            this.ids = ids;
            this.lockedUserIds = lockedUserIds;
            this.wordSimilarity = wordSimilarity;
            this.isKeywordSuperCluster = isKeywordSuperCluster;
            this.annotations = annotations;
        }

        /// <summary>Deep-copies the grouping state of <paramref name="fcf"/> as it stands right now, plus
        /// the <see cref="FastaItemExtended.UserAnnotation"/> of <paramref name="annotationsOf"/> when the
        /// coming operation is about to rewrite those.</summary>
        public static GroupingSnapshot Capture(FamilyClusterFile fcf,
                                               IEnumerable<FastaItemExtended>? annotationsOf = null)
        {
            var ids = new Dictionary<string, (int, int)>(fcf.MyExtendedFastaItems.Count);

            foreach (var item in fcf.MyExtendedFastaItems)
            {
                // Last write wins on a duplicate identifier; proteins carry distinct loci in practice.
                ids[item.SequenceIdentifier] = (item.UserID, item.USearchID);
            }

            Dictionary<string, string>? notes = null;
            if (annotationsOf != null)
            {
                notes = new Dictionary<string, string>();
                foreach (var item in annotationsOf)
                    notes[item.SequenceIdentifier] = item.UserAnnotation ?? string.Empty;
            }

            var snapshot = new GroupingSnapshot(
                ids,
                new HashSet<int>(fcf.LockedUserIds),
                fcf.WordSimilarity,
                fcf.IsKeywordSuperCluster,
                notes);
            foreach (var item in fcf.MyExtendedFastaItems)
                if (item.HasUserId) snapshot.unassignedUserIds.Remove(item.SequenceIdentifier);
                else snapshot.unassignedUserIds.Add(item.SequenceIdentifier);
            return snapshot;
        }

        /// <summary>
        /// Writes the recorded state back onto <paramref name="fcf"/>. The lock set is cleared and
        /// refilled in place so any reference the caller still holds keeps pointing at the live set.
        /// </summary>
        public void Restore(FamilyClusterFile fcf)
        {
            if (RestoreFamilyPanel(fcf)) return;
            if (RestoreReportDetails(fcf)) return;
            if (RestoreEvidence(fcf)) return;
            foreach (var item in fcf.MyExtendedFastaItems)
            {
                if (ids.TryGetValue(item.SequenceIdentifier, out var snap))
                {
                    // A literal restore: both ids go back regardless of the lock state, because a lock
                    // is itself part of the state being restored.
                    item.AssignmentState = new(snap.UserId, !unassignedUserIds.Contains(item.SequenceIdentifier));
                    item.USearchID = snap.USearchId;
                }

                if (annotations != null && annotations.TryGetValue(item.SequenceIdentifier, out string? note))
                    item.UserAnnotation = note;
            }

            fcf.LockedUserIds.Clear();
            foreach (int id in lockedUserIds) fcf.LockedUserIds.Add(id);

            fcf.WordSimilarity = wordSimilarity;
            fcf.IsKeywordSuperCluster = isKeywordSuperCluster;
        }
    }
}
