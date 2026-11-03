using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationCacheTimingCommands() =>
    [new("--measure-curation-hit-cache", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--measure-curation-hit-cache", (args, _) =>
        {
            if (args.Length != 3) throw new ArgumentException("Use --measure-curation-hit-cache <project-copy.fqproj> <new-output-folder>.");
            return MeasureCurationHitCache(args[1], args[2]);
        })];

    private static int MeasureCurationHitCache(string projectPath, string outputDirectory)
    {
        string project = Path.GetFullPath(projectPath), output = Path.GetFullPath(outputDirectory);
        if (!File.Exists(project)) throw new FileNotFoundException("Project copy not found.", project);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("The measurement output must be a new folder.");
        Directory.CreateDirectory(output);
        string cache = Path.Combine(output, "cache");
        string Digest(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        string original = Digest(project);
        var results = new List<(double Load, double Attribution, string Mode, byte[] Composition, byte[] Stored)>();
        for (int round = 0; round < 2; round++)
        {
            var timer = Stopwatch.StartNew();
            var opened = ProjectStore.ReadFqproj(project);
            double load = timer.Elapsed.TotalSeconds;
            var view = new CurationQuantitationViewModel(opened.fcf);
            typeof(CurationQuantitationViewModel).GetField("fullXicCacheDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, cache);
            var messages = new List<string>();
            timer.Restart();
            var task = view.AttachWithOperationAsync(opened.quant, opened.manifest.PerRunTic,
                new OperationContext(progress: new HomologyCacheProgress(messages)));
            bool attached = task.GetAwaiter().GetResult();
            string composition = view.FullXicCompositionCsv() ?? throw new InvalidOperationException("No family composition was produced.");
            double attribution = timer.Elapsed.TotalSeconds;
            if (!attached) throw new InvalidOperationException("Family attribution failed: " + view.Summary);
            var mapping = view.CurrentFullXicAttribution() ?? throw new InvalidOperationException("No family attribution was produced.");
            var attributed = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(opened.quant), mapping);
            FamilyCompositionStudy.StoreRunShares(opened.manifest, attributed);
            byte[] stored = JsonSerializer.SerializeToUtf8Bytes(opened.manifest.FamilyRunShares);
            string mode = messages.Any(message => message.Contains("hits read from the cache", StringComparison.OrdinalIgnoreCase)) ? "cache"
                : messages.Any(message => message.Contains("Searching the stored peptide", StringComparison.Ordinal)) ? "search" : "unknown";
            byte[] bytes = Encoding.UTF8.GetBytes(composition);
            File.WriteAllBytes(Path.Combine(output, round == 0 ? "first-composition.csv" : "second-composition.csv"), bytes);
            File.WriteAllBytes(Path.Combine(output, round == 0 ? "first-stored-shares.json" : "second-stored-shares.json"), stored);
            results.Add((load, attribution, mode, bytes, stored));
            Console.WriteLine("Open " + (round + 1).ToString(CultureInfo.InvariantCulture) + ": " + mode + ", attribution "
                + attribution.ToString("F3", CultureInfo.InvariantCulture) + " s, project read " + load.ToString("F3", CultureInfo.InvariantCulture) + " s");
        }
        bool same = results[0].Composition.SequenceEqual(results[1].Composition) && results[0].Stored.SequenceEqual(results[1].Stored);
        bool unchanged = original == Digest(project);
        bool correctModes = results[0].Mode == "search" && results[1].Mode == "cache";
        string summary = "first_attribution_seconds=" + results[0].Attribution.ToString("R", CultureInfo.InvariantCulture)
            + "\nsecond_attribution_seconds=" + results[1].Attribution.ToString("R", CultureInfo.InvariantCulture)
            + "\nfirst_mode=" + results[0].Mode + "\nsecond_mode=" + results[1].Mode
            + "\nshares_byte_equal=" + same.ToString(CultureInfo.InvariantCulture)
            + "\nproject_unchanged=" + unchanged.ToString(CultureInfo.InvariantCulture) + "\n";
        File.WriteAllText(Path.Combine(output, "timings.txt"), summary, new UTF8Encoding(false));
        Console.WriteLine("Family shares byte equal: " + same + "; project unchanged: " + unchanged);
        return same && unchanged && correctModes ? 0 : 1;
    }
}
