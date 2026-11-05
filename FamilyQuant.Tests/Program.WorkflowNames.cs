using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    /// <summary>One piece of user-visible text: a XAML attribute or text node, or a C# string literal
    /// with any literal-only concatenation joined.</summary>
    internal sealed record UiTextSite(string File, int Line, string Text);

    /// <summary>
    /// Old-name sites that a later W4 step renames, per file and retired name. Each step removes its own
    /// entries in the commit that renames them; the list is empty when W4 is complete. A planted old name
    /// raises a count above its entry, and a renamed site that keeps its entry fails as stale.
    /// </summary>
    private static readonly Dictionary<string, int> PendingRetiredNameSites = new(StringComparer.Ordinal)
    {
        ["FamilyQuant.Core/HelpContent.cs|Compute full XIC"] = 1,
        ["FamilyQuant.Core/HelpContent.cs|New from database"] = 1,
        ["FamilyQuant.Core/HelpWorkflowFacts.cs|New from database"] = 1,
        ["FamilyQuant/ClusterGenerationControl.Presentation.cs|Analysis >"] = 3,
        ["FamilyQuant/ClusterGenerationControl.Presentation.cs|Compute full XIC"] = 4,
        ["FamilyQuant/ClusterGenerationControl.xaml|Build from database"] = 1,
        ["FamilyQuant/ClusterGenerationControl.xaml|Compute full XIC"] = 1,
        ["FamilyQuant/MainWindow.Documents.cs|New from database"] = 4,
        ["FamilyQuant/MainWindow.Help.cs|New from database"] = 2,
        ["FamilyQuant/MainWindow.Help.cs|View > Families"] = 3,
        ["FamilyQuant/MainWindow.xaml|Compute full XIC"] = 1,
        ["FamilyQuant/MainWindow.xaml|New from database"] = 1,
    };

    private static void WorkflowNameTests()
    {
        Section("Names table - one name per step, retired names stay out of UI text");
        var steps = WorkflowNames.Steps;
        Check("names table lists Ana's five steps in order with the optional review last",
            steps.Select(step => step.NumberedName).SequenceEqual(new[]
                { "1 Build families", "2 Curate families", "3 Quantify", "4 Results", "5 De novo review" })
            && steps.Select(step => step.Optional).SequenceEqual(new[] { false, false, false, false, true }));
        Check("Build families groups by sequence identity with USearch",
            steps[0].Method == "Group by sequence identity, USearch");
        Check("Curate families has part a Group by evidence and part b Group by words",
            steps[1].Parts.SequenceEqual(new[] { "Group by evidence", "Group by words" })
            && steps[1].Method == "a Group by evidence, b Group by words" && steps.Where(step => step.Number != 2).All(step => step.Parts.Count == 0));
        Check("names table draws the flow as one line in Ana's order", WorkflowNames.Flow
            == "Home > 1 Build families > 2 Curate families (a Group by evidence, b Group by words) > 3 Quantify > 4 Results; 5 De novo review (optional)");
        Check("menus follow File, Edit, View, Tools, Help and Tools keeps only the extra tools",
            WorkflowNames.MenuOrder.SequenceEqual(new[] { "File", "Edit", "View", "Tools", "Help" })
            && WorkflowNames.ToolCommands.SequenceEqual(new[] { "Spectrum comparison", "Clustering diagnostic", "Reannotate SEPro" })
            && !WorkflowNames.ToolCommands.Intersect(steps.Select(step => step.Name)).Any());
        Check("every retired name maps to a names-table entry and never matches its own replacement",
            WorkflowNames.Retired.Select(retired => retired.OldName).SequenceEqual(new[]
                { "Start page", "New from database", "Build from database", "Compute full XIC", "Analytics / report",
                  "De novo evidence", "De novo", "View > Families", "Analysis >" })
            && WorkflowNames.Retired.All(retired => retired.Pattern.IsMatch(retired.Sample) && !retired.Pattern.IsMatch(retired.NewName)
                && !retired.Pattern.IsMatch(retired.Sample.Replace(retired.OldName, retired.NewName, StringComparison.Ordinal))));
        Check("each user-visible step and screen name is a distinct names-table entry", new[]
            { WorkflowNames.Home, WorkflowNames.BuildFamilies, WorkflowNames.CurateFamilies, WorkflowNames.GroupByEvidence,
              WorkflowNames.GroupByWords, WorkflowNames.Quantify, WorkflowNames.Results, WorkflowNames.DeNovoReview, WorkflowNames.Tools }
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 9);

        const string plantedXaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <!-- New from database stays a comment -->
              <Menu>
                <MenuItem Header="Compute full XIC" ToolTip="Returns to the start page."/>
                <MenuItem Header="Analysis"><MenuItem Header="De novo evidence"/></MenuItem>
              </Menu>
              <TextBlock>Analytics / report</TextBlock>
            </Window>
            """;
        const string plantedSource = """"
            // New from database stays a comment, "Compute full XIC" too
            /* Analytics / report */
            class Fixture { char quote = '"'; char escaped = '\''; string a = "Use File > New " +
                // a comment between the parts
                "from database."; string b = @"Open View > Families ""now"""; string c = $"{count} De Novo evidence {(ok ? "x" : "y")}";
                string d = "THE DE NOVO EVIDENCE SCREEN"; string e = "Analysis > Reannotate SEPro"; string f = $@"Build from database {"{"}";
                string g = "Start page\n"; string h = "Recompute full XIC; export de novo evidence; " + count + " Quantify";
                string i = "Filters shared by Quantify and De novo."; string j = "De novo sequence; De novo only: 3; Open De novo review.";
                string k = "Recompute, or reconcile it in De Novo Step 2."; string l = "Or reconcile it in De novo review, step 2."; }
            """";
        var xamlSites = XamlUiText("Fixture.xaml", plantedXaml).ToArray();
        var sourceSites = CSharpUiText("Fixture.cs", plantedSource).ToArray();
        string[] Names(IEnumerable<UiTextSite> sites) => WorkflowNames.Retired
            .Where(retired => sites.Any(site => retired.Pattern.IsMatch(site.Text))).Select(retired => retired.OldName).ToArray();
        Check("the old-name check goes red on names planted in XAML attributes and text, never in a comment",
            Names(xamlSites).SequenceEqual(new[] { "Start page", "Compute full XIC", "Analytics / report", "De novo evidence" }),
            string.Join(", ", Names(xamlSites)));
        Check("the old-name check goes red on names planted in C# literals, a split literal included, never in a comment",
            Names(sourceSites).SequenceEqual(new[] { "Start page", "New from database", "Build from database", "De novo evidence",
                "De novo", "View > Families", "Analysis >" }), string.Join(", ", Names(sourceSites)));
        var bareStepName = WorkflowNames.Retired.Single(retired => retired.OldName == "De novo").Pattern;
        Check("the bare step name De novo is caught as a place, never as an adjective", sourceSites
            .Where(site => bareStepName.IsMatch(site.Text)).Select(site => site.Text).SequenceEqual(new[]
                { "Filters shared by Quantify and De novo.", "Recompute, or reconcile it in De Novo Step 2." }));
        Check("split literals join with their line and escapes decode",
            sourceSites.Any(site => site.Text == "Use File > New from database." && site.Line == 3)
            && sourceSites.Any(site => site.Text == "Open View > Families \"now\"") && sourceSites.Any(site => site.Text == "Start page\n")
            && !sourceSites.Any(site => site.Text.Contains("stays a comment", StringComparison.Ordinal)));
        Check("the old-name check accepts the new names and ordinary prose", !WorkflowNames.Retired.Any(retired => retired.Pattern.IsMatch(
            "Home; Build families; Curate families; Quantify; Results; De novo review; open De novo review; Tools > Spectrum comparison; "
            + "Recompute full XIC; export de novo evidence; Families for FamilyQuant 1.5 (.fcf); Analysis")));

        string root = GoldenNumbers.FindRepository();
        var counts = RetiredNameCounts(root);
        var problems = RetiredNameProblems(counts, PendingRetiredNameSites);
        Check("no retired workflow name in UI text beyond the sites W4 still renames", problems.Unexpected.Count == 0,
            string.Join("; ", problems.Unexpected));
        Check("every pending old-name site still exists; a renamed site leaves the pending list", problems.Stale.Count == 0,
            string.Join("; ", problems.Stale));
        var planted = new Dictionary<string, int>(counts, StringComparer.Ordinal);
        string key = "FamilyQuant/MainWindow.xaml|Compute full XIC";
        planted[key] = planted.GetValueOrDefault(key) + XamlUiText("FamilyQuant/MainWindow.xaml",
            "<MenuItem xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Header=\"Compute full XIC\"/>")
            .Count(site => WorkflowNames.Retired[3].Pattern.IsMatch(site.Text));
        Check("an old name planted in the real menu fails the source check", RetiredNameProblems(planted, PendingRetiredNameSites)
            .Unexpected.Any(problem => problem.StartsWith(key, StringComparison.Ordinal)));
        WorkflowNameHelpChecks();
    }

    /// <summary>Help follows the five steps with the names table, and draws the flow as Home lists it (Z4h).</summary>
    private static void WorkflowNameHelpChecks()
    {
        EnsureApplication();
        bool InOrder(string text, IEnumerable<string> parts)
        {
            int at = -1;
            foreach (string part in parts)
            {
                int next = text.IndexOf(part, at + 1, StringComparison.Ordinal);
                if (next < 0) return false;
                at = next;
            }
            return true;
        }
        string general = HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
        Check("general Help draws the flow as Home lists it and numbers the five steps with the names table",
            general.Contains(WorkflowNames.Flow, StringComparison.Ordinal)
            && InOrder(general, WorkflowNames.Steps.Select(step => step.Number + ". " + step.Name))
            && general.Contains("Part a, " + WorkflowNames.GroupByEvidence, StringComparison.OrdinalIgnoreCase)
            && general.Contains("part b, " + WorkflowNames.GroupByWords, StringComparison.OrdinalIgnoreCase));
        Check("de novo Help names its screen De novo review",
            HelpContent.BuildDeNovo().StartsWith("THE DE NOVO REVIEW SCREEN", StringComparison.Ordinal));
        var shell = new MainWindow();
        try
        {
            string workflow = (string)typeof(MainWindow).GetMethod("WorkflowText",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, null)!;
            Check("workflow Help draws the same flow and titles its steps with the names table", workflow.Contains(WorkflowNames.Flow, StringComparison.Ordinal)
                && InOrder(workflow, new[] { "STEP 1 · Build families", "STEP 2 · Curate families", "STEP 3 · Quantify", "STEP 4 · Results",
                    "OPTIONAL · 5 De novo review" }));
            Check("each workflow step names its Home and sidebar route with the step's number and shortcut", WorkflowNames.Steps
                .All(step => workflow.Contains("Also: " + step.NumberedName + " on Home or the sidebar (Alt+" + step.Number + ").", StringComparison.Ordinal)));
            Check("Home's context Help draws the same flow", shell.StatusModel.ContextHelp.StartsWith(WorkflowNames.Home + ". ", StringComparison.Ordinal)
                && shell.StatusModel.ContextHelp.Contains(WorkflowNames.Flow, StringComparison.Ordinal), shell.StatusModel.ContextHelp);
        }
        finally { shell.Close(); }
    }

    /// <summary>Counts retired-name matches in the app and Core user-visible text, keyed "file|old name".</summary>
    private static Dictionary<string, int> RetiredNameCounts(string root)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string project in new[] { "FamilyQuant", "FamilyQuant.Core" })
        foreach (string path in UiSourceFiles(Path.Combine(root, project)))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            // The names table holds the wording it retires; that wording is not user-visible text.
            if (relative == "FamilyQuant.Core/WorkflowNames.cs") continue;
            string text = File.ReadAllText(path);
            var sites = path.EndsWith(".xaml", StringComparison.Ordinal) ? XamlUiText(relative, text) : CSharpUiText(relative, text);
            foreach (var site in sites)
            foreach (var retired in WorkflowNames.Retired)
            {
                int found = retired.Pattern.Matches(site.Text).Count;
                if (found == 0) continue;
                string key = relative + "|" + retired.OldName;
                counts[key] = counts.GetValueOrDefault(key) + found;
            }
        }
        return counts;
    }

    private static (List<string> Unexpected, List<string> Stale) RetiredNameProblems(
        IReadOnlyDictionary<string, int> counts, IReadOnlyDictionary<string, int> pending)
    {
        var unexpected = counts.Where(pair => pair.Value > pending.GetValueOrDefault(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + " = " + pair.Value.ToString(CultureInfo.InvariantCulture)).ToList();
        var stale = pending.Where(pair => counts.GetValueOrDefault(pair.Key) < pair.Value)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + " = " + counts.GetValueOrDefault(pair.Key).ToString(CultureInfo.InvariantCulture)
                + " (pending " + pair.Value.ToString(CultureInfo.InvariantCulture) + ")").ToList();
        return (unexpected, stale);
    }

    /// <summary>Every XAML attribute value and text node; comments are not user-visible.</summary>
    internal static IEnumerable<UiTextSite> XamlUiText(string file, string xaml)
    {
        var document = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
                yield return new(file, ((IXmlLineInfo)attribute).LineNumber, attribute.Value);
            foreach (var text in element.Nodes().OfType<XText>())
                yield return new(file, ((IXmlLineInfo)text).LineNumber, text.Value);
        }
    }

    /// <summary>
    /// Every C# string literal outside comments, decoded. Literals joined only by "+" (with whitespace
    /// or comments between) form one site, so a name split across two source lines is still found.
    /// Interpolation holes read as "{}".
    /// </summary>
    internal static IEnumerable<UiTextSite> CSharpUiText(string file, string source)
    {
        var sites = new List<UiTextSite>();
        int index = 0, counted = 0, line = 1;
        while (index < source.Length)
        {
            char c = source[index];
            if (c == '/' && index + 1 < source.Length && source[index + 1] is '/' or '*') { index = SkipComment(source, index); continue; }
            if (c == '\'') { index = SkipCharLiteral(source, index); continue; }
            int start = index;
            if (!TryReadStringLiteral(source, ref index, out string text))
            {
                // Identifiers such as name@ or $ never start a literal; move past the character.
                index++;
                continue;
            }
            var joined = new StringBuilder(text);
            while (true)
            {
                int next = SkipTrivia(source, index);
                if (next >= source.Length || source[next] != '+' || next + 1 < source.Length && source[next + 1] is '+' or '=') break;
                int following = SkipTrivia(source, next + 1);
                if (!TryReadStringLiteral(source, ref following, out string part)) break;
                joined.Append(part);
                index = following;
            }
            for (; counted < start; counted++) if (source[counted] == '\n') line++;
            sites.Add(new UiTextSite(file, line, joined.ToString()));
        }
        return sites;
    }

    private static int SkipComment(string source, int index)
    {
        if (source[index + 1] == '/')
        {
            int end = source.IndexOf('\n', index);
            return end < 0 ? source.Length : end;
        }
        int close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
        return close < 0 ? source.Length : close + 2;
    }

    private static int SkipTrivia(string source, int index)
    {
        while (index < source.Length)
        {
            if (char.IsWhiteSpace(source[index])) index++;
            else if (source[index] == '/' && index + 1 < source.Length && source[index + 1] is '/' or '*') index = SkipComment(source, index);
            else break;
        }
        return index;
    }

    private static int SkipCharLiteral(string source, int index)
    {
        int end = index + (index + 1 < source.Length && source[index + 1] == '\\' ? 3 : 2);
        while (end < source.Length && source[end] != '\'') end++;
        return Math.Min(end + 1, source.Length);
    }

    /// <summary>Reads a regular, verbatim, interpolated or raw string literal that starts at index.</summary>
    private static bool TryReadStringLiteral(string source, ref int index, out string text)
    {
        text = "";
        int position = index, dollars = 0;
        bool verbatim = false;
        while (position < source.Length && source[position] is '$' or '@')
        {
            if (source[position] == '$') dollars++; else verbatim = true;
            position++;
        }
        if (position >= source.Length || source[position] != '"') return false;
        if (index > 0 && position > index && (char.IsLetterOrDigit(source[index - 1]) || source[index - 1] == '_')) return false;
        var builder = new StringBuilder();
        int quotes = 0;
        while (position + quotes < source.Length && source[position + quotes] == '"') quotes++;
        if (quotes >= 3)
        {
            int close = source.IndexOf(new string('"', quotes), position + quotes, StringComparison.Ordinal);
            if (close < 0) return false;
            text = source[(position + quotes)..close].Trim();
            index = close + quotes;
            return true;
        }
        position++;
        while (position < source.Length)
        {
            char c = source[position];
            if (c == '"')
            {
                if (verbatim && position + 1 < source.Length && source[position + 1] == '"') { builder.Append('"'); position += 2; continue; }
                text = builder.ToString();
                index = position + 1;
                return true;
            }
            if (dollars > 0 && c == '{')
            {
                if (position + 1 < source.Length && source[position + 1] == '{') { builder.Append('{'); position += 2; continue; }
                position = SkipInterpolationHole(source, position);
                builder.Append("{}");
                continue;
            }
            if (dollars > 0 && c == '}' && position + 1 < source.Length && source[position + 1] == '}') { builder.Append('}'); position += 2; continue; }
            if (!verbatim && c == '\\' && position + 1 < source.Length)
            {
                position = DecodeEscape(source, position, builder);
                continue;
            }
            if (!verbatim && c == '\n') return false;
            builder.Append(c);
            position++;
        }
        return false;
    }

    private static int SkipInterpolationHole(string source, int open)
    {
        int depth = 0, position = open;
        while (position < source.Length)
        {
            char c = source[position];
            if (c == '\'') { position = SkipCharLiteral(source, position); continue; }
            if (c is '"' or '@' or '$')
            {
                int nested = position;
                if (TryReadStringLiteral(source, ref nested, out _)) { position = nested; continue; }
            }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return position + 1;
            position++;
        }
        return position;
    }

    private static int DecodeEscape(string source, int slash, StringBuilder builder)
    {
        char kind = source[slash + 1];
        switch (kind)
        {
            case 'n': builder.Append('\n'); return slash + 2;
            case 'r': builder.Append('\r'); return slash + 2;
            case 't': builder.Append('\t'); return slash + 2;
            case '0': builder.Append('\0'); return slash + 2;
            case 'u' when slash + 6 <= source.Length:
                builder.Append((char)int.Parse(source.AsSpan(slash + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                return slash + 6;
            case 'U' when slash + 10 <= source.Length:
                builder.Append(char.ConvertFromUtf32(int.Parse(source.AsSpan(slash + 2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                return slash + 10;
            default: builder.Append(kind); return slash + 2;
        }
    }
}
