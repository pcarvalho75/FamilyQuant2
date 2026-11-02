using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.Evidence
{
    /// <summary>
    /// The exports of grouping by evidence: the audit CSV of what was applied, one row per protein placed,
    /// and the left-out CSV (<see cref="LeftOutList.BuildCsv"/>). The audit columns are those of the audit
    /// list delivered before 2.0, in the same order, with English headers. RFC 4180, InvariantCulture, a
    /// leading formula character quoted out.
    /// </summary>
    public static class EvidenceExports
    {
        public static readonly IReadOnlyList<string> AuditColumns = new[]
        {
            "Family", "Family number", "Previous family", "Accession", "Locus", "Description", "Reason",
            "Domains (Pfam)", "Length (aa)", "Cysteines", "Reviewed", "Fragment", "Annotated in scope", "UniProt family",
        };

        /// <summary>The proteins the stored recipe placed, by family number and then locus; empty with no recipe.</summary>
        public static string BuildAuditCsv(FamilyClusterFile file)
        {
            var csv = new StringBuilder(string.Join(",", AuditColumns.Select(Field)) + "\r\n");
            EvidenceRecipe? recipe = file.EvidenceRecipe;
            if (recipe == null) return csv.ToString();
            IReadOnlyList<string> scope = recipe.Options.ScopeTerms;
            foreach (EvidenceAssignment a in recipe.Assignments.OrderBy(a => a.FamilyId).ThenBy(a => a.Identifier, StringComparer.Ordinal))
            {
                if (a.ProteinIndex < 0 || a.ProteinIndex >= file.MyExtendedFastaItems.Count) continue;
                FastaItemExtended item = file.MyExtendedFastaItems[a.ProteinIndex];
                if (!string.Equals(item.SequenceIdentifier, a.Identifier, StringComparison.Ordinal)) continue;
                ProteinEvidence? evidence = item.Evidence;
                string sequence = item.Sequence ?? string.Empty;
                csv.Append(string.Join(",", new[]
                {
                    a.Name, Number(a.FamilyId), Number(a.PreviousFamilyId), NearestReviewed.EntryAccession(item.SequenceIdentifier),
                    item.SequenceIdentifier, item.Description, a.Reason, item.Domains, Number(sequence.Length),
                    Number(CoherenceCheck.Cysteines(sequence)), YesNo(evidence?.Reviewed), MotifCheck.IsComplete(item) ? "no" : "yes",
                    EvidenceClassifier.IsInScope(evidence, scope) ? "yes" : "no", evidence?.FamilyAnnotation ?? string.Empty,
                }.Select(Field))).Append("\r\n");
            }
            return csv.ToString();
        }

        private static string YesNo(bool? value) => value == null ? string.Empty : value.Value ? "yes" : "no";
        private static string Number(int value) => CsvFormatting.Number(value);

        private static string Field(string? value) => CsvFormatting.Guarded(value);
    }
}
