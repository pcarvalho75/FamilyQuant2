using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PatternTools.PLP;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void DeadPathContractTests()
        {
            Check("the unused family-level keyword merge API is gone",
                !typeof(WordCluster).GetMethods().Any(m =>
                    m.Name is "GroupByKeywords" or "GroupByKeywordsCounted"));
            Check("the product exposes only the curated grouping",
                typeof(FamilyClusterFile).Assembly.GetType("FamilyQuant.GroupingType") == null);
            MethodInfo[] attribution = typeof(FamilyClusterFile).GetMethods()
                .Where(m => m.Name == "AttributeXICToGroups").ToArray();
            Check("quantitation has one explicit source, denominator and shared-peptide contract",
                attribution.Length == 1 && attribution[0].GetParameters().Length == 3);
            Check("internal group-XIC implementation has no unused public overloads",
                !typeof(FamilyClusterFile).GetMethods().Any(m => m.Name == "GetGroupXIC"));
            CuratedAttributionTests();
        }

        private static void CuratedAttributionTests()
        {
            string workDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
            string path = WriteDenominatorFixture(workDir, "curated_attribution.plp", "NONE");
            File.WriteAllText(path, File.ReadAllText(path).Replace(
                "1\tPEPTIDEB\tsp|X|PROT1\n", "1\tPEPTIDEB\tsp|X|PROT2\n"));
            var project = new PatternLabProject(new FileInfo(path));
            var first = new FastaItemExtended { SequenceIdentifier = "sp|X|PROT1", USearchID = 7, UserID = 100, XIC = 0.11 };
            var second = new FastaItemExtended { SequenceIdentifier = "sp|X|PROT2", USearchID = 7, UserID = 500, XIC = 0.22 };
            var document = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended> { first, second } };
            document.LockedUserIds.Add(500);

            document.AttributeXICToGroups(project, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
            Check("PatternLab attribution follows manual UserID splits even when USearchID is shared",
                first.XICUser == 0.375 && second.XICUser == 0.625);
            Check("attribution leaves legacy stored XIC, automatic ids and curation locks intact",
                first.XIC == 0.11 && second.XIC == 0.22 && first.USearchID == 7 && second.USearchID == 7 &&
                document.LockedUserIds.SetEquals(new[] { 500 }));
            Check("the quantified-family report follows the same curated split",
                ReportView.QuantifiedFamilies(document.MyExtendedFastaItems) == 2);

            document.AttributeXICToGroups(project, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
            Check("repeating curated attribution leaves the exact same shares",
                first.XICUser == 0.375 && second.XICUser == 0.625);
        }
    }
}
