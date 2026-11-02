using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void CommandRegistryTests()
    {
        Check("X16: test commands use a registration table",
            typeof(Program).Assembly.GetType("FamilyQuant.Tests.TestCommandRegistry") != null);
        var commands = TestCommandRegistry.Commands;
        string[] missing = PreservedCommandFlags.Where(flag => !commands.Any(row => row.Flag == flag)).ToArray();
        Check("X16: every pre-migration flag remains registered", missing.Length == 0, string.Join(", ", missing));
        Check("X16: a planted missing flag fails the catalog guard", Throws<InvalidOperationException>(() =>
            TestCommandRegistry.Validate(commands.Where(row => row.Flag != "--test-identity-study"), PreservedCommandFlags)));
        Check("X16: feature tables are discovered independently", typeof(Program).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Count(method => method.IsDefined(typeof(TestCommandTableAttribute), false)) >= 3);
        Check("X16: contains flags keep their position-independent matching", TestCommandRegistry.Resolve(
            new[] { ".", "--test-core-boundary" }, TestCommandPhase.Raw)?.Flag == "--test-core-boundary");
        Check("X16: first-position flags remain first-position only", TestCommandRegistry.Resolve(
            new[] { ".", "--dependency-file-check" }, TestCommandPhase.Raw) == null);
        Check("X16: exact-arity screen flags still refuse wrong arity", TestCommandRegistry.Resolve(
            new[] { "--shell-menu-screens" }, TestCommandPhase.Raw) == null && TestCommandRegistry.Resolve(
            new[] { "--shell-menu-screens", "output" }, TestCommandPhase.Raw)?.Flag == "--shell-menu-screens");
        Check("X16: handler-owned arity validation is preserved", TestCommandRegistry.Resolve(
            new[] { "--feature-ledger-generate" }, TestCommandPhase.Raw)?.Flag == "--feature-ledger-generate");
        Check("X16: original case-insensitive screen flag stays case-insensitive", TestCommandRegistry.Resolve(
            new[] { "--SCREENS", "output" }, TestCommandPhase.Raw)?.Flag == "--screens");
        Check("X16: original case-sensitive fixture flag stays case-sensitive", TestCommandRegistry.Resolve(
            new[] { "--TEST-CORE-BOUNDARY" }, TestCommandPhase.Raw) == null);
        Check("X16: normalized flags are deferred until after positional normalization", TestCommandRegistry.Resolve(
            new[] { "--golden-verify" }, TestCommandPhase.Raw) == null && TestCommandRegistry.Resolve(
            new[] { "--golden-verify" }, TestCommandPhase.Normalized)?.Flag == "--golden-verify");
        Check("X16: legacy multi-flag precedence is retained", TestCommandRegistry.Resolve(
            new[] { "--test-ui-icons", "--test-dependencies" }, TestCommandPhase.Raw)?.Flag == "--test-dependencies");
        Check("X16: clustering aliases retain the same handler", commands.Single(row => row.Flag == "--clustering-snapshot").Run.Method ==
            commands.Single(row => row.Flag == "--clustering-export-fcf").Run.Method &&
            commands.Single(row => row.Flag == "--clustering-snapshot").Run.Method ==
            commands.Single(row => row.Flag == "--clustering-verify-fcf").Run.Method);
        Check("X16: positional data and unknown flags retain the ordinary-suite path", TestCommandRegistry.Resolve(
            new[] { "data.plp", "data.fasta" }, TestCommandPhase.Raw) == null && TestCommandRegistry.Resolve(
            new[] { "--unknown-fixture-flag" }, TestCommandPhase.Normalized) == null);

        bool ran = false;
        string[] arguments = { "--fixture" };
        var fixture = new TestCommand("--fixture", 10, TestCommandPhase.Normalized, _ => true, (received, work) =>
        {
            ran = ReferenceEquals(received, arguments) && work == "fixture-work";
            return 37;
        });
        var fixtureRows = TestCommandRegistry.Validate(new[] { fixture });
        Check("X16: resolving a command does not execute its handler", TestCommandRegistry.Resolve(
            fixtureRows, arguments, TestCommandPhase.Normalized) == fixture && !ran);
        Check("X16: dispatch forwards exact arguments, work directory and exit code", TestCommandRegistry.TryRun(
            fixtureRows, arguments, TestCommandPhase.Normalized, "fixture-work", out int exit) && ran && exit == 37);
        Check("X16: duplicate flags fail instead of hiding a handler", Throws<InvalidOperationException>(() =>
            TestCommandRegistry.Validate(new[] { fixture, fixture with { Flag = "--FIXTURE" } })));
        var rows = TestCommandRegistry.Validate(new[] { fixture, fixture with { Flag = "--earlier", Order = 1 } });
        Check("X16: priority is independent of table-discovery order", TestCommandRegistry.Resolve(rows,
            new[] { "--fixture", "--earlier" }, TestCommandPhase.Normalized)?.Flag == "--earlier");

        string CatalogText()
        {
            TextWriter previous = Console.Out;
            using var capture = new StringWriter(CultureInfo.InvariantCulture);
            try { Console.SetOut(capture); ListTestCommands(Array.Empty<string>(), string.Empty); }
            finally { Console.SetOut(previous); }
            return capture.ToString();
        }
        string first = CatalogText();
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("X16: catalog repeats exactly across display cultures", first == CatalogText());
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }

        string SourceFile(string relative)
        {
            foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
                for (DirectoryInfo? folder = new(start); folder != null; folder = folder.Parent)
                {
                    string path = Path.Combine(folder.FullName, relative);
                    if (File.Exists(path)) return File.ReadAllText(path);
                }
            throw new FileNotFoundException(relative);
        }
        Check("X16: Program.cs uses standard merge conflict detection", !Regex.IsMatch(SourceFile(".gitattributes"),
            @"^\s*FamilyQuant\.Tests/Program\.cs\s+.*\bmerge=union\b", RegexOptions.Multiline));
        string source = SourceFile("FamilyQuant.Tests/Program.cs");
        int mainStart = source.IndexOf("private static int Main", StringComparison.Ordinal);
        string dispatch = source[mainStart..source.IndexOf("Section(\"CSV export", mainStart, StringComparison.Ordinal)];
        Check("X16: Main has two stable dispatch calls and no flag literals", !dispatch.Contains("--", StringComparison.Ordinal) &&
            Regex.Matches(dispatch, @"TestCommandRegistry\.TryRun\(").Count == 2);
    }
}
