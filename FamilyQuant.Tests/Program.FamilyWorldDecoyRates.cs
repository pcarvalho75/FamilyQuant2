using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyWorldRateCommands() =>
    [
        new("--test-family-world-rates", 374, TestCommandPhase.Raw,
            args => args.Contains("--test-family-world-rates", StringComparer.Ordinal), (_, _) =>
            {
                FamilyWorldRateTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void FamilyWorldRateTests()
    {
        Check("supported decoy rates retain the exact envelope ratio",
            FamilyWorldDecoyRates.Calculate(4, 1) == .25 && FamilyWorldDecoyRates.Calculate(1, 0) == 0
            && FamilyWorldDecoyRates.Calculate(1, 1) == 1);
        Check("there is no absolute signal threshold or family-specific cutoff",
            FamilyWorldDecoyRates.Calculate(1e-30, 2.5e-31) == .25);
        Check("decoy-dominated signal has no reported rate rather than a clamped 100 percent",
            FamilyWorldDecoyRates.Calculate(1, 20) == null
            && FamilyWorldDecoyRates.Status(1, 20, null) == FamilyWorldDecoyRates.DecoyExceedsCredited);
        Check("zero credited signal has its distinct explicit reason",
            FamilyWorldDecoyRates.Calculate(0, 1) == null
            && FamilyWorldDecoyRates.Status(0, 1, null) == FamilyWorldDecoyRates.NoCreditedSignal);
        Check("non-finite credited signal cannot manufacture a rate",
            FamilyWorldDecoyRates.Calculate(double.NaN, 1) == null && FamilyWorldDecoyRates.Calculate(double.PositiveInfinity, 1) == null
            && FamilyWorldDecoyRates.Status(double.NaN, 1, null) == FamilyWorldDecoyRates.NoCreditedSignal);
        var family = new FamilyWorldFamilySummary
        {
            Family = "owned fixture", CreditedEnvelope = 1, DecoyEnvelope = 20, Peptides = 3, DecoyPeptides = 7, DecoyRate = 20,
        };
        Check("legacy stored over-range family rates are unavailable without changing raw envelopes",
            family.DecoyRate == null && family.CreditedEnvelope == 1 && family.DecoyEnvelope == 20
            && family.DecoyRateStatus == FamilyWorldDecoyRates.DecoyExceedsCredited);
        string label = FamilyWorldDecoyRates.Format(FamilyWorldDecoyRates.ForFamily(family), CultureInfo.InvariantCulture);
        Check("unsupported rates retain the target and decoy peptide counts beside the reason",
            label.Contains(FamilyWorldDecoyRates.DecoyExceedsCredited, StringComparison.Ordinal)
            && label.Contains("target 3", StringComparison.Ordinal) && label.Contains("decoy 7", StringComparison.Ordinal));
        var composition = new FamilyWorldCompositionRow
        {
            Family = family.Family, WorldDeNovoEnvelope = 1, WorldDecoyRate = 20,
            WorldTargetPeptides = 3, WorldDecoyPeptides = 7,
        };
        var display = new DatabaseGapDisplayRow(composition, 10);
        Check("the native result row shows the unavailable reason and counts, never an over-100 percentage",
            display.DecoyRate.Contains(FamilyWorldDecoyRates.DecoyExceedsCredited, StringComparison.Ordinal)
            && display.DecoyRate.Contains("target 3", StringComparison.Ordinal) && display.DecoyRate.Contains("decoy 7", StringComparison.Ordinal)
            && !display.DecoyRate.Contains('%'));
        var legacy = JsonSerializer.Deserialize<FamilyWorldCompositionRow>("{\"Family\":\"legacy\",\"WorldDecoyRate\":20}")!;
        string legacyLabel = new DatabaseGapDisplayRow(legacy, 10).DecoyRate;
        Check("a legacy composition is protected at display time without inventing missing counts",
            legacy.WorldDecoyRate == null && legacyLabel.Contains(FamilyWorldDecoyRates.DecoyExceedsCredited, StringComparison.Ordinal)
            && legacyLabel.Contains("target not recorded", StringComparison.Ordinal) && legacyLabel.Contains("decoy not recorded", StringComparison.Ordinal));
        var savedLegacy = JsonSerializer.Deserialize<FamilyWorldCompositionRow>(JsonSerializer.Serialize(legacy))!;
        Check("a legacy unavailable reason survives saving the normalized row",
            savedLegacy.WorldDecoyRate == null && savedLegacy.WorldDecoyRateStatus == FamilyWorldDecoyRates.DecoyExceedsCredited
            && savedLegacy.WorldTargetPeptides == null && savedLegacy.WorldDecoyPeptides == null);
        var pass = new FamilyWorldPassResult { MinimumTagLength = 8, CreditableFamilies = new[] { family.Family } };
        pass.Families.Add(new() { Family = family.Family, CreditedEnvelope = 1, UniqueEnvelope = 1, SplitEnvelope = 1, Peptides = 3, UniquePeptides = 3, DecoyEnvelope = 20, DecoyPeptides = 7 });
        var summary = FamilyWorldErrors.Summarize(pass, Array.Empty<FamilyWorldKnownAnswer>(), "fixture", Array.Empty<string>());
        Check("fresh family summaries store the unavailable state with original target and decoy counts",
            summary.Families.Single().DecoyRate == null && summary.Families.Single().Peptides == 3
            && summary.Families.Single().DecoyPeptides == 7);
        summary.Outcomes.Add(new() { Outcome = nameof(FamilyWorldOutcome.Credited), Envelope = 1, Peptides = 3 });
        summary.Outcomes.Add(new() { Outcome = nameof(FamilyWorldOutcome.Decoy), Envelope = 20, Peptides = 7 });
        var estimate = FamilyWorldErrors.Estimate(summary);
        Check("length estimates retain unavailable rates and global counts", estimate.DecoyRate == null
            && estimate.TargetPeptides == 3 && estimate.DecoyPeptides == 7);
        string lengthLabel = new DatabaseGapLengthRow(8, estimate, true).DecoyRate;
        Check("length rows display the same reason and counts", lengthLabel.Contains(FamilyWorldDecoyRates.DecoyExceedsCredited, StringComparison.Ordinal)
            && lengthLabel.Contains("target 3", StringComparison.Ordinal) && lengthLabel.Contains("decoy 7", StringComparison.Ordinal));
        summary.Composition.Add(composition);
        string csv = summary.BuildCsv(), compositionCsv = summary.BuildCompositionCsv();
        Check("family CSV exposes unavailable reason and counts as columns",
            csv.Contains("\"decoy_rate_status\"", StringComparison.Ordinal) && csv.Contains(FamilyWorldDecoyRates.DecoyExceedsCredited, StringComparison.Ordinal)
            && csv.Contains("\"decoy_peptides\"", StringComparison.Ordinal));
        Check("composition CSV exposes the same unavailable reason and counts",
            compositionCsv.Contains("\"family_world_decoy_rate_status\"", StringComparison.Ordinal)
            && compositionCsv.Contains(FamilyWorldDecoyRates.DecoyExceedsCredited, StringComparison.Ordinal)
            && compositionCsv.Contains("\"family_world_decoy_peptides\"", StringComparison.Ordinal));
        Check("new CSV columns preserve a rectangular table including closing/global rows",
            csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(ParseCsvLine).Select(r => r.Count).Distinct().Count() == 1
            && compositionCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(ParseCsvLine).Select(r => r.Count).Distinct().Count() == 1);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("unavailable-state CSV remains culture-invariant", csv == summary.BuildCsv() && compositionCsv == summary.BuildCompositionCsv());
        }
        finally { CultureInfo.CurrentCulture = culture; }
        var reopened = JsonSerializer.Deserialize<FamilyWorldSummary>(JsonSerializer.Serialize(summary))!;
        Check("unavailable state and counts survive JSON round trip", reopened.Families.Single().DecoyRate == null
            && reopened.Families.Single().DecoyPeptides == 7 && reopened.Families.Single().DecoyEnvelope == 20);
    }
}
