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

        // ======================================================= quantitation

        private static void QuantitationTests(string? plpPath)
        {
            if (plpPath == null || !File.Exists(plpPath))
            {
                Console.WriteLine("  SKIP  quantitation - pass a .plp file as the first argument to run this section");
                skipped++;
                return;
            }

            var plp = new PatternLabProject(new FileInfo(plpPath));

            // A database this project knows nothing about, carrying values from a "previous" run.
            var fcf = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "NOT_IN_THIS_PROJECT_1", USearchID = 0, UserID = 0, XIC = 0.75, XICUser = 0.75 },
                    new FastaItemExtended { SequenceIdentifier = "NOT_IN_THIS_PROJECT_2", USearchID = 1, UserID = 1, XIC = 0.25, XICUser = 0.25 },
                }
            };

            fcf.SetLookupDictionary(plp);
            fcf.AttributeXICToGroups(plp, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);

            Check("XIC is zeroed rather than left showing the previous project's numbers",
                fcf.MyExtendedFastaItems.All(i => i.XICUser == 0));

            fcf.MyExtendedFastaItems.ForEach(i => i.XICUser = 0.5);
            fcf.AttributeXICToGroups(plp, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
            Check("the User column is zeroed too", fcf.MyExtendedFastaItems.All(i => i.XICUser == 0));

            var other = new PatternLabProject(new FileInfo(plpPath));
            fcf.AttributeXICToGroups(other, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
            Check("handing in a different project rebuilds the lookups instead of reusing the cache",
                fcf.MyExtendedFastaItems.All(i => i.XICUser == 0));

            // --- how the project was exported, read from the values rather than from a flag ---
            var profile = QuantitationProfile.Describe(plp);
            Console.WriteLine("        " + profile.Summary());

            Check("the project reports at least one run", profile.FileCount >= 1, profile.FileCount.ToString());

            // Only meaningful when the answer came from the VALUES. A file that records "Denom=NONE"
            // is legitimately not TIC-normalized however small its numbers are, so tying this to the
            // magnitude would be asserting the heuristic rather than the behaviour.
            if (profile.Evidence == DenominatorEvidence.Inferred)
            {
                Check("a TIC-normalized project is recognised as such",
                    !profile.IsTicNormalized || profile.MeanPerFile <= 1.0);
            }

            // Whole-chromatogram mode is offered on two grounds now: the values already ARE fractions
            // of the TIC, or the file carries each run's TIC so they can be converted (Phase 4). The
            // second is why this is no longer plain equality with IsTicNormalized.
            Check("whole-chromatogram mode is offered exactly when it is computable",
                profile.CanReportTotalChromatogram == (profile.IsTicNormalized || profile.CanRescaleToTic),
                $"can={profile.CanReportTotalChromatogram} tic={profile.IsTicNormalized} rescale={profile.CanRescaleToTic}");

            // Proteins the project does know about must normalise to 1 across families.
            var known = plp.MyIndex.TheIndexes
                .SelectMany(ProteinsOf)
                .Distinct()
                .Take(40)
                .ToList();

            if (known.Count >= 2)
            {
                var real = new FamilyClusterFile
                {
                    MyExtendedFastaItems = known
                        .Select((locus, i) => new FastaItemExtended { SequenceIdentifier = locus, USearchID = i % 4 , UserID = i % 4})
                        .ToList()
                };

                real.SetLookupDictionary(plp);
                real.AttributeXICToGroups(plp, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);

                double sum = real.GroupedFastaItems.Sum(g => g.First().XICUser);
                Check("identified-signal mode adds up to 1", Math.Abs(sum - 1.0) < 1e-9, sum.ToString("R"));
                Check("no family gets a negative share", real.MyExtendedFastaItems.All(i => i.XICUser >= 0));
                Check("no family exceeds 100% of the identified signal",
                    real.MyExtendedFastaItems.All(i => i.XICUser <= 1.0),
                    real.MyExtendedFastaItems.Max(i => i.XICUser).ToString("R"));

                if (profile.CanReportTotalChromatogram)
                {
                    // The decisive test: EVERY protein the project knows, split into many families, so
                    // peptides shared between families are abundant. This is the shape that exposed the
                    // overlap defect - a small fixture passes even when the policy is wrong.
                    var wide = new FamilyClusterFile
                    {
                        MyExtendedFastaItems = plp.MyIndex.TheIndexes
                            .SelectMany(ProteinsOf)
                            .Distinct()
                            .Select((locus, i) => new FastaItemExtended { SequenceIdentifier = locus, USearchID = i % 60 , UserID = i % 60})
                            .ToList()
                    };
                    wide.SetLookupDictionary(plp);

                    double identifiedFraction = profile.IdentifiedFractionOfTic;

                    wide.AttributeXICToGroups(plp,
                        NormalizationMode.TotalChromatogram, SharedPeptidePolicy.FamilyUnique);
                    double uniqueSum = wide.GroupedFastaItems.Sum(g => g.First().XICUser);

                    wide.AttributeXICToGroups(plp,
                        NormalizationMode.TotalChromatogram, SharedPeptidePolicy.SplitEqually);
                    double splitSum = wide.GroupedFastaItems.Sum(g => g.First().XICUser);

                    wide.AttributeXICToGroups(plp,
                        NormalizationMode.TotalChromatogram, SharedPeptidePolicy.CountInEveryFamily);
                    double overlapSum = wide.GroupedFastaItems.Sum(g => g.First().XICUser);

                    Console.WriteLine($"        identified {identifiedFraction:P2} · unique {uniqueSum:P2} · " +
                                      $"split {splitSum:P2} · overlapping {overlapSum:P2}");

                    Check("family-unique never claims more of the chromatogram than was identified",
                        uniqueSum <= identifiedFraction + 1e-9, uniqueSum.ToString("R"));
                    Check("splitting shared signal preserves the identified total",
                        Math.Abs(splitSum - identifiedFraction) < 1e-9,
                        splitSum.ToString("R") + " vs " + identifiedFraction.ToString("R"));
                    Check("family-unique is the conservative subset of split",
                        uniqueSum <= splitSum + 1e-12);
                    Check("counting a peptide in every family overlaps, hence the warning on that option",
                        overlapSum >= splitSum - 1e-12);
                    Check("no family alone exceeds the whole chromatogram",
                        wide.MyExtendedFastaItems.All(i => i.XICUser >= 0 && i.XICUser <= 1.0));

                    real.AttributeXICToGroups(plp, NormalizationMode.TotalChromatogram, SharedPeptidePolicy.FamilyUnique);
                    Check("the two modes rank the families the same way",
                        SameRanking(real, plp), "ranking changed between modes");
                }
            }
            else
            {
                Console.WriteLine("  SKIP  normalisation - could not read protein loci from this project");
                skipped++;
            }
        }

        /// <summary>Every protein a peptide row maps to, read the same way the app reads it.</summary>
        private static IEnumerable<string> ProteinsOf(dynamic index)
        {
            string description = index.Description ?? "";

            string[] loci = description.Contains("Mappable proteins : ")
                ? description.Split(new[] { "Mappable proteins : " }, StringSplitOptions.None)[1].Split(new[] { ", " }, StringSplitOptions.None)
                : description.Split(' ');

            return loci.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim());
        }

        /// <summary>
        /// Both modes divide the same per-family totals by a constant, so they must order the families
        /// identically. A difference would mean one of the denominators is not constant across families.
        /// </summary>
        private static bool SameRanking(FamilyClusterFile fcf, PatternLabProject plp)
        {
            fcf.AttributeXICToGroups(plp, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
            var byIdentified = fcf.GroupedFastaItems.OrderByDescending(g => g.First().XICUser).Select(g => g.Key).ToList();

            fcf.AttributeXICToGroups(plp, NormalizationMode.TotalChromatogram, SharedPeptidePolicy.FamilyUnique);
            var byChromatogram = fcf.GroupedFastaItems.OrderByDescending(g => g.First().XICUser).Select(g => g.Key).ToList();

            return byIdentified.SequenceEqual(byChromatogram);
        }

        // ============================================================ locale

        /// <summary>
        /// A .plp stores its numbers with a dot, invariantly. A reader that parses them with the machine's
        /// own culture turns "0.0009686085227719106" into 9.686e15 on a comma-decimal machine, because the
        /// dot is taken for a thousands separator - and the damage is NOT a constant rescale: the factor
        /// follows the digit count, so every family percentage comes out wrong rather than merely large.
        ///
        /// This is what made a TIC-normalized project report itself as "raw quantitation summing to
        /// 3.09E+18" on a Spanish-locale machine while reading correctly here. The test pins the culture
        /// because on an en-US machine the bug is completely invisible.
        /// </summary>
        // ============================================== the recorded denominator (Phase 4)

        /// <summary>
        /// Builds a small .plp by hand so the denominator record can be varied independently of any real
        /// export. Two runs, two peptides, values chosen so the arithmetic is checkable by eye.
        ///
        /// Raw areas:            run A: pep0 = 100, pep1 = 300     (D1 = 400,  D2 = 1000)
        ///                       run B: pep0 = 200, pep1 = 200     (D1 = 400,  D2 = 4000)
        /// </summary>
        private static string WriteDenominatorFixture(string workDir, string fileName, string denomTag,
                                                      bool withPerRunDetail = true, string? aggregate = null,
                                                      string quantity = "XIC")
        {
            // The value stored for each run is area / applied.
            double appliedA = denomTag == "NONE" ? 1 : (denomTag == "ID" ? 400 : 1000);
            double appliedB = denomTag == "NONE" ? 1 : (denomTag == "ID" ? 400 : 4000);

            var sb = new StringBuilder();
            sb.Append("###Description : Fixture [Norm=").Append(denomTag).Append("]\n");

            sb.Append("###Normalization\tVersion=1\tDenom=").Append(denomTag)
              .Append("\tApplied=").Append(denomTag == "NONE" ? "1" : "perRun")
              .Append("\tAggregate=").Append(aggregate ?? "none")
              .Append("\tRuns=2\tSource=Test\tMatrix=Peptides\tQuant=").Append(quantity).Append('\n');

            sb.Append("###SparseMatrix\n");
            sb.Append("#ClassDescription\t1\tclassOne\n");
            sb.Append("#C:\\runs\\runA.sepr2\n");
            sb.Append("1 0:").Append((100 / appliedA).ToString("R", CultureInfo.InvariantCulture))
              .Append(" 1:").Append((300 / appliedA).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("#C:\\runs\\runB.sepr2\n");
            sb.Append("1 0:").Append((200 / appliedB).ToString("R", CultureInfo.InvariantCulture))
              .Append(" 1:").Append((200 / appliedB).ToString("R", CultureInfo.InvariantCulture)).Append('\n');

            sb.Append("###Index\n");
            sb.Append("0\tPEPTIDEA\tsp|X|PROT1\n");
            sb.Append("1\tPEPTIDEB\tsp|X|PROT1\n");

            sb.Append("###SecondaryLabels\n");
            AppendFixtureLabel(sb, "C:\\runs\\runA.sepr2", appliedA, 400, 1000, denomTag, withPerRunDetail, aggregate);
            AppendFixtureLabel(sb, "C:\\runs\\runB.sepr2", appliedB, 400, 4000, denomTag, withPerRunDetail, aggregate);

            string path = Path.Combine(workDir, fileName);
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static void AppendFixtureLabel(StringBuilder sb, string name, double applied, double d1, double d2,
                                               string denomTag, bool withDetail, string? aggregate)
        {
            sb.Append("1\t").Append(name).Append('\t').Append(d2.ToString("R", CultureInfo.InvariantCulture));

            if (withDetail)
            {
                sb.Append("\tDenom=").Append(denomTag)
                  .Append("\tApplied=").Append(applied.ToString("R", CultureInfo.InvariantCulture))
                  .Append("\tD1=").Append(d1.ToString("R", CultureInfo.InvariantCulture))
                  .Append("\tD2=").Append(d2.ToString("R", CultureInfo.InvariantCulture))
                  .Append("\tD3=");

                if (aggregate != null) sb.Append("\tAgg=").Append(aggregate).Append("\tN=3");
            }

            sb.Append('\n');
        }

        /// <summary>
        /// Four runs, two peptides, and neither peptide is seen in more than two of them. No
        /// ###SecondaryLabels, which is the legacy export the run count has to survive.
        /// </summary>
        private static string WriteSparseRunFixture(string workDir, string fileName)
        {
            var sb = new StringBuilder();
            sb.Append("###Description : Sparse runs fixture\n");
            sb.Append("###SparseMatrix\n");
            sb.Append("#ClassDescription\t1\tclassOne\n");
            sb.Append("#C:\\runs\\runA.sepr2\n1 0:0.6\n");
            sb.Append("#C:\\runs\\runB.sepr2\n1 0:0.6\n");
            sb.Append("#C:\\runs\\runC.sepr2\n1 1:0.6\n");
            sb.Append("#C:\\runs\\runD.sepr2\n1 1:0.6\n");
            sb.Append("###Index\n");
            sb.Append("0\tPEPTIDEA\tsp|X|PROT1\n");
            sb.Append("1\tPEPTIDEB\tsp|X|PROT1\n");

            string path = Path.Combine(workDir, fileName);
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        /// <summary>
        /// The run count is the number of rows in the matrix, and each row carries its own run identity;
        /// see the note in NormalizationPlan. Taking it from the widest peptide instead is only a lower
        /// bound, and it is wrong for any project where no single peptide was observed in every run.
        ///
        /// It matters because every denominator mode divides the summed per-run fractions by this number
        /// (FamilyQuantFile.AttributeXICToGroups). Too small a divisor inflates every reported share, and
        /// can carry it past 100%.
        /// </summary>
        private static void RunCountTests(string workDir)
        {
            string path = WriteSparseRunFixture(workDir, "sparse_runs.plp");
            var plp = new PatternLabProject(new FileInfo(path));

            int rows = plp.MySparseMatrix.theMatrixInRows.Count;
            Check("the fixture really has four runs and no peptide in all of them", rows == 4, rows.ToString());
            Check("no peptide is observed in more than two runs",
                plp.MyIndex.TheIndexes.All(i => plp.MySparseMatrix.ExtractDimValues(i.ID, 0, true).Count <= 2));

            var profile = QuantitationProfile.Describe(plp);
            Check("the run count is the number of runs, not the widest peptide's observations",
                profile.FileCount == 4, profile.FileCount.ToString());
        }

        private static void RecordedDenominatorTests(string workDir)
        {
            RunCountTests(workDir);

            // ---- the record is read, and it beats the value-shape guess --------------------------
            string ticPath = WriteDenominatorFixture(workDir, "denom_tic.plp", "TIC");
            var ticPlp = new PatternLabProject(new FileInfo(ticPath));

            Check("the ###Normalization line is parsed", ticPlp.NormalizationInfo != null);
            Check("the denominator is read from the file", ticPlp.RecordedDenominatorTag == "TIC",
                ticPlp.RecordedDenominatorTag);

            var ticProfile = QuantitationProfile.Describe(ticPlp);
            Check("a recorded denominator counts as recorded, not inferred",
                ticProfile.Evidence == DenominatorEvidence.Recorded, ticProfile.Evidence.ToString());
            Check("the summary names the denominator",
                ticProfile.DenominatorSentence().Contains("total ion current"), ticProfile.DenominatorSentence());
            Check("the summary says it was recorded",
                ticProfile.DenominatorSentence().Contains("recorded"), ticProfile.DenominatorSentence());

            // ---- a RAW export is recognised as raw, not mistaken for normalized -------------------
            string rawPath = WriteDenominatorFixture(workDir, "denom_none.plp", "NONE");
            var rawPlp = new PatternLabProject(new FileInfo(rawPath));
            var rawProfile = QuantitationProfile.Describe(rawPlp);

            Check("a raw export is not reported as TIC-normalized", !rawProfile.IsTicNormalized);
            Check("a raw export still knows its denominator was recorded",
                rawProfile.Evidence == DenominatorEvidence.Recorded);

            // This is the Phase 4 payoff: raw values, but the file carries every run's TIC, so the
            // percentage view is available WITHOUT re-exporting.
            Check("a raw export can still be reported against the whole chromatogram",
                rawProfile.CanReportTotalChromatogram);

            // ---- the record beats the heuristic when they disagree --------------------------------
            // Raw fixture values are 100..300, so the value-shape guess says "not normalized"; the
            // ID fixture divides by 400, giving values under 1, so the guess would say "TIC". It is wrong,
            // and the record must win.
            string idPath = WriteDenominatorFixture(workDir, "denom_id.plp", "ID");
            var idPlp = new PatternLabProject(new FileInfo(idPath));
            var idProfile = QuantitationProfile.Describe(idPlp);

            Check("the value-shape guess would have said TIC here", idProfile.InferredTicNormalized);
            Check("but the recorded denominator overrules it", !idProfile.IsTicNormalized,
                "recorded=" + idProfile.RecordedDenominatorTag);

            // ---- the arithmetic of re-expressing a run under another denominator -----------------
            // Fixture: run A areas 100+300 = 400 of a 1000 TIC; run B 200+200 = 400 of a 4000 TIC.
            // As a share of the whole chromatogram that is (0.4 + 0.1) / 2 runs = 0.25 averaged.
            foreach (var (label, path) in new[] { ("raw", rawPath), ("ID-normalized", idPath), ("TIC-normalized", ticPath) })
            {
                var plp = new PatternLabProject(new FileInfo(path));

                var family = new FamilyClusterFile
                {
                    MyExtendedFastaItems = new List<FastaItemExtended>
                    {
                        new FastaItemExtended { SequenceIdentifier = "sp|X|PROT1", USearchID = 0 , UserID = 0}
                    }
                };
                family.SetLookupDictionary(plp);
                family.AttributeXICToGroups(plp,
                    NormalizationMode.TotalChromatogram, SharedPeptidePolicy.FamilyUnique);

                double share = family.MyExtendedFastaItems[0].XICUser;
                Check($"a {label} export reports the same 25% share of the chromatogram",
                    Math.Abs(share - 0.25) < 1e-12, share.ToString("R"));
            }

            // Same three files under the identified-peptide denominator: every area is an identified
            // peptide here, so the family owns 100% of D1 in both runs.
            foreach (var (label, path) in new[] { ("raw", rawPath), ("ID-normalized", idPath), ("TIC-normalized", ticPath) })
            {
                var plp = new PatternLabProject(new FileInfo(path));
                var family = new FamilyClusterFile
                {
                    MyExtendedFastaItems = new List<FastaItemExtended>
                    {
                        new FastaItemExtended { SequenceIdentifier = "sp|X|PROT1", USearchID = 0 , UserID = 0}
                    }
                };
                family.SetLookupDictionary(plp);
                family.AttributeXICToGroups(plp,
                    NormalizationMode.IdentifiedPeptideSignal, SharedPeptidePolicy.FamilyUnique);

                double share = family.MyExtendedFastaItems[0].XICUser;
                Check($"a {label} export reports 100% of the extracted peptide signal",
                    Math.Abs(share - 1.0) < 1e-12, share.ToString("R"));
            }

            // ---- the refusals ---------------------------------------------------------------------
            var d3 = NormalizationPlan.For(ticPlp, PatternTools.PLP.NormalizationInfo.TagChargeRestricted);
            Check("a denominator the file does not carry is refused", !d3.IsAvailable);
            Check("and the refusal says why", d3.UnavailableReason.Length > 0, d3.UnavailableReason);

            // An averaged divisor is a summary and cannot be undone - this is what the Agg=mean stamp is
            // for, and it must actually stop the conversion.
            string mergedPath = WriteDenominatorFixture(workDir, "denom_merged.plp", "ID", aggregate: "mean");
            var mergedPlp = new PatternLabProject(new FileInfo(mergedPath));
            var mergedPlan = NormalizationPlan.For(mergedPlp, PatternTools.PLP.NormalizationInfo.TagTotalTic);
            Check("merged technical replicates refuse to be re-expressed", !mergedPlan.IsAvailable);
            Check("and say it is because the divisors are averages",
                mergedPlan.UnavailableReason.Contains("average"), mergedPlan.UnavailableReason);

            // NIAF is already a ratio; rescaling it by a signal total is meaningless.
            string niafPath = WriteDenominatorFixture(workDir, "denom_niaf.plp", "NONE", quantity: "NIAF");
            var niafPlp = new PatternLabProject(new FileInfo(niafPath));
            var niafPlan = NormalizationPlan.For(niafPlp, PatternTools.PLP.NormalizationInfo.TagTotalTic);
            Check("NIAF values refuse to be re-expressed", !niafPlan.IsAvailable);
            Check("and say it is because they are already a ratio",
                niafPlan.UnavailableReason.Contains("ratio"), niafPlan.UnavailableReason);

            string niafNoOpPath = WriteDenominatorFixture(workDir, "denom_niaf_tic.plp", "TIC", quantity: "NIAF");
            var niafNoOpPlp = new PatternLabProject(new FileInfo(niafNoOpPath));
            Check("NIAF safety gate runs before the matching-denominator no-op",
                !NormalizationPlan.For(niafNoOpPlp, PatternTools.PLP.NormalizationInfo.TagTotalTic).IsAvailable);
            Check("a TIC tag cannot make NIAF report itself as TIC-normalized",
                !QuantitationProfile.Describe(niafNoOpPlp).IsTicNormalized);

            string mergedNoOpPath = WriteDenominatorFixture(
                workDir, "denom_merged_tic.plp", "TIC", aggregate: "mean");
            var mergedNoOpPlp = new PatternLabProject(new FileInfo(mergedNoOpPath));
            Check("aggregate safety gate runs before the matching-denominator no-op",
                !NormalizationPlan.For(mergedNoOpPlp,
                    PatternTools.PLP.NormalizationInfo.TagTotalTic).IsAvailable);

            // ---- a legacy file keeps working, and is labelled as a guess --------------------------
            string legacyPath = Path.Combine(workDir, "denom_legacy.plp");
            File.WriteAllText(legacyPath,
                "###Description : XIC Quant\n###SparseMatrix\n#ClassDescription\t1\tclassOne\n" +
                "#C:\\runs\\runA.sepr2\n1 0:0.4\n###Index\n0\tPEPTIDEA\tsp|X|PROT1\n###SecondaryLabels\n1\tC:\\runs\\runA.sepr2\t1000\n");

            var legacyPlp = new PatternLabProject(new FileInfo(legacyPath));
            var legacyProfile = QuantitationProfile.Describe(legacyPlp);

            Check("a legacy file records no denominator", legacyPlp.RecordedDenominatorTag == null,
                legacyPlp.RecordedDenominatorTag ?? "null");
            Check("so the answer is marked as inferred",
                legacyProfile.Evidence == DenominatorEvidence.Inferred, legacyProfile.Evidence.ToString());
            Check("and the wording says so plainly",
                legacyProfile.DenominatorSentence().Contains("NOT recorded"), legacyProfile.DenominatorSentence());
            Check("the heuristic still works for it", legacyProfile.IsTicNormalized);
            Check("a legacy file cannot be re-expressed",
                !NormalizationPlan.For(legacyPlp, PatternTools.PLP.NormalizationInfo.TagIdentified).IsAvailable);

            // ---- the description token alone is enough (an older PatternTools wrote the file) -----
            string tokenOnlyPath = Path.Combine(workDir, "denom_token_only.plp");
            File.WriteAllText(tokenOnlyPath,
                "###Description : XIC Quant [Norm=TIC]\n###SparseMatrix\n#ClassDescription\t1\tclassOne\n" +
                "#C:\\runs\\runA.sepr2\n1 0:0.4\n###Index\n0\tPEPTIDEA\tsp|X|PROT1\n###SecondaryLabels\n1\tC:\\runs\\runA.sepr2\t1000\n");

            var tokenPlp = new PatternLabProject(new FileInfo(tokenOnlyPath));
            Check("the description token alone still names the denominator",
                tokenPlp.RecordedDenominatorTag == "TIC", tokenPlp.RecordedDenominatorTag ?? "null");
            Check("and that counts as recorded, not inferred",
                QuantitationProfile.Describe(tokenPlp).Evidence == DenominatorEvidence.Recorded);
        }

        private static void LocaleTests(string? plpPath)
        {
            // The premise, provable without any file: this is why the values had to be read invariantly.
            var comma = new CultureInfo("pt-BR");
            Check("a comma-decimal culture misreads an invariant decimal",
                double.Parse("0.5", comma) == 5.0);
            Check("and misreads it NON-linearly, so percentages break rather than just rescale",
                double.Parse("0.5", comma) / 0.5 != double.Parse("0.0005", comma) / 0.0005);

            if (plpPath == null || !File.Exists(plpPath))
            {
                Console.WriteLine("  SKIP  project re-read under pt-BR - pass a .plp file to run this");
                skipped++;
                return;
            }

            var original = CultureInfo.CurrentCulture;
            QuantitationProfile invariantProfile, commaProfile;

            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                invariantProfile = QuantitationProfile.Describe(new PatternLabProject(new FileInfo(plpPath)));

                CultureInfo.CurrentCulture = comma;
                commaProfile = QuantitationProfile.Describe(new PatternLabProject(new FileInfo(plpPath)));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }

            Console.WriteLine($"        invariant: {invariantProfile.GrandTotal:R} · pt-BR: {commaProfile.GrandTotal:R}");

            Check("the project reads the same total under both cultures",
                invariantProfile.GrandTotal == commaProfile.GrandTotal,
                $"{invariantProfile.GrandTotal:R} vs {commaProfile.GrandTotal:R}");
            Check("a TIC-normalized project is still recognised as such under pt-BR",
                invariantProfile.IsTicNormalized == commaProfile.IsTicNormalized);
            Check("the run count survives the culture change",
                invariantProfile.FileCount == commaProfile.FileCount);
        }
    }
}
