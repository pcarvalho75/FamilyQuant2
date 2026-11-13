using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsFamilyPieCommands() =>
    [new("--test-results-family-pie", 10722, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-results-family-pie", (args, _) =>
        {
            ResultsFamilyPieTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "results-family-pie"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ResultsFamilyPieTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var fixture = FamilyPieFixture();
        var random = new Random(2468);
        const string alphabet = "ACDEFGHKMNPQRSTVWY";
        string Sequence() => string.Concat(Enumerable.Range(0, 12).Select(_ => alphabet[random.Next(alphabet.Length)]));
        var rows = new List<QuantRow>();
        int scan = 1;
        foreach (var protein in fixture.File.MyExtendedFastaItems)
        {
            string identified = Sequence(), deNovo = Sequence();
            protein.Sequence = identified + deNovo + "GGGGGGGGGKTTTTTTTTTK";
            rows.Add(Qr("identified", "pie-run", scan++, 2, identified, 2, 2));
            rows.Add(Qr("denovo", "pie-run", scan++, 2, deNovo, 2, 2));
        }
        rows.Add(Qr("identified", "pie-run", scan++, 2, "GGGGGGGGGK", 5, 5));
        rows.Add(Qr("denovo", "pie-run", scan++, 2, "TTTTTTTTTK", 5, 5));
        rows.Add(Qr("identified", "pie-run", scan++, 2, "YYYYYYYYYK", 5, 5));
        rows.Add(Qr("denovo", "pie-run", scan, 2, "WWWWWWWWWK", 5, 5));
        fixture.Manifest.FamilyRunShares!.MembershipSha256 = FamilyStudyMembership.Fingerprint(fixture.File);
        var hits = FamilyEnvelopeQuant.SearchCuration(rows, fixture.File);
        var model = new AnalyticsViewModel(rows, fixture.Manifest, fixture.File, FamilyEnvelopeQuant.AttributeHits(hits, fixture.File));
        var control = new AnalyticsControl(model);
        var selector = (ComboBox)control.FindName("ComboBoxFamilyTopN");
        var choices = selector.Items.OfType<ComboBoxItem>().ToArray();
        var pieChoice = choices.Single(choice => choice.Tag as string == "pie");
        Check("PI2 existing cutoffs and default remain available", choices.Where(choice => choice != pieChoice).Select(choice => choice.Tag as string)
            .SequenceEqual(new[] { "10", "15", "20", "30", "0" }) && !model.FamilyPie && model.FamilyTopN == 15);
        Check("PI2 Pie explains its fixed base", pieChoice.ToolTip.ToString()!.Contains("all-peptide-feature base", StringComparison.Ordinal));
        var bar = WalkTree(control).OfType<FamilyContributionChart>().Single();
        var before = bar.Bars.Select(bucket => (bucket.Kind, bucket.FamilyUserId, bucket.Identified, bucket.DeNovo)).ToArray();
        selector.SelectedItem = pieChoice;
        var pie = WalkTree(control).OfType<FamilyPieChart>().Single();
        var expected = FamilyPieData.Build(model.Families, model.FamilyTopN, model.FamilySharedPolicy, model.FamilyLabels);
        Check("PI2 Results uses the shared HTML pie projection", pie.Data.Unavailable == null && pie.Data.Slices.SequenceEqual(expected.Slices));
        Check("PI2 actual Results pie closes to 100.0", Math.Abs(pie.Data.Slices.Sum(slice => slice.Percent) - 100) < 1e-10);
        Check("PI2 actual native slices equal bar amounts", pie.Data.Slices.Count == before.Length && pie.Data.Slices.Zip(before).All(pair =>
            pair.First.Kind == pair.Second.Kind && pair.First.Percent == 100 * (pair.Second.Identified + pair.Second.DeNovo) / FamilyContributionPlot.MeasuredTotal(model.Families)));
        Check("PI2 actual source data includes all residual kinds", pie.Data.Slices.Any(slice => slice.Kind == FamilyContributionBarKind.OtherFamilies)
            && pie.Data.Slices.Any(slice => slice.Kind == FamilyContributionBarKind.Shared) && pie.Data.Slices.Any(slice => slice.Kind == FamilyContributionBarKind.Unattributed));
        Check("PI2 native exact details name the base and both source parts", pie.SliceDetails(0).Contains("all measured peptide-feature signal", StringComparison.Ordinal)
            && pie.SliceDetails(0).Contains("Identified", StringComparison.Ordinal) && pie.SliceDetails(0).Contains("de novo", StringComparison.Ordinal));
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Light, Themes.AppTheme.Dark })
            {
                Themes.ThemeManager.Apply(theme);
                var window = OffscreenProofWindow(new Window { Content = control, Width = 1366, Height = 1000 });
                try
                {
                    window.Show(); PumpDispatcher();
                    ScreenHarness.Save(window, Path.Combine(output, "results-pie-" + theme + ".png"), 1366, 1000);
                    pie.Focus();
                    pie.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(pie), 0, Key.End) { RoutedEvent = Keyboard.KeyDownEvent });
                    Check("PI2 keyboard selection exposes exact details " + theme, pie.ToolTip.ToString()!.Contains(pie.Data.Slices[^1].Label, StringComparison.Ordinal));
                }
                finally { window.Content = null; window.Close(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
        selector.SelectedItem = choices.Single(choice => choice.Tag as string == "15");
        Check("PI2 a cutoff returns to unchanged bars", !model.FamilyPie && WalkTree(control).OfType<FamilyContributionChart>().Single().Bars
            .Select(bucket => (bucket.Kind, bucket.FamilyUserId, bucket.Identified, bucket.DeNovo)).SequenceEqual(before));
        selector.SelectedItem = choices.Single(choice => choice.Tag as string == "10");
        selector.SelectedItem = pieChoice;
        Check("PI2 Pie preserves the chosen cutoff", model.FamilyTopN == 10 && WalkTree(control).OfType<FamilyPieChart>().Single().Data.Slices
            .SequenceEqual(FamilyPieData.Build(model.Families, 10, model.FamilySharedPolicy, model.FamilyLabels).Slices));
        var empty = new FamilyPieChart(FamilyPieData.Build(null));
        ChartImageExporter.Save(empty, Path.Combine(output, "empty-pie.png"), 1180, 420);
        var named = EffectivePanelFixture();
        named.File.FamilyPanel!.Import = new() { ColumnHeaders = new() { [FamilyQuant.Panels.Import.PanelImportFields.OtherName] = "Other label" } };
        named.File.FamilyPanel.Families.Single(family => family.Key == "main").Names.Add(new() { Name = "Other label", Text = "Alternate main" });
        named = PrepareEffectivePanelFixture(named.File, named.Rows);
        named.Manifest.DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method;
        var namedModel = new AnalyticsViewModel(named.Rows, named.Manifest, named.File, FamilyEnvelopeQuant.AttributeHits(named.Hits, named.File)) { FamilyPie = true };
        var namedControl = new AnalyticsControl(namedModel);
        Check("PI2 initialized Pie model restores the selector", ((ComboBoxItem)((ComboBox)namedControl.FindName("ComboBoxFamilyTopN")).SelectedItem).Tag as string == "pie");
        named.File.ReportFamilyNames = ReportFamilyNameChoice.OtherLanguageName;
        namedModel.RefreshFamilyNames();
        Check("PI2 NM1 name changes redraw a selected Pie", namedModel.FamilyPie && WalkTree(namedControl).OfType<FamilyPieChart>().Single().Data.Slices
            .Any(slice => slice.Label.Contains("Alternate main", StringComparison.Ordinal)));
    }
}
