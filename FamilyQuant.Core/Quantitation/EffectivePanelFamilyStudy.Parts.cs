using FamilyQuant.Evidence.Positions;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public static partial class EffectivePanelFamilyStudy
{
    private static void BuildParts(EffectivePanelFamilyResult result, FamilyPeptideAttribution collapsed,
        CurationHomologyHits hits, FamilyClusterFile source, FamilyClusterFile grouped)
    {
        var bySlot = result.Families.ToDictionary(f => f.StudyFamilyId);
        bool subclassCurrent = SubclassAssignment.IsCurrent(source);
        if (subclassCurrent)
        {
            var candidates = new CompositionProteinCandidates(hits, source);
            var labels = source.ProteinSubclassLabels!.GroupBy(l => l.ProteinIndex).ToDictionary(g => g.Key, g => g.Count() == 1 ? g.Single() : null);
            result.SubclassParts = FamilyCompositionStudy.BuildSubclasses(collapsed, (side, sequence, id) =>
                bySlot[id].SourceUserIds.SelectMany(user => candidates.Resolve(side, sequence, user)).Select(candidate =>
                {
                    if (candidate.AmbiguousIdentifier || !labels.TryGetValue(candidate.Index, out var label) || label == null
                        || label.Identifier != candidate.Protein.SequenceIdentifier || label.FamilyUserId != candidate.Protein.UserID
                        || label.Status != ProteinSubclassStatus.Assigned || label.PanelFamilyKey != bySlot[id].PanelFamilyKey)
                        return null;
                    return label.SubclassKey;
                }).ToArray(), result.Policy);
            foreach (var family in result.SubclassParts.Families)
            {
                string? key = bySlot[family.FamilyUserId].PanelFamilyKey;
                var definition = source.FamilyPanel!.Families.FirstOrDefault(f => f.Key == key);
                foreach (var part in family.Parts.Where(p => p.PartKey != null))
                    part.Label = definition?.Subclasses.FirstOrDefault(s => s.Key == part.PartKey)?.DisplayName is { Length: > 0 } label ? label : part.Label;
            }
        }
        var positions = new QuantManifest();
        FamilyCompositionStudy.StorePositionsFromCuration(positions, collapsed, hits, grouped, result.Policy);
        result.DomainParts = positions.FamilyDomainParts;
        result.Localization = positions.FamilyPeptideLocalization;
        foreach (var slot in result.Families)
        {
            void Complete(List<FamilySignalBreakdown>? families, string label)
            {
                if (families != null && families.All(f => f.FamilyUserId != slot.StudyFamilyId))
                    families.Add(new() { FamilyUserId = slot.StudyFamilyId, Parts = new() { new() { IsUnresolved = true, Label = label } } });
            }
            Complete(result.SubclassParts?.Families, "Subclass not resolved");
            Complete(result.DomainParts?.Domains, "Position not resolved");
            Complete(result.DomainParts?.Products, "Position not resolved");
            if (result.Localization != null && result.Localization.Families.All(f => f.FamilyUserId != slot.StudyFamilyId))
                result.Localization.Families.Add(new() { FamilyUserId = slot.StudyFamilyId });
        }
        foreach (var slot in result.Families.Where(f => f.Kind == EffectivePanelFamilyKind.PanelFamily))
        {
            var definition = source.FamilyPanel!.Families.Single(f => f.Key == slot.PanelFamilyKey);
            var proteins = source.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier) && slot.SourceUserIds.Contains(p.UserID))
                .OrderBy(p => p.SequenceIdentifier, StringComparer.Ordinal).ToList();
            var classes = new Dictionary<string, string>(StringComparer.Ordinal);
            var definitions = definition.Subclasses.ToDictionary(s => s.Key, StringComparer.Ordinal);
            foreach (var original in definition.Subclasses)
            {
                var part = original; var visited = new HashSet<string>(StringComparer.Ordinal);
                while (part.ReportAsSubclassKey.Length > 0)
                {
                    if (!visited.Add(part.Key) || !definitions.TryGetValue(part.ReportAsSubclassKey, out var next))
                        throw new InvalidOperationException("A subclass reporting target is invalid.");
                    part = next;
                }
                classes[part.Key] = part.DisplayName.Length > 0 ? part.DisplayName : part.Key;
            }
            var domains = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in definition.Pfam) domains[part.Accession] = part.Name.Length > 0 ? part.Name : part.Accession;
            var products = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var protein in proteins)
            {
                foreach (var part in (protein.Evidence?.Positions?.Pfam ?? new()).OrderBy(p => p.Accession, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal))
                    domains.TryAdd(part.Accession, part.Name.Length > 0 ? part.Name : part.Accession);
                foreach (var part in protein.Evidence?.Positions?.Features ?? new())
                    products.TryAdd(FamilyCompositionStudy.FeaturePartKey(part), part.Type + (part.Description.Length > 0 ? ": " + part.Description : ""));
            }
            bool Verified(FastaItemExtended protein, bool product)
            {
                var evidence = protein.Evidence?.Positions;
                return evidence != null && evidence.SequenceLength == protein.Sequence.Length
                    && string.Equals(evidence.SequenceSha256, ProteinPositionFetcher.SequenceHash(protein.Sequence), StringComparison.OrdinalIgnoreCase)
                    && (product ? evidence.FeatureSource != null : evidence.DomainSource != null);
            }
            AddAvailability(result, slot, EffectivePanelPartKind.Subclass, classes,
                result.SubclassParts?.Families.Single(f => f.FamilyUserId == slot.StudyFamilyId), subclassCurrent);
            AddAvailability(result, slot, EffectivePanelPartKind.Domain, domains,
                result.DomainParts?.Domains.Single(f => f.FamilyUserId == slot.StudyFamilyId), proteins.Count > 0 && proteins.All(p => Verified(p, false)));
            AddAvailability(result, slot, EffectivePanelPartKind.Product, products,
                result.DomainParts?.Products.Single(f => f.FamilyUserId == slot.StudyFamilyId), proteins.Count > 0 && proteins.All(p => Verified(p, true)));
        }
    }

    private static void AddAvailability(EffectivePanelFamilyResult result, EffectivePanelFamilySlot slot, EffectivePanelPartKind kind,
        Dictionary<string, string> expected, FamilySignalBreakdown? actual, bool sourceAvailable)
    {
        var observed = actual?.Parts.ToDictionary(p => p.PartKey ?? string.Empty, StringComparer.Ordinal) ?? new();
        var keys = expected.Keys.Concat(observed.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (keys.Count == 0) keys.Add(string.Empty);
        foreach (string key in keys)
        {
            observed.TryGetValue(key, out var part);
            bool positive = part is { Signal: > 0 };
            bool unresolved = part?.IsUnresolved == true;
            var state = unresolved ? EffectivePanelPartState.NotResolved : positive ? EffectivePanelPartState.Observed
                : sourceAvailable ? EffectivePanelPartState.NotMeasured : EffectivePanelPartState.NotComputed;
            result.Parts.Add(new()
            {
                PanelFamilyKey = slot.PanelFamilyKey!, Kind = kind, PartKey = key.Length == 0 ? null : key,
                Label = part?.Label ?? expected.GetValueOrDefault(key, kind + " results"), State = state,
                Signal = positive || unresolved ? part!.Signal : null,
                ShareOfFamily = (positive || unresolved) && actual!.FamilySignal > 0 ? part!.ShareOfFamily : null,
                Reason = state switch
                {
                    EffectivePanelPartState.Observed => "Resolved credited peptide support in the stored pooled study.",
                    EffectivePanelPartState.NotResolved => positive ? "Credited peptide signal could not resolve this part." : "No pooled signal remains in the unresolved part.",
                    EffectivePanelPartState.NotMeasured => "No resolved credited peptide support for this named part under the stored mapping and policy.",
                    _ => "Required subclass or source position evidence is unavailable; no value was inferred.",
                },
            });
        }
    }
}
