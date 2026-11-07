using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HandChoiceRecordCommands() =>
    [
        new("--test-hand-choice-record", 100, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-hand-choice-record", (_, _) =>
            {
                HandChoiceRecordTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    /// <summary>Ten complete reviewed members of an invented family with four cysteines each, and two unreviewed
    /// proteins without family evidence: one with twelve cysteines, one shaped like the members.</summary>
    private static FamilyClusterFile HandChoiceFile()
    {
        var random = new Random(4242);
        string Body(int cysteines)
        {
            const string other = "ADEFGHIKLMNPQRSTVWY";
            var residues = Enumerable.Range(0, 100).Select(_ => other[random.Next(other.Length)]).ToArray();
            foreach (int position in Enumerable.Range(0, 100).OrderBy(_ => random.Next()).Take(cysteines)) residues[position] = 'C';
            return new string(residues);
        }
        var rows = new List<FastaItemExtended>();
        for (int n = 0; n < 10; n++)
            rows.Add(new FastaItemExtended
            {
                SequenceIdentifier = "sp|P7" + n.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + "|INV" + n + "_SYN",
                Description = "Invented member " + n + " OS=Synthetica una OX=21", Sequence = Body(4), UserID = n + 1, USearchID = n + 1,
                Evidence = new ProteinEvidence { Family = "Invented family", Reviewed = true, Fragment = false, TransmembraneCount = 0,
                    Keywords = new List<string> { "scope" } },
            });
        rows.Add(new FastaItemExtended { SequenceIdentifier = "tr|Q79001|Q79001_SYN", Description = "Odd relative OS=Synthetica una OX=21",
            Sequence = Body(12), UserID = 11, USearchID = 11, Evidence = new ProteinEvidence { Reviewed = false, Fragment = false, TransmembraneCount = 0 } });
        rows.Add(new FastaItemExtended { SequenceIdentifier = "tr|Q79002|Q79002_SYN", Description = "Plain relative OS=Synthetica una OX=21",
            Sequence = Body(4), UserID = 12, USearchID = 12, Evidence = new ProteinEvidence { Reviewed = false, Fragment = false, TransmembraneCount = 0 } });
        return new FamilyClusterFile { SourceSha256 = "invented-hand-record", SourceSequenceCount = rows.Count, MyExtendedFastaItems = rows };
    }

    private static void HandChoiceRecordTests()
    {
        Section("Hand-added families: the warnings at the choice are recorded and reported (Z12k5a)");
        var file = HandChoiceFile();
        var session = new EvidenceGroupingSession(file, "hand record fixture");
        session.Propose("scope");
        var review = session.AddToFamily(new EvidenceManualRequest(new[] { 10, 11 }, "Invented family", "Invented family", false, "Curator's choice"));
        var oddRow = session.Proposal!.Proteins.Single(p => p.ProteinIndex == 10);
        Check("Z12k5a: the review shows a coherence warning on the odd hand-added protein and none on the plain one",
            oddRow.Tier == EvidenceTier.AddedByHand && oddRow.Flags.Any(f => f.Code.StartsWith("coherence.cysteines", StringComparison.Ordinal))
            && session.Proposal.Proteins.Single(p => p.ProteinIndex == 11).Flags.Count == 0,
            JsonSerializer.Serialize(oddRow.Flags));
        var result = session.Apply(new EvidenceReviewViewModel(review).CaptureSelection());
        var odd = result.Recipe.Assignments.Single(a => a.ProteinIndex == 10);
        var plain = result.Recipe.Assignments.Single(a => a.ProteinIndex == 11);
        Check("Z12k5a: the hand assignment stores exactly the warnings the review showed",
            odd.Flags != null && JsonSerializer.Serialize(odd.Flags) == JsonSerializer.Serialize(oddRow.Flags)
            && plain.Flags is { Count: 0 }, JsonSerializer.Serialize(odd.Flags));
        Check("Z12k5a: automatic assignments carry no warnings field, so their recipe bytes are as before",
            result.Recipe.Assignments.Where(a => a.Tier != EvidenceTier.AddedByHand).All(a => a.Flags == null)
            && !JsonSerializer.Serialize(result.Recipe.Assignments.First(a => a.Tier != EvidenceTier.AddedByHand)).Contains("\"Flags\"", StringComparison.Ordinal));
        var table = FamilyQuant.Report.ReportMethods.Build(file, new QuantManifest()).Blocks.OfType<FamilyQuant.Report.ReportTable>()
            .Single(t => t.Id == "methods-hand-families");
        string[] Cells(string identifier) => table.Rows.Single(r => r[0] == identifier).ToArray();
        Check("Z12k5a: Methods names the warnings at the choice for each hand-added protein",
            table.Columns.SequenceEqual(new[] { "Protein", "Chosen family", "User note", "Status", "Warnings at the choice" })
            && Cells("tr|Q79001|Q79001_SYN")[4] == string.Join(" | ", oddRow.Flags.Select(f => f.Message)) && Cells("tr|Q79001|Q79001_SYN")[4].Length > 0
            && Cells("tr|Q79002|Q79002_SYN")[4] == "None" && table.Rows.All(r => r[3] == "Applied"), string.Join(" || ", table.Rows.Select(r => string.Join(" ; ", r))));
        var reopened = FamilyClusterFile.FromJsonText(file.ToJsonText());
        Check("Z12k5a: the warnings survive save and reopen", JsonSerializer.Serialize(reopened.EvidenceRecipe!.Assignments.Single(a => a.ProteinIndex == 10).Flags)
            == JsonSerializer.Serialize(odd.Flags));

        // A recipe saved before the field existed says the warnings were not recorded and still replays.
        var node = JsonNode.Parse(file.ToJsonText())!.AsObject();
        foreach (var assignment in node["EvidenceRecipe"]!["Assignments"]!.AsArray()) assignment!.AsObject().Remove("Flags");
        var older = FamilyClusterFile.FromJsonText(node.ToJsonString());
        var olderTable = FamilyQuant.Report.ReportMethods.Build(older, new QuantManifest()).Blocks.OfType<FamilyQuant.Report.ReportTable>()
            .Single(t => t.Id == "methods-hand-families");
        Check("Z12k5a: a hand assignment saved without the field reads Not recorded", olderTable.Rows.All(r => r[4] == "Not recorded")
            && older.EvidenceRecipe!.Assignments.All(a => a.Flags == null));
        var recipe = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(result.Recipe))!;
        string applied = file.ToJsonText();
        result.Undo!.Restore(file);
        EvidenceApply.ApplyRecipe(file, recipe);
        Check("Z12k5a: Undo then replaying the saved recipe gives the same file, warnings included", file.ToJsonText() == applied);
    }
}
