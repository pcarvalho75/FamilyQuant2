using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void DeNovoChangedCopyFacts(string output, JsonElement snapshots)
    {
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => row.Source == "FamilyQuant/DeNovoControl.xaml.cs" && row.Kind == "code fact"
            && (row.Line is >= 536 and <= 541 or >= 757 and <= 767 or >= 1731 and <= 1742)).ToArray();
        JsonElement Snapshot(string side, string command) => snapshots.EnumerateArray().Single(item => item.GetProperty("side").GetString() == side
            && item.GetProperty("command").GetString() == command);
        var source = Snapshot("current", "SourceChanged"); var empty = Snapshot("current-empty", "ButtonLoadAndFilter");
        var imported = Snapshot("current", "ButtonLoadAndFilter"); var homology = Snapshot("current", "ButtonSearchHomology");
        string Text(JsonElement state) => state.GetProperty("status").GetString()!;
        string Visible(JsonElement state, string key) => state.GetProperty("visible").GetProperty(key).GetString() ?? "";
        int Import(JsonElement state, string key) => state.GetProperty("import").GetProperty(key).GetInt32();
        string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
        bool Has(JsonElement state, params string[] facts) => facts.All(fact => Text(state).Contains(fact, StringComparison.OrdinalIgnoreCase));
        var records = new List<object>();
        foreach (var row in cohort)
        {
            string fact; bool present; string applicability = "applies"; string reason = "";
            bool numeric = row.Detail == "\"N0\"";
            if (row.Line <= 541)
            {
                (fact, present) = row.Line switch
                {
                    536 => ("The source change resets the visible kept-row count to zero", source.GetProperty("filtered").GetInt32() == 0 && Visible(source, "LabelRowsKept").StartsWith("Rows kept: 0", StringComparison.Ordinal)),
                    537 => ("The per-run area says a new import is required", Visible(source, "TextBlockPerRunBreakdown").Contains("No import yet", StringComparison.Ordinal)),
                    539 when row.Detail == "\" (\"" => ("The active program's native confidence scale reaches the user", Visible(source, "TextBlockNativeConfidence").Contains(DeNovo.DeNovoSource.DeepNovo.ConfidenceColumn, StringComparison.Ordinal)),
                    539 => ("The newly selected de novo program is identified", Has(source, DeNovo.DeNovoSource.DeepNovo.DisplayName, "source changed")),
                    540 => ("The previous source's rows and downstream results are cleared and the status says so", Has(source, "rows", "downstream", "cleared")
                        && !source.GetProperty("hasReconciliation").GetBoolean() && !source.GetProperty("hasHomology").GetBoolean() && source.GetProperty("filtered").GetInt32() == 0),
                    _ => ("Load and filter is the available next action", Has(source, "Load and filter") && source.GetProperty("canImport").GetBoolean()),
                };
            }
            else if (row.Line <= 767)
            {
                int read = Import(imported, "RowsRead"), kept = Import(imported, "RowsKept"), files = Import(imported, "FilesFound");
                int runs = imported.GetProperty("import").GetProperty("PerRun").GetArrayLength();
                (fact, present) = row.Line switch
                {
                    757 => ("An empty import reports its read count", Has(empty, "Showing an import of " + Number(Import(empty, "RowsRead")))),
                    758 => ("An empty import reports its file count", Has(empty, "from " + Number(Import(empty, "FilesFound")) + " file")),
                    759 when numeric == false && row.Detail.Contains("Layer", StringComparison.Ordinal) => ("Reconciliation is blocked when no row survives", !empty.GetProperty("canReconcile").GetBoolean() && Has(empty, "Step 2 stays blocked")),
                    759 => ("The user is told no row cleared the stated criteria", Has(empty, "NONE cleared", "length", "% or more")),
                    760 => ("The status explains there is nothing to reconcile and asks for a looser criterion", Has(empty, "nothing to reconcile", "loosen a criterion")),
                    761 => ("The next command and weaker-sequence warning remain visible", Has(empty, "Load and filter", "weaker de novo sequence is a weaker claim")),
                    762 when !numeric => ("Successful import identifies the next workflow step", Text(imported).StartsWith("Step 2", StringComparison.Ordinal) && imported.GetProperty("canReconcile").GetBoolean()),
                    762 => ("The kept-row count is reported as an integer", Has(imported, "Showing " + Number(kept) + " kept row")),
                    763 => ("The kept/read relationship and read count are reported", Has(imported, Number(kept) + " kept row(s) of " + Number(read) + " read")),
                    764 => ("The successful import reports its file count", Has(imported, "from " + Number(files) + " file")),
                    765 => ("The successful import reports its run count", Has(imported, "across " + Number(runs) + " run")),
                    766 when row.Detail.Contains("Next step", StringComparison.Ordinal) => ("The next action is choosing a SEPro package from the same runs", Has(imported, "choose the SEPro package", "SAME runs")),
                    766 => ("The actual filtering criteria reach the user", Has(imported, "length " + Number(imported.GetProperty("criteria").GetProperty("MinLength").GetInt32()), "consecutive residues", "% or better")),
                    _ => ("The same-run requirement and Reconcile command remain visible", Has(imported, "SAME runs", "Step 2", "Reconcile")),
                };
            }
            else
            {
                var result = homology.GetProperty("homology");
                int queries = result.GetProperty("PeptidesSearched").GetInt32(), mapped = result.GetProperty("MappedToTargets").GetInt32();
                int decoys = result.GetProperty("DecoyOnly").GetInt32(), unmatched = result.GetProperty("Unmatched").GetInt32(), families = result.GetProperty("Families").GetArrayLength();
                string share = (queries > 0 ? (double)mapped / queries : 0).ToString("P2", CultureInfo.InvariantCulture);
                (fact, present) = row.Line switch
                {
                    1731 => ("The number of distinct searched peptides is shown", Has(homology, "Showing " + Number(queries) + " distinct peptide")),
                    1732 => ("The searched family file is named beside the peptide count", Has(homology, "searched against command-fixture.fcf")),
                    1733 => ("The target-mapped count is shown", Has(homology, Number(mapped) + " reached a target")),
                    1734 => ("The target-mapped share is shown with two decimal places", Has(homology, share)),
                    1735 => ("The decoy-only count is shown", Has(homology, Number(decoys) + " reached decoys only")),
                    1736 => ("Decoy-only and unmatched outcomes remain distinct", Has(homology, "decoys only", Number(unmatched) + " reached nothing")),
                    1737 => ("The unmatched count is shown", Has(homology, Number(unmatched) + " reached nothing")),
                    1738 => ("The unmatched outcome and family count remain visible", Has(homology, Number(unmatched) + " reached nothing", Number(families) + " target family")),
                    1739 => ("The target-family count is shown as an integer", Has(homology, Number(families) + " target family")),
                    1740 => ("The user is told to interpret mapped count alongside the decoy control", Has(homology, "Read the mapped count against the decoy-only count")),
                    1741 => ("The false-family warning and export next action reach the user", Has(homology, "threshold invents a family", "Export the homology snapshot from Results")),
                    _ => ("Export assignments or continue to the former Layer 5 partition", false),
                };
                if (row.Line == 1742) { applicability = "requires Paulo ruling"; reason = "Assignment export remains in Results, but the Layer 5 partition screen was retired. This whole observation is not inferred DROP or VERIFIED."; }
            }
            string state = present ? "fact present" : "fact missing";
            records.Add(new { row.Id, row.Source, row.Line, frozenLiteral = row.Detail, fact, state, applicability, reason,
                currentStatus = row.Line <= 541 ? Text(source) : row.Line <= 761 ? Text(empty) : row.Line <= 767 ? Text(imported) : Text(homology) });
            if (applicability == "applies") Check("Changed-copy user fact " + row.Id + ": " + fact, present);
        }
        Check("Changed-copy inventory contains the exact 41 frozen observations", records.Count == 41);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "changed-copy-facts-41.json"), records);
    }
}
