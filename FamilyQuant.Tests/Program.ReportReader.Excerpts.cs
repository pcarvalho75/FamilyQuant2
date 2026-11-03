using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReportReaderPanelExcerptTests()
    {
        const string lead = "Descrição e DECISÃO FamilyQuant: Núcleo; ";
        const string tail = " Está nas ";
        string TextWithTokenAt(string token, int position) => lead
            + new string('x', position - "Campo: ".Length - lead.Length - tail.Length)
            + tail + token + " originais? Texto adicional de referência.";
        ReportSupplementLayoutResult Layout(string raw, string field = "Campo") => ReportSupplementLayout.Apply(
            new ReportDocument { Sections = new[] { new ReportSection("family:synthetic", "Synthetic family", new ReportBlock[]
            {
                new ReportTable("family:synthetic:text", "User panel text", new[] { "Field", "User text" },
                    new IReadOnlyList<string>[] { new[] { field, raw } }, "Complete original panel text."),
            }) } }, new Dictionary<string, string>());
        string Brief(ReportSupplementLayoutResult layout) => layout.Document.Sections.Single().Blocks.OfType<ReportParagraph>()
            .Single(p => p.Id == "family:synthetic:panel-brief").Text;
        foreach (string token in new[] { "27", "Núcleo", "0.12345" })
        {
            var raw = TextWithTokenAt(token, 299);
            var layout = Layout(raw);
            Check(token + " panel excerpt stops before a partial word or numeric token",
                Brief(layout).EndsWith("Está nas ...", StringComparison.Ordinal));
            Check(token + " full panel text remains verbatim in the native supplement",
                layout.Sheets.Single(s => s.Id == "family:synthetic:full-detail").Table.Rows.Any(row => row[3] == raw));
            using var package = new ZipArchive(new MemoryStream(WordReportWriter.BuildBytes(layout.Document)));
            using var reader = new StreamReader(package.GetEntry("word/document.xml")!.Open());
            var xml = XDocument.Parse(reader.ReadToEnd());
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            string text = string.Join("\n", xml.Descendants(w + "t").Select(t => t.Value));
            Check(token + " Portuguese accents are retained in the actual Word text",
                text.Contains("Descrição", StringComparison.Ordinal) && text.Contains("DECISÃO", StringComparison.Ordinal)
                && text.Contains("Núcleo", StringComparison.Ordinal) && text.Contains("Está nas", StringComparison.Ordinal));
        }
        Check("a complete number that fits at the excerpt boundary is retained",
            Brief(Layout(TextWithTokenAt("27", 298))).EndsWith("Está nas 27 ...", StringComparison.Ordinal));
        string longField = new('L', 350);
        Check("an over-budget first token is kept whole rather than cut",
            Brief(Layout("Remaining complete panel text.", longField)).Contains(longField + ":", StringComparison.Ordinal));
    }
}
