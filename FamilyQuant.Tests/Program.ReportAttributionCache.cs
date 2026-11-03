using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class J32Progress : IProgress<OperationProgress>
    {
        public readonly List<OperationProgress> Events = new();
        public void Report(OperationProgress value) => Events.Add(value);
    }

    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportAttributionCacheCommands() =>
    [new("--test-report-attribution-cache", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-report-attribution-cache", (_, _) =>
        {
            ReportAttributionCacheTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportAttributionCacheTests()
    {
        EnsureApplication();
        MethodInfo? method = typeof(ReportProjectExporter).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(m => m.Name == "Build" && m.GetParameters().Any(p => p.Name == "homologyCacheDirectory")
                && m.GetParameters().Any(p => p.Name == "operation"));
        Check("J32 report export exposes isolated cache and operation progress", method != null);
        if (method == null) return;
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.J32", Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(root);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "protein-a", Description = "First", Sequence = "TTACDEFGHIKGG", UserID = 1 },
            new() { SequenceIdentifier = "protein-b", Description = "Second", Sequence = "TTACDEFAHIKGG", UserID = 2 },
            new() { SequenceIdentifier = "Reverse_protein-c", Description = "Third", Sequence = "QQTSRQPNMLKIHH", UserID = 3 },
        } };
        var rows = new[] { Qr(NativeQuantFractions.SideIdentified, "run-a", 1, 2, "ACDEFGHIK", 100, 150),
            Qr(NativeQuantFractions.SideDeNovo, "run-a", 2, 2, "ACDEFAHIK", 50, 70) };
        rows[1].DeNovoEvidence = new() { new() { SourceId = DeNovoSource.DenovoOnly.Id, Sequence = "ACDEFAHIK" },
            new() { SourceId = DeNovoSource.DeepNovo.Id, Sequence = "ACDEFGHIK" } };
        var manifest = new QuantManifest { PerRunTic = new() { ["run-a"] = 100000 },
            FamilyMapping = new() { MinIdentity = .99, Method = "Synthetic recorded mapping" } };
        ReportProjectExport Build(string folder, J32Progress progress) => (ReportProjectExport)method.Invoke(null,
            new object?[] { rows, manifest, file, folder, null, null, null, null,
                new OperationContext(CancellationToken.None, progress), cache })!;
        var coldProgress = new J32Progress(); var warmProgress = new J32Progress();
        var cold = Build(Path.Combine(root, "cold"), coldProgress);
        var warm = Build(Path.Combine(root, "warm"), warmProgress);
        var freshFamilies = FamilyEnvelopeQuant.BuildFromCuration(rows, file, new Homology.HomologyOptions { MinIdentity = .99 });
        var fresh = ReportProjectExporter.Build(rows, manifest, file, Path.Combine(root, "fresh"), familyResults: freshFamilies);
        Check("J32 cold export performs a cache miss and warm export reads a matching hit",
            coldProgress.Events.Any(x => x.Stage == "report-primary-cache" && x.Message.Contains("miss", StringComparison.OrdinalIgnoreCase))
            && warmProgress.Events.Any(x => x.Stage == "report-primary-cache" && x.Message.Contains("hit", StringComparison.OrdinalIgnoreCase)));
        Check("J32 both exports preserve separate primary, identified and de novo census progress",
            new[] { coldProgress, warmProgress }.All(p => new[] { "report-primary-start", "report-primary-end",
                "report-census-identified-start", "report-census-identified-end", "report-census-denovo-start", "report-census-denovo-end" }
                .All(stage => p.Events.Any(e => e.Stage == stage))));
        string[] phases = { "report-primary-start", "report-primary-end", "report-census-identified-start",
            "report-census-identified-end", "report-census-denovo-start", "report-census-denovo-end" };
        Check("J32 progress boundaries occur in measurement order", new[] { coldProgress, warmProgress }.All(p =>
        {
            int[] positions = phases.Select(stage => p.Events.FindIndex(e => e.Stage == stage)).ToArray();
            return positions.All(i => i >= 0) && positions.Zip(positions.Skip(1), (a, b) => a < b).All(x => x);
        }));
        Check("J32 substeps retain the overall report progress fraction", new[] { coldProgress, warmProgress }.All(p =>
            p.Events.Where(e => phases.Contains(e.Stage) || e.Stage == "report-primary-cache")
                .All(e => e.Completed == 2 && e.Total == 8)));
        Check("J32 warm report CSV data equal cold report CSV data", cold.CsvExports.Count == warm.CsvExports.Count
            && cold.CsvExports.All(kv => warm.CsvExports.TryGetValue(kv.Key, out string? value) && value == kv.Value));
        Check("J32 report CSV data also equal the original uncached search path", fresh.CsvExports.Count == warm.CsvExports.Count
            && fresh.CsvExports.All(kv => warm.CsvExports.TryGetValue(kv.Key, out string? value) && value == kv.Value));
        byte[] coldBook = ReportSupplementWorkbook.BuildBytes(cold.Document, cold.SupplementSheets);
        byte[] warmBook = ReportSupplementWorkbook.BuildBytes(warm.Document, warm.SupplementSheets);
        Check("J32 warm and cold companion workbook bytes, and therefore cells, are identical", coldBook.SequenceEqual(warmBook));
        Check("J32 workbook also equals the original uncached search path", warmBook.SequenceEqual(
            ReportSupplementWorkbook.BuildBytes(fresh.Document, fresh.SupplementSheets)));
        manifest.FamilyMapping.MinIdentity = .8;
        var changedProgress = new J32Progress();
        _ = Build(Path.Combine(root, "changed-options"), changedProgress);
        Check("J32 report search option changes do not reuse a mismatched cache entry", changedProgress.Events.Any(x =>
            x.Stage == "report-primary-cache" && x.Message.Contains("miss", StringComparison.OrdinalIgnoreCase)));
    }
}
