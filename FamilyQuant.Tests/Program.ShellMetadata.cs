using System;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ShellMetadataTests(string[] args, string workDir)
    {
        ShellMetadataComparisonTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static void ShellMetadataComparisonTests()
    {
        var row = new FeatureLedger.Observation("fixture", "FamilyQuant/ComputeXicWindow.xaml", 95, "control", "IntegerUpDown IntegerUpDownIsotopes");
        var old = new ShellMetadataSnapshot.Fact(row.Id, row.Source, row.Line, row.Kind, row.Detail,
            "IntegerUpDownIsotopes", "$type", "Xceed.Wpf.Toolkit.IntegerUpDown", "IntegerUpDown", "", "Minimum=1;Maximum=6;Value=6");
        var retained = old with { Type = "FamilyQuant.IntegerUpDown" };
        ShellMetadataComparison.Result Compare(ShellMetadataSnapshot.Fact actual) => ShellMetadataComparison.Compare(row, [old], [actual]);
        Check("native metadata accepts the exact same control from its assigned view", Compare(old).Matched);
        Check("native metadata rejects another observation borrowing the same value", !Compare(old with { Id = "other" }).Matched);
        Check("native metadata rejects another owning view", !Compare(old with { Source = "FamilyQuant/Other.xaml" }).Matched);
        Check("native metadata rejects another named control", !Compare(old with { Anchor = "Other" }).Matched);
        Check("native metadata rejects a missing content-tree object", !Compare(old with { Reason = "Not in content." }).Matched);
        Check("native metadata rejects duplicate snapshots", !ShellMetadataComparison.Compare(row, [old], [old, old]).Matched);
        Check("native metadata requires an actual frozen observation", !ShellMetadataComparison.Compare(row, [], [old]).Matched);
        Check("native metadata refuses changed scalar values", !Compare(old with { Value = "different" }).Matched);
        Check("native metadata refuses a same-named unrelated runtime class", !Compare(old with { Type = "Unrelated.IntegerUpDown" }).Matched);
        Check("native metadata accepts the explicitly tested numeric-control replacement", Compare(retained).Matched);
        Check("numeric replacement requires equal actual constraints", !Compare(retained with { Traits = "Minimum=1;Maximum=9;Value=6" }).Matched);
        Check("numeric replacement requires equal actual defaults", !Compare(retained with { Traits = "Minimum=1;Maximum=6;Value=1" }).Matched);
        Check("numeric replacement cannot pass on a type name alone", !ShellMetadataComparison.Compare(row,
            [old with { Traits = "" }], [retained with { Traits = "" }]).Matched);
        Check("native metadata does not treat an event as metadata", !ShellMetadataComparison.Compare(row with { Kind = "handler" }, [old], [old]).Matched);
        var eventRow = row with { Kind = "handler", Detail = "Button.Click = Click" };
        var clicked = old with { Kind = eventRow.Kind, Detail = eventRow.Detail, Property = "$interaction", Type = "FamilyQuant.HomeControl", Value = "enabled=True;event counts=1,0,0,0,0", Traits = "" };
        Check("native event receipts require actual transition outcomes", ShellMetadataComparison.Compare(eventRow, [clicked], [clicked]).Matched);
        Check("native event receipts refuse a metadata-only observation", !ShellMetadataComparison.Compare(eventRow,
            [clicked with { Property = "$type" }], [clicked with { Property = "$type" }]).Matched);
        Check("native event receipts refuse a failed event execution", !ShellMetadataComparison.Compare(eventRow,
            [clicked], [clicked with { Reason = "Wrong event." }]).Matched);
        var statusRow = row with { Kind = "code fact", Detail = "\"No project open.\"" };
        var status = old with { Kind = statusRow.Kind, Detail = statusRow.Detail, Property = "Text", Type = "System.Windows.Controls.TextBlock", Value = "No project open.", Traits = "" };
        Check("native code facts equal their complete frozen literal", ShellMetadataComparison.Compare(statusRow, [status], [status]).Matched);
        Check("native code facts cannot borrow a different complete frozen literal", !ShellMetadataComparison.Compare(statusRow,
            [status with { Value = "borrowed" }], [status with { Value = "borrowed" }]).Matched);
        var binding = old with { Kind = "column", Property = "Binding", Type = "System.Windows.Controls.DataGridTextColumn", Value = "Path=UserID;Mode=TwoWay;Format=;Converter=" };
        var columnRow = row with { Kind = "column" };
        Check("native metadata preserves the resolved column path and edit mode", ShellMetadataComparison.Compare(columnRow, [binding], [binding]).Matched);
        Check("native metadata refuses a changed column edit mode", !ShellMetadataComparison.Compare(columnRow, [binding],
            [binding with { Value = "Path=UserID;Mode=OneWay;Format=;Converter=" }]).Matched);
        var numeric = new IntegerUpDown { Minimum = 1, Maximum = 6, Value = 6 };
        Check("native numeric metadata reads the actual derived nullable integer property",
            ShellMetadataSnapshot.Property(numeric.GetType(), "Value")?.PropertyType == typeof(int?)
            && Equals(ShellMetadataSnapshot.Property(numeric.GetType(), "Value")?.GetValue(numeric), 6));
    }
}
