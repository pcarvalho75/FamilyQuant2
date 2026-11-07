using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant.CurationFormat2;

/// <summary>Wire references only for byte-identical current/recipe values. Canonical content remains unchanged.</summary>
[CurationFormatPart("recipe-copies", 30)]
internal sealed class RecipeCopiesPart : CurationFormatPart
{
    private const string Assignments = nameof(EvidenceRecipe.PanelProteinAssignments);
    private const string Subclasses = nameof(EvidenceRecipe.ProteinSubclassLabels);
    private const string Search = nameof(EvidenceRecipe.SessionSearch);
    private const string Panel = nameof(EvidenceRecipe.FamilyPanel);

    public override void PrepareWrite(FamilyClusterFile file, CurationFormatContext context)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        if (file.EvidenceRecipe is { } recipe)
        {
            if (SameBytes(file.PanelProteinAssignments, recipe.PanelProteinAssignments)) references.Add(Assignments);
            if (SameBytes(file.ProteinSubclassLabels, recipe.ProteinSubclassLabels)) references.Add(Subclasses);
            if (SameBytes(file.EvidenceSource?.NearestReviewed, recipe.SessionSearch)) references.Add(Search);
            if (SameBytes(file.FamilyPanel, recipe.FamilyPanel)) references.Add(Panel);
        }
        context.State[Name] = references;
        context.SetSection(this, references.Order(StringComparer.Ordinal).ToArray());
    }

    public override void ModifyType(JsonTypeInfo typeInfo, CurationFormatContext context)
    {
        if (!context.Writing || typeInfo.Type != typeof(EvidenceRecipe)
            || !context.State.TryGetValue(Name, out object? state) || state is not HashSet<string> references) return;
        foreach (var property in typeInfo.Properties)
            if (references.Contains(property.Name)) property.ShouldSerialize = (_, _) => false;
    }

    public override void CompleteRead(FamilyClusterFile file, CurationFormatContext context)
    {
        if (!context.TryGetSection<string[]>(this, out var references) || references == null)
            throw new InvalidDataException("The recipe-copy reference table is missing.");
        if (references.Distinct(StringComparer.Ordinal).Count() != references.Length)
            throw new InvalidDataException("A recipe-copy reference is listed twice.");
        if (references.Length == 0) return;
        var recipe = file.EvidenceRecipe ?? throw new InvalidDataException("Recipe-copy references have no stored recipe.");
        foreach (string reference in references)
        {
            switch (reference)
            {
                case Assignments:
                    RequireReference(recipe.PanelProteinAssignments, file.PanelProteinAssignments, reference);
                    recipe.PanelProteinAssignments = Detached(file.PanelProteinAssignments!);
                    break;
                case Subclasses:
                    RequireReference(recipe.ProteinSubclassLabels, file.ProteinSubclassLabels, reference);
                    recipe.ProteinSubclassLabels = Detached(file.ProteinSubclassLabels!);
                    break;
                case Search:
                    RequireReference(recipe.SessionSearch, file.EvidenceSource?.NearestReviewed, reference);
                    recipe.SessionSearch = Detached(file.EvidenceSource!.NearestReviewed!);
                    break;
                case Panel:
                    RequireReference(recipe.FamilyPanel, file.FamilyPanel, reference);
                    recipe.FamilyPanel = Detached(file.FamilyPanel!);
                    break;
                default:
                    throw new InvalidDataException("Unknown recipe-copy reference: " + reference + ".");
            }
        }
    }

    private static void RequireReference(object? stored, object? source, string reference)
    {
        if (stored != null) throw new InvalidDataException("A recipe field is both stored and referenced: " + reference + ".");
        if (source == null) throw new InvalidDataException("A recipe-copy reference has no source: " + reference + ".");
    }

    private static T Detached<T>(T value) where T : class => JsonStreamCopy.Clone(value);

    private static bool SameBytes<T>(T? first, T? second) where T : class
    {
        return first != null && second != null && JsonStreamCopy.SameSerializedBytes(first, second);
    }
}
