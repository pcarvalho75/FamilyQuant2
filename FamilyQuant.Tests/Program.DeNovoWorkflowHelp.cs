using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void DeNovoWorkflowHelpTests()
    {
        EnsureApplication();
        string text = DeNovoWorkflowHelp.Build();
        Check("Z6d Help presents seven numbered steps and the requested page title", text.StartsWith("De novo in FamilyQuant", StringComparison.Ordinal)
            && Enumerable.Range(1, 7).All(i => text.Contains(i.ToString(CultureInfo.InvariantCulture) + ". ", StringComparison.Ordinal)));
        Check("Z6d Help explains the recorded default PatternLab rule and old-result boundary", new[] { "PatternLab wins", "ON, the default", "removes PTMs", "I = L", "old stored results change only after Compute", "report Methods" }.All(s => text.Contains(s, StringComparison.Ordinal)));
        Check("Z6d Help names input roles, duplicate credit, XICs, 80% homology and optional review", new[] { ".sepr2", ".plp", "PEAKS", "not credited again", "XIC", "PepExplorer-style", "80%", "decoy identity gate", "exact tie", "De novo review is optional inspection" }.All(s => text.Contains(s, StringComparison.Ordinal)));
        Check("Z6d Help explains accepted decoy PSMs and top-XCorr priority", text.Contains("FDR-accepted PSMs", StringComparison.Ordinal)
            && text.Contains("a decoy PSM can therefore win", StringComparison.Ordinal) && text.Contains("top-XCorr", StringComparison.Ordinal)
            && text.Contains("lower-ranked PSM is still excluded", StringComparison.Ordinal));
        Check("Z6d Help is reachable in general and review Help", HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique).Contains(text, StringComparison.Ordinal)
            && HelpContent.BuildDeNovo().Contains(text, StringComparison.Ordinal));
        var shell = new MainWindow();
        var item = (MenuItem)shell.FindName("MenuItemDeNovoWorkflowHelp");
        Check("Z6d the Help menu exposes the named page with useful tooltip", (string)item.Header == DeNovoWorkflowHelp.Title
            && item.ToolTip is string tooltip && tooltip.Contains("seven steps", StringComparison.Ordinal));
        shell.Close();
    }

    [TestCommandTable]
    private static IEnumerable<TestCommand> DeNovoWorkflowHelpCommands() =>
    [new("--test-denovo-workflow-help", 10100, TestCommandPhase.Raw, args => args.Contains("--test-denovo-workflow-help", StringComparer.Ordinal), (_, _) =>
    {
        DeNovoWorkflowHelpTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];
}
