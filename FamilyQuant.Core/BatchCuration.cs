using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>What one <see cref="BatchCuration.Apply"/> changed.</summary>
    public sealed class BatchCurationResult
    {
        /// <summary>Rows whose User ID changed.</summary>
        public int Moved { get; set; }

        /// <summary>Rows whose annotation changed.</summary>
        public int Annotated { get; set; }

        /// <summary>The families rows were moved OUT of, whether or not anything is left in them.</summary>
        public List<int> SourceFamilies { get; set; } = new List<int>();

        /// <summary>Family numbers this call added to the lock set.</summary>
        public List<int> NewlyLocked { get; set; } = new List<int>();
    }

    /// <summary>
    /// The batch form of typing into the User ID and User Annotation columns: one User ID and/or one
    /// annotation applied to many rows at once, so joining proteins, splitting some out of a family, or
    /// gathering a whole class (the cellular proteins of a venom database, say) into one labelled family
    /// is one action instead of one edit per row.
    ///
    /// It follows the single-cell edit's rules: a hand-set number is curation, so the family the rows
    /// joined is locked against automatic reclustering, and so is every family they left that still has
    /// members (a split must not be re-merged). A family the move EMPTIED is not locked: there is nothing
    /// left in it to protect, and a batch can empty hundreds, each of which would sit in the lock manager
    /// as an unused number. <see cref="FastaItemExtended.USearchID"/> is left alone, exactly as a
    /// single-cell edit leaves it, so the invariant "USearchID differs from UserID only for a locked
    /// family" holds.
    ///
    /// Pure: no control, no disk. The caller records the undo step and recalculates.
    /// </summary>
    public static class BatchCuration
    {
        /// <param name="userId">The family number to give every row, or null to leave numbers alone.</param>
        /// <param name="annotation">The annotation to give every row, or null/empty to leave annotations alone.</param>
        public static BatchCurationResult Apply(FamilyClusterFile fcf, IEnumerable<FastaItemExtended> rows,
                                                int? userId, string? annotation)
        {
            if (fcf == null) throw new ArgumentNullException(nameof(fcf));
            if (userId < 0) throw new ArgumentOutOfRangeException(nameof(userId), "Family numbers cannot be negative.");

            List<FastaItemExtended> targets = (rows ?? Enumerable.Empty<FastaItemExtended>()).Distinct().ToList();
            var result = new BatchCurationResult();

            if (userId.HasValue)
            {
                var sources = new SortedSet<int>();
                foreach (FastaItemExtended row in targets)
                {
                    if (row.UserID == userId.Value) continue;
                    sources.Add(row.UserID);
                    row.UserID = userId.Value;
                    result.Moved++;
                }
                result.SourceFamilies = sources.ToList();

                if (result.Moved > 0)
                {
                    var stillPopulated = new HashSet<int>(fcf.MyExtendedFastaItems.Select(i => i.UserID));
                    foreach (int id in new[] { userId.Value }.Concat(sources.Where(stillPopulated.Contains)))
                    {
                        if (fcf.LockedUserIds.Add(id)) result.NewlyLocked.Add(id);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(annotation))
            {
                string note = annotation.Trim();
                foreach (FastaItemExtended row in targets)
                {
                    if (string.Equals(row.UserAnnotation, note, StringComparison.Ordinal)) continue;
                    row.UserAnnotation = note;
                    result.Annotated++;
                }
            }

            return result;
        }
    }
}
