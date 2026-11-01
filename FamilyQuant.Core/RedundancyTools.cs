using MAligner;
using FamilyQuant.Operations;
using PatternTools.FastaTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>
    /// Order-preserving FASTA redundancy removal, so users no longer need PatternLab's FastaDBGenerator
    /// just to drop subset / near-identical sequences. The original input order (e.g. alphabetical by
    /// protein name) is ALWAYS preserved, because USEARCH cluster_fast is order-sensitive and reordering
    /// the database changes how families are formed.
    /// </summary>
    public static class RedundancyTools
    {
        /// <summary>
        /// Removes redundant sequences from <paramref name="input"/> and writes the result to
        /// <paramref name="outputFile"/>, keeping the original order and headers.
        /// identity == 100 uses exact subset (substring) elimination natively; identity &lt; 100 delegates
        /// to MAligner's alignment-based identity and then restores the original order (so it is correct
        /// even with an older MAligner build that reorders internally). Returns (countBefore, countAfter).
        /// </summary>
        public static (int before, int after) RemoveRedundancy(FileInfo input, double identityPercent, string outputFile,
            OperationContext? operation = null)
        {
            var result = RemoveRedundancyDetailed(input, identityPercent, outputFile, operation);
            return (result.Before, result.After);
        }

        public static RedundancyResult RemoveRedundancyDetailed(FileInfo input, double identityPercent, string outputFile,
            OperationContext? operation = null)
        {
            operation?.Report("redundancy", "Reading protein sequences");
            var fp = new FastaFileParser() { PreserveHead = true };
            using (var reader = new StreamReader(input.FullName))
            {
                fp.ParseFile(reader, false);
            }
            fp.MyItems.RemoveAll(a => string.IsNullOrEmpty(a.Sequence));

            // Snapshot the original order up front; both paths below filter THIS list so the surviving
            // sequences come out in the order the user supplied.
            var originalOrder = fp.MyItems.ToList();
            int before = originalOrder.Count;

            if (identityPercent >= 100.0)
            {
                var eliminate = FindSubsetSequences(fp.MyItems, operation);
                fp.MyItems = originalOrder.Where(a => !eliminate.Contains(a)).ToList();
            }
            else
            {
                // Alignment-based identity lives in MAligner. It reorders MyItems internally by length,
                // so we recover the survivors and re-emit them in the original order.
                new Compress().CompressByIdentity(fp, identityPercent / 100.0);
                operation?.ThrowIfCancellationRequested();
                var survivors = new HashSet<FastaItem>(fp.MyItems);
                fp.MyItems = originalOrder.Where(a => survivors.Contains(a)).ToList();
            }

            operation?.Report("redundancy", "Preparing nonredundant sequences", fp.MyItems.Count, before);
            WriteFasta(outputFile, fp.MyItems, operation);
            return new RedundancyResult(before, fp.MyItems.Count, outputFile, identityPercent);
        }

        /// <summary>
        /// Returns the set of sequences that are exact subsets (substrings) of another sequence and should
        /// be dropped. Uses a length-sorted scan for efficiency (a sequence can only be a subset of a
        /// longer or equal one), but returns the actual items so the caller can filter while preserving the
        /// original order. The surviving superset gets the dropped entry's identifier appended to its
        /// description, matching PatternLab's FastaDBGenerator behaviour.
        /// </summary>
        private static HashSet<FastaItem> FindSubsetSequences(List<FastaItem> items, OperationContext? operation)
        {
            var byLength = items.OrderBy(a => a.Sequence.Length).ToList();
            var eliminate = new HashSet<FastaItem>();

            for (int i = 0; i < byLength.Count; i++)
            {
                operation?.Report("redundancy", "Comparing sequence subsets", i, byLength.Count);
                for (int j = i + 1; j < byLength.Count; j++)
                {
                    operation?.ThrowIfCancellationRequested();
                    if (byLength[j].Sequence.Contains(byLength[i].Sequence))
                    {
                        eliminate.Add(byLength[i]);
                        byLength[j].Description = byLength[j].Description + " : " +
                            byLength[i].SequenceIdentifier + " " + byLength[i].Description;
                        break;
                    }
                }
            }

            return eliminate;
        }

        private static void WriteFasta(string outputFile, List<FastaItem> items, OperationContext? operation)
        {
            string temp = outputFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var sw = new StreamWriter(temp))
                {
                    foreach (var it in items)
                    {
                        operation?.ThrowIfCancellationRequested();
                        sw.WriteLine(string.IsNullOrEmpty(it.Description)
                            ? ">" + it.SequenceIdentifier
                            : ">" + it.SequenceIdentifier + " " + it.Description);

                        string seq = it.Sequence;
                        for (int k = 0; k < seq.Length; k += 60)
                        {
                            sw.WriteLine(seq.Substring(k, Math.Min(60, seq.Length - k)));
                        }
                    }
                }
                operation?.Report("redundancy", "Publishing nonredundant sequences", items.Count, items.Count);
                void Publish() => File.Move(temp, outputFile, overwrite: true);
                if (operation == null) Publish();
                else operation.Commit(Publish);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }
}
