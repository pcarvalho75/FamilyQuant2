using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.DatabaseUpdate
{
    /// <summary>The comparison of a project with a new database file: which row pairs with which record,
    /// which rows leave, which records arrive and which cases need the user's pick. Read only.</summary>
    public sealed class DatabaseUpdateComparison
    {
        public string ProjectFingerprint { get; init; } = string.Empty;
        /// <summary>The new file this comparison read (<see cref="DatabaseUpdateSource.Fingerprint"/>).</summary>
        public string SourceFingerprint { get; init; } = string.Empty;
        /// <summary>The project, the file and every decision of this comparison (<see cref="DatabaseUpdateComparer.Fingerprint"/>).</summary>
        public string ComparisonFingerprint { get; init; } = string.Empty;
        public DatabaseSourceSummary Previous { get; init; } = new();
        public DatabaseUpdateSource Source { get; init; } = null!;
        public IReadOnlyList<DatabaseUpdateMatch> Matches { get; init; } = Array.Empty<DatabaseUpdateMatch>();
        /// <summary>Project rows that leave at Apply, including unpicked ambiguous rows, in project order.</summary>
        public IReadOnlyList<int> Removed { get; init; } = Array.Empty<int>();
        /// <summary>New records that arrive at Apply and need a family, including unpicked ambiguous records, in file order.</summary>
        public IReadOnlyList<int> Added { get; init; } = Array.Empty<int>();
        /// <summary>Every ambiguous case found, picked or not.</summary>
        public IReadOnlyList<DatabaseUpdateAmbiguity> Ambiguities { get; init; } = Array.Empty<DatabaseUpdateAmbiguity>();
        public IReadOnlyList<DatabaseUpdatePick> Picks { get; init; } = Array.Empty<DatabaseUpdatePick>();
        /// <summary>Every protein that is not unchanged, project rows first in project order, then new records in file order.</summary>
        public IReadOnlyList<DatabaseUpdateRow> Rows { get; init; } = Array.Empty<DatabaseUpdateRow>();
        public DatabaseUpdateCounts Counts { get; init; } = new();
        public DatabaseUpdateCounts DecoyCounts { get; init; } = new();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// One-to-one matching of a project's rows with the records of a new database file, in the ordered steps
    /// of docs/design/DATABASE-UPDATE.md section 3. Every row and every record takes part in at most one match,
    /// and a match is made only when it is the only one possible on both sides; anything else is an ambiguous
    /// case for the user. A target never matches a decoy, and a contaminant never matches a non-contaminant.
    /// </summary>
    public static class DatabaseUpdateComparer
    {
        public const string Method = "Database update matching v1: one to one in ordered steps (accession key; a repeated key paired by a unique "
            + "identical sequence; the same sequence under a new accession; an old sequence contained in exactly one longer new record that contains "
            + "no other); every other case is listed as ambiguous and carries nothing over unless the user picks the pair";
        public const double LowOverlapFraction = 0.5;
        private const int AnchorLength = 10;

        private readonly record struct Side(bool Decoy, bool Contaminant);
        private readonly record struct Key(Side Side, string Accession);

        private static Key KeyOf(string identifier)
        {
            string id = identifier ?? string.Empty;
            bool decoy = SequenceLabels.IsDecoy(id);
            string target = decoy ? id[SequenceLabels.DecoyPrefix.Length..] : id;
            return new Key(new Side(decoy, SequenceLabels.IsContaminant(target)), FamilyNaming.Accession(target));
        }

        public static DatabaseUpdateComparison Compare(FamilyClusterFile project, DatabaseUpdateSource source,
            IReadOnlyList<DatabaseUpdatePick>? picks = null, OperationContext? operation = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(source);
            operation?.Report("database-update", "Comparing the project with the new database");
            List<FastaItemExtended> rows = project.MyExtendedFastaItems;
            if (rows.Count == 0) throw new InvalidDataException("The project holds no proteins to update.");
            IReadOnlyList<DatabaseUpdateEntry> records = source.Records;
            Key[] oldKeys = rows.Select(r => KeyOf(r.SequenceIdentifier)).ToArray();
            Key[] newKeys = records.Select(r => KeyOf(r.Identifier)).ToArray();
            RequireSameKinds(oldKeys, newKeys);

            var matches = new List<DatabaseUpdateMatch>();
            var oldDone = new bool[rows.Count];
            var newDone = new bool[records.Count];
            var cases = new List<(DatabaseUpdateStep Step, List<int> Old, List<int> New, string Reason)>();
            var removedDirect = new List<int>();
            var addedDirect = new List<int>();

            // Steps 1 and 1b: by key.
            var oldByKey = Group(oldKeys);
            var newByKey = Group(newKeys);
            foreach (var (key, olds) in oldByKey)
            {
                operation?.ThrowIfCancellationRequested();
                if (!newByKey.TryGetValue(key, out List<int>? news)) continue;
                if (olds.Count == 1 && news.Count == 1)
                {
                    matches.Add(new DatabaseUpdateMatch(olds[0], news[0], KindOf(rows[olds[0]], records[news[0]]), DatabaseUpdateStep.Key, false));
                    oldDone[olds[0]] = newDone[news[0]] = true;
                    continue;
                }
                var leftOld = new List<int>(olds);
                var leftNew = new List<int>(news);
                foreach (var group in olds.GroupBy(i => rows[i].Sequence ?? string.Empty, StringComparer.Ordinal))
                {
                    var sameNew = news.Where(j => string.Equals(records[j].Sequence, group.Key, StringComparison.Ordinal)).ToList();
                    if (group.Count() != 1 || sameNew.Count != 1) continue;
                    int i = group.Single(), j = sameNew[0];
                    matches.Add(new DatabaseUpdateMatch(i, j, KindOf(rows[i], records[j]), DatabaseUpdateStep.RepeatedKey, false));
                    oldDone[i] = newDone[j] = true;
                    leftOld.Remove(i); leftNew.Remove(j);
                }
                if (leftOld.Count > 0 && leftNew.Count > 0)
                    cases.Add((DatabaseUpdateStep.RepeatedKey, leftOld, leftNew, "The accession " + key.Accession + " is held by "
                        + Count(olds.Count) + " project rows and " + Count(news.Count) + " new records, with no unique identical sequence for these."));
                else
                {
                    // The key's other members found their partners; what is left has no candidate of its key.
                    removedDirect.AddRange(leftOld);
                    addedDirect.AddRange(leftNew);
                }
                foreach (int i in leftOld) oldDone[i] = true;
                foreach (int j in leftNew) newDone[j] = true;
            }

            // Step 2: the same sequence under a key the other side does not hold.
            List<int> oldPool = Enumerable.Range(0, rows.Count).Where(i => !oldDone[i] && !newByKey.ContainsKey(oldKeys[i])).ToList();
            List<int> newPool = Enumerable.Range(0, records.Count).Where(j => !newDone[j] && !oldByKey.ContainsKey(newKeys[j])).ToList();
            var newBySequence = newPool.GroupBy(j => (newKeys[j].Side, records[j].Sequence))
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var group in oldPool.GroupBy(i => (oldKeys[i].Side, rows[i].Sequence ?? string.Empty)).OrderBy(g => g.Min()))
            {
                operation?.ThrowIfCancellationRequested();
                if (!newBySequence.TryGetValue(group.Key, out List<int>? news)) continue;
                var olds = group.ToList();
                if (olds.Count == 1 && news.Count == 1)
                    matches.Add(new DatabaseUpdateMatch(olds[0], news[0], DatabaseUpdateKind.NewAccessionSameSequence, DatabaseUpdateStep.SameSequence, false));
                else
                    cases.Add((DatabaseUpdateStep.SameSequence, olds, news, "The same sequence is held by " + Count(olds.Count)
                        + " project rows and " + Count(news.Count) + " new records under accessions the other side does not hold."));
                foreach (int i in olds) oldDone[i] = true;
                foreach (int j in news) newDone[j] = true;
            }

            // Step 3: an old sequence contained in a longer new record.
            oldPool = oldPool.Where(i => !oldDone[i]).ToList();
            newPool = newPool.Where(j => !newDone[j]).ToList();
            var edges = Containment(rows, records, oldKeys, newKeys, oldPool, newPool, operation);
            var parent = new Dictionary<(bool Old, int Index), (bool Old, int Index)>();
            (bool, int) Find((bool, int) node)
            {
                if (!parent.TryGetValue(node, out var up)) { parent[node] = node; return node; }
                while (!up.Equals(node)) { var next = parent[up]; parent[node] = next; node = up; up = next; }
                return node;
            }
            foreach (var (i, j) in edges)
            {
                var a = Find((true, i)); var b = Find((false, j));
                if (!a.Equals(b)) parent[a] = b;
            }
            foreach (var component in edges.SelectMany(e => new[] { (true, e.Old), (false, e.New) }).Distinct()
                .GroupBy(Find).Select(g => g.ToList()).OrderBy(g => g.Where(n => n.Item1).Min(n => n.Item2)))
            {
                var olds = component.Where(n => n.Item1).Select(n => n.Item2).OrderBy(i => i).ToList();
                var news = component.Where(n => !n.Item1).Select(n => n.Item2).OrderBy(j => j).ToList();
                if (olds.Count == 1 && news.Count == 1)
                    matches.Add(new DatabaseUpdateMatch(olds[0], news[0], DatabaseUpdateKind.ReplacedByLonger, DatabaseUpdateStep.Containment, false));
                else
                    cases.Add((DatabaseUpdateStep.Containment, olds, news, Count(olds.Count) + " project rows and " + Count(news.Count)
                        + " longer new records contain one another's sequences, so no single replacement is certain."));
                foreach (int i in olds) oldDone[i] = true;
                foreach (int j in news) newDone[j] = true;
            }
            removedDirect.AddRange(Enumerable.Range(0, rows.Count).Where(i => !oldDone[i]));
            addedDirect.AddRange(Enumerable.Range(0, records.Count).Where(j => !newDone[j]));

            // Stable case numbers: by step, then by the first project row.
            var ambiguities = cases.OrderBy(c => c.Step).ThenBy(c => c.Old.Min()).ThenBy(c => c.New.Min())
                .Select((c, n) => new DatabaseUpdateAmbiguity(n + 1, c.Step, c.Old.OrderBy(i => i).ToArray(), c.New.OrderBy(j => j).ToArray(), c.Reason))
                .ToList();
            var chosen = ApplyPicks(rows, records, ambiguities, picks ?? Array.Empty<DatabaseUpdatePick>(), matches);
            var removed = new SortedSet<int>(removedDirect);
            var added = new SortedSet<int>(addedDirect);
            foreach (DatabaseUpdateAmbiguity ambiguity in ambiguities)
            {
                chosen.TryGetValue(ambiguity.Case, out DatabaseUpdatePick? pick);
                foreach (int i in ambiguity.OldIndices) if (pick == null || pick.OldIndex != i) removed.Add(i);
                foreach (int j in ambiguity.NewIndices) if (pick == null || pick.NewIndex != j) added.Add(j);
            }
            matches = matches.OrderBy(m => m.OldIndex).ToList();
            var counts = BuildRows(project, records, matches, removed, added, ambiguities, chosen, decoys: false, out var targetRows);
            var decoyCounts = BuildRows(project, records, matches, removed, added, ambiguities, chosen, decoys: true, out var decoyRows);
            var allRows = targetRows.Concat(decoyRows).OrderBy(r => r.OldIndex == null ? 1 : 0)
                .ThenBy(r => r.OldIndex ?? int.MaxValue).ThenBy(r => r.NewIndex ?? int.MaxValue).ToList();

            var warnings = new List<string>();
            int oldTargets = oldKeys.Count(k => !k.Side.Decoy);
            int keptTargets = matches.Count(m => !oldKeys[m.OldIndex].Side.Decoy);
            if (oldTargets > 0 && keptTargets < LowOverlapFraction * oldTargets)
                warnings.Add("Only " + Count(keptTargets) + " of the project's " + Count(oldTargets) + " target proteins are in the new file. "
                    + "It looks like a different database; Build families may be the better choice.");
            if (source.EmptyRecords > 0)
                warnings.Add(Count(source.EmptyRecords) + " records of the new file hold no residues and were left out.");
            var comparison = new DatabaseUpdateComparison
            {
                ProjectFingerprint = ProjectFingerprint(project), SourceFingerprint = source.Fingerprint,
                Previous = new DatabaseSourceSummary { Sha256 = project.SourceSha256, SequenceCount = project.SourceSequenceCount },
                Source = source, Matches = matches, Removed = removed.ToArray(), Added = added.ToArray(), Ambiguities = ambiguities,
                Picks = chosen.Values.OrderBy(p => p.Case).ToArray(), Rows = allRows, Counts = counts, DecoyCounts = decoyCounts, Warnings = warnings,
            };
            return new DatabaseUpdateComparison
            {
                ProjectFingerprint = comparison.ProjectFingerprint, SourceFingerprint = comparison.SourceFingerprint,
                ComparisonFingerprint = Fingerprint(comparison), Previous = comparison.Previous, Source = comparison.Source,
                Matches = comparison.Matches, Removed = comparison.Removed, Added = comparison.Added, Ambiguities = comparison.Ambiguities,
                Picks = comparison.Picks, Rows = comparison.Rows, Counts = comparison.Counts, DecoyCounts = comparison.DecoyCounts,
                Warnings = comparison.Warnings,
            };
        }

        /// <summary>The hash of what a comparison decided: its project, its file (recomputed from the source it holds)
        /// and every match, removal, addition and pick. A placement and a preview are bound to it.</summary>
        public static string Fingerprint(DatabaseUpdateComparison comparison)
        {
            ArgumentNullException.ThrowIfNull(comparison);
            using var hash = new LengthPrefixedSha256();
            hash.Append("Database update comparison v1");
            hash.Append(comparison.ProjectFingerprint);
            hash.Append(comparison.Source?.Fingerprint ?? string.Empty);
            hash.Append(comparison.SourceFingerprint);
            hash.AppendJson(comparison.Matches);
            hash.AppendJson(comparison.Removed);
            hash.AppendJson(comparison.Added);
            hash.AppendJson(comparison.Picks);
            return hash.Finish();
        }

        /// <summary>Refuses a comparison whose recorded fingerprints no longer describe its own content.</summary>
        internal static void RequireConsistent(DatabaseUpdateComparison comparison)
        {
            if (comparison.Source == null || comparison.SourceFingerprint != comparison.Source.Fingerprint
                || comparison.ComparisonFingerprint != Fingerprint(comparison))
                throw new ArgumentException("The comparison does not match the new database it names. Compare again.", nameof(comparison));
        }

        /// <summary>The project state a comparison was made on: rows, families, annotations, locks and source.
        /// Apply refuses a comparison whose fingerprint no longer matches.</summary>
        public static string ProjectFingerprint(FamilyClusterFile project)
        {
            ArgumentNullException.ThrowIfNull(project);
            using var hash = new LengthPrefixedSha256();
            hash.Append("Database update project v1");
            hash.Append(project.SourceSha256 ?? string.Empty);
            hash.Append(project.SourceSequenceCount.ToString(CultureInfo.InvariantCulture));
            hash.Append(project.ClusteringIdentity.ToString(CultureInfo.InvariantCulture));
            hash.Append(project.ClusteringMethod ?? string.Empty);
            foreach (FastaItemExtended row in project.MyExtendedFastaItems)
            {
                hash.Append(row.SequenceIdentifier ?? string.Empty); hash.Append(row.Description ?? string.Empty);
                hash.Append(row.Sequence ?? string.Empty); hash.Append(row.HasUserId ? row.UserID.ToString(CultureInfo.InvariantCulture) : "unassigned");
                hash.Append(row.USearchID.ToString(CultureInfo.InvariantCulture)); hash.Append(row.UserAnnotation ?? string.Empty);
            }
            foreach (int id in project.LockedUserIds.OrderBy(i => i)) hash.Append(id.ToString(CultureInfo.InvariantCulture));
            return hash.Finish();
        }

        private static Dictionary<Key, List<int>> Group(Key[] keys)
        {
            var groups = new Dictionary<Key, List<int>>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (!groups.TryGetValue(keys[i], out List<int>? list)) groups[keys[i]] = list = new List<int>();
                list.Add(i);
            }
            return groups;
        }

        private static void RequireSameKinds(Key[] oldKeys, Key[] newKeys)
        {
            bool oldDecoys = oldKeys.Any(k => k.Side.Decoy), newDecoys = newKeys.Any(k => k.Side.Decoy);
            if (oldDecoys != newDecoys)
                throw new InvalidDataException(oldDecoys
                    ? "The project has decoy (" + SequenceLabels.DecoyPrefix + ") rows and the new file has none. Make the new file the same way, as a target-decoy file."
                    : "The new file has decoy (" + SequenceLabels.DecoyPrefix + ") records and the project has none. Make the new file the same way as the project's database.");
            bool oldContaminants = oldKeys.Any(k => k.Side.Contaminant), newContaminants = newKeys.Any(k => k.Side.Contaminant);
            if (oldContaminants != newContaminants)
                throw new InvalidDataException(oldContaminants
                    ? "The project has contaminant rows and the new file has none. Add the same contaminants to the new file."
                    : "The new file has contaminant records and the project has none. Make the new file the same way as the project's database.");
        }

        private static DatabaseUpdateKind KindOf(FastaItemExtended row, DatabaseUpdateEntry record)
        {
            if (!string.Equals(row.Sequence ?? string.Empty, record.Sequence, StringComparison.Ordinal)) return DatabaseUpdateKind.SequenceChanged;
            return string.Equals(row.SequenceIdentifier ?? string.Empty, record.Identifier, StringComparison.Ordinal)
                && string.Equals(row.Description ?? string.Empty, record.Description, StringComparison.Ordinal)
                ? DatabaseUpdateKind.Unchanged : DatabaseUpdateKind.HeaderChanged;
        }

        /// <summary>Every (old, new) pair of the pools on the same side where the new sequence is longer and contains
        /// the old one. Anchored on each old sequence's first residues, so the cost follows the new pool's residues.</summary>
        private static List<(int Old, int New)> Containment(List<FastaItemExtended> rows, IReadOnlyList<DatabaseUpdateEntry> records,
            Key[] oldKeys, Key[] newKeys, List<int> oldPool, List<int> newPool, OperationContext? operation)
        {
            var edges = new HashSet<(int, int)>();
            if (oldPool.Count == 0 || newPool.Count == 0) return new List<(int, int)>();
            var anchored = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var shortOnes = new List<int>();
            foreach (int i in oldPool)
            {
                string sequence = rows[i].Sequence ?? string.Empty;
                if (sequence.Length == 0) continue;
                if (sequence.Length < AnchorLength) { shortOnes.Add(i); continue; }
                string anchor = sequence[..AnchorLength];
                if (!anchored.TryGetValue(anchor, out List<int>? list)) anchored[anchor] = list = new List<int>();
                list.Add(i);
            }
            var lookup = anchored.GetAlternateLookup<ReadOnlySpan<char>>();
            foreach (int j in newPool)
            {
                operation?.ThrowIfCancellationRequested();
                string longer = records[j].Sequence;
                for (int start = 0; start + AnchorLength <= longer.Length; start++)
                {
                    if (!lookup.TryGetValue(longer.AsSpan(start, AnchorLength), out List<int>? candidates)) continue;
                    foreach (int i in candidates)
                    {
                        string shorter = rows[i].Sequence!;
                        if (shorter.Length < longer.Length && oldKeys[i].Side == newKeys[j].Side
                            && longer.AsSpan(start).StartsWith(shorter, StringComparison.Ordinal)) edges.Add((i, j));
                    }
                }
                foreach (int i in shortOnes)
                    if (rows[i].Sequence!.Length < longer.Length && oldKeys[i].Side == newKeys[j].Side
                        && longer.Contains(rows[i].Sequence!, StringComparison.Ordinal)) edges.Add((i, j));
            }
            return edges.OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToList();
        }

        private static Dictionary<int, DatabaseUpdatePick> ApplyPicks(List<FastaItemExtended> rows, IReadOnlyList<DatabaseUpdateEntry> records,
            IReadOnlyList<DatabaseUpdateAmbiguity> ambiguities, IReadOnlyList<DatabaseUpdatePick> picks, List<DatabaseUpdateMatch> matches)
        {
            var chosen = new Dictionary<int, DatabaseUpdatePick>();
            var byCase = ambiguities.ToDictionary(a => a.Case);
            foreach (DatabaseUpdatePick pick in picks)
            {
                if (!byCase.TryGetValue(pick.Case, out DatabaseUpdateAmbiguity? ambiguity) || !ambiguity.OldIndices.Contains(pick.OldIndex)
                    || !ambiguity.NewIndices.Contains(pick.NewIndex))
                    throw new ArgumentException("A pick does not belong to its ambiguous case.", nameof(picks));
                if (!chosen.TryAdd(pick.Case, pick)) throw new ArgumentException("Pick one pair per ambiguous case.", nameof(picks));
                FastaItemExtended row = rows[pick.OldIndex];
                DatabaseUpdateEntry record = records[pick.NewIndex];
                DatabaseUpdateKind kind;
                switch (ambiguity.Step)
                {
                    case DatabaseUpdateStep.Containment:
                        string shorter = row.Sequence ?? string.Empty;
                        if (shorter.Length == 0 || shorter.Length >= record.Sequence.Length || !record.Sequence.Contains(shorter, StringComparison.Ordinal))
                            throw new ArgumentException("A containment pick must name a new record that contains the project row's sequence.", nameof(picks));
                        kind = DatabaseUpdateKind.ReplacedByLonger;
                        break;
                    case DatabaseUpdateStep.SameSequence:
                        kind = DatabaseUpdateKind.NewAccessionSameSequence;
                        break;
                    default:
                        kind = KindOf(row, record);
                        break;
                }
                matches.Add(new DatabaseUpdateMatch(pick.OldIndex, pick.NewIndex, kind, ambiguity.Step, true));
            }
            return chosen;
        }

        private static DatabaseUpdateCounts BuildRows(FamilyClusterFile project, IReadOnlyList<DatabaseUpdateEntry> records,
            List<DatabaseUpdateMatch> matches, SortedSet<int> removed, SortedSet<int> added, IReadOnlyList<DatabaseUpdateAmbiguity> ambiguities,
            Dictionary<int, DatabaseUpdatePick> chosen, bool decoys, out List<DatabaseUpdateRow> output)
        {
            List<FastaItemExtended> rows = project.MyExtendedFastaItems;
            var names = FamilyNaming.Names(rows.Where(r => !SequenceLabels.IsDecoy(r.SequenceIdentifier ?? string.Empty)));
            var counts = new DatabaseUpdateCounts();
            output = new List<DatabaseUpdateRow>();
            var oldCase = new Dictionary<int, DatabaseUpdateAmbiguity>();
            var newCase = new Dictionary<int, DatabaseUpdateAmbiguity>();
            foreach (DatabaseUpdateAmbiguity a in ambiguities)
            {
                foreach (int i in a.OldIndices) oldCase[i] = a;
                foreach (int j in a.NewIndices) newCase[j] = a;
            }
            DatabaseUpdateRow FromOld(int i, DatabaseUpdateKind kind)
            {
                FastaItemExtended row = rows[i];
                Key key = KeyOf(row.SequenceIdentifier);
                return new DatabaseUpdateRow
                {
                    Kind = kind, Decoy = key.Side.Decoy, Contaminant = key.Side.Contaminant, OldIndex = i,
                    OldIdentifier = row.SequenceIdentifier ?? string.Empty, FamilyUserId = row.HasUserId ? row.UserID : null,
                    FamilySearchId = row.USearchID, FamilyName = row.HasUserId && names.TryGetValue(row.UserID, out string? name) ? name : string.Empty,
                };
            }
            foreach (DatabaseUpdateMatch match in matches)
            {
                if (KeyOf(rows[match.OldIndex].SequenceIdentifier).Side.Decoy != decoys) continue;
                switch (match.Kind)
                {
                    case DatabaseUpdateKind.Unchanged: counts.Unchanged++; break;
                    case DatabaseUpdateKind.HeaderChanged: counts.HeaderChanged++; break;
                    case DatabaseUpdateKind.SequenceChanged: counts.SequenceChanged++; break;
                    case DatabaseUpdateKind.NewAccessionSameSequence: counts.NewAccessionSameSequence++; break;
                    case DatabaseUpdateKind.ReplacedByLonger: counts.ReplacedByLonger++; break;
                }
                if (match.Kind == DatabaseUpdateKind.Unchanged && !match.Picked) continue;
                DatabaseUpdateRow row = FromOld(match.OldIndex, match.Kind);
                row.NewIndex = match.NewIndex; row.NewIdentifier = records[match.NewIndex].Identifier;
                row.Step = match.Step; row.Picked = match.Picked;
                if (match.Picked) row.AmbiguousCase = oldCase[match.OldIndex].Case;
                if (match.Kind == DatabaseUpdateKind.SequenceChanged) row.Note = "Sequence changed under the same accession; kept in its family.";
                if (match.Kind == DatabaseUpdateKind.ReplacedByLonger) row.Note = "Its sequence is part of the new record, which takes its family.";
                output.Add(row);
            }
            foreach (int i in removed)
            {
                if (KeyOf(rows[i].SequenceIdentifier).Side.Decoy != decoys) continue;
                bool ambiguous = oldCase.TryGetValue(i, out DatabaseUpdateAmbiguity? a) && !chosen.ContainsKey(a.Case);
                DatabaseUpdateRow row = FromOld(i, ambiguous ? DatabaseUpdateKind.Ambiguous : DatabaseUpdateKind.Removed);
                if (a != null) { row.AmbiguousCase = a.Case; row.Step = a.Step; }
                row.Note = ambiguous ? a!.Reason + " Without a pick this row is removed."
                    : a != null ? "Not chosen in ambiguous case " + a.Case.ToString(CultureInfo.InvariantCulture) + "; removed."
                    : "Not in the new database; removed.";
                if (ambiguous) counts.AmbiguousOldRows++; else counts.Removed++;
                output.Add(row);
            }
            foreach (int j in added)
            {
                Key key = KeyOf(records[j].Identifier);
                if (key.Side.Decoy != decoys) continue;
                bool ambiguous = newCase.TryGetValue(j, out DatabaseUpdateAmbiguity? a) && !chosen.ContainsKey(a.Case);
                var row = new DatabaseUpdateRow
                {
                    Kind = ambiguous ? DatabaseUpdateKind.Ambiguous : DatabaseUpdateKind.Added, Decoy = key.Side.Decoy, Contaminant = key.Side.Contaminant,
                    NewIndex = j, NewIdentifier = records[j].Identifier, AmbiguousCase = a?.Case, Step = a?.Step,
                    Note = ambiguous ? a!.Reason + " Without a pick this record is added."
                        : a != null ? "Not chosen in ambiguous case " + a.Case.ToString(CultureInfo.InvariantCulture) + "; added."
                        : "New in this database; placed in a family by the clustering tests.",
                };
                if (ambiguous) counts.AmbiguousNewRecords++; else counts.Added++;
                output.Add(row);
            }
            counts.AmbiguousCases = ambiguities.Count(a => !chosen.ContainsKey(a.Case)
                && a.OldIndices.Any(i => KeyOf(rows[i].SequenceIdentifier).Side.Decoy == decoys));
            return counts;
        }

        private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
