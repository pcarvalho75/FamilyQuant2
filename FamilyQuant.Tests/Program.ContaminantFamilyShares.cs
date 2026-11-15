using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ContaminantFamilyShareCommands() =>
    [new("--test-contaminant-family-shares", 10730, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-contaminant-family-shares", (args, _) =>
        {
            ContaminantFamilyShareTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "contaminant-family-shares"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ContaminantFamilyShareTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var file = new FamilyClusterFile();
        void Protein(int id, string identifier, string sequence, string name) => file.MyExtendedFastaItems.Add(new()
            { UserID = id, SequenceIdentifier = identifier, Sequence = sequence, UserAnnotation = name });
        Protein(1, "contaminant_CT_A1", "AAAAAAAK", "Stored group A");
        Protein(1, "contaminant_CT_A2", "AAAAAAAK", "Stored group A");
        Protein(2, "contaminant_CT_B", "CCCCCCCK", "Stored group B");
        Protein(2, SequenceLabels.DecoyPrefix + "CT_B", "VVVVVVVK", "Stored group B");
        Protein(3, "contaminant_CT_MIX", "DDDDDDDK", "Mixed group");
        Protein(3, "CT_MIX_CLEAN", "DDDDDDDK", "Mixed group");
        Protein(4, "CT_CLEAN_A", "EEEEEEEKGGGGGGGK", "Stored group D");
        Protein(5, "CT_CLEAN_B", "FFFFFFFKGGGGGGGK", "Stored group E");
        Protein(6, SequenceLabels.DecoyPrefix + "CT_ONLY", "MMMMMMMK", "Decoy-only group");
        Protein(0, "contaminant_CT_ZERO", "SSSSSSSK", "Family zero");
        file.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "contaminant_CT_UNASSIGNED", Sequence = "QQQQQQQK" });
        var only = FamilyContaminants.OnlyIds(file);
        Check("CT1 classification uses all targets, ignores decoys and preserves real family zero", only.SetEquals(new[] { 0, 1, 2 }));
        Check("CT1 effective mixed memberships stay families", FamilyContaminants.OnlyIds(file, new Dictionary<int, IReadOnlyList<int>>
            { [100] = new[] { 1, 2 }, [101] = new[] { 1, 3 }, [102] = Array.Empty<int>(), [103] = new[] { 2, 6 }, [104] = new[] { 6 } })
                .SetEquals(new[] { 100, 103 }));
        var rows = new List<QuantRow>();
        foreach (var run in new[] { (Name: "ct-a", Areas: new[] { 10d, 15, 20, 40, 10, 3, 2 }),
            (Name: "ct-b", Areas: new[] { 10d, 70, 40, 50, 20, 6, 4 }) })
        {
            string[] sequences = { "AAAAAAAK", "CCCCCCCK", "DDDDDDDK", "EEEEEEEK", "FFFFFFFK", "GGGGGGGK", "WWWWWWWK" };
            for (int index = 0; index < sequences.Length; index++)
                rows.Add(Qr("identified", run.Name, index + 1, 2, sequences[index], run.Areas[index], run.Areas[index]));
        }
        var hits = FamilyEnvelopeQuant.SearchCuration(rows, file);
        var attribution = FamilyEnvelopeQuant.AttributeHits(hits, file);
        var assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), attribution);
        var manifest = new QuantManifest { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method,
            PerRunTic = new() { ["ct-a"] = 200, ["ct-b"] = 400 }, DerivedFractions = new()
            { new() { Run = "ct-a", DetectedPeptideLikeArea = 120 }, new() { Run = "ct-b", DetectedPeptideLikeArea = 240 } } };
        FamilyCompositionStudy.StoreRunShares(manifest, assigned);
        FamilyCompositionStudy.StoreProteinCounts(manifest, assigned, file.MyExtendedFastaItems);
        var pooled = FamilyEnvelopeQuant.BuildFromAttributedPeptides(assigned);
        var labels = FamilyNaming.Names(file.MyExtendedFastaItems);
        foreach (var family in pooled.Families) family.Description = labels.GetValueOrDefault(family.FamilyUserId, "");
        string source = JsonSerializer.Serialize(manifest), quant = JsonSerializer.Serialize(rows);
        var model = new AnalyticsViewModel(rows, manifest, file, attribution);
        var control = new AnalyticsControl(model);
        var bars = WalkTree(control).OfType<FamilyContributionChart>().Single().Bars;
        Check("CT1 contaminant-only families leave Results family bars", bars.All(bar => bar.FamilyUserId is not (1 or 2)));
        var contaminant = bars.SingleOrDefault(bar => bar.Label == "contaminants");
        double expectedSignal = pooled.Families.Where(family => family.FamilyUserId is 1 or 2).Sum(family => family.UniqueTotalEnvelopeReferenceCalibrated);
        Check("CT1 one Results contaminants row sums its source families", contaminant != null && contaminant.Total == expectedSignal && contaminant.FamilyCount == 2);
        Check("CT1 contaminants tooltip lists its contributing families", contaminant?.Description.Contains("Stored group A", StringComparison.Ordinal) == true
            && contaminant.Description.Contains("Stored group B", StringComparison.Ordinal));
        var top = FamilyContributionPlot.Fold(pooled, 3, contaminantFamilyIds: only);
        Check("CT1 a lower family enters Top N when contaminants leave", top.Where(bar => bar.FamilyUserId != null).Select(bar => bar.FamilyUserId).ToHashSet()
            .SetEquals(new int?[] { 3, 4, 5 }) && top.Single(bar => bar.Label == "contaminants").Total == expectedSignal);
        Check("CT1 compact drawing retains the contaminants bucket", WalkTree(control).OfType<FamilyContributionChart>().Single().BarsAtHeight(110)
            .Single(bar => bar.Label == "contaminants").Total == expectedSignal);
        Check("CT1 mixed and other Results family values stay unchanged", pooled.Families.Where(family => family.FamilyUserId is 3 or 4 or 5)
            .All(family => bars.Single(bar => bar.FamilyUserId == family.FamilyUserId).Total == family.UniqueTotalEnvelopeReferenceCalibrated));
        ((ComboBox)control.FindName("ComboBoxFamilyTopN")).SelectedItem = ((ComboBox)control.FindName("ComboBoxFamilyTopN")).Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == "pie");
        var pie = WalkTree(control).OfType<FamilyPieChart>().Single().Data;
        Check("CT1 Results Pie uses one contaminants slice and closes to 100", pie.Unavailable == null
            && pie.Slices.Count(slice => slice.Label == "contaminants") == 1 && Math.Abs(pie.Slices.Sum(slice => slice.Percent) - 100) < 1e-10);
        var export = ReportProjectExporter.Build(rows, manifest, file, Path.Combine(output, "figures"), familyResults: pooled);
        string word = Path.Combine(output, "report.docx"); ReportProjectExporter.Write(export, word);
        var payload = ReadHtmlPayload(HtmlReportWriter.PathForReport(word));
        Check("CT1 HTML ranks no contaminant-only family", payload.Composition.Families.All(family => family.Id is not ("1" or "2")));
        var row = payload.Composition.ContributionRows.SingleOrDefault(row => payload.Fields[row.Name].Text == "contaminants");
        Check("CT1 HTML contaminants mean sums per-run shares", row?.IdentifiedAndDeNovo?.Total is string value
            && Math.Abs(payload.Fields[value].Value!.Value - 32.5) < 1e-10);
        var grouped = payload.Composition.Families.Single(family => family.IsContaminants);
        Check("CT1 contaminants sample SD is calculated after summing each run", grouped.IdentifiedAndDeNovo.Sd is string sd
            && Math.Abs(payload.Fields[sd].Value!.Value - 15 / Math.Sqrt(2)) < 1e-10);
        Check("CT1 HTML retains member information outside the family rank", grouped.ContaminantMembers!.Contains("Stored group A", StringComparison.Ordinal)
            && grouped.ContaminantMembers.Contains("Stored group B", StringComparison.Ordinal));
        Check("CT1 ring groups contaminants in Outside without changing clean shares", Math.Abs(payload.Fields[payload.Composition.RingBases["identifiedAndDeNovo"].Total!].Value!.Value - 62.5) < 1e-10
            && Math.Abs(payload.Fields[payload.Composition.RingBases["identifiedAndDeNovo"].Outside!].Value!.Value - 37.5) < 1e-10);
        var pieRows = payload.FamilyFigures.Pie!.Figure!.Views["fixed"].Rows;
        var pieRow = pieRows.SingleOrDefault(row => payload.Fields[row.Name].Text == "contaminants");
        Check("CT1 HTML pooled contaminants equals the Results share", pieRow?.X is { } amount
            && Math.Abs(payload.Fields[amount.Exact].Value!.Value - 100 * expectedSignal / pooled.MeasuredTotalEnvelopeReferenceCalibrated) < 1e-10);
        var table = ReportCsvTable.Build("ct-headline", "Headline", export.CsvExports["family_composition_headline.csv"], "");
        int familyColumn = table.Columns.ToList().IndexOf("family"), idColumn = table.Columns.ToList().IndexOf("family_user_id");
        int measuredColumn = table.Columns.ToList().IndexOf("mean_measured_pct");
        var tableRow = table.Rows.SingleOrDefault(row => row[familyColumn] == "contaminants");
        Check("CT1 Word/workbook headline groups contaminant-only families once", table.Rows.All(row => row[idColumn] is not ("1" or "2"))
            && tableRow != null && tableRow[measuredColumn] == "32.5");
        Check("CT1 every other headline family keeps its mean", manifest.FamilyRunShares!.Families.Where(family => family.FamilyUserId is 3 or 4 or 5).All(family =>
            table.Rows.Single(row => row[idColumn] == family.FamilyUserId.ToString(CultureInfo.InvariantCulture))[measuredColumn]
                == (100 * family.MeanShareOfMeasuredSignal!.Value).ToString("G6", CultureInfo.InvariantCulture)));
        foreach (string csv in new[] { "family_run_means.csv", "family_run_shares.csv", "family_pooled_minus_mean.csv", "family_composition_measured.csv", "family_composition_credited.csv" })
        {
            var projected = ReportCsvTable.Build("ct-" + csv, csv, export.CsvExports[csv], "");
            int ids = projected.Columns.ToList().IndexOf("family_user_id");
            int names = projected.Columns.ToList().FindIndex(column => column is "family" or "description");
            Check("CT1 family share table contains only the grouped contaminant signal " + csv, projected.Rows.All(item => item[ids] is not ("1" or "2"))
                && projected.Rows.Count(item => item[names] == "contaminants") == (csv == "family_run_shares.csv" ? 2 : 1));
        }
        var listed = FamilyReportThreshold.Build(manifest.FamilyRunShares!, file).ListedFamilies
            .Select(family => family.FamilyUserId.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        var countTable = ReportCsvTable.Build("ct-counts", "Counts", export.CsvExports["family_protein_counts.csv"], "");
        Check("CT3 protein-count CSV contains exactly the ranked families including the mixed family",
            listed.SetEquals(countTable.Rows.Select(item => item[0])) && listed.Contains("3"));
        var originalCounts = ReportCsvTable.Build("ct-original-counts", "Counts", ReportStudyExports.ProteinCounts(manifest.FamilyProteinCounts!, labels), "");
        Check("CT3 every kept protein-count CSV row is unchanged", countTable.Rows.Where(item => listed.Contains(item[0]))
            .All(item => item.SequenceEqual(originalCounts.Rows.Single(original => original[0] == item[0]))));
        var wordCounts = export.Document.Sections.SelectMany(section => section.Blocks).OfType<ReportTable>().Single(item => item.Id == "protein-counts");
        Check("CT3 Word diversity table contains exactly ranked family names", wordCounts.Rows.Select(item => item[0]).ToHashSet(StringComparer.Ordinal)
            .SetEquals(listed.Select(id => labels[int.Parse(id, CultureInfo.InvariantCulture)])));
        Check("CT3 workbook diversity table contains exactly ranked families", export.SupplementSheets.Single(sheet => sheet.Id == "protein-counts")
            .Table.Rows.Select(item => item[0]).ToHashSet(StringComparer.Ordinal).SetEquals(listed.Select(id => labels[int.Parse(id, CultureInfo.InvariantCulture)])));
        var diversity = payload.FamilyFigures.Figures.Single(figure => figure.Kind == "diversity");
        Check("CT3 every HTML diversity base contains exactly ranked families", diversity.Views.Values.All(view => listed.SetEquals(view.Rows.Select(item => item.Key))));
        var reportDiversity = ReportStudyFigures.Plan(manifest, file).Figures.Select(figure => figure.Plot.Chart).OfType<FamilyAbundanceDiversityChart>().Single();
        Check("CT3 Word figure diversity contains exactly ranked families", listed.SetEquals(reportDiversity.Data.Families.Select(item => item.Key))
            && listed.SetEquals(reportDiversity.Points.Select(item => item.Key)));
        var resultsDiversity = model.BuildCompositionFigures(strict: true).Select(figure => figure.Chart).OfType<FamilyAbundanceDiversityChart>().Single();
        Check("CT3 Results diversity contains exactly ranked families", listed.SetEquals(resultsDiversity.Data.Families.Select(item => item.Key))
            && listed.SetEquals(resultsDiversity.Points.Select(item => item.Key)));
        Check("CT3 kept diversity means and protein counts are unchanged", resultsDiversity.Points.Concat(reportDiversity.Points).All(point =>
            point.SharePercent == manifest.FamilyRunShares.Families.Single(family => family.FamilyUserId.ToString(CultureInfo.InvariantCulture) == point.Key).MeanShare * 100
            && point.ProteinCount == manifest.FamilyProteinCounts!.Families.Single(family => family.FamilyUserId.ToString(CultureInfo.InvariantCulture) == point.Key).GreedyProteinCount));
        ContaminantDiversityProjectionCases(file, manifest, labels);
        Check("CT1 grouping does not mutate stored quantitation", source == JsonSerializer.Serialize(manifest) && quant == JsonSerializer.Serialize(rows));
        Check("CT1 HTML values trace to the emitted Word and workbook", ReportNumberTrace.RunHtmlBindings(payload, word,
            ReportSupplementWorkbook.PathForReport(word), file, manifest, Path.Combine(output, "trace.csv")) == 0);
        HtmlReportBrowserChecks(Path.Combine(output, "browser"), new[] { ("phone", payload, 390), ("desktop", payload, 1366) }, """
            const checks={},p=JSON.parse(d.getElementById('fq-data').textContent);
            const rows=[...d.querySelectorAll('#family-ranking .rank-row')];
            checks.noContaminantFamily=rows.every(row=>!['1','2'].includes(row.dataset.family));
            const group=rows.find(row=>row.dataset.family==='contaminants');
            checks.unranked=group===rows.at(-1)&&group.querySelector('.rank-name').title.includes('Stored group A');
            checks.ring=d.querySelectorAll('#composition-ring [data-family="1"],#composition-ring [data-family="2"],#composition-ring [data-family="contaminants"]').length===0;
            checks.ringDetails=d.querySelector('.ring-contaminants-key')?.title.includes('Stored group B');
            d.getElementById('contribution-pie').click();
            checks.pie=[...d.querySelectorAll('#contribution-pie-view .u3c-ring-key button')].some(button=>button.textContent.includes('contaminants')&&button.title.includes('Stored group A'));
            checks.fits=d.documentElement.scrollWidth<=w.innerWidth;
            return checks;
            """);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Light, Themes.AppTheme.Dark })
                foreach (bool pieView in new[] { false, true })
                {
                    Themes.ThemeManager.Apply(theme);
                    var choice = (ComboBox)control.FindName("ComboBoxFamilyTopN");
                    choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == (pieView ? "pie" : "15"));
                    var window = OffscreenProofWindow(new Window { Content = control, Width = 1366, Height = 1000 });
                    try { window.Show(); PumpDispatcher(); ScreenHarness.Save(window, Path.Combine(output, "ct-" + (pieView ? "pie-" : "bars-") + theme + ".png"), 1366, 1000); }
                    finally { window.Content = null; window.Close(); }
                }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
        CaptureHtmlReportScreens(Path.Combine(output, "screens"), payload, "families");
    }

    private static void ContaminantDiversityProjectionCases(FamilyClusterFile file, QuantManifest manifest, IReadOnlyDictionary<int, string> labels)
    {
        file.ReportThreshold = .3;
        try
        {
            var grouping = FamilyReportThreshold.Build(manifest.FamilyRunShares!, file);
            var othersIds = grouping.OtherFamilies.Select(family => family.FamilyUserId).ToHashSet();
            var counts = manifest.FamilyProteinCounts!.Families.Where(family => othersIds.Contains(family.FamilyUserId)).ToArray();
            var diversity = ReportStudyFigures.Plan(manifest, file).Figures.Select(figure => figure.Plot.Chart).OfType<FamilyAbundanceDiversityChart>().Single();
            var others = diversity.Points.SingleOrDefault(point => point.Key == "others");
            Check("CT3 canonical Others retains its existing combined share and union greedy count", othersIds.SetEquals(new[] { 3, 5 })
                && others?.ProteinCount == counts.SelectMany(family => family.SelectedProteinIdentifiers).Distinct(StringComparer.Ordinal).Count()
                && others.SharePercent == grouping.OthersAssignedStatistics.Mean * 100
                && diversity.Points.Select(point => point.Key).ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "4", "others" }));
            var csv = ReportCsvTable.Build("ct-threshold-counts", "Counts", ReportStudyExports.ProteinCounts(manifest.FamilyProteinCounts, labels, grouping), "");
            Check("CT3 native count CSV retains its old non-contaminant Others-member rows", csv.Rows.Select(row => row[0]).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "3", "4", "5" }));
        }
        finally { file.ReportThreshold = null; }

        var panel = EffectivePanelFixture();
        var contaminant = panel.File.MyExtendedFastaItems.Single(protein => protein.UserID == 33);
        contaminant.SequenceIdentifier = "contaminant_" + contaminant.SequenceIdentifier;
        panel.File.PanelProteinAssignments!.Single(assignment => assignment.FamilyUserId == 33).Identifier = contaminant.SequenceIdentifier;
        panel = PrepareEffectivePanelFixture(panel.File, panel.Rows);
        EffectivePanelFamilyStudy.Store(panel.Manifest, panel.Assigned, panel.Hits, panel.File);
        var basis = ReportCompositionBasis.Select(panel.File, panel.Manifest);
        string stored = JsonSerializer.Serialize(panel.Manifest);
        var effective = ReportStudyFigures.PlanForBasis(panel.Manifest, panel.File, basis).Figures.Select(figure => figure.Plot.Chart)
            .OfType<FamilyAbundanceDiversityChart>().Single();
        var expected = basis.Grouping!.ListedFamilies.Select(family => family.FamilyUserId.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        Check("CT3 effective panel diversity omits the contaminant-only collapsed family", !basis.IsIdentity
            && basis.Grouping.Contaminants.Families.Count == 1 && expected.SetEquals(effective.Data.Families.Select(row => row.Key)));
        Check("CT3 effective panel diversity keeps every stored count and mean", effective.Points.All(point =>
            point.ProteinCount == basis.Studies.FamilyProteinCounts!.Families.Single(count => count.FamilyUserId.ToString(CultureInfo.InvariantCulture) == point.Key).GreedyProteinCount
            && point.SharePercent == basis.Studies.FamilyRunShares!.Families.Single(family => family.FamilyUserId.ToString(CultureInfo.InvariantCulture) == point.Key).MeanShare * 100)
            && stored == JsonSerializer.Serialize(panel.Manifest));
    }
}
