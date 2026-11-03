using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    // A stand-in for J2a's applied result; the production accessor still checks the full fingerprint.
    private static void MakePanelAssignmentsCurrent(FamilyClusterFile file, EvidenceClassificationOptions options)
    {
        file.PanelProteinAssignmentsPanelFingerprint = PanelDerivedDataCurrentness.Fingerprint(file.FamilyPanel!);
        foreach (var row in file.PanelProteinAssignments!) row.Reason = "Synthetic applied assignment";
        var recipe = new EvidenceRecipe { Options = options, FamilyPanel = FamilyPanelValidation.Clone(file.FamilyPanel!),
            PanelProteinAssignments = JsonSerializer.Deserialize<List<PanelProteinAssignment>>(JsonSerializer.Serialize(file.PanelProteinAssignments)),
            PanelProteinAssignmentsPanelFingerprint = file.PanelProteinAssignmentsPanelFingerprint };
        file.EvidenceRecipe = recipe;
        var fingerprint = typeof(PanelAssignmentEvidenceStore).GetMethod("Fingerprint", BindingFlags.Static | BindingFlags.NonPublic)!;
        string value = (string)fingerprint.Invoke(null, new object?[] { file, file.PanelProteinAssignments, recipe, file.EvidenceSource, null })!;
        file.PanelProteinAssignmentsFingerprint = recipe.PanelProteinAssignmentsFingerprint = value;
    }
}
