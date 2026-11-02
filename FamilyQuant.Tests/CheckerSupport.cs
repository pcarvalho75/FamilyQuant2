using System;

namespace FamilyQuant.Tests;

internal static class TestArguments
{
    internal static string EmptyFallsBack(string[] args, int flag, int offset, string fallback) =>
        args.Length > flag + offset && args[flag + offset].Length > 0 ? args[flag + offset] : fallback;

    internal static string EmptyPassesThrough(string[] args, int flag, int offset, string fallback) =>
        args.Length > flag + offset ? args[flag + offset] : fallback;
}

internal static class TestCsvFields
{
    internal static string Field(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}

internal static class FrozenAnswerKey
{
    internal static string PythonAccession(string value)
    {
        if (value.StartsWith("contaminant_", StringComparison.Ordinal))
            return value.Substring("contaminant_".Length).Split('|')[0];
        string[] parts = value.Split('|');
        return parts.Length >= 2 ? parts[1] : value;
    }
}
