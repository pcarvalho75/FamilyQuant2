using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyStudyMembershipCommands() =>
    [
        new("--test-family-study-membership", 375, TestCommandPhase.Raw,
            args => args.Contains("--test-family-study-membership", StringComparer.Ordinal), (_, _) =>
            {
                FamilyStudyMembershipTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static bool RefusesMembership(Action action, string expected)
    {
        try { action(); return false; }
        catch (InvalidOperationException ex) { return ex.Message.Contains(expected, StringComparison.Ordinal); }
    }

    private static void FamilyStudyMembershipTests()
    {
        EnsureApplication();
        var original = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "A", Sequence = "AAAAAAAK", UserID = 7, Description = "First protein" },
            new() { SequenceIdentifier = "B", Sequence = "CCCCCCCK", UserID = 9, Description = "Second protein" },
        } };
        var changed = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "A", Sequence = "AAAAAAAK", UserID = 9, Description = "First protein" },
            new() { SequenceIdentifier = "B", Sequence = "CCCCCCCK", UserID = 7, Description = "Second protein" },
        } };
        string initial = FamilyStudyMembership.Fingerprint(original);
        Check("protein-set provenance alone cannot distinguish reused family ids",
            FamilyEnvelopeQuant.ProteinSetFingerprint(original) == FamilyEnvelopeQuant.ProteinSetFingerprint(changed));
        Check("membership provenance changes when a UserID is reused for a different protein family",
            initial != FamilyStudyMembership.Fingerprint(changed));
        var reordered = new FamilyClusterFile { MyExtendedFastaItems = original.MyExtendedFastaItems.AsEnumerable().Reverse().ToList() };
        Check("membership provenance is independent of database row order", initial == FamilyStudyMembership.Fingerprint(reordered));
        string beforeName = initial;
        original.MyExtendedFastaItems[0].UserAnnotation = "A renamed label";
        Check("changing only display text does not change membership", beforeName == FamilyStudyMembership.Fingerprint(original));
        var rows = new List<QuantRow>
        {
            Qr("identified", "r", 1, 2, "AAAAAAAK", 100, 100), Qr("identified", "r", 2, 2, "CCCCCCCK", 20, 20),
        };
        var hits = FamilyEnvelopeQuant.SearchCuration(rows, original);
        var mapping = FamilyEnvelopeQuant.AttributeHits(hits, original);
        var assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), mapping);
        var manifest = new QuantManifest { PerRunTic = new() { ["r"] = 1200 } };
        FamilyCompositionStudy.StoreRunShares(manifest, assigned);
        FamilyCompositionStudy.StoreProteinCounts(manifest, assigned, original.MyExtendedFastaItems);
        FamilyCompositionStudy.StoreSubclasses(manifest, assigned, (_, _, _) => new[] { "part" });
        FamilyCompositionStudy.StorePositions(manifest, assigned, (_, sequence, family) => new[] { new PeptideProteinPlacement
        {
            ProteinIdentifier = original.MyExtendedFastaItems.Single(p => p.UserID == family).SequenceIdentifier,
            ProteinSequence = sequence, MatchKind = PeptideLocalizationMatchKind.ExactContainment,
            Intervals = new() { new() { Start = 1, End = sequence.Length } },
        } });
        var studies = new IFamilyMembershipStudy[] { manifest.FamilyRunShares!, manifest.FamilyProteinCounts!,
            manifest.FamilySubclassParts!, manifest.FamilyDomainParts!, manifest.FamilyPeptideLocalization! };
        Check("every stored family study inherits the current production attribution's membership",
            studies.All(s => s.MembershipSha256 == initial));
        Check("unchanged grouping accepts the saved studies without recomputing signal", FamilyStudyMembership.Problem(original, manifest) == null);
        string values = JsonSerializer.Serialize(manifest);
        Check("the reused-UserID case is refused with a plain membership message",
            RefusesMembership(() => FamilyStudyMembership.RequireCurrent(changed, manifest), "family membership"));
        Check("refusal never edits or silently stamps old values", values == JsonSerializer.Serialize(manifest));
        Check("panel report refuses before relabelling old numbers by current UserID",
            RefusesMembership(() => ReportPanelSections.BuildFromResults(changed, manifest), "family membership"));
        Check("project composition figures refuse the same mismatched grouping",
            RefusesMembership(() => StoredCompositionFigures.Build(manifest, new Dictionary<int, string> { [7] = "Second family", [9] = "First family" }, changed), "family membership"));
        var results = new AnalyticsViewModel(rows, manifest, changed);
        Check("Results can open the project but refuses stale stored charts with a visible reason",
            results.BuildCompositionFigures().Count == 0 && results.Status.Contains("family membership", StringComparison.Ordinal));
        string reportFolder = Path.Combine(Path.GetTempPath(), "FamilyQuant.MembershipReport", Guid.NewGuid().ToString("N"));
        Check("report export refuses before rendering files or joining old family data",
            RefusesMembership(() => ReportProjectExporter.Build(rows, manifest, changed, reportFolder), "family membership")
            && !Directory.Exists(reportFolder));
        var legacy = JsonSerializer.Deserialize<QuantManifest>(values)!;
        foreach (var study in new IFamilyMembershipStudy[] { legacy.FamilyRunShares!, legacy.FamilyProteinCounts!, legacy.FamilySubclassParts!, legacy.FamilyDomainParts!, legacy.FamilyPeptideLocalization! })
            study.MembershipSha256 = null;
        Check("legacy unverified studies are refused rather than assigned the current membership",
            RefusesMembership(() => FamilyStudyMembership.RequireCurrent(original, legacy), "do not record their family membership"));
        Check("a project without family studies remains valid without invented results",
            FamilyStudyMembership.Problem(changed, new QuantManifest()) == null);
        var sensitivity = new FamilySensitivityResult { CurationFingerprint = FamilySensitivityStudy.CurationFingerprint(original) };
        var sensitivityManifest = new QuantManifest { FamilySensitivity = sensitivity };
        Check("sensitivity retains its existing stronger grouping binding", FamilyStudyMembership.Problem(original, sensitivityManifest) == null
            && FamilyStudyMembership.Problem(changed, sensitivityManifest)?.Contains("family membership", StringComparison.Ordinal) == true);
        sensitivity.Variants.Add(new() { Policies = new() { new() { PerRunShares = new() { MembershipSha256 = FamilyStudyMembership.Fingerprint(changed) } } } });
        Check("a sensitivity variant cannot hide a contradictory nested run-share grouping",
            FamilyStudyMembership.Problem(original, sensitivityManifest)?.Contains("family membership", StringComparison.Ordinal) == true);
        var sensitivityView = new FamilySensitivityViewModel(null, sensitivityManifest, original, null, () => { });
        Check("sensitivity display refuses contradictory grouped values with a plain status",
            !sensitivityView.HasData && sensitivityView.Status.Contains("family membership", StringComparison.Ordinal));
        Check("sensitivity cannot stamp a reused-UserID calculation built with stale cached attribution",
            Throws<InvalidDataException>(() => FamilySensitivityStudy.Build(rows, new QuantManifest
            {
                PerRunTic = new() { ["r"] = 1200 }, FamilyMapping = FamilyEnvelopeQuant.ProvenanceFor(original, mapping.IdentityThreshold),
            }, changed, mapping)));
        var otherCounts = JsonSerializer.Deserialize<FamilyProteinCountsResult>(JsonSerializer.Serialize(manifest.FamilyProteinCounts))!;
        otherCounts.MembershipSha256 = FamilyStudyMembership.Fingerprint(changed);
        Check("different stored study memberships cannot be joined even in a standalone figure",
            RefusesMembership(() => StoredCompositionChartData.FromRunShares(manifest.FamilyRunShares!, otherCounts), "family membership"));
        var mixed = new FamilyProteinCountsResult();
        Check("a known study cannot silently join an unverified companion study",
            RefusesMembership(() => StoredCompositionChartData.FromRunShares(manifest.FamilyRunShares!, mixed), "do not record their family membership"));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("membership provenance is culture-invariant", initial == FamilyStudyMembership.Fingerprint(original));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        string scratch = Path.Combine(Path.GetTempPath(), "FamilyQuant.Membership", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string path = Path.Combine(scratch, "bound.fqproj");
        ProjectStore.WriteFqproj(path, original, rows, manifest);
        var reopened = ProjectStore.ReadFqproj(path);
        Check("membership binding survives a real project round trip",
            reopened.manifest.FamilyRunShares?.MembershipSha256 == initial
            && FamilyStudyMembership.Problem(reopened.fcf, reopened.manifest) == null);
    }
}
