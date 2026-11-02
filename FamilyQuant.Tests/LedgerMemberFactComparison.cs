using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests;

/// <summary>
/// Compares retained C# literal declarations, never handler execution or a resolved runtime value.
/// Callers supply a deliberately scoped set of source files and validate their provenance separately.
/// Unsupported syntax and ambiguous destinations remain nonmatches.
/// </summary>
internal static class LedgerMemberFactComparison
{
    internal sealed record Result(string Id, bool Matched, string Before, string Actual, string CurrentPath,
        string Symbol, string Reason, string FrozenMember = "", string CurrentMember = "",
        int FrozenOccurrences = 0, int CurrentOccurrences = 0,
        string FrozenMemberSha256 = "", string CurrentMemberSha256 = "");

    // A mapping is an explicitly reviewed relocation, not a same-text search or proof of reachability.
    // The complete member declaration and body must still retain exactly the same tokens.
    internal sealed record MemberMapping(string FrozenMember, string CurrentPath, string CurrentMember);

    internal static bool HasTypeDeclaration(string source, string owner, string declaration)
    {
        if (!TryRead(source, out var parsed, out _)) return false;
        var members = parsed.Members.Where(member => member.Symbol.StartsWith(owner + "::", StringComparison.Ordinal)).ToArray();
        return members.Length > 0 && members.All(member => member.TypeContext.Length == 1 && member.TypeContext[0] == declaration);
    }

    internal static Result Compare(FeatureLedger.Observation observation, string frozenSource,
        IReadOnlyDictionary<string, string> currentSources, MemberMapping? mapping = null)
    {
        Result No(string reason, Member? before = null, Member? after = null, string path = "") =>
            new(observation.Id, false, observation.Detail,
                after?.Tokens.FirstOrDefault(token => token.IsString && token.Text == observation.Detail)?.Text ?? "",
                path, after?.Symbol ?? "", reason, before?.Symbol ?? "", after?.Symbol ?? "",
                Count(before, observation.Detail), Count(after, observation.Detail), Fingerprint(before), Fingerprint(after));

        if (observation.Kind is not ("code fact" or "help fact" or "format/export" or "export/storage fact" or "option/copy"))
            return No("This comparison supports literal declarations only; behavior needs its own executed check.");
        if (!observation.Source.EndsWith(".cs", StringComparison.Ordinal) || observation.Line < 1)
            return No("A C# source path and physical frozen line are required.");
        if (!TryRead(frozenSource, out var frozen, out string problem)) return No("Frozen source: " + problem);
        var literals = frozen.Tokens.Where(token => token.IsString && token.Line == observation.Line
            && token.Text == observation.Detail).ToArray();
        if (literals.Length != 1) return No("The frozen line does not identify exactly one recorded literal token.");
        var owners = frozen.Members.Where(member => member.Tokens.Contains(literals[0])).ToArray();
        if (owners.Length != 1) return No("The frozen literal has no unique supported containing member.");
        var before = owners[0];
        if (frozen.Members.Count(member => member.Symbol == before.Symbol) != 1)
            return No("The frozen containing member identity is ambiguous.", before);
        if (mapping != null && (mapping.FrozenMember != before.Symbol || string.IsNullOrWhiteSpace(mapping.CurrentPath)
            || string.IsNullOrWhiteSpace(mapping.CurrentMember)))
            return No("The explicit relocation does not identify this frozen member and a current destination.", before);

        string wanted = mapping?.CurrentMember ?? before.Symbol;
        var candidates = new List<(string Path, Member Member)>();
        var sources = mapping == null ? currentSources
            : currentSources.Where(file => file.Key == mapping.CurrentPath).ToDictionary(file => file.Key, file => file.Value, StringComparer.Ordinal);
        if (sources.Count == 0) return No("The explicitly mapped current source is absent.", before);
        foreach (var file in sources.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            if (!TryRead(file.Value, out var current, out problem))
                return No("Current source " + file.Key + ": " + problem, before);
            candidates.AddRange(current.Members.Where(member => member.Symbol == wanted).Select(member => (file.Key, member)));
        }
        if (candidates.Count != 1) return No("The current containing member is missing or ambiguous; no same-text fallback.", before);
        var candidate = candidates[0];
        var after = candidate.Member;
        if (mapping == null && !before.TypeContext.SequenceEqual(after.TypeContext, StringComparer.Ordinal))
            return No("The enclosing type declaration changed and needs an explicit reviewed relocation.", before, after, candidate.Path);
        if (Count(before, observation.Detail) != Count(after, observation.Detail))
            return No("The exact literal multiplicity within the containing member changed.", before, after, candidate.Path);
        if (!before.Tokens.Select(token => token.Text).SequenceEqual(after.Tokens.Select(token => token.Text), StringComparer.Ordinal))
            return No("The containing member declaration or body changed; retained text alone is insufficient.", before, after, candidate.Path);
        return new(observation.Id, true, observation.Detail, observation.Detail, candidate.Path, after.Symbol,
            "Exact literal syntax, multiplicity and complete member tokens agree. Declaration only; no runtime or behavior claim.",
            before.Symbol, after.Symbol, Count(before, observation.Detail), Count(after, observation.Detail), Fingerprint(before), Fingerprint(after));
    }

    private sealed record Token(string Text, int Offset, int Line, bool IsString = false);
    private sealed record Member(string Symbol, string[] TypeContext, Token[] Tokens);
    private sealed record Source(Token[] Tokens, List<Member> Members);
    private static int Count(Member? member, string value) => member?.Tokens.Count(token => token.IsString && token.Text == value) ?? 0;
    private static string Fingerprint(Member? member) => member == null ? "" : Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Concat(member.Tokens.Select(token => token.Text.Length.ToString(CultureInfo.InvariantCulture) + ":" + token.Text)))));

    private static bool TryRead(string text, out Source source, out string problem)
    {
        source = new(Array.Empty<Token>(), new());
        if (!TryLex(text, out var tokens, out problem)) return false;
        var pairs = new Dictionary<int, int>();
        var stack = new Stack<int>();
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].Text;
            if (token is "(" or "[" or "{") stack.Push(i);
            else if (token is ")" or "]" or "}")
            {
                if (stack.Count == 0) { problem = "Unbalanced delimiters."; return false; }
                int opening = stack.Pop();
                if ((tokens[opening].Text, token) is not (("(", ")") or ("[", "]") or ("{", "}")))
                { problem = "Mismatched delimiters."; return false; }
                pairs[opening] = i;
            }
        }
        if (stack.Count != 0) { problem = "Unbalanced delimiters."; return false; }
        var reader = new Reader(tokens, pairs);
        if (!reader.Scope(0, tokens.Length, "", "", Array.Empty<string>(), false))
        { problem = reader.Problem; return false; }
        source = new(tokens, reader.Members);
        return true;
    }

    // This deliberately bounded reader recognizes declarations, not C# semantics. Complete member
    // token equality is required after parsing, so changing a branch or moving a literal is a nonmatch.
    private sealed class Reader(Token[] tokens, Dictionary<int, int> pairs)
    {
        internal List<Member> Members { get; } = new();
        internal string Problem { get; private set; } = "Unsupported declaration syntax.";
        private string Text(int start, int end) => string.Join(" ", tokens[start..end].Select(token => token.Text));
        private bool Fail(string message) { Problem = message; return false; }

        internal bool Scope(int start, int end, string ns, string owner, string[] contexts, bool inType)
        {
            for (int i = start; i < end;)
            {
                if (tokens[i].Text == ";") { i++; continue; }
                if (!inType && tokens[i].Text == "[" && i + 2 < end
                    && tokens[i + 1].Text is "assembly" or "module" && tokens[i + 2].Text == ":")
                { i = pairs[i] + 1; continue; }
                int first = i, boundary = -1, identityEnd = -1;
                bool initializer = false;
                for (; i < end; i++)
                {
                    string value = tokens[i].Text;
                    if (value is "(" or "[") { i = pairs[i]; continue; }
                    if (value is "=" or "=>") { initializer = true; if (identityEnd < 0) identityEnd = i; }
                    if (value == "{" && initializer) { i = pairs[i]; continue; }
                    if (value is "{" or ";") { boundary = i; break; }
                    if (value == "}") return Fail("Unexpected closing declaration brace.");
                }
                if (boundary < 0) return Fail("A declaration has no supported boundary.");
                int finish = tokens[boundary].Text == "{" ? pairs[boundary] + 1 : boundary + 1;
                if (!inType && tokens[first].Text == "namespace")
                {
                    string name = string.Concat(tokens[(first + 1)..boundary].Select(token => token.Text));
                    if (name.Length == 0) return Fail("Unnamed namespace.");
                    string nested = ns.Length == 0 ? name : ns + "." + name;
                    if (tokens[boundary].Text == ";")
                    {
                        if (ns.Length != 0) return Fail("Nested file-scoped namespace is unsupported.");
                        ns = nested; i = finish; continue;
                    }
                    if (!Scope(boundary + 1, finish - 1, nested, "", contexts, false)) return false;
                    i = finish; continue;
                }
                int type = -1;
                // Attribute arguments and method parameters cannot lend a declaration keyword.
                bool parametersSeen = false;
                for (int j = first; j < boundary; j++)
                {
                    if (tokens[j].Text == "[") { j = pairs[j]; continue; }
                    if (tokens[j].Text == "(") { parametersSeen = true; j = pairs[j]; continue; }
                    if (!parametersSeen && tokens[j].Text is "class" or "struct" or "interface" or "enum" or "record") { type = j; break; }
                }
                if (type >= 0 && !initializer)
                {
                    int nameAt = type + 1;
                    if (tokens[type].Text == "record" && nameAt < boundary && tokens[nameAt].Text is "class" or "struct") nameAt++;
                    if (nameAt >= boundary || !Identifier(tokens[nameAt].Text)) return Fail("Unsupported type name.");
                    string name = (owner.Length == 0 ? (ns.Length == 0 ? "" : ns + ".") : owner + "+") + tokens[nameAt].Text;
                    if (tokens[boundary].Text == "{" && tokens[type].Text != "enum"
                        && !Scope(boundary + 1, finish - 1, ns, name, contexts.Append(Text(first, boundary)).ToArray(), true)) return false;
                    i = finish; continue;
                }
                if (!inType)
                {
                    if (tokens[boundary].Text != ";" || tokens[first].Text is not ("using" or "extern" or "global" or "delegate"))
                        return Fail("Top-level statements or unsupported root declarations need another check.");
                    i = finish; continue;
                }
                if (tokens[boundary].Text == "{" && finish < end && tokens[finish].Text == "=")
                {
                    // Auto-property initializers are part of the same declaration.
                    int j = finish + 1;
                    for (; j < end && tokens[j].Text != ";"; j++)
                        if (tokens[j].Text is "(" or "[" or "{") j = pairs[j];
                    if (j == end) return Fail("Unterminated property initializer.");
                    finish = j + 1;
                }
                int headerEnd = identityEnd >= 0 ? identityEnd : boundary;
                if (headerEnd == first) return Fail("Unnamed member declaration.");
                Members.Add(new(owner + "::" + Text(first, headerEnd), contexts, tokens[first..finish]));
                i = finish;
            }
            return true;
        }
    }

    private static bool Identifier(string text) => text.Length > 0 && (char.IsLetter(text[0]) || text[0] is '_' or '@');
    private static readonly string[] Operators =
    {
        ">>>=", "<<=", ">>=", "??=", ">>>", "=>", "++", "--", "&&", "||", "??", "?.", "::", "==", "!=", "<=", ">=",
        "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "..", "->"
    };

    private static bool TryLex(string text, out Token[] result, out string problem)
    {
        var tokens = new List<Token>();
        int line = 1;
        problem = "";
        for (int i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i])) { if (text[i++] == '\n') line++; continue; }
            int start = i, oldLine = line;
            if (text[i] == '#' && text[..i].AsSpan(text.LastIndexOf('\n', Math.Max(0, i - 1)) + 1).Trim().Length == 0)
            {
                int end = text.IndexOf('\n', i); if (end < 0) end = text.Length;
                string directive = text[(i + 1)..end].TrimStart().Split(new[] { ' ', '\t', '\r' }, 2)[0];
                if (directive is not ("region" or "endregion" or "nullable" or "pragma" or "line"))
                { result = Array.Empty<Token>(); problem = "Conditional or unsupported preprocessor context."; return false; }
                i = end; continue;
            }
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
            { i = text.IndexOf('\n', i); if (i < 0) i = text.Length; continue; }
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) { result = Array.Empty<Token>(); problem = "Unterminated comment."; return false; }
                i = end + 2;
                line += text.AsSpan(start, i - start).Count('\n'); continue;
            }
            bool isString = StringPrefix(text, i, out int quote, out bool verbatim, out bool interpolated);
            if (isString || text[i] == '\'')
            {
                if (!(isString ? ReadString(text, ref i, quote, verbatim, interpolated) : ReadQuoted(text, ref i, '\'')))
                { result = Array.Empty<Token>(); problem = "Unsupported raw or unterminated literal."; return false; }
                line += text.AsSpan(start, i - start).Count('\n');
                tokens.Add(new(text[start..i], start, oldLine, isString)); continue;
            }
            if (char.IsLetter(text[i]) || text[i] is '_' or '@')
            {
                i++; while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
            }
            else if (char.IsDigit(text[i]))
            {
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'
                    || ((text[i] is '+' or '-') && (text[i - 1] is 'e' or 'E' or 'p' or 'P')))) i++;
            }
            else
            {
                string? op = Operators.FirstOrDefault(op => text.AsSpan(i).StartsWith(op, StringComparison.Ordinal));
                if (op != null) i += op.Length;
                else if ("{}[]();,.:?~!+-*/%&|^=<>".Contains(text[i])) i++;
                else { result = Array.Empty<Token>(); problem = "Unsupported source token."; return false; }
            }
            tokens.Add(new(text[start..i], start, oldLine));
        }
        result = tokens.ToArray(); return true;
    }

    private static bool StringPrefix(string text, int at, out int quote, out bool verbatim, out bool interpolated)
    {
        quote = at; verbatim = false; interpolated = false;
        if (text[at] == '"') return true;
        if (text[at] is not ('$' or '@')) return false;
        int next = at + 1;
        verbatim = text[at] == '@'; interpolated = text[at] == '$';
        if (next < text.Length && ((verbatim && text[next] == '$') || (interpolated && text[next] == '@')))
        { verbatim = true; interpolated = true; next++; }
        if (next >= text.Length || text[next] != '"') return false;
        quote = next; return true;
    }

    private static bool ReadQuoted(string text, ref int at, char delimiter)
    {
        at++;
        while (at < text.Length)
        {
            if (text[at] == '\\') { at += 2; continue; }
            if (text[at++] == delimiter) return true;
            if (text[at - 1] is '\r' or '\n') return false;
        }
        return false;
    }

    private static bool ReadString(string text, ref int at, int quote, bool verbatim, bool interpolated)
    {
        if (quote + 2 < text.Length && text[quote + 1] == '"' && text[quote + 2] == '"') return false;
        at = quote + 1;
        while (at < text.Length)
        {
            char c = text[at++];
            if (c == '"')
            {
                if (verbatim && at < text.Length && text[at] == '"') { at++; continue; }
                return true;
            }
            if (!verbatim && c == '\\') { at++; continue; }
            if (!verbatim && c is '\r' or '\n') return false;
            if (!interpolated || c != '{') continue;
            if (at < text.Length && text[at] == '{') { at++; continue; }
            int depth = 1;
            while (at < text.Length && depth > 0)
            {
                if (StringPrefix(text, at, out int nestedQuote, out bool nestedVerbatim, out bool nestedInterpolation))
                { if (!ReadString(text, ref at, nestedQuote, nestedVerbatim, nestedInterpolation)) return false; continue; }
                if (text[at] == '\'') { if (!ReadQuoted(text, ref at, '\'')) return false; continue; }
                if (text[at] == '{') depth++;
                if (text[at] == '}') depth--;
                at++;
            }
            if (depth != 0) return false;
        }
        return false;
    }
}
