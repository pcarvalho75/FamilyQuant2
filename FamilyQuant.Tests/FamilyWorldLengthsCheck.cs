using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// B10 on a real computed project: the analysis Compute now runs (every minimum tag length from one search)
    /// against the table the project already stores for 9, and each length's table against an analysis at that
    /// length alone. It times both, and writes the quick estimate per length.
    /// </summary>
    internal static class FamilyWorldLengthsCheck
    {
        internal const string Argument = "--family-world-lengths";

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) => TestArguments.EmptyFallsBack(args, flag, offset, fallback);
                string projectPath = At(1, "D:/FamilyQuant-data/out-opus/b6/bothrops-jararaca-family-world.fqproj");
                string outDir = At(2, "D:/FamilyQuant-data/out-opus/b10/lengths");
                Directory.CreateDirectory(outDir);
                RunOn(projectPath, outDir, check);
            }
            catch (Exception ex) { check("family world lengths check completed", false, ex.GetBaseException().ToString()); }
        }

        private static string Json(object value) => JsonSerializer.Serialize(value);
        private static string P(double? v) => v.HasValue ? (100 * v.Value).ToString("F2", CultureInfo.InvariantCulture) + "%" : "n/a";

        private static void RunOn(string projectPath, string outDir, Action<string, bool, string?> check)
        {
            var (curation, quant, manifest) = ProjectStore.ReadFqproj(projectPath);
            FamilyWorldSummary stored = manifest.FamilyWorldResult ?? throw new InvalidDataException("The project stores no family-world table.");
            FamilyWorldSnapshot snapshot = FamilyWorldFetcher.Stored(curation);
            FamilyWorld world = FamilyWorld.FromSnapshot(snapshot);

            var watch = Stopwatch.StartNew();
            FamilyWorldSummary every = FamilyWorldAnalysis.FromStoredWorld(quant, curation, manifest.SpectrumComparisonRows).Summary;
            double everySeconds = watch.Elapsed.TotalSeconds;
            watch.Restart();
            FamilyWorldSummary nine = FamilyWorldAnalysis.Run(quant, curation, manifest.SpectrumComparisonRows, world, snapshot.Release).Summary;
            double nineSeconds = watch.Elapsed.TotalSeconds;
            Console.WriteLine("        every length 7 to 15 from one search: " + everySeconds.ToString("F1", CultureInfo.InvariantCulture) +
                " s; length 9 alone, as before B10: " + nineSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");

            List<FamilyWorldSummary> tables = every.Lengths ?? new List<FamilyWorldSummary>();
            List<FamilyWorldLengthEstimate> estimates = every.LengthEstimates ?? new List<FamilyWorldLengthEstimate>();
            every.Lengths = null;
            every.LengthEstimates = null;
            check("family world lengths: the default table is the 9 table the project already stores, byte for byte",
                every.MinimumTagLength == 9 && Json(every) == Json(stored) && Json(nine) == Json(stored), null);
            every.Lengths = tables;
            every.LengthEstimates = estimates;

            watch.Restart();
            var direct = new List<FamilyWorldSummary>();
            foreach (int length in FamilyWorldOptions.LengthChoices)
                direct.Add(FamilyWorldAnalysis.Run(quant, curation, manifest.SpectrumComparisonRows, world, snapshot.Release,
                    options: new FamilyWorldOptions { MinimumTagLength = length }).Summary);
            double directSeconds = watch.Elapsed.TotalSeconds;
            Console.WriteLine("        the nine lengths searched one by one: " + directSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
            check("family world lengths: each length's table from one search equals an analysis searched at that length alone",
                tables.Count == direct.Count && tables.Zip(direct).All(x => Json(x.First) == Json(x.Second)),
                string.Join(",", tables.Zip(direct).Where(x => Json(x.First) != Json(x.Second)).Select(x => x.First.MinimumTagLength)));
            watch.Restart();
            FamilyWorldSummary? chosen = every.ForLength(12);
            double chooseMicroseconds = watch.Elapsed.TotalMilliseconds * 1000;
            check("family world lengths: choosing a length reads its stored table, with no search",
                chosen != null && chosen.MinimumTagLength == 12, chooseMicroseconds.ToString("F0", CultureInfo.InvariantCulture) + " microseconds");

            Console.WriteLine("        minimum | landed on a family | decoys of landed | known answers right of landed | families with a rate");
            var csv = new StringBuilder("minimum_tag_length,residual_envelope,credited_envelope,landed_fraction,decoy_envelope,decoy_rate,known_landed,known_right,right_rate,families_measured\n");
            string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            foreach (FamilyWorldLengthEstimate e in estimates)
            {
                Console.WriteLine("          " + e.MinimumTagLength.ToString(CultureInfo.InvariantCulture).PadLeft(7) + " | " + P(e.LandedFraction) +
                    " | " + P(e.DecoyRate) + " | " + e.KnownRight.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                    e.KnownLanded.ToString("N0", CultureInfo.InvariantCulture) + " (" + P(e.RightRate) + ") | " +
                    e.FamiliesMeasured.ToString(CultureInfo.InvariantCulture));
                csv.Append(string.Join(",", e.MinimumTagLength.ToString(CultureInfo.InvariantCulture), R(e.ResidualEnvelope), R(e.CreditedEnvelope),
                    R(e.LandedFraction), R(e.DecoyEnvelope), e.DecoyRate.HasValue ? R(e.DecoyRate.Value) : "",
                    e.KnownLanded.ToString(CultureInfo.InvariantCulture), e.KnownRight.ToString(CultureInfo.InvariantCulture),
                    e.RightRate.HasValue ? R(e.RightRate.Value) : "", e.FamiliesMeasured.ToString(CultureInfo.InvariantCulture))).Append('\n');
            }
            File.WriteAllText(Path.Combine(outDir, "family_world_lengths.csv"), csv.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "family_world_result_all_lengths.json"), Json(every), new UTF8Encoding(false));
            Console.WriteLine("        written to " + outDir);
        }
    }
}
