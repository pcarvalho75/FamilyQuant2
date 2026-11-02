using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static CoreRuntimeCase? XicManifestKeyCase(FeatureLedger.Observation observation, string file, bool negative)
    {
        Type owner = typeof(FullXicComputer).Assembly.GetType("FamilyQuant.FullXicSourceStage", true)!;
        MethodInfo parent = owner.GetMethod("RebuildObservedInputManifests", BindingFlags.NonPublic | BindingFlags.Static)!;
        var calls = XicReferencedMethods(parent);
        // The callback must actually be referenced by this canonicalization method, which must call
        // GroupBy. Its signature and called operations identify the key projection, not matching text.
        if (!calls.Any(method => method.DeclaringType == typeof(Enumerable) && method.Name == "GroupBy")) return null;
        var selectors = calls.Where(method => method.Name.StartsWith("<RebuildObservedInputManifests>", StringComparison.Ordinal)
            && method.ReturnType == typeof(string) && method.GetParameters().Length == 1
            && method.GetParameters()[0].ParameterType == typeof(DeNovoInputManifest)).Distinct().Where(method =>
        {
            var body = XicReferencedMethods(method);
            return body.Any(call => call.DeclaringType == typeof(string) && call.Name == "Concat"
                    && call.GetParameters().Length == 3 && call.GetParameters().All(parameter => parameter.ParameterType == typeof(string)))
                && body.Any(call => call.DeclaringType == typeof(Path) && call.Name == "GetFullPath")
                && body.Any(call => call.DeclaringType == typeof(DeNovoInputManifest) && call.Name == "get_SourceId")
                && body.Any(call => call.DeclaringType == typeof(DeNovoInputManifest) && call.Name == "get_FilePath");
        }).ToArray();
        if (selectors.Length != 1) return null;
        MethodInfo selector = selectors[0];
        object? target = selector.IsStatic ? null : Activator.CreateInstance(selector.DeclaringType!, nonPublic: true);
        string separator = JsonSerializer.Deserialize<string>(observation.Detail)!;
        string?[] sources = { "native-source-a", "native-source-b", null };
        string[] expected = sources.Select(source => (source ?? string.Empty) + separator + Path.GetFullPath(file)).ToArray();
        string[] actual = sources.Select((source, index) => (string)selector.Invoke(target, new object[]
            { new DeNovoInputManifest { SourceId = negative && index == 0 ? "wrong-native-source" : source!, FilePath = file } })!).ToArray();
        return new(observation.Id, "execute canonicalization GroupBy key selector linked from its compiled parent",
            JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual), expected.SequenceEqual(actual) && actual.Distinct(StringComparer.Ordinal).Count() == 3);
    }

    private static List<MethodInfo> XicReferencedMethods(MethodInfo method)
    {
        var result = new List<MethodInfo>();
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
        var instructions = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));
        for (int offset = 0; offset < bytes.Length;)
        {
            ushort value = bytes[offset++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
            OpCode code = instructions[value];
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(bytes, offset) * 4),
                _ => 4,
            };
            if (code.OperandType == OperandType.InlineMethod
                && method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset)) is MethodInfo called) result.Add(called);
            offset = checked(offset + size);
            if (offset > bytes.Length) throw new InvalidDataException("A compiled callback instruction extends past its method body.");
        }
        return result;
    }
}
