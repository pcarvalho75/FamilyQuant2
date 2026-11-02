using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string TextStep = "N6a-text";
    // Anything that can change a number or a file stays on strict checks (M61).
    private static readonly HashSet<string> TextExcludedKinds = new(StringComparer.Ordinal) { "export/storage fact", "format/export" };
    private static readonly HashSet<string> TextProperties = new(StringComparer.Ordinal) { "ToolTip", "Header", "Content", "Text" };
    private static readonly Regex FrozenTokens = new(
        "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|@\"(?:\"\"|[^\"])*\"|\\$?\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])'", RegexOptions.Compiled);

    internal sealed record TextResult(string Id, string Kind, bool Closed, string Reason, string Literal, string Assembly, string Method,
        string Path, string Symbol, string Entry, int Steps, string Feature);

    /// <summary>
    /// N6a: closes OPEN ledger rows whose frozen 1.5 text is in the compiled 2.0 program, held by a method of the same
    /// feature, reachable from the application's entry point. Rows of the export/storage and format kinds are never
    /// considered. Each row that fails stays OPEN and names the check that failed: 1 the literal is not compiled into
    /// the program, 2 it is only outside its feature, 3 it is in its feature but nothing reaches it.
    /// </summary>
    private static int LedgerTextPreservedCommand(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply the frozen source root, a fresh output folder and a date timestamp.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Text tool output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var registry = FeatureLedgerEvidence.Load(root, inventory);
        var declared = File.ReadAllText(Path.Combine(root, "docs/FEATURE-LEDGER.md")).Replace("\r\n", "\n").Split('\n')
            .Where(line => line.StartsWith("| FQ15-", StringComparison.Ordinal)).Select(line => line.Split('|').Select(c => c.Trim()).ToArray())
            .ToDictionary(c => c[1], c => (State: c[7], Evidence: c[8]), StringComparer.Ordinal);
        // The cohort: retained rows declared OPEN, and rows this tool itself closed before (a refresh re-proves them).
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row) && !TextExcludedKinds.Contains(row.Kind)
            && declared.TryGetValue(row.Id, out var d) && (d.State == "OPEN" || d.Evidence == "evidence:" + TextStep + ":" + row.Id))
            .OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        var frozenText = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string source in cohort.Select(r => r.Source).Distinct(StringComparer.Ordinal))
        {
            string path = Path.Combine(frozenRoot, source);
            if (!File.Exists(path) || FeatureLedgerEvidence.Hash(path) != inventory.Files.Single(f => f.Path == source).Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + source);
            frozenText[source] = File.ReadAllText(path);
        }
        // The reviewed relocations, from valid receipts of other steps only: for a C# row, the current members those receipts
        // name for rows of the same frozen member; for a XAML row, the current XAML files they name for rows of the same
        // frozen XAML file.
        var observationOf = inventory.Observations.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var frozenMembers = new Dictionary<string, string>(StringComparer.Ordinal);
        string FrozenMemberOf(FeatureLedger.Observation row)
        {
            if (frozenMembers.TryGetValue(row.Id, out string? known)) return known;
            string member = row.Source.EndsWith(".cs", StringComparison.Ordinal) && frozenText.TryGetValue(row.Source, out string? text)
                ? LedgerMemberFactComparison.Compare(row, text, new Dictionary<string, string>(StringComparer.Ordinal)).FrozenMember : "";
            frozenMembers[row.Id] = member;
            return member;
        }
        var memberMappings = new Dictionary<(string Source, string Member), HashSet<Owner>>();
        var xamlMappings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in registry.Entries.Values.Where(e => e.Valid && e.Step != TextStep && observationOf.ContainsKey(e.Row.ObservationId))
            .OrderBy(e => e.Reference, StringComparer.Ordinal))
        {
            FeatureLedger.Observation mappedRow = observationOf[entry.Row.ObservationId];
            if (!frozenText.ContainsKey(mappedRow.Source)) continue;
            if (mappedRow.Source.EndsWith(".xaml", StringComparison.Ordinal))
            {
                if (!xamlMappings.TryGetValue(mappedRow.Source, out var files)) xamlMappings[mappedRow.Source] = files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                files.UnionWith(entry.Row.Implementation.Select(i => i.Path.Replace('\\', '/')).Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)));
                continue;
            }
            string frozenMember = FrozenMemberOf(mappedRow);
            if (frozenMember.Length == 0) continue;
            var owners = entry.Row.Implementation.Select(i => OwnerOf(i.Symbol)).Where(o => o != null).Select(o => o!).ToList();
            if (owners.Count == 0) continue;
            if (!memberMappings.TryGetValue((mappedRow.Source, frozenMember), out var set)) memberMappings[(mappedRow.Source, frozenMember)] = set = new HashSet<Owner>();
            set.UnionWith(owners);
        }

        string bin = AppContext.BaseDirectory;
        string app = Path.Combine(bin, "FamilyQuant2.dll"), core = Path.Combine(bin, "FamilyQuant.Core.dll"), tests = typeof(Program).Assembly.Location;
        LedgerTextProgram program = LedgerTextProgram.Read(new[] { app, core }, root, typeof(FamilyQuant.MainWindow).Assembly);
        LedgerTextProgram control = LedgerTextProgram.Read(new[] { tests }, root, null, "FamilyQuant.Tests.LedgerTextControl::Entry()");

        var results = new List<TextResult>();
        TextWriter original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            Console.WriteLine("Compiled program: " + program.Methods.Count.ToString(CultureInfo.InvariantCulture) + " methods, " +
                program.Reached.Count.ToString(CultureInfo.InvariantCulture) + " reachable from " + program.Methods[program.EntryMethod].Key + "; " +
                program.Bamls.Count.ToString(CultureInfo.InvariantCulture) + " compiled XAML resources, " +
                program.Bamls.Values.Count(b => b.Failure != null).ToString(CultureInfo.InvariantCulture) + " not readable");
            // Controls first: the reachability and the decision are tried where the answer is known.
            Check("Text tool control: a literal reached from the control entry is reachable",
                control.MethodsByLiteral.GetValueOrDefault(LedgerTextControl.LiveLiteral)?.Any(m => control.Methods[m].TopType == "FamilyQuant.Tests.LedgerTextControl"
                    && control.Reached.ContainsKey(m)) == true);
            Check("Text tool control: a literal held only by dead code is not reachable",
                control.MethodsByLiteral.GetValueOrDefault(LedgerTextControl.DeadLiteral)?.Where(m => control.Methods[m].TopType == "FamilyQuant.Tests.LedgerTextControl")
                    .All(m => !control.Reached.ContainsKey(m)) == true);
            TextResult live = DecideControl(control, "control-live", LedgerTextControl.LiveLiteral, "FamilyQuant.Tests.LedgerTextControl", "LiveText");
            TextResult dead = DecideControl(control, "control-dead", LedgerTextControl.DeadLiteral, "FamilyQuant.Tests.LedgerTextControl", "Dead");
            Check("Text tool control: a row whose literal is reachable in its feature closes", live.Closed);
            Check("Text tool control: a row whose literal is only in dead code stays OPEN and names check 3", !dead.Closed && dead.Reason.StartsWith("3 failed", StringComparison.Ordinal));
            // FQAssay's counterexamples (review of 330afa5), as controls that must hold before any row closes.
            Check("Text tool control: a virtual method of a class nothing instantiates is not reachable",
                control.MethodsByLiteral.GetValueOrDefault(LedgerTextControl.NeverInstantiatedLiteral)?.All(m => !control.Reached.ContainsKey(m)) == true);
            Check("Text tool control: a literal overwritten later in the same call path does not close",
                !DecideControl(control, "control-overwritten", LedgerTextControl.OverwrittenLiteral, "FamilyQuant.Tests.LedgerTextDeadStore", "Caption").Closed);
            Check("Text tool control: a sentence held by its own frozen member closes",
                DecideControl(control, "control-own", LedgerTextControl.OwnMemberLiteral, "FamilyQuant.Tests.LedgerTextLender", "Alpha").Closed);
            Check("Text tool control: a sentence held by another member of the same class does not close",
                !DecideControl(control, "control-lent", LedgerTextControl.LentLiteral, "FamilyQuant.Tests.LedgerTextLender", "Alpha").Closed);
            Check("Text tool control: a format token does not close by text alone, even in its own member",
                !DecideControl(control, "control-short", LedgerTextControl.ShortLiteral, "FamilyQuant.Tests.LedgerTextLender", "Alpha").Closed);
            // FQAssay's review of b3f5540: structured operands with two letter runs, held by their own member.
            Check("Text tool control (review-field-path): a dotted binding path held only as a caption in its member does not close",
                !DecideControl(control, "review-field-path", LedgerTextControl.FieldPathLiteral, "FamilyQuant.Tests.LedgerTextOperands", "Display").Closed);
            Check("Text tool control (review-format-operand): a numeric format with a quoted unit does not close by text",
                !DecideControl(control, "review-format-operand", LedgerTextControl.FormatOperandLiteral, "FamilyQuant.Tests.LedgerTextOperands", "Display").Closed);
            Check("Text tool control (review-attached-path): a parenthesized attached property path held only as a caption does not close",
                !DecideControl(control, "review-attached-path", LedgerTextControl.AttachedPathLiteral, "FamilyQuant.Tests.LedgerTextOperands", "Display").Closed);

            foreach (var row in cohort)
            {
                TextResult result;
                if (Literal(row, frozenText[row.Source]) is not { } literal) result = Open(row, "1 failed: the row records no literal text (kind " + row.Kind + ")");
                else if (literal.Xaml is { } xaml)
                {
                    var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string current = FeatureLedgerEvidence.CurrentPath(root, row.Source);
                    if (current.Length > 0) files.Add(current);
                    if (xamlMappings.TryGetValue(row.Source, out var more)) files.UnionWith(more);
                    result = DecideXaml(program, row, xaml, files, root);
                }
                else
                {
                    string frozenMember = FrozenMemberOf(row);
                    var owners = new List<Owner>();
                    if (OwnerOf(frozenMember) is { } own) owners.Add(own);
                    if (frozenMember.Length > 0 && memberMappings.TryGetValue((row.Source, frozenMember), out var reviewed))
                        owners.AddRange(reviewed.Where(o => !owners.Contains(o)).OrderBy(o => o.Type, StringComparer.Ordinal).ThenBy(o => o.Member, StringComparer.Ordinal));
                    result = owners.Count == 0 ? Open(row, "2 failed: the frozen literal's containing member is not identified, so no current member can be its holder")
                        : Decide(program, row, literal.Pieces, owners, "member " + string.Join(" or ", owners.Select(o => o.Type + "." + o.Member)));
                }
                results.Add(result);
                if (result.Closed) Check("Text " + result.Id + ": " + result.Method, true);
                else { skipped++; Console.WriteLine("  SKIP  " + result.Id + ": " + result.Reason); }
            }
            var unsupported = results.Where(r => ReviewCounterexamples.Contains(r.Id) && r.Closed).Select(r => r.Id).ToList();
            Check("Text tool counterexamples: the ten rows FQAssay found unsupported in 330afa5 stay OPEN", unsupported.Count == 0,
                string.Join(" ", unsupported));
            foreach (var group in results.Where(r => r.Closed).GroupBy(r => r.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
                Check(KindAssertion(group.Key), group.All(r => r.Closed && r.Path.Length > 0 && r.Entry.Length > 0));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }

        string log = Path.Combine(output, "text.log"), data = Path.Combine(output, "text.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(data, results);
        string Kind(string kind) => "text-" + kind.Replace('/', '-').Replace(' ', '-');
        var rows = results.Select(r => new FeatureLedgerEvidence.Row(r.Id,
            r.Closed ? new() { new(r.Path, r.Symbol, FeatureLedgerEvidence.Hash(Path.Combine(root, r.Path))) } : new(), new(),
            r.Closed ? "Frozen text in the compiled program, in its feature, reachable; no behavior, number or file claim." : r.Reason,
            r.Closed ? Kind(r.Kind) : null,
            r.Closed ? "frozen literal \"" + TextShorten(r.Literal) + "\" (SHA256 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(r.Literal)))[..16]
                + ") verbatim in " + r.Assembly + " " + r.Method + " [" + r.Path + "]; feature " + r.Feature + "; reached as " + r.Entry + ", "
                + r.Steps.ToString(CultureInfo.InvariantCulture) + " steps from the entry point" : null)).ToList();
        var kindReceipts = results.Where(r => r.Closed).Select(r => r.Kind).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new FeatureLedgerEvidence.KindReceipt(Kind(k), k, new() { new("text-tool", "test", nameof(LedgerTextPreservedCommand), KindAssertion(k), log, "") }))
            .ToList();
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record executing commit.");
        var artifacts = new List<FeatureLedgerEvidence.Artifact> { new(log, FeatureLedgerEvidence.Hash(log)), new(data, FeatureLedgerEvidence.Hash(data)) };
        void Retain(string source, string relative)
        {
            string destination = Path.Combine(output, "executed", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, false);
            artifacts.Add(new(destination, FeatureLedgerEvidence.Hash(destination)));
        }
        foreach (string file in new[] { app, Path.ChangeExtension(app, ".pdb"), core, Path.ChangeExtension(core, ".pdb"), tests })
            Retain(file, Path.GetFileName(file));
        foreach (string source in new[] { "FamilyQuant.Tests/LedgerTextPreserved.cs", "FamilyQuant.Tests/Program.LedgerTextPreserved.cs",
            "FamilyQuant.Tests/Program.Commands.LedgerTextPreserved.cs", "FamilyQuant.Tests/FeatureLedgerEvidence.cs" })
            Retain(Path.Combine(root, source), source);
        var run = new FeatureLedgerEvidence.Run("text-tool", Environment.CommandLine, args[3], commit, artifacts, root);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, TextStep + ".json"),
            new FeatureLedgerEvidence.Register(1, TextStep, FeatureLedger.ReferenceCommit, new() { run }, rows, kindReceipts));
        Console.WriteLine("Text tool: " + cohort.Length.ToString(CultureInfo.InvariantCulture) + " OPEN rows considered; "
            + results.Count(r => r.Closed).ToString(CultureInfo.InvariantCulture) + " closed; by kind "
            + string.Join(", ", results.Where(r => r.Closed).GroupBy(r => r.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " " + g.Count().ToString(CultureInfo.InvariantCulture)))
            + ". Still open by reason: " + string.Join("; ", results.Where(r => !r.Closed).GroupBy(r => r.Reason[..Math.Min(r.Reason.Length, r.Reason.IndexOf(':') < 0 ? r.Reason.Length : r.Reason.IndexOf(':'))])
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " " + g.Count().ToString(CultureInfo.InvariantCulture))));
        return failed == 0 ? 0 : 1;
    }

    // Review counterexamples checked after the run; the rule never reads this list.
    private static readonly HashSet<string> ReviewCounterexamples = new(StringComparer.Ordinal)
    {
        "FQ15-7696D63AAAAF", "FQ15-02D50AED453D", "FQ15-1ED341AE1494", "FQ15-0908BA69C203", "FQ15-0906BBD71270",
        "FQ15-02C518935F28", "FQ15-1000D1800FFE", "FQ15-08A48106FA52", "FQ15-02E2055621DE", "FQ15-52B123DB1542",
    };

    /// <summary>A control row whose frozen literal sits in the named member of the named type.</summary>
    private static TextResult DecideControl(LedgerTextProgram program, string id, string literal, string ownerType, string ownerMember) =>
        Decide(program, new FeatureLedger.Observation(id, "control", 1, "code fact", "\"" + literal + "\""), new[] { literal },
            new List<Owner> { new(ownerType[(ownerType.LastIndexOf('.') + 1)..], ownerMember, ownerMember == "Caption") }, "control member " + ownerMember);

    private static string KindAssertion(string kind) =>
        "Text tool kind " + kind + ": every listed row holds its frozen literal verbatim in the compiled program, in its feature, reachable from the entry point";
    private static string TextShorten(string text)
    {
        string escaped = new string(text.Select(c => c < 32 || c > 126 ? '?' : c).ToArray()).Replace("\"", "'");
        return escaped.Length <= 100 ? escaped : escaped[..100] + "...";
    }
    private static TextResult Open(FeatureLedger.Observation row, string reason) => new(row.Id, row.Kind, false, reason, "", "", "", "", "", "", 0, "");

    /// <summary>A source member: its type's simple name and the member's name; a field or property is credited only through the
    /// literal stored into it by a constructor.</summary>
    private sealed record Owner(string Type, string Member, bool Field);

    /// <summary>The owner a member symbol names ("Ns.Type::declaration ( ... )"), or null when the symbol names none.</summary>
    private static Owner? OwnerOf(string symbol)
    {
        int split = symbol.IndexOf("::", StringComparison.Ordinal);
        if (split <= 0) return null;
        string type = symbol[..split];
        if (type.Contains(' ', StringComparison.Ordinal)) return null;
        type = type[(type.LastIndexOfAny(new[] { '.', '+', '/' }) + 1)..];
        string[] tokens = symbol[(split + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int open = Array.IndexOf(tokens, "(");
        if (open > 0)
        {
            int at = open - 1;
            if (tokens[at] == ">")
                for (int depth = 0; at >= 0; at--)
                {
                    if (tokens[at] == ">") depth++;
                    else if (tokens[at] == "<" && --depth == 0) { at--; break; }
                }
            return at >= 0 && IsName(tokens[at]) ? new Owner(type, tokens[at], false) : null;
        }
        int end = Array.FindIndex(tokens, t => t is "=" or "=>" or "{" or ";");
        string name = end > 0 ? tokens[end - 1] : tokens.LastOrDefault() ?? "";
        return IsName(name) ? new Owner(type, name, true) : null;
    }
    private static bool IsName(string token) => token.Length > 0 && (char.IsLetter(token[0]) || token[0] == '_') && token.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>Whether a compiled method is the owner's member: the same type and member, the constructor for a constructor,
    /// or a constructor storing the literal straight into the owner's field or property.</summary>
    private static bool Holds(LedgerTextProgram.Method method, Owner owner, string? storedInto)
    {
        if (method.SourceType != owner.Type) return false;
        if (method.SourceMember == owner.Member) return true;
        bool constructor = method.SourceMember is ".ctor" or ".cctor";
        if (constructor && owner.Member == owner.Type) return true;
        return constructor && owner.Field && storedInto != null && storedInto.EndsWith("::" + owner.Member, StringComparison.Ordinal);
    }

    /// <summary>Text alone identifies a fact only when it is prose: at least two words of letters separated by whitespace (N6a-4).
    /// A token with no whitespace, whatever its punctuation (an identifier, a binding or attached property path, a format
    /// token, a separator), is never prose, and neither is a numeric or date format operand, quoted unit words included.</summary>
    private static bool IsProse(string text) => !IsFormatOperand(text)
        && Regex.Split(text.Trim(), @"\s+").Count(token => Regex.IsMatch(token, @"\p{L}{2,}")) >= 2;

    /// <summary>Whether the text is a .NET numeric or date and time format: with its quoted parts and escaped characters set
    /// aside, what remains is a standard numeric specifier, a custom numeric pattern, or a custom date and time pattern.</summary>
    private static bool IsFormatOperand(string text)
    {
        string rest = Regex.Replace(text, @"'[^']*'|""[^""]*""|\\.", " ").Trim();
        return rest.Length > 0 && (Regex.IsMatch(rest, @"^[NnFfCcDdPpXxGgRrEe]\d{0,2}$")
            || Regex.IsMatch(rest, @"^[0#.,%\u2030;Ee+\-\s]*[0#][0#.,%\u2030;Ee+\-\s]*$")
            || Regex.IsMatch(rest, @"^[yMdHhmsfFtzKg:/\-.,\s]*(yy|MM|dd|HH|hh|mm|ss)[yMdHhmsfFtzKg:/\-.,\s]*$"));
    }

    private static string BamlOf(string xamlPath)
    {
        string path = xamlPath.Replace('\\', '/');
        int slash = path.IndexOf('/');
        return (slash >= 0 ? path[(slash + 1)..] : path).ToLowerInvariant()[..^5] + ".baml";
    }

    private static readonly Dictionary<string, List<Match>> TokensOf = new(StringComparer.Ordinal);
    private sealed record FrozenLiteral(string[] Pieces, (string Name, string Member, string Value, bool Control)? Xaml);

    /// <summary>What the frozen row compiles to. A C# literal is read with its neighbours: a run of constant literals joined
    /// by '+' compiles to one string, so the run's value is what the compiled program must hold; an interpolated literal
    /// compiles to its constant pieces, in order. A XAML attribute row names its element, property and value; a control row
    /// its element type and name. Other kinds record no literal.</summary>
    private static FrozenLiteral? Literal(FeatureLedger.Observation row, string frozen)
    {
        if (row.Source.EndsWith(".xaml", StringComparison.Ordinal))
        {
            if (row.Kind == "control")
            {
                string[] parts = row.Detail.Split(' ', 2);
                return parts.Length == 2 && parts[0] != parts[1] ? new FrozenLiteral(Array.Empty<string>(), (parts[1], "", "", true)) : null;
            }
            int equals = row.Detail.IndexOf(" = ", StringComparison.Ordinal);
            if (equals < 0 || row.Kind is "handler") return null;
            string left = row.Detail[..equals], value = row.Detail[(equals + 3)..];
            int dot = left.LastIndexOf('.');
            if (dot < 0 || value.StartsWith("{", StringComparison.Ordinal)) return null;
            return new FrozenLiteral(new[] { value }, (left[..dot], left[(dot + 1)..], value, false));
        }
        if (row.Kind is not ("code fact" or "help fact")) return null;
        if (!TokensOf.TryGetValue(frozen, out var tokens)) TokensOf[frozen] = tokens = FrozenTokens.Matches(frozen).Cast<Match>().ToList();
        int lineStart = 0;
        for (int line = 1; line < row.Line; line++) lineStart = frozen.IndexOf('\n', lineStart) + 1;
        int lineEnd = frozen.IndexOf('\n', lineStart);
        var here = tokens.Select((t, i) => (t, i)).Where(x => x.t.Index >= lineStart && (lineEnd < 0 || x.t.Index < lineEnd) && x.t.Value == row.Detail).ToList();
        if (here.Count == 0) return null;
        int index = here[0].i;
        bool IsString(Match t) => t.Value.StartsWith("\"", StringComparison.Ordinal) || t.Value.StartsWith("@\"", StringComparison.Ordinal);
        if (row.Detail.StartsWith("$", StringComparison.Ordinal))
            return new FrozenLiteral(Interpolated(row.Detail).Where(p => p.Length > 0).ToArray() is { Length: > 0 } pieces ? pieces : Array.Empty<string>(), null);
        bool Joined(Match a, Match b)
        {
            string between = frozen[(a.Index + a.Length)..b.Index];
            foreach (Match t in tokens.Where(t => t.Index >= a.Index + a.Length && t.Index < b.Index)) between = between.Replace(t.Value, " ");
            return between.Trim() == "+";
        }
        int first = index, last = index;
        while (first > 0 && IsString(tokens[first - 1]) && Joined(tokens[first - 1], tokens[first])) first--;
        while (last + 1 < tokens.Count && IsString(tokens[last + 1]) && Joined(tokens[last], tokens[last + 1])) last++;
        return new FrozenLiteral(new[] { string.Concat(tokens.Skip(first).Take(last - first + 1).Select(t => Unescape(t.Value))) }, null);
    }

    private static IEnumerable<string> Interpolated(string token)
    {
        bool verbatim = token.StartsWith("$@", StringComparison.Ordinal) || token.StartsWith("@$", StringComparison.Ordinal);
        string body = token[(verbatim ? 3 : 2)..^1];
        var piece = new StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '{' && i + 1 < body.Length && body[i + 1] == '{') { piece.Append('{'); i++; continue; }
            if (body[i] == '}' && i + 1 < body.Length && body[i + 1] == '}') { piece.Append('}'); i++; continue; }
            if (body[i] == '{')
            {
                yield return verbatim ? piece.ToString().Replace("\"\"", "\"") : UnescapeBody(piece.ToString());
                piece.Clear();
                int depth = 1;
                for (i++; i < body.Length && depth > 0; i++) { if (body[i] == '{') depth++; else if (body[i] == '}') depth--; }
                i--;
                continue;
            }
            if (!verbatim && body[i] == '\\' && i + 1 < body.Length) { piece.Append(body[i]).Append(body[i + 1]); i++; continue; }
            piece.Append(body[i]);
        }
        yield return verbatim ? piece.ToString().Replace("\"\"", "\"") : UnescapeBody(piece.ToString());
    }

    private static string Unescape(string token) => token.StartsWith("@", StringComparison.Ordinal)
        ? token[2..^1].Replace("\"\"", "\"") : UnescapeBody(token[1..^1]);
    private static string UnescapeBody(string body)
    {
        var text = new StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\' || i + 1 >= body.Length) { text.Append(body[i]); continue; }
            char c = body[++i];
            switch (c)
            {
                case 'n': text.Append('\n'); break;
                case 'r': text.Append('\r'); break;
                case 't': text.Append('\t'); break;
                case '0': text.Append('\0'); break;
                case 'a': text.Append('\a'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                case 'v': text.Append('\v'); break;
                case 'u': text.Append((char)Convert.ToInt32(body.Substring(i + 1, 4), 16)); i += 4; break;
                case 'U': text.Append(char.ConvertFromUtf32(Convert.ToInt32(body.Substring(i + 1, 8), 16))); i += 8; break;
                case 'x':
                    int n = 0;
                    while (n < 4 && i + 1 + n < body.Length && Uri.IsHexDigit(body[i + 1 + n])) n++;
                    text.Append((char)Convert.ToInt32(body.Substring(i + 1, n), 16)); i += n; break;
                default: text.Append(c); break;
            }
        }
        return text.ToString();
    }

    /// <summary>A C# row: the frozen member (or a reviewed mapping of it) holds every piece, in order, as prose that is not
    /// overwritten later in the same call path, and is reached from the entry point.</summary>
    private static TextResult Decide(LedgerTextProgram program, FeatureLedger.Observation row, string[] pieces, List<Owner> owners, string how)
    {
        if (pieces.Length == 0) return Open(row, "1 failed: the interpolated literal has no constant text");
        if (!IsProse(string.Concat(pieces))) return Open(row, "1 failed: a format string, separator or single word does not identify a fact by text alone");
        var holders = (program.MethodsByLiteral.GetValueOrDefault(pieces[0]) ?? new List<int>()).Where(m => Ordered(program.Literals[m], pieces))
            .OrderBy(m => program.Methods[m].Assembly, StringComparer.Ordinal).ThenBy(m => program.Methods[m].Key, StringComparer.Ordinal).ToList();
        if (holders.Count == 0)
            return Open(row, "1 failed: the frozen literal is not in the compiled program" + (pieces.Length > 1 ? " (" + pieces.Length.ToString(CultureInfo.InvariantCulture) + " constant pieces)" : ""));
        var uses = holders.ToDictionary(m => m, m => program.LiteralUses[m].Where(u => u.Text == pieces[0]).ToList());
        var own = holders.Where(m => owners.Any(o => uses[m].Any(u => Holds(program.Methods[m], o, u.StoredInto)))).ToList();
        if (own.Count == 0)
            return Open(row, "2 failed: the literal is compiled only in other members (" + string.Join(", ", holders.Take(2).Select(m => program.Methods[m].Key)) + "), not in " + how);
        var live = own.Where(m => uses[m].Any(u => !program.Overwritten(m, u))).ToList();
        if (live.Count == 0)
            return Open(row, "1 failed: the compiled literal is overwritten later in the same call path (" + program.Methods[own[0]].Key + ")");
        var reached = live.Where(m => program.Reached.ContainsKey(m) && program.Methods[m].Document.Length > 0
            && !program.Methods[m].Document.Contains("/obj/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (reached.Count == 0)
            return Open(row, "3 failed: held by " + program.Methods[live[0]].Key + " but not reachable from the entry point");
        int method = reached[0];
        var (entry, steps) = program.EntryOf(method);
        return new TextResult(row.Id, row.Kind, true, "", string.Join("{}", pieces), program.Methods[method].Assembly, program.Methods[method].Key,
            program.Methods[method].Document, program.Methods[method].Key, entry, steps, how);
    }

    private static bool Ordered(List<string> literals, string[] pieces)
    {
        int at = 0;
        foreach (string text in literals) if (at < pieces.Length && text == pieces[at]) at++;
        return at == pieces.Length;
    }

    /// <summary>A XAML row: the compiled XAML of its own screen (its relocated file, or a XAML file reviewed receipts map rows of
    /// the same frozen file to) holds an element of the same name (or, unnamed, of the same type with a prose value) with that
    /// property and value; neither it nor an element around it has its visibility set by markup or by reachable code; and the
    /// class that loads that XAML is reached. There is no fallback to code: a XAML fact keeps its element and property.</summary>
    private static TextResult DecideXaml(LedgerTextProgram program, FeatureLedger.Observation row, (string Name, string Member, string Value, bool Control) xaml,
        HashSet<string> files, string root)
    {
        bool Matches(LedgerTextProgram.XamlValue v) => xaml.Control
            ? v.Member.Length == 0 && v.ElementName == xaml.Name && row.Detail.StartsWith(v.ElementType + " ", StringComparison.Ordinal)
            : v.Member == xaml.Member && v.Value == xaml.Value
                && (v.ElementName == xaml.Name || v.ElementName.Length == 0 && v.ElementType == xaml.Name && IsProse(xaml.Value));
        var own = files.Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => (Path: f, Baml: program.Bamls.GetValueOrDefault(BamlOf(f)))).Where(x => x.Baml != null).ToList();
        var matches = own.SelectMany(x => x.Baml!.Values.Where(Matches).Select(v => (x.Path, Baml: x.Baml!, Value: v))).ToList();
        if (matches.Count == 0)
        {
            var elsewhere = program.Bamls.Values.Where(b => b.Values.Any(Matches)).Select(b => b.Resource).Take(2).ToList();
            return elsewhere.Count > 0
                ? Open(row, "2 failed: only another screen's compiled XAML holds it (" + string.Join(", ", elsewhere) + ")")
                : Open(row, "1 failed: no compiled XAML of its screen holds " + (xaml.Control ? "this named element" : "this element, property and value"));
        }
        foreach (var (path, baml, value) in matches)
        {
            bool hidden = value.VisibilityInMarkup || program.VisibilityFields.Contains(value.ElementName) || value.Ancestors.Any(program.VisibilityFields.Contains);
            int loader = program.Methods.FindIndex(m => baml.OwnerType != null && m.Key == baml.OwnerType + "::InitializeComponent()");
            if (hidden || loader < 0 || !program.Reached.ContainsKey(loader)) continue;
            var (entry, steps) = program.EntryOf(loader);
            return new TextResult(row.Id, row.Kind, true, "", xaml.Control ? row.Detail : xaml.Value, "FamilyQuant2.dll", program.Methods[loader].Key + " loading " + baml.Resource,
                path, "compiled XAML " + baml.Resource + ": " + (xaml.Control ? row.Detail : xaml.Name + "." + xaml.Member), entry, steps,
                "compiled XAML of " + (baml.OwnerType ?? baml.Resource));
        }
        var first = matches[0];
        bool anyHidden = matches.Any(x => x.Value.VisibilityInMarkup || program.VisibilityFields.Contains(x.Value.ElementName) || x.Value.Ancestors.Any(program.VisibilityFields.Contains));
        return Open(row, anyHidden
            ? "3 failed: it or an element around it has its visibility set by markup or code (" + first.Baml.Resource + "), so being shown is not established"
            : "3 failed: in its screen's compiled XAML (" + first.Baml.Resource + ") but its class is not reachable from the entry point");
    }
}
