using FamilyQuant.Panels;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> InterpretationInputCommands() =>
    [new("--test-interpretation-input", 10467, TestCommandPhase.Raw,
        args => args.Length == 1 && args[0] == "--test-interpretation-input", (_, _) =>
        {
            InterpretationInputTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    // The J44 fixture plus report details, panel text with a column separator and a line break, and a Methods section.
    private static ReportInterpretationInputData InterpretationInputFixture()
    {
        var findings = FindingsFixture();
        findings.Panel!.Name = "Fixture panel";
        findings.Panel.Families[0].SourceDecision = "Core";
        findings.Panel.Families[0].TextFields.Add(new() { Name = "Reference", Text = "first | second\nthird" });
        return new ReportInterpretationInputData
        {
            Details = new() { Title = "Fixture title", Authors = "A. Author", SampleDescription = "Three runs", Workflow = "Digest", DatabaseKind = "Public" },
            Methods = new ReportSection(ReportSectionIds.Methods, "Methods", new ReportBlock[]
            {
                new ReportTable("methods-rules", "Search and quantitation rules", new[] { "Setting", "Stored value" },
                    new IReadOnlyList<string>[] { new[] { "Extraction ppm", "10" } }, "Settings recorded with the computation."),
                new ReportParagraph("methods-unavailable", "An unavailable measurement is not a zero."),
            }),
            Findings = findings, ApplicationVersion = "9.8.7.6",
        };
    }

    private static void InterpretationInputTests()
    {
        Section("Interpretation input - one deterministic Markdown file of the stored tables for the user's own assistant");
        var data = InterpretationInputFixture();
        string text = ReportInterpretationInput.Build(data);
        string[] lines = text.Split('\n');
        Check("J45 the same data give the same text, with Unix line ends and one final line end", text == ReportInterpretationInput.Build(InterpretationInputFixture())
            && !text.Contains('\r') && text.EndsWith("\n", StringComparison.Ordinal) && !text.EndsWith("\n\n", StringComparison.Ordinal));
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            string portuguese = ReportInterpretationInput.Build(InterpretationInputFixture());
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("J45 the text is identical under pt-BR and en-US", portuguese == ReportInterpretationInput.Build(InterpretationInputFixture()));
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
        Check("J45 the file opens with its title, the writing version and the no-interpretation statement", lines[0] == "# Interpretation input"
            && lines[2].StartsWith("Written by FamilyQuant 9.8.7.6 from the project's stored results.", StringComparison.Ordinal)
            && lines[2].EndsWith("It contains no interpretation.", StringComparison.Ordinal), lines[2]);
        Check("J45 the instruction block states that numbers are quoted from the tables", ReportInterpretationInput.Instructions.All(i => lines.Contains("- " + i))
            && ReportInterpretationInput.Instructions[0].StartsWith("Quote every number exactly as printed", StringComparison.Ordinal));
        Check("J45 report details are printed as entered; an empty interpretation says none was entered", lines.Contains("| Title | Fixture title |")
            && lines.Contains("| Sample description | Three runs |") && lines.Contains("| Written interpretation | None entered |"));
        Check("J45 the panel table carries key, name, decision as entered, action and database members", lines.Contains("Panel name: Fixture panel")
            && lines.Contains("| a | Alpha group | Core | Quantify |  |  | 10 |") && lines.Contains("| e | Epsilon group |  | ReportUnderOthers |  |  | 1 |"),
            string.Join(" / ", lines.Where(l => l.StartsWith("| a |", StringComparison.Ordinal))));
        Check("J45 panel text keeps its line break and escapes the column separator", lines.Contains("| a | Text | Reference | first \\| second<br>third |"));
        Check("J45 subclass rules are listed under the family they belong to", lines.Contains("| a | x | Part X |  |  |  |  |") && lines.Contains("| a | y | Part Y |  |  |  |  |"));
        var findings = ReportFindings.Build(data.Findings).Section.Blocks.OfType<ReportParagraph>().Select(p => p.Text).ToList();
        Check("J45 every summary-of-findings sentence is printed verbatim", findings.Count > 5 && findings.All(lines.Contains));
        var headline = ReportCsvTable.Build("h", "h", ReportStudyExports.Headline(data.Findings.Grouping, data.Findings.Counts, data.Findings.Labels), "",
            "family_user_id", "family", "status", "available_runs", "mean_pct", "sample_sd_pct", "cv_pct", "mean_measured_pct", "greedy_protein_estimate",
            "unique_peptide_proteins", "protein_explanation").Rows;
        Check("J45 the headline table holds every row of the program's headline export, cell for cell", headline.Count == 3
            && headline.All(r => lines.Contains("| " + string.Join(" | ", r) + " |")), string.Join(" / ", lines.Where(l => l.StartsWith("| 1 |", StringComparison.Ordinal))));
        var others = ReportCsvTable.Build("o", "o", ReportStudyExports.Others(data.Findings.Grouping, data.Findings.Labels), "",
            "family_user_id", "family", "reason", "mean_pct", "sample_sd_pct", "cv_pct").Rows;
        Check("J45 the Others table names each member with its reason, as the supplement does", others.Count == 5
            && others.All(r => lines.Contains("| " + string.Join(" | ", r) + " |")) && others.Any(r => r[2] == "Threshold, PanelDecision"));
        Check("J45 quantified panel families without a peptide are listed with their members", lines.Contains("| d | Delta group | 7 |"));
        int parts = Array.IndexOf(lines, "## Subclass parts, all runs pooled");
        var partRows = lines.Skip(parts + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal)).Where(l => l.StartsWith("| ", StringComparison.Ordinal)).ToList();
        Check("J45 subclass parts appear only for the family with subclass rules, with the not-resolved rule", parts > 0 && lines[parts + 2] == ReportFindings.NotResolvedMethod
            && partRows.Count == 4 && partRows.Skip(1).All(r => r.StartsWith("| 1 | Alpha group |", StringComparison.Ordinal)) && lines.Contains("| 1 | Alpha group | Part X | No | 40 |"),
            string.Join(" / ", partRows));
        Check("J45 run totals and the analysis choices are printed", lines.Contains("| run_a | 80 | 100 |  |") && lines.Contains("- Report threshold: 0.1% of the credited family signal")
            && lines.Contains("- Evidence scope phrases: \"term one\", \"term two\"") && lines.Contains("- Shared peptides: " + data.Findings.Shares.Policy));
        int methods = Array.IndexOf(lines, "## Methods");
        Check("J45 the report's Methods section follows, each table under its title with its caption", methods > 0 && lines[methods + 2] == "### Search and quantitation rules"
            && lines[methods + 4] == "Settings recorded with the computation." && lines.Contains("| Extraction ppm | 10 |") && lines[^2] == "An unavailable measurement is not a zero.");
        Check("J45 fixed wording names no organism, venom or toxin and has no en or em dash (D21)", !Regex.IsMatch(text,
            @"venom|toxin|snake|viper|bothrops|lectin|metallo|[–—]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        string bare = ReportInterpretationInput.Build(new ReportInterpretationInputData
        {
            Findings = data.Findings with { Panel = null, PanelKeyByUserId = new Dictionary<int, string?>(), Subclasses = null, ScopeTerms = Array.Empty<string>() },
        });
        Check("J45 without a panel or methods those parts are absent, not invented", !bare.Contains("## Family panel", StringComparison.Ordinal)
            && !bare.Contains("## Subclass parts", StringComparison.Ordinal) && !bare.Contains("## Methods", StringComparison.Ordinal)
            && !bare.Contains("## Quantified panel families", StringComparison.Ordinal) && bare.Contains("- Evidence scope phrases: none\n", StringComparison.Ordinal)
            && bare.Split('\n')[2].StartsWith("Written by FamilyQuant from", StringComparison.Ordinal));
        string refusal;
        try { ReportInterpretationInput.FromProject(new FamilyClusterFile(), new QuantManifest(), "1"); refusal = "no refusal"; }
        catch (InvalidOperationException ex) { refusal = ex.Message; }
        Check("J45 a project without stored family shares is refused with the step to take", refusal.StartsWith("The project has no stored family shares.", StringComparison.Ordinal), refusal);

        EnsureApplication();
        var shell = new MainWindow();
        string? message = null;
        void Seen(ShellMessage shown) => message = shown.Text;
        ShellMessages.Reported += Seen;
        try
        {
            var item = ExportOptions(shell).Single(option => option.Kind == ProjectExportKind.InterpretationInput);
            Check("J45 File > Export holds Interpretation input under Markdown", item.Section == "Interpretation input (Markdown)"
                && item.Label == "Interpretation input" && !item.Available && item.Hint.Contains("Results", StringComparison.Ordinal));
            var task = shell.ExportInterpretationInputAsync();
            Check("J45 without stored Results the command explains what to do and writes nothing", task.IsCompleted && !task.Result
                && message?.StartsWith("The interpretation input needs stored Results.", StringComparison.Ordinal) == true, message);
        }
        finally { ShellMessages.Reported -= Seen; shell.Close(); }
    }
}
