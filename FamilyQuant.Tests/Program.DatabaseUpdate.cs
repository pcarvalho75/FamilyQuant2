using FamilyQuant.DatabaseUpdate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DatabaseUpdateCommands() =>
    [
        new("--test-database-update", 100, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-database-update", (_, _) =>
            {
                DatabaseUpdateMatchingTests();
                DatabaseUpdatePlacementTests();
                DatabaseUpdatePlacementSideTests();
                DatabaseUpdateApplyTests();
                DatabaseUpdateBindingTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private const string UpdateAminoAcids = "ACDEFGHIKLMNPQRSTVWY";

    /// <summary>A reproducible invented protein sequence; no real protein is used anywhere in these checks.</summary>
    private static string UpdateSequence(int seed, int length)
    {
        var random = new Random(seed);
        return new string(Enumerable.Range(0, length).Select(_ => UpdateAminoAcids[random.Next(UpdateAminoAcids.Length)]).ToArray());
    }

    private static string UpdateReverse(string text) => new string(text.Reverse().ToArray());

    private static FastaItemExtended UpdateRow(string identifier, string description, string sequence, int family) => new()
    {
        SequenceIdentifier = identifier, Description = description, Sequence = sequence, UserID = family, USearchID = family,
    };

    private sealed record UpdateFixture(FamilyClusterFile Project, DatabaseUpdateSource Source, Dictionary<string, int> Old, Dictionary<string, int> New);

    /// <summary>Every class of section 3 of docs/design/DATABASE-UPDATE.md, a decoy side and contaminants.</summary>
    private static UpdateFixture DatabaseUpdateFixture()
    {
        string a = UpdateSequence(1, 120), c = UpdateSequence(3, 140), c2 = UpdateSequence(33, 140), d = UpdateSequence(4, 90);
        string e = UpdateSequence(5, 30), f = UpdateSequence(6, 80), g = UpdateSequence(7, 70), h = UpdateSequence(8, 60), h2 = UpdateSequence(88, 64);
        string b = UpdateSequence(2, 100), k = UpdateSequence(9, 50), tag = "YPYDVPDYA";
        string r1 = UpdateSequence(11, 60), r2 = UpdateSequence(12, 60), r3 = UpdateSequence(13, 60), r4 = UpdateSequence(14, 60);
        string s = UpdateSequence(15, 75), x = UpdateSequence(16, 40), y = UpdateSequence(17, 45), z = UpdateSequence(18, 35), w = UpdateSequence(19, 38);
        var project = new FamilyClusterFile
        {
            ClusteringIdentity = 70, SourceSha256 = "OLD", SourceSequenceCount = 20,
            MyExtendedFastaItems = new List<FastaItemExtended>
            {
                UpdateRow("sp|P10001|AAA_SYN", "Alpha one OS=Synthetica una OX=1", a, 1),
                UpdateRow("tr|Q10002|Q10002_SYN", "Beta OS=Synthetica una OX=1", b, 2),
                UpdateRow("sp|P10003|CCC_SYN", "Gamma OS=Synthetica una OX=1", c, 3),
                UpdateRow("tr|Q10004|Q10004_SYN", "Delta OS=Synthetica una OX=1", d, 4),
                UpdateRow("tr|Q10005|Q10005_SYN", "Epsilon (Fragment) OS=Synthetica una OX=1", e, 5),
                UpdateRow("tr|Q10006|Q10006_SYN", "Zeta OS=Synthetica una OX=1", f, 6),
                UpdateRow("sp|P10008-2|DDD_SYN", "Isoform 2 of Eta OS=Synthetica una OX=1", h, 8),
                UpdateRow("tr|Q10011|A_SYN", "Theta OS=Synthetica una OX=1", r1, 11),
                UpdateRow("sp|Q10011|B_SYN", "Theta OS=Synthetica una OX=1", r2, 11),
                UpdateRow("tr|Q10012|Q10012_SYN", "Iota OS=Synthetica una OX=1", s, 12),
                UpdateRow("tr|Q10013|Q10013_SYN", "Iota OS=Synthetica duo OX=2", s, 12),
                UpdateRow("tr|Q10014|Q10014_SYN", "Kappa part OS=Synthetica una OX=1", x, 14),
                UpdateRow("tr|Q10015|Q10015_SYN", "Kappa part OS=Synthetica una OX=1", y, 14),
                UpdateRow("tr|Q10016|Q10016_SYN", "Lambda part OS=Synthetica una OX=1", z, 16),
                UpdateRow("tr|Q10017|Q10017_SYN", "Lambda part OS=Synthetica una OX=1", w, 16),
                UpdateRow("contaminant_P10001|K1_SYN", "", k, 20),
                UpdateRow("contaminant_AAAA1|Anti-HA_AffinityTag", "", tag, 21),
                UpdateRow("Reverse_sp|P10001|AAA_SYN", "", UpdateReverse(a), 30),
                UpdateRow("Reverse_tr|Q10006|Q10006_SYN", "", UpdateReverse(f), 31),
            },
        };
        var entries = new List<DatabaseUpdateEntry>
        {
            new("sp|P10001|AAA_SYN", "Alpha one OS=Synthetica una OX=1", a),
            new("sp|Q10002|BBB_SYN", "Beta reviewed OS=Synthetica una OX=1", b),
            new("sp|P10003|CCC_SYN", "Gamma OS=Synthetica una OX=1", c2),
            new("tr|Q20004|Q20004_SYN", "Delta OS=Synthetica una OX=1", d),
            new("tr|Q20005|Q20005_SYN", "Epsilon OS=Synthetica una OX=1", "MKT" + e + "GGG"),
            new("tr|Q20007|Q20007_SYN", "Omicron OS=Synthetica una OX=1", g),
            new("sp|P10008-2|DDD_SYN", "Isoform 2 of Eta OS=Synthetica una OX=1", h),
            new("sp|P10008|DDD_SYN", "Eta OS=Synthetica una OX=1", h2),
            new("tr|Q10011|A_SYN", "Theta OS=Synthetica una OX=1", r3),
            new("sp|Q10011|B_SYN", "Theta OS=Synthetica una OX=1", r4),
            new("tr|Q20012|Q20012_SYN", "Iota OS=Synthetica una OX=1", s),
            new("tr|Q20014|Q20014_SYN", "Kappa OS=Synthetica una OX=1", x + y),
            new("tr|Q20016|Q20016_SYN", "Lambda OS=Synthetica una OX=1", z + "AAAA"),
            new("tr|Q20017|Q20017_SYN", "Lambda OS=Synthetica una OX=1", z + w),
            new("contaminant_P10001|K1_SYN", "", k),
            new("contaminant_AAAA1|Anti-HA_AffinityTag", "", tag),
            new("Reverse_sp|P10001|AAA_SYN", "", UpdateReverse(a)),
        };
        var source = DatabaseUpdateSource.FromEntries("new.fasta", "NEW", entries);
        return new UpdateFixture(project, source,
            project.MyExtendedFastaItems.Select((p, i) => (p.SequenceIdentifier, i)).ToDictionary(t => t.SequenceIdentifier, t => t.i, StringComparer.Ordinal),
            entries.Select((p, i) => (p.Identifier, i)).ToDictionary(t => t.Identifier, t => t.i, StringComparer.Ordinal));
    }

    private static void DatabaseUpdateMatchingTests()
    {
        Section("Database update: one-to-one matching of a project with a new database (Z14c3a)");
        var fixture = DatabaseUpdateFixture();
        var (project, source, old, @new) = (fixture.Project, fixture.Source, fixture.Old, fixture.New);
        DatabaseUpdateComparison comparison = DatabaseUpdateComparer.Compare(project, source);

        DatabaseUpdateKind? KindOfOld(string identifier) =>
            comparison.Matches.FirstOrDefault(m => m.OldIndex == old[identifier])?.Kind;
        int? PartnerOf(string identifier) => comparison.Matches.FirstOrDefault(m => m.OldIndex == old[identifier])?.NewIndex;
        Check("DU1: an identical row is unchanged and paired with its record", KindOfOld("sp|P10001|AAA_SYN") == DatabaseUpdateKind.Unchanged
            && PartnerOf("sp|P10001|AAA_SYN") == @new["sp|P10001|AAA_SYN"]);
        Check("DU1: tr to sp with a new entry name is a header change of the same accession",
            KindOfOld("tr|Q10002|Q10002_SYN") == DatabaseUpdateKind.HeaderChanged && PartnerOf("tr|Q10002|Q10002_SYN") == @new["sp|Q10002|BBB_SYN"]);
        Check("DU1: a new sequence under the same accession is a sequence change", KindOfOld("sp|P10003|CCC_SYN") == DatabaseUpdateKind.SequenceChanged);
        Check("DU1: the same sequence under a new accession pairs as a header change of step 2",
            KindOfOld("tr|Q10004|Q10004_SYN") == DatabaseUpdateKind.NewAccessionSameSequence && PartnerOf("tr|Q10004|Q10004_SYN") == @new["tr|Q20004|Q20004_SYN"]);
        Check("DU1: an old sequence inside exactly one longer new record is replaced by it",
            KindOfOld("tr|Q10005|Q10005_SYN") == DatabaseUpdateKind.ReplacedByLonger && PartnerOf("tr|Q10005|Q10005_SYN") == @new["tr|Q20005|Q20005_SYN"]);
        Check("DU1: a row absent from the new file is removed", comparison.Removed.Contains(old["tr|Q10006|Q10006_SYN"])
            && comparison.Rows.Any(r => r.OldIndex == old["tr|Q10006|Q10006_SYN"] && r.Kind == DatabaseUpdateKind.Removed && r.FamilyUserId == 6));
        Check("DU1: a new record is added", comparison.Added.Contains(@new["tr|Q20007|Q20007_SYN"])
            && comparison.Rows.Any(r => r.NewIndex == @new["tr|Q20007|Q20007_SYN"] && r.Kind == DatabaseUpdateKind.Added));
        Check("DU1: an isoform keeps its suffix in the key: the isoform is unchanged and the canonical record is added",
            KindOfOld("sp|P10008-2|DDD_SYN") == DatabaseUpdateKind.Unchanged && comparison.Added.Contains(@new["sp|P10008|DDD_SYN"]));
        Check("DU1: a contaminant with a target's accession matches only the contaminant",
            PartnerOf("contaminant_P10001|K1_SYN") == @new["contaminant_P10001|K1_SYN"] && PartnerOf("sp|P10001|AAA_SYN") == @new["sp|P10001|AAA_SYN"]);
        Check("DU1: an identifier without a UniProt accession is keyed by the whole identifier",
            KindOfOld("contaminant_AAAA1|Anti-HA_AffinityTag") == DatabaseUpdateKind.Unchanged);
        Check("DU1: a decoy matches only its decoy record and a decoy without one is removed",
            PartnerOf("Reverse_sp|P10001|AAA_SYN") == @new["Reverse_sp|P10001|AAA_SYN"] && comparison.Removed.Contains(old["Reverse_tr|Q10006|Q10006_SYN"])
            && comparison.DecoyCounts.Unchanged == 1 && comparison.DecoyCounts.Removed == 1);

        var cases = comparison.Ambiguities;
        DatabaseUpdateAmbiguity? CaseOf(string oldIdentifier) => cases.FirstOrDefault(a => a.OldIndices.Contains(old[oldIdentifier]));
        Check("DU1: four ambiguous cases, numbered by step and then by the first project row",
            cases.Count == 4 && cases.Select(a => a.Case).SequenceEqual(new[] { 1, 2, 3, 4 })
            && cases[0].Step == DatabaseUpdateStep.RepeatedKey && cases[1].Step == DatabaseUpdateStep.SameSequence
            && cases[2].Step == DatabaseUpdateStep.Containment && cases[3].Step == DatabaseUpdateStep.Containment,
            string.Join("; ", cases.Select(a => a.Case + " " + a.Step)));
        Check("DU1: two rows and two records of one accession with no unique identical sequence are ambiguous",
            CaseOf("tr|Q10011|A_SYN") is { Step: DatabaseUpdateStep.RepeatedKey } repeated && repeated.OldIndices.Count == 2 && repeated.NewIndices.Count == 2);
        Check("DU1: one sequence held by two unmatched rows and one record is ambiguous",
            CaseOf("tr|Q10012|Q10012_SYN") is { Step: DatabaseUpdateStep.SameSequence } same && same.OldIndices.Count == 2 && same.NewIndices.Count == 1);
        Check("DU1: one new record containing two old rows is ambiguous",
            CaseOf("tr|Q10014|Q10014_SYN") is { Step: DatabaseUpdateStep.Containment } both && both.OldIndices.Count == 2
            && both.NewIndices.SequenceEqual(new[] { @new["tr|Q20014|Q20014_SYN"] }));
        Check("DU1: an old row contained in two new records is ambiguous with the whole linked set",
            CaseOf("tr|Q10016|Q10016_SYN") is { Step: DatabaseUpdateStep.Containment } linked && linked.OldIndices.Count == 2 && linked.NewIndices.Count == 2);
        Check("DU1: nothing of an ambiguous case is matched; its rows leave and its records arrive unless picked",
            cases.All(a => a.OldIndices.All(i => comparison.Matches.All(m => m.OldIndex != i) && comparison.Removed.Contains(i))
                && a.NewIndices.All(j => comparison.Matches.All(m => m.NewIndex != j) && comparison.Added.Contains(j)))
            && comparison.Rows.Count(r => r.Kind == DatabaseUpdateKind.Ambiguous) == 8 + 6);
        Check("DU1: target counts by class", comparison.Counts is { Unchanged: 4, HeaderChanged: 1, SequenceChanged: 1, NewAccessionSameSequence: 1,
            ReplacedByLonger: 1, Removed: 1, Added: 2, AmbiguousCases: 4, AmbiguousOldRows: 8, AmbiguousNewRecords: 6 },
            JsonSerializer.Serialize(comparison.Counts));
        Check("DU1: one to one: every row and every record is used exactly once", OneToOne(comparison, project.MyExtendedFastaItems.Count, source.Records.Count));
        Check("DU1: rows describe the old family and every protein that is not unchanged",
            comparison.Rows.All(r => r.Kind != DatabaseUpdateKind.Unchanged)
            && comparison.Rows.Single(r => r.OldIndex == old["sp|P10003|CCC_SYN"]) is { FamilyUserId: 3, FamilySearchId: 3, NewIdentifier: "sp|P10003|CCC_SYN" }
            && comparison.Rows.Single(r => r.OldIndex == old["sp|P10003|CCC_SYN"]).FamilyName.StartsWith("Gamma", StringComparison.Ordinal));

        // Picks follow the outcome of the step that found the case (FIXED Z14c1 3b7d835).
        var picks = new[]
        {
            new DatabaseUpdatePick(1, old["tr|Q10011|A_SYN"], @new["tr|Q10011|A_SYN"]),
            new DatabaseUpdatePick(2, old["tr|Q10012|Q10012_SYN"], @new["tr|Q20012|Q20012_SYN"]),
            new DatabaseUpdatePick(3, old["tr|Q10014|Q10014_SYN"], @new["tr|Q20014|Q20014_SYN"]),
        };
        var picked = DatabaseUpdateComparer.Compare(project, source, picks);
        DatabaseUpdateMatch? Picked(string identifier) => picked.Matches.FirstOrDefault(m => m.OldIndex == old[identifier]);
        Check("DU1: a repeated-key pick takes the step 1 outcome (here a sequence change) and the rest of the case leaves and arrives",
            Picked("tr|Q10011|A_SYN") is { Kind: DatabaseUpdateKind.SequenceChanged, Picked: true, Step: DatabaseUpdateStep.RepeatedKey }
            && picked.Removed.Contains(old["sp|Q10011|B_SYN"]) && picked.Added.Contains(@new["sp|Q10011|B_SYN"])
            && picked.Rows.Any(r => r.OldIndex == old["sp|Q10011|B_SYN"] && r.Kind == DatabaseUpdateKind.Removed && r.AmbiguousCase == 1));
        Check("DU1: a same-sequence pick is a header change of step 2",
            Picked("tr|Q10012|Q10012_SYN") is { Kind: DatabaseUpdateKind.NewAccessionSameSequence, Picked: true } && picked.Removed.Contains(old["tr|Q10013|Q10013_SYN"]));
        Check("DU1: a containment pick gives the record the chosen row's place; the other row leaves",
            Picked("tr|Q10014|Q10014_SYN") is { Kind: DatabaseUpdateKind.ReplacedByLonger, Picked: true } && picked.Removed.Contains(old["tr|Q10015|Q10015_SYN"]));
        Check("DU1: picks keep the case numbers and leave one ambiguous case", picked.Ambiguities.Count == 4 && picked.Counts.AmbiguousCases == 1
            && picked.Picks.Count == 3 && OneToOne(picked, project.MyExtendedFastaItems.Count, source.Records.Count));
        Check("DU1: a pick outside its case is refused", Throws<ArgumentException>(() =>
            DatabaseUpdateComparer.Compare(project, source, new[] { new DatabaseUpdatePick(1, old["tr|Q10012|Q10012_SYN"], @new["tr|Q10011|A_SYN"]) })));
        Check("DU1: two picks in one case are refused", Throws<ArgumentException>(() =>
            DatabaseUpdateComparer.Compare(project, source, new[] { picks[0], new DatabaseUpdatePick(1, old["sp|Q10011|B_SYN"], @new["sp|Q10011|B_SYN"]) })));
        Check("DU1: a containment pick whose record does not contain the row is refused", Throws<ArgumentException>(() =>
            DatabaseUpdateComparer.Compare(project, source, new[] { new DatabaseUpdatePick(4, old["tr|Q10017|Q10017_SYN"], @new["tr|Q20016|Q20016_SYN"]) })));

        // Refusals and warnings.
        var noDecoys = DatabaseUpdateSource.FromEntries("n.fasta", "N", source.Records.Where(r => !r.Identifier.StartsWith("Reverse_", StringComparison.Ordinal)).ToList());
        Check("DU1: decoys in the project and none in the new file are refused, naming the decoys",
            Throws<InvalidDataException>(() => DatabaseUpdateComparer.Compare(project, noDecoys), message => decoyMessage = message) && decoyMessage.Contains("decoy", StringComparison.Ordinal));
        var noContaminants = DatabaseUpdateSource.FromEntries("n.fasta", "N", source.Records.Where(r => !r.Identifier.StartsWith("contaminant_", StringComparison.Ordinal)).ToList());
        Check("DU1: contaminants in the project and none in the new file are refused", Throws<InvalidDataException>(() => DatabaseUpdateComparer.Compare(project, noContaminants)));
        Check("DU1: a repeated identifier in the new file is refused", Throws<InvalidDataException>(() => DatabaseUpdateSource.FromEntries("n.fasta", "N",
            new[] { new DatabaseUpdateEntry("tr|Q1|X", "", "MK"), new DatabaseUpdateEntry("tr|Q1|X", "", "MKK") })));
        var other = DatabaseUpdateSource.FromEntries("other.fasta", "O", new[]
        {
            new DatabaseUpdateEntry("tr|Q90001|Q90001_SYN", "Other OS=Synthetica tres OX=3", UpdateSequence(91, 80)),
            new DatabaseUpdateEntry("contaminant_AAAA1|Anti-HA_AffinityTag", "", "YPYDVPDYA"),
            new DatabaseUpdateEntry("Reverse_tr|Q90001|Q90001_SYN", "", UpdateSequence(92, 80)),
        });
        Check("DU1: a file sharing under half of the target rows warns that it looks like another database",
            DatabaseUpdateComparer.Compare(project, other).Warnings.Any(w => w.Contains("different database", StringComparison.Ordinal)));
        Check("DU1: the comparison changes nothing in the project", DatabaseUpdateComparer.ProjectFingerprint(project) == comparison.ProjectFingerprint
            && project.MyExtendedFastaItems.Count == 19 && project.DatabaseUpdates == null);

        // Test 9: repeat.
        string Serialized(DatabaseUpdateComparison value) => JsonSerializer.Serialize(new
        {
            value.ProjectFingerprint, value.Matches, value.Removed, value.Added, value.Ambiguities, value.Picks, value.Rows, value.Counts, value.DecoyCounts, value.Warnings,
        });
        Check("DU9: the same two inputs give byte-identical comparisons", Serialized(comparison) == Serialized(DatabaseUpdateComparer.Compare(DatabaseUpdateFixture().Project, DatabaseUpdateFixture().Source))
            && Serialized(picked) == Serialized(DatabaseUpdateComparer.Compare(project, source, picks.Reverse().ToArray())));

        // Reading a file the way Build families reads it.
        string directory = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "database-update");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "new-database.fasta");
        File.WriteAllText(path, ">sp|P10001|AAA_SYN Alpha one OS=Synthetica una OX=1\nACDEF\nGHIK\n>tr|Q10002|Q10002_SYN Beta\nMMMM\n>tr|Q10003|EMPTY_SYN Empty\n>Reverse_sp|P10001|AAA_SYN\nKIHGFEDCA\n");
        var read = DatabaseUpdateSource.Read(path);
        Check("DU1: the reader splits the header at the first space and joins sequence lines",
            read.Records.Count == 3 && read.Records[0] == new DatabaseUpdateEntry("sp|P10001|AAA_SYN", "Alpha one OS=Synthetica una OX=1", "ACDEFGHIK")
            && read.EmptyRecords == 1 && read.Summary.SequenceCount == 4 && read.Summary.FileName == "new-database.fasta"
            && read.Summary.Sha256 == FileFingerprint.Sha256(path), JsonSerializer.Serialize(read.Records));

        var withNoUpdates = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended> { UpdateRow("sp|P1|A", "", "MK", 1) } };
        Check("DU1: a project never updated writes no DatabaseUpdates field", !withNoUpdates.ToJsonText().Contains("DatabaseUpdates", StringComparison.Ordinal));
        withNoUpdates.DatabaseUpdates = new List<DatabaseUpdateRecord> { new() { ProgramVersion = "test", Rows = comparison.Rows.ToList(), Counts = comparison.Counts } };
        string text = withNoUpdates.ToJsonText();
        Check("DU1: an update record round-trips through the family file", FamilyClusterFile.FromJsonText(text).ToJsonText() == text
            && FamilyClusterFile.FromJsonText(text).DatabaseUpdates![0].Rows.Count == comparison.Rows.Count);
    }

    private static string decoyMessage = string.Empty;

    private static bool OneToOne(DatabaseUpdateComparison comparison, int rows, int records)
    {
        var oldUses = comparison.Matches.Select(m => m.OldIndex).Concat(comparison.Removed).ToList();
        var newUses = comparison.Matches.Select(m => m.NewIndex).Concat(comparison.Added).ToList();
        return oldUses.Count == rows && oldUses.Distinct().Count() == rows && newUses.Count == records && newUses.Distinct().Count() == records;
    }
}
