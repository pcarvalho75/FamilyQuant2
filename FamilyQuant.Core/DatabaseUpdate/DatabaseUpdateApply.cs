using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.DatabaseUpdate
{
    /// <summary>Everything the preview window shows before Apply: the rows, the counts, the family names that
    /// would change with their Keep the old name defaults, and the families the update empties.</summary>
    public sealed class DatabaseUpdatePreview
    {
        public string ProjectFingerprint { get; init; } = string.Empty;
        public DatabaseUpdateComparison Comparison { get; init; } = null!;
        public DatabaseUpdatePlacement Placement { get; init; } = null!;
        public IReadOnlyList<DatabaseUpdateRow> Rows { get; init; } = Array.Empty<DatabaseUpdateRow>();
        public DatabaseUpdateCounts Counts { get; init; } = new();
        public DatabaseUpdateCounts DecoyCounts { get; init; } = new();
        public IReadOnlyList<DatabaseUpdateNameChange> NameChanges { get; init; } = Array.Empty<DatabaseUpdateNameChange>();
        /// <summary>Family numbers left with no protein at all; their locks go with them.</summary>
        public IReadOnlyList<int> EmptiedFamilies { get; init; } = Array.Empty<int>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public sealed class DatabaseUpdateApplyResult
    {
        public DatabaseUpdateRecord Record { get; init; } = new();
    }

    /// <summary>The planned project after an update, shared by the preview and Apply so both read one plan.</summary>
    internal sealed record PlannedRow(FastaItemExtended? Existing, DatabaseUpdateMatch? Match, int? NewIndex, string Identifier,
        string Description, string Sequence, int UserId, bool HasUserId, int USearchId, string Annotation, bool ClearEvidence);

    public static class DatabaseUpdatePlanner
    {
        public static DatabaseUpdatePreview Preview(FamilyClusterFile project, DatabaseUpdateComparison comparison, DatabaseUpdatePlacement placement)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(comparison);
            ArgumentNullException.ThrowIfNull(placement);
            string fingerprint = DatabaseUpdateComparer.ProjectFingerprint(project);
            if (fingerprint != comparison.ProjectFingerprint || fingerprint != placement.ProjectFingerprint)
                throw new InvalidOperationException("The project changed since it was compared with the new database. Compare again.");
            if (placement.Added.Count != comparison.Added.Count || placement.Added.Select(p => p.NewIndex).Except(comparison.Added).Any())
                throw new ArgumentException("The placement does not belong to this comparison.", nameof(placement));
            List<PlannedRow> planned = Plan(project, comparison, placement);
            var before = Targets(project.MyExtendedFastaItems);
            var after = planned.Where(p => !SequenceLabels.IsDecoy(p.Identifier)).ToList();
            var oldNames = FamilyNaming.Names(before);
            var newNames = FamilyNaming.Names(after.Select(Item));
            var changes = new List<DatabaseUpdateNameChange>();
            foreach (var (family, newName) in newNames.OrderBy(p => p.Key))
            {
                if (!oldNames.TryGetValue(family, out string? oldName) || string.Equals(oldName, newName, StringComparison.Ordinal)) continue;
                bool fromAnnotation = before.Any(p => p.UserID == family && (p.UserAnnotation ?? string.Empty).Trim().Length > 0);
                var differing = after.Where(p => p.UserId == family && p.Annotation.Trim().Length > 0
                        && !string.Equals(p.Annotation.Trim(), oldName.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Identifier).ToList();
                bool keep = fromAnnotation && differing.Count == 0;
                changes.Add(new DatabaseUpdateNameChange
                {
                    FamilyUserId = family, OldName = oldName, NewName = newName, OldNameFromAnnotation = fromAnnotation,
                    DifferingAnnotationMembers = differing, KeepByDefault = keep, Kept = keep,
                });
            }
            var remaining = planned.Where(p => p.HasUserId).Select(p => p.UserId).ToHashSet();
            var emptied = project.MyExtendedFastaItems.Where(p => p.HasUserId).Select(p => p.UserID).Distinct()
                .Where(f => !remaining.Contains(f)).OrderBy(f => f).ToList();
            var counts = Copy(placement.Counts);
            counts.FamiliesEmptied = before.Where(p => p.HasUserId).Select(p => p.UserID).Distinct()
                .Count(f => !after.Any(a => a.HasUserId && a.UserId == f));
            counts.NamesKept = changes.Count(c => c.Kept);
            return new DatabaseUpdatePreview
            {
                ProjectFingerprint = fingerprint, Comparison = comparison, Placement = placement, Rows = placement.Rows, Counts = counts,
                DecoyCounts = Copy(placement.DecoyCounts), NameChanges = changes, EmptiedFamilies = emptied, Warnings = comparison.Warnings,
            };
        }

        /// <summary>Kept rows in project order with the new identifier, header and sequence; removed rows left out;
        /// added records appended in file order with their placed family. Nothing is written.</summary>
        internal static List<PlannedRow> Plan(FamilyClusterFile project, DatabaseUpdateComparison comparison, DatabaseUpdatePlacement placement)
        {
            List<FastaItemExtended> rows = project.MyExtendedFastaItems;
            IReadOnlyList<DatabaseUpdateEntry> records = comparison.Source.Records;
            var matchByOld = comparison.Matches.ToDictionary(m => m.OldIndex);
            var removed = comparison.Removed.ToHashSet();
            var planned = new List<PlannedRow>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (removed.Contains(i)) continue;
                FastaItemExtended row = rows[i];
                if (!matchByOld.TryGetValue(i, out DatabaseUpdateMatch? match))
                    throw new InvalidOperationException("A project row is neither kept nor removed. Compare again.");
                DatabaseUpdateEntry record = records[match.NewIndex];
                // Evidence belongs to one accession and one sequence; a new accession or a new sequence starts without it.
                bool clear = match.Kind is DatabaseUpdateKind.SequenceChanged or DatabaseUpdateKind.NewAccessionSameSequence or DatabaseUpdateKind.ReplacedByLonger
                    || !string.Equals(row.Sequence ?? string.Empty, record.Sequence, StringComparison.Ordinal);
                planned.Add(new PlannedRow(row, match, match.NewIndex, record.Identifier, record.Description, record.Sequence,
                    row.UserID, row.HasUserId, row.USearchID, row.UserAnnotation ?? string.Empty, clear));
            }
            var placed = placement.Added.ToDictionary(p => p.NewIndex);
            foreach (int j in comparison.Added)
            {
                DatabaseUpdateAddedPlacement p = placed[j];
                DatabaseUpdateEntry record = records[j];
                planned.Add(new PlannedRow(null, null, j, record.Identifier, record.Description, record.Sequence, p.UserId, true, p.USearchId,
                    string.Empty, false));
            }
            return planned;
        }

        internal static List<FastaItemExtended> Targets(IEnumerable<FastaItemExtended> rows) =>
            rows.Where(r => !SequenceLabels.IsDecoy(r.SequenceIdentifier ?? string.Empty)).ToList();

        internal static FastaItemExtended Item(PlannedRow row)
        {
            var item = new FastaItemExtended
            {
                SequenceIdentifier = row.Identifier, Description = row.Description, Sequence = row.Sequence,
                USearchID = row.USearchId, UserAnnotation = row.Annotation,
            };
            if (row.HasUserId) item.UserID = row.UserId;
            return item;
        }

        internal static DatabaseUpdateCounts Copy(DatabaseUpdateCounts counts) => new()
        {
            Unchanged = counts.Unchanged, HeaderChanged = counts.HeaderChanged, SequenceChanged = counts.SequenceChanged,
            NewAccessionSameSequence = counts.NewAccessionSameSequence, ReplacedByLonger = counts.ReplacedByLonger, Removed = counts.Removed,
            Added = counts.Added, AmbiguousCases = counts.AmbiguousCases, AmbiguousOldRows = counts.AmbiguousOldRows,
            AmbiguousNewRecords = counts.AmbiguousNewRecords, AddedJoinedFamilies = counts.AddedJoinedFamilies,
            AddedJoinedLockedFamilies = counts.AddedJoinedLockedFamilies, AddedInNewFamilies = counts.AddedInNewFamilies,
            NewFamilies = counts.NewFamilies, AddedCloseToSeveralFamilies = counts.AddedCloseToSeveralFamilies,
            FamiliesEmptied = counts.FamiliesEmptied, NamesKept = counts.NamesKept,
        };
    }

    /// <summary>
    /// Applies a previewed database update to the open project as one change (docs/design/DATABASE-UPDATE.md
    /// sections 5 to 8). Kept rows keep their order, families, numbers and annotations (and their objects when
    /// nothing in their record changed); removed rows leave; added records are appended in file order with their
    /// placed family; evidence and domains are cleared where the accession or the sequence changed; a kept family
    /// name is written into the User
    /// Annotation of every target member of that family; locks of emptied families go; the source hash and
    /// count become the new file's; the update is recorded. Everything is prepared first and published by the
    /// operation's single commit, so a refusal or a cancel leaves the project as it was. The caller empties the
    /// undo stack afterwards: earlier steps refer to rows that no longer exist.
    /// </summary>
    public static class DatabaseUpdateApply
    {
        public static DatabaseUpdateApplyResult Apply(FamilyClusterFile project, DatabaseUpdatePreview preview,
            IReadOnlyCollection<int> keepNameFamilyUserIds, string programVersion, OperationContext? operation = null, DateTimeOffset? now = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(preview);
            ArgumentNullException.ThrowIfNull(keepNameFamilyUserIds);
            if (string.IsNullOrWhiteSpace(programVersion)) throw new ArgumentException("Supply the running program version.", nameof(programVersion));
            operation?.Report("database-update", "Checking the previewed update");
            if (DatabaseUpdateComparer.ProjectFingerprint(project) != preview.ProjectFingerprint)
                throw new InvalidOperationException("The project changed since the update was previewed. Compare again.");
            var keep = keepNameFamilyUserIds.ToHashSet();
            var offered = preview.NameChanges.Select(c => c.FamilyUserId).ToHashSet();
            if (!keep.IsSubsetOf(offered)) throw new ArgumentException("Keep the old name refers to a family whose name does not change.", nameof(keepNameFamilyUserIds));

            List<PlannedRow> planned = DatabaseUpdatePlanner.Plan(project, preview.Comparison, preview.Placement);
            var annotations = planned.Select(p => p.Annotation).ToArray();
            var written = new List<string>();
            var names = new List<DatabaseUpdateNameChange>();
            foreach (DatabaseUpdateNameChange change in preview.NameChanges)
            {
                bool kept = keep.Contains(change.FamilyUserId);
                names.Add(new DatabaseUpdateNameChange
                {
                    FamilyUserId = change.FamilyUserId, OldName = change.OldName, NewName = change.NewName,
                    OldNameFromAnnotation = change.OldNameFromAnnotation, DifferingAnnotationMembers = change.DifferingAnnotationMembers.ToList(),
                    KeepByDefault = change.KeepByDefault, Kept = kept,
                });
                if (!kept) continue;
                for (int n = 0; n < planned.Count; n++)
                {
                    PlannedRow row = planned[n];
                    if (!row.HasUserId || row.UserId != change.FamilyUserId || SequenceLabels.IsDecoy(row.Identifier)) continue;
                    if (!string.Equals(annotations[n], change.OldName, StringComparison.Ordinal)) written.Add(row.Identifier);
                    annotations[n] = change.OldName;
                }
            }
            var remaining = planned.Where(p => p.HasUserId).Select(p => p.UserId).ToHashSet();
            var locks = project.LockedUserIds.Where(remaining.Contains).OrderBy(id => id).ToList();
            var counts = DatabaseUpdatePlanner.Copy(preview.Counts);
            counts.NamesKept = names.Count(c => c.Kept);
            var record = new DatabaseUpdateRecord
            {
                UpdatedAtUtc = now ?? DateTimeOffset.UtcNow, ProgramVersion = programVersion,
                Method = DatabaseUpdateComparer.Method + ". " + DatabaseUpdatePlacer.Method,
                Previous = Copy(preview.Comparison.Previous), New = Copy(preview.Comparison.Source.Summary),
                ClusteringIdentity = preview.Placement.ClusteringIdentity, ExhaustivePlacement = preview.Placement.Exhaustive,
                Counts = counts, DecoyCounts = DatabaseUpdatePlanner.Copy(preview.DecoyCounts),
                Rows = preview.Rows.ToList(), NameChanges = names, AnnotationsWritten = written.Distinct(StringComparer.Ordinal).ToList(),
            };
            // Stage the rows. A kept row whose identifier, header and sequence are unchanged keeps its object (only a
            // kept name can touch its annotation, inside the commit). Any other row is a new object built the way
            // Build families builds one (USearchTools.ParseClusterFile), carrying the family, number, annotation and
            // PatternLab reading, and the evidence only when accession and sequence stayed: the PatternTools item
            // caches values derived from its sequence (its monoisotopic mass), so a sequence is never changed in place.
            var staged = new List<FastaItemExtended>(planned.Count);
            var inPlace = new bool[planned.Count];
            for (int n = 0; n < planned.Count; n++)
            {
                PlannedRow row = planned[n];
                FastaItemExtended? existing = row.Existing;
                if (existing != null && string.Equals(existing.SequenceIdentifier, row.Identifier, StringComparison.Ordinal)
                    && string.Equals(existing.Description ?? string.Empty, row.Description, StringComparison.Ordinal)
                    && string.Equals(existing.Sequence ?? string.Empty, row.Sequence, StringComparison.Ordinal))
                {
                    staged.Add(existing);
                    inPlace[n] = true;
                    continue;
                }
                FastaItemExtended item = DatabaseUpdatePlanner.Item(row);
                item.UserAnnotation = annotations[n];
                if (existing != null)
                {
                    item.XICUser = existing.XICUser;
                    if (!row.ClearEvidence) { item.Evidence = existing.Evidence; item.Domains = existing.Domains; }
                }
                staged.Add(item);
            }
            string expected = preview.ProjectFingerprint;
            operation?.Report("database-update", "Applying the database update", 1, 1);
            void Publish()
            {
                if (DatabaseUpdateComparer.ProjectFingerprint(project) != expected)
                    throw new InvalidOperationException("The project changed while the update was prepared. Nothing was applied.");
                for (int n = 0; n < planned.Count; n++)
                    if (inPlace[n]) staged[n].UserAnnotation = annotations[n];
                project.MyExtendedFastaItems = staged;
                project.LockedUserIds.Clear();
                foreach (int id in locks) project.LockedUserIds.Add(id);
                project.SourceSha256 = preview.Comparison.Source.Summary.Sha256;
                project.SourceSequenceCount = preview.Comparison.Source.Summary.SequenceCount;
                project.DatabaseUpdates ??= new List<DatabaseUpdateRecord>();
                project.DatabaseUpdates.Add(record);
            }
            if (operation == null) Publish();
            else operation.Commit(Publish);
            return new DatabaseUpdateApplyResult { Record = record };
        }

        private static DatabaseSourceSummary Copy(DatabaseSourceSummary summary) =>
            new() { FileName = summary.FileName, Sha256 = summary.Sha256, SequenceCount = summary.SequenceCount };
    }
}
