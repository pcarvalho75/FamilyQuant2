using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationStreamingCallerCommands() =>
    [new("--test-curation-streaming-callers", 21114, TestCommandPhase.Raw,
        args => args is ["--test-curation-streaming-callers"], (_, _) =>
        { CurationStreamingCallerTests(); return failed == 0 ? 0 : 1; })];

    private static void CurationStreamingCallerTests()
    {
        Section("Z12p5f4 - compiled curation callers must stream");
        // Portable PDB documents select executable methods, including lambdas and async MoveNext.
        // Decode IL operand widths and resolve method tokens, so comments, strings and aliases cannot
        // create false matches. Require symbols and every owned file rather than silently skipping it.
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FamilyListImportTransaction.cs"] = "Z12p5f1 FQHome",
            ["FamilyListImportViewModel.Commit.cs"] = "Z12p5f2a FQHome",
            ["CurationQuantitationViewModel.Operations.cs"] = "Z12p5f2b FQPolish",
            ["CurationQuantitationViewModel.PatternLab.cs"] = "Z12p5f2b FQPolish",
            ["CurationViewModel.Evidence.cs"] = "Z12p5f2c FQFix",
            ["MainWindow.ReportExport.cs"] = "Z12p5f2d FQFix",
            ["PanelEvidenceMatching.cs"] = "Z12p5f2e FQFetch",
            ["FamilyQuantFile.cs"] = "Z12p5f3 FQTips (save and open)",
            ["ShellProjectOperations.cs"] = "Z12p5f3 FQTips (shell open)"
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();
        foreach (var assembly in new[] { typeof(FamilyClusterFile).Assembly, typeof(MainWindow).Assembly }.Distinct())
        {
            string symbols = Path.ChangeExtension(assembly.Location, ".pdb");
            using var source = File.OpenRead(symbols);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(source);
            var pdb = provider.GetMetadataReader();
            foreach (var type in assembly.GetTypes())
            foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
            {
                var debug = pdb.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0x00ffffff));
                var points = debug.GetSequencePoints().Where(point => !point.IsHidden).ToArray();
                if (points.Length == 0) continue;
                foreach (var point in points)
                {
                    var document = point.Document.IsNil ? debug.Document : point.Document;
                    string path = pdb.GetString(pdb.GetDocument(document).Name).Replace('\\', '/');
                    if (owners.ContainsKey(Path.GetFileName(path))) seen.Add(Path.GetFileName(path));
                }
                foreach (var (offset, called) in CurationCallerInstructions(method))
                {
                    if (!CurationWholeTextCall(called)) continue;
                    var point = points.LastOrDefault(point => point.Offset <= offset);
                    if (point.StartLine == 0) point = points[0];
                    var document = point.Document.IsNil ? debug.Document : point.Document;
                    string path = pdb.GetString(pdb.GetDocument(document).Name).Replace('\\', '/');
                    if (!owners.TryGetValue(Path.GetFileName(path), out string? owner)) continue;
                    int project = path.IndexOf("/FamilyQuant.Core/", StringComparison.Ordinal);
                    if (project < 0) project = path.IndexOf("/FamilyQuant/", StringComparison.Ordinal);
                    if (project >= 0) path = path[(project + 1)..];
                    violations.Add($"{owner}: {path}:{point.StartLine} calls {called.DeclaringType!.FullName}.{called.Name} (IL_{offset:x4})");
                }
            }
        }
        foreach (var owner in owners)
            Check(owner.Value + " symbols cover " + owner.Key, seen.Contains(owner.Key));
        foreach (string violation in violations.OrderBy(value => value, StringComparer.Ordinal))
            Console.WriteLine("RED " + violation);
        Check("all eight curation paths, shell open and evidence matching avoid whole-document text/byte materialization", violations.Count == 0,
            string.Join(Environment.NewLine, violations));
        // A compiled planted call proves that instruction decoding actually detects the original defect.
        var control = typeof(Program).GetMethod(nameof(CurationCallerNegativeControl), BindingFlags.Static | BindingFlags.NonPublic)!;
        Check("compiled caller guard detects planted curation and evidence string calls without executing them",
            CurationCallerInstructions(control).Count(call => CurationWholeTextCall(call.Called)) == 2);
    }

    private static string[] CurationCallerNegativeControl(FamilyClusterFile file) => [file.ToJsonText(), JsonSerializer.Serialize(file.EvidenceSource)];

    private static bool CurationWholeTextCall(MethodBase method) =>
        method.DeclaringType == typeof(FamilyClusterFile) && method.Name == nameof(FamilyClusterFile.ToJsonText)
        || method.DeclaringType == typeof(File) && method.Name is nameof(File.ReadAllText) or nameof(File.ReadAllBytes)
        || (method.DeclaringType == typeof(StreamReader) || method.DeclaringType == typeof(TextReader))
            && method.Name == nameof(StreamReader.ReadToEnd)
        || method is MethodInfo serializer && serializer.DeclaringType == typeof(JsonSerializer)
            && serializer.Name == nameof(JsonSerializer.Serialize) && serializer.ReturnType == typeof(string)
            && serializer.GetGenericArguments().Contains(typeof(EvidenceProvenance));

    private static IEnumerable<(int Offset, MethodBase Called)> CurationCallerInstructions(MethodBase method)
    {
        var opcodes = CurationCallerOpcodes;
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
        for (int position = 0; position < bytes.Length;)
        {
            int offset = position;
            ushort value = bytes[position++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[position++]);
            var code = opcodes[value];
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(bytes, position) * 4),
                _ => 4
            };
            if ((code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Ldftn || code == OpCodes.Ldvirtftn)
                && method.Module.ResolveMethod(BitConverter.ToInt32(bytes, position), method.DeclaringType?.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : null) is { } called)
                yield return (offset, called);
            position = checked(position + size);
            if (position > bytes.Length) throw new InvalidDataException("A caller instruction extends beyond its compiled method.");
        }
    }

    private static readonly Dictionary<ushort, OpCode> CurationCallerOpcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => unchecked((ushort)code.Value));
}
