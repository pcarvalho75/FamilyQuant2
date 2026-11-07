using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelEvidenceMatchingCommandTable() =>
    [
        new("--test-panel-evidence-matching", 74, TestCommandPhase.Raw, args => args.Contains("--test-panel-evidence-matching", StringComparer.Ordinal), (_, _) =>
        {
            PanelEvidenceMatchingTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static FamilyClusterFile EvidenceMatchingFile()
    {
        ProteinEvidence Evidence(bool reviewed) => new()
        {
            Reviewed = reviewed, Family = "Stored group r", Keywords = new() { "Action alpha", "Action alpha" },
            TissueSpecificity = new() { "Supporting\nregion" }, Pfam = new() { new() { Accession = "PF-invented", Name = "Common fold" } },
        };
        var reference = Evidence(true); reference.Pfam[0].Name = "Catalytic violet"; reference.Keywords.Add("Reference only phrase");
        return new FamilyClusterFile
        {
            FamilyPanel = new() { Name = "Supplied panel", Families = new() { new() { Key = "stable-key", DisplayName = "Nome fornecido",
                Names = new() { new() { Name = "Actual other header", Text = "Catalytic violet" } } } } },
            EvidenceSource = new() { ReferencePanel = new() { Entries = new()
                { new() { Accession = "P11001", Evidence = reference }, new() { Accession = "P11001", Evidence = reference } } } },
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "sp|P10001|first", Description = "Unrelated first", Sequence = "ACDEFGHIK", UserID = 1, USearchID = 1, Evidence = Evidence(true) },
                new() { SequenceIdentifier = "tr|P10002|second", Description = "Unrelated second", Sequence = "ACDEFGHIL", UserID = 2, USearchID = 2, Evidence = Evidence(false) },
                new() { SequenceIdentifier = "contaminant_sp|P10003|excluded", Sequence = "ACDEFGHIK", Evidence = Evidence(true) },
                new() { SequenceIdentifier = "Reverse_sp|P10004|excluded", Sequence = "ACDEFGHIK", Evidence = Evidence(true) },
            },
        };
    }

    private static void PanelEvidenceMatchingTests()
    {
        var file = EvidenceMatchingFile(); string before = file.ToJsonText();
        var snapshot = new PanelEvidenceMatching(file);
        var names = file.FamilyPanel!.Families[0].Names.Select(name => name.Text).Prepend(file.FamilyPanel.Families[0].DisplayName).ToArray();
        var suggestions = snapshot.Suggest(names);
        var family = suggestions.Single(row => row.Kind == PanelEvidenceMatchKind.UniProtFamily);
        Check("Z12p4 stored reference names suggest co-carried source families without a synonym table",
            family is { Value: "Stored group r", DatabaseTargets: 2, ReviewedTargets: 1, ReferenceEntries: 1 }
            && family.Reason.StartsWith("Co-occurs", StringComparison.Ordinal));
        Check("Z12p4 Pfam name suggestion reports exact target and deduplicated reference coverage",
            suggestions.Single(row => row.Kind == PanelEvidenceMatchKind.Pfam) is { Value: "PF-invented", Name: "Catalytic violet", DatabaseTargets: 2, ReviewedTargets: 1, ReferenceEntries: 1 });
        Check("Z12p4 duplicate labels, contaminants and decoys cannot inflate target counts",
            suggestions.Single(row => row.Kind == PanelEvidenceMatchKind.Keyword && row.Value == "Action alpha").DatabaseTargets == 2);
        Check("Z12p4 reference-only labels have explicit zero database target coverage",
            suggestions.Single(row => row.Value == "Reference only phrase") is { DatabaseTargets: 0, ReferenceEntries: 1 });
        Check("Z12p4 every stable suggestion key decodes its kind and value", suggestions.All(row => PanelEvidenceMatching.ChoiceFromKey(row.Key) == new PanelEvidenceChoice(row.Kind, row.Value)));
        Check("Z12p4 suggestions are detached and never write confirmation records", file.ToJsonText() == before && file.FamilyPanel.ConfirmedEvidenceMatches == null);
        Check("Z12p4 edited unknown values have zero exact stored-label coverage", snapshot.Coverage(PanelEvidenceMatchKind.Keyword, "Not a supplied label").DatabaseTargets == 0);
        Check("Z12p4 tissue whitespace is normalized consistently for coverage and matching",
            snapshot.Coverage(PanelEvidenceMatchKind.TissuePhrase, " Supporting region ").DatabaseTargets == 2
            && PanelEvidenceMatching.Matches(file.MyExtendedFastaItems[0].Evidence, new(PanelEvidenceMatchKind.TissuePhrase, "Supporting region")));
        bool refused = false;
        try { PanelEvidenceMatching.WithConfirmed(file.FamilyPanel, "stable-key", new()); } catch (ArgumentException) { refused = true; }
        Check("Z12p4 unconfirmed records are refused before entering the panel", refused && file.ToJsonText() == before);
        var choices = new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.UniProtFamily, family.Value), new(PanelEvidenceMatchKind.Keyword, "Action alpha") };
        var confirmed = PanelEvidenceMatching.Confirm(names, choices);
        var detached = PanelEvidenceMatching.WithConfirmed(file.FamilyPanel, "stable-key", confirmed);
        Check("Z12p4 confirmation stays keyed by the stable abbreviation and preserves source names",
            detached.ConfirmedEvidenceMatches!["stable-key"].NamesUsed.SequenceEqual(names) && detached.Families[0].Names[0].Name == "Actual other header"
            && file.ToJsonText() == before);
        refused = false;
        try { PanelEvidenceMatching.WithConfirmed(detached, "unknown-key", confirmed); } catch (ArgumentException) { refused = true; }
        Check("Z12p4 confirmation refuses a missing family key", refused);
        var ordinary = PanelEvidenceClassifier.Propose(file, new() { ScopeTerms = new[] { "Action alpha" } });
        file.FamilyPanel = detached;
        var mapped = PanelEvidenceClassifier.Propose(file, new() { ScopeTerms = new[] { "Action alpha" } });
        Check("Z12p4 confirmed matching metadata never changes the automatic proposal", JsonSerializer.Serialize(ordinary) == JsonSerializer.Serialize(mapped));
        var checklist = PanelFamilyChecklist.Build(file, mapped).Single();
        Check("Z12p4 checklist consumes confirmed matches as candidates with separate contaminant counts",
            checklist is { Matches: 2, Reviewed: 1, Placed: 0, Outside: 2, ExcludedContaminants: 1 }
            && checklist.Proteins.All(row => row.Match.HasFlag(PanelChecklistMatch.ConfirmedMatch)));
        Check("Z12p4 confirmed records survive project round trip", FamilyClusterFile.FromJsonText(file.ToJsonText()).ToJsonText() == file.ToJsonText());
        Check("Z12p4 legacy panels omit new metadata", !JsonSerializer.Serialize(new FamilyPanel()).Contains("ConfirmedEvidenceMatches", StringComparison.Ordinal)
            && !JsonSerializer.Serialize(new EvidenceRecipe()).Contains("SourceConfirmedMatchesFingerprint", StringComparison.Ordinal));
        file.MyExtendedFastaItems[0].Evidence!.Keywords.Add("Changed after snapshot");
        Check("Z12p4 snapshot remains detached and detects evidence changes", !snapshot.IsCurrent(file)
            && snapshot.Coverage(PanelEvidenceMatchKind.Keyword, "Changed after snapshot").DatabaseTargets == 0);
        var partial = EvidenceMatchingFile(); partial.EvidenceSource!.ReferencePanel!.MissingFamilies.Add("Unfetched supplied group");
        var partialSnapshot = new PanelEvidenceMatching(partial);
        Check("Z12p4 incomplete reference metadata stays explicit while available stored-label counts remain bounded",
            partialSnapshot.ReferencePanelComplete == false && partialSnapshot.Coverage(PanelEvidenceMatchKind.Pfam, "PF-invented").ReferenceEntries == 1);
        var reordered = EvidenceMatchingFile(); reordered.MyExtendedFastaItems.Reverse();
        Check("Z12p4 suggestion keys, order and counts are independent of database entry order",
            JsonSerializer.Serialize(new PanelEvidenceMatching(reordered).Suggest(names)) == JsonSerializer.Serialize(suggestions));
        var empty = new FamilyClusterFile { FamilyPanel = FamilyPanelValidation.Clone(EvidenceMatchingFile().FamilyPanel!) };
        var emptySnapshot = new PanelEvidenceMatching(empty);
        empty.FamilyPanel = PanelEvidenceMatching.WithConfirmed(empty.FamilyPanel, "stable-key", PanelEvidenceMatching.Confirm(names,
            new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Keyword, "User entered label") }));
        Check("Z12p4 no-evidence panels can hold an explicit edited choice with zero claimed coverage",
            emptySnapshot.Suggest(names).Count == 0 && emptySnapshot.Coverage(PanelEvidenceMatchKind.Keyword, "User entered label").DatabaseTargets == 0
            && FamilyClusterFile.FromJsonText(empty.ToJsonText()).FamilyPanel!.ConfirmedEvidenceMatches!["stable-key"].UserConfirmed);

        var replayFile = EvidenceMatchingFile(); string replayBefore = replayFile.ToJsonText();
        var session = new EvidenceGroupingSession(replayFile, "match fixture"); session.Propose("Action alpha");
        session.ConfirmPanelEvidenceMatches("stable-key", confirmed);
        var manual = session.AddToFamily(new EvidenceManualRequest(new[] { 0 }, "stable-key", "Nome fornecido", false, "Confirmed mapping fixture"));
        var result = session.Apply(new EvidenceReviewViewModel(manual).CaptureSelection() with { ProteinIndices = new[] { 0 } });
        string replayAfter = replayFile.ToJsonText();
        Check("Z12p4 normal Apply stores confirmed matches with explicit hand proof", replayFile.FamilyPanel!.ConfirmedEvidenceMatches!.ContainsKey("stable-key")
            && result.Recipe.Assignments.Single().Tier == EvidenceTier.AddedByHand);
        result.Undo!.Restore(replayFile);
        bool undo = replayFile.ToJsonText() == replayBefore;
        EvidenceApply.ApplyRecipe(replayFile, result.Recipe);
        Check("Z12p4 saved matches and grouping replay byte for byte", undo && replayFile.ToJsonText() == replayAfter);
        var tampered = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(result.Recipe))!;
        tampered.FamilyPanel!.ConfirmedEvidenceMatches!["stable-key"] = PanelEvidenceMatching.Confirm(names, new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Keyword, "Edited label") });
        var source = FamilyClusterFile.FromJsonText(replayBefore); refused = false;
        try { EvidenceApply.ApplyRecipe(source, tampered); } catch (InvalidOperationException) { refused = true; }
        Check("Z12p4 recipe matching-metadata edits are refused before publication", refused && source.ToJsonText() == replayBefore);
        source = FamilyClusterFile.FromJsonText(replayBefore); string externalEdit = "";
        var progress = new InlineOperationProgress(value =>
        {
            if (value.Stage != "evidence-apply" || value.Completed != 1) return;
            source.FamilyPanel!.ConfirmedEvidenceMatches = new() { ["stable-key"] = PanelEvidenceMatching.Confirm(names,
                new[] { new PanelEvidenceChoice(PanelEvidenceMatchKind.Keyword, "Concurrent user choice") }) };
            externalEdit = source.ToJsonText();
        });
        var operation = new Operations.OperationContext(progress: progress); refused = false;
        try { EvidenceApply.ApplyRecipe(source, result.Recipe, operation); }
        catch (InvalidOperationException error) { refused = error.Message.Contains("panel matches changed", StringComparison.Ordinal); }
        Check("Z12p4 a concurrent confirmed-match edit wins over the prepared grouping transaction",
            refused && !operation.HasCommitted && source.ToJsonText() == externalEdit);
    }
}
