using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.Evidence
{
    /// <summary>A protein carrying a family's learned marker domain that the proposal did not put in that family.</summary>
    public sealed class LeftOutRow
    {
        /// <summary>The family the marker points to.</summary>
        public string Family { get; init; } = string.Empty;
        public string Accession { get; init; } = string.Empty;
        public string Identifier { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public int Length { get; init; }
        public bool? Reviewed { get; init; }
        public bool Fragment { get; init; }
        public string MarkerAccession { get; init; } = string.Empty;
        public string MarkerName { get; init; } = string.Empty;

        /// <summary>The family the proposal gave it instead; empty when it gave none.</summary>
        public string AssignedFamily { get; init; } = string.Empty;
        public string Why { get; init; } = string.Empty;
    }

    /// <summary>
    /// The left-out list: for each family, the targets carrying one of its learned marker domains
    /// (<see cref="EvidenceProposal.Markers"/>, voted from the data) that the proposal did not assign to it,
    /// with the proposal's own reason. No domain or family is named here. Decoys and the database's
    /// contaminants are not candidates of any family and are not listed.
    /// </summary>
    public static class LeftOutList
    {
        public static List<LeftOutRow> Build(IReadOnlyList<FastaItemExtended> proteins, EvidenceProposal proposal)
        {
            var byIndex = proposal.Proteins.ToDictionary(p => p.ProteinIndex);
            var markers = proposal.Markers.ToDictionary(m => m.Accession, StringComparer.Ordinal);
            var rows = new List<(int Index, LeftOutRow Row)>();
            for (int index = 0; index < proteins.Count; index++)
            {
                FastaItemExtended protein = proteins[index];
                string identifier = protein.SequenceIdentifier ?? string.Empty;
                if (NearestReviewed.IsDecoy(protein) || identifier.StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.Ordinal)) continue;
                if (!byIndex.TryGetValue(index, out EvidenceProteinProposal? proposed) || protein.Evidence == null) continue;

                // The strongest marker of each family the protein carries.
                var carried = protein.Evidence.Pfam.Select(d => d.Accession).Distinct(StringComparer.Ordinal)
                    .Where(markers.ContainsKey).Select(a => markers[a])
                    .GroupBy(m => m.Family, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(m => m.VoteFraction).ThenByDescending(m => m.FamilyCarriers)
                        .ThenBy(m => m.Accession, StringComparer.Ordinal).First());
                foreach (EvidenceDomainMarker marker in carried)
                {
                    if (string.Equals(proposed.Family, marker.Family, StringComparison.OrdinalIgnoreCase)) continue;
                    rows.Add((index, new LeftOutRow
                    {
                        Family = marker.Family,
                        Accession = NearestReviewed.EntryAccession(identifier),
                        Identifier = identifier,
                        Description = protein.Description ?? string.Empty,
                        Length = (protein.Sequence ?? string.Empty).Length,
                        Reviewed = protein.Evidence.Reviewed,
                        Fragment = !MotifCheck.IsComplete(protein),
                        MarkerAccession = marker.Accession,
                        MarkerName = marker.Name,
                        AssignedFamily = proposed.Family,
                        Why = Why(proposed, marker),
                    }));
                }
            }
            return rows.OrderBy(r => r.Row.Family, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Row.Family, StringComparer.Ordinal)
                .ThenBy(r => r.Index).Select(r => r.Row).ToList();
        }

        private static string Why(EvidenceProteinProposal proposed, EvidenceDomainMarker marker)
        {
            string carries = "Carries " + marker.Name + ", which " + Count(marker.FamilyCarriers) + " of " + Count(marker.LabelledCarriers)
                + " labelled carriers in scope place in " + marker.Family + ". ";
            if (proposed.Family.Length > 0)
                return carries + "Assigned to " + proposed.Family + " instead: " + proposed.Reason;
            string scope = proposed.HasScopeAnnotation ? string.Empty : "Outside the scope words. ";
            string flags = proposed.Flags.Count == 0 ? string.Empty
                : " Flagged: " + string.Join(" ", proposed.Flags.Select(f => f.Message).Distinct(StringComparer.Ordinal));
            return carries + scope + "Not assigned: " + proposed.Reason + flags;
        }

        /// <summary>The list as CSV: RFC 4180, InvariantCulture, a leading formula character quoted out.</summary>
        public static string BuildCsv(IEnumerable<LeftOutRow> rows)
        {
            var csv = new StringBuilder("Family,Accession,Locus,Description,Length,Reviewed,Fragment,MarkerDomain,MarkerName,AssignedInstead,Why\r\n");
            foreach (LeftOutRow row in rows)
                csv.Append(string.Join(",", new[]
                {
                    row.Family, row.Accession, row.Identifier, row.Description, Count(row.Length),
                    row.Reviewed == null ? string.Empty : row.Reviewed.Value ? "yes" : "no", row.Fragment ? "yes" : "no",
                    row.MarkerAccession, row.MarkerName, row.AssignedFamily, row.Why,
                }.Select(Field))).Append("\r\n");
            return csv.ToString();
        }

        private static string Count(int value) => CsvFormatting.Number(value);

        private static string Field(string? value) => CsvFormatting.Guarded(value);
    }
}
