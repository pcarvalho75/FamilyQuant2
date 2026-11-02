using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record SecondaryInjection(string Method, string IlSha256, int IlBytes, int ExceptionClauses,
        string[] Replacements);

    // Copy compiled instructions, branch offsets, locals and exception regions verbatim. Only
    // metadata tokens are relocated, plus explicitly named platform calls with identical stacks.
    // This never patches a loaded assembly, a product file, or a process-wide native function.
    private static DynamicMethod SecondaryInject(MethodInfo method, IReadOnlyDictionary<MethodBase, MethodInfo> replacements,
        List<SecondaryInjection> evidence)
    {
        var body = method.GetMethodBody() ?? throw new InvalidDataException("The injected method has no compiled body.");
        byte[] original = body.GetILAsByteArray()!, code = (byte[])original.Clone();
        var parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToList();
        if (!method.IsStatic) parameters.Insert(0, method.DeclaringType!);
        var copy = new DynamicMethod("secondary_" + method.Name, method.ReturnType, parameters.ToArray(), typeof(Program).Module, true)
            { InitLocals = body.InitLocals };
        var info = copy.GetDynamicILInfo();
        var locals = SignatureHelper.GetLocalVarSigHelper();
        foreach (var local in body.LocalVariables) locals.AddArgument(local.LocalType, local.IsPinned);
        info.SetLocalSignature(locals.GetSignature());
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(op => unchecked((ushort)op.Value));
        var used = new List<string>();
        Type[]? typeArgs = method.DeclaringType?.GetGenericArguments(), methodArgs = method.GetGenericArguments();
        int Token(MemberInfo member) => member switch
        {
            Type type => info.GetTokenFor(type.TypeHandle),
            DynamicMethod dynamic => info.GetTokenFor(dynamic),
            MethodBase call => info.GetTokenFor(call.MethodHandle, call.DeclaringType!.TypeHandle),
            FieldInfo field => info.GetTokenFor(field.FieldHandle, field.DeclaringType!.TypeHandle),
            _ => throw new InvalidDataException("Unsupported compiled token: " + member),
        };
        for (int offset = 0; offset < code.Length;)
        {
            int start = offset;
            ushort value = code[offset++];
            if (value == 0xfe) value = (ushort)(0xfe00 | code[offset++]);
            OpCode op = opcodes[value];
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(code, offset) * 4),
                _ => 4,
            };
            int? relocated = null;
            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                var member = method.Module.ResolveMember(BitConverter.ToInt32(code, offset), typeArgs, methodArgs)!;
                if (member is MethodBase call && replacements.TryGetValue(call, out var replacement))
                {
                    var expected = call.GetParameters().Select(parameter => parameter.ParameterType).ToList();
                    if (!call.IsStatic) expected.Insert(0, call.DeclaringType!);
                    if (!replacement.IsStatic || call is not MethodInfo called || called.ReturnType != replacement.ReturnType
                        || !expected.SequenceEqual(replacement.GetParameters().Select(parameter => parameter.ParameterType))
                        || op != OpCodes.Call && op != OpCodes.Callvirt)
                        throw new InvalidDataException("An injected boundary changes the evaluation stack: " + call);
                    if (op == OpCodes.Callvirt) code[start] = unchecked((byte)OpCodes.Call.Value);
                    used.Add(call.DeclaringType!.FullName + "." + call.Name + " -> " + replacement.Name);
                    member = replacement;
                }
                relocated = Token(member);
            }
            else if (op.OperandType == OperandType.InlineString) relocated = info.GetTokenFor(method.Module.ResolveString(BitConverter.ToInt32(code, offset)));
            else if (op.OperandType == OperandType.InlineSig) throw new InvalidDataException("Standalone signatures require a separately reviewed injector.");
            if (relocated is int token) BitConverter.GetBytes(token).CopyTo(code, offset);
            offset = checked(offset + size);
            if (offset > code.Length) throw new InvalidDataException("A compiled instruction extends beyond its body.");
        }
        if (used.Count != replacements.Count || replacements.Keys.Any(call => !used.Any(item => item.StartsWith(call.DeclaringType!.FullName + "." + call.Name + " -> ", StringComparison.Ordinal))))
            throw new InvalidDataException("Each injected platform boundary must occur exactly once in this compiled method.");
        if (body.ExceptionHandlingClauses.Count > 0)
        {
            byte[] regions = new byte[4 + 24 * body.ExceptionHandlingClauses.Count];
            regions[0] = 0x41; regions[1] = (byte)regions.Length; regions[2] = (byte)(regions.Length >> 8); regions[3] = (byte)(regions.Length >> 16);
            int cursor = 4;
            foreach (var clause in body.ExceptionHandlingClauses)
            {
                int data = clause.Flags == ExceptionHandlingClauseOptions.Filter ? clause.FilterOffset
                    : clause.Flags == ExceptionHandlingClauseOptions.Clause ? Token(clause.CatchType!) : 0;
                foreach (int part in new[] { (int)clause.Flags, clause.TryOffset, clause.TryLength, clause.HandlerOffset, clause.HandlerLength, data })
                { BitConverter.GetBytes(part).CopyTo(regions, cursor); cursor += 4; }
            }
            info.SetExceptions(regions);
        }
        info.SetCode(code, body.MaxStackSize);
        evidence.Add(new(method.DeclaringType!.FullName + "." + method.Name, Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),
            original.Length, body.ExceptionHandlingClauses.Count, used.ToArray()));
        return copy;
    }
}
