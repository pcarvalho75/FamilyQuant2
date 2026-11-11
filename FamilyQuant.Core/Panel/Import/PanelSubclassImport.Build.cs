using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Panels.Import;

public static partial class PanelSubclassImport
{
    /// <summary>Check explicit selections against the current summary keys and return a complete detached set or item problems.</summary>
    public static PanelSubclassBuildResult BuildDefinitions(IReadOnlyList<PanelSubclassProposal> proposals,
        IReadOnlyList<PanelSubclassAcceptance> accepted, IReadOnlyCollection<string> validTargetFamilyKeys)
    {
        ArgumentNullException.ThrowIfNull(proposals); ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(validTargetFamilyKeys);
        var result = new PanelSubclassBuildResult();
        void Problem(string proposal, string rule, string message) => result.Problems.Add(new()
            { ProposalId = proposal, RuleId = rule, Message = message });
        var byId = new Dictionary<string, PanelSubclassProposal>(StringComparer.Ordinal);
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proposal in proposals)
        {
            if (proposal.ProposalId.Length == 0 || !byId.TryAdd(proposal.ProposalId, proposal))
                Problem(proposal.ProposalId, "", "Each proposed subclass needs a unique nonempty identifier; refresh the summary.");
            foreach (var rule in proposal.Rules)
                if (rule.RuleId.Length == 0 || !ruleIds.Add(rule.RuleId))
                    Problem(proposal.ProposalId, rule.RuleId, "Each proposed rule needs a unique nonempty identifier; refresh the summary.");
        }
        var targets = validTargetFamilyKeys.ToHashSet(StringComparer.Ordinal);
        var chosenIds = new HashSet<string>(StringComparer.Ordinal);
        var chosenKeys = new HashSet<(string Family, string Subclass)>();
        foreach (var selection in accepted)
        {
            if (!chosenIds.Add(selection.ProposalId)) Problem(selection.ProposalId, "", "This subclass selection is repeated; refresh its choices.");
            if (!byId.TryGetValue(selection.ProposalId, out var proposal))
            { Problem(selection.ProposalId, "", "This subclass proposal is no longer available; refresh the summary."); continue; }
            var available = proposal.Rules.GroupBy(rule => rule.RuleId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var chosenRules = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in selection.Rules)
            {
                if (!chosenRules.Add(choice.RuleId)) Problem(selection.ProposalId, choice.RuleId, "This rule selection is repeated.");
                if (!available.ContainsKey(choice.RuleId)) Problem(selection.ProposalId, choice.RuleId, "This rule is no longer in the proposal; refresh its choices.");
            }
            if (!selection.Accepted) continue;
            string family = selection.FamilyKey.Length > 0 ? selection.FamilyKey : proposal.FamilyKey;
            string key = selection.Key.Length > 0 ? selection.Key : proposal.Key;
            string name = selection.DisplayName.Length > 0 ? selection.DisplayName : proposal.DisplayName;
            if (string.IsNullOrWhiteSpace(family) || !targets.Contains(family)) Problem(selection.ProposalId, "", "Choose a family key shown in the current summary before accepting this subclass.");
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) Problem(selection.ProposalId, "", "An accepted subclass needs a nonempty key and display name.");
            if (!chosenKeys.Add((family, key))) Problem(selection.ProposalId, "", "Subclass keys must be unique within the selected family.");
            var definition = new FamilyPanelSubclass { Key = key, DisplayName = name,
                UniProtSubfamilies = proposal.UniProtSubfamilies.Order(StringComparer.Ordinal).ToList() };
            foreach (var field in proposal.Notes.Concat(SourceFields(proposal.SourceRows)))
                if (!definition.TextFields.Any(existing => existing.Name == field.Name && existing.Text == field.Text))
                    definition.TextFields.Add(new() { Name = field.Name, Text = field.Text });
            foreach (var choice in selection.Rules.Where(choice => choice.Accepted))
            {
                if (!available.TryGetValue(choice.RuleId, out var rule)) continue;
                var kind = choice.Kind ?? rule.Kind;
                if (!Enum.IsDefined(kind)) { Problem(selection.ProposalId, choice.RuleId, "Choose required or absent for this rule."); continue; }
                if (string.IsNullOrWhiteSpace(rule.Accession) || rule.CandidateAccessions.Count > 1
                    || rule.CandidateAccessions.Count > 0 && !rule.CandidateAccessions.Contains(rule.Accession, StringComparer.Ordinal))
                { Problem(selection.ProposalId, choice.RuleId, "This Pfam rule has no uniquely recognized accession; leave it unselected and review the source text."); continue; }
                var entries = kind == PanelSubclassRuleKind.RequiredPfam ? definition.RequiredPfam : definition.AbsentPfam;
                if (!entries.Contains(rule.Accession, StringComparer.Ordinal)) entries.Add(rule.Accession);
            }
            foreach (string accession in definition.RequiredPfam.Intersect(definition.AbsentPfam, StringComparer.Ordinal))
            {
                string rule = proposal.Rules.First(item => item.Accession == accession).RuleId;
                Problem(selection.ProposalId, rule, "The same Pfam accession cannot be both required and absent; review the selected rule polarities.");
            }
            definition.RequiredPfam.Sort(StringComparer.Ordinal); definition.AbsentPfam.Sort(StringComparer.Ordinal);
            result.Definitions.Add(new() { FamilyKey = family, Definition = definition });
        }
        if (result.Problems.Count > 0) result.Definitions.Clear();
        return result;
    }
}
