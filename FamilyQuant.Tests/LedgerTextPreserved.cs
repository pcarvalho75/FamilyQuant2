using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Text;
using System.Xaml;

namespace FamilyQuant.Tests;

/// <summary>
/// N6a: what the compiled 2.0 program holds, read from its assemblies rather than its source. Every method's string
/// literals (ldstr), the member each literal is stored into, and its source document (portable PDB); every compiled
/// XAML resource's elements, names, string property values and visibility (BAML); and a call graph from the
/// application's entry point. A method is reachable through a call or a delegate (ldftn) from reachable code. A
/// virtual method is reachable only on a type that is really constructed (a reachable constructor, a XAML load that
/// creates it, or a value type passed as a generic argument), and then only when the framework can call that slot
/// (an override of a method outside these assemblies, or an explicit interface implementation) or reachable code
/// calls a method of that name. Naming or touching a type reaches only its static constructor. A reachable reference
/// to a XAML resource loads it, which constructs its class and every type its elements name.
/// </summary>
internal sealed class LedgerTextProgram
{
    internal sealed record Method(int Id, string Assembly, string Key, string TopType, string Document, bool IsVirtual,
        bool IsConstructor, bool IsStaticConstructor, int Type, string Name, int Parameters, bool IsNewSlot, bool FrameworkSlot,
        string SourceType, string SourceMember);
    /// <summary>One string property of a compiled XAML element. <see cref="Ancestors"/> are the names of the elements around it,
    /// nearest first; <see cref="VisibilityInMarkup"/> is true when it or one of them sets Visibility to anything but Visible.</summary>
    internal sealed record XamlValue(string ElementType, string ElementName, string Member, string Value, IReadOnlyList<string> Ancestors,
        bool VisibilityInMarkup);
    internal sealed record Baml(string Resource, string? OwnerType, List<XamlValue> Values, List<string> ElementTypes, string? Failure);
    internal sealed record Reach(int Parent, string Reason);
    /// <summary>A literal as the method uses it: its IL offset and the member it is stored into right away, if any.</summary>
    internal sealed record LiteralUse(int Offset, string Text, string? StoredInto);

    internal readonly List<Method> Methods = new();
    internal readonly Dictionary<int, List<string>> Literals = new();
    internal readonly Dictionary<int, List<LiteralUse>> LiteralUses = new();
    internal readonly Dictionary<string, List<int>> MethodsByLiteral = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Baml> Bamls = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<int, Reach> Reached = new();
    /// <summary>Fields read by a method that sets (or binds) a Visibility: an element of that name may be hidden at run time.</summary>
    internal readonly HashSet<string> VisibilityFields = new(StringComparer.Ordinal);
    internal int EntryMethod = -1;

    private readonly Dictionary<string, int> methodByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> typeByName = new(StringComparer.Ordinal);
    private readonly List<(string Name, List<int> Methods, int BaseType, bool IsValueType)> types = new();
    private readonly Dictionary<int, List<(int Target, string Reason)>> calls = new();
    private readonly Dictionary<int, HashSet<string>> calledNames = new();
    private readonly Dictionary<int, HashSet<int>> touches = new();
    private readonly Dictionary<int, HashSet<int>> genericValueTypes = new();
    private readonly Dictionary<int, List<(int Offset, string Target)>> stores = new();
    private readonly Dictionary<int, List<(int Offset, int Callee)>> callSites = new();
    private readonly Dictionary<int, (HashSet<string> Fields, bool SetsVisibility)> visibilityUse = new();
    private readonly Dictionary<int, HashSet<string>> xamlReferences = new();
    private readonly Dictionary<string, string> ownerByResource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, HashSet<string>> storesReached = new();
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>Reads the assemblies (with their portable PDBs beside them) and builds the graph from the first one's entry point,
    /// or from <paramref name="entryKey"/> when it is given.</summary>
    internal static LedgerTextProgram Read(IReadOnlyList<string> assemblyPaths, string sourceRoot, Assembly? xamlAssembly, string? entryKey = null)
    {
        var program = new LedgerTextProgram();
        var readers = new List<(string Path, PEReader Pe, MetadataReader Md, MetadataReader? Pdb)>();
        try
        {
            foreach (string path in assemblyPaths)
            {
                var pe = new PEReader(File.OpenRead(path));
                string pdbPath = Path.ChangeExtension(path, ".pdb");
                MetadataReader? pdb = File.Exists(pdbPath) ? MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath)).GetMetadataReader() : null;
                readers.Add((path, pe, pe.GetMetadataReader(), pdb));
            }
            var ids = readers.Select(r => program.Declare(r.Md, r.Pdb, Path.GetFileName(r.Path), sourceRoot)).ToList();
            for (int a = 0; a < readers.Count; a++) program.ReadBodies(readers[a].Pe, readers[a].Md, ids[a]);
            if (entryKey != null) program.EntryMethod = program.methodByKey.TryGetValue(entryKey, out int id) ? id : -1;
            else
            {
                int token = readers[0].Pe.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress;
                program.EntryMethod = token == 0 ? -1 : ids[0][(MethodDefinitionHandle)MetadataTokens.EntityHandle(token)];
            }
        }
        finally { foreach (var r in readers) r.Pe.Dispose(); }
        if (xamlAssembly != null) program.ReadXaml(xamlAssembly);
        if (program.EntryMethod >= 0) program.Walk();
        return program;
    }

    private Dictionary<MethodDefinitionHandle, int> Declare(MetadataReader md, MetadataReader? pdb, string assembly, string sourceRoot)
    {
        var ids = new Dictionary<MethodDefinitionHandle, int>();
        // Methods that implement a declaration outside these assemblies through an explicit MethodImpl (an explicit interface
        // implementation, a compiler state machine's MoveNext): the framework can call them.
        var external = new HashSet<MethodDefinitionHandle>();
        foreach (TypeDefinitionHandle th in md.TypeDefinitions)
            foreach (MethodImplementationHandle ih in md.GetTypeDefinition(th).GetMethodImplementations())
            {
                MethodImplementation impl = md.GetMethodImplementation(ih);
                if (impl.MethodBody.Kind == HandleKind.MethodDefinition && impl.MethodDeclaration.Kind == HandleKind.MemberReference)
                    external.Add((MethodDefinitionHandle)impl.MethodBody);
            }
        foreach (TypeDefinitionHandle th in md.TypeDefinitions)
        {
            TypeDefinition t = md.GetTypeDefinition(th);
            string name = TypeName(md, th);
            int typeId = types.Count;
            typeByName.TryAdd(name, typeId);
            var list = new List<int>();
            string? baseName = t.BaseType.IsNil ? null : TypeOf(md, t.BaseType);
            types.Add((name, list, -1, baseName is "System.ValueType" or "System.Enum"));
            string top = name.Split('/')[0];
            foreach (MethodDefinitionHandle mh in t.GetMethods())
            {
                MethodDefinition m = md.GetMethodDefinition(mh);
                string methodName = md.GetString(m.Name);
                MethodSignature<string> signature = m.DecodeSignature(Signatures.Instance, null);
                string key = MethodKey(name, methodName, signature);
                string document = "";
                if (pdb != null)
                {
                    MethodDebugInformation debug = pdb.GetMethodDebugInformation(mh.ToDebugInformationHandle());
                    DocumentHandle dh = debug.Document;
                    if (dh.IsNil) dh = debug.GetSequencePoints().Select(p => p.Document).FirstOrDefault(d => !d.IsNil);
                    if (!dh.IsNil) document = Relative(pdb.GetString(pdb.GetDocument(dh).Name), sourceRoot);
                }
                var (sourceType, sourceMember) = SourceOf(name, methodName);
                int id = Methods.Count;
                Methods.Add(new Method(id, assembly, key, top, document, (m.Attributes & MethodAttributes.Virtual) != 0,
                    methodName == ".ctor", methodName == ".cctor", typeId, methodName, signature.ParameterTypes.Length,
                    (m.Attributes & MethodAttributes.NewSlot) != 0, external.Contains(mh) || methodName.Contains('.', StringComparison.Ordinal) && methodName[0] != '.',
                    sourceType, sourceMember));
                methodByKey.TryAdd(key, id);
                ids[mh] = id;
                list.Add(id);
            }
        }
        // Base types, resolved by name across the assemblies read.
        int start = types.Count - md.TypeDefinitions.Count;
        int index = 0;
        foreach (TypeDefinitionHandle th in md.TypeDefinitions)
        {
            EntityHandle b = md.GetTypeDefinition(th).BaseType;
            string? baseName = b.IsNil ? null : TypeOf(md, b);
            if (baseName != null && typeByName.TryGetValue(baseName, out int baseId))
                types[start + index] = (types[start + index].Name, types[start + index].Methods, baseId, types[start + index].IsValueType);
            index++;
        }
        return ids;
    }

    /// <summary>The source type and member a compiled method comes from: a state machine, closure or lambda belongs to the member
    /// it was written in, an accessor to its property or event.</summary>
    internal static (string Type, string Member) SourceOf(string typeName, string methodName)
    {
        string[] segments = typeName.Split('/');
        string type = string.Join("/", segments.Where(s => !s.StartsWith("<", StringComparison.Ordinal)));
        string member = methodName;
        string? machine = segments.LastOrDefault(s => s.StartsWith("<", StringComparison.Ordinal) && !s.StartsWith("<>", StringComparison.Ordinal));
        if (machine != null) member = machine[1..machine.IndexOf('>')];
        if (member.StartsWith("<", StringComparison.Ordinal) && member.IndexOf('>') > 1) member = member[1..member.IndexOf('>')];
        foreach (string prefix in new[] { "get_", "set_", "add_", "remove_" })
            if (member.StartsWith(prefix, StringComparison.Ordinal)) { member = member[prefix.Length..]; break; }
        int dot = type.LastIndexOfAny(new[] { '.', '/' });
        return (dot >= 0 ? type[(dot + 1)..] : type, member);
    }

    private static string Relative(string document, string sourceRoot)
    {
        string path = document.Replace('\\', '/');
        string root = Path.GetFullPath(sourceRoot).Replace('\\', '/').TrimEnd('/') + "/";
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return path[root.Length..];
        return path.StartsWith("/_/", StringComparison.Ordinal) ? path[3..] : path;
    }

    private void ReadBodies(PEReader pe, MetadataReader md, Dictionary<MethodDefinitionHandle, int> ids)
    {
        foreach (MethodDefinitionHandle mh in md.MethodDefinitions)
        {
            int id = ids[mh];
            MethodDefinition m = md.GetMethodDefinition(mh);
            var targets = new List<(int, string)>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var touched = new HashSet<int>();
            var valueTypes = new HashSet<int>();
            var literals = new List<string>();
            var uses = new List<LiteralUse>();
            var stored = new List<(int, string)>();
            var sites = new List<(int, int)>();
            var fields = new HashSet<string>(StringComparer.Ordinal);
            bool setsVisibility = false;
            var xaml = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            calls[id] = targets; calledNames[id] = names; touches[id] = touched; genericValueTypes[id] = valueTypes;
            stores[id] = stored; callSites[id] = sites;
            if (m.RelativeVirtualAddress == 0) continue;
            byte[] il = pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes() ?? Array.Empty<byte>();
            bool connect = md.GetString(m.Name).EndsWith("Connect", StringComparison.Ordinal);
            (int Offset, string Text)? pending = null;
            for (int i = 0; i < il.Length;)
            {
                int offset = i;
                short value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
                i += il[i] == 0xFE ? 2 : 1;
                if (!OpCodesByValue.TryGetValue(value, out OpCode op)) break;
                int size = op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                    _ => 4,
                };
                string? storedHere = null;
                if (op.OperandType is OperandType.InlineString or OperandType.InlineMethod or OperandType.InlineField
                    or OperandType.InlineType or OperandType.InlineTok)
                {
                    int token = BitConverter.ToInt32(il, i);
                    EntityHandle handle = op.OperandType == OperandType.InlineString ? default : MetadataTokens.EntityHandle(token);
                    if (op.OperandType == OperandType.InlineString)
                    {
                        string text = md.GetUserString(MetadataTokens.UserStringHandle(token & 0xFFFFFF));
                        literals.Add(text);
                        if (XamlResource(text) is string resource) xaml.Add(resource);
                    }
                    else
                    {
                        Resolve(md, ids, handle, op, connect, targets, names, touched, valueTypes);
                        string? member = MemberName(md, handle);
                        if (op.OperandType == OperandType.InlineField && member != null)
                        {
                            if (op == OpCodes.Stfld || op == OpCodes.Stsfld) storedHere = member;
                            if (op == OpCodes.Ldfld) fields.Add(member.Split("::")[1]);
                            if (op == OpCodes.Ldsfld && member.EndsWith("::VisibilityProperty", StringComparison.Ordinal)) setsVisibility = true;
                        }
                        else if (op.OperandType == OperandType.InlineMethod && member != null)
                        {
                            if (member.EndsWith("::set_Visibility", StringComparison.Ordinal)) setsVisibility = true;
                            int setter = member.IndexOf("::set_", StringComparison.Ordinal);
                            if (setter >= 0) storedHere = member[..setter] + "::" + member[(setter + 6)..];
                            if (MethodOf(md, ids, handle) is int callee) sites.Add((offset, callee));
                        }
                    }
                }
                if (storedHere != null) stored.Add((offset, storedHere));
                // A literal is stored into a member when the very next instruction stores it.
                if (pending is { } p) uses.Add(new LiteralUse(p.Offset, p.Text, storedHere));
                pending = op.OperandType == OperandType.InlineString ? (offset, literals[^1]) : null;
                i += size;
            }
            if (pending is { } last) uses.Add(new LiteralUse(last.Offset, last.Text, null));
            Literals[id] = literals;
            LiteralUses[id] = uses;
            visibilityUse[id] = (fields, setsVisibility);
            foreach (string text in literals.Distinct(StringComparer.Ordinal))
            {
                if (!MethodsByLiteral.TryGetValue(text, out var holders)) MethodsByLiteral[text] = holders = new List<int>();
                holders.Add(id);
            }
            if (xaml.Count > 0) xamlReferences[id] = xaml;
        }
    }

    /// <summary>The member a field or method token names, as "Type::Name", with a property's backing field read as the property.</summary>
    private static string? MemberName(MetadataReader md, EntityHandle handle)
    {
        string? type = null, name = null;
        switch (handle.Kind)
        {
            case HandleKind.FieldDefinition:
                FieldDefinition f = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                type = TypeName(md, f.GetDeclaringType()); name = md.GetString(f.Name); break;
            case HandleKind.MethodDefinition:
                MethodDefinition m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                type = TypeName(md, m.GetDeclaringType()); name = md.GetString(m.Name); break;
            case HandleKind.MemberReference:
                MemberReference r = md.GetMemberReference((MemberReferenceHandle)handle);
                type = TypeOf(md, r.Parent); name = md.GetString(r.Name); break;
            case HandleKind.MethodSpecification:
                return MemberName(md, md.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
        }
        if (type == null || name == null) return null;
        if (name.StartsWith("<", StringComparison.Ordinal) && name.EndsWith(">k__BackingField", StringComparison.Ordinal)) name = name[1..name.IndexOf('>')];
        return type + "::" + name;
    }

    private void Resolve(MetadataReader md, Dictionary<MethodDefinitionHandle, int> ids, EntityHandle handle, OpCode op, bool connect,
        List<(int, string)> targets, HashSet<string> names, HashSet<int> touched, HashSet<int> valueTypes)
    {
        string reason = op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn ? (connect ? "xaml handler" : "delegate") : "call";
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            case HandleKind.MemberReference:
                if (CalledName(md, handle) is string called) names.Add(called);
                if (MethodOf(md, ids, handle) is int target) { targets.Add((target, reason)); Touch(touched, Methods[target].Type); }
                else if (handle.Kind == HandleKind.MemberReference && TypeOf(md, md.GetMemberReference((MemberReferenceHandle)handle).Parent) is string parent)
                    TouchName(touched, parent);
                break;
            case HandleKind.MethodSpecification:
                MethodSpecification spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                if (CalledName(md, spec.Method) is string genericName) names.Add(genericName);
                if (MethodOf(md, ids, spec.Method) is int generic) { targets.Add((generic, reason)); Touch(touched, Methods[generic].Type); }
                foreach (string argument in spec.DecodeSignature(Signatures.Instance, null))
                {
                    TouchName(touched, argument);
                    // A value type needs no constructor to exist: passed as a generic argument (a builder's Start<T>), it is an instance.
                    if (typeByName.TryGetValue(Generic(argument), out int t) && types[t].IsValueType) valueTypes.Add(t);
                }
                break;
            case HandleKind.FieldDefinition:
                TouchName(touched, TypeName(md, md.GetFieldDefinition((FieldDefinitionHandle)handle).GetDeclaringType()));
                break;
            case HandleKind.TypeDefinition:
            case HandleKind.TypeReference:
            case HandleKind.TypeSpecification:
                if (TypeOf(md, handle) is string type) TouchName(touched, type);
                break;
        }
    }

    /// <summary>A called method's name and parameter count, inside or outside these assemblies: what name-based dispatch matches.</summary>
    private static string? CalledName(MetadataReader md, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
            return md.GetString(m.Name) + "/" + m.DecodeSignature(Signatures.Instance, null).ParameterTypes.Length;
        }
        if (handle.Kind == HandleKind.MemberReference)
        {
            MemberReference r = md.GetMemberReference((MemberReferenceHandle)handle);
            return r.GetKind() == MemberReferenceKind.Method ? md.GetString(r.Name) + "/" + r.DecodeMethodSignature(Signatures.Instance, null).ParameterTypes.Length : null;
        }
        return null;
    }

    private int? MethodOf(MetadataReader md, Dictionary<MethodDefinitionHandle, int> ids, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MethodDefinition) return ids[(MethodDefinitionHandle)handle];
        if (handle.Kind != HandleKind.MemberReference) return null;
        MemberReference r = md.GetMemberReference((MemberReferenceHandle)handle);
        if (r.GetKind() != MemberReferenceKind.Method || TypeOf(md, r.Parent) is not string parent) return null;
        string key = MethodKey(parent, md.GetString(r.Name), r.DecodeMethodSignature(Signatures.Instance, null));
        return methodByKey.TryGetValue(key, out int id) ? id : null;
    }

    private void Touch(HashSet<int> touched, int type) { if (type >= 0) touched.Add(type); }
    private void TouchName(HashSet<int> touched, string type)
    {
        string name = Generic(type);
        if (typeByName.TryGetValue(name, out int id)) touched.Add(id);
        int open = type.IndexOf('{');
        if (open >= 0)
            foreach (string inner in SplitArguments(type[(open + 1)..type.LastIndexOf('}')])) TouchName(touched, inner);
    }
    private static string Generic(string type)
    {
        // Instantiations are written with braces: compiler-generated names hold angle brackets.
        string name = type;
        int open = name.IndexOf('{');
        if (open >= 0) name = name[..open];
        return name.TrimEnd('&', '*', ']', '[', ',');
    }
    private static IEnumerable<string> SplitArguments(string text)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
            else if (text[i] == ',' && depth == 0) { yield return text[start..i]; start = i + 1; }
        }
        if (start < text.Length) yield return text[start..];
    }

    private static string? TypeOf(MetadataReader md, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeName(md, (TypeDefinitionHandle)handle),
        HandleKind.TypeReference => TypeName(md, (TypeReferenceHandle)handle),
        HandleKind.TypeSpecification => Generic(md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(Signatures.Instance, null)),
        _ => null,
    };
    internal static string TypeName(MetadataReader md, TypeDefinitionHandle handle)
    {
        TypeDefinition t = md.GetTypeDefinition(handle);
        TypeDefinitionHandle outer = t.GetDeclaringType();
        if (!outer.IsNil) return TypeName(md, outer) + "/" + md.GetString(t.Name);
        string ns = md.GetString(t.Namespace);
        return ns.Length == 0 ? md.GetString(t.Name) : ns + "." + md.GetString(t.Name);
    }
    internal static string TypeName(MetadataReader md, TypeReferenceHandle handle)
    {
        TypeReference t = md.GetTypeReference(handle);
        if (t.ResolutionScope.Kind == HandleKind.TypeReference) return TypeName(md, (TypeReferenceHandle)t.ResolutionScope) + "/" + md.GetString(t.Name);
        string ns = md.GetString(t.Namespace);
        return ns.Length == 0 ? md.GetString(t.Name) : ns + "." + md.GetString(t.Name);
    }
    private static string MethodKey(string type, string name, MethodSignature<string> signature) =>
        type + "::" + name + (signature.GenericParameterCount > 0 ? "``" + signature.GenericParameterCount : "") + "(" + string.Join(",", signature.ParameterTypes) + ")";

    /// <summary>The compiled resource a literal names, if it names a XAML file: "/X;component/a/b.xaml" or "b.xaml" become "a/b.baml".</summary>
    internal static string? XamlResource(string text)
    {
        if (!text.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) return null;
        string path = text.Replace('\\', '/');
        int component = path.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
        if (component >= 0) path = path[(component + ";component/".Length)..];
        else if (path.Contains(":", StringComparison.Ordinal)) return null;
        return path.TrimStart('/').ToLowerInvariant()[..^5] + ".baml";
    }

    private void ReadXaml(Assembly assembly)
    {
        // The class of each resource: the type whose InitializeComponent names it.
        foreach (var (id, references) in xamlReferences)
            if (Methods[id].Key.Contains("::InitializeComponent(", StringComparison.Ordinal))
                foreach (string resource in references) ownerByResource.TryAdd(resource, types[Methods[id].Type].Name);
        using Stream? stream = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources");
        if (stream == null) return;
        using var resources = new ResourceReader(stream);
        var entries = new List<(string Name, byte[] Bytes)>();
        var e = resources.GetEnumerator();
        while (e.MoveNext())
        {
            string name = (string)e.Key;
            if (!name.EndsWith(".baml", StringComparison.OrdinalIgnoreCase) || e.Value is not Stream s) continue;
            using var copy = new MemoryStream();
            s.CopyTo(copy);
            entries.Add((name, copy.ToArray()));
        }
        foreach (var (name, bytes) in entries.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var values = new List<XamlValue>();
            var elements = new List<string>();
            string? failure = null;
            try
            {
                using var reader = new System.Windows.Baml2006.Baml2006Reader(new MemoryStream(bytes), new XamlReaderSettings { LocalAssembly = assembly });
                var stack = new Stack<XamlFrame>();
                var members = new Stack<string>();
                bool visibilityPending = false;
                while (reader.Read())
                {
                    if (visibilityPending && reader.NodeType is XamlNodeType.Value or XamlNodeType.StartObject)
                    {
                        // Visibility set to anything but a literal Visible (Collapsed, Hidden, a binding or resource) may hide it.
                        if (!(reader.NodeType == XamlNodeType.Value && reader.Value is string v && v == "Visible") && stack.Count > 0)
                            stack.Peek().HidesItself = true;
                        visibilityPending = false;
                    }
                    switch (reader.NodeType)
                    {
                        case XamlNodeType.StartObject:
                            elements.Add(reader.Type?.UnderlyingType?.FullName ?? reader.Type?.Name ?? "");
                            stack.Push(new XamlFrame(reader.Type?.Name ?? ""));
                            break;
                        case XamlNodeType.GetObject:
                            stack.Push(new XamlFrame(""));
                            break;
                        case XamlNodeType.StartMember:
                            members.Push(reader.Member?.Name ?? "");
                            if (members.Peek() == "Visibility") visibilityPending = true;
                            break;
                        case XamlNodeType.EndMember:
                            if (members.Count > 0) members.Pop();
                            break;
                        case XamlNodeType.Value when stack.Count > 0 && members.Count > 0 && reader.Value is string text:
                            if (members.Peek() == "Name") stack.Peek().Name = text;
                            else stack.Peek().Values.Add((members.Peek(), text));
                            break;
                        case XamlNodeType.EndObject when stack.Count > 0:
                            XamlFrame done = stack.Pop();
                            if (done.Type.Length == 0) break;
                            var around = stack.Where(f => f.Type.Length > 0).ToList();
                            var ancestors = around.Where(f => f.Name.Length > 0).Select(f => f.Name).ToList();
                            bool hidden = done.HidesItself || around.Any(f => f.HidesItself);
                            foreach (var (member, text) in done.Values) values.Add(new XamlValue(done.Type, done.Name, member, text, ancestors, hidden));
                            if (done.Name.Length > 0) values.Add(new XamlValue(done.Type, done.Name, "", "", ancestors, hidden));
                            break;
                    }
                }
            }
            catch (Exception ex) { failure = ex.GetType().Name + ": " + ex.Message; }
            ownerByResource.TryGetValue(name, out string? owner);
            Bamls[name] = new Baml(name, owner, values, elements, failure);
        }
    }

    private sealed class XamlFrame
    {
        internal XamlFrame(string type) { Type = type; }
        internal string Type { get; }
        internal string Name { get; set; } = "";
        internal bool HidesItself { get; set; }
        internal List<(string Member, string Value)> Values { get; } = new();
    }

    private void Walk()
    {
        var queue = new Queue<int>();
        var touchedDone = new HashSet<int>();
        var instantiated = new HashSet<int>();
        var bamlsDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var namesCalled = new HashSet<string>(StringComparer.Ordinal);
        var waiting = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        void Add(int method, int parent, string reason)
        {
            if (method < 0 || Reached.ContainsKey(method)) return;
            Reached[method] = new Reach(parent, reason);
            queue.Enqueue(method);
        }
        bool DeclaredInBase(int type, string name, int parameters)
        {
            for (int t = types[type].BaseType; t >= 0; t = types[t].BaseType)
                if (types[t].Methods.Any(m => Methods[m].IsVirtual && Methods[m].Name == name && Methods[m].Parameters == parameters)) return true;
            return false;
        }
        void Instantiate(int type, int by)
        {
            for (int t = type; t >= 0 && instantiated.Add(t); t = types[t].BaseType)
                foreach (int m in types[t].Methods.Where(m => Methods[m].IsVirtual))
                {
                    Method v = Methods[m];
                    if (v.FrameworkSlot) Add(m, by, "framework slot of a constructed type");
                    else if (!v.IsNewSlot && !DeclaredInBase(t, v.Name, v.Parameters)) Add(m, by, "framework override of a constructed type");
                    else if (namesCalled.Contains(v.Name + "/" + v.Parameters)) Add(m, by, "virtual call by name on a constructed type");
                    else
                    {
                        string key = v.Name + "/" + v.Parameters;
                        if (!waiting.TryGetValue(key, out var list)) waiting[key] = list = new List<int>();
                        list.Add(m);
                    }
                }
        }
        void Touched(int type, int by)
        {
            if (!touchedDone.Add(type)) return;
            foreach (int m in types[type].Methods.Where(m => Methods[m].IsStaticConstructor)) Add(m, by, "static constructor");
        }
        void Load(string resource, int by)
        {
            if (!bamlsDone.Add(resource) || !Bamls.TryGetValue(resource, out Baml? baml)) return;
            foreach (string name in baml.ElementTypes.Prepend(baml.OwnerType ?? "").Where(n => n.Length > 0).Distinct(StringComparer.Ordinal))
                if (typeByName.TryGetValue(name, out int type))
                    foreach (int m in types[type].Methods.Where(m => Methods[m].IsConstructor && Methods[m].Parameters == 0)) Add(m, by, "xaml screen");
        }
        Add(EntryMethod, -1, "entry point");
        while (queue.Count > 0)
        {
            int method = queue.Dequeue();
            if (Methods[method].IsConstructor) Instantiate(Methods[method].Type, method);
            foreach (var (target, reason) in calls.GetValueOrDefault(method) ?? new List<(int, string)>()) Add(target, method, reason);
            foreach (string called in calledNames.GetValueOrDefault(method) ?? new HashSet<string>())
                if (namesCalled.Add(called) && waiting.Remove(called, out var released))
                    foreach (int m in released) Add(m, method, "virtual call by name on a constructed type");
            foreach (int type in touches.GetValueOrDefault(method) ?? new HashSet<int>()) Touched(type, method);
            foreach (int type in genericValueTypes.GetValueOrDefault(method) ?? new HashSet<int>()) Instantiate(type, method);
            foreach (string resource in xamlReferences.GetValueOrDefault(method) ?? new HashSet<string>()) Load(resource, method);
        }
        // Any method that sets a visibility counts, reached or not: the shell can call a setter through reflection, which no
        // call graph sees, so an element whose host may be hidden stays unproved.
        foreach (var use in visibilityUse.Values)
            if (use.SetsVisibility) VisibilityFields.UnionWith(use.Fields);
    }

    /// <summary>Whether the member a literal is stored into is stored again later in the same method, directly or through a
    /// method it calls afterwards: then the literal is a dead store, not what the screen shows.</summary>
    internal bool Overwritten(int method, LiteralUse use)
    {
        if (use.StoredInto == null) return false;
        if (stores[method].Any(s => s.Offset > use.Offset && s.Target == use.StoredInto)) return true;
        return callSites[method].Any(c => c.Offset > use.Offset && StoresOf(c.Callee, new HashSet<int>()).Contains(use.StoredInto));
    }

    private HashSet<string> StoresOf(int method, HashSet<int> visiting)
    {
        if (storesReached.TryGetValue(method, out var known)) return known;
        var all = new HashSet<string>(stores[method].Select(s => s.Target), StringComparer.Ordinal);
        if (visiting.Add(method))
            foreach (var (_, callee) in callSites[method]) all.UnionWith(StoresOf(callee, visiting));
        storesReached[method] = all;
        return all;
    }

    /// <summary>The nearest screen, handler or delegate on the path from the entry point, and the number of steps from it.</summary>
    internal (string Entry, int Steps) EntryOf(int method)
    {
        int steps = 0;
        for (int m = method; m >= 0; m = Reached[m].Parent, steps++)
            if (Reached[m].Reason is "xaml handler" or "xaml screen" or "delegate" or "entry point")
                return (Reached[m].Reason + " " + Methods[m].Key, steps);
        return ("entry point", steps);
    }

    /// <summary>Method signatures as plain text, alike in every assembly, so a reference finds its definition.</summary>
    private sealed class Signatures : ISignatureTypeProvider<string, object?>
    {
        internal static readonly Signatures Instance = new();
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "{" + string.Join(",", typeArguments) + "}";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => TypeName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => TypeName(reader, handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}

/// <summary>N6a's negative and positive controls: one literal reachable from the control entry, one only in dead code.</summary>
internal static class LedgerTextControl
{
    internal const string LiveLiteral = "N6a control: this literal is reachable from the control entry";
    internal const string DeadLiteral = "N6a control: this literal is only in dead code";
    internal const string NeverInstantiatedLiteral = "N6a control: a virtual method of a class nothing instantiates";
    internal const string OverwrittenLiteral = "N6a control: an initial caption overwritten before use";
    internal const string OwnMemberLiteral = "N6a control: a sentence the Alpha member holds";
    internal const string LentLiteral = "N6a control: a sentence another member holds";
    internal const string ShortLiteral = "N0";
    internal const string FieldPathLiteral = "Source.DisplayName";
    internal const string FormatOperandLiteral = "0.0 'counts per second'";
    internal const string AttachedPathLiteral = "(Grid.Column)";
    // The counterexamples of FQAssay's review of 330afa5: a type named but never constructed, a store overwritten in
    // the same call path, text held by a different member of the right class, and a format token.
    internal static string Entry() => Live() + typeof(LedgerTextNeverInstantiated).Name + LedgerTextDeadStore.Make()
        + LedgerTextLender.Alpha() + LedgerTextLender.Beta() + LedgerTextOperands.Display(1.25, 3.5);
    private static string Live() => string.Concat("N6a control: this literal is reachable ", "from the control entry") + LiveText();
    private static string LiveText() => "N6a control: this literal is reachable from the control entry";
    internal static string Dead() => "N6a control: this literal is only in dead code";
}

/// <summary>N6a control: named by the control entry, never constructed; its virtual method must stay unreachable.</summary>
internal class LedgerTextNeverInstantiated
{
    private LedgerTextNeverInstantiated() { }
    public virtual string Text() => "N6a control: a virtual method of a class nothing instantiates";
}

/// <summary>N6a control: the initial caption is overwritten by the constructor before anything reads it.</summary>
internal sealed class LedgerTextDeadStore
{
    internal string Caption { get; private set; } = "N6a control: an initial caption overwritten before use";
    private LedgerTextDeadStore() { Reset(); }
    private void Reset() { Caption = "N6a control: the caption the screen shows"; }
    internal static string Make() => new LedgerTextDeadStore().Caption;
}

/// <summary>N6a control: Alpha holds its own sentence and a format token; Beta holds a sentence of its own that must
/// not close a row whose frozen member is Alpha.</summary>
internal static class LedgerTextLender
{
    internal static string Alpha() => "N6a control: a sentence the Alpha member holds" + 12.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    internal static string Beta() => "N6a control: a sentence another member holds";
}

/// <summary>N6a control (FQAssay's review of b3f5540): one member binds a changed path and keeps the old dotted path only as a
/// caption, and formats its original quantity with N1 while another quantity keeps a numeric format with a quoted unit. Neither
/// the dotted path nor the format operand is prose, so neither may close a row by text.</summary>
internal static class LedgerTextOperands
{
    private static string Caption(string text) => text;
    internal static string Display(double original, double other) =>
        new System.Windows.Data.Binding("Source.ChangedValue").Path.Path + Caption("Source.DisplayName") + Caption("(Grid.Column)")
        + original.ToString("N1", System.Globalization.CultureInfo.InvariantCulture)
        + other.ToString("0.0 'counts per second'", System.Globalization.CultureInfo.InvariantCulture);
}
