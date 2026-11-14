using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsCascadeCommands() =>
    [
        new("--test-results-cascade", 74, TestCommandPhase.Raw, a => a.Contains("--test-results-cascade", StringComparer.Ordinal), (_, _) =>
        {
            ResultsCascadeTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>RS1: each line of the Results cascade equals the value it names, on a synthetic project.</summary>
    private static void ResultsCascadeTests()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var data = new AnalyticsData { Grouped = new NativeRunFraction { IsGrouped = true, Tic = 1000, DetectedPeptideLikeArea = 600,
                DetectedEnvelopeCount = 3000, IdentifiedEnvelope = 450, DeNovoEnvelope = 70 } };
            foreach (string run in new[] { "r1", "r2", "r3" }) data.Fractions.Add(new NativeRunFraction { Run = run, Tic = 1 });
            foreach (var (run, side, sequence, charge) in new[]
            {
                ("r1", NativeQuantFractions.SideIdentified, "PEPTIDEK", 2), ("r1", NativeQuantFractions.SideIdentified, "PEPTIDEK", 3),
                ("r1", NativeQuantFractions.SideDeNovo, "LLLLLK", 2), ("r2", NativeQuantFractions.SideIdentified, "PEPTIDEK", 2),
                ("r2", NativeQuantFractions.SideIdentified, "AAAAK", 2), ("r3", NativeQuantFractions.SideDeNovo, "LLLLLK", 2),
            })
                data.Peptides.Add(new AnalyticsPeptide { Run = run, Side = side, Sequence = sequence, Charge = charge });
            var families = new FamilyEnvelopeQuantResult { MeasuredIdentifiedEnvelope = 450, MeasuredDeNovoEnvelope = 70 };
            families.Families.Add(new() { FamilyUserId = 1, UniqueIdentifiedEnvelope = 400, UniqueDeNovoEnvelope = 40, SplitIdentifiedEnvelope = 410, SplitDeNovoEnvelope = 45 });
            families.Families.Add(new() { FamilyUserId = 2, UniqueIdentifiedEnvelope = 30, SplitIdentifiedEnvelope = 35 });
            families.Families.Add(new() { FamilyUserId = 3 });
            var built = ResultsCascade.Build(data, families, SharedPeptidePolicy.FamilyUnique);
            var lines = built.ToDictionary(line => line.Label, line => line.Value);
            Check("RS1 TIC is the 100% base", lines["TIC"] == "100.0%");
            Check("RS1 isotope envelopes measured are the detector total over the TIC, with envelopes per injection",
                lines["Isotope envelopes (measured)"] == "60.0% of TIC; about 1,000 envelopes per injection, not FDR-calibrated");
            Check("RS1 PSM and de novo are their measured envelopes over the measured isotope envelopes and over the TIC",
                lines["PSM and de novo"] == "86.7% of the isotope envelopes (measured), 52.0% of TIC (PSM 45.0% + de novo 7.0%)");
            Check("RS1 the measured-envelope ratio is not described as a subset fraction", built.Single(line => line.Label == "PSM and de novo")
                .Source.Contains("not a subset fraction", StringComparison.Ordinal));
            Check("RS1 credited to families is the family-unique signal over PSM and de novo and over the TIC",
                lines["Credited to families"] == "90.4% of PSM and de novo, 47.0% of TIC (PSM 95.6%, de novo 57.1%; peptides reaching several families are not credited)");
            Check("RS1 the counts are representatives, per injection, non-redundant and distinct sequences",
                lines["Peptide representatives"] == "6 (sum of the 3 run lists), about 2 per injection (PSM about 1 + de novo about 1); 4 non-redundant (3 PSM + 1 de novo; 3 distinct sequences)");
            Check("RS1 families reached and those with credited signal", lines["Families reached"] == "3 (2 with credited signal)");
            var split = ResultsCascade.Build(data, families, SharedPeptidePolicy.SplitEqually).Single(line => line.Label == "Credited to families").Value;
            Check("RS1 the split-equally policy credits shared peptides and says so", split == "94.2% of PSM and de novo, 49.0% of TIC (PSM 98.9%, de novo 64.3%; peptides reaching several families are split equally)");
            data.Grouped.DetectedPeptideLikeArea = 0;
            var noDetector = ResultsCascade.Build(data, null, SharedPeptidePolicy.FamilyUnique);
            Check("RS1 without a detector total or families, those lines are left out, never estimated", noDetector.All(line => line.Label is not ("Isotope envelopes (measured)" or "Credited to families" or "Families reached"))
                && noDetector.Single(line => line.Label == "PSM and de novo").Value == "52.0% of TIC (PSM 45.0% + de novo 7.0%)");
            Check("RS1 every line names its source", ResultsCascade.Build(data, families, SharedPeptidePolicy.FamilyUnique).All(line => line.Source.Length > 20));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
