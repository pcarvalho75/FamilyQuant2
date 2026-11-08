using FamilyQuant.DatabaseUpdate;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    /// <summary>The placement fixture plus annotated families for the name rules, a locked family that empties,
    /// a header change and stored evidence.</summary>
    private static PlacementFixture DatabaseUpdateApplyFixture()
    {
        var basis = DatabaseUpdatePlacementFixture(exhaustive: true);
        FamilyClusterFile project = basis.Project;
        var entries = basis.Source.Records.ToList();
        foreach (FastaItemExtended row in project.MyExtendedFastaItems.Where(r => r.UserID == 1)) row.UserAnnotation = "Alpha family name";
        project.MyExtendedFastaItems[0].Evidence = new ProteinEvidence { Accession = "P30001", Reviewed = true, Family = "Alpha family" };
        project.MyExtendedFastaItems[1].Evidence = new ProteinEvidence { Accession = "Q30002", Reviewed = false };
        project.MyExtendedFastaItems[1].Domains = "PF99999";
        project.MyExtendedFastaItems[5].Evidence = new ProteinEvidence { Accession = "P30006", Reviewed = true };
        project.MyExtendedFastaItems[5].Domains = "PF99998";
        int at = entries.FindIndex(e => e.Identifier == "tr|Q30002|Q30002_SYN");
        entries[at] = new DatabaseUpdateEntry("sp|Q30002|A2_SYN", "Alpha two reviewed OS=Synthetica una OX=1", entries[at].Sequence);

        void Family(int family, params (string Id, string Annotation, bool Survives)[] members)
        {
            int n = 0;
            foreach (var (id, annotation, survives) in members)
            {
                string sequence = UpdateMutate(UpdateSequence(400 + family, 140), 500 + family * 10 + n++, 0.05);
                var row = UpdateRow(id, "Protein " + family + " member " + n + " OS=Synthetica una OX=1", sequence, family);
                row.UserAnnotation = annotation;
                project.MyExtendedFastaItems.Add(row);
                if (survives) entries.Add(new DatabaseUpdateEntry(id, row.Description, sequence));
            }
        }
        // Old-name carriers all removed, no other annotation: the name is kept by default.
        Family(8, ("sp|P30009|E1_SYN", "Kept name", false), ("tr|Q30010|Q30010_SYN", "", true));
        // A different annotation survives: unticked by default; keeping writes the old name on every member.
        Family(9, ("sp|P30011|F1_SYN", "Old nine", false), ("tr|Q30012|Q30012_SYN", "Other nine", true), ("tr|Q30013|Q30013_SYN", "", true));
        // Two different surviving annotations at equal count: a tie that member order would settle.
        Family(10, ("sp|P30014|G1_SYN", "Ten A", false), ("tr|Q30015|Q30015_SYN", "Ten B", true), ("tr|Q30016|Q30016_SYN", "Ten C", true));
        // The old name and a different one survive at equal count; the different one comes first.
        Family(11, ("tr|Q30017|Q30017_SYN", "Other eleven", true), ("sp|P30018|H1_SYN", "Eleven", true), ("tr|Q30019|Q30019_SYN", "Eleven", false));
        // A locked family whose only protein is removed.
        Family(12, ("tr|Q30020|Q30020_SYN", "", false));
        project.LockedUserIds.Add(12);
        project.SourceSequenceCount = project.MyExtendedFastaItems.Count;
        return new PlacementFixture(project, DatabaseUpdateSource.FromEntries("new.fasta", "NEWSHA", entries),
            project.MyExtendedFastaItems.Select((r, i) => (r.SequenceIdentifier, i)).ToDictionary(t => t.SequenceIdentifier, t => t.i, StringComparer.Ordinal),
            entries.Select((r, i) => (r.Identifier, i)).ToDictionary(t => t.Identifier, t => t.i, StringComparer.Ordinal));
    }

    /// <summary>FQKimi on Z14c3c: a comparison and a placement made from different new files must never meet;
    /// panel assignments and a confirmed task go out of date; current-reader readback is retained; the frozen-reader gate is retired by D80.</summary>
    private static void DatabaseUpdateBindingTests()
    {
        Section("Database update: one new file per update, and what goes out of date (Z14c3c fix)");
        var basis = DatabaseUpdateApplyFixture();
        FamilyClusterFile project = basis.Project;
        // The offline taxonomy fixture keeps taxa 1, 10 to 13 for its lineage, so the invented taxa move to 21 and up.
        static string Taxa(string description) => System.Text.RegularExpressions.Regex.Replace(description, @"OX=(\d+)",
            m => "OX=" + (20 + int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (FastaItemExtended row in project.MyExtendedFastaItems) row.Description = Taxa(row.Description);
        var entries = basis.Source.Records.Select(e => e with { Description = Taxa(e.Description) }).ToList();
        var fixture = basis with { Source = DatabaseUpdateSource.FromEntries("new.fasta", "NEWSHA", entries) };
        // A second file with the same records, except that one added record now resembles family 1.
        var otherEntries = fixture.Source.Records.ToList();
        int pi = otherEntries.FindIndex(e => e.Identifier == "tr|Q40005|Q40005_SYN");
        otherEntries[pi] = new DatabaseUpdateEntry(otherEntries[pi].Identifier, otherEntries[pi].Description,
            UpdateMutate(project.MyExtendedFastaItems[0].Sequence, 990, 0.05));
        var otherSource = DatabaseUpdateSource.FromEntries("other.fasta", "OTHERSHA", otherEntries);
        var comparison = DatabaseUpdateComparer.Compare(project, fixture.Source);
        var otherComparison = DatabaseUpdateComparer.Compare(project, otherSource);
        var otherPlacement = DatabaseUpdatePlacer.Place(project, otherComparison);
        Check("DU6: the two files give the same added positions, so only a binding can tell them apart",
            comparison.Added.SequenceEqual(otherComparison.Added));
        Check("DU6: a placement made from another new file is refused by Preview",
            Throws<ArgumentException>(() => DatabaseUpdatePlanner.Preview(project, comparison, otherPlacement)));
        var mixed = new DatabaseUpdatePreview
        {
            ProjectFingerprint = comparison.ProjectFingerprint, Comparison = comparison, Placement = otherPlacement,
            Rows = otherPlacement.Rows, Counts = otherPlacement.Counts, DecoyCounts = otherPlacement.DecoyCounts,
        };
        string untouched = project.ToJsonText();
        Check("DU6: a preview assembled from another file's placement is refused by Apply and changes nothing",
            Throws<ArgumentException>(() => DatabaseUpdateApply.Apply(project, mixed, Array.Empty<int>(), "test")) && project.ToJsonText() == untouched);

        // Panel assignments current before the update, out of date after it.
        project.FamilyPanel = new Panels.FamilyPanel { Name = "Synthetic panel", Families = new() { new Panels.FamilyPanelFamily { Key = "alpha", DisplayName = "Alpha" } } };
        project.PanelProteinAssignments = project.MyExtendedFastaItems.Select((p, i) => new Panels.PanelProteinAssignment
            { ProteinIndex = i, Identifier = p.SequenceIdentifier, FamilyUserId = p.UserID }).ToList();
        MakePanelAssignmentsCurrent(project, new EvidenceClassificationOptions { ScopeTerms = new[] { "Leading scope" } });
        bool panelCurrentBefore = PanelAssignmentEvidenceStore.Current(project) != null;
        // A confirmed task from discovery (offline taxonomy fixture) binds the protein set.
        using var handler = new TaskTaxonomyHandler();
        EvidenceTaskProposal task = EvidenceTaskDiscovery.DiscoverAsync(project, handler).GetAwaiter().GetResult();
        bool taskConfirmedBefore = !Throws<InvalidOperationException>(() => EvidenceTaskDiscovery.Confirm(project, task, new[] { "Leading scope" }));
        var preview = UpdatePreview(new PlacementFixture(project, fixture.Source, fixture.Old, fixture.New));
        DatabaseUpdateApply.Apply(project, preview, preview.NameChanges.Where(c => c.KeepByDefault).Select(c => c.FamilyUserId).ToArray(), "test");
        Check("DU5: panel family assignments read as out of date after the update",
            panelCurrentBefore && PanelAssignmentEvidenceStore.Current(project) == null && !SubclassAssignment.IsCurrent(project)
            && project.PanelProteinAssignments != null && project.FamilyPanel != null);
        Check("DU5: a scope confirmed before the update is refused after it",
            taskConfirmedBefore && Throws<InvalidOperationException>(() => EvidenceTaskDiscovery.Confirm(project, task, new[] { "Leading scope" })));

        // Retired deliberately by Paulo's D80; current-reader readback stays covered below.
        Console.WriteLine("  RETIRED " + FeatureLedger.Du7Retirement);

    }

    private static DatabaseUpdatePreview UpdatePreview(PlacementFixture fixture)
    {
        var comparison = DatabaseUpdateComparer.Compare(fixture.Project, fixture.Source);
        return DatabaseUpdatePlanner.Preview(fixture.Project, comparison, DatabaseUpdatePlacer.Place(fixture.Project, comparison));
    }

    private static void DatabaseUpdateApplyTests()
    {
        Section("Database update: the preview and the apply transaction (Z14c3c)");
        var fixture = DatabaseUpdateApplyFixture();
        FamilyClusterFile project = fixture.Project;
        DatabaseUpdatePreview preview = UpdatePreview(fixture);
        DatabaseUpdateNameChange? Change(int family) => preview.NameChanges.FirstOrDefault(c => c.FamilyUserId == family);

        Check("DU4: a family named by annotation whose carriers are all removed is offered with Keep ticked",
            Change(8) is { OldName: "Kept name", OldNameFromAnnotation: true, KeepByDefault: true } eight && eight.DifferingAnnotationMembers.Count == 0,
            JsonSerializer.Serialize(Change(8)));
        Check("DU4: a surviving different annotation lists its member and starts unticked",
            Change(9) is { OldName: "Old nine", NewName: "Other nine", KeepByDefault: false } nine
            && nine.DifferingAnnotationMembers.SequenceEqual(new[] { "tr|Q30012|Q30012_SYN" }), JsonSerializer.Serialize(Change(9)));
        Check("DU4: two surviving annotations at equal count (a tie) start unticked with both listed",
            Change(10) is { OldName: "Ten A", NewName: "Ten B", KeepByDefault: false } ten && ten.DifferingAnnotationMembers.Count == 2,
            JsonSerializer.Serialize(Change(10)));
        Check("DU4: the old name tied with a surviving different one that comes first is offered unticked",
            Change(11) is { OldName: "Eleven", NewName: "Other eleven", KeepByDefault: false } eleven
            && eleven.DifferingAnnotationMembers.SequenceEqual(new[] { "tr|Q30017|Q30017_SYN" }), JsonSerializer.Serialize(Change(11)));
        Check("DU4: families whose name does not change are not offered", Change(1) == null && Change(2) == null && Change(5) == null,
            string.Join(", ", preview.NameChanges.Select(c => c.FamilyUserId)));
        Check("DU4: the emptied locked family is named", preview.EmptiedFamilies.SequenceEqual(new[] { 12 }) && preview.Counts.FamiliesEmptied == 1);

        var beforeRows = project.MyExtendedFastaItems.Select(r => (r.SequenceIdentifier, r.UserID, r.USearchID, r.UserAnnotation)).ToList();
        var beforeNames = FamilyNaming.Names(project.MyExtendedFastaItems);
        string beforeMembership = FamilyStudyMembership.Fingerprint(project);
        var provenance = FamilyEnvelopeQuant.ProvenanceFor(project, 50.0)!;
        string panelJson = JsonSerializer.Serialize(new { project.FamilyPanel, project.Motifs, project.ReportDetails, project.ReportSelection });
        var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        string untouched = project.ToJsonText();
        Check("DU6: a cancelled update leaves the project byte-identical", Throws<OperationCanceledException>(() =>
            DatabaseUpdateApply.Apply(project, preview, new[] { 8, 9, 10, 11 }, "test", new OperationContext(cancelled.Token)))
            && project.ToJsonText() == untouched);
        Check("DU6: keeping a name that does not change is refused", Throws<ArgumentException>(() =>
            DatabaseUpdateApply.Apply(project, preview, new[] { 1 }, "test")) && project.ToJsonText() == untouched);

        var stamp = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        DatabaseUpdateApplyResult result = DatabaseUpdateApply.Apply(project, preview, new[] { 8, 9, 10, 11 }, "2.1.11-test",
            new OperationContext(), stamp);
        var after = project.MyExtendedFastaItems;
        FastaItemExtended Row(string identifier) => after.Single(r => r.SequenceIdentifier == identifier);
        var keptBefore = beforeRows.Where(b => after.Any(r => r.SequenceIdentifier == b.SequenceIdentifier || b.SequenceIdentifier == "tr|Q30002|Q30002_SYN")).ToList();

        Check("DU4: kept rows keep their order, families and numbers; removed rows leave; added records come last in file order",
            after.Take(after.Count - preview.Placement.Added.Count).Select(r => r.UserID).SequenceEqual(keptBefore.Select(b => b.UserID))
            && after.Take(after.Count - preview.Placement.Added.Count).Select(r => r.USearchID).SequenceEqual(keptBefore.Select(b => b.USearchID))
            && after.Skip(after.Count - preview.Placement.Added.Count).Select(r => r.SequenceIdentifier)
                .SequenceEqual(preview.Comparison.Added.Select(j => fixture.Source.Records[j].Identifier))
            && after.All(r => r.SequenceIdentifier != "sp|P30009|E1_SYN" && r.SequenceIdentifier != "tr|Q30020|Q30020_SYN"));
        Check("DU4: annotations of kept rows are unchanged except those a kept name wrote",
            keptBefore.Where(b => b.UserID < 8).All(b => after.Single(r => r.UserID == b.UserID && (r.SequenceIdentifier == b.SequenceIdentifier
                || b.SequenceIdentifier == "tr|Q30002|Q30002_SYN" && r.SequenceIdentifier == "sp|Q30002|A2_SYN")).UserAnnotation == b.UserAnnotation)
            && result.Record.AnnotationsWritten.ToHashSet().SetEquals(new[] { "tr|Q30010|Q30010_SYN", "tr|Q30012|Q30012_SYN", "tr|Q30013|Q30013_SYN",
                "tr|Q30015|Q30015_SYN", "tr|Q30016|Q30016_SYN", "tr|Q30017|Q30017_SYN" }), string.Join(", ", result.Record.AnnotationsWritten));
        var afterNames = FamilyNaming.Names(after);
        Check("DU4: kept names win with no tie, untouched names are equal, and the lock set keeps every surviving family",
            afterNames[8] == "Kept name" && afterNames[9] == "Old nine" && afterNames[10] == "Ten A" && afterNames[11] == "Eleven"
            && new[] { 1, 2, 3, 4, 6 }.All(f => afterNames[f] == beforeNames[f]) && project.LockedUserIds.SetEquals(new[] { 2 }),
            JsonSerializer.Serialize(afterNames));
        Check("DU4: the header change keeps evidence of the same accession; the changed sequence loses evidence and domains",
            Row("sp|Q30002|A2_SYN") is { Evidence.Accession: "Q30002", Domains: "PF99999", Description: "Alpha two reviewed OS=Synthetica una OX=1" }
            && Row("sp|P30006|C1_SYN") is { Evidence: null, Domains: "" } && Row("sp|P30001|A1_SYN").Evidence != null);
        Check("DU4: panel, motifs and report text are kept as stored",
            JsonSerializer.Serialize(new { project.FamilyPanel, project.Motifs, project.ReportDetails, project.ReportSelection }) == panelJson);
        Check("DU8: the project records the new source and one update record",
            project.SourceSha256 == "NEWSHA" && project.SourceSequenceCount == fixture.Source.Summary.SequenceCount
            && project.DatabaseUpdates is { Count: 1 } updates && updates[0] is { ProgramVersion: "2.1.11-test", ClusteringIdentity: 70, ExhaustivePlacement: true }
            && updates[0].UpdatedAtUtc == stamp && updates[0].Previous.Sha256 == "OLD" && updates[0].New.Sha256 == "NEWSHA"
            && updates[0].NameChanges.Count(c => c.Kept) == 4 && updates[0].Counts.NamesKept == 4 && updates[0].Counts.FamiliesEmptied == 1
            && updates[0].Rows.Count == preview.Rows.Count);

        // Test 5: what goes out of date says so through the existing checks.
        var manifest = new QuantManifest { FamilyRunShares = new FamilyRunSharesResult { MembershipSha256 = beforeMembership } };
        Check("DU5: saved family results report the changed membership",
            FamilyStudyMembership.Problem(project, manifest)?.Contains(FamilyStudyMembership.ChangedMessage, StringComparison.Ordinal) == true);
        string? kept = FamilyEnvelopeQuant.RefreshSpectrumFamilies(new List<DeNovo.SpectrumComparisonRow> { new() }, provenance, project,
            _ => throw new InvalidOperationException("No re-join may run for another database."));
        Check("DU5: the spectrum family columns are kept as computed against a different protein database",
            kept?.Contains("different protein database", StringComparison.Ordinal) == true, kept);
        Check("DU5: a stale preview is refused after the update", Throws<InvalidOperationException>(() =>
            DatabaseUpdateApply.Apply(project, preview, Array.Empty<int>(), "test")));

        // Test 7: save and reopen.
        string directory = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "database-update-apply");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "updated.fcf");
        var roundTrip = FamilyClusterFile.FromJsonText(project.ToJsonText());
        int differing = Enumerable.Range(0, after.Count).FirstOrDefault(i => JsonSerializer.Serialize(after[i]) != JsonSerializer.Serialize(roundTrip.MyExtendedFastaItems[i]), -1);
        Check("DU7: every row reads back as written", differing < 0, differing < 0 ? null
            : JsonSerializer.Serialize(after[differing]) + " READ " + JsonSerializer.Serialize(roundTrip.MyExtendedFastaItems[differing]));
        project.Save(path);
        FamilyClusterFile reopened = FamilyClusterFile.Load(path);
        Check("DU7: the updated project saves with a passing check and reopens with its update record",
            project.LastSaveCheck?.Passed == true && reopened.ToJsonText() == project.ToJsonText()
            && reopened.DatabaseUpdates![0].Rows.Count == preview.Rows.Count && reopened.MyExtendedFastaItems.Count == after.Count,
            project.LastSaveCheck?.ToString());

        // Mixed annotations left unticked: the surviving annotation names the family.
        var second = DatabaseUpdateApplyFixture();
        // An applied recipe stores protein positions; the row after a removed one moves up.
        int moved = second.Old["tr|Q30010|Q30010_SYN"];
        second.Project.EvidenceRecipe = new EvidenceRecipe { Assignments = { new EvidenceAssignment
            { ProteinIndex = moved, Identifier = "tr|Q30010|Q30010_SYN", FamilyId = 8, Name = "Recipe name" } } };
        var secondPreview = UpdatePreview(second);
        DatabaseUpdateApply.Apply(second.Project, secondPreview, secondPreview.NameChanges.Where(c => c.KeepByDefault).Select(c => c.FamilyUserId).ToArray(), "test");
        var secondNames = FamilyNaming.Names(second.Project.MyExtendedFastaItems);
        string audit = EvidenceExports.BuildAuditCsv(second.Project);
        Check("DU4: a stored recipe position that now holds another protein is never read as that protein",
            second.Project.MyExtendedFastaItems[moved].SequenceIdentifier != "tr|Q30010|Q30010_SYN" && !audit.Contains("Recipe name", StringComparison.Ordinal)
            && audit.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length == 1, audit);
        Check("DU4: with the default ticks, the annotation name is kept and unticked mixed families follow their surviving annotations",
            secondNames[8] == "Kept name" && secondNames[9] == "Other nine" && secondNames[10] == "Ten B" && secondNames[11] == "Other eleven",
            JsonSerializer.Serialize(secondNames));
    }
}
