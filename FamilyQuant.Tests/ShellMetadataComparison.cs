using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static class ShellMetadataComparison
{
    internal sealed record Result(string Id, bool Matched, string Before, string Actual, string Reason);

    internal static Result Compare(FeatureLedger.Observation row,
        IReadOnlyList<ShellMetadataSnapshot.Fact> frozen, IReadOnlyList<ShellMetadataSnapshot.Fact> current)
    {
        Result No(string reason, string before = "", string actual = "") => new(row.Id, false, before, actual, reason);
        if (row.Kind is not ("control" or "option/copy" or "column" or "handler" or "handler definition" or "code fact")) return No("Kind requires a different proof.");
        var before = frozen.Where(fact => fact.Id == row.Id).ToArray();
        var after = current.Where(fact => fact.Id == row.Id).ToArray();
        if (before.Length != 1 || after.Length != 1) return No("Missing or duplicate native observation.");
        var old = before[0]; var actual = after[0];
        bool SameObservation(ShellMetadataSnapshot.Fact fact) => fact.Source == row.Source && fact.Line == row.Line
            && fact.Kind == row.Kind && fact.Detail == row.Detail;
        if (!SameObservation(old) || !SameObservation(actual)) return No("Native observation identity differs from frozen inventory.");
        if (old.Reason.Length > 0 || actual.Reason.Length > 0) return No(old.Reason + "; " + actual.Reason);
        if (row.Kind is "handler" or "handler definition" && (old.Property != "$interaction" || actual.Property != "$interaction"))
            return No("An event requires an executed native transition, not metadata.");
        if (row.Kind == "code fact")
        {
            try
            {
                if (JsonSerializer.Deserialize<string>(row.Detail) != old.Value) return No("Frozen native value differs from its recorded literal.");
            }
            catch (JsonException) { return No("This native code-fact check supports complete plain string literals only."); }
        }
        if (old.Anchor.Length == 0 || old.Property.Length == 0 || old.Anchor != actual.Anchor || old.Property != actual.Property)
            return No("Native property anchors differ.");
        bool numericReplacement = row.Source is "FamilyQuant/ComputeXicWindow.xaml" or "FamilyQuant/ClusterGenerationControl.xaml"
            && old.Type is "Xceed.Wpf.Toolkit.IntegerUpDown" or "Xceed.Wpf.Toolkit.DecimalUpDown"
            && actual.Type == old.Type.Replace("Xceed.Wpf.Toolkit.", "FamilyQuant.", StringComparison.Ordinal)
            && old.Traits.Length > 0 && old.Traits == actual.Traits;
        if (old.Type != actual.Type && !numericReplacement)
            return No("Runtime control type changed without equal supported numeric constraints/defaults.", old.Type, actual.Type);
        if (old.Traits != actual.Traits) return No("Native numeric constraints or default value changed.", old.Traits, actual.Traits);
        if (old.Value != actual.Value) return No("Effective native values differ.", old.Value, actual.Value);
        string type = numericReplacement ? actual.Type : old.Type;
        return new(row.Id, true, type + "." + old.Anchor + "." + old.Property + "=" + old.Value + ";" + old.Traits,
            type + "." + actual.Anchor + "." + actual.Property + "=" + actual.Value + ";" + actual.Traits,
            row.Kind is "handler" or "handler definition"
                ? "Equal native click outcomes, with exactly the corresponding isolated Home event emitted once; no shell-route or scientific-operation claim."
                : "Equal compiled native metadata from the same frozen observation and owning view; no handler or result claim.");
    }
}
