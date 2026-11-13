using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportFamilyNameCommands() =>
    [new("--test-report-family-names", 10720, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-report-family-names", (args, _) =>
        {
            ReportFamilyNameTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "report-family-names"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ReportFamilyNameTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var fixture = EffectivePanelFixture();
        var panel = fixture.File.FamilyPanel!;
        panel.Import = new() { FamilySheet = "Families", ColumnHeaders = new() { [PanelImportFields.OtherName] = "Other label" } };
        panel.Families.Single(family => family.Key == "main").Names.Add(new() { Name = "Other label", Text = "Alternate main" });
        panel.Families.Single(family => family.Key == "other").Names.Add(new() { Name = "Other label", Text = " " });
        foreach (var protein in fixture.File.MyExtendedFastaItems)
            protein.UserAnnotation = protein.UserID == 33 ? "Other group" : "Main group";
        fixture = PrepareEffectivePanelFixture(fixture.File, fixture.Rows);
        fixture.Manifest.DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method;
        fixture.Manifest.FormatVersion = ProjectStore.CurrentFormatVersion;
        fixture.Manifest.SpectrumComparisonRowCount = fixture.Manifest.SpectrumComparisonRows?.Count ?? 0;
        EffectivePanelFamilyStudy.Store(fixture.Manifest, fixture.Assigned, fixture.Hits, fixture.File);
        var pooled = FamilyEnvelopeQuant.BuildFromAttributedPeptides(fixture.Assigned);
        string storedStudies = JsonSerializer.Serialize(fixture.Manifest);
        string sourceRows = JsonSerializer.Serialize(fixture.Rows);
        var snapshots = new List<string[]>();
        foreach (var choice in new[] { ReportFamilyNameChoice.Name, ReportFamilyNameChoice.OtherLanguageName })
        {
            fixture.File.ReportFamilyNames = choice;
            string name = choice == ReportFamilyNameChoice.Name ? "Main group" : "Alternate main";
            string folder = Path.Combine(output, choice.ToString()); Directory.CreateDirectory(folder);
            var export = ReportProjectExporter.Build(fixture.Rows, fixture.Manifest, fixture.File, Path.Combine(folder, "figures"), familyResults: pooled);
            string word = Path.Combine(folder, "report.docx");
            ReportProjectExporter.Write(export, word);
            var payload = ReadHtmlPayload(HtmlReportWriter.PathForReport(word));
            Check("NM1 HTML ranking uses selected panel name " + choice, payload.Composition.Families.Any(family => payload.Fields[family.Name].Text == name));
            Check("NM1 blank other-language names keep Name " + choice, payload.Composition.Families.Any(family => payload.Fields[family.Name].Text == "Other group"));
            Check("NM1 HTML native figures use selected name " + choice, payload.FamilyFigures.Figures.SelectMany(figure => figure.Views.Values)
                .SelectMany(view => view.Rows).Any(row => payload.Fields[row.Name].Text.Contains(name, StringComparison.Ordinal)));
            Check("NM1 coverage selectors use selected name " + choice, payload.CoverageCatalog!.Families.Any(family => family.Name.Contains(name, StringComparison.Ordinal)));
            using (var zip = ZipFile.OpenRead(word))
            using (var stream = zip.GetEntry("word/document.xml")!.Open())
            {
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var headings = XDocument.Load(stream).Descendants(w + "p").Where(paragraph => paragraph.Element(w + "pPr")?.Element(w + "pStyle") != null)
                    .Select(paragraph => string.Concat(paragraph.Descendants(w + "t").Select(text => text.Value))).ToArray();
                Check("NM1 Word family heading uses selected name " + choice, headings.Contains(name));
            }
            Check("NM1 Word/workbook/HTML values and labels trace together " + choice,
                ReportNumberTrace.RunHtmlBindings(payload, word, ReportSupplementWorkbook.PathForReport(word), fixture.File, fixture.Manifest, Path.Combine(folder, "trace.csv")) == 0);
            snapshots.Add(payload.Fields.Values.Where(field => field.Value.HasValue).Select(field => JsonSerializer.Serialize(new { field.Value, field.Unit, field.Source }))
                .Order(StringComparer.Ordinal).ToArray());
            string path = Path.Combine(folder, "project.fqproj");
            ProjectStore.WriteFqproj(path, fixture.File, fixture.Rows, fixture.Manifest);
            var reopened = ProjectStore.ReadFqproj(path);
            Check("NM1 choice survives normal project save and reopen " + choice, reopened.fcf.ReportFamilyNames == choice
                && ReportCompositionBasis.Select(reopened.fcf, reopened.manifest).Labels.Values.Contains(name));
            var model = new AnalyticsViewModel(fixture.Rows, fixture.Manifest, fixture.File, FamilyEnvelopeQuant.AttributeHits(fixture.Hits, fixture.File));
            var control = new AnalyticsControl(model);
            var chart = ((StackPanel)control.FindName("PanelFamilyChart")).Children.OfType<StackPanel>().SelectMany(group => group.Children.OfType<Border>())
                .Select(border => border.Child).OfType<FamilyContributionChart>().SingleOrDefault();
            Check("NM1 actual Results chart uses selected family name " + choice, chart != null
                && chart.Bars.Any(bar => bar.FamilyUserId == 11 && bar.Label.Contains(name, StringComparison.Ordinal)));
        }
        Check("NM1 name choice preserves every numeric report field", snapshots[0].SequenceEqual(snapshots[1]));
        Check("NM1 name choice preserves stored quantitation", storedStudies == JsonSerializer.Serialize(fixture.Manifest) && sourceRows == JsonSerializer.Serialize(fixture.Rows));
        Check("NM1 older projects default to Name", JsonSerializer.Deserialize<FamilyClusterFile>("{}")!.ReportFamilyNames == ReportFamilyNameChoice.Name);
        var table = new PanelSpreadsheetTable { SheetName = "Families", HeaderRowNumber = 1, Headers = new() { "Key", "Name", "English" }, Rows = new()
        {
            new() { RowNumber = 2, Cells = new() { ["Key"] = "group-a", ["Name"] = "Original name", ["English"] = "Alternate name" } },
        } };
        var imported = new FamilyPanelSpreadsheetImporter().Import(table, new() { FamilySheet = table.SheetName,
            ColumnHeaders = new() { [PanelImportFields.FamilyKey] = "Key", [PanelImportFields.DisplayName] = "English" } }, Array.Empty<FamilyPanelDecisionMapping>());
        Check("NM1 the prior English-as-Family-name mapping still imports and falls back", imported.Families.Single().DisplayName == "Alternate name"
            && ReportFamilyNames.DisplayName(imported, imported.Families.Single(), ReportFamilyNameChoice.OtherLanguageName) == "Alternate name");
        var options = new[] { new ProjectExportOption(ProjectExportKind.Report, "Report", "HTML, Word and supplement workbook", true, "Export the synthetic report. Save keeps its project choices.") };
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Light, Themes.AppTheme.Dark })
            {
                Themes.ThemeManager.Apply(theme);
                var window = OffscreenProofWindow(new ExportWindow(options, ReportFamilyNameChoice.OtherLanguageName));
                try
                {
                    window.Show(); PumpDispatcher();
                    Check("NM1 Export restores the saved choice " + theme, window.FamilyNames == ReportFamilyNameChoice.OtherLanguageName);
                    var selector = (ComboBox)window.FindName("FamilyNameChoice");
                    Check("NM1 name choices explain fallback and persistence " + theme, selector.Items.Count == 2 && selector.ToolTip.ToString()!.Contains("Save keeps", StringComparison.Ordinal));
                    ScreenHarness.Save(window, Path.Combine(output, "export-names-" + theme + ".png"), 680, 720);
                }
                finally { window.Close(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
