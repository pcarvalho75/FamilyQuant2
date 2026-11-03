using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class J39ProgressAction : IProgress<OperationProgress>
    {
        private readonly Action<OperationProgress> action;
        public J39ProgressAction(Action<OperationProgress> action) => this.action = action;
        public void Report(OperationProgress value) => action(value);
    }

    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportSourceCensusCacheCommands() =>
    [new("--test-report-source-census-cache", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-report-source-census-cache", (_, _) =>
        {
            ReportSourceCensusCacheTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportSourceCensusCacheTests()
    {
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.J39", Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(root, "cache"); Directory.CreateDirectory(root);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "protein-a", Description = "First", Sequence = "TTACDEFGHIKGG", UserID = 1 },
            new() { SequenceIdentifier = "protein-b", Description = "Second", Sequence = "TTACDEFAHLKGG", UserID = 2 },
            new() { SequenceIdentifier = "Reverse_protein-c", Description = "Third", Sequence = "QQTSRQPNMLKIHH", UserID = 3 },
        } };
        var rows = new[] { Qr(NativeQuantFractions.SideIdentified, "run-a", 1, 2, "ACDEFGHIK", 100, 150),
            Qr(NativeQuantFractions.SideDeNovo, "run-a", 2, 2, "ACDEFAHIK", 50, 70) };
        rows[1].DeNovoEvidence = new() { new() { SourceId = DeNovoSource.DenovoOnly.Id, Sequence = "ACDEFAHIK" },
            new() { SourceId = DeNovoSource.DeepNovo.Id, Sequence = "ACDEFAHLK" } };
        var manifest = new QuantManifest { PerRunTic = new() { ["run-a"] = 100000 },
            FamilyMapping = new() { MinIdentity = .99, Method = "Synthetic recorded mapping" } };
        int run = 0;
        (ReportProjectExport Export, J32Progress Progress) Build()
        {
            var progress = new J32Progress();
            var export = ReportProjectExporter.Build(rows, manifest, file, Path.Combine(root, (++run).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                operation: new OperationContext(CancellationToken.None, progress), homologyCacheDirectory: cache);
            return (export, progress);
        }
        bool Mode(J32Progress progress, string mode) => progress.Events.Any(e => e.Stage == "report-census-cache"
            && e.Message.Contains(mode, StringComparison.OrdinalIgnoreCase));
        var cold = Build(); var warm = Build();
        Check("J39 source census reports a cold miss and a warm hit", Mode(cold.Progress, "miss") && Mode(warm.Progress, "hit"));
        Check("J39 warm source census does not launch either homology search", warm.Progress.Events
            .Where(e => e.Stage is "report-census-identified-start" or "report-census-denovo-start")
            .All(e => e.Message.Contains("cached", StringComparison.OrdinalIgnoreCase)) && Mode(warm.Progress, "hit"));
        var directFamilies = FamilyEnvelopeQuant.BuildFromCuration(rows, file, new Homology.HomologyOptions { MinIdentity = .99 });
        var direct = ReportProjectExporter.Build(rows, manifest, file, Path.Combine(root, "direct"), familyResults: directFamilies);
        var attribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(rows, file, new Homology.HomologyOptions { MinIdentity = .99 });
        var exactProgress = new J32Progress();
        var exactCold = FamilyEnvelopeQuant.BuildFromAttribution(rows, attribution,
            new OperationContext(CancellationToken.None, exactProgress), Path.Combine(root, "exact-census"));
        var exactWarm = FamilyEnvelopeQuant.BuildFromAttribution(rows, attribution,
            new OperationContext(CancellationToken.None, exactProgress), Path.Combine(root, "exact-census"));
        byte[] NativeCensus(FamilyEnvelopeQuantResult result) => JsonSerializer.SerializeToUtf8Bytes(result.SourceSequenceSummary);
        Check("J39 cached and uncached source census objects are byte-identical", NativeCensus(directFamilies).SequenceEqual(NativeCensus(exactCold))
            && NativeCensus(directFamilies).SequenceEqual(NativeCensus(exactWarm)) && Mode(exactProgress, "hit"));
        bool SameCsv(ReportProjectExport x, ReportProjectExport y) => x.CsvExports.Count == y.CsvExports.Count
            && x.CsvExports.All(kv => y.CsvExports.TryGetValue(kv.Key, out string? value) && value == kv.Value);
        Check("J39 warm and cold report CSVs equal the independent uncached census", SameCsv(cold.Export, warm.Export) && SameCsv(direct, warm.Export));
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); var otherCulture = Build();
            Check("J39 cache keys and source data are culture invariant", Mode(otherCulture.Progress, "hit") && SameCsv(direct, otherCulture.Export));
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        byte[] workbook = ReportSupplementWorkbook.BuildBytes(direct.Document, direct.SupplementSheets);
        Check("J39 warm and cold workbook bytes equal the independent uncached census", workbook.SequenceEqual(
            ReportSupplementWorkbook.BuildBytes(cold.Export.Document, cold.Export.SupplementSheets)) && workbook.SequenceEqual(
            ReportSupplementWorkbook.BuildBytes(warm.Export.Document, warm.Export.SupplementSheets)));
        rows[1].DeNovoEvidence![1].Sequence = "ACDEFAHIK";
        Check("J39 an alternate non-folded source claim invalidates the census", Mode(Build().Progress, "miss"));
        rows[1].DeNovoEvidence![1].Sequence = "ACDEFAHLK";
        rows[1].DeNovoEvidence![1].SourceId = DeNovoSource.DenovoOnly.Id;
        Check("J39 a per-caller identity change invalidates the census", Mode(Build().Progress, "miss"));
        rows[1].DeNovoEvidence![1].SourceId = DeNovoSource.DeepNovo.Id;
        double originalMz = rows[1].PrecursorMz;
        rows[1].PrecursorMz += .1;
        Check("J39 precursor overlap inputs invalidate the census", Mode(Build().Progress, "miss"));
        rows[1].PrecursorMz = originalMz;
        rows[1].Run = "run-b";
        Check("J39 source run changes invalidate the census", Mode(Build().Progress, "miss"));
        rows[1].Run = "run-a";
        rows[1].Charge = 3;
        Check("J39 precursor charge changes invalidate the census", Mode(Build().Progress, "miss"));
        rows[1].Charge = 2;
        string originalSequence = file.MyExtendedFastaItems[0].Sequence;
        file.MyExtendedFastaItems[0].Sequence += "W";
        Check("J39 protein sequence changes invalidate the census", Mode(Build().Progress, "miss"));
        file.MyExtendedFastaItems[0].Sequence = originalSequence;
        file.MyExtendedFastaItems[0].UserID = 9;
        Check("J39 family regrouping invalidates cached family census rows", Mode(Build().Progress, "miss"));
        file.MyExtendedFastaItems[0].UserID = 1;
        file.MyExtendedFastaItems[0].UserAnnotation = "Edited family";
        Check("J39 family-name changes invalidate cached census descriptions", Mode(Build().Progress, "miss"));
        file.MyExtendedFastaItems[0].UserAnnotation = string.Empty;
        manifest.FamilyMapping.MinIdentity = .8;
        Check("J39 report search option changes invalidate the census", Mode(Build().Progress, "miss"));
        manifest.FamilyMapping.MinIdentity = .99;
        string[] entries = Directory.Exists(Path.Combine(cache, "source-census"))
            ? Directory.GetFiles(Path.Combine(cache, "source-census"), "*.fqcensus") : Array.Empty<string>();
        if (entries.Length > 0) foreach (string entry in entries) File.WriteAllText(entry, "corrupt fixture");
        var repaired = Build();
        Check("J39 corrupted census cache is ignored and recomputed without changing data", entries.Length > 0
            && Mode(repaired.Progress, "miss") && SameCsv(direct, repaired.Export));
        string blockedCache = Path.Combine(root, "not-a-directory"); File.WriteAllText(blockedCache, "fixture");
        var blockedProgress = new J32Progress();
        var blocked = ReportProjectExporter.Build(rows, manifest, file, Path.Combine(root, "blocked"),
            operation: new OperationContext(CancellationToken.None, blockedProgress), homologyCacheDirectory: blockedCache);
        Check("J39 an unavailable cache preserves fresh report data", Mode(blockedProgress, "miss") && SameCsv(direct, blocked));
        using var cancellation = new CancellationTokenSource();
        var cancelProgress = new J39ProgressAction(e => { if (e.Stage == "report-census-cache" && e.Message.Contains("miss", StringComparison.OrdinalIgnoreCase)) cancellation.Cancel(); });
        bool cancelled = false; string cancelCache = Path.Combine(root, "cancel-cache");
        try { ReportProjectExporter.Build(rows, manifest, file, Path.Combine(root, "cancel"),
            operation: new OperationContext(cancellation.Token, cancelProgress), homologyCacheDirectory: cancelCache); }
        catch (OperationCanceledException) { cancelled = true; }
        Check("J39 cancellation before census does not publish a census entry", cancelled
            && (!Directory.Exists(Path.Combine(cancelCache, "source-census")) || Directory.GetFiles(Path.Combine(cancelCache, "source-census"), "*.fqcensus").Length == 0));
        bool changedDuringCensus = false;
        var mutateProgress = new J39ProgressAction(e => { if (!changedDuringCensus && e.Stage == "report-census-identified-end")
            { changedDuringCensus = true; rows[1].DeNovoEvidence![1].Sequence = "ACDEFAHIK"; } });
        bool refusedChanged = false;
        try { ReportProjectExporter.Build(rows, manifest, file, Path.Combine(root, "changed"),
            operation: new OperationContext(CancellationToken.None, mutateProgress), homologyCacheDirectory: Path.Combine(root, "mutation-cache")); }
        catch (InvalidDataException ex) when (ex.Message.Contains("inputs changed", StringComparison.Ordinal)) { refusedChanged = true; }
        Check("J39 changed source inputs during a census are refused", changedDuringCensus && refusedChanged);
    }
}
