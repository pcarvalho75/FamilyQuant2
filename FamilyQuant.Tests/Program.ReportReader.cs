using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportReaderCommandTable() =>
    [new("--test-report-reader", 440, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-report-reader", (_, _) =>
        {
            ReportReaderTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportReaderTests()
    {
        Section("Report reader - labelled counting grains and print layout");
        EnsureApplication();
        var data = AnalyticsBuilder.Build(SyntheticAnalyticsRows(), new Dictionary<string, double>());
        // Deliberately different populations: copying AnalyticsData.DeNovoCount is incorrect here.
        data.Grouped = new NativeRunFraction { IsGrouped = true, DeNovoPeptides = 7, IdentifiedPeptides = 11 };
        var manifest = new QuantManifest { DeNovoIdentification = new DeNovoIdentificationSummary() };
        var plan = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile(), null);
        var figure = plan.Charts.Single(c => c.FileName == PresentationFigures.PeptideEvidenceFile);
        var facts = figure.CaptionInput!.Numbers;
        Check("representative caption names the same credited count as its figure",
            facts.Any(n => n.Label.Contains("Credited de novo", StringComparison.Ordinal) && n.Value == 7)
            && figure.Caption.Contains("after cross-side suppression", StringComparison.Ordinal));
        Check("pre-suppression representatives are separately named",
            facts.Any(n => n.Label.Contains("before cross-side suppression", StringComparison.Ordinal)
                && n.Label.Contains("de novo", StringComparison.Ordinal) && n.Value == data.DeNovoCount));
        ReportReaderTableTests();
        ReportReaderNativeTests(data);
    }

    private static void ReportReaderTableTests()
    {
        var wide = new ReportTable("wide", "Source inputs", new[] { "Source", "Score", "Minimum", "Priority", "Raw rows", "Passing rows", "Input path" },
            new[] { (IReadOnlyList<string>)new[] { "Synthetic caller", "Native confidence", "80", "1", "12345", "8280", "C:/Synthetic input/very long folder/source.denovo.csv" } }, "Stored values.");
        var audit = new ReportTable("audit", "Audit", new[] { "Stage", "Count", "Denominator", "Definition" },
            new[] { (IReadOnlyList<string>)new[] { "precursor_qualified_one_to_one_pairs", "8280", "14892", "A long definition of the precise comparison population, not all shared scans." } }, "Audit values.");
        var document = new ReportDocument { Sections = new[] { new ReportSection("tables", "Tables", new ReportBlock[] { wide, audit }) } };
        byte[] bytes = WordReportWriter.BuildBytes(document);
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(bytes), false))
            Check("reader table package satisfies the Word schema", !new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package).Any());
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = XDocument.Parse(reader.ReadToEnd());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var tables = xml.Descendants(w + "tbl").ToArray();
        Check("wide source records use readable field/value tables",
            tables[0].Element(w + "tblGrid")!.Elements(w + "gridCol").Count() == 2
            && wide.Rows[0].All(value => xml.Descendants(w + "t").Any(t => t.Value == value)));
        Check("narrative audit columns get more width than numeric counts",
            tables[^1].Element(w + "tblGrid")!.Elements(w + "gridCol")
                .Select(c => (int)c.Attribute(w + "w")!).Distinct().Count() > 1);
        Check("every printed table row stays together and repeats its header",
            tables.All(t => t.Elements(w + "tr").All(r => r.Element(w + "trPr")?.Element(w + "cantSplit") != null)
                && t.Elements(w + "tr").First().Element(w + "trPr")?.Element(w + "tblHeader") != null));
    }

    private static void ReportReaderNativeTests(AnalyticsData data)
    {
        data.Grouped!.Tic = 1000;
        data.Grouped.IdentifiedEnvelope = 280;
        data.Grouped.DeNovoEnvelopeReferenceCalibrated = 151;
        var overlap = new DeNovoOverlapSummary
        {
            RawDeNovoRows = 100, FilteredDeNovoRows = 100, NoSearchPsmInFileAndScan = 90, SharedFileAndScan = 10,
            DifferentChargeInSharedScan = 4, SameFileScanAndCharge = 6, SameChargeButNoMzMatch = 3,
            DifferentPrecursorInSharedScan = 7, SamePrecursorWithinTolerance = 3,
            ScanLevelStrictSequenceMatchesIgnoringPtm = 2, ScanLevelSequenceDivergencesIgnoringPtmStrict = 4,
            ScanLevelSequenceDivergencesAfterIleLeuFold = 4, StrictSequenceMatchesIgnoringPtm = 1,
            SequenceDivergencesIgnoringPtmStrict = 2, AdditionalIleLeuEquivalentMatches = 1,
            SequenceDivergencesAfterIleLeuFold = 1,
        };
        var plan = ReportFigureRenderer.Plan(data, new QuantManifest { DeNovoOverlap = overlap,
            IdentificationConcordance = new IdentificationConcordanceSummary() }, new FamilyClusterFile(), null);
        foreach (string file in new[] { PresentationFigures.DeNovoSearchCrosscheckFile, PresentationFigures.SignalUpliftFile })
        {
            var chart = plan.Charts.Single(c => c.FileName == file);
            foreach (var theme in new[] { ChartPaletteVariant.Light, ChartPaletteVariant.Dark })
            {
                var native = chart.CreateChart();
                string target = Path.Combine(Path.GetTempPath(), "reader-figures", theme.ToString(), file);
                ChartImageExporter.Save(native, target, chart.Width, chart.Height, variant: theme);
                var glyphs = ReaderGlyphs(VisualTreeHelper.GetDrawing(native)).ToArray();
                double pointsPerDip = Math.Min(468 / chart.Width, 360 / chart.Height);
                Check(file + " " + theme + " has print-readable text",
                    glyphs.Length > 0 && glyphs.All(g => g.GlyphRun.FontRenderingEmSize * pointsPerDip >= 10));
                Check(file + " " + theme + " labels remain in the figure",
                    glyphs.All(g => g.Bounds.Left >= 0 && g.Bounds.Top >= 0
                        && g.Bounds.Right <= chart.Width && g.Bounds.Bottom <= chart.Height));
            }
        }
    }

    private static IEnumerable<GlyphRunDrawing> ReaderGlyphs(Drawing drawing) => drawing switch
    {
        GlyphRunDrawing glyph => new[] { glyph },
        DrawingGroup group => group.Children.SelectMany(ReaderGlyphs),
        _ => Array.Empty<GlyphRunDrawing>(),
    };
}
