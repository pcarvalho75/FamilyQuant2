using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SuiteMergeGuardCommands() =>
    [
        new("--test-suite-merge-guard", 80, TestCommandPhase.Raw,
            args => args.Contains("--test-suite-merge-guard", StringComparer.Ordinal), (_, _) =>
            {
                SuiteMergeGuardTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void SuiteMergeGuardTests()
    {
        string? root = FindLedgerRoot();
        Check("J8 suite merge guard finds the source checkout", root != null);
        if (root == null) return;
        string source = File.ReadAllText(Path.Combine(root, "FamilyQuant.Tests", "Program.cs"));
        bool Valid(string text)
        {
            int start = text.IndexOf("private static int Main(", StringComparison.Ordinal);
            int end = text.IndexOf("// Later real-data checks", start, StringComparison.Ordinal);
            if (start < 0 || end < 0) return false;
            string main = text.Substring(start, end - start);
            return !Regex.IsMatch(main, @"(?m)^\s*Section\s*\(")
                && Regex.Matches(main, @"\bRunSuite\s*\(").Count == 1
                && main.IndexOf("TakeSuiteShard", StringComparison.Ordinal) < main.IndexOf("RunSuite", StringComparison.Ordinal);
        }
        Check("J8 Main runs only the ordered suite after shard selection", Valid(source));
        string planted = source.Replace("            RunSuite(DefaultSuite(args, workDir), shard);",
            "            Section(\"duplicate\");\n            RunSuite(DefaultSuite(args, workDir), shard);", StringComparison.Ordinal);
        Check("J8 guard rejects a planted straight-line suite call", !Valid(planted));
    }
}
