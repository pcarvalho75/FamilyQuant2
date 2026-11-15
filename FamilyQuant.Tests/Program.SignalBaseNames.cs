using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SignalBaseNameCommands() =>
    [new("--test-signal-base-names", 74, TestCommandPhase.Raw, args => args.Contains("--test-signal-base-names", StringComparer.Ordinal), (args, _) =>
    {
        SignalBaseNameTests(args.Length == 2 ? args[1] : null);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];

    private static void SignalBaseNameTests(string? output = null)
    {
        EnsureApplication();
        string folder = output ?? Path.Combine(Path.GetTempPath(), "signal-base-names"); Directory.CreateDirectory(folder);
        const string measured = "Isotope envelopes (measured)", accepted = "Isotope envelopes (measured and identified)";
        bool Old(string text) => Regex.IsMatch(text, @"detector(?: isotope)? envelopes|peptide[- ]feature signal|measured envelope of the peptide features", RegexOptions.IgnoreCase);
        var run = new NativeRunFraction { Run = "synthetic-run", Tic = 1000, IdentifiedAreaM0 = 100, DeNovoAreaM0 = 40,
            IdentifiedEnvelope = 200, DeNovoEnvelope = 100, DetectedPeptideLikeArea = 800,
            DetectedAreaMultiplyCharged = 600, DetectedAreaSinglyCharged = 200 };
        string before = JsonSerializer.Serialize(run);
        var recovery = new RecoveryChart(new[] { run }, isotopeCeiling: 6);
        var signal = HtmlSignalRecoveryData.Read(recovery, "figure:recovery.png", "Signal recovery")!;
        Check("TM1 measured readings use canonical names and retain their isotope limit", recovery.EnvelopeLabel.StartsWith(accepted, StringComparison.Ordinal)
            && recovery.EnvelopeLabel.Contains("M+5", StringComparison.Ordinal) && signal.Rows[0].Parts.Single(part => part.Key == "detector").Label == measured
            && recovery.LegendLines.Any(line => line.StartsWith(measured, StringComparison.Ordinal))
            && !Old(recovery.EnvelopeLabel + signal.Legend + string.Join(" ", signal.Rows[0].Parts.Select(part => part.Label))));
        Check("TM1 recovery names leave native amounts unchanged", signal.Rows[0].Parts.Single(part => part.Key == "measured").Value == 30
            && signal.Rows[0].Parts.Single(part => part.Key == "detector").Value == 80 && before == JsonSerializer.Serialize(run));
        var pieFixture = FamilyPieFixture();
        var pie = new FamilyPieChart(FamilyPieData.Build(pieFixture.Families));
        var bars = new FamilyContributionChart(pieFixture.Families, asPercentOfTotal: true);
        var model = new AnalyticsViewModel(pieFixture.Rows, pieFixture.Manifest, pieFixture.File);
        Check("TM1 Results names PSM and de novo and the accepted-envelope reading", bars.Heading.Contains("PSM and de novo", StringComparison.Ordinal)
            && FamilyPieData.Basis.StartsWith("PSM and de novo", StringComparison.Ordinal) && pie.SliceDetails(0).Contains("of PSM and de novo", StringComparison.Ordinal)
            && model.Cards.Any(card => card.Title.Contains(accepted, StringComparison.Ordinal)) && !Old(FamilyPieData.Caption + pie.ToolTip + bars.Heading));
        Check("TM1 Help names the measured base consistently", !Old(HelpWorkflowFacts.FilesAndNavigation)
            && HelpWorkflowFacts.FilesAndNavigation.Contains(measured, StringComparison.Ordinal));
        var fixture = HtmlSharePartsFixture();
        string studyBefore = JsonSerializer.Serialize(fixture.Context.StoredStudies);
        var payload = HtmlReportPayloadBuilder.Build(fixture.Document, fixture.Sheets, context: fixture.Context) with { SignalCharts = new[] { signal } };
        Check("TM1 wording preserves stored studies and ring totals", studyBefore == JsonSerializer.Serialize(fixture.Context.StoredStudies)
            && Math.Abs(payload.Fields[payload.Composition.RingBases["tic"].Total!].Value!.Value - 7) < 1e-10);
        HtmlReportBrowserChecks(Path.Combine(folder, "browser"), new[] { ("desktop", payload, 1366), ("phone", payload, 390) }, """
            const checks={}, names={identifiedOnly:'PSM only',identifiedAndDeNovo:'PSM and de novo',detectorEnvelopes:'Isotope envelopes (measured)',tic:'TIC'};
            for(const [key,name] of Object.entries(names)){
              const choice=d.getElementById('basis-'+key);choice.click();
              checks[key+'Choice']=choice.parentElement.querySelector('span').firstChild.textContent===name;
              checks[key+'Centre']=d.getElementById('ring-name').textContent==='of '+name;
              checks[key+'Caption']=d.getElementById('composition-basis').textContent.includes('Percent of '+name)
                &&d.getElementById('composition-plain-legend').textContent.includes(name);
            }
            d.getElementById('tab-recovery').click();
            checks.readings=d.querySelector('[data-reading="measured"] h4').textContent==='Isotope envelopes (measured and identified)'
              &&d.querySelector('[data-reading="detector"] h4').textContent==='Isotope envelopes (measured)';
            checks.oldNamesGone=!/detector(?: isotope)? envelopes|peptide[- ]feature signal|measured envelope of the peptide features/i.test(d.body.innerText);
            checks.fits=d.documentElement.scrollWidth<=w.innerWidth;
            return checks;
            """);
        if (output != null)
        {
            CaptureHtmlReportScreens(Path.Combine(output, "report"), payload, "families");
            CaptureHtmlReportScreens(Path.Combine(output, "recovery"), payload, "recovery");
            var previous = Themes.ThemeManager.Current;
            try
            {
                foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
                {
                    Themes.ThemeManager.Apply(theme);
                    var window = new Window { Content = new AnalyticsControl(model), Width = 1366, Height = 1000, Left = -30000, Top = -30000,
                        ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                    try { window.Show(); PumpDispatcher(); ScreenHarness.Save(window, Path.Combine(output, "results-" + theme + ".png"), 1366, 1000); }
                    finally { window.Close(); PumpDispatcher(); }
                }
            }
            finally { Themes.ThemeManager.Apply(previous); }
        }
    }
}
