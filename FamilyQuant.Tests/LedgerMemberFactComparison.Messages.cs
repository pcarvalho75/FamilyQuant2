using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class LedgerMemberFactComparison
{
    internal sealed record MessageToken(string Text, int Line, bool IsString);
    internal sealed record MessageFrame(string Member, MessageToken[] Text, MessageToken[] Caption, string MemberSha256);
    internal sealed record MessageValue(bool Supported, string Value, MessageToken[] Contributing, string Reason);
    internal sealed record DialogFrame(string Member, string DialogType, string Property, MessageToken[] Expression, string MemberSha256);

    internal static bool TryScopedMessageLiteral(FeatureLedger.Observation row, string source, string expectedMethod,
        out string memberName, out string memberHash)
    {
        memberName = memberHash = "";
        if (!TryRead(source, out var parsed, out _)) return false;
        var members = parsed.Members.Where(member => member.Symbol.StartsWith("FamilyQuant.MainWindow::", StringComparison.Ordinal)
            && member.Symbol.Contains(" " + expectedMethod + " (", StringComparison.Ordinal)).ToArray();
        if (members.Length != 1 || members[0].Tokens.Count(token => token.IsString && token.Line == row.Line && token.Text == row.Detail) != 1) return false;
        memberName = members[0].Symbol; memberHash = Fingerprint(members[0]); return true;
    }

    internal static bool TryDialogFrame(FeatureLedger.Observation row, string source, string expectedMethod,
        out DialogFrame? frame, out string reason)
    {
        frame = null;
        if (!TryRead(source, out var parsed, out reason)) return false;
        var members = parsed.Members.Where(member => member.Symbol.StartsWith("FamilyQuant.MainWindow::", StringComparison.Ordinal)
            && member.Symbol.Contains(" " + expectedMethod + " (", StringComparison.Ordinal)).ToArray();
        if (members.Length != 1) { reason = "No unique frozen handler for this dialog."; return false; }
        var member = members[0]; var tokens = member.Tokens; var found = new List<DialogFrame>();
        if (tokens.Count(token => token.IsString && token.Line == row.Line && token.Text == row.Detail) != 1)
        { reason = "The frozen row does not identify exactly one dialog literal token."; return false; }
        for (int index = 0; index + 2 < tokens.Length; index++)
        {
            if (tokens[index].Text != "new" || tokens[index + 1].Text is not ("OpenFileDialog" or "SaveFileDialog")
                || tokens[index + 2].Text != "{") continue;
            int depth = 1;
            for (int cursor = index + 3; cursor < tokens.Length && depth > 0; cursor++)
            {
                if (tokens[cursor].Text == "{") depth++;
                if (tokens[cursor].Text == "}") depth--;
                if (depth != 1 || cursor + 1 >= tokens.Length || tokens[cursor + 1].Text != "=") continue;
                string property = tokens[cursor].Text; var expression = new List<MessageToken>(); int nested = 0;
                for (int end = cursor + 2; end < tokens.Length; end++)
                {
                    var token = tokens[end];
                    if (!token.IsString)
                    {
                        if (nested == 0 && token.Text is "," or "}") break;
                        if (token.Text is "(" or "[" or "{") nested++;
                        if (token.Text is ")" or "]" or "}") nested--;
                    }
                    expression.Add(new(token.Text, token.Line, token.IsString));
                }
                if (expression.Any(token => token.IsString && token.Line == row.Line && token.Text == row.Detail))
                    found.Add(new(member.Symbol, tokens[index + 1].Text, property, expression.ToArray(), Fingerprint(member)));
            }
        }
        if (found.Count != 1) { reason = "No unique literal-bearing file-dialog property in this frozen handler."; return false; }
        frame = found[0]; reason = ""; return true;
    }

    internal static bool TryMessageFrame(FeatureLedger.Observation row, string source, string expectedMethod,
        out MessageFrame? frame, out bool caption, out string reason)
    {
        frame = null; caption = false;
        if (!TryRead(source, out var parsed, out reason)) return false;
        var owners = parsed.Members.Where(member => member.Symbol.StartsWith("FamilyQuant.MainWindow::", StringComparison.Ordinal)
            && member.Symbol.Contains(" " + expectedMethod + " (", StringComparison.Ordinal)).ToArray();
        if (owners.Length != 1) { reason = "The frozen handler member is absent or ambiguous."; return false; }
        var member = owners[0]; var tokens = member.Tokens;
        if (tokens.Count(token => token.IsString && token.Line == row.Line && token.Text == row.Detail) != 1)
        { reason = "The frozen row does not name exactly one literal in this handler."; return false; }
        var found = new List<(MessageFrame Frame, bool Caption)>();
        for (int index = 0; index + 3 < tokens.Length; index++)
        {
            if (tokens[index].Text != "MessageBox" || tokens[index + 1].Text != "."
                || tokens[index + 2].Text != "Show" || tokens[index + 3].Text != "(") continue;
            var arguments = new List<MessageToken[]>(); var argument = new List<MessageToken>();
            int parentheses = 1, brackets = 0, braces = 0;
            for (int cursor = index + 4; cursor < tokens.Length; cursor++)
            {
                var token = tokens[cursor];
                if (!token.IsString)
                {
                    if (token.Text == "(") parentheses++;
                    else if (token.Text == ")") parentheses--;
                    else if (token.Text == "[") brackets++;
                    else if (token.Text == "]") brackets--;
                    else if (token.Text == "{") braces++;
                    else if (token.Text == "}") braces--;
                    if (parentheses == 0) { arguments.Add(argument.ToArray()); break; }
                    if (token.Text == "," && parentheses == 1 && brackets == 0 && braces == 0)
                    { arguments.Add(argument.ToArray()); argument.Clear(); continue; }
                }
                argument.Add(new(token.Text, token.Line, token.IsString));
            }
            if (arguments.Count < 2) continue;
            bool IsRow(MessageToken token) => token.IsString && token.Line == row.Line && token.Text == row.Detail;
            var candidate = new MessageFrame(member.Symbol, arguments[0], arguments[1], Fingerprint(member));
            if (arguments[0].Any(IsRow)) found.Add((candidate, false));
            if (arguments[1].Any(IsRow)) found.Add((candidate, true));
        }
        if (found.Count != 1) { reason = "The literal has no unique first/second MessageBox argument in the frozen handler."; return false; }
        frame = found[0].Frame; caption = found[0].Caption; reason = ""; return true;
    }

    internal static MessageValue EvaluateMessage(MessageToken[] expression, IReadOnlyDictionary<string, string> bindings,
        IReadOnlyDictionary<string, bool>? branches = null)
    {
        var terms = new List<MessageToken[]>(); var term = new List<MessageToken>(); int depth = 0;
        foreach (var token in expression)
        {
            if (!token.IsString)
            {
                if (token.Text is "(" or "[" or "{") depth++;
                else if (token.Text is ")" or "]" or "}") depth--;
                if (token.Text == "+" && depth == 0) { terms.Add(term.ToArray()); term.Clear(); continue; }
            }
            term.Add(token);
        }
        terms.Add(term.ToArray());
        var value = new System.Text.StringBuilder(); var contributed = new List<MessageToken>();
        foreach (var part in terms)
        {
            string key = string.Concat(part.Select(token => token.Text));
            if (part.Length == 1 && part[0].IsString)
            {
                string literal = part[0].Text;
                try
                {
                    string? decoded = literal.StartsWith("@\"", StringComparison.Ordinal)
                        ? literal[2..^1].Replace("\"\"", "\"", StringComparison.Ordinal) : JsonSerializer.Deserialize<string>(literal);
                    if (decoded == null) return new(false, "", [], "Null is not a literal message string.");
                    value.Append(decoded); contributed.Add(part[0]);
                }
                catch (JsonException) { return new(false, "", [], "Unsupported C# literal escape or interpolation."); }
            }
            else if (bindings.TryGetValue(key, out string? actual)) value.Append(actual);
            else if (part.Length >= 5 && part[0].Text == "(" && part[^1].Text == ")")
            {
                int nested = 0, question = -1, colon = -1;
                for (int index = 1; index < part.Length - 1; index++)
                {
                    if (part[index].IsString) continue;
                    if (part[index].Text is "(" or "[" or "{") nested++;
                    if (part[index].Text is ")" or "]" or "}") nested--;
                    if (nested == 0 && part[index].Text == "?") { if (question >= 0) return new(false, "", [], "Nested conditional needs a separate native proof."); question = index; }
                    if (nested == 0 && part[index].Text == ":") { if (colon >= 0) return new(false, "", [], "Ambiguous conditional needs a separate native proof."); colon = index; }
                }
                string condition = question > 0 ? string.Concat(part[1..question].Select(token => token.Text)) : "";
                if (question < 0 || colon <= question || branches == null || !branches.TryGetValue(condition, out bool selected))
                    return new(false, "", [], "Conditional predicate was not recorded by the native case: " + key);
                var evaluated = EvaluateMessage(selected ? part[(question + 1)..colon] : part[(colon + 1)..^1], bindings, branches);
                if (!evaluated.Supported) return evaluated;
                value.Append(evaluated.Value); contributed.AddRange(evaluated.Contributing);
            }
            else if (key == "string.Empty") { }
            else return new(false, "", [], "Unexecuted or unsupported dynamic message term: " + key);
        }
        return new(true, value.ToString(), contributed.ToArray(), "");
    }
}
