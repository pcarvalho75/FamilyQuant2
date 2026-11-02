using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FamilyQuant.Tests;

internal enum TestCommandPhase { Raw, Normalized }

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestCommandTableAttribute : Attribute { }

internal sealed record TestCommand(string Flag, int Order, TestCommandPhase Phase,
    Func<string[], bool> Matches, Func<string[], string, int> Run);

/// <summary>Feature-owned tables share one dispatcher without sharing registration edit locations.</summary>
internal static class TestCommandRegistry
{
    private static readonly Lazy<IReadOnlyList<TestCommand>> Catalog = new(Discover);
    internal static IReadOnlyList<TestCommand> Commands => Catalog.Value;

    private static IReadOnlyList<TestCommand> Discover() => Validate(typeof(Program)
        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Where(method => method.IsDefined(typeof(TestCommandTableAttribute), false))
        .OrderBy(method => method.Name, StringComparer.Ordinal)
        .SelectMany(method => (IEnumerable<TestCommand>)(method.Invoke(null, null)
            ?? throw new InvalidOperationException("A test command table returned null: " + method.Name))), Program.PreservedCommandFlags);

    internal static IReadOnlyList<TestCommand> Validate(IEnumerable<TestCommand> source, IEnumerable<string>? requiredFlags = null)
    {
        TestCommand[] rows = source.ToArray();
        if (rows.Any(row => string.IsNullOrWhiteSpace(row.Flag) || !row.Flag.StartsWith("--", StringComparison.Ordinal)
            || row.Flag.Any(char.IsWhiteSpace) || row.Matches == null || row.Run == null))
            throw new InvalidOperationException("Every test command needs a flag, predicate and handler.");
        string[] duplicates = rows.GroupBy(row => row.Flag, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() != 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0) throw new InvalidOperationException("Duplicate test flags: " + string.Join(", ", duplicates));
        if (requiredFlags != null)
        {
            string[] missing = requiredFlags.Except(rows.Select(row => row.Flag), StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("Missing test flags: " + string.Join(", ", missing));
        }
        return Array.AsReadOnly(rows.OrderBy(row => row.Phase).ThenBy(row => row.Order)
            .ThenBy(row => row.Flag, StringComparer.Ordinal).ToArray());
    }

    internal static TestCommand? Resolve(IReadOnlyList<TestCommand> commands, string[] args, TestCommandPhase phase) =>
        commands.FirstOrDefault(command => command.Phase == phase
            && args.Contains(command.Flag, StringComparer.OrdinalIgnoreCase) && command.Matches(args));

    internal static TestCommand? Resolve(string[] args, TestCommandPhase phase) => Resolve(Commands, args, phase);

    internal static bool TryRun(string[] args, TestCommandPhase phase, string workDir, out int exitCode)
        => TryRun(Commands, args, phase, workDir, out exitCode);

    internal static bool TryRun(IReadOnlyList<TestCommand> commands, string[] args,
        TestCommandPhase phase, string workDir, out int exitCode)
    {
        TestCommand? command = Resolve(commands, args, phase);
        if (command == null) { exitCode = 0; return false; }
        exitCode = command.Run(args, workDir);
        return true;
    }
}
