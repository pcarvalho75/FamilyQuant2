using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportTableSheetCommands() =>
    [
        new("--test-report-table-sheets", 439, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-table-sheets", (args, _) =>
            {
                ReportTableSheetTests(args.Length > 1 ? args[1] : null);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void ReportTableSheetTests(string? output)
    {
        Section("Every printed report table - methods, exact values and unrounded supplements");
        const double mean = .12345678901234567, cutoff = 81.23456789012345, ppm = 12.345678901234567;
        var project = new FamilyClusterFile { SourceSequenceCount = 17, ClusteringIdentity = 87,
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "source_a", Sequence = "PEPTIDEK", UserID = 1 },
                new() { SequenceIdentifier = "source_b", Sequence = "AAAAAAAK", UserID = 2 },
                new() { SequenceIdentifier = SequenceLabels.ContaminantPrefix + "source", Sequence = "CCCCCCCK" },
                new() { SequenceIdentifier = SequenceLabels.DecoyPrefix + "source", Sequence = "KEDITPEP" },
                new() { SequenceIdentifier = SequenceLabels.DecoyPrefix + SequenceLabels.ContaminantPrefix + "source", Sequence = "KAAAAAAA" },
            } };
        var shares = new FamilyRunSharesResult { Method = "Stored arithmetic mean and sample SD", MembershipSha256 = FamilyStudyMembership.Fingerprint(project),
            Runs = new() { new() { Run = "run_1", AssignedSignal = 100, MeasuredSignal = 200, TotalIonCurrent = 1000 },
                new() { Run = "run_2", AssignedSignal = 100, MeasuredSignal = 200, TotalIonCurrent = 1000 } } };
        foreach (var (id, share) in new[] { (1, mean), (2, 1 - mean) })
            shares.Families.Add(new() { FamilyUserId = id, MeanShare = share, StandardDeviation = 0, CoefficientOfVariation = 0,
                MeanShareOfMeasuredSignal = share / 2, StandardDeviationOfMeasuredSignal = 0, AvailableRunCount = 2,
                PooledSignal = share * 200, PooledShare = share, PooledMinusMeanShare = 0,
                Runs = new() { new() { Run = "run_1", Signal = share * 100, ShareOfAssignedSignal = share, ShareOfMeasuredSignal = share / 2, ShareOfTic = share / 10 },
                    new() { Run = "run_2", Signal = share * 100, ShareOfAssignedSignal = share, ShareOfMeasuredSignal = share / 2, ShareOfTic = share / 10 } } });
        var manifest = new QuantManifest { Ppm = ppm, Ms1Tolerance = 31, IsotopeCount = 6, MinPointsForQuant = 3,
            FamilyRunShares = shares, ScientificInputFingerprints = new() { ["fixture_0001.raw"] = new string('a', 64) },
            DeNovoInputs = new() { new() { SourceId = "fixture-caller", SourceDisplayName = "Fixture caller", ConfidenceField = "Native score",
                MinimumConfidence = cutoff, SourcePriority = 2, RawRows = 37, RowsPassingSourceFilter = 23,
                FilePath = "fixture_0001.csv", Sha256 = new string('b', 64) } },
            DeNovoSourceScoreDistributions = new() { new() { SourceId = "fixture-caller", SourceDisplayName = "Fixture caller",
                MinLength = 9, StretchLength = 4, StretchFloor = 93 } } };
        var facts = new ReportInputProvenance { DatabaseFileName = "fixture_database.fasta", DatabaseFileSha256 = new string('c', 64),
            SeproFileName = "fixture.sepr2", SearchParameterFileName = "fixture_2026.params", SearchParameterSha256 = new string('d', 64),
            SearchParameters = new[] { new ReportSearchParameter("Minimum length", "5"),
                new ReportSearchParameter("Repeated setting", "first"), new ReportSearchParameter("Repeated setting", "second") } };
        var source = ReportDocumentBuilder.Build(project, manifest, new() { Title = "Synthetic complete table fixture" }, facts);
        var current = ReportCurrentResults.Build(new AnalyticsData(), manifest, null);
        var packet = ReportStoredResults.Apply(current, manifest, new Dictionary<int, string> { [1] = "First group", [2] = "Second group" },
            FamilyReportThreshold.Build(shares, project));
        var replacements = packet.Sections.ToDictionary(s => s.Id, StringComparer.Ordinal);
        source = source with { Sections = source.Sections.Select(s => replacements.GetValueOrDefault(s.Id) ?? s).ToArray() };
        var layout = ReportSupplementLayout.Apply(source, packet.CsvExports);
        var printed = layout.Document.Sections.SelectMany(s => s.Blocks.OfType<ReportTable>().Select(t => (Owner: s.Id, Table: t))).ToArray();
        byte[] word = WordReportWriter.BuildBytes(layout.Document);
        byte[] workbook = ReportSupplementWorkbook.BuildBytes(layout.Document, layout.Sheets);
        var book = ReadSupplement(workbook);
        var index = book["Contents"].Skip(1).ToDictionary(row => row[3], row => row[0], StringComparer.Ordinal);
        string[][]? Cells(string id) => index.TryGetValue(id, out string? name) && book.TryGetValue(name, out var cells) ? cells : null;
        Check("every printed table has exactly one identified supplement worksheet", printed.All(p => Cells(p.Table.Id) != null)
            && book["Contents"].Skip(1).Select(r => r[3]).Distinct(StringComparer.Ordinal).Count() == book.Count - 1);
        var methods = printed.Where(p => p.Owner == ReportSectionIds.Methods).Select(p => p.Table).ToArray();
        Check("all method tables retain every printed value without extra rounding", methods.Length == 6 && methods.All(t =>
            Cells(t.Id) is { } cells && cells.Skip(1).Select(r => string.Join("\t", r)).SequenceEqual(t.Rows.Select(r => string.Join("\t", r)))));
        Check("database count sheet retains targets, decoys and both contaminant classes", Cells("methods-project") is { } database
            && new[] { ("Records in the saved database", "5"), ("Targets (including target contaminants)", "3"), ("Decoys", "2"),
                ("Target contaminants", "1"), ("Decoy contaminants", "1") }.All(p => database.Skip(1).Any(r => r[0] == p.Item1 && r[1] == p.Item2)));
        Check("source sheet keeps native cutoff, read count, passing count and file identity", Cells("methods-denovo") is { } calls
            && calls[1].SequenceEqual(new[] { "Fixture caller", "Native score", cutoff.ToString("R", CultureInfo.InvariantCulture), "2", "37", "23", "fixture_0001.csv" }));
        Check("rule sheet retains exact ppm and duplicate search settings in order", Cells("methods-rules") is { } rules
            && rules.Skip(1).Single(r => r[0] == "Extraction ppm")[1] == ppm.ToString("R", CultureInfo.InvariantCulture)
            && rules.Skip(1).Where(r => r[0] == "Repeated setting").Select(r => r[1]).SequenceEqual(new[] { "first", "second" }));
        Check("scientific input names and fingerprints have their own worksheet", Cells("methods-inputs") is { } inputs
            && inputs.Skip(1).Any(r => r.SequenceEqual(new[] { "fixture_0001.raw", new string('a', 64) }))
            && inputs.Skip(1).Any(r => r.SequenceEqual(new[] { "fixture_2026.params", new string('d', 64) })));
        Check("method worksheet headers state that Word keeps stored text unchanged", methods.All(t => Cells(t.Id) is { } cells
            && cells[0].Any(c => c.Contains("Word", StringComparison.Ordinal) && c.Contains("unchanged", StringComparison.Ordinal))));
        var headline = printed.Single(p => p.Table.Id == "family-run-means").Table;
        var first = headline.Rows.Single(r => r[0] == "First group");
        Check("fixture distinguishes rounded Word percent from the unrounded value", first[1] == (100 * mean).ToString("G6", CultureInfo.InvariantCulture)
            && first[1] != (100 * mean).ToString("R", CultureInfo.InvariantCulture));
        Check("rounded printed percentages keep unrounded numeric cells with explicit header guidance", Cells(headline.Id) is { } headlineCells
            && headlineCells.Skip(1).Single(r => r[0] == "First group")[1] == (100 * mean).ToString("R", CultureInfo.InvariantCulture)
            && headlineCells[0][1].Contains("6 significant digits", StringComparison.Ordinal));
        Check("supplement also retains the exact stored fraction behind each displayed percent", Cells(headline.Id) is { } nativeCells
            && nativeCells[0].Select((h, i) => (h, i)).Any(p => p.h.Contains("Stored value for Mean share (%)", StringComparison.Ordinal)
                && nativeCells.Skip(1).Single(r => r[0] == "First group")[p.i] == mean.ToString("R", CultureInfo.InvariantCulture)));
        Check("existing supplement sheets keep their names, order and every source cell", book.Keys.Take(layout.Sheets.Count + 1)
            .SequenceEqual(new[] { "Contents" }.Concat(layout.Sheets.Select(s => s.Name))) && layout.Sheets.All(s =>
                book[s.Name].Select(r => string.Join("\t", r)).SequenceEqual(new[] { s.Table.Columns }.Concat(s.Table.Rows).Select(r => string.Join("\t", r)))));
        var excluded = new ReportSelection { ExcludedIds = new[] { ReportSectionIds.Methods, "family-run-means" } };
        var selected = ReadSupplement(ReportSupplementWorkbook.BuildBytes(layout.Document, layout.Sheets, excluded));
        Check("method-section and printed-table exclusions remove exactly their new worksheets", selected["Contents"].Skip(1)
            .All(r => r[1] != "Methods" && r[3] != "family-run-means") && selected["Contents"].Length < book["Contents"].Length);
        var wrongOwner = new ReportSupplementSheet("methods-project", ReportSectionIds.Results, "Wrong owner", methods.Single(t => t.Id == "methods-project"));
        Check("a supplied printed-table sheet cannot bypass section exclusions with a different owner",
            Throws<ArgumentException>(() => ReportSupplementWorkbook.BuildBytes(layout.Document, layout.Sheets.Concat(new[] { wrongOwner }).ToArray())));
        ReportTable Percent(string shown, string csv) => ReportTableSupplements.WithStoredPercentages(
            new ReportTable("percentage-fixture", "Percentage fixture", new[] { "Share (%)" },
                new[] { (IReadOnlyList<string>)new[] { shown } }, ""), csv, new[] { "mean_pct" });
        byte[] PercentBook(ReportTable table) => ReportSupplementWorkbook.BuildBytes(new ReportDocument { Sections = new[]
            { new ReportSection("results", "Results", new ReportBlock[] { table }) } }, Array.Empty<ReportSupplementSheet>());
        Check("missing native values never silently pass rounded cells off as unrounded",
            Throws<InvalidDataException>(() => PercentBook(Percent("12.3457", "mean_pct\n12.3457\n"))));
        Check("a stored fraction must reproduce the Word display before publication",
            Throws<InvalidDataException>(() => PercentBook(Percent("12.3457", "mean_pct,mean_fraction\n12.3457,0.2\n"))));
        var unavailable = ReadSupplement(PercentBook(Percent("Unavailable", "mean_pct,mean_fraction\n,\n")));
        var noRate = unavailable.Single(p => p.Key != "Contents").Value;
        Check("unavailable native values remain empty and their Word state remains explicit",
            noRate[1][0] == "Unavailable" && noRate[1][1] == "" && noRate[1][2] == "Unavailable");
        var old = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            Check("complete table supplement bytes are culture invariant", workbook.SequenceEqual(ReportSupplementWorkbook.BuildBytes(layout.Document, layout.Sheets)));
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
        Check("adding supplement sheets does not rewrite any Word table or its rounding", word.SequenceEqual(WordReportWriter.BuildBytes(layout.Document)));
        using (var zip = new ZipArchive(new MemoryStream(word)))
        using (var xml = zip.GetEntry("word/document.xml")!.Open())
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var actual = XDocument.Load(xml).Descendants(w + "tbl").Select(t => t.Elements(w + "tr").Select(r => r.Elements(w + "tc")
                .Select(c => string.Join("\n", c.Descendants(w + "t").Select(n => n.Value))).ToArray()).ToArray()).ToArray();
            Check("actual DOCX method cells equal the native supplement data cells", actual.Length == printed.Length && methods.All(t =>
                Cells(t.Id) is { } cells && actual[Array.FindIndex(printed, p => p.Table.Id == t.Id)].Skip(1)
                    .Select(r => string.Join("\t", r)).SequenceEqual(cells.Skip(1).Select(r => string.Join("\t", r)))));
        }
        string folder = output ?? Path.Combine(Path.GetTempPath(), "FamilyQuant.TableSheets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string documentPath = Path.Combine(folder, "synthetic-tables.docx"), sidecars = Path.Combine(folder, "source");
        File.WriteAllBytes(documentPath, word);
        File.WriteAllBytes(Path.Combine(folder, "synthetic-tables_supplement.xlsx"), workbook);
        Directory.CreateDirectory(sidecars);
        foreach (var export in packet.CsvExports) File.WriteAllText(Path.Combine(sidecars, export.Key), export.Value);
        int traceExit = ReportNumberTrace.Run(documentPath, sidecars, Path.Combine(folder, "trace.csv"));
        Check("J18 traces every input, setting and other printed fixture number to a native export", traceExit == 0);
        foreach (var method in methods) Console.WriteLine(method.Id + " -> " + (index.GetValueOrDefault(method.Id) ?? "MISSING"));
    }
}
