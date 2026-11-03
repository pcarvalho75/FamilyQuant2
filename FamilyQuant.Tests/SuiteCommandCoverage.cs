using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace FamilyQuant.Tests;

internal sealed record SuiteCommandRule(string Flag, string[] Fixtures, string? ExclusionReason)
{
    internal static SuiteCommandRule Covered(string flag, params string[] fixtures) => new(flag, fixtures, null);
    internal static SuiteCommandRule Excluded(string flag, string reason) => new(flag, Array.Empty<string>(), reason);
}

internal sealed record SuiteCommandCoverageRow(string Flag, string Disposition, string Fixtures, string Reason,
    string ReachableSuiteFixtures, string Problem);

/// <summary>Checks registration and compiled call references only. It never invokes a command or a suite body.
/// Explicit declarations are required for every flag, including data/proof helpers and unfamiliar flag names.</summary>
internal sealed class SuiteCommandCoverage
{
    private static readonly Dictionary<ushort, OpCode> Instructions = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => unchecked((ushort)code.Value));
    private readonly Dictionary<(MethodInfo Method, bool CallsOnly), MethodInfo[]> references = new();
    private readonly Assembly assembly = typeof(Program).Assembly;

    internal static string Key(MethodInfo method) => method.DeclaringType!.FullName + "." + method.Name
        + (method.IsGenericMethod ? "<" + string.Join(",", method.GetGenericArguments().Select(type => type.ToString())) + ">" : "") + "("
        + string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.ToString())) + ")";

    internal HashSet<string> SuiteFixtures(IEnumerable<Delegate> bodies)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<MethodInfo>();
        foreach (Delegate body in bodies)
        {
            // A method-group entry is itself the fixture. A lambda entry calls its fixture methods.
            if (!body.Method.Name.StartsWith("<", StringComparison.Ordinal)) result.Add(Key(body.Method));
            else
                foreach (var method in References(body.Method, callsOnly: true).Where(method => method.DeclaringType?.Assembly == assembly
                    && !method.Name.StartsWith("<", StringComparison.Ordinal) && !SharedHelper(method))) result.Add(Key(method));
            pending.Push(body.Method);
        }
        var visited = new HashSet<MethodInfo>();
        while (pending.TryPop(out var method))
        {
            if (!visited.Add(method) || method.DeclaringType?.Assembly != assembly || SharedHelper(method)
                || method.DeclaringType == typeof(Program) && method.Name is "DefaultSuite" or "RunSuite" or "Main") continue;
            if (AssertionFixture(method)) result.Add(Key(method));
            // Calls below an aggregate count; merely creating an unused delegate does not.
            foreach (var called in References(method, callsOnly: true)) pending.Push(called);
            var state = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            if (state?.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance) is { } move) pending.Push(move);
        }
        return result;
    }

    private bool AssertionFixture(MethodInfo method) => !method.Name.StartsWith("<", StringComparison.Ordinal)
        && method.DeclaringType?.IsDefined(typeof(CompilerGeneratedAttribute), false) != true
        && !method.Name.StartsWith("Run", StringComparison.Ordinal)
        && (method.Name.EndsWith("Tests", StringComparison.Ordinal) || method.Name.EndsWith("Checks", StringComparison.Ordinal))
        && References(method).Any(called => called.DeclaringType == typeof(Program) && called.Name == "Check");

    private HashSet<string> ReachableFixtures(MethodInfo start, HashSet<string> suiteFixtures)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<MethodInfo>();
        var pending = new Stack<MethodInfo>(); pending.Push(start);
        while (pending.TryPop(out var method))
        {
            if (!visited.Add(method) || method.DeclaringType?.Assembly != assembly) continue;
            string key = Key(method);
            if (SharedHelper(method)) continue;
            // Stop at fixture boundaries even if that fixture is missing from the suite. Otherwise
            // one enrolled helper could conceal another, unenrolled fixture in the same command.
            if (suiteFixtures.Contains(key) || AssertionFixture(method)) { found.Add(key); continue; }
            // Auditing or constructing a suite must never count as invoking its fixtures.
            if (method.DeclaringType == typeof(Program) && method.Name is "DefaultSuite" or "RunSuite" or "Main") continue;
            foreach (var called in References(method)) pending.Push(called);
            var state = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            if (state?.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance) is { } move) pending.Push(move);
        }
        return found;
    }

    private static bool SharedHelper(MethodInfo method) => method.DeclaringType == typeof(Program)
        && method.Name is "Check" or "EnsureApplication" or "Section" or "FeatureLedgerReady";

    internal IReadOnlyList<SuiteCommandCoverageRow> Audit(IReadOnlyList<TestCommand> commands,
        IReadOnlyList<SuiteCommandRule> rules, HashSet<string> suiteFixtures)
    {
        var rows = new List<SuiteCommandCoverageRow>();
        var declarations = rules.GroupBy(rule => rule.Flag, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var command in commands.OrderBy(command => command.Flag, StringComparer.Ordinal))
        {
            var reachable = ReachableFixtures(command.Run.Method, suiteFixtures);
            string candidates = string.Join(";", reachable.OrderBy(value => value, StringComparer.Ordinal));
            if (!declarations.TryGetValue(command.Flag, out var matches) || matches.Length != 1)
            {
                rows.Add(new(command.Flag, "UNCLASSIFIED", "", "", candidates,
                    matches == null ? "No coverage declaration or reasoned exclusion." : "Duplicate coverage declarations."));
                continue;
            }
            var rule = matches[0];
            string reason = rule.ExclusionReason ?? "";
            string fixtures = string.Join(";", rule.Fixtures.OrderBy(value => value, StringComparer.Ordinal));
            string problem = "";
            if (rule.ExclusionReason != null)
            {
                if (rule.Fixtures.Length != 0 || string.IsNullOrWhiteSpace(reason)) problem = "An exclusion requires its own nonempty reason and no claimed suite fixtures.";
            }
            else if (rule.Fixtures.Length == 0 || rule.Fixtures.Distinct(StringComparer.Ordinal).Count() != rule.Fixtures.Length)
                problem = "Suite coverage requires distinct explicit fixture entry points.";
            else if (rule.Fixtures.Any(fixture => !suiteFixtures.Contains(fixture)))
                problem = "A declared fixture is absent from DefaultSuite: " + string.Join(";", rule.Fixtures.Where(fixture => !suiteFixtures.Contains(fixture)));
            else if (!reachable.SetEquals(rule.Fixtures))
                problem = "Declared fixture entry points differ from the compiled command-to-suite references.";
            rows.Add(new(command.Flag, rule.ExclusionReason == null ? "SUITE" : "EXCLUDED", fixtures, reason, candidates, problem));
        }
        var flags = commands.Select(command => command.Flag).ToHashSet(StringComparer.Ordinal);
        foreach (var rule in rules.Where(rule => !flags.Contains(rule.Flag)).OrderBy(rule => rule.Flag, StringComparer.Ordinal))
            rows.Add(new(rule.Flag, "STALE", "", rule.ExclusionReason ?? "", "", "Declaration names an unregistered command."));
        return rows;
    }

    private MethodInfo[] References(MethodInfo method, bool callsOnly = false)
    {
        if (references.TryGetValue((method, callsOnly), out var cached)) return cached;
        var result = new List<MethodInfo>();
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
        for (int offset = 0; offset < bytes.Length;)
        {
            ushort value = bytes[offset++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
            var code = Instructions[value];
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(bytes, offset) * 4),
                _ => 4,
            };
            if (code.OperandType == OperandType.InlineMethod && (!callsOnly || code == OpCodes.Call || code == OpCodes.Callvirt)
                && method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset),
                method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null) is MethodInfo called)
                result.Add(called);
            offset = checked(offset + size);
            if (offset > bytes.Length) throw new InvalidDataException("A compiled command instruction extends past its method body.");
        }
        return references[(method, callsOnly)] = result.Distinct().ToArray();
    }

    internal static string Listing(IReadOnlyList<SuiteCommandCoverageRow> rows)
    {
        string Cell(string value) => value.Replace("\t", " ", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return "flag\tdisposition\tdeclared_suite_fixtures\texclusion_reason\tcommand_fixture_entry_points\tproblem\n"
            + string.Join("\n", rows.Select(row => string.Join("\t", new[] { row.Flag, row.Disposition, row.Fixtures, row.Reason, row.ReachableSuiteFixtures, row.Problem }.Select(Cell)))) + "\n";
    }
}
