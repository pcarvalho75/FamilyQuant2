using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlReportSampleCommands() =>
    [new("--html-payload-sample", 10496, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--html-payload-sample", (args, _) => HtmlReportSample(args))];

    /// <summary>Program-produced stored-study sample, not an alternate scientific result route.
    /// Uses the normal stored-study report builders; never opens the UI, searches or calls Compute.</summary>
    private static int HtmlReportSample(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Use --html-payload-sample <copied-project> <copied-delivered-word> <fresh-source-folder> <payload.json>.");
        string project = Path.GetFullPath(args[1]), delivered = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[4]);
        if (output.Equals(project, StringComparison.OrdinalIgnoreCase) || output.Equals(delivered, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The sample must not overwrite its source inputs.");
        var stage = new DeliveryWorkspace(args[3], false);
        string before = DeliveryWorkspace.Sha256(project), wordBefore = DeliveryWorkspace.Sha256(delivered);
        var (file, rows, manifest) = ProjectStore.ReadFqproj(project);
        FamilyStudyMembership.RequireCurrent(file, manifest);
        var basis = ReportCompositionBasis.Select(file, manifest);
        if (!basis.IsIdentity) throw new InvalidOperationException("This bounded sample route requires an identity composition basis; use the full prepared export for a collapsed unit.");
        var empty = new[] { ReportSectionIds.Summary, ReportSectionIds.Results, ReportSectionIds.Quality,
            ReportSectionIds.Sensitivity, ReportSectionIds.DatabaseGap, ReportSectionIds.Supplements }
            .Select(id => new ReportSection(id, id, Array.Empty<ReportBlock>())).ToArray();
        var packet = ReportStoredResults.Apply(new(empty, new Dictionary<string, string>()), manifest, basis.Labels, basis.Grouping, file);
        packet = basis.Apply(packet);
        var replacements = packet.Sections.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var document = ReportDocumentBuilder.Build(file, manifest);
        document = document with { Sections = document.Sections.Select(s => replacements.GetValueOrDefault(s.Id) ?? s).ToArray() };
        var layout = ReportSupplementLayout.Apply(document, packet.CsvExports);
        var sheets = ReportTableSupplements.Complete(layout.Document, layout.Sheets);
        var context = new HtmlReportContext { StoredStudies = basis.Studies, UsesPanel = basis.UsesPanel,
            IsIdentity = basis.IsIdentity, IsAdditive = basis.Studies.FamilyRunShares?.Policy != SharedPeptidePolicy.CountInEveryFamily };
        var payload = HtmlReportPayloadBuilder.Build(layout.Document, sheets, file.ReportSelection, context);
        var actual = payload.Composition.Families.Select(f => (Name: payload.Fields[f.Name].Text,
            Mean: f.Mean == null ? "" : payload.Fields[f.Mean].Text)).ToArray();
        var expected = DeliveredHeadline(delivered);
        if (!actual.SequenceEqual(expected)) throw new InvalidDataException("Program sample headline differs from the copied delivered Word cells.");
        string sampleWord = stage.PathFor("report.docx"), sampleBook = stage.PathFor("report_supplement.xlsx");
        WordReportWriter.Write(sampleWord, layout.Document, file.ReportSelection);
        ReportSupplementWorkbook.Write(sampleBook, layout.Document, sheets, file.ReportSelection);
        File.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
        if (before != DeliveryWorkspace.Sha256(project) || wordBefore != DeliveryWorkspace.Sha256(delivered))
            throw new InvalidDataException("A source changed during the stored-only sample command.");
        stage.WriteText("sample-proof.json", JsonSerializer.Serialize(new
        {
            ProjectSha256 = before, DeliveredWordSha256 = wordBefore, SourceUnchanged = true,
            Project = project, DeliveredWord = delivered, Payload = output, NativeWord = sampleWord, NativeWorkbook = sampleBook,
            Families = actual.Length, Fields = payload.Fields.Count, AllHeadlineLabelsAndMeansEqualDeliveredWord = true,
            Mode = "Native stored-study report builders; no UI open, RAW extraction, homology search or Compute",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS native stored-only payload: " + actual.Length + " headline rows, " + payload.Fields.Count
            + " bound fields; every headline label/mean equals delivered Word; source hashes unchanged. " + output);
        return 0;
    }

    private static (string Name, string Mean)[] DeliveredHeadline(string path)
    {
        using var word = WordprocessingDocument.Open(path, false);
        foreach (var table in word.MainDocumentPart!.Document!.Body!.Elements<W.Table>())
        {
            var rows = table.Elements<W.TableRow>().ToArray();
            string Text(W.TableCell cell) => string.Join("\n", cell.Elements<W.Paragraph>().Select(p => p.InnerText));
            var headers = rows[0].Elements<W.TableCell>().Select(Text).ToArray();
            if (headers.Length != 7 || headers[0] != "Family" || headers[1] != "Mean share (%)") continue;
            return rows.Skip(1).Select(row => row.Elements<W.TableCell>().Select(Text).ToArray())
                .Select(cells => (cells[0], cells[1])).ToArray();
        }
        throw new InvalidDataException("The copied delivered Word has no canonical headline table.");
    }
}
