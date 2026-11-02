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
/// literals (ldstr) and source document (portable PDB); every compiled XAML resource's element names and string
/// property values (BAML); and a call graph from the application's entry point. Reachability over-approximates
/// dispatch the usual way: a type touched by reachable code (constructed, called, its fields read or written, or
/// named as a generic argument) has all its virtual methods and its static constructor reachable, a delegate's target
/// (ldftn) is reachable, and a reachable reference to a XAML resource loads it, which constructs its class and every
/// type its elements name. A method nothing reaches stays unreachable, whatever literal it holds.
/// </summary>
internal sealed class LedgerTextProgram
{
    internal sealed record Method(int Id, string Assembly, string Key, string TopType, string Document, bool IsVirtual,
        bool IsConstructor, bool IsStaticConstructor, int Type);
    internal sealed record XamlValue(string ElementType, string ElementName, string Member, string Value);
    internal sealed record Baml(string Resource, string? OwnerType, List<XamlValue> Values, List<string> ElementTypes, string? Failure);
    internal sealed record Reach(int Parent, string Reason);

    internal readonly List<Method> Methods = new();
    internal readonly Dictionary<int, List<string>> Literals = new();
    internal readonly Dictionary<string, List<int>> MethodsByLiteral = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Baml> Bamls = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<int, Reach> Reached = new();
    internal int EntryMethod = -1;

    private readonly Dictionary<string, int> methodByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> typeByName = new(StringComparer.Ordinal);
    private readonly List<(string Name, List<int> Methods, int BaseType)> types = new();
    private readonly Dictionary<int, List<(int Target, string Reason)>> calls = new();
    private readonly Dictionary<int, HashSet<int>> touches = new();
    private readonly Dictionary<int, HashSet<string>> xamlReferences = new();
    private readonly Dictionary<string, string> ownerByResource = new(StringComparer.OrdinalIgnoreCase);
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
        foreach (TypeDefinitionHandle th in md.TypeDefinitions)
        {
            TypeDefinition t = md.GetTypeDefinition(th);
            string name = TypeName(md, th);
            int typeId = types.Count;
            typeByName.TryAdd(name, typeId);
            var list = new List<int>();
            types.Add((name, list, -1));
            string top = name.Split('/')[0];
            foreach (MethodDefinitionHandle mh in t.GetMethods())
            {
                MethodDefinition m = md.GetMethodDefinition(mh);
                string methodName = md.GetString(m.Name);
                string key = MethodKey(name, methodName, m.DecodeSignature(Signatures.Instance, null));
                string document = "";
                if (pdb != null)
                {
                    MethodDebugInformation debug = pdb.GetMethodDebugInformation(mh.ToDebugInformationHandle());
                    DocumentHandle dh = debug.Document;
                    if (dh.IsNil) dh = debug.GetSequencePoints().Select(p => p.Document).FirstOrDefault(d => !d.IsNil);
                    if (!dh.IsNil) document = Relative(pdb.GetString(pdb.GetDocument(dh).Name), sourceRoot);
                }
                int id = Methods.Count;
                Methods.Add(new Method(id, assembly, key, top, document, (m.Attributes & MethodAttributes.Virtual) != 0,
                    methodName == ".ctor", methodName == ".cctor", typeId));
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
                types[start + index] = (types[start + index].Name, types[start + index].Methods, baseId);
            index++;
        }
        return ids;
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
            var touched = new HashSet<int>();
            var literals = new List<string>();
            var xaml = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            calls[id] = targets; touches[id] = touched;
            Touch(touched, Methods[id].Type);
            if (m.RelativeVirtualAddress == 0) continue;
            byte[] il = pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes() ?? Array.Empty<byte>();
            bool connect = md.GetString(m.Name).EndsWith("Connect", StringComparison.Ordinal);
            for (int i = 0; i < il.Length;)
            {
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
                if (op.OperandType is OperandType.InlineString or OperandType.InlineMethod or OperandType.InlineField
                    or OperandType.InlineType or OperandType.InlineTok)
                {
                    int token = BitConverter.ToInt32(il, i);
                    if (op.OperandType == OperandType.InlineString)
                    {
                        string text = md.GetUserString(MetadataTokens.UserStringHandle(token & 0xFFFFFF));
                        literals.Add(text);
                        if (XamlResource(text) is string resource) xaml.Add(resource);
                    }
                    else Resolve(md, ids, MetadataTokens.EntityHandle(token), op, connect, targets, touched);
                }
                i += size;
            }
            Literals[id] = literals;
            foreach (string text in literals.Distinct(StringComparer.Ordinal))
            {
                if (!MethodsByLiteral.TryGetValue(text, out var holders)) MethodsByLiteral[text] = holders = new List<int>();
                holders.Add(id);
            }
            if (xaml.Count > 0) xamlReferences[id] = xaml;
        }
    }

    private void Resolve(MetadataReader md, Dictionary<MethodDefinitionHandle, int> ids, EntityHandle handle, OpCode op, bool connect,
        List<(int, string)> targets, HashSet<int> touched)
    {
        string reason = op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn ? (connect ? "xaml handler" : "delegate") : "call";
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            case HandleKind.MemberReference:
                if (MethodOf(md, ids, handle) is int target) { targets.Add((target, reason)); Touch(touched, Methods[target].Type); }
                else if (handle.Kind == HandleKind.MemberReference && TypeOf(md, md.GetMemberReference((MemberReferenceHandle)handle).Parent) is string parent)
                    TouchName(touched, parent);
                break;
            case HandleKind.MethodSpecification:
                MethodSpecification spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                if (MethodOf(md, ids, spec.Method) is int generic) { targets.Add((generic, reason)); Touch(touched, Methods[generic].Type); }
                foreach (string argument in spec.DecodeSignature(Signatures.Instance, null)) TouchName(touched, argument);
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

    private int? MethodOf(MetadataReader md, Dictionary<MethodDefinitionHandle, int> ids, EntityHandle handle)
    {
        string? key = null;
        if (handle.Kind == HandleKind.MethodDefinition) return ids[(MethodDefinitionHandle)handle];
        if (handle.Kind == HandleKind.MemberReference)
        {
            MemberReference r = md.GetMemberReference((MemberReferenceHandle)handle);
            if (r.GetKind() != MemberReferenceKind.Method || TypeOf(md, r.Parent) is not string parent) return null;
            key = MethodKey(parent, md.GetString(r.Name), r.DecodeMethodSignature(Signatures.Instance, null));
        }
        return key != null && methodByKey.TryGetValue(key, out int id) ? id : null;
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
                var stack = new Stack<(string Type, string Name, List<(string Member, string Value)> Values)>();
                var members = new Stack<string>();
                while (reader.Read())
                {
                    switch (reader.NodeType)
                    {
                        case XamlNodeType.StartObject:
                            string type = reader.Type?.UnderlyingType?.FullName ?? reader.Type?.Name ?? "";
                            elements.Add(type);
                            stack.Push((reader.Type?.Name ?? "", "", new List<(string, string)>()));
                            break;
                        case XamlNodeType.GetObject:
                            stack.Push(("", "", new List<(string, string)>()));
                            break;
                        case XamlNodeType.StartMember:
                            members.Push(reader.Member?.Name ?? "");
                            break;
                        case XamlNodeType.EndMember:
                            if (members.Count > 0) members.Pop();
                            break;
                        case XamlNodeType.Value when stack.Count > 0 && members.Count > 0 && reader.Value is string text:
                            var top = stack.Pop();
                            if (members.Peek() == "Name") top = (top.Type, text, top.Values);
                            else top.Values.Add((members.Peek(), text));
                            stack.Push(top);
                            break;
                        case XamlNodeType.EndObject when stack.Count > 0:
                            var done = stack.Pop();
                            if (done.Type.Length > 0)
                                foreach (var (member, text) in done.Values) values.Add(new XamlValue(done.Type, done.Name, member, text));
                            if (done.Type.Length > 0 && done.Name.Length > 0) values.Add(new XamlValue(done.Type, done.Name, "", ""));
                            break;
                    }
                }
            }
            catch (Exception ex) { failure = ex.GetType().Name + ": " + ex.Message; }
            ownerByResource.TryGetValue(name, out string? owner);
            Bamls[name] = new Baml(name, owner, values, elements, failure);
        }
    }

    private void Walk()
    {
        var queue = new Queue<int>();
        var typesDone = new HashSet<int>();
        var bamlsDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(int method, int parent, string reason)
        {
            if (method < 0 || Reached.ContainsKey(method)) return;
            Reached[method] = new Reach(parent, reason);
            queue.Enqueue(method);
        }
        void TypeTouched(int type, int by)
        {
            for (int t = type; t >= 0 && typesDone.Add(t); t = types[t].BaseType)
                foreach (int m in types[t].Methods)
                    if (Methods[m].IsVirtual) Add(m, by, "virtual of a touched type");
                    else if (Methods[m].IsStaticConstructor) Add(m, by, "static constructor");
        }
        void Load(string resource, int by)
        {
            if (!bamlsDone.Add(resource) || !Bamls.TryGetValue(resource, out Baml? baml)) return;
            foreach (string name in baml.ElementTypes.Prepend(baml.OwnerType ?? "").Where(n => n.Length > 0).Distinct(StringComparer.Ordinal))
                if (typeByName.TryGetValue(name, out int type))
                {
                    foreach (int m in types[type].Methods.Where(m => Methods[m].IsConstructor)) Add(m, by, "xaml screen");
                    TypeTouched(type, by);
                }
        }
        Add(EntryMethod, -1, "entry point");
        while (queue.Count > 0)
        {
            int method = queue.Dequeue();
            foreach (var (target, reason) in calls.GetValueOrDefault(method) ?? new List<(int, string)>()) Add(target, method, reason);
            foreach (int type in touches.GetValueOrDefault(method) ?? new HashSet<int>()) TypeTouched(type, method);
            foreach (string resource in xamlReferences.GetValueOrDefault(method) ?? new HashSet<string>()) Load(resource, method);
        }
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
    // The counterexamples of FQAssay's review of 330afa5: a type named but never constructed, a store overwritten in
    // the same call path, text held by a different member of the right class, and a format token.
    internal static string Entry() => Live() + typeof(LedgerTextNeverInstantiated).Name + LedgerTextDeadStore.Make()
        + LedgerTextLender.Alpha() + LedgerTextLender.Beta();
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
