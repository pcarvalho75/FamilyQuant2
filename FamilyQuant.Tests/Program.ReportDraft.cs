using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void WordReportDraftTests()
    {
        Section("Draft report - exact native CSV cells and honest missing studies");
        var table = ReportCsvTable.Build("fixture", "Fixture", "name,value,missing\r\n\"a, b\"\"c\r\nline\",0.125,\r\n", "Fixture caption");
        Check("CSV commas, escaped quotes, multiline text and empty numbers preserved", table.Rows.Single().SequenceEqual(new[] { "a, b\"c\r\nline", "0.125", "" }));
        bool refused = false;
        try { ReportCsvTable.Build("bad", "Bad", "a,b\n1\n", ""); } catch (InvalidDataException) { refused = true; }
        Check("inconsistent source row width fails", refused);
        refused = false;
        try { ReportCsvTable.Build("bad", "Bad", "a,b\n1,2\n", "", "absent"); } catch (InvalidDataException) { refused = true; }
        Check("missing export column fails instead of inventing values", refused);
        var data = AnalyticsBuilder.Build(SyntheticAnalyticsRows(), new Dictionary<string, double> { ["ui_01"] = 5e8, ["ui_02"] = 4e8 });
        var families = FamilyFixture(3, unattributedIdentified: 4000);
        var packet = ReportCurrentResults.Build(data, new QuantManifest(), families);
        var native = ReportCsvTable.Build("source", "Source", packet.CsvExports["family_composition_credited.csv"], "", "family_user_id", "description", "signal_primary_estimate", "fraction");
        var printed = packet.Sections.Single(s => s.Id == ReportSectionIds.Results).Blocks.OfType<ReportTable>().Single(t => t.Id == "composition-assigned");
        Check("every composition cell is copied from its native CSV sidecar", printed.Rows.All(row => native.Rows.Any(source => row.SequenceEqual(source))));
        Check("pooled composition explicitly labelled", printed.Title.Contains("all runs pooled", StringComparison.Ordinal));
        Check("new uncomputed results are explicit", packet.Sections.Single(s => s.Id == ReportSectionIds.Results).Blocks.OfType<ReportParagraph>()
            .Any(p => p.Id == "family-runs-unavailable" && p.Text.Contains("not computed in this project", StringComparison.Ordinal)));
        var overlapPacket = ReportCurrentResults.Build(data, new QuantManifest(), families, SharedPeptidePolicy.CountInEveryFamily);
        Check("overlapping full counts are never normalized as composition", !overlapPacket.CsvExports.ContainsKey("family_composition_credited.csv")
            && overlapPacket.Sections.SelectMany(s => s.Blocks).OfType<ReportParagraph>().Any(p => p.Id == "composition-overlap-warning"));
        Check("report data do not require a talk-only study", packet.CsvExports.ContainsKey("signal_by_run.csv") && packet.CsvExports.ContainsKey("peptide_representatives.csv"));
        EnsureApplication();
        var mapRows = new[] { Qr("identified", "report_fixture", 1, 2, "ACDEFGHIK", 100, 150),
            Qr("denovo", "report_fixture", 2, 2, "ACDEFAHIK", 50, 70) };
        var mapFile = new FamilyClusterFile { ReportDetails = new() { Title = "Synthetic saved-project report", WrittenInterpretation = "Synthetic stored interpretation." },
            MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "SYNTHETIC_REPORT_TARGET", Sequence = "TTACDEFGHIKGG", Description = "Synthetic report mapping", UserID = 1, USearchID = 1 },
        } };
        var mapManifest = new QuantManifest { PerRunTic = new() { ["report_fixture"] = 100000 },
            FamilyMapping = new() { MinIdentity = .99, Method = "Synthetic recorded mapping" } };
        var expected = FamilyEnvelopeQuant.BuildFromCuration(mapRows, mapFile, new Homology.HomologyOptions { MinIdentity = .99 });
        string fixtureFolder = Path.Combine(Path.GetTempPath(), "fq-report-draft-" + Guid.NewGuid().ToString("N"));
        var generated = ReportProjectExporter.Build(mapRows, mapManifest, mapFile, fixtureFolder);
        var actualTable = ReportCsvTable.Build("mapping", "Mapping", generated.CsvExports["family_composition_measured.csv"], "");
        Check("draft family mapping honors the project's recorded identity gate", actualTable.Rows.Single(r => r[0] == "1")[2]
            == expected.Families.Single().Signal(SharedPeptidePolicy.FamilyUnique).ToString(System.Globalization.CultureInfo.InvariantCulture));
        string fixtureProject = Path.Combine(fixtureFolder, "synthetic-project.fqproj");
        ProjectStore.WriteFqproj(fixtureProject, mapFile, mapRows, mapManifest);
        string fixtureReport = Path.Combine(fixtureFolder, "synthetic-cli-report.docx");
        Check("native saved-project report command writes the complete draft", RunWordProjectExport(new[] { "--export-word-report", fixtureProject, fixtureReport }, fixtureFolder) == 0);
        using var zip = new System.IO.Compression.ZipArchive(File.OpenRead(fixtureReport));
        using var xmlReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        string documentXml = xmlReader.ReadToEnd();
        Check("reopened project details and interpretation reach the CLI document", documentXml.Contains("Synthetic saved-project report", StringComparison.Ordinal)
            && documentXml.Contains("Synthetic stored interpretation.", StringComparison.Ordinal));
    }
}
