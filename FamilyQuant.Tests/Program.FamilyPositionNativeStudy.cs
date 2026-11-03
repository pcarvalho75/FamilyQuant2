using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyPositionNativeCommands() =>
    [
        new("--test-family-position-native", 369, TestCommandPhase.Raw,
            args => args.Contains("--test-family-position-native", StringComparer.Ordinal),
            (args, workDir) =>
            {
                FamilyPositionNativeTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void FamilyPositionNativeTests()
    {
        const string protein = "AAAAAAAKCCCCCCCK";
        var curation = new FamilyClusterFile
        {
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "P", UserID = 1, Sequence = protein, Evidence = new ProteinEvidence
                {
                    Positions = new ProteinPositionEvidence
                    {
                        SequenceLength = protein.Length, SequenceSha256 = ProteinPositionFetcher.SequenceHash(protein),
                        DomainSource = new() { Provider = "fixture" }, FeatureSource = new() { Provider = "fixture" },
                        Pfam = new() { new() { Accession = "first", Start = 1, End = 8 }, new() { Accession = "second", Start = 9, End = 16 } },
                    },
                } },
            },
        };
        var rows = new List<QuantRow>
        {
            Qr("identified", "r", 1, 2, "CCCCCCCK", 2, 2), Qr("denovo", "r", 2, 2, "AAAAAASK", 4, 4),
        };
        CurationHomologyHits hits = FamilyEnvelopeQuant.SearchCuration(rows, curation);
        var assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), FamilyEnvelopeQuant.AttributeHits(hits, curation));
        double total = FamilyEnvelopeQuant.BuildFromAttributedPeptides(assigned).Families.Single().Signal(SharedPeptidePolicy.FamilyUnique);
        Check("native localization fixture retains exact and homology-only family evidence",
            assigned.Peptides.Count == 2 && assigned.Peptides.All(p => p.FamilyUserIds.SequenceEqual(new[] { 1 })));
        var manifest = new QuantManifest();
        FamilyCompositionStudy.StorePositionsFromCuration(manifest, assigned, hits, curation);
        Check("native positions consume AS evidence and close to the original pooled family",
            manifest.FamilyDomainParts?.Domains.SingleOrDefault()?.Parts.Sum(p => p.Signal) == total
            && manifest.FamilyPeptideLocalization?.Families.SingleOrDefault()?.FamilySignal == total);
        Check("native de novo homology remains aligned rather than being presented as exact containment",
            manifest.FamilyPeptideLocalization?.Segments.Any(s => s.Side == "denovo") == true
            && manifest.FamilyPeptideLocalization.Segments.Where(s => s.Side == "denovo")
                .All(s => s.MatchKind == PeptideLocalizationMatchKind.Aligned && s.Intervals.Sum(i => i.End - i.Start + 1) == 8));
        Check("native exact identified placement uses actual containment coordinates",
            manifest.FamilyPeptideLocalization?.Segments.SingleOrDefault(s => s.Side == "identified") is { MatchKind: PeptideLocalizationMatchKind.ExactContainment } exact
            && exact.Intervals.Single().Start == 9 && exact.Intervals.Single().End == 16);
        curation.MyExtendedFastaItems[0].Evidence!.Positions = null;
        FamilyCompositionStudy.StorePositionsFromCuration(manifest, assigned, hits, curation);
        Check("missing fetched positions stay unresolved while protein localization remains inspectable",
            manifest.FamilyDomainParts?.Domains.SingleOrDefault()?.Parts.Single(p => p.IsUnresolved).Signal == total
            && manifest.FamilyPeptideLocalization?.Segments.Count == 2
            && manifest.FamilyPeptideLocalization.Families.Single().Proteins.All(p => !p.PositionEvidenceVerified));
    }
}
