using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static readonly Regex RetiredUiTerms = new(
        @"\b(?:Layer\s+[123]|Data Browser|Controls tab|Create Super Cluster|Report XIC as|Full XIC share of|PatternLab source|Quantitation options)\b|File\s*[·>]\s*Compute\s*/\s*update",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static int TooltipSentenceCount(string text) =>
        1 + Regex.Matches(text, @"[.!?](?:[""')\]]*)\s+(?=\S)", RegexOptions.CultureInvariant).Count;

    private static IEnumerable<string> ScreenWording(FrameworkElement root)
    {
        foreach (FrameworkElement element in WalkTree(root).OfType<FrameworkElement>())
        {
            if (element.ToolTip is string tip) yield return tip;
            if (element is TextBlock text) yield return text.Text;
            if (element is ContentControl { Content: string content }) yield return content;
            if (element is HeaderedContentControl { Header: string header }) yield return header;
            if (element is DataGrid grid)
                foreach (DataGridColumn column in grid.Columns)
                {
                    if (column.Header is string heading) yield return heading;
                    if (HeaderTooltipOf(column) is string help) yield return help;
                }
        }
    }

    private static void CurationWordingTests()
    {
        EnsureApplication();
        GeneralWordingTests();
        Check("tooltip sentence guard counts lowercase sentences and preserves decimals and extensions",
            TooltipSentenceCount("First. second? third!") == 3
            && TooltipSentenceCount("Choose a .plp file at 0.95. Disabled until a file is loaded.") == 2);
        string help = Flatten(HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique));
        string deNovo = Flatten(HelpContent.BuildDeNovo());
        Check("general, de novo and grouping Help use current screen and command names",
            !RetiredUiTerms.IsMatch(help + " " + deNovo + " " + GroupingReviewHelp.Text));
        var screen = new ClusterGenerationControl(new FamilyClusterFile { MyExtendedFastaItems = Vipers() });
        LayoutAt(screen, new Size(1366, 900), -1);
        string[] commands = { "ButtonSuperCluster", "ButtonWordCluster" };
        var missing = commands.Select(name => UiField<Button>(screen, name)?.Content as string)
            .Where(text => string.IsNullOrWhiteSpace(text) || !help.Contains(text, StringComparison.Ordinal)).ToArray();
        Check("Help names the actual word and keyword grouping commands", missing.Length == 0, string.Join(", ", missing));
        string[] labels = WalkTree(screen).OfType<Label>().Select(label => label.Content as string)
            .Where(text => text == "PatternLab share of" || text == "Share of").Select(text => text!).ToArray();
        Check("Help names the current Full XIC and PatternLab denominator controls",
            labels.Length == 2 && labels.All(text => help.Contains(text, StringComparison.Ordinal)));
        Check("Help explains family selection, all-protein display, naming and lock editing",
            help.Contains("All proteins", StringComparison.Ordinal) && help.Contains("family overview", StringComparison.OrdinalIgnoreCase)
            && help.Contains("hidden", StringComparison.OrdinalIgnoreCase) && help.Contains("checkbox", StringComparison.OrdinalIgnoreCase));
        Check("lock-manager Help describes the family checkbox and batch locking paths",
            LockManagerViewModel.Explanation.Contains("checkbox", StringComparison.OrdinalIgnoreCase)
            && LockManagerViewModel.Explanation.Contains("batch", StringComparison.OrdinalIgnoreCase)
            && !LockManagerViewModel.Explanation.Contains("and by nothing else", StringComparison.Ordinal));
        var shell = new MainWindow();
        try
        {
            string workflow = (string)typeof(MainWindow).GetMethod("WorkflowText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null)!;
            Check("shell workflow Help uses current screen names", !RetiredUiTerms.IsMatch(workflow));
        }
        finally { shell.Close(); }
        var preview = new SuperClusterPreview(Vipers(), null, ViperKeywords, "", KeywordMatch.Any,
            new[] { "metalloproteinase" }, null);
        Check("grouping review notes use the current word-grouping command", !RetiredUiTerms.IsMatch(preview.Notes));
        var review = new SuperClusterPreviewWindow(preview);
        try
        {
            LayoutAt((FrameworkElement)review.Content, new Size(review.Width, review.Height), -1);
            CheckTooltipCoverage("Grouping review wording", (FrameworkElement)review.Content);
        }
        finally { review.Close(); }
        Check("Help text is deterministic for the same state",
            help == Flatten(HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique))
            && deNovo == Flatten(HelpContent.BuildDeNovo()));
    }
}
