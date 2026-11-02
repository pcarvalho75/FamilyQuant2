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
        // The reviewed relocations: every current file a register maps a row of the same frozen file to.
        var sourceOf = inventory.Observations.ToDictionary(o => o.Id, o => o.Source, StringComparer.Ordinal);
        var mapped = registry.Entries.Values.Where(e => e.Step != TextStep && sourceOf.ContainsKey(e.Row.ObservationId))
            .SelectMany(e => e.Row.Implementation.Select(i => (Source: sourceOf[e.Row.ObservationId], i.Path)))
            .GroupBy(x => x.Source, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Path.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);

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
            var controlFeature = new Feature(new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.Ordinal) { "FamilyQuant.Tests.LedgerTextControl" }, "control type");
            TextResult live = Decide(control, new FeatureLedger.Observation("control-live", "control", 1, "code fact", "\"" + LedgerTextControl.LiveLiteral + "\""),
                new[] { LedgerTextControl.LiveLiteral }, controlFeature);
            TextResult dead = Decide(control, new FeatureLedger.Observation("control-dead", "control", 1, "code fact", "\"" + LedgerTextControl.DeadLiteral + "\""),
                new[] { LedgerTextControl.DeadLiteral }, controlFeature);
            Check("Text tool control: a row whose literal is reachable in its feature closes", live.Closed);
            Check("Text tool control: a row whose literal is only in dead code stays OPEN and names check 3", !dead.Closed && dead.Reason.StartsWith("3 failed", StringComparison.Ordinal));

            var features = new Dictionary<string, Feature>(StringComparer.Ordinal);
            foreach (var row in cohort)
            {
                if (!features.TryGetValue(row.Source, out Feature? feature)) features[row.Source] = feature = FeatureOf(program, row, root, mapped);
                TextResult result = Literal(row, frozenText[row.Source]) is not { } literal ? Open(row, "1 failed: the row records no literal text (kind " + row.Kind + ")")
                    : literal.Xaml is { } xaml ? DecideXaml(program, row, xaml, literal.Pieces, feature, root)
                    : Decide(program, row, literal.Pieces, feature);
                results.Add(result);
                if (result.Closed) Check("Text " + result.Id + ": " + result.Method, true);
                else { skipped++; Console.WriteLine("  SKIP  " + result.Id + ": " + result.Reason); }
            }
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

    private static string KindAssertion(string kind) =>
        "Text tool kind " + kind + ": every listed row holds its frozen literal verbatim in the compiled program, in its feature, reachable from the entry point";
    private static string TextShorten(string text)
    {
        string escaped = new string(text.Select(c => c < 32 || c > 126 ? '?' : c).ToArray()).Replace("\"", "'");
        return escaped.Length <= 100 ? escaped : escaped[..100] + "...";
    }
    private static TextResult Open(FeatureLedger.Observation row, string reason) => new(row.Id, row.Kind, false, reason, "", "", "", "", "", "", 0, "");

    /// <summary>The current files and types of a frozen row's feature: its relocated path, every current file the reviewed
    /// registers map rows of the same frozen file to, and the top-level types those files declare (partial files of the
    /// same class included); a XAML file's class too.</summary>
    private sealed record Feature(HashSet<string> Files, HashSet<string> Types, string How);
    private static Feature FeatureOf(LedgerTextProgram program, FeatureLedger.Observation row, string root, Dictionary<string, HashSet<string>> mapped)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = FeatureLedgerEvidence.CurrentPath(root, row.Source);
        if (current.Length > 0) files.Add(current);
        string code = row.Source.EndsWith(".xaml", StringComparison.Ordinal) ? FeatureLedgerEvidence.CurrentPath(root, row.Source + ".cs") : "";
        if (code.Length > 0) files.Add(code);
        if (mapped.TryGetValue(row.Source, out var more)) files.UnionWith(more);
        var types = program.Methods.Where(m => m.Document.Length > 0 && files.Contains(m.Document)).Select(m => m.TopType).ToHashSet(StringComparer.Ordinal);
        foreach (string file in files.Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
            if (program.Bamls.TryGetValue(BamlOf(file), out var baml) && baml.OwnerType != null) types.Add(baml.OwnerType.Split('/')[0]);
        return new Feature(files, types, (current.Length > 0 ? current : "no relocated path") + (more != null ? " and " + more.Count.ToString(CultureInfo.InvariantCulture) + " reviewed mapped files" : ""));
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

    /// <summary>A C# row: a method holds every piece (in order, for an interpolated literal), belongs to the feature and is reached.</summary>
    private static TextResult Decide(LedgerTextProgram program, FeatureLedger.Observation row, string[] pieces, Feature feature)
    {
        if (pieces.Length == 0) return Open(row, "1 failed: the interpolated literal has no constant text");
        var holders = (program.MethodsByLiteral.GetValueOrDefault(pieces[0]) ?? new List<int>()).Where(m => Ordered(program.Literals[m], pieces))
            .OrderBy(m => program.Methods[m].Assembly, StringComparer.Ordinal).ThenBy(m => program.Methods[m].Key, StringComparer.Ordinal).ToList();
        if (holders.Count == 0)
            return Open(row, "1 failed: the frozen literal is not in the compiled program" + (pieces.Length > 1 ? " (" + pieces.Length.ToString(CultureInfo.InvariantCulture) + " constant pieces)" : ""));
        var inFeature = holders.Where(m => feature.Files.Contains(program.Methods[m].Document) || feature.Types.Contains(program.Methods[m].TopType)).ToList();
        if (inFeature.Count == 0)
            return Open(row, "2 failed: the literal is compiled only outside its feature (" + string.Join(", ", holders.Take(2).Select(m => program.Methods[m].Key)) + ")");
        var reached = inFeature.Where(m => program.Reached.ContainsKey(m) && program.Methods[m].Document.Length > 0
            && !program.Methods[m].Document.Contains("/obj/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (reached.Count == 0)
            return Open(row, "3 failed: in its feature (" + program.Methods[inFeature[0]].Key + ") but not reachable from the entry point");
        int method = reached[0];
        var (entry, steps) = program.EntryOf(method);
        string how = feature.Files.Contains(program.Methods[method].Document) ? "file " + program.Methods[method].Document : "class " + program.Methods[method].TopType;
        return new TextResult(row.Id, row.Kind, true, "", string.Join("{}", pieces), program.Methods[method].Assembly, program.Methods[method].Key,
            program.Methods[method].Document, program.Methods[method].Key, entry, steps, how + " (" + feature.How + ")");
    }

    private static bool Ordered(List<string> literals, string[] pieces)
    {
        int at = 0;
        foreach (string text in literals) if (at < pieces.Length && text == pieces[at]) at++;
        return at == pieces.Length;
    }

    /// <summary>A XAML row: a compiled XAML resource of the feature holds an element of the same name (or, unnamed, of the same
    /// type) with that property and value, and the class that loads it is reached; a text property may also have moved
    /// into code of the same feature.</summary>
    private static TextResult DecideXaml(LedgerTextProgram program, FeatureLedger.Observation row, (string Name, string Member, string Value, bool Control) xaml,
        string[] pieces, Feature feature, string root)
    {
        bool Matches(LedgerTextProgram.XamlValue v) => xaml.Control
            ? v.Member.Length == 0 && v.ElementName == xaml.Name && row.Detail.StartsWith(v.ElementType + " ", StringComparison.Ordinal)
            : v.Member == xaml.Member && v.Value == xaml.Value && (v.ElementName == xaml.Name || v.ElementName.Length == 0 && v.ElementType == xaml.Name);
        var holders = program.Bamls.Values.Where(b => b.Values.Any(Matches)).OrderBy(b => b.Resource, StringComparer.Ordinal).ToList();
        var inFeature = holders.Where(b => feature.Files.Any(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) && BamlOf(f) == b.Resource)
            || b.OwnerType != null && feature.Types.Contains(b.OwnerType.Split('/')[0])).ToList();
        foreach (LedgerTextProgram.Baml baml in inFeature)
        {
            int loader = program.Methods.FindIndex(m => baml.OwnerType != null && m.Key == baml.OwnerType + "::InitializeComponent()");
            string xamlPath = feature.Files.FirstOrDefault(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) && BamlOf(f) == baml.Resource)
                ?? Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*.xaml", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).FirstOrDefault(f => BamlOf(f) == baml.Resource) ?? "";
            if (loader < 0 || !program.Reached.ContainsKey(loader) || xamlPath.Length == 0) continue;
            var (entry, steps) = program.EntryOf(loader);
            return new TextResult(row.Id, row.Kind, true, "", xaml.Control ? row.Detail : xaml.Value, "FamilyQuant2.dll", program.Methods[loader].Key + " loading " + baml.Resource,
                xamlPath, "compiled XAML " + baml.Resource + ": " + (xaml.Control ? row.Detail : xaml.Name + "." + xaml.Member), entry, steps,
                "compiled XAML of " + (baml.OwnerType ?? baml.Resource) + " (" + feature.How + ")");
        }
        if (!xaml.Control && TextProperties.Contains(xaml.Member))
        {
            TextResult code = Decide(program, row, pieces, feature);
            if (code.Closed || holders.Count == 0) return code;
        }
        if (holders.Count == 0) return Open(row, "1 failed: no compiled XAML holds " + (xaml.Control ? "this named element" : "this element, property and value"));
        if (inFeature.Count == 0) return Open(row, "2 failed: compiled XAML holds it only outside its feature (" + string.Join(", ", holders.Take(2).Select(b => b.Resource)) + ")");
        return Open(row, "3 failed: in its feature's compiled XAML (" + inFeature[0].Resource + ") but its class is not reachable from the entry point");
    }
}
