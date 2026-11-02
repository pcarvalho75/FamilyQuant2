using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// B10 on a real project: the analysis Compute now runs (every minimum tag length from one search) against an
    /// analysis at 9 alone and, when the project stores one, its stored 9 table; and each length's table against an
    /// analysis at that length alone. It times both and writes the quick estimate per length, what each step down in
    /// length adds, and the known-answer halves of the B1 study at 7, 8 and 9. Without a world file the project's own
    /// stored world is read; with one (B4's file, for a project that stores none) the same analysis runs on that world.
    /// </summary>
    internal static class FamilyWorldLengthsCheck
    {
        internal const string Argument = "--family-world-lengths";
        private static readonly int[] HalfLengths = { 7, 8, 9 };

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) => TestArguments.EmptyFallsBack(args, flag, offset, fallback);
                string projectPath = At(1, "D:/FamilyQuant-data/out-opus/b6/bothrops-jararaca-family-world.fqproj");
                string outDir = At(2, "D:/FamilyQuant-data/out-opus/b10/lengths");
                string worldPath = At(3, ".");
                Directory.CreateDirectory(outDir);
                RunOn(projectPath, worldPath == "." ? null : worldPath, outDir, check);
            }
            catch (Exception ex) { check("family world lengths check completed", false, ex.GetBaseException().ToString()); }
        }

        private static string Json(object value) => JsonSerializer.Serialize(value);
        private static string N(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
        private static string P(double? v) => v.HasValue ? (100 * v.Value).ToString("F2", CultureInfo.InvariantCulture) + "%" : "n/a";
        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static string Seconds(double v) => v.ToString("F1", CultureInfo.InvariantCulture) + " s";
        private static int HalfOf(FamilyWorldKnownAnswer a) => SHA256.HashData(Encoding.UTF8.GetBytes(a.Identified))[0] & 1;

        private static void RunOn(string projectPath, string? worldPath, string outDir, Action<string, bool, string?> check)
        {
            var (curation, quant, manifest) = ProjectStore.ReadFqproj(projectPath);
            FamilyWorldSummary? stored = worldPath != null ? null
                : manifest.FamilyWorldResult ?? throw new InvalidDataException("The project stores no family-world table.");
            FamilyWorldSnapshot snapshot = FamilyWorldFetcher.Stored(worldPath == null ? curation : FamilyClusterFile.Load(worldPath));
            FamilyWorld world = FamilyWorld.FromSnapshot(snapshot);
            Console.WriteLine("        world " + (worldPath ?? "stored in the project") + ": " + N(world.Proteins.Count) + " entries, " +
                N(world.ScopeFamilies.Count) + " families in scope, hash " + snapshot.ContentSha256);

            var watch = Stopwatch.StartNew();
            FamilyWorldSummary every = (worldPath == null
                ? FamilyWorldAnalysis.FromStoredWorld(quant, curation, manifest.SpectrumComparisonRows)
                : FamilyWorldAnalysis.Run(quant, curation, manifest.SpectrumComparisonRows, world, snapshot.Release,
                    lengths: FamilyWorldOptions.LengthChoices)).Summary;
            double everySeconds = watch.Elapsed.TotalSeconds;
            watch.Restart();
            FamilyWorldSummary nine = FamilyWorldAnalysis.Run(quant, curation, manifest.SpectrumComparisonRows, world, snapshot.Release).Summary;
            double nineSeconds = watch.Elapsed.TotalSeconds;
            Console.WriteLine("        every length 7 to 15 from one search: " + Seconds(everySeconds) + "; length 9 alone, as before B10: " + Seconds(nineSeconds));

            List<FamilyWorldSummary> tables = every.Lengths ?? new List<FamilyWorldSummary>();
            List<FamilyWorldLengthEstimate> estimates = every.LengthEstimates ?? new List<FamilyWorldLengthEstimate>();
            every.Lengths = null;
            every.LengthEstimates = null;
            check("family world lengths: the default table is the 9 table of an analysis at 9 alone, byte for byte",
                every.MinimumTagLength == 9 && Json(every) == Json(nine), null);
            if (stored != null)
                check("family world lengths: the default table is the 9 table the project already stores, byte for byte",
                    Json(every) == Json(stored) && Json(nine) == Json(stored), null);
            every.Lengths = tables;
            every.LengthEstimates = estimates;

            watch.Restart();
            var direct = new List<FamilyWorldAnalysisResult>();
            foreach (int length in FamilyWorldOptions.LengthChoices)
                direct.Add(FamilyWorldAnalysis.Run(quant, curation, manifest.SpectrumComparisonRows, world, snapshot.Release,
                    options: new FamilyWorldOptions { MinimumTagLength = length }));
            double directSeconds = watch.Elapsed.TotalSeconds;
            Console.WriteLine("        the nine lengths searched one by one: " + Seconds(directSeconds));
            check("family world lengths: each length's table from one search equals an analysis searched at that length alone",
                tables.Count == direct.Count && tables.Zip(direct).All(x => Json(x.First) == Json(x.Second.Summary)),
                string.Join(",", tables.Zip(direct).Where(x => Json(x.First) != Json(x.Second.Summary)).Select(x => x.First.MinimumTagLength)));
            watch.Restart();
            FamilyWorldSummary? chosen = every.ForLength(12);
            double chooseMicroseconds = watch.Elapsed.TotalMilliseconds * 1000;
            check("family world lengths: choosing a length reads its stored table, with no search",
                chosen != null && chosen.MinimumTagLength == 12, chooseMicroseconds.ToString("F0", CultureInfo.InvariantCulture) + " microseconds");

            Console.WriteLine("        minimum | landed on a family | decoys of landed | known answers right of landed | families with a rate");
            var csv = new StringBuilder("minimum_tag_length,residual_envelope,credited_envelope,landed_fraction,decoy_envelope,decoy_rate,known_landed,known_right,right_rate,families_measured\n");
            foreach (FamilyWorldLengthEstimate e in estimates)
            {
                Console.WriteLine("          " + e.MinimumTagLength.ToString(CultureInfo.InvariantCulture).PadLeft(7) + " | " + P(e.LandedFraction) +
                    " | " + P(e.DecoyRate) + " | " + N(e.KnownRight) + " of " + N(e.KnownLanded) + " (" + P(e.RightRate) + ") | " +
                    e.FamiliesMeasured.ToString(CultureInfo.InvariantCulture));
                csv.Append(string.Join(",", e.MinimumTagLength.ToString(CultureInfo.InvariantCulture), R(e.ResidualEnvelope), R(e.CreditedEnvelope),
                    R(e.LandedFraction), R(e.DecoyEnvelope), e.DecoyRate.HasValue ? R(e.DecoyRate.Value) : "",
                    e.KnownLanded.ToString(CultureInfo.InvariantCulture), e.KnownRight.ToString(CultureInfo.InvariantCulture),
                    e.RightRate.HasValue ? R(e.RightRate.Value) : "", e.FamiliesMeasured.ToString(CultureInfo.InvariantCulture))).Append('\n');
            }

            // What one step down in length adds: lowering the minimum only lets shorter tags in, so the gain from n+1
            // to n is the tags of exactly n residues. The decoy share uses the table's denominator, the credited signal.
            Console.WriteLine("        step | credited signal gained (of the residual) | decoys of the gained signal | known answers gained, right");
            var steps = new StringBuilder("from_length,to_length,credited_gained,credited_gained_of_residual,decoy_gained,decoy_share_of_gained,known_landed_gained,known_right_gained\n");
            var exactGains = new List<string>();
            for (int i = 0; i + 1 < estimates.Count; i++)
            {
                FamilyWorldLengthEstimate low = estimates[i], high = estimates[i + 1];
                double gained = low.CreditedEnvelope - high.CreditedEnvelope, decoyGained = low.DecoyEnvelope - high.DecoyEnvelope;
                int knownGained = low.KnownLanded - high.KnownLanded, rightGained = low.KnownRight - high.KnownRight;
                double? decoyShare = gained > 0 ? decoyGained / gained : null;
                Console.WriteLine("          " + high.MinimumTagLength.ToString(CultureInfo.InvariantCulture).PadLeft(2) + " to " +
                    low.MinimumTagLength.ToString(CultureInfo.InvariantCulture).PadLeft(2) + " | " + R(gained) + " (" + P(gained / low.ResidualEnvelope) +
                    ") | " + P(decoyShare) + " | " + N(knownGained) + ", " + N(rightGained) + " right" +
                    (knownGained > 0 ? " (" + P(rightGained / (double)knownGained) + ")" : ""));
                steps.Append(string.Join(",", high.MinimumTagLength.ToString(CultureInfo.InvariantCulture), low.MinimumTagLength.ToString(CultureInfo.InvariantCulture),
                    R(gained), R(gained / low.ResidualEnvelope), R(decoyGained), decoyShare.HasValue ? R(decoyShare.Value) : "",
                    knownGained.ToString(CultureInfo.InvariantCulture), rightGained.ToString(CultureInfo.InvariantCulture))).Append('\n');
                List<FamilyWorldKnownAnswer> atLow = direct.Single(d => d.Summary.MinimumTagLength == low.MinimumTagLength).KnownAnswers
                    .Where(a => a.IsKnown && a.Credit != null && a.Landed && a.Tag.Length == low.MinimumTagLength).ToList();
                if (gained < 0 || decoyGained < 0 || atLow.Count != knownGained || atLow.Count(a => a.Right) != rightGained)
                    exactGains.Add(high.MinimumTagLength.ToString(CultureInfo.InvariantCulture) + " to " + low.MinimumTagLength.ToString(CultureInfo.InvariantCulture));
            }
            check("family world lengths: each step down only adds, and the known answers it adds are the landed tags of exactly that length",
                estimates.Count == FamilyWorldOptions.LengthChoices.Count && exactGains.Count == 0, string.Join("; ", exactGains));

            // The outcome-blind halves of the B1 study (low bit of the identified peptide's SHA-256), at each length alone.
            Console.WriteLine("        known answers right of landed by half (table half | held-out half | pooled)");
            var halves = new StringBuilder("minimum_tag_length,half,known_landed,known_right,right_rate\n");
            var pooledDiffers = new List<int>();
            foreach (int length in HalfLengths)
            {
                List<FamilyWorldKnownAnswer> known = direct.Single(d => d.Summary.MinimumTagLength == length).KnownAnswers
                    .Where(a => a.IsKnown && a.Credit != null).ToList();
                var parts = new List<string>();
                foreach (var (name, set) in new[] { ("table", known.Where(a => HalfOf(a) == 0)), ("heldout", known.Where(a => HalfOf(a) == 1)), ("pooled", known) })
                {
                    List<FamilyWorldKnownAnswer> landed = set.Where(a => a.Landed).ToList();
                    int right = landed.Count(a => a.Right);
                    double? rate = landed.Count > 0 ? right / (double)landed.Count : null;
                    parts.Add(N(right) + " of " + N(landed.Count) + " (" + P(rate) + ")");
                    halves.Append(string.Join(",", length.ToString(CultureInfo.InvariantCulture), name, landed.Count.ToString(CultureInfo.InvariantCulture),
                        right.ToString(CultureInfo.InvariantCulture), rate.HasValue ? R(rate.Value) : "")).Append('\n');
                    if (name == "pooled")
                    {
                        FamilyWorldLengthEstimate e = estimates.Single(x => x.MinimumTagLength == length);
                        if (landed.Count != e.KnownLanded || right != e.KnownRight) pooledDiffers.Add(length);
                    }
                }
                Console.WriteLine("          " + length.ToString(CultureInfo.InvariantCulture).PadLeft(7) + " | " + string.Join(" | ", parts));
            }
            check("family world lengths: the two halves at 7, 8 and 9 add up to the table's known answers at that length",
                pooledDiffers.Count == 0, string.Join(",", pooledDiffers));

            File.WriteAllText(Path.Combine(outDir, "family_world_lengths.csv"), csv.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "family_world_length_steps.csv"), steps.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "family_world_length_halves.csv"), halves.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "family_world_result_all_lengths.json"), Json(every), new UTF8Encoding(false));
            Console.WriteLine("        written to " + outDir);
        }
    }
}
