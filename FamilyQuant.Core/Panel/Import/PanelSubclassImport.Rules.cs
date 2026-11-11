using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyQuant.Panels.Import;

public static partial class PanelSubclassImport
{
    private sealed class SubclassVocabulary
    {
        private readonly Dictionary<string, FamilyPanelDomain[]> aliases;
        private readonly Regex? tokens;
        // Short prepositions are cell-prefix markers only: in a header, Portuguese "no" can mean "in the".
        // Headers use explicit absent/absente/absents/absentes or ausente/ausentes adjectives and a leading minus.
        // Required header labels use required, exigido(a)(s) or requerido(a)(s); a leading plus is required.
        // Cell markers take precedence. Header-only polarity remains uncertain and never confirms a rule.
        private const string HeaderAbsenceWords = @"absente?s?|ausentes?";
        private const string CellAbsenceWords = @"without|no|not|sem|sin|sans|ohne|" + HeaderAbsenceWords;
        private const string RequiredHeaderWords = @"required|exigid[oa]s?|requerid[oa]s?";
        private static readonly Regex AbsentPrefix = new(@"(?:^|[\s(,;|])(?<marker>" + CellAbsenceWords + @")\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex MinusPrefix = new(@"(?:^|[\s(,;|])(?<marker>-)\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex PlusPrefix = new(@"(?:^|[\s(,;|])(?<marker>\+)\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex AbsentHeader = new(@"(?<![\p{L}\p{N}_])(?:" + HeaderAbsenceWords + @")(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RequiredHeader = new(@"(?<![\p{L}\p{N}_])(?:" + RequiredHeaderWords + @")(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal SubclassVocabulary(IReadOnlyList<FamilyPanelDomain> vocabulary)
        {
            var pairs = vocabulary.Where(domain => !string.IsNullOrWhiteSpace(domain.Accession))
                .SelectMany(domain => new[] { domain.Accession, domain.Name }.Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Select(alias => (Alias: alias.Trim(), Domain: domain)));
            aliases = pairs.GroupBy(pair => pair.Alias, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key,
                group => group.Select(pair => pair.Domain).GroupBy(domain => domain.Accession, StringComparer.Ordinal)
                    .Select(domains => new FamilyPanelDomain { Accession = domains.Key, Name = domains.First().Name }).ToArray(), StringComparer.OrdinalIgnoreCase);
            if (aliases.Count > 0) tokens = new Regex(@"(?<![\p{L}\p{N}_])(?:" + string.Join("|", aliases.Keys.OrderByDescending(alias => alias.Length)
                .ThenBy(alias => alias, StringComparer.Ordinal).Select(Regex.Escape)) + @")(?![\p{L}\p{N}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }

        internal (List<PanelSubclassRuleProposal> Rules, bool UnreadText) Read(string text, string column, PanelSubclassSourceRow source)
        {
            var rules = new List<PanelSubclassRuleProposal>();
            if (string.IsNullOrWhiteSpace(text)) return (rules, false);
            if (tokens == null) return (rules, true);
            Match[] matches;
            try { matches = tokens.Matches(text).Cast<Match>().ToArray(); }
            catch (RegexMatchTimeoutException) { return (rules, true); }
            bool headerAbsent = AbsentHeader.IsMatch(column) || Regex.IsMatch(column, @"^\s*-", RegexOptions.CultureInvariant);
            bool headerRequired = RequiredHeader.IsMatch(column) || Regex.IsMatch(column, @"^\s*\+", RegexOptions.CultureInvariant);
            PanelSubclassRuleKind? headerKind = headerAbsent ? PanelSubclassRuleKind.AbsentPfam : headerRequired ? PanelSubclassRuleKind.RequiredPfam : null;
            char[] remainder = text.ToCharArray();
            void Erase(int start, int length) { for (int index = start; index < start + length; index++) remainder[index] = ' '; }
            for (int index = 0; index < matches.Length; index++)
            {
                var match = matches[index];
                FamilyPanelDomain[] domains = aliases[match.Value];
                Match absent = AbsentPrefix.Match(text[..match.Index]);
                if (!absent.Success) absent = MinusPrefix.Match(text[..match.Index]);
                Match required = PlusPrefix.Match(text[..match.Index]);
                Match prefix = absent.Success ? absent : required;
                PanelSubclassRuleKind? cellKind = absent.Success ? PanelSubclassRuleKind.AbsentPfam : required.Success ? PanelSubclassRuleKind.RequiredPfam : null;
                var kind = cellKind ?? headerKind ?? PanelSubclassRuleKind.RequiredPfam;
                bool headerDerived = !cellKind.HasValue && headerKind.HasValue
                    && column != PanelImportTemplate.SubclassRequiredPfam && column != PanelImportTemplate.SubclassAbsentPfam;
                bool polarityConflict = headerAbsent && headerRequired || headerKind.HasValue && cellKind.HasValue && headerKind != cellKind;
                if (prefix.Success) Erase(prefix.Index, prefix.Length);
                Erase(match.Index, match.Length);
                // A uniquely identified name followed by its parenthesized accession is one token.
                // Its accession inherits the name's polarity instead of proposing a conflicting second rule.
                if (domains.Length == 1 && index + 1 < matches.Length)
                {
                    var next = matches[index + 1];
                    var nextDomains = aliases[next.Value];
                    string gap = text[(match.Index + match.Length)..next.Index];
                    var closing = Regex.Match(text[(next.Index + next.Length)..], @"^\s*\)", RegexOptions.CultureInvariant);
                    if (nextDomains.Length == 1 && nextDomains[0].Accession == domains[0].Accession
                        && string.Equals(next.Value, domains[0].Accession, StringComparison.OrdinalIgnoreCase)
                        && Regex.IsMatch(gap, @"^\s*\(\s*$", RegexOptions.CultureInvariant) && closing.Success)
                    {
                        Erase(match.Index + match.Length, next.Index + next.Length + closing.Length - match.Index - match.Length);
                        index++;
                    }
                }
                string accession = domains.Length == 1 ? domains[0].Accession : "";
                string reason = domains.Length != 1 ? "This Pfam name matches more than one accession; leave the unresolved rule unselected."
                    : headerDerived ? "The column header suggests this recognized Pfam domain is " + (kind == PanelSubclassRuleKind.AbsentPfam ? "absent" : "required") + "; this inferred polarity is uncertain and needs review."
                    : kind == PanelSubclassRuleKind.AbsentPfam ? "The source marks this recognized Pfam domain as absent; confirmation is still required."
                    : "The source lists this recognized Pfam domain as required; confirmation is still required.";
                if (headerAbsent && headerRequired) reason += " The column header contains both required and absent markers; choose the polarity before selecting this rule.";
                else if (polarityConflict) reason += " The explicit cell marker takes precedence over the column header; review their disagreement before selecting this rule.";
                rules.Add(new PanelSubclassRuleProposal
                {
                    Accession = accession, Name = domains.Length == 1 ? domains[0].Name.Length > 0 ? domains[0].Name : accession : match.Value,
                    CandidateAccessions = domains.Select(domain => domain.Accession).Order(StringComparer.Ordinal).ToList(),
                    Kind = kind, SourceRows = new() { CopyRow(source) }, IsUncertain = domains.Length != 1 || polarityConflict || headerDerived,
                    Reason = reason,
                });
            }
            bool unread = Regex.IsMatch(new string(remainder), @"[\p{L}\p{N}]", RegexOptions.CultureInvariant);
            return (rules, unread);
        }
    }
}
