using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportAssemblyCommandTable() => new[]
    {
        new TestCommand("--test-report-assembly", 10, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-report-assembly", (args, work) =>
            {
                ReportAssemblyChecks(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "fq-report-assembly-" + Guid.NewGuid().ToString("N")));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            })
    };

    private static void ReportAssemblyChecks(string folder)
    {
        Section("Complete report assembly - producer packets, support adjacency and pair parity");
        EnsureApplication();
        var (manifest, project, _) = ReportStudyFigureFixture();
        // This fixture exercises stored studies without claiming verified production panel evidence.
        project.FamilyPanel = null; project.PanelProteinAssignments = null; project.ProteinSubclassLabels = null;
        project.ReportDetails = new() { Title = "Synthetic complete report fixture", Authors = "Synthetic fixture",
            WrittenInterpretation = "Synthetic stored interpretation; no biological result is claimed." };
        var rows = SyntheticAnalyticsRows();
        var nativeFamilies = FamilyFixture(3);
        var old = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        ReportProjectExport? first = null; byte[]? firstWord = null, firstBook = null;
        try
        {
            foreach (string locale in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                var export = ReportProjectExporter.Build(rows, manifest, project, Path.Combine(folder, locale + "-figures"), familyResults: nativeFamilies);
                byte[] word = WordReportWriter.BuildBytes(export.Document);
                byte[] book = ReportSupplementWorkbook.BuildBytes(export.Document, export.SupplementSheets);
                if (first == null) { first = export; firstWord = word; firstBook = book; }
                else
                {
                    Check("complete assembled Word bytes match across locales", firstWord!.SequenceEqual(word));
                    Check("complete assembled workbook bytes match across locales", firstBook!.SequenceEqual(book));
                }
                string path = Path.Combine(folder, "synthetic-complete-" + locale + ".docx");
                ReportProjectExporter.Write(export, path);
            }
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
        var blocks = first!.Document.Sections.Single(s => s.Id == ReportSectionIds.Results).Blocks;
        int source = blocks.ToList().FindLastIndex(b => b is ReportFigure f && f.Id.StartsWith("figure:family_signal_sources", StringComparison.Ordinal));
        Check("source figure has its stored support table immediately beside it", source >= 0 && blocks[source + 1] is ReportTable { Id: "source-world-support" });
        var support = (ReportTable)blocks[source + 1];
        Check("unavailable source rates are explicit and retain evidence states", support.Rows.All(r => r[1] == "Unavailable" && r[2] == "Unavailable" && r[5] == "Unavailable"));
        Check("new and existing native figures share one binding-complete document", first.Figures.Any(f => f.Figure.Id.Contains("family_composition", StringComparison.Ordinal))
            && first.Figures.Any(f => f.Figure.Id == "figure:" + AnalyticsFigures.XicByMassFile)
            && first.Document.Sections.SelectMany(s => s.Blocks).OfType<ReportFigure>().All(f => f.Caption.Length > 0));
        Check("every source support cell has its native CSV companion", first.CsvExports.ContainsKey("family_sources_support.csv"));
        Check("caption facts are tied to figure ids for exact selection exclusion", first.SupplementSheets.Any(s => s.Id.StartsWith("figure:", StringComparison.Ordinal)
            && s.Table.Columns.Contains("Displayed value")));
    }
}
