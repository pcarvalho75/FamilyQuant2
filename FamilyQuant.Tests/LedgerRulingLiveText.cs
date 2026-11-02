using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Resources;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

// Only installed application/Core assemblies are inspected. Test strings are never witnesses.
internal static class LedgerRulingLiveText
{
    internal sealed record Witness(string AssemblyName, string Symbol, string Text);
    internal sealed record Match(bool Found, string AssemblyName, string Symbol, string TextSha256);
    private static readonly Lazy<IReadOnlyList<Witness>> Current = new(ReadCurrent);
    internal static IReadOnlyList<Witness> Values => Current.Value;
    internal static string Normalize(string text) => Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant).Trim();

    internal static Match Find(string text)
    {
        string wanted = Normalize(text);
        if (!wanted.Any(char.IsLetterOrDigit)) return new(false, "", "", "");
        foreach (var value in Values)
        {
            string actual = Normalize(value.Text); int offset = actual.IndexOf(wanted, StringComparison.Ordinal);
            while (offset >= 0)
            {
                int end = offset + wanted.Length;
                bool left = offset == 0 || !char.IsLetterOrDigit(wanted[0]) || !char.IsLetterOrDigit(actual[offset - 1]) && actual[offset - 1] != '_';
                bool right = end == actual.Length || !char.IsLetterOrDigit(wanted[^1]) || !char.IsLetterOrDigit(actual[end]) && actual[end] != '_';
                if (left && right) return new(true, value.AssemblyName, value.Symbol, Hash(value.Text));
                offset = actual.IndexOf(wanted, offset + 1, StringComparison.Ordinal);
            }
        }
        return new(false, "", "", "");
    }

    internal static string Hash(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private static IReadOnlyList<Witness> ReadCurrent()
    {
        var result = new List<Witness>();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(op => unchecked((ushort)op.Value));
        foreach (var assembly in new[] { typeof(App).Assembly, typeof(FamilyClusterFile).Assembly }.Distinct().OrderBy(value => value.GetName().Name, StringComparer.Ordinal))
        {
            string name = assembly.GetName().Name!;
            foreach (var type in assembly.GetTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (field.IsLiteral && field.FieldType == typeof(string) && field.GetRawConstantValue() is string text)
                        result.Add(new(name, type.FullName + "::" + field.Name, text));
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                    .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)).OrderBy(method => method.MetadataToken))
                {
                    byte[]? il = method.GetMethodBody()?.GetILAsByteArray(); if (il == null) continue;
                    for (int index = 0; index < il.Length;)
                    {
                        ushort code = il[index++]; if (code == 0xfe) code = (ushort)(0xfe00 | il[index++]);
                        if (!opcodes.TryGetValue(code, out var op)) throw new InvalidDataException("Unknown IL opcode.");
                        if (op.OperandType == OperandType.InlineString)
                            result.Add(new(name, type.FullName + "::" + method.Name, method.Module.ResolveString(BitConverter.ToInt32(il, index))));
                        index += op.OperandType switch {
                            OperandType.InlineNone => 0,
                            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                            OperandType.InlineVar => 2,
                            OperandType.InlineI8 or OperandType.InlineR => 8,
                            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, index),
                            _ => 4 };
                    }
                }
            }
            foreach (string resource in assembly.GetManifestResourceNames().Where(resource => resource.EndsWith(".g.resources", StringComparison.Ordinal)).OrderBy(resource => resource, StringComparer.Ordinal))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!; using var reader = new ResourceReader(stream);
                foreach (DictionaryEntry entry in reader)
                    if (entry.Key is string key && key.EndsWith(".baml", StringComparison.Ordinal) && entry.Value is Stream baml)
                        ReadBaml(name, key, baml, result);
            }
        }
        // Resolved Help joins compiled fragments and formats current enum labels. No window is shown.
        foreach (var mode in Enum.GetValues<NormalizationMode>())
        foreach (var shared in Enum.GetValues<SharedPeptidePolicy>())
            result.Add(new(typeof(FamilyClusterFile).Assembly.GetName().Name!, "FamilyQuant.HelpContent::Build/" + mode + "/" + shared,
                HelpContent.Build(null, mode, shared)));
        return result.Where(value => value.Text.Length > 0).Distinct().OrderBy(value => value.AssemblyName, StringComparer.Ordinal)
            .ThenBy(value => value.Symbol, StringComparer.Ordinal).ThenBy(value => value.Text, StringComparer.Ordinal).ToArray();
    }

    private static void ReadBaml(string assembly, string resource, Stream stream, List<Witness> result)
    {
        var type = typeof(System.Windows.Application).Assembly.GetType("System.Windows.Baml2006.Baml2006Reader")
            ?? throw new InvalidDataException("Compiled WPF reader is unavailable.");
        var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Stream) }, null)
            ?? throw new InvalidDataException("Compiled WPF stream reader is unavailable.");
        object reader = constructor.Invoke(new object[] { stream });
        try
        {
            var read = type.GetMethod("Read")!; var nodeType = type.GetProperty("NodeType")!; var value = type.GetProperty("Value")!;
            while ((bool)read.Invoke(reader, null)!)
                if (nodeType.GetValue(reader)?.ToString() == "Value" && value.GetValue(reader) is string text)
                    result.Add(new(assembly, "BAML:" + resource, text));
        }
        finally { (reader as IDisposable)?.Dispose(); }
    }
}
