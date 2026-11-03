using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AnalysisHelpCommandTable() =>
    [
        new("--test-analysis-help", 79, TestCommandPhase.Raw,
            args => args.Contains("--test-analysis-help", StringComparer.Ordinal), (_, _) =>
            {
                AnalysisHelpTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void AnalysisHelpTests()
    {
        var type = typeof(HelpContent).Assembly.GetType("FamilyQuant.PanelReportHelp");
        var build = type?.GetMethod("Build", Type.EmptyTypes);
        Check("J13 panel and report Help exists", build != null);
        if (build == null) return;
        string text = (string)build.Invoke(null, Array.Empty<object>())!;
        bool Has(string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
        Check("J13 Help covers panel import, mapping, checking and editing", Has("header row") && Has("decision")
            && Has("Pfam") && Has("template") && Has("Undo"));
        Check("J13 Help explains evidence links, ambiguity and subclass limitations", Has("confirm") && Has("evidence")
            && Has("unresolved") && Has("absent") && Has("subclass"));
        Check("J13 Help distinguishes run means, sample SD, pooled basis and denominators", Has("mean of available per-run shares")
            && Has("sample standard deviation") && Has("pooled") && Has("measured peptide signal") && Has("TIC"));
        Check("J13 Help explains greedy protein counts without claiming genes", Has("greedy") && Has("unexplained")
            && Has("not a gene") && Has("peptide of its own"));
        Check("J13 Help covers others, sensitivity, figures and report supplement", Has("Report under others")
            && Has("sensitivity") && Has("coverage") && Has("ring") && Has("supplement") && Has("Export report"));
        Check("J13 fixed Help is general and uses ordinary hyphens",
            !Regex.IsMatch(text, @"\b(venom|toxin|snake|bothrops)\b|[\u2013\u2014]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); string first = (string)build.Invoke(null, Array.Empty<object>())!;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("J13 fixed Help repeats across cultures", first == (string)build.Invoke(null, Array.Empty<object>())!);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        Check("J13 Help is reachable from the main family Help", HelpContent.Build(null, NormalizationMode.IdentifiedSignal,
            SharedPeptidePolicy.FamilyUnique).Contains(text, StringComparison.Ordinal));
        Check("J13 report export Help includes the shared report explanation", ReportExportWindow.HelpText.Contains("supplement", StringComparison.OrdinalIgnoreCase)
            && ReportExportWindow.HelpText.Contains("Report under others", StringComparison.OrdinalIgnoreCase));
    }
}
