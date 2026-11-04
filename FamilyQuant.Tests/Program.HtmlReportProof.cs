using FamilyQuant.Homology;
using FamilyQuant.Operations;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlStoredProofCommands() =>
    [new("--export-stored-html-report", 10497, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--export-stored-html-report", (args, _) => ExportStoredHtmlReport(args))];

    private sealed class HtmlProofProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    { public void Report(OperationProgress value) => report(value); }

    private static int ExportStoredHtmlReport(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Use --export-stored-html-report <copied-project> <copied-delivered-word> <fresh-output>.");
        if (typeof(ReportProjectExport).GetProperty("HtmlContext") == null)
            throw new InvalidOperationException("Integrate the three-file exporter before running this proof.");
        string project = Path.GetFullPath(args[1]), delivered = Path.GetFullPath(args[2]);
        var stage = new DeliveryWorkspace(args[3], false);
        string projectHash = DeliveryWorkspace.Sha256(project), deliveredHash = DeliveryWorkspace.Sha256(delivered);
        using var log = new StreamWriter(stage.PathFor("stored-export.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
        var gate = new object();
        void Log(string text) { lock (gate) { log.WriteLine(text); Console.WriteLine(text); } }
        var operation = HtmlStoredOnlyOperation(Log);
        try
        {
            var (curation, rows, manifest) = ProjectStore.ReadFqproj(project);
            FamilyStudyMembership.RequireCurrent(curation, manifest);
            string snapshot = HtmlStoredSnapshot(curation, rows, manifest);
            var hits = CurationHomologyCache.GetOrSearch(rows, curation, options: HomologyDefaults.ForStoredMapping(manifest.FamilyMapping),
                progress: hit => { Log("primary retained-hit cache " + (hit ? "hit" : "miss"));
                    if (!hit) throw new OperationCanceledException("Stored-only proof stopped before a primary cache-miss search. No Compute is authorized."); });
            string censusCache = Path.Combine(ApplicationPaths.LocalDataDirectory, FullXicComputer.CacheFolderName, "source-census");
            var families = HtmlStoredCensusFamilies(rows, FamilyEnvelopeQuant.AttributeHits(hits, curation), operation, censusCache);
            EnsureApplication();
            ReportProjectExport? prepared = null;
            AppOperationFiles.RunSta(() => prepared = ReportProjectExporter.Build(rows, manifest, curation,
                stage.PathFor(".work/png"), familyResults: families, operation: operation)).GetAwaiter().GetResult();
            if (prepared == null) throw new InvalidDataException("The native producer returned no report.");
            if (snapshot != HtmlStoredSnapshot(curation, rows, manifest))
                throw new OperationCanceledException("Stored-only proof stopped: report preparation changed an input value. No publication performed.");
            var table = prepared.Document.Sections.SelectMany(s => s.Blocks).OfType<ReportTable>()
                .Single(t => t.Id is "panel-family-run-means" or "family-run-means" && !t.SupplementOnly);
            var expected = DeliveredHeadline(delivered);
            var actual = table.Rows.Select(row => (Name: row[0], Mean: row[1])).ToArray();
            if (!actual.SequenceEqual(expected))
                throw new OperationCanceledException("Stored-only proof stopped: native headline differs from delivered Word. No publication performed.");
            string word = stage.PathFor("report.docx"), workbook = ReportSupplementWorkbook.PathForReport(word), html = stage.PathFor("report.html");
            byte[] expectedWord = WordReportWriter.BuildBytes(prepared.Document, prepared.Selection);
            byte[] expectedBook = ReportSupplementWorkbook.BuildBytes(prepared.Document, prepared.SupplementSheets, prepared.Selection);
            ReportProjectExporter.Write(prepared, word, operation: operation);
            if (!File.Exists(html) || !expectedWord.SequenceEqual(File.ReadAllBytes(word)) || !expectedBook.SequenceEqual(File.ReadAllBytes(workbook)))
                throw new InvalidDataException("The three-file export is incomplete or HTML changed the Word/workbook bytes.");
            if (projectHash != DeliveryWorkspace.Sha256(project) || deliveredHash != DeliveryWorkspace.Sha256(delivered)
                || snapshot != HtmlStoredSnapshot(curation, rows, manifest))
                throw new InvalidDataException("An input changed during the stored-only export.");
            stage.WriteText("export-proof.json", JsonSerializer.Serialize(new { ProjectSha256 = projectHash, DeliveredWordSha256 = deliveredHash,
                StoredSnapshotSha256 = snapshot, InputsUnchanged = true, AllHeadlineLabelsAndMeansEqualDeliveredWord = true,
                HeadlineRows = actual.Length, WordBytesUnchangedByHtml = true, WorkbookBytesUnchangedByHtml = true,
                HtmlBytes = new FileInfo(html).Length, HtmlSha256 = DeliveryWorkspace.Sha256(html),
                Mode = "Stored results and retained hits only; cache-miss searches refused; no UI open or Compute" }, new JsonSerializerOptions { WriteIndented = true }));
            if (typeof(ReportNumberTrace).GetMethod("RunHtml", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic) == null)
            { Log("EXPORT COMPLETE; HTML trace awaits its owned source dependency. Not claiming M1o complete."); return 4; }
            int traceExit = ReportNumberTrace.Run(html, Path.GetDirectoryName(project)!, stage.PathFor("html-trace.csv"));
            if (projectHash != DeliveryWorkspace.Sha256(project) || deliveredHash != DeliveryWorkspace.Sha256(delivered)
                || snapshot != HtmlStoredSnapshot(curation, rows, manifest))
                throw new InvalidDataException("An input changed during the stored-only proof.");
            var trace = ReportCsvTable.Build("html-proof", "Trace", File.ReadAllText(stage.PathFor("html-trace.csv")), "");
            int status = trace.Columns.ToList().IndexOf("status");
            int missing = trace.Rows.Count(r => r[status] == "NOT FOUND");
            var files = new[] { word, workbook, html }.Select(path => new { File = Path.GetFileName(path), Bytes = new FileInfo(path).Length,
                Sha256 = DeliveryWorkspace.Sha256(path) }).ToArray();
            stage.WriteText("proof.json", JsonSerializer.Serialize(new { ProjectSha256 = projectHash, DeliveredWordSha256 = deliveredHash,
                StoredSnapshotSha256 = snapshot, InputsUnchanged = true, AllHeadlineLabelsAndMeansEqualDeliveredWord = true,
                HeadlineRows = actual.Length, WordBytesUnchangedByHtml = true, WorkbookBytesUnchangedByHtml = true,
                TraceRows = trace.Rows.Count, NotFound = missing, Files = files,
                Mode = "Stored results and verified retained hits only; cache-miss searches refused; no UI open or Compute" }, new JsonSerializerOptions { WriteIndented = true }));
            Log("PASS stored-only triple export: " + actual.Length + " headline rows equal delivered Word; " + trace.Rows.Count
                + " trace rows, " + missing + " NOT FOUND; original and stored inputs unchanged.");
            return traceExit;
        }
        catch (OperationCanceledException ex)
        {
            stage.WriteText("guarded-stop.json", JsonSerializer.Serialize(new { Stopped = true, Reason = ex.Message,
                ProjectUnchanged = projectHash == DeliveryWorkspace.Sha256(project), DeliveredWordUnchanged = deliveredHash == DeliveryWorkspace.Sha256(delivered) },
                new JsonSerializerOptions { WriteIndented = true }));
            Log("STOP: " + ex.Message); return 3;
        }
    }

    private static string HtmlStoredSnapshot(FamilyClusterFile curation, IReadOnlyList<QuantRow> rows, QuantManifest manifest)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        JsonSerializer.Serialize(stream, new { Project = curation.JsonContentSha256(), Rows = rows, Manifest = manifest },
            new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
        stream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash!);
    }

    private static OperationContext HtmlStoredOnlyOperation(Action<string>? log = null, Action<OperationProgress>? observe = null)
        => new(progress: new HtmlProofProgress(p =>
        {
            observe?.Invoke(p); log?.Invoke(p.Stage + ": " + p.Message);
            if (p.Message.Contains("cache miss", StringComparison.OrdinalIgnoreCase)
                || p.Stage == "attribution-cache" && p.Message.StartsWith("Searching", StringComparison.Ordinal))
                throw new OperationCanceledException("Stored-only proof stopped before a cache-miss search. No Compute is authorized.");
            if (p.Stage is "report-census-identified-start" or "report-census-denovo-start"
                && !p.Message.EndsWith("cached", StringComparison.Ordinal))
                throw new OperationCanceledException("Stored-only proof stopped before an uncached census search-start phase.");
        }));

    private static FamilyEnvelopeQuantResult HtmlStoredCensusFamilies(IReadOnlyList<QuantRow> rows,
        PeptideFamilyAttribution attribution, OperationContext operation, string censusCache)
    {
        if (string.IsNullOrWhiteSpace(censusCache) || !Path.IsPathFullyQualified(censusCache) || !Directory.Exists(censusCache))
            throw new OperationCanceledException("The explicit stored source-census cache directory is absent. No search is authorized.");
        // Four arguments deliberately select the independently keyed cached census path. A missing,
        // stale or corrupt entry reports its miss before invoking the search delegate, which the
        // synchronous operation guard refuses. The three-argument uncached overload is forbidden.
        return FamilyEnvelopeQuant.BuildFromAttribution(rows, attribution, operation, censusCache);
    }
}
