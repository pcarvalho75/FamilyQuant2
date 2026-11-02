using System;
using System.Linq;

namespace FamilyQuant.Evidence;

internal static class EvidenceDomains
{
    internal static string[] Ordered(ProteinEvidence? evidence) => evidence?.Pfam == null ? Array.Empty<string>()
        : evidence.Pfam.Select(domain => domain.Accession).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
}
