using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>
    /// Domain-column formatting and accession batching shared by the evidence fetch and its callers.
    /// </summary>
    public static class ProteinDomains
    {
        public const string Separator = "; ";

        private const int BatchSize = 500;

        /// <summary>The domain names of a stored value.</summary>
        public static List<string> Names(string? domains) =>
            (domains ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim()).Where(d => d.Length > 0).ToList();

        /// <summary>The distinct UniProt accessions of these proteins, in batches the service accepts. It
        /// refuses a whole request over one malformed accession, so only well-formed ones are asked.</summary>
        public static List<List<string>> Batches(IEnumerable<FastaItemExtended> items)
        {
            List<string> accessions = items.Select(i => FamilyNaming.Accession(i.SequenceIdentifier))
                .Where(FamilyNaming.IsUniProtAccession).Select(a => a.Split('-')[0])
                .Distinct(StringComparer.Ordinal).ToList();

            var batches = new List<List<string>>();
            for (int start = 0; start < accessions.Count; start += BatchSize)
                batches.Add(accessions.Skip(start).Take(BatchSize).ToList());
            return batches;
        }

    }
}
