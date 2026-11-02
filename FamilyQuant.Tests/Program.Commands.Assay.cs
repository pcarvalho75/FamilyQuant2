using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AssayCommandTable() =>
    [
        new("--dependency-file-check", 100, TestCommandPhase.Raw, MatchesCommand001, RunCommand001),
        new("--test-dependencies", 200, TestCommandPhase.Raw, MatchesCommand002, RunCommand002),
        new("--test-usearch-binary", 10000, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-usearch-binary", RunUseSearchBinaryTests),
    ];

    private static bool MatchesCommand001(string[] args) => args.Length > 0 && args[0] == "--dependency-file-check";
    private static int RunCommand001(string[] args, string workDir)
    {
        return DependencyFileCheck(args);

    }
    private static bool MatchesCommand002(string[] args) => args.Contains("--test-dependencies", StringComparer.Ordinal);
    private static int RunCommand002(string[] args, string workDir)
    {
        DependencyCacheTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }


}
