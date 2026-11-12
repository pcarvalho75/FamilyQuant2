using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyCounterFilterCommands() =>
    [new("--test-family-counter-filters", 10, TestCommandPhase.Raw, a => a.Contains("--test-family-counter-filters", StringComparer.Ordinal), (_, _) =>
    {
        FamilyCounterFilterTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void FamilyCounterFilterTests()
    {
        EnsureApplication();
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "sp|P59001|FIXTURE", Description = "Chosen protein", Sequence = "ACDEFGHIK", UserID = 1 },
            new() { SequenceIdentifier = "Reverse_sp|P59002|FIXTURE", Description = "Other protein", Sequence = "KHDGFIECA", UserID = 2 },
            new() { SequenceIdentifier = "contaminant_sp|P59003|FIXTURE", Description = "Other protein", Sequence = "ACDEFGHIK", UserID = 3 },
        } };
        string unchanged = file.ToJsonText();
        var screen = new ClusterGenerationControl(file);
        var counter = (Label)screen.FindName("LabelNumberOfGroups");
        var decoys = (CheckBox)screen.FindName("CheckBoxExcludeReverse");
        var contaminants = (CheckBox)screen.FindName("CheckBoxExcludeContaminants");
        var quantified = (CheckBox)screen.FindName("CheckBoxShowXICOnly");
        var search = (TextBox)screen.FindName("TextBoxSearch");
        string Count(int n) => n.ToString("N0", CultureInfo.CurrentCulture);
        Check("SF4 default caption names only decoys and retains both existing counts", (string)counter.Content
            == Count(2) + " shown of " + Count(3) + " in file; hidden: decoys");
        decoys.IsChecked = false;
        Check("SF4 no active filters means no hidden-filter claim", (string)counter.Content
            == Count(3) + " shown of " + Count(3) + " in file");
        search.Text = "Chosen"; contaminants.IsChecked = true;
        Check("SF4 search and contaminants are named when active", (string)counter.Content
            == Count(1) + " shown of " + Count(3) + " in file; hidden: contaminants, search");
        decoys.IsChecked = true; quantified.IsChecked = true;
        Check("SF4 quantified-only and every other active filter are named without invented counts", (string)counter.Content
            == Count(0) + " shown of " + Count(3) + " in file; hidden: decoys, contaminants, search, only quantified");
        Check("SF4 display filter changes preserve every project byte", file.ToJsonText() == unchanged);
    }
}
