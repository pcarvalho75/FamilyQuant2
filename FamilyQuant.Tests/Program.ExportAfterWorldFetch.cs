using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ExportAfterWorldFetchCommands() =>
    [
        new("--test-export-after-world-fetch", 74, TestCommandPhase.Raw, a => a.Contains("--test-export-after-world-fetch", StringComparer.Ordinal), (_, _) =>
        {
            ExportAfterWorldFetchTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "export-after-world-fetch"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>A grouped synthetic project whose proteins can fetch UniProt families through the fake downloads of the
    /// family-world tests; the same peptides as the curation export test.</summary>
    private static FamilyClusterFile WorldGrouped()
    {
        static FastaItemExtended Protein(string id, int taxon, bool words, string sequence, int userId, params string[] domains) => new()
        {
            SequenceIdentifier = id, Sequence = sequence, UserID = userId, USearchID = userId,
            Description = "Fixture protein OS=Species " + taxon.ToString(CultureInfo.InvariantCulture) + " OX=" + taxon.ToString(CultureInfo.InvariantCulture) + " PE=1 SV=1",
            Evidence = new() { Accession = NearestReviewed.EntryAccession(id), Reviewed = id.StartsWith("sp|", StringComparison.Ordinal), Family = domains.Length > 0 ? "Fixture family" : "",
                Keywords = words ? new() { "Leading scope" } : new() { "Other" }, Pfam = domains.Select(d => new EvidenceDomain { Accession = d }).ToList(),
                Source = new EvidenceRecordSource { Provider = "UniProt", Release = "fixture" } },
        };
        var file = new FamilyClusterFile
        {
            EvidenceSource = new EvidenceProvenance { Provider = "UniProt", Release = "fixture" },
            FamilyPanel = new() { Families = new()
            {
                new() { Key = "main", DisplayName = "Main group", Pfam = new() { new() { Accession = "domain-a" } } },
                new() { Key = "other", DisplayName = "Other group", Pfam = new() { new() { Accession = "domain-b" } } },
            } },
            MyExtendedFastaItems = new()
            {
                Protein("sp|P57001|SYNTHETIC", 21, true, "AAAAAAAKCCCCCCCKGGGGGGGK", 1, "domain-a"),
                Protein("sp|P57002|SYNTHETIC", 22, true, "AAAAAAAKDDDDDDDKHHHHHHHK", 2, "domain-a"),
                Protein("sp|P57003|SYNTHETIC", 21, true, "EEEEEEEKFFFFFFFK", 3, "domain-b"),
                Protein("tr|P57004|SYNTHETIC", 21, false, "WWWWWWWKYYYYYYYK", 4),
                Protein("tr|P57005|SYNTHETIC", 22, false, "MMMMMMMKNNNNNNNK", 5),
            },
        };
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "Leading scope" } };
        var proposal = PanelEvidenceClassifier.Propose(file, options);
        EvidenceApply.Apply(file, proposal, new EvidenceApplySelection { SourceFingerprint = proposal.SourceFingerprint,
            SelectedProteinIndices = proposal.Proteins.Where(p => p.SuggestedSelected).Select(p => p.ProteinIndex).ToArray(),
            FamilyPanel = FamilyPanelValidation.Clone(file.FamilyPanel!) }, "fixture");
        return file;
    }

    /// <summary>Fetch UniProt families as the panel button does, through the fake taxonomy and family downloads.</summary>
    private static bool FetchWorldForTest(FamilyClusterFile file)
    {
        using var taxonomy = new TaskTaxonomyHandler();
        var discovered = EvidenceTaskDiscovery.DiscoverAsync(file, taxonomy).GetAwaiter().GetResult();
        var task = EvidenceTaskDiscovery.Confirm(file, discovered, discovered.ProposedScopeTerms);
        using var download = new WorldFetchHandler();
        return CoreOperation.RunAsync("Family world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context, download))
            .GetAwaiter().GetResult().Status == OperationStatus.Succeeded && file.EvidenceSource?.FamilyWorld != null;
    }

    /// <summary>XP1: after Apply grouping, the actions that write the evidence source or the list without changing what
    /// grouping reads keep the report exportable; a change to what grouping reads still refuses.</summary>
    private static void ExportAfterWorldFetchTests(string output)
    {
        Directory.CreateDirectory(output);
        var rows = new List<QuantRow>
        {
            Qr("identified", "run_a", 1, 2, "AAAAAAAK", 10, 10), Qr("identified", "run_a", 2, 2, "CCCCCCCK", 10, 10),
            Qr("identified", "run_a", 3, 2, "EEEEEEEK", 20, 20), Qr("identified", "run_a", 4, 2, "WWWWWWWK", 5, 5),
            Qr("identified", "run_b", 5, 2, "AAAAAAAK", 20, 20), Qr("identified", "run_b", 6, 2, "DDDDDDDK", 20, 20),
            Qr("identified", "run_b", 7, 2, "EEEEEEEK", 40, 40), Qr("identified", "run_b", 8, 2, "MMMMMMMK", 5, 5),
        };
        string Export(FamilyClusterFile file) => QuantifyAndExportReport(file, rows, output);
        var baseline = WorldGrouped();
        Check("XP1 the grouped project exports before any later action", baseline.EvidenceRecipe?.PanelContext != null && Export(baseline) == "");

        var world = WorldGrouped(); bool fetched = FetchWorldForTest(world);
        string message = Export(world);
        if (message.Length > 0) Console.WriteLine("  XP1 export after Fetch UniProt families: " + message);
        Check("XP1 Fetch UniProt families after Apply grouping, then Quantify and Export report, succeeds", fetched && message == "");
        Check("XP1 fetching UniProt families again keeps the report exportable", FetchWorldForTest(world) && Export(world) == "");

        var reopened = WorldGrouped(); FetchWorldForTest(reopened);
        string saved = Path.Combine(output, "world-" + Guid.NewGuid().ToString("N") + ".fqproj");
        ProjectStore.WriteFqproj(saved, reopened, Array.Empty<QuantRow>(), new QuantManifest());
        Check("XP1 save and reopen after the fetch keeps the report exportable", Export(ProjectStore.ReadFqproj(saved).fcf) == "");

        var locks = WorldGrouped(); locks.LockedUserIds.Clear();
        Check("XP1 unlocking families keeps the report exportable", Export(locks) == "");
        var subclass = WorldGrouped();
        subclass.FamilyPanel!.Families[0].Subclasses.Add(new() { Key = "part", DisplayName = "A part", RequiredPfam = new() { "domain-a" } });
        subclass.FamilyPanel.Families[0].DisplayName = "Renamed group";
        Check("XP1 a subclass rule or a new display name in the list keeps the report exportable", Export(subclass) == "");

        // XP2: a fingerprint written before 2.1.19 also hashed a world stored before Apply grouping; it still verifies.
        var older = WorldGrouped(); FetchWorldForTest(older);
        var fingerprints = typeof(PanelAssignmentEvidenceStore).GetMethod("Fingerprints", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var state = ((IReadOnlyList<PanelProteinAssignment>)older.PanelProteinAssignments!, (Func<int, int>)(index => older.MyExtendedFastaItems[index].UserID), false);
        string withWorld = ((string[])fingerprints.Invoke(null, new object?[] { older, older.EvidenceRecipe, older.EvidenceSource, true, true, new[] { state } })!)[0];
        bool differs = withWorld != older.PanelProteinAssignmentsFingerprint;
        older.PanelProteinAssignmentsFingerprint = older.EvidenceRecipe!.PanelProteinAssignmentsFingerprint = withWorld;
        Check("XP2 a fingerprint that also hashed the stored world still verifies and exports", differs
            && PanelAssignmentEvidenceStore.Current(older) != null && Export(older) == "");
        var model = new CurationViewModel(older);
        model.ApplyBatch(new[] { older.MyExtendedFastaItems[3] }, older.MyExtendedFastaItems[0].UserID.ToString(CultureInfo.InvariantCulture), "");
        Check("XP2 a family change on such a project keeps the facts current", older.PanelProteinAssignmentsFingerprint!.StartsWith("2:", StringComparison.Ordinal)
            && PanelAssignmentEvidenceStore.Current(older) != null);

        var legacyDigest = WorldGrouped(); FetchWorldForTest(legacyDigest);
        string withoutWorldDigest = legacyDigest.PanelProteinAssignmentsFingerprint!;
        var digestState = ((IReadOnlyList<PanelProteinAssignment>)legacyDigest.PanelProteinAssignments!,
            (Func<int, int>)(index => legacyDigest.MyExtendedFastaItems[index].UserID), true);
        string withWorldDigest = ((string[])fingerprints.Invoke(null, new object?[]
            { legacyDigest, legacyDigest.EvidenceRecipe, legacyDigest.EvidenceSource, true, true, new[] { digestState } })!)[0];
        legacyDigest.PanelProteinAssignmentsFingerprint = legacyDigest.EvidenceRecipe!.PanelProteinAssignmentsFingerprint = withWorldDigest;
        Check("XP2 a legacy digest that hashed the stored world still verifies", withoutWorldDigest != withWorldDigest
            && withWorldDigest.StartsWith("2:", StringComparison.Ordinal) && PanelAssignmentEvidenceStore.Current(legacyDigest) != null);
        string legacyDigestSaved = Path.Combine(output, "legacy-digest-" + Guid.NewGuid().ToString("N") + ".fqproj");
        ProjectStore.WriteFqproj(legacyDigestSaved, legacyDigest, Array.Empty<QuantRow>(), new QuantManifest());
        var reopenedLegacyDigest = ProjectStore.ReadFqproj(legacyDigestSaved).fcf;
        Check("XP2 a legacy digest still verifies and exports after save and reopen",
            PanelAssignmentEvidenceStore.Current(reopenedLegacyDigest) != null && Export(reopenedLegacyDigest) == "");

        var listed = WorldGrouped(); FetchWorldForTest(listed);
        listed.FamilyPanel!.Families[0].Pfam.Add(new() { Accession = "domain-z" });
        Check("XP1 a list row's changed Pfam codes still refuse the report", Export(listed) == PanelAssignmentEvidenceStore.RebuildMessage);
        var evidence = WorldGrouped(); FetchWorldForTest(evidence);
        evidence.MyExtendedFastaItems[1].Evidence!.Pfam.Add(new() { Accession = "domain-b" });
        Check("XP1 changed protein evidence that grouping reads still refuses the report", Export(evidence) == PanelAssignmentEvidenceStore.RebuildMessage);
    }
}
