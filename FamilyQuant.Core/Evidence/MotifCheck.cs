using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FamilyQuant.Evidence
{
    /// <summary>A motif the user typed, kept with the project so the same table can be made again.</summary>
    public sealed class MotifPattern
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// What the user typed. A regular expression over the residues ("GDSGGP", "[TSV]AAHC|GDSGGP",
        /// "CC..H.C"); "not " before one counts the proteins without it; "count C 12-16", "count C &gt;= 8" or
        /// "count C &lt;= 10" counts the proteins whose number of that residue is in the range.
        /// </summary>
        public string Pattern { get; set; } = string.Empty;
    }

    public sealed class MotifFamilyCount
    {
        public string Family { get; init; } = string.Empty;

        /// <summary>Members not known to be fragments: the proteins the motif is counted on.</summary>
        public int Complete { get; init; }
        public int Carriers { get; init; }
    }

    public sealed class MotifResult
    {
        public MotifPattern Motif { get; init; } = new MotifPattern();

        /// <summary>One row per family, in the order the families were given.</summary>
        public List<MotifFamilyCount> Families { get; init; } = new List<MotifFamilyCount>();
    }

    /// <summary>
    /// The motif check: for each family, how many of its complete members carry a motif the user typed.
    /// Only the sequence is read, never Pfam, and no motif is written in the program: the patterns are the
    /// user's, stored in the project (<see cref="FamilyClusterFile.Motifs"/>).
    /// </summary>
    public static class MotifCheck
    {
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);
        private static readonly Regex CountForm = new Regex(
            @"^count\s+([A-Za-z])\s+(?:(\d+)\s*-\s*(\d+)|>=\s*(\d+)|<=\s*(\d+))$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The test a pattern stands for, or an <see cref="ArgumentException"/> saying what is
        /// wrong with it, so a typing error is reported rather than counted as no carrier.</summary>
        public static Func<string, bool> Compile(string pattern)
        {
            string text = (pattern ?? string.Empty).Trim();
            if (text.Length == 0) throw new ArgumentException("Type a motif first.");

            Match count = CountForm.Match(text);
            if (count.Success)
            {
                char residue = char.ToUpperInvariant(count.Groups[1].Value[0]);
                int low = count.Groups[2].Success ? Number(count.Groups[2].Value)
                    : count.Groups[4].Success ? Number(count.Groups[4].Value) : 0;
                int high = count.Groups[3].Success ? Number(count.Groups[3].Value)
                    : count.Groups[5].Success ? Number(count.Groups[5].Value) : int.MaxValue;
                if (low > high) throw new ArgumentException("The range \"" + text + "\" is empty: its lower end is above its upper end.");
                return sequence =>
                {
                    int n = 0;
                    foreach (char c in sequence) if (char.ToUpperInvariant(c) == residue) n++;
                    return n >= low && n <= high;
                };
            }
            if (text.StartsWith("count", StringComparison.OrdinalIgnoreCase) && text.Length > 5 && char.IsWhiteSpace(text[5]))
                throw new ArgumentException("\"" + text + "\" is not a count. Write it as \"count C 12-16\", \"count C >= 8\" or \"count C <= 10\".");

            bool negate = false;
            if (text.Equals("not", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("\"not\" needs a motif after it.");
            if (text.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
            {
                negate = true;
                text = text.Substring(4).Trim();
                if (text.Length == 0) throw new ArgumentException("\"not\" needs a motif after it.");
            }
            Regex regex;
            try
            {
                regex = new Regex(text, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("\"" + text + "\" is not a motif the program can read: " + ex.Message, ex);
            }
            return sequence => regex.IsMatch(sequence) != negate;
        }

        /// <summary>A member counts unless its evidence or its description says it is a fragment.</summary>
        public static bool IsComplete(FastaItemExtended protein) =>
            protein.Evidence?.Fragment != true
            && (protein.Description ?? string.Empty).IndexOf("(Fragment", StringComparison.OrdinalIgnoreCase) < 0;

        /// <summary>Carriers per family among complete members. Families come out in the order they are
        /// first met; a protein listed twice under one family is counted once.</summary>
        public static MotifResult Count(MotifPattern motif, IEnumerable<(string Family, FastaItemExtended Protein)> members)
        {
            Func<string, bool> test = Compile(motif.Pattern);
            var order = new List<string>();
            var counted = new Dictionary<string, (HashSet<FastaItemExtended> Seen, int Complete, int Carriers)>(StringComparer.Ordinal);
            foreach ((string family, FastaItemExtended protein) in members)
            {
                if (!counted.TryGetValue(family, out var row))
                {
                    order.Add(family);
                    row = (new HashSet<FastaItemExtended>(ReferenceEqualityComparer.Instance), 0, 0);
                }
                if (row.Seen.Add(protein) && IsComplete(protein))
                {
                    row.Complete++;
                    if (test(protein.Sequence ?? string.Empty)) row.Carriers++;
                }
                counted[family] = row;
            }
            return new MotifResult
            {
                Motif = new MotifPattern { Name = motif.Name, Pattern = motif.Pattern },
                Families = order.Select(f => new MotifFamilyCount
                {
                    Family = f, Complete = counted[f].Complete, Carriers = counted[f].Carriers,
                }).ToList(),
            };
        }

        /// <summary>Every motif stored in the project against the same families.</summary>
        public static List<MotifResult> CountAll(FamilyClusterFile file, IReadOnlyList<(string Family, FastaItemExtended Protein)> members) =>
            (file.Motifs ?? new List<MotifPattern>()).Select(m => Count(m, members)).ToList();

        private static int Number(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        /// <summary>The motifs the user typed for the motif check, in their order; null until there is one,
        /// so a file without motifs is written exactly as before.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Evidence.MotifPattern>? Motifs { get; set; }
    }
}
