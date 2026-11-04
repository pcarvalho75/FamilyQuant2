using FamilyQuant.Homology;
using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HomologyDefaultMigrationCommands() =>
    [new("--test-homology-default-migration", 22, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-homology-default-migration", (_, _) =>
        {
            HomologyDefaultMigrationTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HomologyDefaultMigrationTests()
    {
        var current = new FullXicOptions().FamilyMappingOptions!;
        Check("J47d new Compute defaults to margin-zero score tier", current.Science?.ScoreTier is { Enabled: true, MaximumPamScoreDrop: 0 });
        Check("J47d length and tag gates remain OFF", current.Science?.DecoyGate?.Enabled != true && current.Science?.TagErrors?.Enabled != true);
        Check("J47d missing saved mapping retains legacy OFF semantics", HomologyDefaults.ForStoredMapping(null).Science == null);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "target-a", Sequence = "XXACDEFGHIKLQQ", UserID = 1 },
            new() { SequenceIdentifier = "target-b", Sequence = "XXACDEYGHIKLQQ", UserID = 2 },
            new() { SequenceIdentifier = "target-c", Sequence = "XXMNPQRSTVWYQQ", UserID = 3 },
        } };
        var rows = new List<QuantRow>
        {
            new() { Side = NativeQuantFractions.SideIdentified, Run = "run-a", Sequence = "MNPQRSTVWY", Charge = 2, Scan = 1, PrecursorMz = 500, EnvelopeSum = 100, IsotopeAreas = new[] { 100.0, 0.0 } },
            new() { Side = NativeQuantFractions.SideDeNovo, Run = "run-a", Sequence = "ACDEFGHIKL", Charge = 2, Scan = 2, PrecursorMz = 600, EnvelopeSum = 10, IsotopeAreas = new[] { 10.0, 0.0 } },
        };
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.J47d", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var legacyOptions = new HomologyOptions();
        var legacyHits = CurationHomologyCache.GetOrSearch(rows, file, root, legacyOptions);
        var legacyMap = FamilyEnvelopeQuant.AttributeHits(legacyHits, file);
        var legacy = new QuantManifest { FamilyMapping = FamilyEnvelopeQuant.ProvenanceFor(file, 0.8), PerRunTic = new() { ["run-a"] = 200 } };
        FamilyCompositionStudy.StoreRunShares(legacy, FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), legacyMap));
        string snapshot = JsonSerializer.Serialize(legacy.FamilyRunShares);
        string headline = ReportStudyExports.Headline(FamilyReportThreshold.Build(legacy.FamilyRunShares!, file), null, FamilyNaming.Names(file.MyExtendedFastaItems));
        string project = Path.Combine(root, "legacy.fqproj");
        ProjectStore.WriteFqproj(project, file, rows, legacy);
        var reopened = ProjectStore.ReadFqproj(project);
        Check("J47d legacy project mapping has no newly invented Science policy", reopened.manifest.FamilyMapping?.Science == null
            && !JsonSerializer.Serialize(reopened.manifest.FamilyMapping).Contains("Science", StringComparison.Ordinal));
        bool legacyHit = false;
        var oldAgain = CurationHomologyCache.GetOrSearch(reopened.quant, reopened.fcf, root,
            HomologyDefaults.ForStoredMapping(reopened.manifest.FamilyMapping), progress: hit => legacyHit = hit);
        Check("J47d reopening legacy mapping keeps the existing attribution cache key", legacyHit);
        bool? newHit = null;
        var newHits = CurationHomologyCache.GetOrSearch(rows, file, root, current, progress: hit => newHit = hit);
        Check("J47d new Compute uses a separate score-tier cache entry", newHit == false && Directory.GetFiles(root, "*.fqhits").Length == 2);
        var newMap = FamilyEnvelopeQuant.AttributeHits(newHits, file);
        Check("J47d tier narrows de novo family credit while legacy remains broad", legacyMap.Resolve(NativeQuantFractions.SideDeNovo, "ACDEFGHIKL").Count == 2
            && newMap.Resolve(NativeQuantFractions.SideDeNovo, "ACDEFGHIKL").SequenceEqual(new[] { 1 }));
        Check("J47d identified family credit is unchanged", legacyMap.Resolve(NativeQuantFractions.SideIdentified, "MNPQRSTVWY")
            .SequenceEqual(newMap.Resolve(NativeQuantFractions.SideIdentified, "MNPQRSTVWY")));
        var provenance = FamilyEnvelopeQuant.ProvenanceForScience(file, current.MinIdentity, current.Science)!;
        var persisted = JsonSerializer.Deserialize<FamilyMappingProvenance>(JsonSerializer.Serialize(provenance))!;
        Check("J47d saved new mapping restores the recorded score-tier policy", HomologyDefaults.ForStoredMapping(persisted).Science?.ScoreTier?.Enabled == true);
        bool scoredHit = false;
        CurationHomologyCache.GetOrSearch(rows, file, root, HomologyDefaults.ForStoredMapping(persisted), progress: hit => scoredHit = hit);
        Check("J47d reopening new mapping reuses its own matching cache entry", scoredHit);
        var spectrum = new List<SpectrumComparisonRow> { new() { QuantSide = NativeQuantFractions.SideDeNovo,
            QuantSequence = "ACDEFGHIKL", FamilyUserIds = "99", FamilyAssignments = "previous" } };
        string? rejoinNote = FamilyEnvelopeQuant.RefreshSpectrumFamilies(spectrum, persisted, file, _ => newMap);
        Check("J47d recorded tier provenance permits the matching spectrum-family rejoin", rejoinNote == null && spectrum[0].FamilyUserIds == "1");
        string newProject = Path.Combine(root, "new.fqproj");
        ProjectStore.WriteFqproj(newProject, file, rows, new QuantManifest { FamilyMapping = persisted });
        Check("J47d project codec preserves recorded tier provenance", ProjectStore.ReadFqproj(newProject).manifest.FamilyMapping?.Science?.ScoreTier?.Enabled == true);
        var view = new CurationQuantitationViewModel(reopened.fcf);
        typeof(CurationQuantitationViewModel).GetField("fullXicCacheDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, root);
        view.SetFullXicMappingProvenance(reopened.manifest.FamilyMapping);
        view.SetFullXicQuant(reopened.quant, reopened.manifest.PerRunTic);
        Check("J47d legacy family screen uses recorded OFF semantics", view.CurrentFullXicAttribution()!.Resolve(NativeQuantFractions.SideDeNovo, "ACDEFGHIKL").Count == 2);
        view.SetFullXicMappingProvenance(persisted); view.SetFullXicQuant(rows, legacy.PerRunTic);
        Check("J47d new family screen uses its recorded tier semantics", view.CurrentFullXicAttribution()!.Resolve(NativeQuantFractions.SideDeNovo, "ACDEFGHIKL").Count == 1);
        var basis = ReportCompositionBasis.Select(reopened.fcf, reopened.manifest);
        string after = ReportStudyExports.Headline(basis.Grouping!, null, basis.Labels);
        Check("J47d old stored report headline is byte-identical after open and new-cache work", headline == after, "Before=" + headline + " After=" + after);
        Check("J47d new-cache work never changes old stored numeric studies", snapshot == JsonSerializer.Serialize(legacy.FamilyRunShares));
        Check("J47d migration Help states stored preservation and explicit Compute", HomologyMigrationHelp.Text.Contains("opening alone does not apply", StringComparison.Ordinal)
            && HomologyMigrationHelp.Text.Contains("separate cache entry", StringComparison.Ordinal));
    }
}
