using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilyListImportMatchTests()
    {
        var project = ManualChoiceFile();
        project.MyExtendedFastaItems[0].Description = "Invented alpha enzyme";
        project.MyExtendedFastaItems[0].Evidence!.Family = "Supplied alpha enzyme family";
        project.MyExtendedFastaItems[0].Evidence!.Reviewed = true;
        project.MyExtendedFastaItems[0].Evidence!.Keywords = new() { "Invented alpha function" };
        string before = project.ToJsonText();
        var panel = new FamilyPanel { Name = "Supplied list", Families = new() { new() { Key = "alpha", DisplayName = "Invented alpha enzyme",
            Names = new() { new() { Name = "Other-language name", Text = "Invented alpha" } } } } };
        string panelBefore = JsonSerializer.Serialize(panel);
        var adapter = new FamilyListStoredEvidenceMatches(project);
        var rows = adapter.Suggest(project, panel);
        var shared = new PanelEvidenceMatching(project).Suggest(new[] { panel.Families[0].DisplayName }.Concat(panel.Families[0].Names.Select(n => n.Text)));
        Check("wizard matching is the shared Core projection and starts unchecked", rows.Single().Suggestions.Count > 0
            && rows.Single().Suggestions.Select(s => s.Key).SequenceEqual(shared.Select(s => s.Key))
            && rows.Single().Suggestions.All(s => !s.Use) && project.ToJsonText() == before && JsonSerializer.Serialize(panel) == panelBefore);
        var selected = rows.Single().Suggestions[0]; selected.Use = true;
        selected.Text = "An explicitly edited value absent from stored evidence";
        Check("editing a suggested label refreshes exact coverage instead of retaining its old counts", selected.Proteins == 0 && selected.Reviewed == 0
            && selected.Details.Contains("No exact stored label", StringComparison.Ordinal));
        rows.Single().Choice = "Confirmed";
        var accepted = adapter.Accept(panel, rows);
        var confirmed = accepted.ConfirmedEvidenceMatches?.GetValueOrDefault("alpha");
        Check("only explicitly selected edited values are stored under the stable panel key", accepted.ConfirmedEvidenceMatches?.Count == 1
            && confirmed is { UserConfirmed: true }
            && confirmed.Choices.Single().Value == selected.Text
            && confirmed.NamesUsed.Contains("Invented alpha")
            && project.ToJsonText() == before && JsonSerializer.Serialize(panel) == panelBefore);
        rows.Single().Choice = "Skipped";
        Check("skipping a family adds no confirmed match records", adapter.Accept(panel, rows).ConfirmedEvidenceMatches == null);
        project.MyExtendedFastaItems[0].Evidence!.Keywords.Add("A later annotation");
        string after = project.ToJsonText(); bool refused = false;
        try { adapter.Accept(panel, rows); } catch (InvalidOperationException error) { refused = error.Message.Contains("evidence changed", StringComparison.Ordinal); }
        Check("the wizard adapter refuses a changed stored-evidence snapshot without edits", refused && project.ToJsonText() == after);
    }
}
