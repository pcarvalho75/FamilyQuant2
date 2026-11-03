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
    private static IEnumerable<TestCommand> FamilyRunSharesStudyCommands() =>
    [
        new("--test-family-run-shares", 366, TestCommandPhase.Raw,
            args => args.Contains("--test-family-run-shares", StringComparer.Ordinal),
            (args, workDir) =>
            {
                FamilyRunSharesStudyTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void FamilyRunSharesStudyTests()
    {
        var map = new PeptideFamilyAttribution();
        map.IdentifiedBySequence["AAAAAAAK"] = new[] { 1 };
        map.IdentifiedBySequence["CCCCCCCK"] = new[] { 2 };
        map.IdentifiedBySequence["DDDDDDDK"] = new[] { 1, 2 };
        var rows = new List<QuantRow>
        {
            Qr("identified", @"C:\raw\RunB.raw", 1, 2, "AAAAAAAK", 3, 3),
            Qr("identified", "RunA.raw.sqt", 2, 2, "AAAAAAAK", 1, 1),
            Qr("identified", "runa", 3, 2, "CCCCCCCK", 3, 3),
            Qr("identified", "runb", 4, 2, "CCCCCCCK", 1, 1),
            Qr("identified", "runa", 5, 2, "DDDDDDDK", 4, 4),
            Qr("identified", "runb", 6, 2, "EEEEEEEK", 4, 4),
        };
        FamilyPeptideAttribution attribution = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), map);
        string before = JsonSerializer.Serialize(attribution);
        FamilyRunSharesResult result = FamilyCompositionStudy.BuildRunShares(attribution,
            new[] { "RunB.RAW", "RunA.raw", "Empty.raw" });
        Check("run shares match normalized file names without relying on row positions", result.Runs.Count == 3
            && result.Runs.Count(r => r.AssignedSignal == 4 && r.MeasuredSignal == 8) == 2);
        Check("a declared zero-signal acquisition remains present with unavailable shares",
            result.Runs.Any(r => r.Run == "Empty" && r.AssignedSignal == 0)
            && result.Families.Count == 2 && result.Families.All(f => f.Runs.Any(r => r.Run == "Empty" && r.ShareOfAssignedSignal == null)));
        FamilyRunShareSummary? first = result.Families.FirstOrDefault(f => f.FamilyUserId == 1);
        Check("run statistics use unweighted run shares and sample SD", first?.AvailableRunCount == 2
            && first.MeanShare == 0.5 && Math.Abs(first.StandardDeviation.GetValueOrDefault() - Math.Sqrt(0.125)) < 1e-15
            && Math.Abs(first.CoefficientOfVariation.GetValueOrDefault() - Math.Sqrt(0.5)) < 1e-15);
        Check("run parts sum to assigned family signal", result.Runs.Count == 3 && result.Runs.All(r =>
            result.Families.Sum(f => f.Runs.Single(v => v.Run == r.Run).Signal) == r.AssignedSignal));
        Check("assigned shares sum to one for every measurable run", result.Runs.Count == 3
            && result.Runs.Where(r => r.AssignedSignal > 0).All(r =>
                result.Families.Sum(f => f.Runs.Single(v => v.Run == r.Run).ShareOfAssignedSignal) == 1));
        Check("the measured-signal share remains separate from normalized family composition",
            first?.Runs.Any(r => r.ShareOfAssignedSignal == 0.75 && r.ShareOfMeasuredSignal == 0.375) == true);
        FamilyRunSharesResult split = FamilyCompositionStudy.BuildRunShares(attribution, policy: SharedPeptidePolicy.SplitEqually);
        Check("per-run shares honor the existing split-equally policy",
            split.Runs.Any(r => r.AssignedSignal == 8) && split.Runs.Any(r => r.AssignedSignal == 4));
        Check("overlapping full counts are refused as a normalized composition", Throws<ArgumentException>(() =>
            FamilyCompositionStudy.BuildRunShares(attribution, policy: SharedPeptidePolicy.CountInEveryFamily)));
        FamilyRunSharesResult one = FamilyCompositionStudy.BuildRunShares(FamilyEnvelopeQuant.AttributePeptides(
            FamilyEnvelopeQuant.PreparePeptides(rows.Take(1)), map));
        Check("one run has a mean but no sample SD or CV", one.Families.Count == 1
            && one.Families[0].MeanShare == 1 && one.Families[0].StandardDeviation == null
            && one.Families[0].CoefficientOfVariation == null);
        Check("run shares only read the existing peptide attribution", before == JsonSerializer.Serialize(attribution));
        var manifest = new QuantManifest { PerRunTic = new() { ["RUNA.RAW"] = 16 } };
        FamilyCompositionStudy.StoreRunShares(manifest, attribution);
        Check("recorded TIC shares are separate and match the run by filename",
            manifest.FamilyRunShares?.Families.Single(f => f.FamilyUserId == 1).Runs
                .Any(r => r.ShareOfAssignedSignal == 0.25 && r.ShareOfMeasuredSignal == 0.125 && r.ShareOfTic == 0.0625) == true);
        Check("a missing TIC is unavailable rather than substituted with peptide signal",
            manifest.FamilyRunShares?.Families.All(f => f.Runs.Count(r => r.ShareOfTic == null) == 1) == true);
        var measured = FamilyCompositionStudy.BuildRunShares(attribution).Families.Single(f => f.FamilyUserId == 1);
        Check("measured-signal statistics use their own run shares and sample SD",
            measured.MeanShareOfMeasuredSignal == 0.25
            && Math.Abs(measured.StandardDeviationOfMeasuredSignal.GetValueOrDefault() - Math.Sqrt(0.03125)) < 1e-15);
        var completeTic = FamilyCompositionStudy.BuildRunShares(attribution,
            perRunTic: new Dictionary<string, double> { ["RUNA.RAW"] = 16, ["runb.raw"] = 8 }).Families.Single(f => f.FamilyUserId == 1);
        Check("TIC statistics use complete per-file TIC shares and their sample SD",
            completeTic.MeanShareOfTic == 0.21875
            && Math.Abs(completeTic.StandardDeviationOfTic.GetValueOrDefault() - Math.Sqrt(0.048828125)) < 1e-15);
        Check("secondary means and SDs are unavailable if any run lacks that share",
            result.Families.All(f => f.MeanShareOfMeasuredSignal == null && f.StandardDeviationOfMeasuredSignal == null)
            && manifest.FamilyRunShares!.Families.All(f => f.MeanShareOfTic == null && f.StandardDeviationOfTic == null));
        Check("one measured run has a secondary mean but no sample SD",
            one.Families[0].MeanShareOfMeasuredSignal == 1 && one.Families[0].StandardDeviationOfMeasuredSignal == null);
        var unequal = new[]
        {
            Qr("identified", "A", 1, 2, "AAAAAAAK", 1, 1), Qr("identified", "A", 2, 2, "CCCCCCCK", 99, 99),
            Qr("identified", "B", 3, 2, "AAAAAAAK", 1, 1), Qr("identified", "B", 4, 2, "CCCCCCCK", 1, 1),
        };
        var unequalResult = FamilyCompositionStudy.BuildRunShares(FamilyEnvelopeQuant.AttributePeptides(
            FamilyEnvelopeQuant.PreparePeptides(unequal), map));
        var unequalFirst = unequalResult.Families.Single(f => f.FamilyUserId == 1);
        Check("pooled share and its difference from the per-run mean are explicit",
            unequalFirst.PooledShare == 2.0 / 102 && unequalFirst.MeanShare == 0.255
            && unequalFirst.PooledMinusMeanShare == unequalFirst.PooledShare - unequalFirst.MeanShare);
        map.IdentifiedBySequence["FFFFFFFK"] = new[] { 1, 3 };
        var zeroResult = FamilyCompositionStudy.BuildRunShares(FamilyEnvelopeQuant.AttributePeptides(
            FamilyEnvelopeQuant.PreparePeptides(unequal.Append(Qr("identified", "A", 5, 2, "FFFFFFFK", 5, 5))), map));
        var zero = zeroResult.Families.Single(f => f.FamilyUserId == 3);
        Check("an absent family is zero in measured runs with mean zero and undefined CV",
            zero.Runs.All(r => r.Signal == 0 && r.ShareOfAssignedSignal == 0)
            && zero.MeanShare == 0 && zero.StandardDeviation == 0 && zero.CoefficientOfVariation == null);
        string json = JsonSerializer.Serialize(manifest.FamilyRunShares);
        string scratch = Path.Combine(Path.GetTempPath(), "FamilyQuant.CompositionStudy");
        Directory.CreateDirectory(scratch);
        string path = Path.Combine(scratch, "runs.fqproj");
        ProjectStore.WriteFqproj(path, new FamilyClusterFile(), rows, manifest);
        Check("per-run statistics and TIC survive project persistence", json ==
            JsonSerializer.Serialize(ProjectStore.ReadFqproj(path).manifest.FamilyRunShares));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("per-run results repeat across cultures", json == JsonSerializer.Serialize(
                FamilyCompositionStudy.BuildRunShares(attribution, manifest.PerRunTic.Keys, perRunTic: manifest.PerRunTic)));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
