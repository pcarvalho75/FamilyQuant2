using System.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FamilyQuant.Evidence;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    internal static List<QuantRow> FinishQuantRows() => SyntheticAnalyticsRows();

    internal static QuantManifest FinishStoredManifest()
    {
        var tables = FamilyWorldOptions.LengthChoices.Select(length =>
        {
            var table = new FamilyWorldSummary { MinimumTagLength = length, MinIdentity = .8, MinimumKnownCredits = 10,
                CompositionRules = FamilyWorldErrors.CompositionRule, Rules = "Synthetic stored UI fixture",
                ScopeFamilies = new() { "Synthetic UI family" }, WorldRelease = "Synthetic UI fixture",
                MeasuredIdentifiedEnvelope = 100, MeasuredDeNovoEnvelope = 40, UnexplainedEnvelope = length };
            table.Composition.Add(new FamilyWorldCompositionRow { Family = "Synthetic UI family", IdentifiedEnvelope = 100,
                DatabaseDeNovoEnvelope = 10, WorldDeNovoEnvelope = 30 - length, WorldKnownCredited = 12,
                WorldKnownCreditedTrue = 11, WorldRightRate = 11d / 12, WorldDecoyRate = .01, WorldEvidence = "Synthetic stored UI fixture" });
            return table;
        }).ToList();
        var root = JsonSerializer.Deserialize<FamilyWorldSummary>(JsonSerializer.Serialize(
            tables.Single(table => table.MinimumTagLength == FamilyWorldOptions.DefaultMinimumTagLength)))!;
        root.Lengths = tables;
        root.LengthEstimates = FamilyWorldOptions.LengthChoices.Select(length => new FamilyWorldLengthEstimate
        { MinimumTagLength = length, LandedFraction = (30 - length) / 30d, CreditedEnvelope = 30 - length, DecoyEnvelope = (30 - length) * .01, DecoyRate = .01,
            KnownRight = 11, KnownLanded = 12, RightRate = 11d / 12, FamiliesMeasured = 1 }).ToList();
        return new QuantManifest { FamilyWorldResult = root, PerRunTic = new() { ["ui_01"] = 5e8, ["ui_02"] = 4e8 } };
    }

    internal static bool VerifyFinishLayout(string name, FrameworkElement root, Size size)
    {
        int before = failed;
        CheckLayout(name, root, size, -1);
        return before == failed;
    }
}
