using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> WorkflowReportHelpCommands() =>
    [
        new("--test-workflow-report-help", 79, TestCommandPhase.Raw,
            args => args.Contains("--test-workflow-report-help", StringComparer.Ordinal), (_, _) =>
            {
                WorkflowReportHelpTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void WorkflowReportHelpTests()
    {
        EnsureApplication();
        var shell = new MainWindow();
        try
        {
            var menu = (Menu)shell.FindName("MainMenu");
            var paths = new Dictionary<MenuItem, string>();
            void Visit(ItemsControl parent, string prefix)
            {
                foreach (MenuItem item in parent.Items.OfType<MenuItem>())
                {
                    string path = prefix.Length == 0 ? (string)item.Header : prefix + " > " + item.Header;
                    paths.Add(item, path);
                    Visit(item, path);
                }
            }
            Visit(menu, "");
            string Workflow() => (string)typeof(MainWindow).GetMethod("WorkflowText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null)!;
            string text = Workflow();
            string flat = Regex.Replace(text, @"\s+", " ");
            string[] required = { "MenuItemFamilyPanel", "MenuItemFamilies", "MenuItemReportDetails", "MenuItemComputeXic", "MenuItemExport" };
            var requiredPaths = required.Select(name => paths[(MenuItem)shell.FindName(name)]).ToArray();
            Check("J21 workflow contains every required path from the live menu",
                requiredPaths.All(path => flat.Contains(path, StringComparison.Ordinal)), string.Join("; ", requiredPaths.Where(path => !flat.Contains(path, StringComparison.Ordinal))));
            string[] MenuLines(string source) => Regex.Matches(source, @"(?m)^\s*Menu:\s*([^\r\n]+)")
                .Select(m => m.Groups[1].Value.Trim()).ToArray();
            var menuLines = MenuLines(text);
            var knownPaths = paths.Values.ToHashSet(StringComparer.Ordinal);
            Check("J21 every Menu line matches a complete live menu path", menuLines.Length > 0 && menuLines.All(knownPaths.Contains),
                string.Join("; ", menuLines.Where(path => !knownPaths.Contains(path))));
            string typo = text.Replace("Menu: File > Export", "Menu: File > Exprot", StringComparison.Ordinal);
            Check("J21 menu check rejects a misspelled command even when its prefix is a menu", typo != text
                && MenuLines(typo).Any(path => !knownPaths.Contains(path)));
            var mentions = Regex.Matches(flat, @"\b(?:File|Edit|View|Tools|Analysis|Help) > ").Select(m => flat[m.Index..]).ToArray();
            bool HasLivePrefix(string mention) => knownPaths.Any(path => path.Contains(" > ", StringComparison.Ordinal)
                && mention.StartsWith(path, StringComparison.Ordinal)
                && (mention.Length == path.Length || !char.IsLetterOrDigit(mention[path.Length])));
            Check("J21 every inline top-level menu path begins with a live menu path", mentions.Length > 0 && mentions.All(HasLivePrefix),
                string.Join("; ", mentions.Where(x => !HasLivePrefix(x)).Select(x => x[..Math.Min(x.Length, 70)])));
            int[] order = requiredPaths.Select(path => flat.IndexOf(path, StringComparison.Ordinal)).ToArray();
            Check("J21 panel, family review, report details, compute and export are in order", order.All(x => x >= 0)
                && order.Zip(order.Skip(1), (a, b) => a < b).All(x => x));
            string[] details = { "Choose spreadsheet", "Preview columns", "Quantify", "Report under others", "Not a family", "Merge into family",
                "Preview panel", "Check Pfam", "Apply panel", "Group by evidence", "Update proposal", "Panel links", "Use link choices", "Apply grouping", "subclass", "title", "authors",
                "sample", "workflow", "database", "interpretation", "section", "threshold", "Word", "supplement" };
            Check("J21 workflow covers the panel-to-report choices and outputs", details.All(value => flat.Contains(value, StringComparison.OrdinalIgnoreCase)),
                string.Join("; ", details.Where(value => !flat.Contains(value, StringComparison.OrdinalIgnoreCase))));
            string[][] releaseDetails =
            {
                new[] { "Export report", "progress", "Operation log", "elapsed" },
                new[] { "opening", "attribution", "available" },
                new[] { "cached", "reopen", "export" },
                new[] { "no peptide observed", "not proof" },
                new[] { "scope phrases", "keywords", "tissue", "audit", "outside" },
                new[] { "accuracy check", "multiple scope phrases", "reviewed", "not independent" },
                new[] { "link", "note", "report" },
                new[] { "Compute", "current item", "since the last step", "known total" },
                new[] { "supplement", "one row per item" },
                new[] { "written by the program", "without AI" },
            };
            foreach (string[] group in releaseDetails)
                Check("J40 workflow explains " + string.Join(", ", group),
                    group.All(value => flat.Contains(value, StringComparison.OrdinalIgnoreCase)),
                    string.Join("; ", group.Where(value => !flat.Contains(value, StringComparison.OrdinalIgnoreCase))));
            Check("J21 workflow uses general wording and ordinary hyphens", !Regex.IsMatch(text,
                @"\b(?:venom|toxin|snake|bothrops)\b|[\u2013\u2014]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); string br = Workflow();
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Check("J21 workflow text is identical across cultures", br == Workflow());
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
        finally { shell.Close(); }
    }
}
