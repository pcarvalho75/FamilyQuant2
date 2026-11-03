using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        /// <summary>
        /// F5.6, the real homology run: every KEPT de novo peptide of the three runs (DeNovoOnly and
        /// CoIsolatedExtra verdicts, deduplicated - homology sees peptides, not spectra) searched
        /// against the venom target-decoy database and mapped onto its 70% identity families, the
        /// same clustering the rest of the program works with. The database and the family file are
        /// the SAME FASTA, which turns UnknownProteinIds into a hard invariant: any hit the family
        /// file does not know is a defect, not a configuration mismatch. Prints the mapped fraction,
        /// the families reached and the timing; asserts completeness, the invariants and determinism
        /// rather than any particular biology.
        /// </summary>
        private static void HomologyRealTests(string? peaksDir, string? desktopDir, string workDir)
        {
            string? sepr2 = desktopDir == null ? null : Path.Combine(desktopDir, "PLVI.sepr2");
            string? database = desktopDir == null ? null : Path.Combine(desktopDir,
                "uniprotkb_taxonomy_id_8724_2026_06_25_ORDENADO_CONT123_NO-Redundancy.T-R");
            if (peaksDir == null || sepr2 == null || !File.Exists(sepr2) || !File.Exists(database!))
            {
                Console.WriteLine("  SKIP  real homology - needs the PEAKS directory, PLVI.sepr2 and the venom T-R");
                skipped++;
                return;
            }

            FamilyClusterFile? loaded = EnsureVenomFcf(database!, workDir, out string fcfPath);
            if (loaded == null)
            {
                Console.WriteLine("  SKIP  real homology - USEARCH/clustering unavailable");
                skipped++;
                return;
            }

            FamilyClusterFile fcf = loaded;
            Check("the venom T-R clusters into a family per protein row (targets, decoys, contaminants)",
                fcf.MyExtendedFastaItems.Count > 500, fcf.MyExtendedFastaItems.Count.ToString("N0"));
            // Ties the DecoyLabel default to the actual data: if the T-R's decoy prefix ever changed,
            // every decoy would silently count as a target and the headline would inflate.
            Check("the family file really carries Reverse_ decoy rows for the suppression to act on",
                fcf.MyExtendedFastaItems.Any(i => i.SequenceIdentifier != null
                    && i.SequenceIdentifier.StartsWith("Reverse_", StringComparison.Ordinal)));

            var psms = FamilyQuant.DeNovo.SeproPsmAdapter.Load(sepr2);
            var criteria = new FamilyQuant.DeNovo.DeNovoFilterCriteria();
            var filtered = new List<FamilyQuant.DeNovo.PeaksDeNovoRow>();
            int rawDeNovoRows = 0;
            foreach (string csv in Directory.GetFiles(peaksDir, "*.denovoOnly.csv", SearchOption.AllDirectories))
            {
                var read = FamilyQuant.DeNovo.PeaksCsvReader.ReadDeNovoOnly(csv);
                rawDeNovoRows += read.Count;
                filtered.AddRange(FamilyQuant.DeNovo.DeNovoFilter.Apply(read, criteria));
            }

            var peptides = FamilyQuant.DeNovo.DeNovoReconciler.Reconcile(filtered, psms)
                .Where(r => r.Verdict == FamilyQuant.DeNovo.ReconciliationVerdict.DeNovoOnly
                         || r.Verdict == FamilyQuant.DeNovo.ReconciliationVerdict.CoIsolatedExtra)
                .Select(r => FamilyQuant.DeNovo.DeNovoReconciler.NormalizeSequence(r.DeNovo.Peptide))
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            Check("the kept rows collapse to a five-digit set of distinct peptides",
                peptides.Count > 10000, peptides.Count.ToString("N0"));

            var homologyDb = FamilyQuant.Homology.HomologyDatabase.Build(fcf.MyExtendedFastaItems);
            var started = DateTime.UtcNow;
            var results = FamilyQuant.Homology.HomologySearch.Search(peptides, homologyDb);
            double seconds = (DateTime.UtcNow - started).TotalSeconds;

            // Search answers one result per DISTINCT normalized query; equality here proves the
            // test's own input prep (NormalizeSequence then Distinct) is a fixed point of the
            // mapper's normalization, so the two lists join positionally.
            Check("the deduplicated input survives the mapper's own deduplication unchanged",
                results.Count == peptides.Count, $"{results.Count:N0} de {peptides.Count:N0}");
            int rejected = results.Count(r => r.IsRejected);
            Check("no peptide was rejected - the normalized alphabet is the standard twenty residues",
                rejected == 0, rejected.ToString());

            int unmatched = results.Count(r => !r.IsRejected && r.Hits.Count == 0);
            int decoyOnly = results.Count(r => r.Hits.Count > 0 && r.Hits.All(h => h.IsDecoy));
            int targetMapped = results.Count(r => r.Hits.Any(h => !h.IsDecoy));
            homologyTargetMappedCensus = targetMapped;
            Console.WriteLine($"        peptides {peptides.Count:N0} | mapped to targets {targetMapped:N0} " +
                $"({(double)targetMapped / peptides.Count * 100:F2}%) | decoy-only {decoyOnly:N0} " +
                $"({(double)decoyOnly / peptides.Count * 100:F2}%) | unmatched {unmatched:N0} | {seconds:F1} s");
            Check("the classes partition the results", rejected + unmatched + decoyOnly + targetMapped == results.Count);
            Check("some peptides map to the venom at 80% identity", targetMapped > 0, targetMapped.ToString("N0"));
            Check("target hits dominate decoy-only hits", targetMapped > decoyOnly,
                $"{targetMapped:N0} contra {decoyOnly:N0}");

            var assignments = FamilyQuant.Homology.HomologySearch.MapToFamilies(results, fcf.MyExtendedFastaItems);
            Check("one assignment per result, positionally joinable",
                assignments.Count == results.Count
                && assignments.Zip(results, (a, r) => a.Peptide == (r.IsRejected ? r.Peptide : r.NormalizedPeptide)).All(x => x));
            Check("every hit protein is known to the family file (database and family file are the same FASTA)",
                assignments.All(a => a.UnknownProteinIds.Count == 0),
                assignments.Sum(a => a.UnknownProteinIds.Count).ToString());

            // The headline table counts only target hits: a decoy family reached by a decoy-only
            // peptide is the control, not the biology. Through the ENGINE's own aggregation, which is
            // also the one the screen fills its table from: a census the suite computed for itself
            // would prove only that the suite's copy agreed with the suite's copy.
            var familyRows = FamilyQuant.Homology.HomologySearch.CountByFamily(
                results, fcf.MyExtendedFastaItems, new FamilyQuant.Homology.HomologyOptions().DecoyLabel);

            Console.WriteLine($"        familias alvo alcancadas: {familyRows.Count:N0}");
            foreach (var row in familyRows.Take(5))
            {
                string desc = row.Description;
                if (desc.Length > 60) desc = desc.Substring(0, 60);
                Console.WriteLine($"        familia {row.FamilyId,5}  {row.PeptideCount,6:N0} peptideos  {desc}");
            }
            Check("more than one family is reached", familyRows.Count > 1, familyRows.Count.ToString());
            // The very census the de novo SCREEN section pins, reached through the same engine method:
            // one aggregation, checked once, read by both.
            // 33 since the 1.2.0.0 exhaustive clustering (2026-08-21). The 47 pinned on 08-16 kept passing only
            // because the cached homology_venom70.fcf predated that change; it was rebuilt on 2026-10-28 and
            // unmodified fdcc42f then gave 33 as well, on both this and the screen check below.
            Check("and the family census is the 33 the screen section pins as well",
                familyRows.Count == 33, familyRows.Count.ToString());
            Check("the family counts arrive most peptides first, ties by family id",
                familyRows.Zip(familyRows.Skip(1), (a, b) =>
                    a.PeptideCount > b.PeptideCount
                    || (a.PeptideCount == b.PeptideCount && a.FamilyId < b.FamilyId)).All(x => x));
            Check("and no family is named after a decoy member",
                familyRows.All(r => !r.Description.StartsWith("Reverse_", StringComparison.Ordinal)));

            // Determinism: the parallel search must not let scheduling into the output. A subset is
            // re-searched and every hit list compared field by field.
            var subset = peptides.Take(300).ToList();
            string Fingerprint(List<FamilyQuant.Homology.HomologyResult> rs) => string.Join(";", rs.Select(r =>
                r.NormalizedPeptide + "=" + string.Join(",", r.Hits.Select(h =>
                    h.ProteinId + "@" + h.Offset.ToString(CultureInfo.InvariantCulture)
                    + ":" + h.Score.ToString(CultureInfo.InvariantCulture)
                    + ":" + h.IdentityFraction.ToString("R", CultureInfo.InvariantCulture)
                    + ":" + h.GateIdentityFraction.ToString("R", CultureInfo.InvariantCulture)
                    + ":" + h.MatchedSubsequence + ":" + (h.IsDecoy ? "D" : "T")))));
            var againA = FamilyQuant.Homology.HomologySearch.Search(subset, homologyDb);
            var againB = FamilyQuant.Homology.HomologySearch.Search(subset, homologyDb);
            Check("two searches of the same subset are hit-for-hit identical",
                Fingerprint(againA) == Fingerprint(againB));
            Check("and identical to the full run's answers for those peptides",
                Fingerprint(againA) == Fingerprint(results.Take(300).ToList()));
        }

        /// <summary>
        /// The venom family file at 70% identity, generated once and reused by every section that
        /// needs it.
        ///
        /// The clustering is deterministic for a fixed FASTA and identity, so it is cached. The cache
        /// is discarded when the T-R is newer than it (an edited database must not keep answering from
        /// the old clustering), written to a temp name first so a crash mid-save cannot leave a torn
        /// file behind, and a load failure regenerates instead of failing every future run. USEARCH is
        /// an external process, so its failure returns null and the caller SKIPS, never the death of
        /// the sections after it.
        /// </summary>
        private static FamilyClusterFile? EnsureVenomFcf(string database, string workDir, out string fcfPath)
        {
            fcfPath = Path.Combine(workDir, "homology_venom70.fcf");

            try
            {
                if (File.Exists(fcfPath)
                    && File.GetLastWriteTimeUtc(fcfPath) < File.GetLastWriteTimeUtc(database))
                    File.Delete(fcfPath);

                if (!File.Exists(fcfPath))
                {
                    string tmp = fcfPath + ".tmp";
                    USearchTools.GenerateFCF(new FileInfo(database), 70).Save(tmp);
                    File.Move(tmp, fcfPath);
                }

                try
                {
                    return FamilyClusterFile.Load(fcfPath);
                }
                catch
                {
                    File.Delete(fcfPath);
                    string tmp = fcfPath + ".tmp";
                    USearchTools.GenerateFCF(new FileInfo(database), 70).Save(tmp);
                    File.Move(tmp, fcfPath);
                    return FamilyClusterFile.Load(fcfPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("        the venom family file could not be produced: " + ex.Message);
                return null;
            }
        }
    }
}
