using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// Sensitivity study of the IDENTIFIED-peptide family attribution rule on real data: every target
    /// protein past the identity gate (the v2 rule) against only the proteins tied at the best score.
    /// It also checks that "tied at the best score" is the same set as the proteins that contain the
    /// peptide (I/L folded), which is the claim the best-score rule rests on. Reads a .fqproj for the
    /// quant and optionally a separate .fcf for the curation; nothing is written back.
    /// </summary>
    internal static class AttributionRuleStudy
    {
        public const string Argument = "--attribution-rule-study";

        public static void Run(Action<string, bool, string?> check, string? fqprojPath, string? fcfPath,
                               string? outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(fqprojPath) || !File.Exists(fqprojPath))
            {
                check("attribution study: the .fqproj was given (positional argument 3)", false, fqprojPath);
                return;
            }

            var (embedded, quant, _) = ProjectStore.ReadFqproj(fqprojPath);
            var curations = new List<(string Name, FamilyClusterFile Curation)>();
            if (embedded?.MyExtendedFastaItems != null && embedded.MyExtendedFastaItems.Count > 0)
                curations.Add(("project curation", embedded));
            if (!string.IsNullOrWhiteSpace(fcfPath) && fcfPath != "." && File.Exists(fcfPath))
                curations.Add((Path.GetFileName(fcfPath), FamilyClusterFile.Load(fcfPath)));
            check("attribution study: at least one curation to search", curations.Count > 0,
                curations.Count.ToString(CultureInfo.InvariantCulture));

            string outDir = string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(fqprojPath))!
                : Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outDir);

            List<string> peptides = quant
                .Where(r => r != null && string.Equals(r.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal))
                .Select(r => DeNovoReconciler.NormalizeSequence(r.Sequence ?? string.Empty))
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Console.WriteLine("        identified distinct sequences: " + peptides.Count.ToString("N0", CultureInfo.InvariantCulture));

            foreach (var (name, curation) in curations)
                RunOne(check, name, curation, quant, peptides, outDir);
        }

        private static void RunOne(Action<string, bool, string?> check, string name, FamilyClusterFile curation,
                                   List<QuantRow> quant, List<string> peptides, string outDir)
        {
            Console.WriteLine();
            Console.WriteLine("        ---- curation: " + name + " (" +
                curation.MyExtendedFastaItems.Count.ToString(CultureInfo.InvariantCulture) + " entries) ----");

            PeptideFamilyAttribution all = FamilyEnvelopeQuant.BuildAttributionFromCuration(quant, curation, null, false);
            PeptideFamilyAttribution best = FamilyEnvelopeQuant.BuildAttributionFromCuration(quant, curation, null, true);

            const string decoy = "Reverse_";
            static string Fold(string s) => (s ?? string.Empty).ToUpperInvariant().Replace('I', 'L');
            var targets = curation.MyExtendedFastaItems
                .Where(i => i != null && !(i.SequenceIdentifier ?? string.Empty).StartsWith(decoy, StringComparison.Ordinal))
                .Select(i => (Seq: Fold(i.Sequence ?? string.Empty), i.UserID)).ToList();
            var decoys = curation.MyExtendedFastaItems
                .Where(i => i != null && (i.SequenceIdentifier ?? string.Empty).StartsWith(decoy, StringComparison.Ordinal))
                .Select(i => Fold(i.Sequence ?? string.Empty)).ToList();

            IReadOnlyList<int> Get(PeptideFamilyAttribution a, string p) =>
                a.IdentifiedBySequence.TryGetValue(p, out IReadOnlyList<int>? v) ? v : Array.Empty<int>();

            int attributedAll = 0, attributedBest = 0, sharedAll = 0, sharedBest = 0, changed = 0,
                sharedToUnique = 0, fewerFamilies = 0, notSubset = 0;
            int containAgree = 0, containDecoyTie = 0, containButNoBest = 0, bestButNotContained = 0,
                containDiffer = 0, allAddsHomologOnly = 0;
            var examples = new List<string>();

            foreach (string p in peptides)
            {
                var fa = new HashSet<int>(Get(all, p));
                var fb = new HashSet<int>(Get(best, p));
                if (fa.Count > 0) attributedAll++;
                if (fb.Count > 0) attributedBest++;
                if (fa.Count > 1) sharedAll++;
                if (fb.Count > 1) sharedBest++;
                if (!fb.IsSubsetOf(fa)) notSubset++;
                if (!fa.SetEquals(fb))
                {
                    changed++;
                    if (fa.Count > 1 && fb.Count == 1) sharedToUnique++;
                    else if (fb.Count > 1 && fb.Count < fa.Count) fewerFamilies++;
                }

                var contain = new HashSet<int>(targets.Where(t => t.Seq.Contains(p, StringComparison.Ordinal)).Select(t => t.UserID));
                bool decoyContains = decoys.Any(d => d.Contains(p, StringComparison.Ordinal));
                if (contain.Count > 0 && decoyContains) { containDecoyTie++; continue; }
                if (contain.SetEquals(fb)) containAgree++;
                else if (contain.Count > 0 && fb.Count == 0) containButNoBest++;
                else if (contain.Count == 0 && fb.Count > 0) bestButNotContained++;
                else
                {
                    containDiffer++;
                    if (examples.Count < 5)
                        examples.Add(p + " contain=[" + string.Join(",", contain.OrderBy(x => x)) + "] best=[" +
                                     string.Join(",", fb.OrderBy(x => x)) + "]");
                }
                if (contain.Count > 0 && fa.Except(contain).Any()) allAddsHomologOnly++;
            }

            string N(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
            Console.WriteLine("        attributed to >= 1 family: all-past-gate " + N(attributedAll) + ", best-score " + N(attributedBest));
            Console.WriteLine("        shared (>= 2 families):    all-past-gate " + N(sharedAll) + ", best-score " + N(sharedBest));
            Console.WriteLine("        family set changed: " + N(changed) + " (shared -> unique " + N(sharedToUnique) +
                              ", still shared but fewer families " + N(fewerFamilies) + ")");
            Console.WriteLine("        contained in a target protein, and the old rule ALSO credits a family that does not contain it: " +
                              N(allAddsHomologOnly));
            Console.WriteLine("        best-score vs containment: agree " + N(containAgree) + ", target+decoy both contain (decoy wins) " +
                              N(containDecoyTie) + ", contained but no best-score family " + N(containButNoBest) +
                              ", best-score family but not contained (homolog only) " + N(bestButNotContained) +
                              ", differ " + N(containDiffer));
            foreach (string e in examples) Console.WriteLine("          differ: " + e);

            check("attribution study (" + name + "): best-score families are a subset of every-past-gate families",
                notSubset == 0, N(notSubset) + " not a subset");
            check("attribution study (" + name + "): best-score ties equal the I/L-folded containing proteins' families",
                containDiffer == 0 && containButNoBest == 0, N(containDiffer) + " differ, " + N(containButNoBest) + " contained but unmapped");

            // ---- the composition both ways ---------------------------------------------------------
            FamilyEnvelopeQuantResult ra = FamilyEnvelopeQuant.BuildFromAttribution(quant, all);
            FamilyEnvelopeQuantResult rb = FamilyEnvelopeQuant.BuildFromAttribution(quant, best);
            string P(double v) => (100 * v).ToString("F2", CultureInfo.InvariantCulture) + "%";
            Console.WriteLine("        composition shared bucket: " + P(ra.SharedShare) + " -> " + P(rb.SharedShare) +
                              "; unattributed " + P(ra.UnattributedShare) + " -> " + P(rb.UnattributedShare));
            double idShareA = ra.MeasuredIdentifiedEnvelope <= 0 ? 0 : ra.SharedIdentifiedEnvelope / ra.MeasuredIdentifiedEnvelope;
            double idShareB = rb.MeasuredIdentifiedEnvelope <= 0 ? 0 : rb.SharedIdentifiedEnvelope / rb.MeasuredIdentifiedEnvelope;
            Console.WriteLine("        identified signal in the shared bucket: " + P(idShareA) + " -> " + P(idShareB));

            var byId = new SortedSet<int>(ra.Families.Select(f => f.FamilyUserId).Concat(rb.Families.Select(f => f.FamilyUserId)));
            var rows = byId.Select(id =>
            {
                FamilyEnvelopeQuantRow? a = ra.Families.FirstOrDefault(f => f.FamilyUserId == id);
                FamilyEnvelopeQuantRow? b = rb.Families.FirstOrDefault(f => f.FamilyUserId == id);
                return (Id: id, Desc: (b ?? a)!.Description, A: a == null ? 0 : ra.CompositionShare(a),
                        B: b == null ? 0 : rb.CompositionShare(b));
            }).OrderByDescending(r => r.B).ToList();
            double sumAbs = rows.Sum(r => Math.Abs(r.B - r.A));
            var biggest = rows.OrderByDescending(r => Math.Abs(r.B - r.A)).Take(8).ToList();
            Console.WriteLine("        sum over families of |share change|: " + P(sumAbs) + " (" + N(rows.Count) + " families)");
            Console.WriteLine("        top families, composition share all-past-gate -> best-score:");
            foreach (var r in rows.Take(10))
                Console.WriteLine("          F" + r.Id.ToString(CultureInfo.InvariantCulture) + " " + Short(r.Desc) + ": " + P(r.A) + " -> " + P(r.B));
            Console.WriteLine("        largest changes:");
            foreach (var r in biggest)
                Console.WriteLine("          F" + r.Id.ToString(CultureInfo.InvariantCulture) + " " + Short(r.Desc) + ": " + P(r.A) + " -> " + P(r.B));

            var csv = new StringBuilder("family_user_id,description,share_all_past_gate,share_best_score,change\n");
            foreach (var r in rows)
                csv.Append(r.Id.ToString(CultureInfo.InvariantCulture)).Append(",\"").Append((r.Desc ?? string.Empty).Replace("\"", "\"\""))
                   .Append("\",").Append(r.A.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                   .Append(r.B.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                   .Append((r.B - r.A).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            csv.Append("shared,,").Append(ra.SharedShare.ToString("R", CultureInfo.InvariantCulture)).Append(',')
               .Append(rb.SharedShare.ToString("R", CultureInfo.InvariantCulture)).Append(',')
               .Append((rb.SharedShare - ra.SharedShare).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            csv.Append("unattributed,,").Append(ra.UnattributedShare.ToString("R", CultureInfo.InvariantCulture)).Append(',')
               .Append(rb.UnattributedShare.ToString("R", CultureInfo.InvariantCulture)).Append(',')
               .Append((rb.UnattributedShare - ra.UnattributedShare).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            string safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            string path = Path.Combine(outDir, "attribution_rule_study_" + safe + ".csv");
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
            Console.WriteLine("        CSV: " + path);

            double closeA = ra.Families.Sum(f => ra.CompositionShare(f)) + ra.SharedShare + ra.UnattributedShare;
            double closeB = rb.Families.Sum(f => rb.CompositionShare(f)) + rb.SharedShare + rb.UnattributedShare;
            check("attribution study (" + name + "): both compositions close on 1",
                Math.Abs(closeA - 1) < 1e-9 && Math.Abs(closeB - 1) < 1e-9,
                closeA.ToString("R", CultureInfo.InvariantCulture) + " / " + closeB.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string Short(string? s)
        {
            string t = (s ?? string.Empty).Split(new[] { " OS=" }, StringSplitOptions.None)[0];
            return t.Length > 40 ? t.Substring(0, 40) : t;
        }
    }
}
