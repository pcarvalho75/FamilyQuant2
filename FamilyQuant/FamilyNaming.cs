using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FamilyQuant.Homology;

namespace FamilyQuant
{
    /// <summary>
    /// The name a family is reported under: the Full XIC composition CSV, the Analytics family chart, the
    /// evidence pack, the spectrum table's family labels and the de novo screen's family table.
    ///
    /// It used to be the description of the family's first protein in file order, which named a family of
    /// 43 metalloproteinases with 17 different descriptions after whichever came first, and ignored the
    /// label the user typed (Ana, 2026-10-25). Now:
    ///   1. the User Annotation, when any member carries one (the most frequent, if they differ);
    ///   2. otherwise the most frequent protein name among the members, read before " OS=" and with a
    ///      trailing "(Fragment)" ignored, reported as the full description of a member bearing it
    ///      (a complete entry before a fragment);
    ///   3. otherwise an empty string.
    /// Every tie goes to the one met first in the family's member order, which the curation keeps stable
    /// (its only re-sort is a stable sort by User ID), so the name never depends on the grid's sort.
    /// </summary>
    public static class FamilyNaming
    {
        /// <summary>One name per family of the given proteins; the caller removes the decoys first.</summary>
        public static Dictionary<int, string> Names(IEnumerable<FastaItemExtended?> targets)
        {
            var names = new Dictionary<int, string>();
            foreach (IGrouping<int, FastaItemExtended> family in (targets ?? Enumerable.Empty<FastaItemExtended?>())
                         .Where(i => i != null).Select(i => i!).GroupBy(i => i.UserID))
            {
                names[family.Key] = Name(family);
            }
            return names;
        }

        /// <summary>The name of one family, from its members in their stored order.</summary>
        public static string Name(IEnumerable<FastaItemExtended?> members)
        {
            List<FastaItemExtended> list = (members ?? Enumerable.Empty<FastaItemExtended?>())
                .Where(i => i != null).Select(i => i!).ToList();

            string? annotation = MostFrequent(list
                .Select(i => (i.UserAnnotation ?? string.Empty).Trim())
                .Where(a => a.Length > 0)
                .Select(a => (Key: a, Item: a)))?.First();
            if (annotation != null) return annotation;

            List<FastaItemExtended>? bearers = MostFrequent(list
                .Select(i => (Key: ProteinName(i.Description), Item: i))
                .Where(x => x.Key.Length > 0));
            if (bearers == null) return string.Empty;

            FastaItemExtended representative = bearers.FirstOrDefault(i => !IsFragment(i.Description)) ?? bearers[0];
            return representative.Description ?? string.Empty;
        }

        /// <summary>The protein name a description is counted under: the text before " OS=", trimmed, with a
        /// trailing "(Fragment)" or "(Fragments)" removed. Compared case-insensitively.</summary>
        public static string ProteinName(string? description)
        {
            string name = (description ?? string.Empty).Trim();
            int os = name.IndexOf(" OS=", StringComparison.OrdinalIgnoreCase);
            if (os >= 0) name = name.Substring(0, os).Trim();
            foreach (string suffix in new[] { "(Fragments)", "(Fragment)" })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length).Trim();
                    break;
                }
            }
            return name;
        }

        /// <summary>UniProt's published accession format, with an isoform suffix allowed.</summary>
        private static readonly Regex UniProtAccession = new Regex(
            @"^([OPQ][0-9][A-Z0-9]{3}[0-9]|[A-NR-Z][0-9]([A-Z][A-Z0-9]{2}[0-9]){1,2})(-\d+)?$", RegexOptions.Compiled);

        private const string ContaminantLabel = "contaminant_";

        /// <summary>The code a protein is looked up by: P31982 from "sp|P31982|VM2I_CROCC", and P05784 from
        /// "contaminant_P05784|K1C18_MOUSE". An identifier holding no UniProt accession comes back whole,
        /// because it is still the code of its own database. A decoy exists in no database and has none.</summary>
        public static string Accession(string? identifier)
        {
            string id = (identifier ?? string.Empty).Trim();
            if (id.StartsWith(HomologyOptions.DefaultDecoyLabel, StringComparison.Ordinal)) return string.Empty;

            foreach (string field in id.Split('|'))
            {
                string code = field.StartsWith(ContaminantLabel, StringComparison.Ordinal)
                    ? field.Substring(ContaminantLabel.Length) : field;
                if (UniProtAccession.IsMatch(code)) return code;
            }
            return id;
        }

        /// <summary>The UniProt page of a code from <see cref="Accession"/>, or empty when the code is not a
        /// UniProt accession. An isoform opens the page of its protein.</summary>
        public static string UniProtUrl(string? code)
        {
            string text = (code ?? string.Empty).Trim();
            if (!IsUniProtAccession(text)) return string.Empty;
            return "https://www.uniprot.org/uniprotkb/" + text.Split('-')[0] + "/entry";
        }

        public static bool IsUniProtAccession(string? code) => UniProtAccession.IsMatch(code ?? string.Empty);

        private static bool IsFragment(string? description)
        {
            string name = (description ?? string.Empty).Trim();
            int os = name.IndexOf(" OS=", StringComparison.OrdinalIgnoreCase);
            if (os >= 0) name = name.Substring(0, os).Trim();
            return name.EndsWith("(Fragment)", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("(Fragments)", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The items of the most frequent key (case-insensitive), in their order; a tie goes to the
        /// key met first. Null when there is no key at all.</summary>
        private static List<T>? MostFrequent<T>(IEnumerable<(string Key, T Item)> keyed)
        {
            var groups = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach ((string key, T item) in keyed)
            {
                if (!groups.TryGetValue(key, out List<T>? items))
                {
                    groups[key] = items = new List<T>();
                    order.Add(key);
                }
                items.Add(item);
            }

            List<T>? best = null;
            foreach (string key in order)
                if (best == null || groups[key].Count > best.Count) best = groups[key];
            return best;
        }
    }
}
