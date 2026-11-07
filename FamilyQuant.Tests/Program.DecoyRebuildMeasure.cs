using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DecoyRebuildMeasureCommands() =>
    [new("--measure-decoy-rebuild", 10600, TestCommandPhase.Raw,
        args => args.Length == 2 && args[0] == "--measure-decoy-rebuild", (args, _) => MeasureDecoyRebuild(args[1]))];

    private sealed record DecoyFieldComparison(string Field, bool Equal, string StoredSha256, string RebuiltSha256);
    private sealed record DecoyRebuildComparison(string Fixture, int Row, string Identifier, int TargetCount,
        bool IdentifierEqual, bool SequenceEqual, bool FreshRowEqual, bool TargetCopyEqual,
        List<DecoyFieldComparison> FreshFields, List<DecoyFieldComparison> CopiedFields);

    private static int MeasureDecoyRebuild(string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Choose a fresh decoy measurement output folder.");
        Directory.CreateDirectory(output);
        Section("Z15b5 exact decoy reconstruction on repository synthetic curation fixtures");
        var fixtures = new (string Name, FamilyClusterFile File)[]
        {
            ("database-update", DatabaseUpdateFixture().Project),
            ("database-update-apply", DatabaseUpdateApplyFixture().Project),
            ("evidence-matching", EvidenceMatchingFile()),
            ("panel-checklist", ChecklistFile()),
        };
        var comparisons = new List<DecoyRebuildComparison>();
        foreach (var fixture in fixtures)
        {
            string path = Path.Combine(output, fixture.Name + ".fcf");
            File.WriteAllText(path, fixture.File.ToJsonText(), new UTF8Encoding(false));
            var stored = FamilyClusterFile.Load(path);
            var targets = stored.MyExtendedFastaItems.Where(row => !SequenceLabels.IsDecoy(row.SequenceIdentifier))
                .ToLookup(row => row.SequenceIdentifier, StringComparer.Ordinal);
            int decoys = 0;
            for (int index = 0; index < stored.MyExtendedFastaItems.Count; index++)
            {
                var decoy = stored.MyExtendedFastaItems[index];
                if (!SequenceLabels.IsDecoy(decoy.SequenceIdentifier)) continue;
                decoys++;
                var source = targets[decoy.SequenceIdentifier[SequenceLabels.DecoyPrefix.Length..]].ToArray();
                if (source.Length != 1)
                {
                    comparisons.Add(new(fixture.Name, index, decoy.SequenceIdentifier, source.Length,
                        false, false, false, false, CompareDecoyFields(decoy, null), CompareDecoyFields(decoy, null)));
                    continue;
                }
                var target = source[0];
                string identifier = SequenceLabels.DecoyPrefix + target.SequenceIdentifier;
                string sequence = new(target.Sequence.Reverse().ToArray());
                // The in-product FamilyWorld builder reconstructs only identifier/sequence tuples.
                // Test both possible metadata policies without treating either as a curation rule.
                var fresh = new FastaItemExtended { SequenceIdentifier = identifier, Sequence = sequence, Description = "" };
                var copied = JsonSerializer.Deserialize<FastaItemExtended>(
                    JsonSerializer.Serialize(target, FamilyClusterFile.CurationJsonOptions), FamilyClusterFile.CurationJsonOptions)!;
                copied.SequenceIdentifier = identifier; copied.Sequence = sequence; copied.Description = "";
                comparisons.Add(new(fixture.Name, index, decoy.SequenceIdentifier, 1,
                    decoy.SequenceIdentifier == identifier, decoy.Sequence == sequence,
                    DecoyStoredBytes(decoy).SequenceEqual(DecoyStoredBytes(fresh)),
                    DecoyStoredBytes(decoy).SequenceEqual(DecoyStoredBytes(copied)),
                    CompareDecoyFields(decoy, fresh), CompareDecoyFields(decoy, copied)));
            }
            Check("Z15b5 every stored decoy was measured in " + fixture.Name,
                comparisons.Count(row => row.Fixture == fixture.Name) == decoys);
        }
        var positive = new FastaItemExtended { SequenceIdentifier = SequenceLabels.DecoyPrefix + "synthetic-control", Sequence = "GFEDCA", Description = "" };
        var same = new FastaItemExtended { SequenceIdentifier = SequenceLabels.DecoyPrefix + "synthetic-control", Sequence = "GFEDCA", Description = "" };
        Check("Z15b5 exact serialized field comparison accepts an identical row", CompareDecoyFields(positive, same).All(field => field.Equal)
            && DecoyStoredBytes(positive).SequenceEqual(DecoyStoredBytes(same)));
        same.UserAnnotation = "Independent stored annotation";
        Check("Z15b5 exact serialized field comparison detects independent metadata", CompareDecoyFields(positive, same)
            .Any(field => field.Field == nameof(FastaItemExtended.UserAnnotation) && !field.Equal));
        Check("Z15b5 the measured existing corpus includes a reversed pair and an absent target",
            comparisons.Any(row => row.TargetCount == 1 && row.IdentifierEqual && row.SequenceEqual)
            && comparisons.Any(row => row.TargetCount == 0));
        Check("Z15b5 existing stored family IDs cannot be recovered by either metadata policy",
            comparisons.Any(row => row.SequenceEqual && row.CopiedFields.Any(field => field.Field == nameof(FastaItemExtended.UserID) && !field.Equal)));

        bool allExact = comparisons.Count > 0
            && (comparisons.All(row => row.FreshRowEqual) || comparisons.All(row => row.TargetCopyEqual));
        var report = new
        {
            RuleSources = new[] { "FamilyQuant.Core/SequenceLabels.cs:7-11", "FamilyQuant.Core/Homology/HomologyOptions.cs:50",
                "FamilyQuant.Core/Quantitation/FamilyWorldPass.cs:241-249", "FamilyQuant.Core/USearchTools.cs:761-784",
                "FamilyQuant.Tests/Program.Database.cs:319-349" },
            RuleScope = "FamilyWorld builds prefixed identifier/reversed sequence tuples. Imported curation rows retain independent fields from the database and clustering.",
            Corpus = fixtures.Select(fixture => fixture.Name).ToArray(),
            Decoys = comparisons.Count,
            MatchedTargets = comparisons.Count(row => row.TargetCount == 1),
            MissingTargets = comparisons.Count(row => row.TargetCount == 0),
            IdentifierAndSequenceExact = comparisons.Count(row => row.IdentifierEqual && row.SequenceEqual),
            FreshRowsExact = comparisons.Count(row => row.FreshRowEqual),
            TargetCopiesExact = comparisons.Count(row => row.TargetCopyEqual),
            AllFieldsRebuildExactly = allExact,
            Decision = allExact ? "A bounded reconstruction policy passed; review is required before format changes."
                : "Keep decoys stored. The supported fixtures include independent curation fields or a missing target.",
            Comparisons = comparisons,
        };
        File.WriteAllText(Path.Combine(output, "measurement.json"), JsonSerializer.Serialize(report, FamilyClusterFile.CurationJsonOptions), new UTF8Encoding(false));
        Console.WriteLine("Measured " + report.Decoys + " decoys across " + fixtures.Length + " synthetic curation fixtures.");
        Console.WriteLine("Exact identifier and sequence pairs: " + report.IdentifierAndSequenceExact
            + "; exact fresh rows: " + report.FreshRowsExact + "; exact target copies: " + report.TargetCopiesExact + "; missing targets: " + report.MissingTargets + ".");
        Console.WriteLine(report.Decision);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static byte[] DecoyStoredBytes(FastaItemExtended row) =>
        JsonSerializer.SerializeToUtf8Bytes(row, FamilyClusterFile.CurationJsonOptions);

    private static List<DecoyFieldComparison> CompareDecoyFields(FastaItemExtended stored, FastaItemExtended? rebuilt)
    {
        using var left = JsonDocument.Parse(DecoyStoredBytes(stored));
        using var right = JsonDocument.Parse(rebuilt == null ? Encoding.UTF8.GetBytes("{}") : DecoyStoredBytes(rebuilt));
        var leftFields = left.RootElement.EnumerateObject().ToDictionary(field => field.Name, field => field.Value.GetRawText(), StringComparer.Ordinal);
        var rightFields = right.RootElement.EnumerateObject().ToDictionary(field => field.Name, field => field.Value.GetRawText(), StringComparer.Ordinal);
        string Hash(string? text) => text == null ? "MISSING" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return leftFields.Keys.Union(rightFields.Keys).OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new DecoyFieldComparison(name, leftFields.GetValueOrDefault(name) == rightFields.GetValueOrDefault(name),
                Hash(leftFields.GetValueOrDefault(name)), Hash(rightFields.GetValueOrDefault(name)))).ToList();
    }
}
