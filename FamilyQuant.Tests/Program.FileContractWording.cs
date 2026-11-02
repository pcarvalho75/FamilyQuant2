using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    // This decision list supplies no ledger proof. In particular a compiled refusal string is
    // described as such, rather than represented as an executed clustering or format check.
    private static void FileContractWording(string output)
    {
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var current = new USearchLib();
        typeof(USearchLib).GetMethod("ButtonCalculateFDR_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(current, new object[] { current, new RoutedEventArgs() });
        string missingFile = current.ViewModel.Details;
        typeof(USearchLib).GetProperty("fcf")!.SetValue(current, new FamilyClusterFile { MyExtendedFastaItems = new() {
            new() { SequenceIdentifier = "native-target", Sequence = "ACDEFGHIK", UserID = 1 } } });
        typeof(USearchLib).GetMethod("ButtonCalculateFDR_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(current, new object[] { current, new RoutedEventArgs() });
        string report = BuildReportFormatter.Format(new() { new() { Identity = 90, Groups = 1, TargetGroups = 1, MixedGroups = 0 } });
        string refusal = FileContractStrings(typeof(FamilyClusterFile).Assembly.GetType("FamilyQuant.FamilyClusterScoring", true)!.GetMethod("DivideAndConquerDetailed")!)
            .Single(value => value.Contains("Use a target-decoy database (.T-R)", StringComparison.Ordinal));
        var values = new[] {
            ("FamilyQuant/USearch.xaml.cs", 213, missingFile, "Current native no-file FDR refusal. Added because the shared row contains quoted line breaks; this is explanatory copy, not a file-format proof."),
            ("FamilyQuant/DeNovoControl.xaml.cs", 281, new DeNovoControl().ViewModel.Status, "Current native initial Discovery status. The former RAW-extraction status block was retired from this UI; no format proof or inferred DROP."),
            ("FamilyQuant/FamilyQuantFile.cs", 682, refusal, "Exact compiled current refusal fragment. The USEARCH no-decoy branch was not executed by this format packet; no behavioral proof inferred."),
            ("FamilyQuant/USearch.xaml.cs", 223, current.ViewModel.Details, "Current native mixing-score handler on a synthetic target-only file; explanatory copy is listed for Paulo and is not a file-format proof."),
            ("FamilyQuant/USearch.xaml.cs", 438, report, "Current native report formatter on one synthetic no-decoy identity row; explanatory copy is listed for Paulo and is not a file-format proof.") };
        var lines = new List<string> { SecondaryWordingHeader };
        foreach (var value in values)
        {
            var row = inventory.Observations.Single(row => row.Source == value.Item1 && row.Line == value.Item2 && row.Kind is "format/export" or "export/storage fact");
            string old = JsonSerializer.Deserialize<string>(row.Detail)!;
            if (string.IsNullOrWhiteSpace(value.Item3)) throw new InvalidDataException("The current wording capture is empty: " + row.Id);
            lines.Add(string.Join('\t', new[] { "FQFloater", row.Id, old, value.Item3, value.Item4 }.Select(SecondaryWordingCell)));
        }
        File.WriteAllLines(Path.Combine(output, "wording-file-gaps.tsv"), lines, new UTF8Encoding(false));
    }

    private static IEnumerable<string> FileContractStrings(MethodInfo method)
    {
        byte[] code = method.GetMethodBody()!.GetILAsByteArray()!;
        var operations = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(operation => unchecked((ushort)operation.Value));
        for (int offset = 0; offset < code.Length;)
        {
            ushort value = code[offset++]; if (value == 0xfe) value = (ushort)(0xfe00 | code[offset++]);
            OpCode operation = operations[value];
            if (operation.OperandType == OperandType.InlineString) yield return method.Module.ResolveString(BitConverter.ToInt32(code, offset));
            offset += operation.OperandType switch {
                OperandType.InlineNone => 0, OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2, OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(code, offset) * 4), _ => 4 };
        }
    }
}
