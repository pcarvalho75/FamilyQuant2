using FamilyQuant.DatabaseUpdate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    /// <summary>An invented variant: exactly round(fraction x length) positions replaced by a different residue.</summary>
    private static string UpdateMutate(string sequence, int seed, double fraction)
    {
        var random = new Random(seed);
        char[] residues = sequence.ToCharArray();
        foreach (int position in Enumerable.Range(0, residues.Length).OrderBy(_ => random.Next()).Take((int)Math.Round(fraction * residues.Length)))
        {
            char replacement;
            do replacement = UpdateAminoAcids[random.Next(UpdateAminoAcids.Length)]; while (replacement == residues[position]);
            residues[position] = replacement;
        }
        return new string(residues);
    }

    private sealed record PlacementFixture(FamilyClusterFile Project, DatabaseUpdateSource Source, Dictionary<string, int> Old, Dictionary<string, int> New);

    private static PlacementFixture DatabaseUpdatePlacementFixture(bool exhaustive)
    {
        string a = UpdateSequence(101, 200), b = UpdateSequence(102, 180), m = UpdateSequence(103, 190), c = UpdateSequence(104, 150);
        string d = UpdateSequence(105, 160), p = UpdateSequence(106, 170);
        string a2 = UpdateMutate(a, 201, 0.10), x1 = UpdateMutate(m, 202, 0.18), y1 = UpdateMutate(m, 203, 0.18), c2 = UpdateMutate(c, 204, 0.10);
        string n1 = UpdateMutate(a, 205, 0.22), n2 = UpdateMutate(n1, 206, 0.22);
        var project = new FamilyClusterFile
        {
            ClusteringIdentity = 70, ClusteringMethod = exhaustive ? USearchTools.ExhaustiveClusteringMethod : USearchTools.FastClusteringMethod,
            SourceSha256 = "OLD", SourceSequenceCount = 8,
            MyExtendedFastaItems = new List<FastaItemExtended>
            {
                UpdateRow("sp|P30001|A1_SYN", "Alpha OS=Synthetica una OX=1", a, 1),
                UpdateRow("tr|Q30002|Q30002_SYN", "Alpha-like OS=Synthetica una OX=1", a2, 1),
                UpdateRow("sp|P30003|B1_SYN", "Beta OS=Synthetica una OX=1", b, 2),
                UpdateRow("tr|Q30004|Q30004_SYN", "Mu one OS=Synthetica una OX=1", x1, 3),
                UpdateRow("tr|Q30005|Q30005_SYN", "Mu two OS=Synthetica una OX=1", y1, 4),
                UpdateRow("sp|P30006|C1_SYN", "Gamma OS=Synthetica una OX=1", c, 5),
                UpdateRow("tr|Q30007|Q30007_SYN", "Gamma-like OS=Synthetica una OX=1", c2, 5),
                UpdateRow("sp|P30008|D1_SYN", "Delta OS=Synthetica una OX=1", d, 6),
            },
        };
        project.LockedUserIds.Add(2);
        var entries = new List<DatabaseUpdateEntry>
        {
            new("sp|P30001|A1_SYN", "Alpha OS=Synthetica una OX=1", a),
            new("tr|Q30002|Q30002_SYN", "Alpha-like OS=Synthetica una OX=1", a2),
            new("sp|P30003|B1_SYN", "Beta OS=Synthetica una OX=1", b),
            new("tr|Q30004|Q30004_SYN", "Mu one OS=Synthetica una OX=1", x1),
            new("tr|Q30005|Q30005_SYN", "Mu two OS=Synthetica una OX=1", y1),
            new("sp|P30006|C1_SYN", "Gamma OS=Synthetica una OX=1", UpdateSequence(107, 150)),
            new("tr|Q30007|Q30007_SYN", "Gamma-like OS=Synthetica una OX=1", c2),
            new("sp|P30008|D1_SYN", "Delta OS=Synthetica una OX=1", UpdateMutate(d, 207, 0.05)),
            new("tr|Q40001|Q40001_SYN", "Alpha new OS=Synthetica duo OX=2", n1),
            new("tr|Q40002|Q40002_SYN", "Alpha newer OS=Synthetica duo OX=2", n2),
            new("tr|Q40003|Q40003_SYN", "Beta new OS=Synthetica duo OX=2", UpdateMutate(b, 208, 0.05)),
            new("tr|Q40004|Q40004_SYN", "Alpha (Fragment) OS=Synthetica duo OX=2", a[..80]),
            new("tr|Q40005|Q40005_SYN", "Pi OS=Synthetica duo OX=2", p),
            new("tr|Q40006|Q40006_SYN", "Pi OS=Synthetica tres OX=3", UpdateMutate(p, 209, 0.05)),
            new("tr|Q40007|Q40007_SYN", "Mu OS=Synthetica duo OX=2", m),
        };
        return new PlacementFixture(project, DatabaseUpdateSource.FromEntries("new.fasta", "NEW", entries),
            project.MyExtendedFastaItems.Select((r, i) => (r.SequenceIdentifier, i)).ToDictionary(t => t.SequenceIdentifier, t => t.i, StringComparer.Ordinal),
            entries.Select((r, i) => (r.Identifier, i)).ToDictionary(t => t.Identifier, t => t.i, StringComparer.Ordinal));
    }

    private static void DatabaseUpdatePlacementTests()
    {
        Section("Database update: placing added proteins with the Build families tests (Z14c3b)");
        var fixture = DatabaseUpdatePlacementFixture(exhaustive: true);
        var comparison = DatabaseUpdateComparer.Compare(fixture.Project, fixture.Source);
        string before = DatabaseUpdateComparer.ProjectFingerprint(fixture.Project);
        DatabaseUpdatePlacement placement = DatabaseUpdatePlacer.Place(fixture.Project, comparison);
        DatabaseUpdateAddedPlacement Of(string identifier) => placement.Added.Single(p => p.NewIndex == fixture.New[identifier]);
        string Describe(DatabaseUpdateAddedPlacement p) => JsonSerializer.Serialize(p);

        Check("DU2: an added protein above the identity joins its nearest kept neighbour's family, with that protein's search number",
            Of("tr|Q40001|Q40001_SYN") is { Kind: DatabaseUpdatePlacementKind.NearestKeptNeighbour, UserId: 1, USearchId: 1, PassesSecondPass: true, JoinedLockedFamily: false },
            Describe(Of("tr|Q40001|Q40001_SYN")));
        Check("DU2: an added protein linked only to another added one follows it",
            Of("tr|Q40002|Q40002_SYN") is { Kind: DatabaseUpdatePlacementKind.FollowedAddedRelative, UserId: 1, PlacedBy: "tr|Q40001|Q40001_SYN" },
            Describe(Of("tr|Q40002|Q40002_SYN")));
        Check("DU2: an added protein may join a locked family, which stays locked and is counted",
            Of("tr|Q40003|Q40003_SYN") is { UserId: 2, JoinedLockedFamily: true } && placement.Counts.AddedJoinedLockedFamilies == 1
            && fixture.Project.LockedUserIds.SetEquals(new[] { 2 }), Describe(Of("tr|Q40003|Q40003_SYN")));
        Check("DU2: a fragment passing the fast-pass test but not the second-pass test still joins",
            Of("tr|Q40004|Q40004_SYN") is { UserId: 1, PassesSecondPass: false, Kind: DatabaseUpdatePlacementKind.NearestKeptNeighbour },
            Describe(Of("tr|Q40004|Q40004_SYN")));
        Check("DU2: added proteins linked only to each other form one new family numbered above every number in use",
            Of("tr|Q40005|Q40005_SYN") is { Kind: DatabaseUpdatePlacementKind.NewFamily, UserId: 7, USearchId: 7 }
            && Of("tr|Q40006|Q40006_SYN") is { Kind: DatabaseUpdatePlacementKind.NewFamily, UserId: 7 } && placement.Counts.NewFamilies == 1,
            Describe(Of("tr|Q40005|Q40005_SYN")) + " " + Describe(Of("tr|Q40006|Q40006_SYN")));
        var close = Of("tr|Q40007|Q40007_SYN");
        Check("DU2: a protein close to two kept families joins the nearest and both stay apart",
            (close.UserId == 3 && close.OtherLinkedFamilies.SequenceEqual(new[] { 4 }) || close.UserId == 4 && close.OtherLinkedFamilies.SequenceEqual(new[] { 3 }))
            && placement.Counts.AddedCloseToSeveralFamilies == 1, Describe(close));
        Check("DU2: placement counts", placement.Counts is { AddedJoinedFamilies: 5, AddedInNewFamilies: 2, NewFamilies: 1, Added: 7 },
            JsonSerializer.Serialize(placement.Counts));
        var gamma = placement.SequenceChecks.Single(c => c.OldIndex == fixture.Old["sp|P30006|C1_SYN"]);
        var delta = placement.SequenceChecks.Single(c => c.OldIndex == fixture.Old["sp|P30008|D1_SYN"]);
        Check("DU2: a changed sequence no longer close to its family is flagged and not moved",
            gamma is { LinkedToOwnFamily: false, HasOtherMembers: true, FamilyUserId: 5 }
            && placement.Rows.Single(r => r.OldIndex == fixture.Old["sp|P30006|C1_SYN"]) is { FamilyUserId: 5 } changedRow
            && changedRow.Note.Contains("no longer passes", StringComparison.Ordinal), JsonSerializer.Serialize(gamma));
        Check("DU2: a changed sequence alone in its family is not flagged", delta is { LinkedToOwnFamily: true, HasOtherMembers: false });
        Check("DU2: placement rows carry the family, the placing protein and a plain note",
            placement.Rows.Single(r => r.NewIndex == fixture.New["tr|Q40001|Q40001_SYN"]) is
                { FamilyUserId: 1, Placement: DatabaseUpdatePlacementKind.NearestKeptNeighbour, PlacedBy: "sp|P30001|A1_SYN" } row
            && row.Note.Contains("Joins family 1", StringComparison.Ordinal) && row.FamilyName.Length > 0);
        Check("DU2: placement changes nothing in the project", DatabaseUpdateComparer.ProjectFingerprint(fixture.Project) == before);
        Check("DU2: placing twice gives the same answer", JsonSerializer.Serialize(placement.Added) == JsonSerializer.Serialize(DatabaseUpdatePlacer.Place(fixture.Project, comparison).Added)
            && JsonSerializer.Serialize(placement.Rows) == JsonSerializer.Serialize(DatabaseUpdatePlacer.Place(fixture.Project, comparison).Rows));
        fixture.Project.MyExtendedFastaItems[0].UserAnnotation = "edited";
        Check("DU2: a project changed after the comparison is refused", Throws<InvalidOperationException>(() => DatabaseUpdatePlacer.Place(fixture.Project, comparison)));

        var fast = DatabaseUpdatePlacementFixture(exhaustive: false);
        var fastPlacement = DatabaseUpdatePlacer.Place(fast.Project, DatabaseUpdateComparer.Compare(fast.Project, fast.Source));
        var fastClose = fastPlacement.Added.Single(p => p.NewIndex == fast.New["tr|Q40007|Q40007_SYN"]);
        Check("DU2: a project built without the second pass uses the fast-pass test only and lists no second-pass links",
            !fastPlacement.Exhaustive && fastPlacement.Added.All(p => !p.PassesSecondPass) && fastClose.OtherLinkedFamilies.Count == 0
            && fastPlacement.Added.Single(p => p.NewIndex == fast.New["tr|Q40004|Q40004_SYN"]).UserId == 1);

        // Test 3: where no kept family would be merged, the update's partition equals a fresh Build families.
        string directory = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "database-update-rebuild");
        Directory.CreateDirectory(directory);
        string g = UpdateSequence(301, 210), h = UpdateSequence(302, 190), k = UpdateSequence(303, 175), q = UpdateSequence(304, 160);
        var oldRecords = new List<(string Id, string Sequence)>
        {
            ("sp|P50001|G1_SYN", g), ("tr|Q50002|Q50002_SYN", UpdateMutate(g, 311, 0.08)), ("tr|Q50003|Q50003_SYN", UpdateMutate(g, 312, 0.12)),
            ("sp|P50004|H1_SYN", h), ("tr|Q50005|Q50005_SYN", UpdateMutate(h, 313, 0.10)), ("sp|P50006|K1_SYN", k),
        };
        var newRecords = oldRecords.Concat(new (string Id, string Sequence)[]
        {
            ("tr|Q60001|Q60001_SYN", UpdateMutate(g, 314, 0.15)), ("tr|Q60002|Q60002_SYN", UpdateMutate(k, 315, 0.10)),
            ("tr|Q60003|Q60003_SYN", q), ("tr|Q60004|Q60004_SYN", UpdateMutate(q, 316, 0.10)),
        }).ToList();
        string oldPath = Path.Combine(directory, "old.fasta"), newPath = Path.Combine(directory, "new.fasta");
        File.WriteAllText(oldPath, string.Concat(oldRecords.Select(r => ">" + r.Id + " Synthetic OS=Synthetica una OX=1\n" + r.Sequence + "\n")));
        File.WriteAllText(newPath, string.Concat(newRecords.Select(r => ">" + r.Id + " Synthetic OS=Synthetica una OX=1\n" + r.Sequence + "\n")));
        FamilyClusterFile built = USearchTools.Cluster(new FileInfo(oldPath), 70, exhaustive: true).Document;
        var update = DatabaseUpdatePlacer.Place(built, DatabaseUpdateComparer.Compare(built, DatabaseUpdateSource.Read(newPath)));
        var updateFamilies = built.MyExtendedFastaItems.Select(r => (r.SequenceIdentifier, Family: r.UserID))
            .Concat(update.Added.Select(p => (DatabaseUpdateSource.Read(newPath).Records[p.NewIndex].Identifier, Family: p.UserId)));
        FamilyClusterFile rebuilt = USearchTools.Cluster(new FileInfo(newPath), 70, exhaustive: true).Document;
        Check("DU3: with no family to merge, the update's families equal a fresh Build families of the new database (numbers aside)",
            Partition(updateFamilies) == Partition(rebuilt.MyExtendedFastaItems.Select(r => (r.SequenceIdentifier, Family: r.USearchID))),
            Partition(updateFamilies) + " | " + Partition(rebuilt.MyExtendedFastaItems.Select(r => (r.SequenceIdentifier, Family: r.USearchID))));
    }

    private static string Partition(IEnumerable<(string Identifier, int Family)> members) => string.Join(" / ",
        members.GroupBy(m => m.Family).Select(g => string.Join(",", g.Select(m => m.Identifier).OrderBy(i => i, StringComparer.Ordinal)))
            .OrderBy(s => s, StringComparer.Ordinal));
}
