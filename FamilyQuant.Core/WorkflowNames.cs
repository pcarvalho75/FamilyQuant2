using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FamilyQuant;

/// <summary>One numbered step of the workflow, as Home and the sidebar show it.</summary>
/// <param name="Number">The step number shown beside the name.</param>
/// <param name="Name">The one name of the step's screen.</param>
/// <param name="Method">The short line under the name.</param>
/// <param name="Optional">True for a step the other steps do not need.</param>
/// <param name="Parts">The named parts inside the step's screen, in order (a, b).</param>
public sealed record WorkflowStep(int Number, string Name, string Method, bool Optional, IReadOnlyList<string> Parts)
{
    /// <summary>The number and the name, for example "3 Quantify".</summary>
    public string NumberedName => Number + " " + Name;
}

/// <summary>A name the program used before the names table, and the name that replaces it.</summary>
/// <param name="OldName">The retired wording, as users saw it.</param>
/// <param name="NewName">The names-table entry that replaces it.</param>
/// <param name="Pattern">Finds the retired wording in user-visible text.</param>
/// <param name="Example">A sentence the pattern must catch, when the old name alone is not one.</param>
public sealed record RetiredWorkflowName(string OldName, string NewName, Regex Pattern, string? Example = null)
{
    /// <summary>The text a check plants to prove the pattern catches this old name.</summary>
    public string Sample => Example ?? OldName;
}

/// <summary>
/// The names table (roadmap Z4a): one name for each screen and step, in the order Home shows them.
/// Every screen title, sidebar entry, menu item, message and Help page names a step with these
/// constants, and the suite fails when a retired name returns to user-visible text. The table names
/// screens only; it calculates nothing and changes no number. docs/WORKFLOW-NAMES.md is its reader copy.
/// </summary>
public static class WorkflowNames
{
    public const string Home = "Home";
    public const string BuildFamilies = "Build families";
    public const string BuildFamiliesMethod = "Group by sequence identity, USearch";
    public const string CurateFamilies = "Curate families";
    public const string GroupByEvidence = "Group by evidence";
    public const string GroupByWords = "Group by words";
    public const string Quantify = "Quantify";
    public const string Results = "Results";
    public const string DeNovoReview = "De novo review";
    public const string Optional = "Optional";

    /// <summary>The menu that holds the extra tools; the five steps live on Home and the sidebar.</summary>
    public const string Tools = "Tools";

    /// <summary>The standard Windows menu order.</summary>
    public static IReadOnlyList<string> MenuOrder { get; } = new[] { "File", "Edit", "View", Tools, "Help" };

    /// <summary>The extra tools, in menu order. None of them is a workflow step.</summary>
    public static IReadOnlyList<string> ToolCommands { get; } = new[] { "Spectrum comparison", "Clustering diagnostic", "Reannotate SEPro" };

    /// <summary>Ana's order: four steps in sequence, then the optional review.</summary>
    public static IReadOnlyList<WorkflowStep> Steps { get; } = new[]
    {
        new WorkflowStep(1, BuildFamilies, BuildFamiliesMethod, false, System.Array.Empty<string>()),
        new WorkflowStep(2, CurateFamilies, "a " + GroupByEvidence + ", b " + GroupByWords, false, new[] { GroupByEvidence, GroupByWords }),
        new WorkflowStep(3, Quantify, "Full XIC from the RAW files", false, System.Array.Empty<string>()),
        new WorkflowStep(4, Results, "Family shares, figures and report", false, System.Array.Empty<string>()),
        new WorkflowStep(5, DeNovoReview, "Inspect de novo calls beside PatternLab", true, System.Array.Empty<string>())
    };

    /// <summary>The flow in one line, as Help draws it: the four steps in order, then the optional review.</summary>
    public static string Flow => Home + " > " + Steps[0].NumberedName + " > " + Steps[1].NumberedName + " ("
        + "a " + GroupByEvidence + ", b " + GroupByWords + ") > " + Steps[2].NumberedName + " > " + Steps[3].NumberedName
        + "; " + Steps[4].NumberedName + " (optional)";

    private const RegexOptions AnyCase = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// The wording the names table retires. A match in user-visible text is a second name for a step.
    /// "de novo evidence" in lower case stays ordinary prose; only the capitalized screen name is retired.
    /// The bare step name De novo is caught where a sentence uses it as a place (open, from, in, and De
    /// novo at the end of a phrase); "De novo" as an adjective, as in De novo sequence, stays.
    /// </summary>
    public static IReadOnlyList<RetiredWorkflowName> Retired { get; } = new[]
    {
        new RetiredWorkflowName("Start page", Home, new Regex(@"\bstart page\b", AnyCase)),
        new RetiredWorkflowName("New from database", BuildFamilies, new Regex(@"\bnew from database\b", AnyCase)),
        new RetiredWorkflowName("Build from database", BuildFamilies, new Regex(@"\bbuild from database\b", AnyCase)),
        new RetiredWorkflowName("Compute full XIC", Quantify, new Regex(@"\bcompute full XIC\b", AnyCase)),
        new RetiredWorkflowName("Analytics / report", Results, new Regex(@"\banalytics\s*/\s*report\b", AnyCase)),
        new RetiredWorkflowName("De novo evidence", DeNovoReview,
            new Regex(@"\b(?:De [Nn]ovo [Ee]vidence|DE NOVO EVIDENCE)\b", RegexOptions.CultureInvariant)),
        new RetiredWorkflowName("De novo", DeNovoReview, new Regex(@"\b(?i:from|open|in|and|to|the|by)\s+De [Nn]ovo\b(?=\s*(?:[.,;:)]|and\b|screen\b|$))",
            RegexOptions.CultureInvariant), "Open De novo and press Search."),
        new RetiredWorkflowName("View > Families", "View > " + CurateFamilies, new Regex(@"\bView\s*>\s*Families\b", RegexOptions.CultureInvariant)),
        new RetiredWorkflowName("Analysis >", Tools + " >", new Regex(@"\bAnalysis\s*>", RegexOptions.CultureInvariant))
    };
}
