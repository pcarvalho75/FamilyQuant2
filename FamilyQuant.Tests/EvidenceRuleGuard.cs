using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests
{
    internal static class EvidenceRuleGuard
    {
        private static readonly Regex Codes = new Regex(@"\b(?:PF\d{5}|PTHR\d+(?::SF\d+)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ScopeWord = new Regex(@"venom", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Literals = new Regex("@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"", RegexOptions.CultureInvariant);

        internal static List<string> Violations(string text, IEnumerable<string> suppliedNames)
        {
            var violations = new List<string>();
            if (Codes.IsMatch(text)) violations.Add("biological accession literal");
            if (ScopeWord.IsMatch(text)) violations.Add("fixed scope word");
            var names = suppliedNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (Match literal in Literals.Matches(text))
            {
                string value = literal.Value;
                try
                {
                    value = value.StartsWith("@\"", StringComparison.Ordinal)
                        ? value.Substring(2, value.Length - 3).Replace("\"\"", "\"")
                        : Regex.Unescape(value.Substring(1, value.Length - 2));
                }
                catch (ArgumentException) { continue; }
                if (Codes.IsMatch(value)) violations.Add("encoded biological accession literal");
                if (ScopeWord.IsMatch(value)) violations.Add("encoded fixed scope word");
                if (names.Contains(value.Trim())) violations.Add("data-supplied family or domain name literal: " + value);
            }
            return violations.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
