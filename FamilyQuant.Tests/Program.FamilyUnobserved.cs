using FamilyQuant.Evidence;
using FamilyQuant.DeNovo;
using FamilyQuant.Panels;
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
    private static IEnumerable<TestCommand> FamilyUnobservedCommands() =>
    [
        new("--test-family-unobserved", 440, TestCommandPhase.Raw,
            args => args.Contains("--test-family-unobserved", StringComparer.Ordinal), (_, _) =>
            {
                FamilyUnobservedTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void FamilyUnobservedTests()
    {
        Section("Completed family studies - no peptide observed versus unavailable");
        var file = new FamilyClusterFile { FamilyPanel = new FamilyPanel { Families = new()
        {
            new() { Key = "observed", DisplayName = "Observed group" },
            new() { Key = "silent", DisplayName = "Unobserved group" },
        } }, MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "target_a", Sequence = "AAAAAAAK", UserID = 11 },
            new() { SequenceIdentifier = "target_b", Sequence = "CCCCCCCK", UserID = 33 },
        } };
        file.PanelProteinAssignments = file.MyExtendedFastaItems.Select((p, index) => new PanelProteinAssignment
        {
            ProteinIndex = index, Identifier = p.SequenceIdentifier, FamilyUserId = p.UserID,
            PanelFamilyKey = index == 0 ? "observed" : "silent", IsApplied = true,
        }).ToList();
        MakePanelAssignmentsCurrent(file, new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } });
        var rows = new[] { Qr("identified", "run_a", 1, 2, "AAAAAAAK", 3, 3), Qr("identified", "run_b", 2, 2, "AAAAAAAK", 7, 7) };
        var map = new PeptideFamilyAttribution();
        map.IdentifiedBySequence["AAAAAAAK"] = new[] { 11 };
        var assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), map);
        var manifest = new QuantManifest { FamilyRunShares = FamilyCompositionStudy.BuildRunShares(assigned,
            new[] { "run_a", "run_b", "empty" }, perRunTic: new Dictionary<string, double> { ["run_a"] = 30 }),
            FamilyProteinCounts = FamilyCompositionStudy.BuildProteinCounts(assigned, file.MyExtendedFastaItems) };
        manifest.FamilyRunShares.MembershipSha256 = manifest.FamilyProteinCounts.MembershipSha256 = FamilyStudyMembership.Fingerprint(file);
        string before = JsonSerializer.Serialize(manifest);
        ReportTable[] Silent() => ReportPanelSections.BuildFromResults(file, manifest).Single(s => s.Id == "family:silent").Blocks.OfType<ReportTable>().ToArray();
        var silent = Silent();
        Check("completed native studies expose zero shares for a panel family with no observed peptide", silent.Any(t => t.Title == "Observed family share"
            && t.Rows.Any(r => r.SequenceEqual(new[] { "33", "2", "0", "0", "Not available" }))));
        Check("the no-peptide state is explicit instead of a missing computation", silent.Any(t => t.Rows.Any(r => r.Contains("no peptide observed")))
            && !silent.Any(t => t.Title.Contains("availability", StringComparison.Ordinal)));
        Check("no-peptide protein explanation is zero rather than unavailable", silent.Any(t => t.Title == "Observed database protein counts"
            && t.Rows.Single().SequenceEqual(new[] { "33", "0", "0", "0", "0" })));
        Check("a zero denominator remains unavailable while a measured zero is zero", silent.Any(t => t.Title == "Observed run shares"
            && t.Rows.Any(r => r.SequenceEqual(new[] { "33", "empty", "0", "Not available", "Not available" }))
            && t.Rows.Any(r => r.SequenceEqual(new[] { "33", "run_a", "0", "0", "0" }))));
        Check("reading no-peptide report rows does not mutate saved studies", JsonSerializer.Serialize(manifest) == before);
        var counts = manifest.FamilyProteinCounts;
        manifest.FamilyProteinCounts = null;
        Check("an uncomputed protein study stays unavailable", Silent().Any(t => t.Title == "Protein count availability" && t.Rows.Single()[1] == "Not available"));
        manifest.FamilyProteinCounts = counts;
        string method = manifest.FamilyRunShares.Method;
        manifest.FamilyRunShares.Method = "An unspecified partial result";
        Check("an unspecified partial study cannot invent a measured zero", Silent().Any(t => t.Title == "Family share availability"));
        manifest.FamilyRunShares.Method = method;
        counts!.Families.Add(new() { FamilyUserId = 33, ObservedPeptideCount = 1, GreedyProteinCount = 1 });
        Check("counterevidence of an observed peptide prevents inventing an omitted share", Silent().Any(t => t.Title == "Family share availability"));
        counts.Families.RemoveAt(counts.Families.Count - 1);
        var countRows = counts.Families;
        counts.Families = new();
        Check("an observed share prevents inventing an omitted protein computation", FamilyUnobservedStudies.Read(file, manifest).ProteinCounts.All(f => f.FamilyUserId != 11));
        counts.Families = countRows;
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); string en = JsonSerializer.Serialize(Silent());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("no-peptide tables repeat across display cultures", en == JsonSerializer.Serialize(Silent()));
        }
        finally { CultureInfo.CurrentCulture = prior; }
        var labels = new Dictionary<int, string> { [11] = "Observed group", [33] = "Unobserved group" };
        string sharesCsv = ReportStudyExports.MeanShares(manifest.FamilyRunShares, labels);
        string countsCsv = ReportStudyExports.ProteinCounts(counts, labels);
        string observedShare = JsonSerializer.Serialize(manifest.FamilyRunShares.Families.Single());
        string observedCount = JsonSerializer.Serialize(counts.Families.Single());
        manifest.FamilySubclassParts = new() { MembershipSha256 = FamilyStudyMembership.Fingerprint(file) };
        manifest.FamilyDomainParts = new() { MembershipSha256 = FamilyStudyMembership.Fingerprint(file) };
        manifest.FamilyPeptideLocalization = new() { MembershipSha256 = FamilyStudyMembership.Fingerprint(file) };
        string original = JsonSerializer.Serialize(manifest);
        var view = FamilyUnobservedStudies.View(file, manifest);
        Check("the canonical Others partition and report exports receive the same no-peptide zero", FamilyReportThreshold.Build(view.FamilyRunShares!, file)
            .OtherFamilies.Any(f => f.FamilyUserId == 33 && f.MeanShare == 0)
            && ReportStudyExports.MeanShares(view.FamilyRunShares!, labels).Contains("Unobserved group", StringComparison.Ordinal));
        Check("the complete report view leaves original study containers and lists unchanged", original == JsonSerializer.Serialize(manifest)
            && view.FamilySubclassParts!.Families.Single().FamilySignal == 0 && manifest.FamilySubclassParts.Families.Count == 0);
        FamilyUnobservedStudies.Store(file, manifest);
        var zero = manifest.FamilyRunShares.Families.Single(f => f.FamilyUserId == 33);
        Check("compute completion stores zero rows and the explicit no-peptide flag", zero.NoPeptideObserved && zero.MeanShare == 0
            && zero.StandardDeviation == 0 && zero.CoefficientOfVariation == null && zero.MeanShareOfTic == null
            && manifest.FamilyProteinCounts!.Families.Single(f => f.FamilyUserId == 33).ObservedPeptideCount == 0);
        Check("adding no-peptide rows leaves all existing native CSV bytes unchanged", sharesCsv == ReportStudyExports.MeanShares(
            new FamilyRunSharesResult { Families = manifest.FamilyRunShares.Families.Where(f => f.FamilyUserId == 11).ToList() }, labels)
            && countsCsv == ReportStudyExports.ProteinCounts(new FamilyProteinCountsResult
            { Families = manifest.FamilyProteinCounts!.Families.Where(f => f.FamilyUserId == 11).ToList() }, labels)
            && observedShare == JsonSerializer.Serialize(manifest.FamilyRunShares.Families.Single(f => f.FamilyUserId == 11))
            && observedCount == JsonSerializer.Serialize(manifest.FamilyProteinCounts.Families.Single(f => f.FamilyUserId == 11)));
        Check("zero-signal parts and rankings retain the stored composition family universe", manifest.FamilySubclassParts.Families.Single().FamilySignal == 0
            && manifest.FamilyDomainParts.Domains.Single().FamilySignal == 0 && manifest.FamilyDomainParts.Products.Single().FamilySignal == 0
            && manifest.FamilyPeptideLocalization.Families.Single().Proteins.Count == 0);
        string stored = JsonSerializer.Serialize(manifest);
        FamilyUnobservedStudies.Store(file, manifest);
        Check("storing explicit zero rows is idempotent", stored == JsonSerializer.Serialize(manifest));
        string path = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.Unobserved", "stored.fqproj");
        ProjectStore.WriteFqproj(path, file, rows, manifest);
        var reopened = ProjectStore.ReadFqproj(path);
        Check("no-peptide rows survive native project save and reopen", reopened.manifest.FamilyRunShares!.Families.Single(f => f.FamilyUserId == 33).NoPeptideObserved
            && reopened.manifest.FamilyRunShares.Families.Single(f => f.FamilyUserId == 33).MeanShare == 0
            && reopened.manifest.FamilyProteinCounts!.Families.Single(f => f.FamilyUserId == 33).GreedyProteinCount == 0);
        var empty = FamilyEnvelopeQuant.AttributePeptides(Array.Empty<NativePeptideQuant>(), new PeptideFamilyAttribution());
        var noDenominator = new QuantManifest { FamilyRunShares = FamilyCompositionStudy.BuildRunShares(empty, new[] { "empty" }),
            FamilyProteinCounts = FamilyCompositionStudy.BuildProteinCounts(empty, file.MyExtendedFastaItems) };
        noDenominator.FamilyRunShares.MembershipSha256 = noDenominator.FamilyProteinCounts.MembershipSha256 = FamilyStudyMembership.Fingerprint(file);
        FamilyUnobservedStudies.Store(file, noDenominator);
        Check("an entirely empty assigned denominator is never converted to a zero share", noDenominator.FamilyRunShares.Families.All(f => f.NoPeptideObserved
            && f.MeanShare == null && f.StandardDeviation == null && f.PooledShare == null && f.Runs.Single().ShareOfAssignedSignal == null));
        var sharedMap = new PeptideFamilyAttribution();
        sharedMap.IdentifiedBySequence["AAAAAAAK"] = new[] { 11, 33 };
        var shared = FamilyCompositionStudy.BuildRunShares(FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), sharedMap));
        shared.MembershipSha256 = FamilyStudyMembership.Fingerprint(file);
        Check("a shared peptide receiving zero unique-policy credit is still an observed peptide", FamilyUnobservedStudies.Read(file,
            new QuantManifest { FamilyRunShares = shared }).Shares.All(f => !f.NoPeptideObserved));
    }
}
