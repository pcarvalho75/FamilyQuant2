using FamilyQuant.DeNovo;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace FamilyQuant.Tests
{
    /// <summary>Runs the production computation, with isolated caches and outputs, for before/after
    /// stage-refactor comparisons. The supplied project supplies curation and source cutoffs. No talk
    /// producer, canonical figure, shared cache or golden snapshot is written.</summary>
    internal static class FullXicPipelineSnapshot
    {
        internal const string Argument = "--fullxic-pipeline-snapshot";

        internal static int Run(string[] args)
        {
            if (args.Length != 6)
                throw new ArgumentException("Supply project.fqproj, RAW folder, de novo folder, empty output folder and cache folder.");
            string output = Path.GetFullPath(args[4]);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("The snapshot output folder must be empty: " + output);
            Directory.CreateDirectory(output);
            string cache = Path.GetFullPath(args[5]);
            Directory.CreateDirectory(cache);
            var (curation, _, previous) = ProjectStore.ReadFqproj(args[1]);
            var selections = previous.DeNovoInputs
                .GroupBy(i => i.SourceId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(i => i.SourcePriority)
                .Select(i => new DeNovoSourceSelection(DeNovoSource.FromId(i.SourceId)
                    ?? throw new InvalidDataException("Unknown stored source: " + i.SourceId),
                    i.MinimumConfidence, i.SourcePriority)).ToList();
            if (selections.Count == 0)
                throw new InvalidDataException("The snapshot project must record its source selections.");
            var distributions = previous.DeNovoSourceScoreDistributions;
            if (distributions.Count != selections.Count || distributions.Select(d =>
                (d.MinLength, d.StretchLength, d.StretchFloor)).Distinct().Count() != 1)
                throw new InvalidDataException("The snapshot needs a consistent stored length and local-confidence filter for every source.");
            var filter = distributions[0];
            var options = new FullXicOptions
            {
                IsotopeCount = previous.IsotopeCount,
                MinPointsForQuant = previous.MinPointsForQuant,
                Ppm = previous.Ppm,
                Ms1Tolerance = previous.Ms1Tolerance,
                AppVersion = previous.AppVersion,
                FamilyCuration = curation,
                DeNovoSelections = selections,
                DeNovoCriteria = new DeNovoFilterCriteria
                {
                    MinLength = filter.MinLength,
                    StretchLength = filter.StretchLength,
                    StretchFloor = filter.StretchFloor,
                },
                SpectrumComparisonCsvPath = Path.Combine(output, "spectrum.csv"),
            };
            Console.WriteLine("Sources: " + string.Join("; ", selections.Select(s => s.Source.Id + " " + s.CutoffLabel)));
            using Process process = Process.GetCurrentProcess();
            TimeSpan cpuStart = process.TotalProcessorTime;
            var watch = Stopwatch.StartNew();
            FullXicComputeResult result = FullXicComputer.ComputeFullXic(args[2],
                Path.Combine(args[2], "PLVI.sepr2"), args[3], options,
                (side, run) => Path.Combine(cache, side + "_" + run + ".json"),
                Console.WriteLine, detectedCachePathFor: run => Path.Combine(cache, "detected_" + run + ".json"));
            watch.Stop();
            double cpuSeconds = (process.TotalProcessorTime - cpuStart).TotalSeconds;
            Console.WriteLine("Compute completed: " + watch.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)
                + " seconds wall; " + cpuSeconds.ToString("F3", CultureInfo.InvariantCulture) + " seconds CPU.");
            process.Refresh();
            long peakBeforeSave = process.PeakWorkingSet64;
            string project = Path.Combine(output, "computed.fqproj");
            ProjectStore.WriteFqproj(project, curation, result.Rows, result.Manifest);
            using (ZipArchive zip = ZipFile.OpenRead(project))
            {
                WriteEntry(zip, ProjectStore.QuantEntryName, Path.Combine(output, "quant.tsv"), true);
                WriteEntry(zip, "spectrum_comparison.jsonl.gz", Path.Combine(output, "spectrum.jsonl"), true);
                using var manifestReader = new StreamReader(zip.GetEntry(ProjectStore.ManifestEntryName)!.Open());
                JsonNode manifest = JsonNode.Parse(manifestReader.ReadToEnd())!;
                // The computation's two clock fields are intentionally volatile. Everything else,
                // including every study, fingerprint and source cutoff, participates in byte comparison.
                manifest[nameof(QuantManifest.CreatedUtc)] = "<computation time>";
                manifest[nameof(QuantManifest.UpdatedUtc)] = "<computation time>";
                File.WriteAllText(Path.Combine(output, "manifest.json"), manifest.ToJsonString(), new UTF8Encoding(false));
            }
            File.WriteAllText(Path.Combine(output, "elapsed-seconds.txt"),
                watch.Elapsed.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "cpu-seconds.txt"),
                cpuSeconds.ToString("F6", CultureInfo.InvariantCulture), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "peak-before-save-bytes.txt"),
                peakBeforeSave.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
            process.Refresh();
            File.WriteAllText(Path.Combine(output, "peak-process-bytes.txt"),
                process.PeakWorkingSet64.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
            Console.WriteLine("Snapshot: " + result.Rows.Count.ToString(CultureInfo.InvariantCulture) +
                " rows; " + watch.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + " seconds; " + output);
            if (result.SpectrumComparisonCsvError.Length > 0 || result.Rows.Count == 0)
                throw new InvalidDataException("The computation did not produce a complete snapshot.");
            return 0;
        }

        private static void WriteEntry(ZipArchive zip, string name, string path, bool compressed)
        {
            using Stream input = zip.GetEntry(name)?.Open()
                ?? throw new InvalidDataException("Missing project entry: " + name);
            using Stream content = compressed ? new GZipStream(input, CompressionMode.Decompress) : input;
            using Stream output = File.Create(path);
            content.CopyTo(output);
        }
    }
}
