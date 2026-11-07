using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DatabaseUpdate
{
    /// <summary>Where one added record goes. <see cref="PlacedByOldIndex"/> is the kept project row whose link
    /// placed it, or for a follower the kept row behind the added relative it followed.</summary>
    public sealed record DatabaseUpdateAddedPlacement(int NewIndex, int UserId, int USearchId, DatabaseUpdatePlacementKind Kind,
        int? PlacedByOldIndex, string PlacedBy, double? IdentityPercent, int? AlignmentLength, bool PassesSecondPass,
        bool JoinedLockedFamily, IReadOnlyList<int> OtherLinkedFamilies);

    /// <summary>A kept protein whose sequence changed: is it still linked to another member of its family?</summary>
    public sealed record DatabaseUpdateSequenceCheck(int OldIndex, int NewIndex, int FamilyUserId, bool HasOtherMembers,
        bool LinkedToOwnFamily, int? NearestFamilyUserId, double? NearestIdentityPercent);

    /// <summary>The placement of every added record, read only. Rows are the comparison's rows with the
    /// placement filled in for added records and the check noted on changed sequences.</summary>
    public sealed class DatabaseUpdatePlacement
    {
        public string ProjectFingerprint { get; init; } = string.Empty;
        /// <summary>The new file and the comparison this placement was made from; Preview and Apply refuse any other.</summary>
        public string SourceFingerprint { get; init; } = string.Empty;
        public string ComparisonFingerprint { get; init; } = string.Empty;
        public int ClusteringIdentity { get; init; }
        public bool Exhaustive { get; init; }
        public string Method { get; init; } = string.Empty;
        public IReadOnlyList<DatabaseUpdateAddedPlacement> Added { get; init; } = Array.Empty<DatabaseUpdateAddedPlacement>();
        public IReadOnlyList<DatabaseUpdateSequenceCheck> SequenceChecks { get; init; } = Array.Empty<DatabaseUpdateSequenceCheck>();
        public IReadOnlyList<DatabaseUpdateRow> Rows { get; init; } = Array.Empty<DatabaseUpdateRow>();
        public DatabaseUpdateCounts Counts { get; init; } = new();
        public DatabaseUpdateCounts DecoyCounts { get; init; } = new();
        /// <summary>Plain sentences for the preview, for example that only the fast-pass test was used.</summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Places the added records of a comparison with the tests Build families uses, at the project's own identity
    /// (docs/design/DATABASE-UPDATE.md section 4). Every added sequence is aligned with every kept sequence by
    /// USEARCH search_global, the call the neighbour search already makes; a hit at or above the identity is the
    /// fast-pass test cluster_fast makes against its centroids, and a hit that also passes
    /// <see cref="USearchTools.PassesExhaustiveGate"/> is a second-pass link. An added protein joins the family of
    /// its best link; added proteins with no link follow the family most of their added-only relatives joined
    /// (the added set clustered by <see cref="USearchTools.Cluster"/>); the rest form new families numbered above
    /// every number in use. Kept families are never merged and kept proteins never move. Links and relatives are
    /// only ever on the same side: targets with targets, decoys with decoys. Second-pass links exist only for a
    /// project whose method is <see cref="USearchTools.ExhaustiveClusteringMethod"/>.
    /// </summary>
    public static class DatabaseUpdatePlacer
    {
        public const string Method = "Database update placement v1: added sequences aligned with every kept sequence by USEARCH search_global at the "
            + "project identity (each hit is a fast-pass link; a hit passing Build families' second-pass test is a second-pass link); an added protein "
            + "joins the family of its best link (second-pass links first, then identity, aligned length, family number, kept row); an added protein "
            + "with no link follows the family most of its added-only relatives joined (the added set clustered as Build families clusters); the rest "
            + "form new families numbered above every number in use; kept families are never merged and kept proteins never move";

        private sealed record Link(int OldIndex, int Family, int SearchId, double Identity, int AlignmentLength, bool SecondPass);

        public static DatabaseUpdatePlacement Place(FamilyClusterFile project, DatabaseUpdateComparison comparison, OperationContext? operation = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(comparison);
            if (DatabaseUpdateComparer.ProjectFingerprint(project) != comparison.ProjectFingerprint)
                throw new InvalidOperationException("The project changed since it was compared with the new database. Compare again.");
            DatabaseUpdateComparer.RequireConsistent(comparison);
            int identity = project.ClusteringIdentity;
            if (identity <= 0 || identity > 100) throw new InvalidDataException("The project records no clustering identity, so new proteins cannot be placed.");
            // Only the method string Build families writes after its second pass earns second-pass links; an empty,
            // older or fast-only method is placed by the fast-pass test (docs/design/DATABASE-UPDATE.md section 4).
            bool exhaustive = string.Equals(project.ClusteringMethod, USearchTools.ExhaustiveClusteringMethod, StringComparison.Ordinal);
            var warnings = new List<string>();
            if (!exhaustive)
                warnings.Add(string.Equals(project.ClusteringMethod, USearchTools.FastClusteringMethod, StringComparison.Ordinal)
                    ? "This project was built without the exhaustive second pass, so new proteins are placed by the fast-pass test only."
                    : "This project does not record that Build families ran its exhaustive second pass ("
                        + (string.IsNullOrWhiteSpace(project.ClusteringMethod) ? "no method recorded" : "method \"" + project.ClusteringMethod + "\"")
                        + "), so new proteins are placed by the fast-pass test only.");
            List<FastaItemExtended> rows = project.MyExtendedFastaItems;
            IReadOnlyList<DatabaseUpdateEntry> records = comparison.Source.Records;

            // The kept proteins with the sequences they will have after the update.
            var kept = comparison.Matches.Where(m => rows[m.OldIndex].HasUserId)
                .Select(m => (m.OldIndex, m.NewIndex, Sequence: records[m.NewIndex].Sequence)).ToList();
            var changed = comparison.Matches.Where(m => m.Kind == DatabaseUpdateKind.SequenceChanged && rows[m.OldIndex].HasUserId).ToList();
            List<int> added = comparison.Added.ToList();
            var hits = new Dictionary<int, List<(int Kept, USearchTools.PairwiseEdge Edge)>>();
            // A target never takes its family from a decoy, nor a decoy from a target (section 3).
            bool[] keptDecoy = kept.Select(k => SequenceLabels.IsDecoy(rows[k.OldIndex].SequenceIdentifier ?? string.Empty)).ToArray();
            bool[] queryDecoy = added.Select(j => SequenceLabels.IsDecoy(records[j].Identifier))
                .Concat(changed.Select(m => SequenceLabels.IsDecoy(records[m.NewIndex].Identifier))).ToArray();
            if (kept.Count > 0 && (added.Count > 0 || changed.Count > 0))
            {
                // Queries: added records first, then changed sequences; labels are positions, never headers.
                var queries = added.Select(j => records[j].Sequence).Concat(changed.Select(m => records[m.NewIndex].Sequence)).ToList();
                string directory = ApplicationPaths.ScratchDirectory;
                Directory.CreateDirectory(directory);
                string token = Guid.NewGuid().ToString("N");
                string queryPath = Path.Combine(directory, "dbupdate_q_" + token + ".fasta");
                string keptPath = Path.Combine(directory, "dbupdate_k_" + token + ".fasta");
                try
                {
                    WriteFasta(queryPath, 'q', queries);
                    WriteFasta(keptPath, 'k', kept.Select(k => k.Sequence));
                    operation?.Report("database-update", "Aligning " + Count(queries.Count) + " new and changed sequences with "
                        + Count(kept.Count) + " kept sequences", 0, 1);
                    USearchTools.SearchGlobal(queryPath, keptPath, identity / 100.0, edge =>
                    {
                        int q = Label(edge.QueryId, 'q', queries.Count), k = Label(edge.TargetId, 'k', kept.Count);
                        if (queryDecoy[q] != keptDecoy[k]) return;
                        if (!hits.TryGetValue(q, out var list)) hits[q] = list = new();
                        list.Add((k, edge));
                    }, operation);
                }
                finally { TryDelete(queryPath); TryDelete(keptPath); }
            }
            operation?.ThrowIfCancellationRequested();

            Link ToLink((int Kept, USearchTools.PairwiseEdge Edge) hit)
            {
                int i = kept[hit.Kept].OldIndex;
                return new Link(i, rows[i].UserID, rows[i].USearchID, hit.Edge.IdentityPercent, hit.Edge.AlignmentLength,
                    exhaustive && USearchTools.PassesExhaustiveGate(hit.Edge, identity));
            }
            static IEnumerable<Link> Ranked(IEnumerable<Link> links) => links.OrderByDescending(l => l.SecondPass)
                .ThenByDescending(l => l.Identity).ThenByDescending(l => l.AlignmentLength).ThenBy(l => l.Family).ThenBy(l => l.OldIndex);

            // Nearest kept neighbour.
            var placed = new Dictionary<int, DatabaseUpdateAddedPlacement>();
            for (int q = 0; q < added.Count; q++)
            {
                if (!hits.TryGetValue(q, out var list)) continue;
                var links = Ranked(list.Select(ToLink)).ToList();
                Link best = links[0];
                var others = exhaustive ? links.Where(l => l.SecondPass && l.Family != best.Family).Select(l => l.Family).Distinct().OrderBy(f => f).ToList()
                    : new List<int>();
                placed[added[q]] = new DatabaseUpdateAddedPlacement(added[q], best.Family, best.SearchId, DatabaseUpdatePlacementKind.NearestKeptNeighbour,
                    best.OldIndex, rows[best.OldIndex].SequenceIdentifier ?? string.Empty, best.Identity, best.AlignmentLength, best.SecondPass,
                    project.LockedUserIds.Contains(best.Family), others);
            }

            // Added-only relatives, then new families.
            int next = rows.Select(r => Math.Max(r.UserID, r.USearchID)).Concat(project.LockedUserIds).Append(0).Max();
            var unplaced = added.Where(j => !placed.ContainsKey(j)).ToList();
            if (unplaced.Count > 0)
            {
                Dictionary<int, int> relatives = AddedOnlyFamilies(added.Select(j => records[j].Sequence).ToList(), identity, exhaustive, operation);
                // Relatives are followed only on the same side: a decoy never follows a target, nor a target a decoy.
                var byFamily = added.Select((j, q) => (j, Family: (relatives[q], SequenceLabels.IsDecoy(records[j].Identifier)))).GroupBy(t => t.Family)
                    .OrderBy(g => g.Min(t => t.j)).ToList();
                foreach (var group in byFamily)
                {
                    var members = group.Select(t => t.j).OrderBy(j => j).ToList();
                    var placedMembers = members.Where(placed.ContainsKey).Select(j => placed[j]).ToList();
                    var waiting = members.Where(j => !placed.ContainsKey(j)).ToList();
                    if (waiting.Count == 0) continue;
                    if (placedMembers.Count > 0)
                    {
                        // The family most placed relatives joined; a tie goes to the relative with the best link.
                        DatabaseUpdateAddedPlacement guide = placedMembers.GroupBy(p => p.UserId)
                            .OrderByDescending(g => g.Count())
                            .ThenBy(g => Rank(g.OrderBy(Order).First()))
                            .Select(g => g.OrderBy(Order).First()).First();
                        foreach (int j in waiting)
                            placed[j] = new DatabaseUpdateAddedPlacement(j, guide.UserId, guide.USearchId, DatabaseUpdatePlacementKind.FollowedAddedRelative,
                                guide.PlacedByOldIndex, records[guide.NewIndex].Identifier, null, null, false, project.LockedUserIds.Contains(guide.UserId),
                                Array.Empty<int>());
                    }
                    else
                    {
                        if (next == int.MaxValue) throw new InvalidOperationException("There are no unused family numbers above the existing numbers.");
                        next++;
                        foreach (int j in waiting)
                            placed[j] = new DatabaseUpdateAddedPlacement(j, next, next, DatabaseUpdatePlacementKind.NewFamily, null, string.Empty,
                                null, null, false, false, Array.Empty<int>());
                    }
                }
            }

            // Changed sequences: still linked to another member of their own family?
            var checks = new List<DatabaseUpdateSequenceCheck>();
            for (int c = 0; c < changed.Count; c++)
            {
                DatabaseUpdateMatch match = changed[c];
                int family = rows[match.OldIndex].UserID;
                bool hasOthers = kept.Any(k => k.OldIndex != match.OldIndex && rows[k.OldIndex].UserID == family);
                var links = hits.TryGetValue(added.Count + c, out var list)
                    ? Ranked(list.Where(h => kept[h.Kept].OldIndex != match.OldIndex).Select(ToLink)).ToList() : new List<Link>();
                bool linked = links.Any(l => l.Family == family);
                checks.Add(new DatabaseUpdateSequenceCheck(match.OldIndex, match.NewIndex, family, hasOthers, linked || !hasOthers,
                    links.Count == 0 ? null : links[0].Family, links.Count == 0 ? null : links[0].Identity));
            }

            var (rowsOut, counts, decoyCounts) = Fill(project, comparison, placed, checks);
            return new DatabaseUpdatePlacement
            {
                ProjectFingerprint = comparison.ProjectFingerprint, SourceFingerprint = comparison.SourceFingerprint,
                ComparisonFingerprint = comparison.ComparisonFingerprint, ClusteringIdentity = identity, Exhaustive = exhaustive, Method = Method,
                Added = added.Select(j => placed[j]).ToArray(), SequenceChecks = checks, Rows = rowsOut, Counts = counts, DecoyCounts = decoyCounts,
                Warnings = warnings,
            };

            static (int, double, int, int, int) Order(DatabaseUpdateAddedPlacement p) => Rank(p);
            static (int, double, int, int, int) Rank(DatabaseUpdateAddedPlacement p) =>
                (p.PassesSecondPass ? 0 : 1, -(p.IdentityPercent ?? 0), -(p.AlignmentLength ?? 0), p.UserId, p.NewIndex);
        }

        /// <summary>The added sequences clustered among themselves as Build families clusters a database; the
        /// result maps each added position to its added-only family.</summary>
        private static Dictionary<int, int> AddedOnlyFamilies(List<string> sequences, int identity, bool exhaustive, OperationContext? operation)
        {
            string directory = Path.Combine(ApplicationPaths.ScratchDirectory, "dbupdate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "added.fasta");
            try
            {
                WriteFasta(path, 'a', sequences);
                operation?.Report("database-update", "Clustering " + Count(sequences.Count) + " new sequences among themselves", 0, 1);
                ClusteringResult result = USearchTools.Cluster(new FileInfo(path), identity, exhaustive, operation);
                var families = new Dictionary<int, int>();
                foreach (FastaItemExtended item in result.Document.MyExtendedFastaItems)
                    families[Label(item.SequenceIdentifier, 'a', sequences.Count)] = item.USearchID;
                if (families.Count != sequences.Count)
                    throw new InvalidDataException("USEARCH left new sequences out of their clustering. No placement was made.");
                return families;
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch { /* a leftover scratch folder is harmless */ }
            }
        }

        private static (List<DatabaseUpdateRow>, DatabaseUpdateCounts, DatabaseUpdateCounts) Fill(FamilyClusterFile project,
            DatabaseUpdateComparison comparison, Dictionary<int, DatabaseUpdateAddedPlacement> placed, List<DatabaseUpdateSequenceCheck> checks)
        {
            var names = FamilyNaming.Names(project.MyExtendedFastaItems.Where(r => !SequenceLabels.IsDecoy(r.SequenceIdentifier ?? string.Empty)));
            var byOld = checks.ToDictionary(c => c.OldIndex);
            var output = new List<DatabaseUpdateRow>();
            foreach (DatabaseUpdateRow source in comparison.Rows)
            {
                DatabaseUpdateRow row = Copy(source);
                if (row.NewIndex is int j && row.OldIndex == null && placed.TryGetValue(j, out DatabaseUpdateAddedPlacement? p))
                {
                    row.FamilyUserId = p.UserId; row.FamilySearchId = p.USearchId; row.Placement = p.Kind;
                    row.FamilyName = names.TryGetValue(p.UserId, out string? name) ? name : string.Empty;
                    row.PlacedBy = p.PlacedBy; row.PlacedByIdentityPercent = p.IdentityPercent; row.PlacedByAlignmentLength = p.AlignmentLength;
                    row.JoinedLockedFamily = p.JoinedLockedFamily; row.OtherLinkedFamilies = p.OtherLinkedFamilies.Count == 0 ? null : p.OtherLinkedFamilies.ToList();
                    string where = p.Kind switch
                    {
                        DatabaseUpdatePlacementKind.NearestKeptNeighbour => "Joins family " + Number(p.UserId) + " through " + p.PlacedBy + " ("
                            + p.IdentityPercent!.Value.ToString("0.#", CultureInfo.InvariantCulture) + "% identity over " + Number(p.AlignmentLength!.Value)
                            + " aligned columns" + (p.PassesSecondPass ? "" : "; fast-pass test only") + ").",
                        DatabaseUpdatePlacementKind.FollowedAddedRelative => "Joins family " + Number(p.UserId) + " with its new relative " + p.PlacedBy + ".",
                        _ => "Starts new family " + Number(p.UserId) + ".",
                    };
                    if (p.JoinedLockedFamily) where += " The family is locked and stays locked.";
                    if (p.OtherLinkedFamilies.Count > 0)
                        where += " Also close to families " + string.Join(", ", p.OtherLinkedFamilies.Select(Number)) + "; families are not merged.";
                    row.Note = (row.Note.Length == 0 ? "" : row.Note + " ") + where;
                }
                else if (row.OldIndex is int i && byOld.TryGetValue(i, out DatabaseUpdateSequenceCheck? check) && !check.LinkedToOwnFamily)
                {
                    row.Note += " Its new sequence no longer passes the clustering test with any other member of family " + Number(check.FamilyUserId)
                        + (check.NearestFamilyUserId is int nearest ? "; nearest now family " + Number(nearest) + "." : "; no kept protein is close to it.")
                        + " Not moved.";
                }
                output.Add(row);
            }
            var counts = Copy(comparison.Counts);
            var decoyCounts = Copy(comparison.DecoyCounts);
            foreach (DatabaseUpdateAddedPlacement p in placed.Values)
            {
                bool decoy = SequenceLabels.IsDecoy(comparison.Source.Records[p.NewIndex].Identifier);
                DatabaseUpdateCounts target = decoy ? decoyCounts : counts;
                if (p.Kind == DatabaseUpdatePlacementKind.NewFamily) target.AddedInNewFamilies++;
                else target.AddedJoinedFamilies++;
                if (p.JoinedLockedFamily) target.AddedJoinedLockedFamilies++;
                if (p.OtherLinkedFamilies.Count > 0) target.AddedCloseToSeveralFamilies++;
            }
            foreach (var family in placed.Values.Where(p => p.Kind == DatabaseUpdatePlacementKind.NewFamily).GroupBy(p => p.UserId))
            {
                bool decoy = family.All(p => SequenceLabels.IsDecoy(comparison.Source.Records[p.NewIndex].Identifier));
                (decoy ? decoyCounts : counts).NewFamilies++;
            }
            return (output, counts, decoyCounts);
        }

        private static DatabaseUpdateRow Copy(DatabaseUpdateRow row) => new()
        {
            Kind = row.Kind, Step = row.Step, Picked = row.Picked, AmbiguousCase = row.AmbiguousCase, Decoy = row.Decoy, Contaminant = row.Contaminant,
            OldIndex = row.OldIndex, NewIndex = row.NewIndex, OldIdentifier = row.OldIdentifier, NewIdentifier = row.NewIdentifier,
            FamilyUserId = row.FamilyUserId, FamilySearchId = row.FamilySearchId, FamilyName = row.FamilyName, Placement = row.Placement,
            PlacedBy = row.PlacedBy, PlacedByIdentityPercent = row.PlacedByIdentityPercent, PlacedByAlignmentLength = row.PlacedByAlignmentLength,
            JoinedLockedFamily = row.JoinedLockedFamily, OtherLinkedFamilies = row.OtherLinkedFamilies?.ToList(), Note = row.Note,
        };

        private static DatabaseUpdateCounts Copy(DatabaseUpdateCounts counts) => new()
        {
            Unchanged = counts.Unchanged, HeaderChanged = counts.HeaderChanged, SequenceChanged = counts.SequenceChanged,
            NewAccessionSameSequence = counts.NewAccessionSameSequence, ReplacedByLonger = counts.ReplacedByLonger, Removed = counts.Removed,
            Added = counts.Added, AmbiguousCases = counts.AmbiguousCases, AmbiguousOldRows = counts.AmbiguousOldRows,
            AmbiguousNewRecords = counts.AmbiguousNewRecords,
        };

        private static void WriteFasta(string path, char prefix, IEnumerable<string> sequences)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
            int n = 0;
            foreach (string sequence in sequences)
            {
                writer.WriteLine(">" + prefix + n.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(sequence);
                n++;
            }
        }

        private static int Label(string label, char prefix, int count)
        {
            if (label.Length > 1 && label[0] == prefix && int.TryParse(label.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index < count) return index;
            throw new InvalidDataException("USEARCH reported a label it was not given: \"" + label + "\".");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* a leftover scratch file is harmless */ }
        }

        private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
