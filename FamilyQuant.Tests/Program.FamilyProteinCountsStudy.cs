using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Globalization;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyProteinCountsStudyCommands() =>
    [
        new("--test-family-protein-counts", 367, TestCommandPhase.Raw,
            args => args.Contains("--test-family-protein-counts", StringComparer.Ordinal),
            (args, workDir) =>
            {
                FamilyProteinCountsStudyTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static FamilyPeptideAttribution ProteinCountAssignments(IEnumerable<string> peptides)
    {
        var map = new PeptideFamilyAttribution();
        var rows = peptides.Select((p, i) => Qr("identified", "r", i + 1, 2, p, 1, 1)).ToList();
        foreach (var row in rows) map.IdentifiedBySequence[FamilyQuant.DeNovo.DeNovoReconciler.NormalizeSequence(row.Sequence)] = new[] { 1 };
        return FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), map);
    }

    private static void FamilyProteinCountsStudyTests()
    {
        var peptides = new[] { "AAAAAAK", "CCCCCCK", "DDDDDDK", "EEEEEEK", "FFFFFFK", "VVVVVVK" };
        FamilyPeptideAttribution attribution = ProteinCountAssignments(peptides);
        FastaItemExtended Protein(string id, params int[] indices) => new()
        {
            SequenceIdentifier = id, UserID = 1, Sequence = string.Join("R", indices.Select(i => peptides[i])),
        };
        var database = new List<FastaItemExtended>
        {
            Protein("P1", 0, 1), Protein("P2", 2, 3), Protein("P3", 0, 2), Protein("P4", 1, 3), Protein("P5", 4),
            Protein(SequenceLabels.DecoyPrefix + "test", 4),
        };
        FamilyProteinCountsResult result = FamilyCompositionStudy.BuildProteinCounts(attribution, database);
        var family = result.Families.FirstOrDefault(f => f.FamilyUserId == 1);
        Check("greedy cover finds the known three-protein minimal explanation in the fixture", family?.GreedyProteinCount == 3
            && family.SelectedProteinIdentifiers.SequenceEqual(new[] { "P1", "P2", "P5" }));
        Check("own-peptide support counts proteins separately from abundance and cover size",
            family?.UniquePeptideProteinCount == 1 && family.ObservedPeptideCount == 6);
        var contaminantCompetition = FamilyCompositionStudy.BuildProteinCounts(attribution,
            database.Append(Protein(SequenceLabels.ContaminantPrefix + "competitor", 0, 1, 2, 3, 4, 5))).Families.FirstOrDefault();
        Check("contaminants compete against own-peptide specificity without explaining a biological family",
            contaminantCompetition?.UniquePeptideProteinCount == 0 && contaminantCompetition.GreedyProteinCount == 3
            && contaminantCompetition.UnexplainedPeptides.SequenceEqual(new[] { "VVVVVVK" }));
        Check("a peptide without exact database support remains explicitly unexplained",
            family?.UnexplainedPeptides.SequenceEqual(new[] { "VVVVVVK" }) == true
            && !family.ExplainsAllObservedPeptides && family.ExplainedPeptideCount == 5);
        Check("cover tie-breaking and output are independent of database input order",
            JsonSerializer.Serialize(result) == JsonSerializer.Serialize(FamilyCompositionStudy.BuildProteinCounts(attribution, database.AsEnumerable().Reverse())));
        database.Add(new() { SequenceIdentifier = "other-family", UserID = 2, Sequence = peptides[4] });
        Check("a peptide of its own means unique across all database targets, including other families",
            FamilyCompositionStudy.BuildProteinCounts(attribution, database).Families.FirstOrDefault()?.UniquePeptideProteinCount == 0);
        Check("repeated target identifiers are refused rather than counted as distinguishable proteins", Throws<ArgumentException>(() =>
            FamilyCompositionStudy.BuildProteinCounts(attribution, database.Append(database[0]))));

        var approximatePeptides = new[] { "AAAAAAK", "CCCCCCK", "DDDDDDK", "EEEEEEK", "FFFFFFK", "GGGGGGK" };
        FastaItemExtended Approx(string id, params int[] indices) => new()
        {
            SequenceIdentifier = id, UserID = 1, Sequence = string.Join("R", indices.Select(i => approximatePeptides[i])),
        };
        var approximate = FamilyCompositionStudy.BuildProteinCounts(ProteinCountAssignments(approximatePeptides), new[]
        {
            Approx("A", 0, 1, 2, 3), Approx("B", 0, 1, 4), Approx("C", 2, 3, 5), Approx("D", 4), Approx("E", 5),
        });
        Check("the declared greedy estimate does not claim an exact optimum on a counterexample",
            approximate.Method.Contains("greedy", StringComparison.OrdinalIgnoreCase)
            && approximate.Families.FirstOrDefault()?.SelectedProteinIdentifiers.SequenceEqual(new[] { "A", "B", "C" }) == true);
        var folded = FamilyCompositionStudy.BuildProteinCounts(ProteinCountAssignments(new[] { "ILILILK", "I(+15.99)LILILK" }),
            new[] { new FastaItemExtended { SequenceIdentifier = "folded", UserID = 1, Sequence = "LILLLLK" } });
        Check("protein support collapses modifications and I/L ambiguity to one peptide sequence",
            folded.Families.FirstOrDefault()?.ObservedPeptideCount == 1
            && folded.Families[0].GreedyProteinCount == 1 && folded.Families[0].UniquePeptideProteinCount == 1);
        var missing = FamilyCompositionStudy.BuildProteinCounts(attribution, Array.Empty<FastaItemExtended>());
        Check("an empty database invents no proteins", missing.Families.FirstOrDefault()?.GreedyProteinCount == 0
            && missing.Families[0].UnexplainedPeptides.Count == 6);
        var suffix = FamilyCompositionStudy.BuildProteinCounts(ProteinCountAssignments(new[] { "AAAAAAK", "AAAAK" }),
            new[] { new FastaItemExtended { SequenceIdentifier = "repeated", UserID = 1, Sequence = "AAAAAAKRAAAAAAK" } });
        Check("suffix matches and repeated positions count distinct peptides and proteins once",
            suffix.Families.FirstOrDefault()?.ObservedPeptideCount == 2
            && suffix.Families[0].GreedyProteinCount == 1 && suffix.Families[0].UniquePeptideProteinCount == 1);
        var manifest = new QuantManifest();
        FamilyCompositionStudy.StoreProteinCounts(manifest, attribution, database);
        string json = JsonSerializer.Serialize(manifest.FamilyProteinCounts);
        string scratch = Path.Combine(Path.GetTempPath(), "FamilyQuant.CompositionStudy");
        Directory.CreateDirectory(scratch);
        string path = Path.Combine(scratch, "proteins.fqproj");
        ProjectStore.WriteFqproj(path, new FamilyClusterFile(), Array.Empty<QuantRow>(), manifest);
        Check("protein cover, unexplained peptides and own-peptide counts survive project persistence", json ==
            JsonSerializer.Serialize(ProjectStore.ReadFqproj(path).manifest.FamilyProteinCounts));
        var prior = manifest.FamilyProteinCounts;
        Check("a failed protein explanation leaves a previous study intact", Throws<ArgumentException>(() =>
            FamilyCompositionStudy.StoreProteinCounts(manifest, attribution, database.Append(database[0])))
            && ReferenceEquals(manifest.FamilyProteinCounts, prior));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("protein explanation results repeat across cultures", json == JsonSerializer.Serialize(
                FamilyCompositionStudy.BuildProteinCounts(attribution, database)));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
