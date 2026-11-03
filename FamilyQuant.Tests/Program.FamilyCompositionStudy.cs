using FamilyQuant.DeNovo;
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
    private static IEnumerable<TestCommand> FamilyCompositionStudyCommands() =>
    [
        new("--test-family-composition-study", 365, TestCommandPhase.Raw,
            args => args.Contains("--test-family-composition-study", StringComparer.Ordinal),
            (args, workDir) =>
            {
                FamilySubclassStudyTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void FamilySubclassStudyTests()
    {
        FamilySubclassNativeStudyTests();
        var rows = new List<QuantRow>
        {
            Qr("identified", "r1", 1, 2, "AAAAAAAK", 2, 2),
            Qr("identified", "r1", 2, 2, "CCCCCCCK", 3, 3),
            Qr("identified", "r1", 3, 2, "DDDDDDDK", 5, 5),
            Qr("identified", "r1", 4, 2, "EEEEEEEK", 7, 7),
            Qr("identified", "r1", 5, 2, "FFFFFFFK", 11, 11),
            Qr("denovo", "r2", 6, 2, "GGGGGGGK", 13, 13),
        };
        var map = new PeptideFamilyAttribution();
        foreach (QuantRow row in rows.Where(r => r.Side == "identified"))
            map.IdentifiedBySequence[row.Sequence] = row.Sequence == "FFFFFFFK" ? new[] { 1, 2 } : new[] { 1 };
        map.DeNovoBySequence["GGGGGGGK"] = new[] { 1 };
        var labels = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal)
        {
            ["AAAAAAAK"] = new string?[] { "alpha", "alpha" },
            ["CCCCCCCK"] = new string?[] { "alpha", "beta" },
            ["DDDDDDDK"] = new string?[] { "alpha", null },
            ["EEEEEEEK"] = Array.Empty<string?>(),
            ["FFFFFFFK"] = new string?[] { "alpha" },
            ["GGGGGGGK"] = new string?[] { "beta" },
        };
        FamilyPeptideAttribution attribution = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), map);
        string original = JsonSerializer.Serialize(attribution);
        foreach (SharedPeptidePolicy policy in Enum.GetValues<SharedPeptidePolicy>())
        {
            FamilySubclassPartsResult result = FamilyCompositionStudy.BuildSubclasses(attribution,
                (side, sequence, family) => labels[sequence], policy);
            FamilyEnvelopeQuantResult totals = FamilyEnvelopeQuant.BuildFromAttributedPeptides(attribution);
            Check($"subclass study retains all families under {policy}", result.Families.Count == totals.Families.Count);
            foreach (FamilyEnvelopeQuantRow family in totals.Families)
            {
                FamilySignalBreakdown? split = result.Families.FirstOrDefault(f => f.FamilyUserId == family.FamilyUserId);
                Check($"subclass parts close bit for bit under {policy}, family {family.FamilyUserId}",
                    split != null && split.FamilySignal == family.Signal(policy)
                    && split.Parts.Sum(p => p.Signal) == family.Signal(policy)
                    && split.Parts.All(p => p.Signal >= 0 && double.IsFinite(p.Signal)));
                Check($"subclass study retains explicit not resolved under {policy}, family {family.FamilyUserId}",
                    split?.Parts.Count(p => p.IsUnresolved && p.PartKey == null) == 1);
            }
        }
        FamilySubclassPartsResult unique = FamilyCompositionStudy.BuildSubclasses(attribution,
            (side, sequence, family) => labels[sequence]);
        FamilySignalBreakdown? first = unique.Families.FirstOrDefault(f => f.FamilyUserId == 1);
        Check("only a subclass unique across all candidate proteins receives its peptide",
            first?.Parts.SingleOrDefault(p => p.PartKey == "alpha")?.Signal == 2);
        Check("cross-subclass, missing-label and missing-protein evidence stays unresolved",
            first?.Parts.SingleOrDefault(p => p.IsUnresolved)?.Signal == 15);
        Check("subclass study keeps calibrated de novo signal on the existing primary basis",
            first?.Parts.SingleOrDefault(p => p.PartKey == "beta")?.Signal
                == attribution.Peptides.Single(p => p.Peptide.Sequence == "GGGGGGGK").Peptide.ReferenceCalibratedEnvelope);
        Check("subclass study only reads existing assignments and prepared values", original == JsonSerializer.Serialize(attribution));
        Check("an empty subclass study is computed-empty", FamilyCompositionStudy.BuildSubclasses(
            FamilyEnvelopeQuant.AttributePeptides(Array.Empty<NativePeptideQuant>(), map), (_, _, _) => Array.Empty<string?>()).Families.Count == 0);
        string json = JsonSerializer.Serialize(unique);
        CultureInfo culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("subclass output is deterministic across cultures", json == JsonSerializer.Serialize(
                FamilyCompositionStudy.BuildSubclasses(attribution, (side, sequence, family) => labels[sequence])));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Check("never-computed composition studies are omitted from manifest JSON",
            !JsonSerializer.Serialize(new QuantManifest()).Contains("FamilySubclassParts", StringComparison.Ordinal));
        string scratch = Path.Combine(Path.GetTempPath(), "FamilyQuant.CompositionStudy");
        Directory.CreateDirectory(scratch);
        string path = Path.Combine(scratch, "subclasses.fqproj");
        ProjectStore.WriteFqproj(path, new FamilyClusterFile(), rows,
            new QuantManifest { FamilySubclassParts = unique });
        QuantManifest? reopened = ProjectStore.ReadFqproj(path).manifest;
        Check("subclass parts survive a real project round trip", reopened?.FamilySubclassParts != null
            && JsonSerializer.Serialize(reopened.FamilySubclassParts) == json);
        var preserved = new QuantManifest { FamilySubclassParts = unique };
        Check("failed subclass calculation leaves a prior stored study intact", Throws<InvalidOperationException>(() =>
            FamilyCompositionStudy.StoreSubclasses(preserved, attribution, (_, _, _) => throw new InvalidOperationException("fixture")))
            && ReferenceEquals(preserved.FamilySubclassParts, unique));
        var random = new Random(4301);
        bool exact = true;
        for (int trial = 0; trial < 25; trial++)
        {
            var stressMap = new PeptideFamilyAttribution();
            var stressRows = "ACDEFGHV".Select((letter, i) =>
            {
                string sequence = new string(letter, 7) + "K";
                stressMap.IdentifiedBySequence[sequence] = new[] { 1 };
                double area = Math.Pow(10, random.Next(-8, 17)) * (0.1 + random.NextDouble());
                return Qr("identified", "r", i + 1, 2, sequence, area, area);
            }).ToList();
            var stress = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(stressRows), stressMap);
            var split = FamilyCompositionStudy.BuildSubclasses(stress, (_, sequence, _) => new[] { (sequence[0] % 3).ToString(CultureInfo.InvariantCulture) }).Families.Single();
            exact &= split.Parts.Sum(p => p.Signal) == FamilyEnvelopeQuant.BuildFromAttributedPeptides(stress).Families.Single().Signal(SharedPeptidePolicy.FamilyUnique)
                && split.Parts.All(p => p.Signal >= 0);
        }
        Check("subclass parts close bit for bit across wide signal ranges", exact);
    }
}
