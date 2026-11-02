using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static LedgerMemberFactComparison.MemberMapping? ShellPartialRelocation(string source,
        LedgerMemberFactComparison.Result result, string frozen, IReadOnlyDictionary<string, string> current)
    {
        if (result.Reason != "The enclosing type declaration changed and needs an explicit reviewed relocation."
            || result.CurrentPath.Length == 0 || result.CurrentMember != result.FrozenMember) return null;
        var type = source switch
        {
            "FamilyQuant/MainWindow.xaml.cs" => ("MainWindow", "public partial class MainWindow : Window", "public partial class MainWindow : Window", "public partial class MainWindow"),
            "FamilyQuant/ClusterGenerationControl.xaml.cs" => ("ClusterGenerationControl", "public partial class ClusterGenerationControl : UserControl", "public partial class ClusterGenerationControl : UserControl", "public partial class ClusterGenerationControl"),
            "FamilyQuant/USearch.xaml.cs" => ("USearchLib", "public partial class USearchLib : UserControl", "public partial class USearchLib : UserControl", "public partial class USearchLib"),
            "FamilyQuant/LockManagerWindow.cs" => ("LockManagerWindow", "public class LockManagerWindow : Window", "public partial class LockManagerWindow : Window", "public partial class LockManagerWindow"),
            "FamilyQuant/SpectrumComparisonWindow.cs" => ("SpectrumComparisonWindow", "public static class SpectrumComparisonWindow", "public static partial class SpectrumComparisonWindow", "public static partial class SpectrumComparisonWindow"),
            _ => ("", "", "", "")
        };
        if (type.Item1.Length == 0 || !current.TryGetValue(source, out string? primary)
            || !current.TryGetValue(result.CurrentPath, out string? destination)) return null;
        string owner = "FamilyQuant." + type.Item1;
        if (!result.FrozenMember.StartsWith(owner + "::", StringComparison.Ordinal)
            || !LedgerMemberFactComparison.HasTypeDeclaration(frozen, owner, type.Item2)
            || !LedgerMemberFactComparison.HasTypeDeclaration(primary, owner, type.Item3)
            || !(LedgerMemberFactComparison.HasTypeDeclaration(destination, owner, type.Item3)
                || LedgerMemberFactComparison.HasTypeDeclaration(destination, owner, type.Item4))) return null;
        return new(result.FrozenMember, result.CurrentPath, result.CurrentMember);
    }

    private static string[] ShellLedgerSources(string frozenPath, IEnumerable<string> available)
    {
        if (frozenPath.EndsWith(".xaml", StringComparison.Ordinal))
        {
            string[] views = frozenPath == "FamilyQuant/ClusterGenerationControl.xaml"
                ? [frozenPath, "FamilyQuant/CurationQuantitationPanel.xaml"] : [frozenPath];
            return available.Where(views.Contains).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }
        string name = Path.GetFileName(frozenPath).Replace(".xaml.cs", "", StringComparison.Ordinal);
        if (name.EndsWith(".cs", StringComparison.Ordinal)) name = name[..^3];
        return available.Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => Path.GetFileName(path) == name + ".cs"
                || Path.GetFileName(path).StartsWith(name + ".", StringComparison.Ordinal)
                || name == "ClusterGenerationControl" && Path.GetFileName(path).StartsWith("Curation", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private static void ShellLedgerComparisonTests()
    {
        string[] files = ["FamilyQuant/MainWindow.xaml", "FamilyQuant/OtherWindow.xaml",
            "FamilyQuant/MainWindow.Documents.cs", "FamilyQuant/MainWindow.xaml.cs",
            "FamilyQuant/ClusterGenerationControl.xaml", "FamilyQuant/CurationQuantitationPanel.xaml",
            "FamilyQuant/ClusterGenerationControl.Presentation.cs", "FamilyQuant/CurationViewModel.cs",
            "FamilyQuant/CurationQuantitationViewModel.cs", "FamilyQuant.Core/GridCopy.cs"];
        Check("shell declarations cannot borrow a named property from another window",
            ShellLedgerSources("FamilyQuant/MainWindow.xaml", files).SequenceEqual(new[] { "FamilyQuant/MainWindow.xaml" }));
        Check("curation declarations compare only the two explicitly reviewed views",
            ShellLedgerSources("FamilyQuant/ClusterGenerationControl.xaml", files).SequenceEqual(new[]
            { "FamilyQuant/ClusterGenerationControl.xaml", "FamilyQuant/CurationQuantitationPanel.xaml" }));
        Check("shell member comparisons include its partial source files only",
            ShellLedgerSources("FamilyQuant/MainWindow.xaml.cs", files).SequenceEqual(new[]
            { "FamilyQuant/MainWindow.Documents.cs", "FamilyQuant/MainWindow.xaml.cs" }));
        Check("curation member comparisons include its explicitly assigned presentation models",
            ShellLedgerSources("FamilyQuant/ClusterGenerationControl.xaml.cs", files).SequenceEqual(new[]
            { "FamilyQuant/ClusterGenerationControl.Presentation.cs", "FamilyQuant/CurationQuantitationViewModel.cs", "FamilyQuant/CurationViewModel.cs" }));
        Check("an unchanged Core relocation is eligible without unrelated source borrowing",
            ShellLedgerSources("FamilyQuant/GridCopy.cs", files).SequenceEqual(new[] { "FamilyQuant.Core/GridCopy.cs" }));
        Check("a missing logical module stays unverified",
            ShellLedgerSources("FamilyQuant/Missing.xaml", files).Length == 0);
        const string old = "namespace FamilyQuant; public partial class MainWindow : Window { private string Copy = \"retained\"; }";
        const string moved = "namespace FamilyQuant; public partial class MainWindow { private string Copy = \"retained\"; }";
        const string primary = "namespace FamilyQuant; public partial class MainWindow : Window { private int State; }";
        var row = new FeatureLedger.Observation("fixture", "FamilyQuant/MainWindow.xaml.cs", 1, "code fact", "\"retained\"");
        var current = new Dictionary<string, string>
        { [row.Source] = primary, ["FamilyQuant/MainWindow.Documents.cs"] = moved };
        var comparison = LedgerMemberFactComparison.Compare(row, old, current);
        var mapping = ShellPartialRelocation(row.Source, comparison, old, current);
        Check("an inspected partial split with the same primary base still requires exact complete member tokens",
            mapping != null && LedgerMemberFactComparison.Compare(row, old, current, mapping).Matched);
        current[row.Source] = primary.Replace(": Window", ": OtherBase", StringComparison.Ordinal);
        Check("the partial relocation guard refuses a changed primary base",
            ShellPartialRelocation(row.Source, comparison, old, current) == null);
        current[row.Source] = primary;
        current[comparison.CurrentPath] = moved.Replace("private string Copy", "private static string Copy", StringComparison.Ordinal);
        Check("an authorized partial split cannot approve a changed member",
            mapping != null && !LedgerMemberFactComparison.Compare(row, old, current, mapping).Matched);
    }
}
