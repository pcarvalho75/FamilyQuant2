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
        args => args.Length > 0 && args[0] == "--test-report-reader", (args, _) =>
        {
            ReportReaderTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "reader-proof"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportReaderTests(string output)
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
        ReportReaderQualificationTests(data);
        var families = ReportReaderFamilyTests(data);
        ReportReaderDocumentTests(data, families, output);
        ReportReaderCensusTraceTests();
        ReportReaderWorkbookReferenceTests();
        ReportReaderPanelExcerptTests();
    }

    private static void ReportReaderCensusTraceTests()
    {
        var table = ReportFamilySourceSequences.Build(new FamilySourceSequenceSummary());
        string caption = FamilyQuant.Report.Captions.ReportCaptionBuilder.Table(table,
            new FamilyQuant.Report.Captions.TableCaptionInput(table.Title, table.Caption, SummaryColumns: Array.Empty<int>()));
        Check("census caption does not substitute an unstored fixed precursor tolerance",
            !caption.Contains("20-ppm", StringComparison.Ordinal));
        Check("supplement-only caption does not invent trace tokens for its shape",
            !caption.Contains("The table shows", StringComparison.Ordinal));
        var methods = ReportMethods.Build(new FamilyClusterFile(), new QuantManifest
            { DeNovoOverlap = new DeNovoOverlapSummary { PrecursorTolerancePpm = 17.125 } });
        Check("Methods retain the recorded precursor tolerance rather than a default",
            methods.Blocks.OfType<ReportTable>().SelectMany(t => t.Rows)
                .Any(row => row[0] == "Overlap precursor tolerance (ppm)" && row[1] == "17.125"));
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
        var sourceWidths = tables[0].Element(w + "tblGrid")!.Elements(w + "gridCol").Select(c => (int)c.Attribute(w + "w")!).ToArray();
        Check("wide source tables retain their native grid with more space for paths",
            sourceWidths.Length == wide.Columns.Count && sourceWidths[^1] > sourceWidths[2]
            && tables[0].Elements(w + "tr").Skip(1).SelectMany(r => r.Elements(w + "tc"))
                .Select(c => string.Join("\n", c.Descendants(w + "t").Select(t => t.Value))).SequenceEqual(wide.Rows.SelectMany(r => r)));
        var headerFace = new Typeface(new FontFamily("Calibri"), System.Windows.FontStyles.Normal,
            System.Windows.FontWeights.Bold, System.Windows.FontStretches.Normal);
        Check("numeric source headers fit without splitting their words",
            Enumerable.Range(2, 4).All(column => wide.Columns[column].Split(' ').All(word =>
                new FormattedText(word, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                    headerFace, 11 * 96.0 / 72, ChartPalette.Light.Ink, 1).Width * 72 / 96 + 10 <= sourceWidths[column] / 20.0)));
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

    private static void ReportReaderQualificationTests(AnalyticsData data)
    {
        data.Grouped!.DetectedPeptideLikeArea = 300;
        data.Grouped.DetectedAreaMultiplyCharged = 200;
        data.Grouped.DetectedAreaSinglyCharged = 100;
        var names = new[] { DeNovoSource.DenovoOnly.DisplayName, DeNovoSource.DeNovoAll.DisplayName, DeNovoSource.DeepNovo.DisplayName,
            DeNovoVariantComparison.OnlyPlusDeepNovoVariantName, DeNovoVariantComparison.AllPlusDeepNovoVariantName };
        var variants = names.Select((name, i) => new DeNovoVariantSummary
        {
            Variant = name, Sources = i < 3 ? name : (i == 3 ? names[0] : names[1]) + " + " + names[2],
            GenerationId = "reader", GeneratedUtc = "2026-01-01", DatasetFingerprint = "reader", DeNovoInputFingerprint = name,
            QuantitationSettings = "reader", AppVersion = "reader", IdentifiedFractionOfTic = .28,
            DeNovoModelEstimateFractionOfTic = .1 + i * .01, CombinedModelEstimateFractionOfTic = .38 + i * .01,
        }).ToList();
        var manifest = new QuantManifest
        {
            PresentationDeNovoVariants = variants,
            DeNovoCallerAgreement = new DeNovoAgreementResult { PeaksObservedMinimumConfidence = 50, DeepNovoObservedMinimumConfidence = 60 },
            DeNovoAcceptedScoreScatter = new DeNovoAcceptedScoreScatter
            {
                PeaksMinimumAcceptedScore = 80, DeepNovoMinimumAcceptedScore = 75,
                Points = new() { new() { ScanNumber = 1, Charge = 2, PeaksCleanSequence = "ACDEFGHIK", DeepNovoCleanSequence = "ACDEFGHIK", StrictSequenceMatch = true } },
            },
            PresentationIdentitySweep = new()
            {
                new() { Identity = 95, Groups = 3, TargetGroups = 2, MixedGroups = 0 },
                new() { Identity = 75, Groups = 3, TargetGroups = 2, MixedGroups = 0 },
                new() { Identity = 70, Groups = 2, TargetGroups = 2, MixedGroups = 1, Score = .5 },
            },
        };
        var plan = ReportFigureRenderer.Plan(data, manifest, new FamilyClusterFile { ClusteringIdentity = 70 }, null);
        ReportChart Figure(string file) => plan.Charts.Single(c => c.FileName == file);
        var recovery = Figure(AnalyticsFigures.RecoveryFile);
        Check("recovery caption carries the complete charge caveat and recorded split",
            recovery.Caption.Contains(Ms1EnvelopeDetector.SinglyChargedCaveat, StringComparison.Ordinal)
            && recovery.Caption.Contains("z >= 2", StringComparison.Ordinal)
            && recovery.Caption.Contains("z = 1", StringComparison.Ordinal)
            && recovery.CaptionInput!.Numbers.Any(n => n.Label.Contains("charge 2 or higher", StringComparison.Ordinal)
                && n.Label.Contains("TIC", StringComparison.Ordinal) && n.Value == 20)
            && recovery.CaptionInput.Numbers.Any(n => n.Label.Contains("charge 1 / TIC", StringComparison.Ordinal) && n.Value == 10));
        var raw = Figure(PresentationFigures.DeNovoAgreementFile);
        Check("raw caller caption names imported population and observed source floors",
            raw.Caption.Contains("downstream quantitation filters are not applied", StringComparison.Ordinal)
            && raw.CaptionInput!.Numbers.Any(n => n.Label.Contains("observed", StringComparison.OrdinalIgnoreCase) && n.Value == 50));
        var accepted = Figure(PresentationFigures.DeNovoAcceptedScoreScatterFile);
        Check("accepted-pair caption names complete source filters and source-specific gates",
            accepted.Caption.Contains(manifest.DeNovoAcceptedScoreScatter.PeaksFilterDescription, StringComparison.Ordinal)
            && accepted.Caption.Contains(DeNovoAcceptedScoreScatter.ScoreScaleCaveat, StringComparison.Ordinal)
            && accepted.CaptionInput!.Numbers.Any(n => n.Label.Contains("cutoff", StringComparison.OrdinalIgnoreCase) && n.Value == 75));
        var identity = Figure("identity_scan.png");
        Check("identity caption explains both axes and records the floor separately from this file",
            identity.Caption.Contains("not an identification FDR", StringComparison.Ordinal)
            && identity.CaptionInput!.Numbers.Any(n => n.Label.Contains("floor", StringComparison.OrdinalIgnoreCase) && n.Value == 75)
            && identity.CaptionInput.Numbers.Any(n => n.Label.Contains("file", StringComparison.OrdinalIgnoreCase) && n.Value == 70));
        string variantCaption = Figure(PresentationFigures.DeNovoVariantComparisonFile).Caption;
        Check("variant caption keeps source confidence rules without the removed model-sensitivity whiskers",
            variantCaption.Contains("Each source keeps its recorded confidence rule", StringComparison.Ordinal)
            && !variantCaption.Contains("whisker", StringComparison.OrdinalIgnoreCase)
            && !variantCaption.Contains("model sensitivity", StringComparison.OrdinalIgnoreCase));
        Check("source-effect caption names the exact union rather than the separate headline variant",
            Figure(PresentationFigures.DeNovoTalkComparisonFile).Caption.Contains(DeNovoVariantComparison.OnlyPlusDeepNovoVariantName, StringComparison.Ordinal)
            && Figure(PresentationFigures.DeNovoTalkComparisonFile).Caption.Contains(DeNovoVariantComparison.AllPlusDeepNovoVariantName, StringComparison.Ordinal));
    }

    private static FamilyEnvelopeQuantResult ReportReaderFamilyTests(AnalyticsData data)
    {
        var families = FamilyFixture(12);
        families.SourceSequenceSummary.SearchInputDistinctSequences = 24;
        foreach (var family in families.Families)
        {
            family.Description = "Synthetic complete family name with a long descriptive architecture " + family.FamilyUserId;
            families.SourceSequenceSummary.Families.Add(new()
            {
                FamilyUserId = family.FamilyUserId, Description = family.Description, SearchDistinctSequences = 4,
                PeaksDeNovoOnlyExclusiveDistinctSequences = 5, SharedPeaksDeepNovoDistinctSequences = 2,
                DeepNovoExclusiveDistinctSequences = 3, DeNovoUnionDistinctSequences = 10,
            });
        }
        var plan = ReportFigureRenderer.Plan(data, new QuantManifest(), new FamilyClusterFile(), families);
        var source = plan.Charts.Where(c => c.FileName.StartsWith("family_source_sequence_counts", StringComparison.Ordinal)).ToArray();
        Check("source census caption declares the selected top eight and the complete population",
            source.All(c => c.CaptionInput!.Numbers.Any(n => n.Label.Contains("Selected", StringComparison.Ordinal) && n.Value == 8)
                && c.CaptionInput.Numbers.Any(n => n.Label.Contains("census", StringComparison.Ordinal) && n.Value == 12)
                && c.CaptionInput.Numbers.Any(n => n.Label == "Input identified distinct sequences" && n.Value == 24)));
        Check("abbreviated family labels point to complete names by id",
            plan.Charts.Where(c => c.FileName == AnalyticsFigures.FamilyContributionFile || c.FileName == PresentationFigures.FamilySignalShareFile)
                .All(c => c.Caption.Contains("family id", StringComparison.OrdinalIgnoreCase)
                    && c.Caption.Contains("companion workbook", StringComparison.Ordinal)));
        var tables = ReportCurrentResults.Build(data, new QuantManifest(), families).Sections.SelectMany(s => s.Blocks).OfType<ReportTable>();
        Check("the complete source census retains every family and full name in a supplement table",
            tables.Any(t => t.Id == "family-source-sequences" && t.SupplementOnly && t.Rows.Count == 12
                && families.Families.All(f => t.Rows.Any(row => row.Contains(f.Description)))));
        foreach (var chart in source)
        foreach (var theme in new[] { ChartPaletteVariant.Light, ChartPaletteVariant.Dark })
        {
            var native = chart.CreateChart();
            ChartImageExporter.Save(native, Path.Combine(Path.GetTempPath(), "reader-figures", theme.ToString(), chart.FileName), chart.Width, chart.Height, variant: theme);
            var glyphs = ReaderGlyphs(VisualTreeHelper.GetDrawing(native)).ToArray();
            double pointsPerDip = Math.Min(468 / chart.Width, 360 / chart.Height);
            Check(chart.FileName + " " + theme + " source labels are readable at print size",
                glyphs.Length > 0 && glyphs.All(g => g.GlyphRun.FontRenderingEmSize * pointsPerDip >= 10)
                && !glyphs.Any(g => new string(g.GlyphRun.Characters.ToArray()).Contains("toxin", StringComparison.OrdinalIgnoreCase)));
            Check(chart.FileName + " " + theme + " full name labels remain in bounds",
                glyphs.All(g => g.Bounds.Left >= 0 && g.Bounds.Right <= chart.Width && g.Bounds.Top >= 0 && g.Bounds.Bottom <= chart.Height));
        }
        return families;
    }

    private static void ReportReaderDocumentTests(AnalyticsData data, FamilyEnvelopeQuantResult families, string output)
    {
        var manifest = new QuantManifest { UpdatedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc) };
        for (int index = 0; index < 6; index++) manifest.DeNovoInputs.Add(new()
        {
            SourceId = "synthetic-" + index, SourceDisplayName = "Synthetic source " + index,
            ConfidenceField = "Native confidence", MinimumConfidence = 80, SourcePriority = index,
            RawRows = 100, RowsPassingSourceFilter = 80,
            FilePath = "C:/Synthetic inputs/Long input folder/Acquisition with technical replicate number " + index + "/source.denovo.csv",
        });
        var curation = new FamilyClusterFile { ReportDetails = new()
        {
            Title = "Synthetic J34 reader fixture", Authors = "Synthetic author", SampleDescription = "Synthetic sample",
        } };
        var packet = ReportProjectExporter.Build(SyntheticAnalyticsRows(), manifest, curation, Path.Combine(output, "figures"),
            familyResults: families, analyticsData: data);
        Check("Database gap unavailability is printed once",
            packet.Document.Sections.Single(s => s.Id == ReportSectionIds.DatabaseGap).Blocks.OfType<ReportParagraph>()
                .Count(p => p.Text.StartsWith("Database gap:", StringComparison.OrdinalIgnoreCase)) == 1);
        var cover = packet.Document.Sections.Single(s => s.Id == ReportSectionIds.Title).Blocks.OfType<ReportParagraph>().ToArray();
        Check("cover prints the stored sample and snapshot date without inventing metadata",
            cover.Any(p => p.Text.Contains("Synthetic sample", StringComparison.Ordinal))
            && cover.Any(p => p.Text.Contains("2026-01-02", StringComparison.Ordinal)));
        Check("empty interpretation has an explicit missing-text notice",
            packet.Document.Sections.Single(s => s.Id == ReportSectionIds.Interpretation).Blocks.OfType<ReportParagraph>()
                .Any(p => p.Text.Contains("No written interpretation", StringComparison.Ordinal)));
        byte[] bytes = WordReportWriter.BuildBytes(packet.Document);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = XDocument.Parse(reader.ReadToEnd());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        Check("print contents carry page-reference fields with real heading bookmarks",
            xml.Descendants(w + "fldSimple").Any(f => ((string?)f.Attribute(w + "instr"))?.StartsWith("PAGEREF ", StringComparison.Ordinal) == true)
            && zip.GetEntry("word/settings.xml") != null);
        ReportProjectExporter.Write(packet, Path.Combine(output, "reader-proof.docx"));
        Check("native reader proof writes Word and complete workbook",
            File.Exists(Path.Combine(output, "reader-proof.docx")) && File.Exists(Path.Combine(output, "reader-proof_supplement.xlsx")));
    }
}
