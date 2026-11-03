using FamilyQuant.DeNovo;
using FamilyQuant.Evidence.Positions;
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
    private static IEnumerable<TestCommand> FamilyPositionStudyCommands() =>
    [
        new("--test-family-position-studies", 368, TestCommandPhase.Raw,
            args => args.Contains("--test-family-position-studies", StringComparer.Ordinal),
            (args, workDir) =>
            {
                FamilyPositionStudyTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static FamilyPeptideAttribution PositionAssignments(params (string Sequence, double Signal)[] rows)
    {
        var map = new PeptideFamilyAttribution();
        var quants = rows.Select((p, i) => Qr("identified", "r", i + 1, 2, p.Sequence, p.Signal, p.Signal)).ToList();
        foreach (var quant in quants) map.IdentifiedBySequence[DeNovoReconciler.NormalizeSequence(quant.Sequence)] = new[] { 1 };
        return FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(quants), map);
    }

    private static void FamilyPositionStudyTests()
    {
        const string protein = "AAAAAKCCCCCKDDDDDK";
        var positions = new ProteinPositionEvidence
        {
            SequenceLength = protein.Length, SequenceSha256 = ProteinPositionFetcher.SequenceHash(protein),
            DomainSource = new() { Provider = "fixture" }, FeatureSource = new() { Provider = "fixture" },
            Pfam = new()
            {
                new() { Accession = "D1", Name = "domain one", Start = 1, End = 6 },
                new() { Accession = "D2", Name = "domain two", Start = 7, End = 12 },
            },
            Features = new()
            {
                new() { Type = "Chain", Description = "parent", Start = 1, End = 18, StartModifier = "EXACT", EndModifier = "EXACT" },
                new() { Type = "Peptide", Description = "product one", Start = 1, End = 6, StartModifier = "EXACT", EndModifier = "EXACT" },
                new() { Type = "Peptide", Description = "product two", Start = 7, End = 12, StartModifier = "EXACT", EndModifier = "EXACT" },
            },
        };
        PeptideProteinPlacement Place(string sequence, ProteinPositionEvidence? evidence = null, string identifier = "P") => new()
        {
            ProteinIdentifier = identifier, ProteinSequence = protein, Positions = evidence ?? positions,
            MatchKind = PeptideLocalizationMatchKind.ExactContainment,
            Intervals = new() { new() { Start = protein.IndexOf(sequence, StringComparison.Ordinal) + 1, End = protein.IndexOf(sequence, StringComparison.Ordinal) + sequence.Length } },
        };
        var attribution = PositionAssignments(("AAAAAK", 2), ("CCCCCK", 3), ("DDDDDK", 5));
        string evidenceBefore = JsonSerializer.Serialize(positions);
        string inputBefore = JsonSerializer.Serialize(attribution);
        FamilyPositionStudiesResult result = FamilyCompositionStudy.BuildPositions(attribution, (_, sequence, _) => new[] { Place(sequence) });
        var domain = result.DomainParts.Domains.FirstOrDefault();
        Check("domain signal and confirmed outside signal partition the pooled family",
            domain?.Parts.SingleOrDefault(p => p.PartKey == "D1")?.Signal == 2
            && domain.Parts.Single(p => p.PartKey == "D2").Signal == 3
            && domain.Parts.Single(p => p.PartKey == FamilyCompositionStudy.OutsidePositionKey).Signal == 5
            && domain.Parts.Sum(p => p.Signal) == domain.FamilySignal && domain.FamilySignal == 10);
        var products = result.DomainParts.Products.FirstOrDefault();
        Check("named products use the narrowest containing feature rather than also counting its parent",
            products?.Parts.SingleOrDefault(p => p.Label.Contains("product one", StringComparison.Ordinal))?.Signal == 2
            && products.Parts.Single(p => p.Label.Contains("product two", StringComparison.Ordinal)).Signal == 3
            && products.Parts.Single(p => p.Label.Contains("parent", StringComparison.Ordinal)).Signal == 5
            && products.Parts.Sum(p => p.Signal) == 10);
        Check("localized segments retain evidence side, exact kind and real residue intervals",
            result.Localization.Segments.Count == 3 && result.Localization.Segments.All(p => p.Side == "identified"
                && p.MatchKind == PeptideLocalizationMatchKind.ExactContainment && p.Intervals.Count == 1)
            && result.Localization.Segments.Sum(p => p.Signal) == 10);
        Check("protein ranking retains the source sequence identity and allocated signal",
            result.Localization.Families.FirstOrDefault()?.Proteins.FirstOrDefault() is { Rank: 1, CreditedSignal: 10, ProteinLength: 18, PositionEvidenceVerified: true } ranked
            && ranked.ProteinSequenceSha256 == positions.SequenceSha256 && ranked.ExactSignal == 10 && ranked.AlignedSignal == 0);
        Check("position studies never mutate quantitation or source evidence",
            inputBefore == JsonSerializer.Serialize(attribution) && evidenceBefore == JsonSerializer.Serialize(positions));

        var boundary = FamilyCompositionStudy.BuildPositions(PositionAssignments(("AAAKCCC", 7)), (_, sequence, _) => new[] { Place(sequence) });
        Check("boundary-spanning signal is apportioned by aligned residue overlap",
            boundary.DomainParts.Domains.FirstOrDefault()?.Parts.SingleOrDefault(p => p.PartKey == "D1")?.Signal == 4
            && boundary.DomainParts.Domains[0].Parts.Single(p => p.PartKey == "D2").Signal == 3);
        var discontinuous = JsonSerializer.Deserialize<ProteinPositionEvidence>(evidenceBefore)!;
        discontinuous.Pfam[0].Fragments = new() { new() { Start = 1, End = 2 }, new() { Start = 5, End = 6 } };
        var fragment = FamilyCompositionStudy.BuildPositions(PositionAssignments(("AAAKCCC", 7)), (_, sequence, _) => new[] { Place(sequence, discontinuous) });
        Check("domain fragment gaps are not treated as covered domains",
            fragment.DomainParts.Domains.FirstOrDefault()?.Parts.SingleOrDefault(p => p.PartKey == "D1")?.Signal == 2
            && fragment.DomainParts.Domains[0].Parts.Single(p => p.PartKey == FamilyCompositionStudy.OutsidePositionKey).Signal == 2);
        var wrong = JsonSerializer.Deserialize<ProteinPositionEvidence>(evidenceBefore)!;
        wrong.SequenceSha256 = "wrong sequence";
        var mismatch = FamilyCompositionStudy.BuildPositions(attribution, (_, sequence, _) => new[] { Place(sequence, wrong) });
        Check("same length with a different sequence hash cannot authorize domain positions",
            mismatch.DomainParts.Domains.FirstOrDefault()?.Parts.SingleOrDefault(p => p.IsUnresolved)?.Signal == 10
            && mismatch.Localization.Families.FirstOrDefault()?.Proteins.All(p => !p.PositionEvidenceVerified) == true);
        var unknownFeature = JsonSerializer.Deserialize<ProteinPositionEvidence>(evidenceBefore)!;
        unknownFeature.Features.Add(new() { Type = "Peptide", Description = "unknown boundary", Start = null, End = 6, StartModifier = "UNKNOWN" });
        var uncertain = FamilyCompositionStudy.BuildPositions(attribution, (_, sequence, _) => new[] { Place(sequence, unknownFeature) });
        Check("unknown feature boundaries stay unresolved rather than becoming outside products",
            uncertain.DomainParts.Products.FirstOrDefault()?.Parts.SingleOrDefault(p => p.IsUnresolved)?.Signal == 10);

        var gapEvidence = JsonSerializer.Deserialize<ProteinPositionEvidence>(evidenceBefore)!;
        gapEvidence.Pfam = new() { new() { Accession = "covered", Start = 1, End = 3 }, new() { Accession = "gap", Start = 4, End = 6 } };
        var gapPlacement = new PeptideProteinPlacement
        {
            ProteinIdentifier = "P", ProteinSequence = protein, Positions = gapEvidence, MatchKind = PeptideLocalizationMatchKind.Aligned,
            Intervals = new() { new() { Start = 1, End = 3 }, new() { Start = 7, End = 9 } },
        };
        var gap = FamilyCompositionStudy.BuildPositions(PositionAssignments(("AAAAAA", 6)), (_, _, _) => new[] { gapPlacement });
        Check("gapped alignment localization never covers the gap in its bounding span",
            gap.Localization.Segments.FirstOrDefault()?.Intervals.Count == 2
            && gap.Localization.Segments[0].MatchKind == PeptideLocalizationMatchKind.Aligned
            && gap.DomainParts.Domains.FirstOrDefault()?.Parts.SingleOrDefault(p => p.PartKey == "covered")?.Signal == 3
            && !gap.DomainParts.Domains[0].Parts.Any(p => p.PartKey == "gap" && p.Signal > 0));
        gapPlacement.MatchKind = PeptideLocalizationMatchKind.ExactContainment;
        var falseExact = FamilyCompositionStudy.BuildPositions(PositionAssignments(("AAAAAA", 6)), (_, _, _) => new[] { gapPlacement });
        Check("homology cannot be mislabeled exact containment", falseExact.Localization.Segments.Count == 0
            && falseExact.Localization.Families.FirstOrDefault()?.UnlocalizedSignal == 6);

        var other = JsonSerializer.Deserialize<ProteinPositionEvidence>(evidenceBefore)!;
        other.Pfam[0].Accession = "different domain";
        var candidates = new[] { Place("AAAAAK", positions, "A"), Place("AAAAAK", other, "B") };
        var ambiguous = FamilyCompositionStudy.BuildPositions(PositionAssignments(("AAAAAK", 10)), (_, _, _) => candidates);
        Check("incompatible candidate domains remain unresolved without assuming equal protein abundance",
            ambiguous.DomainParts.Domains.FirstOrDefault()?.Parts.SingleOrDefault(p => p.IsUnresolved)?.Signal == 10);
        Check("protein display ranking allocates ambiguous signal once with deterministic identifier ties",
            ambiguous.Localization.Families.FirstOrDefault()?.Proteins.Select(p => p.ProteinIdentifier).SequenceEqual(new[] { "A", "B" }) == true
            && ambiguous.Localization.Families[0].Proteins.All(p => p.CreditedSignal == 5)
            && ambiguous.Localization.Segments.Sum(p => p.Signal) == 10);
        Check("position results are independent of candidate input order", JsonSerializer.Serialize(ambiguous)
            == JsonSerializer.Serialize(FamilyCompositionStudy.BuildPositions(PositionAssignments(("AAAAAK", 10)), (_, _, _) => candidates.Reverse().ToArray())));
        var runMap = new PeptideFamilyAttribution();
        runMap.IdentifiedBySequence["AAAAAK"] = new[] { 1 };
        var multi = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(new[]
        {
            Qr("identified", "r2", 1, 2, "AAAAAK", 2, 2), Qr("identified", "r1", 2, 2, "AAAAAK", 3, 3),
        }), runMap);
        var pooled = FamilyCompositionStudy.BuildPositions(multi, (_, sequence, _) => new[] { Place(sequence) });
        Check("positions pool repeated runs into the same segment without taking a run mean",
            pooled.Localization.Segments.Single().Signal == 5
            && pooled.DomainParts.Domains.Single().Parts.Single(p => p.PartKey == "D1").Signal == 5);
        var zeroRow = Qr("identified", "r", 1, 2, "AAAAAK", 0, 0);
        zeroRow.IsotopeAreas[0] = DeNovoXicExtractor.UnderSampled;
        zeroRow.UnderSampledArea = 2;
        var zeroAssigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(new[] { zeroRow }), runMap);
        var zeroSignal = FamilyCompositionStudy.BuildPositions(zeroAssigned, (_, sequence, _) => new[] { Place(sequence) });
        Check("localized under-sampled evidence survives with zero credited signal",
            zeroSignal.Localization.Segments.SingleOrDefault() is { Signal: 0, FamilyMembershipCount: 1 }
            && zeroSignal.Localization.Families.Single().Proteins.Single().CreditedSignal == 0);
        runMap.IdentifiedBySequence["AAAAAK"] = new[] { 1, 2 };
        var shared = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(new[] { Qr("identified", "r", 1, 2, "AAAAAK", 10, 10) }), runMap);
        var sharedCoverage = FamilyCompositionStudy.BuildPositions(shared, (_, sequence, family) =>
            new[] { Place(sequence, positions, "P" + family.ToString(CultureInfo.InvariantCulture)) });
        Check("shared coverage survives without silently receiving family-unique abundance credit",
            sharedCoverage.Localization.Segments.Count == 2
            && sharedCoverage.Localization.Segments.All(s => s.Signal == 0 && s.FamilyMembershipCount == 2)
            && sharedCoverage.Localization.Families.All(f => f.FamilySignal == 0 && f.Proteins.All(p => p.CreditedSignal == 0)));

        var manifest = new QuantManifest();
        FamilyCompositionStudy.StorePositions(manifest, attribution, (_, sequence, _) => new[] { Place(sequence) });
        string scratch = Path.Combine(Path.GetTempPath(), "FamilyQuant.CompositionStudy");
        Directory.CreateDirectory(scratch);
        string path = Path.Combine(scratch, "positions.fqproj");
        ProjectStore.WriteFqproj(path, new FamilyClusterFile(), Array.Empty<QuantRow>(), manifest);
        var reopened = ProjectStore.ReadFqproj(path).manifest;
        Check("domain parts and localizations survive an offline project round trip", manifest.FamilyDomainParts != null
            && JsonSerializer.Serialize(manifest.FamilyDomainParts) == JsonSerializer.Serialize(reopened.FamilyDomainParts)
            && JsonSerializer.Serialize(manifest.FamilyPeptideLocalization) == JsonSerializer.Serialize(reopened.FamilyPeptideLocalization));
        var priorDomain = manifest.FamilyDomainParts;
        var priorLocalization = manifest.FamilyPeptideLocalization;
        Check("failed position calculation retains both previously stored studies", Throws<InvalidOperationException>(() =>
            FamilyCompositionStudy.StorePositions(manifest, attribution, (_, _, _) => throw new InvalidOperationException("fixture")))
            && ReferenceEquals(priorDomain, manifest.FamilyDomainParts) && ReferenceEquals(priorLocalization, manifest.FamilyPeptideLocalization));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("position studies repeat across cultures", JsonSerializer.Serialize(result) == JsonSerializer.Serialize(
                FamilyCompositionStudy.BuildPositions(attribution, (_, sequence, _) => new[] { Place(sequence) })));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        string empty = JsonSerializer.Serialize(new QuantManifest());
        Check("never-computed position studies are absent from manifest JSON",
            !empty.Contains("FamilyDomainParts", StringComparison.Ordinal) && !empty.Contains("FamilyPeptideLocalization", StringComparison.Ordinal));
    }
}
