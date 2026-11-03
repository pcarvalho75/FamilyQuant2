using FamilyQuant.Report;
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void WordReportTests(string? output)
    {
        Section("Word report - deterministic document and original figure bytes");
        var writer = typeof(ReportDocument).Assembly.GetType("FamilyQuant.Report.WordReportWriter");
        Check("Word report writer is implemented in Core", writer != null);
        if (writer == null) return;
        var build = writer.GetMethod("BuildBytes", new[] { typeof(ReportDocument), typeof(ReportSelection) });
        Check("writer exposes the document and selection entry point", build != null);
        if (build == null) return;
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6MpIAAAAASUVORK5CYII=");
        var document = new ReportDocument
        {
            Title = "Synthetic report < & >", Authors = "Test author", ApplicationVersion = "fixture-1",
            Sections = new[]
            {
                new ReportSection(ReportSectionIds.Title, "Title", new ReportBlock[] { new ReportParagraph("title-note", "Synthetic document fixture") }),
                new ReportSection(ReportSectionIds.Contents, "Contents", Array.Empty<ReportBlock>()),
                new ReportSection(ReportSectionIds.Methods, "Methods", new ReportBlock[]
                {
                    new ReportParagraph("method-text", "Value 1.25; user text á\nSecond paragraph."),
                    new ReportTable("method-table", "Inputs", new[] { "File", "SHA-256" },
                        new[] { (System.Collections.Generic.IReadOnlyList<string>)new[] { "a.raw", "abcd" } }, "Stored input fingerprints."),
                    new ReportFigure("figure-one", "Test figure", png, "Original test PNG.", 1, 1),
                }),
                new ReportSection(ReportSectionIds.Interpretation, "Interpretation", new ReportBlock[] { new ReportParagraph("notes", "Saved user interpretation.") }),
            },
        };
        byte[] Bytes(ReportDocument doc, ReportSelection? selection = null) =>
            (byte[])build.Invoke(null, new object?[] { doc, selection })!;
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            byte[] portuguese = Bytes(document);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            byte[] english = Bytes(document);
            if (output != null) { Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, "synthetic-report.docx"), english); }
            Check("entire DOCX bytes equal under pt-BR and en-US", portuguese.SequenceEqual(english));
            Check("repeat export bytes equal", english.SequenceEqual(Bytes(document)));
            using (var validateStream = new MemoryStream(english))
            using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(validateStream, false))
            {
                var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package).ToList();
                Check("DOCX passes Open XML schema validation", errors.Count == 0,
                    string.Join("; ", errors.Select(e => e.Path?.XPath + ": " + e.Description)));
            }
            using var stream = new MemoryStream(english);
            using var zip = new ZipArchive(stream);
            string Read(string name) { using var reader = new StreamReader(zip.GetEntry(name)!.Open()); return reader.ReadToEnd(); }
            var xml = XDocument.Parse(Read("word/document.xml"));
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var texts = xml.Descendants(w + "t").Select(n => n.Value).ToList();
            Check("title and saved notes are printed", texts.Contains(document.Title) && texts.Contains("Saved user interpretation."));
            Check("user text and XML metacharacters preserved", texts.Contains("Value 1.25; user text á") && texts.Contains("Second paragraph."));
            Check("table cells and captions printed", texts.Contains("abcd") && texts.Contains("Stored input fingerprints.") && texts.Contains("Original test PNG."));
            var media = zip.Entries.Where(e => e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
            using var imageBytes = new MemoryStream();
            using (var imageStream = media.Single().Open()) imageStream.CopyTo(imageBytes);
            Check("embedded figure is byte-equal to its PNG export", imageBytes.ToArray().SequenceEqual(png));
            Check("core dates fixed, not export time", Read("docProps/core.xml").Contains("2000-01-01T00:00:00Z"));
            Check("every ZIP entry has the deterministic timestamp", zip.Entries.All(e => e.LastWriteTime.Year == 2000));
            Check("headings use built-in styles for contents", xml.Descendants(w + "pStyle").Any(n => (string?)n.Attribute(w + "val") == "Heading1"));
            var drawingParagraph = xml.Descendants(w + "p").Single(p => p.Descendants(w + "drawing").Any());
            var captionParagraph = drawingParagraph.ElementsAfterSelf(w + "p").First();
            Check("figure stays with its complete caption", drawingParagraph.Element(w + "pPr")?.Element(w + "keepNext") != null
                && captionParagraph.Element(w + "pPr")?.Element(w + "keepLines") != null);
            byte[] selected = Bytes(document, new ReportSelection { ExcludedIds = new[] { "figure-one", ReportSectionIds.Interpretation } });
            using var selectedZip = new ZipArchive(new MemoryStream(selected));
            using var selectedReader = new StreamReader(selectedZip.GetEntry("word/document.xml")!.Open());
            string selectedXml = selectedReader.ReadToEnd();
            Check("excluding a section and figure removes exactly those parts", !selectedXml.Contains("Saved user interpretation.")
                && !selectedXml.Contains("Original test PNG.") && selectedXml.Contains("abcd")
                && !selectedZip.Entries.Any(e => e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
            bool refused = false;
            try { Bytes(document with { Sections = document.Sections.Concat(new[] { document.Sections[0] }).ToArray() }); }
            catch (System.Reflection.TargetInvocationException e) when (e.InnerException is ArgumentException) { refused = true; }
            Check("duplicate section ids refused", refused);
            var project = new FamilyClusterFile
            {
                SourceSha256 = "saved-database-hash", SourceSequenceCount = 4,
                MyExtendedFastaItems = new()
                {
                    new() { SequenceIdentifier = "target", Sequence = "PEPTIDE" },
                    new() { SequenceIdentifier = SequenceLabels.ContaminantPrefix + "target", Sequence = "PEPTIDE" },
                    new() { SequenceIdentifier = SequenceLabels.DecoyPrefix + "target", Sequence = "EDITPEP" },
                    new() { SequenceIdentifier = SequenceLabels.DecoyPrefix + SequenceLabels.ContaminantPrefix + "target", Sequence = "EDITPEP" },
                },
            };
            var manifest = new QuantManifest
            {
                Ppm = 12.5, IsotopeCount = 4, MinPointsForQuant = 2, AppVersion = "saved-version",
                ScientificInputFingerprints = new() { ["saved.raw"] = "saved-raw-hash" },
                DeNovoInputs = new() { new() { SourceDisplayName = "Saved caller", ConfidenceField = "SCORE", MinimumConfidence = 81.5,
                    FilePath = "saved.csv", Sha256 = "saved-csv-hash" } },
            };
            var details = new ReportDetails { Title = "Saved title", Authors = "Saved authors", SampleDescription = "Saved sample",
                Workflow = "Saved workflow", DatabaseKind = "Saved kind", WrittenInterpretation = "Saved notes\nSecond note" };
            var facts = new ReportInputProvenance { DatabaseFileName = "original.fasta", SeproFileName = "original.sepr2",
                SearchParameterFileName = "original.params", SearchParameterSha256 = "saved-params-hash",
                SearchParameters = new[] { new ReportSearchParameter("Search minimum length", "5"),
                    new ReportSearchParameter("Repeated key", "first"), new ReportSearchParameter("Repeated key", "second") } };
            var generated = ReportDocumentBuilder.Build(project, manifest, details, facts);
            project.ReportDetails = details;
            var fromSavedDetails = ReportDocumentBuilder.Build(project, manifest);
            Check("saved report details are used without caller overrides", fromSavedDetails.Title == details.Title
                && fromSavedDetails.Authors == details.Authors
                && fromSavedDetails.Sections.Single(s => s.Id == ReportSectionIds.Interpretation).Blocks.OfType<ReportParagraph>().Single().Text == details.WrittenInterpretation);
            string TableCell(string label) => generated.Sections.Single(s => s.Id == ReportSectionIds.Methods).Blocks.OfType<ReportTable>()
                .SelectMany(t => t.Rows).Single(r => r[0] == label)[1];
            Check("methods count actual database records and label classes", TableCell("Targets (including target contaminants)") == "2"
                && TableCell("Decoys") == "2" && TableCell("Target contaminants") == "1" && TableCell("Decoy contaminants") == "1");
            Check("methods print stored extraction settings, not defaults", TableCell("Extraction ppm") == "12.5"
                && TableCell("Isotope ceiling") == "4" && TableCell("Minimum points per cluster/isotope") == "2");
            Check("all report-detail fields reach their document places", generated.Title == details.Title && generated.Authors == details.Authors
                && TableCell("Sample") == details.SampleDescription && TableCell("Workflow") == details.Workflow && TableCell("Database kind") == details.DatabaseKind
                && generated.Sections.Single(s => s.Id == ReportSectionIds.Interpretation).Blocks.OfType<ReportParagraph>().Single().Text == details.WrittenInterpretation);
            Check("original input names and search settings come from captured facts", TableCell("Database input") == "original.fasta"
                && TableCell("SEPro input") == "original.sepr2" && TableCell("Search minimum length") == "5");
            Check("search parameter order and duplicate keys preserved", generated.Sections.Single(s => s.Id == ReportSectionIds.Methods)
                .Blocks.OfType<ReportTable>().Single(t => t.Id == "methods-rules").Rows.Where(r => r[0] == "Repeated key")
                .Select(r => r[1]).SequenceEqual(new[] { "first", "second" }));
            var methodText = string.Join("\n", generated.Sections.SelectMany(s => s.Blocks).OfType<ReportTable>().SelectMany(t => t.Rows).SelectMany(r => r));
            Check("all recorded input names, hashes and source score gates printed", new[] { "saved.raw", "saved-raw-hash", "saved.csv", "saved-csv-hash", "saved-database-hash", "SCORE", "81.5" }.All(methodText.Contains));
            Check("unrecorded methods marked plainly", ReportMethods.Build(project, new QuantManifest()).Blocks.OfType<ReportTable>()
                .Single(t => t.Id == "methods-rules").Rows.Single(r => r[0] == "Search settings")[1] == "Not recorded");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            byte[] generatedPt = Bytes(ReportDocumentBuilder.Build(project, manifest, details, facts));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("project-built report is culture invariant", generatedPt.SequenceEqual(Bytes(ReportDocumentBuilder.Build(project, manifest, details, facts))));
            if (output != null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllBytes(Path.Combine(output, "synthetic-report.docx"), english);
                File.WriteAllBytes(Path.Combine(output, "synthetic-methods.docx"), Bytes(generated));
            }
        }
        finally { CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUi; }
    }
}
