using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DatabaseEntryCountCommands() =>
    [new("--test-database-entry-counts", 10510, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-database-entry-counts", (args, _) =>
        {
            DatabaseEntryCountTests(args.Length > 1 ? args[1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static FamilyClusterFile DatabaseEntryFixture(bool conflict = false)
    {
        var file = new FamilyClusterFile
        {
            FamilyPanel = new FamilyPanel { Families = new()
            {
                new() { Key = "main", DisplayName = "Combined family" },
                new() { Key = "alias", DisplayName = "Linked family", Decision = FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey = "main" },
                new() { Key = "other", DisplayName = "Other family" },
                new() { Key = "zero", DisplayName = "Zero family" },
                new() { Key = "empty", DisplayName = "Filtered family" },
            } },
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "ENTRY_A", Sequence = "AAAA", UserID = 11 },
                new() { SequenceIdentifier = "ENTRY_B", Sequence = "AAAA", UserID = 11 },
                new() { SequenceIdentifier = "ENTRY_C", Sequence = "CCCC", UserID = 22 },
                new() { SequenceIdentifier = "Reverse_ENTRY_D", Sequence = "DDDD", UserID = 11 },
                new() { SequenceIdentifier = "contaminant_ENTRY_E", Sequence = "EEEE", UserID = 22 },
                new() { SequenceIdentifier = "Reverse_contaminant_ENTRY_F", Sequence = "FFFF", UserID = 22 },
                new() { SequenceIdentifier = "Contaminant_CASE_TARGET", Sequence = "GGGG", UserID = 22 },
                new() { SequenceIdentifier = "ENTRY_OUTSIDE", Sequence = "HHHH", UserID = 99 },
                new() { SequenceIdentifier = "ZERO_FAMILY", Sequence = "IIII", UserID = 0 },
                new() { SequenceIdentifier = "contaminant_ONLY", Sequence = "KKKK", UserID = 44 },
            },
        };
        file.PanelProteinAssignments = file.MyExtendedFastaItems.Select((p, i) => (p, i))
            .Where(pair => !SequenceLabels.IsDecoy(pair.p.SequenceIdentifier)).Select(pair =>
            {
                string? key = pair.p.UserID switch { 11 => "main", 22 => conflict && pair.p.SequenceIdentifier == "ENTRY_C" ? "other" : "alias", 0 => "zero", 44 => "empty", _ => null };
                return new PanelProteinAssignment { ProteinIndex = pair.i, Identifier = pair.p.SequenceIdentifier,
                    FamilyUserId = pair.p.UserID, PanelFamilyKey = key, IsApplied = key != null };
            }).ToList();
        MakePanelAssignmentsCurrent(file, new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } });
        return file;
    }

    private static void DatabaseEntryCountTests(string? output = null)
    {
        Section("G1b stored database-entry population KnownAnswers");
        var file = DatabaseEntryFixture();
        string before = file.ToJsonText();
        var studies = new QuantManifest { FamilyRunShares = new() { Families = new() { new() { FamilyUserId = 11, MeanShare = .25 } } } };
        string savedStudies = JsonSerializer.Serialize(studies);
        var view = FamilyDatabaseEntryCounts.Read(file);
        Check("database entries are stored records, not distinct sequences or peptide estimates",
            view.ByUserId.GetValueOrDefault(11) == 2 && view.ByUserId.GetValueOrDefault(22) == 2);
        Check("D15 identifier filters exclude decoys and contaminants with exact prefix semantics",
            view.ByUserId.GetValueOrDefault(44) == 0 && view.ByUserId.ContainsKey(44)
            && view.ByUserId.GetValueOrDefault(22) == 2);
        Check("a clean effective panel key unions two source families once", view.PanelMappingCurrent
            && view.ByPanelKey.TryGetValue("main", out var count) && count == 4 && !view.ByPanelKey.ContainsKey("alias"));
        Check("an unlinked entry stays outside the effective panel union", view.ByUserId.GetValueOrDefault(99) == 1
            && view.ByPanelKey.GetValueOrDefault("main") == 4);
        Check("UserID zero is a real stored family", view.ByUserId.TryGetValue(0, out var zeroFamily) && zeroFamily == 1
            && view.ByPanelKey.GetValueOrDefault("zero") == 1);
        Check("a family with only filtered entries has an available zero", view.ByPanelKey.TryGetValue("empty", out var empty) && empty == 0);
        var conflicted = FamilyDatabaseEntryCounts.Read(DatabaseEntryFixture(true));
        Check("a conflicting panel key is unavailable rather than a partial union or zero",
            conflicted.ByPanelKey.TryGetValue("main", out var main) && main == null
            && conflicted.ByPanelKey.TryGetValue("other", out var other) && other == null);
        var raw = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "EXPLICIT_ZERO", Sequence = "AAAA", UserID = 0 } } };
        var unassigned = JsonSerializer.Deserialize<FastaItemExtended>("{\"SequenceIdentifier\":\"UNASSIGNED\",\"Sequence\":\"LLLL\"}")!;
        raw.MyExtendedFastaItems.Add(unassigned);
        var rawCounts = FamilyDatabaseEntryCounts.Read(raw);
        Check("a missing UserID is unassigned and never silently joins family zero", rawCounts.ByUserId.GetValueOrDefault(0) == 1);
        using var missingJson = JsonDocument.Parse(JsonSerializer.Serialize(unassigned, FamilyClusterFile.CurationJsonOptions));
        Check("roundtrip keeps an unassigned entry without a UserID", !missingJson.RootElement.TryGetProperty("UserID", out _));
        Check("reading counts changes neither stored curation nor existing studies", before == file.ToJsonText() && savedStudies == JsonSerializer.Serialize(studies));
        Check("count dictionaries cannot be modified through mutable interfaces", Throws<NotSupportedException>(() =>
            ((IDictionary<int, int>)view.ByUserId).Add(100, 1)));
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "assigned-curation.json"), before);
            File.WriteAllText(Path.Combine(output, "stored-studies.json"), savedStudies);
        }
    }
}
