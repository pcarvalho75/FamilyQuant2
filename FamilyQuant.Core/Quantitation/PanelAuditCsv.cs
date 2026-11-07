using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;

namespace FamilyQuant
{
    /// <summary>Actual family-assignment evidence and reason supplied by the assignment producer.</summary>
    public sealed class PanelFamilyAuditEvidence
    {
        public int ProteinIndex { get; set; }
        public string Identifier { get; set; } = string.Empty;
        public string EvidenceUsed { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>One row for every target, including unassigned proteins. Recorded evidence is not
    /// described as used unless the family-assignment producer supplies that fact.</summary>
    public static class PanelAuditCsv
    {
        public static string BuildCsv(FamilyClusterFile file) => BuildCsvCore(file, null);

        /// <summary>Exports explicitly supplied audit facts, after checking their target identities.</summary>
        public static string BuildCsv(FamilyClusterFile file, IReadOnlyList<PanelFamilyAuditEvidence> assignments)
        {
            ArgumentNullException.ThrowIfNull(assignments);
            return BuildCsvCore(file, assignments);
        }

        private static string BuildCsvCore(FamilyClusterFile file, IReadOnlyList<PanelFamilyAuditEvidence>? supplied)
        {
            ArgumentNullException.ThrowIfNull(file);
            bool subclassCurrent = SubclassAssignment.IsCurrent(file);
            var labels = (subclassCurrent ? file.ProteinSubclassLabels! : new List<ProteinSubclassLabel>()).ToDictionary(x => x.ProteinIndex);
            var currentAssignments = PanelAssignmentEvidenceStore.Current(file);
            var currentByIndex = currentAssignments?.ToDictionary(x => x.ProteinIndex) ?? new Dictionary<int, PanelProteinAssignment>();
            // Explain the current scope at export time rather than rewriting the saved recipe. Older
            // assignments therefore remain replayable and retain their original grouping decisions.
            var scope = file.EvidenceRecipe?.Options?.ScopeTerms;
            var resolver = currentAssignments != null && file.EvidenceRecipe?.PanelContext != null && scope is { Count: > 0 }
                ? PanelEvidenceClassifier.CreateCurrentResolver(file) : null;
            var conflicts = currentAssignments == null || file.FamilyPanel == null ? new HashSet<int>()
                : PanelAssignmentEvidenceStore.BuildFamilyMap(file.FamilyPanel, currentAssignments).ConflictingUserIds.ToHashSet();
            var audit = (supplied ?? currentAssignments?.Select(ToAuditEvidence).ToArray()
                ?? Array.Empty<PanelFamilyAuditEvidence>()).ToDictionary(x => x.ProteinIndex);
            foreach (var pair in labels.Select(x => (x.Key, x.Value.Identifier)).Concat(audit.Select(x => (x.Key, x.Value.Identifier))))
            {
                if (pair.Key < 0 || pair.Key >= file.MyExtendedFastaItems.Count
                    || file.MyExtendedFastaItems[pair.Key].SequenceIdentifier != pair.Identifier
                    || SequenceLabels.IsDecoy(pair.Identifier))
                    throw new InvalidOperationException("Protein audit data does not match the current target list.");
            }
            var csv = new StringBuilder();
            void Append(params string?[] cells) => csv.Append(string.Join(",", cells.Select(x => SpreadsheetSafeCsv.Encode(x)))).Append("\r\n");
            Append("Accession", "Protein name", "Panel family", "Subclass", "Family evidence used", "Family reason", "Subclass reason", "Evidence recorded");
            for (int i = 0; i < file.MyExtendedFastaItems.Count; i++)
            {
                var item = file.MyExtendedFastaItems[i];
                if (SequenceLabels.IsDecoy(item.SequenceIdentifier ?? string.Empty)) continue;
                labels.TryGetValue(i, out var label);
                audit.TryGetValue(i, out var assignment);
                currentByIndex.TryGetValue(i, out var currentFamily);
                if ((label != null && label.Identifier != item.SequenceIdentifier)
                    || (assignment != null && assignment.Identifier != item.SequenceIdentifier))
                    throw new InvalidOperationException("Protein audit data does not match the current protein list.");
                FamilyPanelFamily? family = null;
                string? familyKey = conflicts.Contains(item.UserID) ? null : label?.PanelFamilyKey
                    ?? (currentFamily?.IsApplied == true ? currentFamily.PanelFamilyKey : null);
                if (familyKey != null && file.FamilyPanel != null)
                    familyKey = FamilyPanelValidation.ResolveFamilyKey(file.FamilyPanel, familyKey);
                if (familyKey != null)
                    family = file.FamilyPanel?.Families.SingleOrDefault(x => x.Key == familyKey)
                        ?? throw new InvalidOperationException("A protein label refers to a missing panel family.");
                FamilyPanelSubclass? subclass = null;
                if (label?.SubclassKey != null)
                    subclass = family?.Subclasses.SingleOrDefault(x => x.Key == label.SubclassKey)
                        ?? throw new InvalidOperationException("A protein label refers to a missing panel subclass.");
                var evidence = item.Evidence;
                var recorded = new List<string>();
                if (!string.IsNullOrEmpty(evidence?.FamilyAnnotation)) recorded.Add("Family: " + evidence.FamilyAnnotation);
                if (!string.IsNullOrEmpty(evidence?.Subfamily)) recorded.Add("Subfamily: " + evidence.Subfamily);
                if (evidence?.Pfam != null && evidence.Pfam.Count > 0)
                    recorded.Add("Pfam: " + string.Join("; ", evidence.Pfam.Select(x => x.Accession)));
                string familyReason = assignment?.Reason ?? (currentAssignments == null ? "Family assignment evidence is unavailable or out of date." : "");
                if (resolver != null && assignment != null && evidence != null
                    && PanelEvidenceClassifier.SourceFamilies(evidence).Length > 0 && !EvidenceClassifier.IsInScope(evidence, scope!))
                {
                    string? ownFamily = resolver.PanelFamilyForAudit(evidence);
                    if (ownFamily != null)
                    {
                        string name = file.FamilyPanel!.Families.Single(f => f.Key == ownFamily).DisplayName;
                        if (string.IsNullOrWhiteSpace(name)) name = ownFamily;
                        familyReason = ExplainScopeExclusion(evidence, scope!, name, familyReason);
                    }
                }
                Append(FamilyNaming.Accession(item.SequenceIdentifier), ProteinNameFormatting.ForNewOutput(item.Description),
                    family?.DisplayName, subclass?.DisplayName, assignment?.EvidenceUsed,
                    familyReason,
                    conflicts.Contains(item.UserID) ? "The current User ID has conflicting panel family or outside members."
                        : label?.Reason ?? (subclassCurrent ? "" : "Subclass labels are unavailable or out of date."), string.Join(" | ", recorded));
            }
            return csv.ToString();
        }

        public static string ExplainScopeExclusion(Evidence.ProteinEvidence? evidence, IReadOnlyList<string> scope,
            string? mappedFamilyName, string reason)
        {
            if (EvidenceClassifier.IsInScope(evidence, scope)) return reason;
            string prefix = mappedFamilyName == null ? "The protein's keywords and tissue annotations do not match the user scope phrases: "
                : "The protein's own source family maps to panel family " + mappedFamilyName
                    + ", but its keywords and tissue annotations do not match the user scope phrases: ";
            return prefix + string.Join("; ", scope.Select(term => "\"" + term + "\"")) + ". " + reason;
        }

        private static PanelFamilyAuditEvidence ToAuditEvidence(PanelProteinAssignment assignment)
        {
            var parts = new List<string>();
            bool Has(PanelAssignmentEvidenceKind kind) => (assignment.EvidenceKind & kind) != 0;
            if (Has(PanelAssignmentEvidenceKind.UniProtFamily))
                parts.Add(assignment.SourceFamilies.Count == 0 ? "Source family annotation" : "Source families: " + string.Join("; ", assignment.SourceFamilies));
            if (Has(PanelAssignmentEvidenceKind.NearestReviewed))
            {
                var neighbour = new List<string>();
                if (!string.IsNullOrEmpty(assignment.NeighbourAccession)) neighbour.Add(assignment.NeighbourAccession);
                if (!Has(PanelAssignmentEvidenceKind.UniProtFamily) && assignment.SourceFamilies.Count > 0)
                    neighbour.Add("source families " + string.Join("; ", assignment.SourceFamilies));
                if (assignment.IdentityPercent.HasValue) neighbour.Add("identity " + assignment.IdentityPercent.Value.ToString("R", CultureInfo.InvariantCulture) + "%");
                if (assignment.QueryCoveragePercent.HasValue) neighbour.Add("query coverage " + assignment.QueryCoveragePercent.Value.ToString("R", CultureInfo.InvariantCulture) + "%");
                parts.Add(neighbour.Count == 0 ? "Reviewed neighbour" : "Reviewed neighbour: " + string.Join("; ", neighbour));
            }
            if (Has(PanelAssignmentEvidenceKind.DomainArchitecture))
            {
                var architecture = new List<string>();
                if (assignment.ObservedPfam.Count > 0) architecture.Add("observed " + string.Join("; ", assignment.ObservedPfam));
                if (assignment.ReferencePfam.Count > 0) architecture.Add("reference " + string.Join("; ", assignment.ReferencePfam));
                if (assignment.LinkKeys.Count > 0) architecture.Add("links " + string.Join("; ", assignment.LinkKeys));
                parts.Add(architecture.Count == 0 ? "Domain architecture" : "Domain architecture: " + string.Join("; ", architecture));
            }
            if (Has(PanelAssignmentEvidenceKind.ConfirmedLink)) parts.Add("Confirmed panel link");
            string evidence = string.Join(" | ", parts);
            if (!assignment.IsApplied && evidence.Length > 0) evidence = "Considered, not applied: " + evidence;
            return new PanelFamilyAuditEvidence { ProteinIndex = assignment.ProteinIndex, Identifier = assignment.Identifier,
                EvidenceUsed = evidence, Reason = assignment.Reason };
        }
    }
}

