using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SuiteCommandCoverageCommands() =>
    [new("--test-suite-command-coverage", 82, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-suite-command-coverage", (args, _) =>
        {
            if (args.Length > 3 || args.Skip(1).Count(arg => arg == "--negative-control") > 1
                || args.Skip(1).Count(arg => arg != "--negative-control") > 1
                || args.Skip(1).Any(arg => arg.StartsWith("--", StringComparison.Ordinal) && arg != "--negative-control"))
                throw new ArgumentException("Use --test-suite-command-coverage [output.tsv] [--negative-control].");
            SuiteCommandCoverageTests(args.Skip(1).FirstOrDefault(arg => arg != "--negative-control"), args.Contains("--negative-control", StringComparer.Ordinal));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void SuiteCommandCoverageTests(string? output, bool negative = false)
    {
        Section("K8b every registered command has suite coverage or an explicit exclusion");
        var checker = new SuiteCommandCoverage();
        var suite = DefaultSuite(Array.Empty<string>(), Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests"));
        var fixtures = checker.SuiteFixtures(suite.Entries.Select(entry => (Delegate)entry.Body));
        var commands = TestCommandRegistry.Commands;
        var rules = SuiteCommandInventory.Entries;
        bool invoked = false;
        var planted = new TestCommand("--test-k8b-planted-ungated", 0, TestCommandPhase.Raw, _ => true, (_, _) => { invoked = true; return 0; });
        var audited = negative ? TestCommandRegistry.Validate(commands.Append(planted), PreservedCommandFlags) : commands;
        var rows = checker.Audit(audited, rules, fixtures);
        string listing = SuiteCommandCoverage.Listing(rows);
        Console.Write(listing);
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, listing, new UTF8Encoding(false));
            File.WriteAllLines(path + ".suite-fixtures.txt", fixtures.OrderBy(value => value, StringComparer.Ordinal), new UTF8Encoding(false));
            File.WriteAllLines(path + ".suite-entries.tsv", new[] { "index\tsection\tfixtures" }.Concat(suite.Entries.Select((entry, index) =>
                index.ToString(CultureInfo.InvariantCulture) + "\t" + entry.Section.Replace('\t', ' ') + "\t"
                + string.Join(";", checker.SuiteFixtures(new[] { (Delegate)entry.Body }).OrderBy(value => value, StringComparer.Ordinal)))), new UTF8Encoding(false));
        }
        var problems = rows.Where(row => row.Problem.Length != 0).ToArray();
        Check("K8b every registered command is covered or explicitly excluded with a reason", problems.Length == 0,
            string.Join(Environment.NewLine, problems.Select(row => row.Flag + ": " + row.Problem)));
        Check("K8b inventory lists every registered flag exactly once", rows.Count == audited.Count
            && rows.Select(row => row.Flag).ToHashSet(StringComparer.Ordinal).SetEquals(audited.Select(command => command.Flag)));
        Check("K8b a newly registered no-RAW test without enrollment fails", checker.Audit(new[] { planted }, Array.Empty<SuiteCommandRule>(), fixtures).Single().Problem.Length > 0);
        Check("K8b an unfamiliar command name cannot bypass enrollment", checker.Audit(new[] { planted with { Flag = "--unfamiliar-check" } }, Array.Empty<SuiteCommandRule>(), fixtures).Single().Problem.Length > 0);
        Check("K8b an empty exclusion reason fails", checker.Audit(new[] { planted }, new[] { SuiteCommandRule.Excluded(planted.Flag, " ") }, fixtures).Single().Problem.Length > 0);
        Check("K8b a stale exclusion fails", checker.Audit(Array.Empty<TestCommand>(), new[] { SuiteCommandRule.Excluded(planted.Flag, "Owned manual probe.") }, fixtures).Single().Problem.Length > 0);
        Check("K8b duplicate declarations fail", checker.Audit(new[] { planted }, new[] { SuiteCommandRule.Excluded(planted.Flag, "First reason."), SuiteCommandRule.Excluded(planted.Flag, "Second reason.") }, fixtures).Single().Problem.Length > 0);
        string Fixture(string name) => SuiteCommandCoverage.Key(typeof(Program).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!);
        string entry = Fixture(nameof(SuiteCommandCoverageTests));
        var self = commands.Single(command => command.Flag == "--test-suite-command-coverage");
        var declared = new[] { SuiteCommandRule.Covered(self.Flag, entry) };
        Check("K8b removing an enrolled fixture from the suite fails", checker.Audit(new[] { self }, declared, new HashSet<string>(StringComparer.Ordinal)).Single().Problem.Length > 0);
        Check("K8b naming a suite fixture without calling it cannot claim coverage", checker.Audit(new[] { planted }, new[] { SuiteCommandRule.Covered(planted.Flag, entry) }, fixtures).Single().Problem.Length > 0);
        var partial = planted with { Run = (_, _) => { SuiteMergeGuardTests(); K8bMissingFixtureTests(); return 0; } };
        Check("K8b an enrolled helper cannot conceal an unenrolled fixture", checker.Audit(new[] { partial },
            new[] { SuiteCommandRule.Covered(planted.Flag, Fixture(nameof(SuiteMergeGuardTests))) }, fixtures).Single().Problem.Length > 0);
        var helper = planted with { Run = (_, _) => { Check("Never executed", true); return 0; } };
        Check("K8b the common assertion helper cannot prove fixture coverage", checker.Audit(new[] { helper },
            new[] { SuiteCommandRule.Covered(planted.Flag, Fixture(nameof(Check))) }, fixtures).Single().Problem.Length > 0);
        Action aggregate = K8cAggregateFixtureTests;
        var nestedFixtures = checker.SuiteFixtures(new[] { aggregate });
        string nestedKey = Fixture(nameof(K8cNestedFixtureTests));
        var nestedCommand = planted with { Run = (_, _) => { K8cNestedFixtureTests(); return 0; } };
        Check("K8c an actually called nested fixture is already gated through its aggregate", checker.Audit(new[] { nestedCommand },
            new[] { SuiteCommandRule.Covered(planted.Flag, nestedKey) }, nestedFixtures).Single().Problem.Length == 0);
        Action unusedReference = K8cUnusedReferenceFixtureTests;
        Check("K8c an unused delegate reference does not enroll a fixture", !checker.SuiteFixtures(new[] { unusedReference }).Contains(nestedKey));
        Check("K8c metadata-only planted fixtures do not contaminate the real suite", !fixtures.Contains(nestedKey));
        Check("K8b auditing never invokes a registered command", !invoked);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("K8b coverage listing is culture invariant", listing == SuiteCommandCoverage.Listing(checker.Audit(audited, rules, fixtures)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void K8bMissingFixtureTests()
    {
        Check("Never executed missing fixture", false);
        throw new InvalidOperationException("The metadata-only negative control must never execute.");
    }

    private static void K8cAggregateFixtureTests() => K8cNestedFixtureTests();

    private static void K8cUnusedReferenceFixtureTests()
    {
        Action unused = K8cNestedFixtureTests;
        GC.KeepAlive(unused);
    }

    private static void K8cNestedFixtureTests()
    {
        Check("Never executed nested fixture", false);
        throw new InvalidOperationException("The metadata-only nested control must never execute.");
    }
}
