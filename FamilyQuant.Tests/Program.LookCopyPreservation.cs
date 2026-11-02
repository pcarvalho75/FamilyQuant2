using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void LookCopyComparisonTests()
    {
        const string before = "namespace Fixture { public static class Help { public static string Build() { return \"A fact about the denominator.\"; } } }";
        var row = new FeatureLedger.Observation("fixture", "FamilyQuant/Help.cs", 1, "help fact", "\"A fact about the denominator.\"");
        var binding = LedgerMemberFactComparison.Compare(row, before,
            new Dictionary<string, string> { ["current.cs"] = before.Replace("denominator", "changed", StringComparison.Ordinal) });
        var shown = new LedgerPublishedCopyComparison.Publication(binding.FrozenMember, "current.cs", "Help.Build -> report.Text",
            "General Help", "Heading\nA fact about the denominator.\nMore text.");
        LedgerPublishedCopyComparison.Result Compare(string text, string? owner = null,
            FeatureLedger.Observation? observation = null, LedgerMemberFactComparison.Result? anchor = null)
        {
            var selected = observation ?? row;
            string selectedSource = before.Replace(row.Detail, selected.Detail, StringComparison.Ordinal);
            var selectedAnchor = anchor ?? (observation == null ? binding : LedgerMemberFactComparison.Compare(selected, selectedSource,
                new Dictionary<string, string> { ["current.cs"] = selectedSource.Replace(selected.Detail, "\"Changed copy\"", StringComparison.Ordinal) }));
            return LedgerPublishedCopyComparison.Compare(selected, selectedAnchor,
                new[] { shown with { Text = text, FrozenMember = owner ?? selectedAnchor.FrozenMember } });
        }
        Check("published Help proof retains an unchanged frozen fact after the containing member changed", Compare(shown.Text).Matched);
        Check("published Help proof records the actual text control and configuration", Compare(shown.Text) is { CurrentPath: "current.cs", Context: "General Help", Actual: "A fact about the denominator." });
        Check("published Help proof permits wrapping whitespace without changing the words", Compare("A fact about\n  the denominator.").Matched);
        Check("published Help proof refuses missing text", !Compare("A different fact.").Matched);
        Check("published Help proof refuses another member lending the same text", !Compare(shown.Text, "Fixture.Other.Build").Matched);
        Check("published Help proof requires a unique frozen literal/member anchor", !Compare(shown.Text, anchor: binding with { FrozenMember = "" }).Matched);
        Check("published Help proof does not silently fold scientific letter case", !Compare("A fact about the Denominator.").Matched);
        Check("published Help proof does not turn a numeric substring into a retained value",
            !Compare("Value 180 residues.", observation: row with { Detail = "\"80 residues\"" }).Matched);
        Check("published Help proof accepts a complete numeric phrase",
            Compare("Value 80 residues.", observation: row with { Detail = "\"80 residues\"" }).Matched);
        Check("published Help proof compares decorative lines as whole lines",
            !Compare("--------", observation: row with { Detail = "\"----\"" }).Matched
            && Compare("Heading\n----\nBody", observation: row with { Detail = "\"----\"" }).Matched);
        Check("published Help proof refuses unsupported interpolated literals",
            !Compare("A fact", observation: row with { Detail = "$\"A {value}\"" }).Matched);
        Check("published Help proof cannot substitute for a handler execution",
            !Compare(shown.Text, observation: row with { Kind = "handler" }).Matched);
        Check("published Help proof cannot borrow a binding for another observation",
            !Compare(shown.Text, anchor: binding with { Id = "other" }).Matched);
        Check("published Help proof cannot borrow a binding for a different frozen literal",
            !Compare(shown.Text, observation: row with { Detail = "\"denominator\"" }, anchor: binding).Matched);
        Check("published Help proof refuses a number embedded after a decimal point",
            !Compare("Value 1.80 residues.", observation: row with { Detail = "\"80 residues\"" }).Matched);
    }
}
