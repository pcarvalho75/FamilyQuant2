using FamilyQuant.Homology;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HomologySearchEquivalenceCommands() =>
    [new("--test-homology-search-equivalence", 15, TestCommandPhase.Raw,
        args => args.Contains("--test-homology-search-equivalence", StringComparer.Ordinal), (_, _) =>
        {
            HomologySearchEquivalenceTests();
            HomologyTests.Run(Check);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private delegate List<HomologyResult> SearchMethod(IEnumerable<string> queries, HomologyDatabase database,
        HomologyOptions? options, OperationContext? operation, string progressStage, string? progressLabel, int progressBatchSize);

    private static void HomologySearchEquivalenceTests()
    {
        Type? type = typeof(HomologyDatabase).Assembly.GetType("FamilyQuant.Homology.HomologySearch");
        MethodInfo? method = type?.GetMethod("Search", BindingFlags.Public | BindingFlags.Static);
        Check("J38d separate HomologySearch engine is present", method != null);
        if (method == null) return;
        var engine = method.CreateDelegate<SearchMethod>();
        List<HomologyResult> search(IEnumerable<string> queries, HomologyDatabase database,
            HomologyOptions? options, OperationContext? operation) => engine(queries, database, options, operation, "homology", null, 1);
        var random = new Random(3801);
        const string residues = "ARNDCQEGHILKMFPSTWYVBZJXU*";
        string RandomSequence(int n) => new(Enumerable.Range(0, n).Select(_ => residues[random.Next(residues.Length)]).ToArray());

        for (int length = 1; length <= 40; length++)
        {
            string peptide = RandomSequence(length);
            string il = peptide.Replace('I', 'L'), kq = peptide.Replace('K', 'Q');
            char[] mutant = peptide.ToCharArray();
            for (int i = 2; i < mutant.Length; i += 4) mutant[i] = mutant[i] == 'A' ? 'G' : 'A';
            var entries = new List<(string, string)>
            {
                ("target-z", "GG" + peptide + "AA" + peptide),
                ("target-a", peptide), ("Reverse_tie", peptide),
                ("il", "WW" + il), ("kq", kq), ("mutant", new string(mutant)),
                ("Reverse_lower", RandomSequence(length + 7)),
                ("unicode", "\u03A9" + peptide + "O"), ("too-short", length == 1 ? "A" : peptide[..^1]),
            };
            HomologyDatabase db = HomologyDatabase.Build(entries);
            string[] queries = [peptide, " " + peptide.ToLowerInvariant() + " ", il, kq, "", "O", "C(+57)K"];
            string difference = string.Empty;
            int comparisons = 0;
            foreach (bool foldIL in new[] { false, true })
            foreach (bool foldKQ in new[] { false, true })
            foreach (double identity in new[] { 0.0, 0.8, 1.0, double.NaN })
            foreach (bool best in new[] { false, true })
            foreach (HomologySearchStrategy strategy in Enum.GetValues<HomologySearchStrategy>())
            {
                if (difference.Length > 0) continue;
                var options = new HomologyOptions { ConsiderILSame = foldIL, KQEquivalent = foldKQ,
                    MinIdentity = identity, BestScoreOnly = best, Strategy = strategy,
                    BruteForceProteinLimit = 0, TopHitsPerPeptide = 3 };
#pragma warning disable CS0618 // Frozen reference engine, used only by equivalence tests.
                List<HomologyResult> expected = HomologyMapper.Search(queries, db, options);
#pragma warning restore CS0618
                difference = HomologySearchEquivalence.Difference(expected, search(queries, db, options, null));
                comparisons++;
                if (difference.Length > 0)
                {
                    difference += $"; IL={foldIL}, KQ={foldKQ}, gate={identity}, best={best}, strategy={strategy}";
                    break;
                }
            }
            Check($"J38d length {length}: hit fields and competition flags equal ({comparisons} configurations)",
                difference.Length == 0, difference);
        }

        // A protein can pass identity at one window while PAM chooses another. The positional
        // filter must never restrict PAM scoring to only the identity-passing windows.
        string[] edgeQueries = ["LLLLLL", "IIIIII", "BBBBBB", "ZZZZZZ", "XXXXXX", "JJJJJJ", "UUUUUU", "******",
            "AAAGAAAGAAAG", "PEPTLDEK", "KQKQKQ", "ARNDCQEGHLKM", "A"];
        var edgeDb = HomologyDatabase.Build(new[]
        {
            ("exact", "LLLLLLBBBBBBZZZZZZXXXXXXJJJJJJUUUUUU******AAAGAAAGAAAGPEPTIDEKKQKQKQARNDCQEGHLKM"),
            ("Reverse_exact", "LLLLLLBBBBBBZZZZZZXXXXXXJJJJJJUUUUUU******"),
            ("separate-windows", "LLLLLALLLLLL"), ("gate-pam", "AAAGAAAGAAAGGGGAAAAAAAAAAAA"),
            ("undefined", "\u03A9OOOOXXPEPTLDEK"), ("end", "WPEPTLDEK"),
        });
        foreach (double threshold in new[] { 0.5, 0.8, 0.8000000000000002, 1.0 })
        foreach (bool best in new[] { false, true })
        {
            var options = new HomologyOptions { MinIdentity = threshold, BestScoreOnly = best,
                BruteForceProteinLimit = 0, TopHitsPerPeptide = 100 };
#pragma warning disable CS0618
            var expected = HomologyMapper.Search(edgeQueries, edgeDb, options);
#pragma warning restore CS0618
            string difference = HomologySearchEquivalence.Difference(expected, search(edgeQueries, edgeDb, options, null));
            Check($"J38d wildcard/matrix, separate gate/PAM, tail and tie fixtures gate={threshold}, best={best}", difference.Length == 0, difference);
        }

        // Positional packing must not truncate offsets to 16 bits.
        var longDb = HomologyDatabase.Build(new[] { ("long", new string('G', 70000) + "PEPTLDEK") });
        var indexed = new HomologyOptions { MinIdentity = 1, BruteForceProteinLimit = 0 };
        HomologyResult longHit = search(["PEPTLDEK"], longDb, indexed, null).Single();
        Check("J38d q3 positions retain offsets above 65535", longHit.Hits.Single().Offset == 70000);

        var wideDb = HomologyDatabase.Build(Enumerable.Range(0, 65537)
            .Select(i => ("protein-" + i, i == 65536 ? "PEPTLDEK" : "AAA")));
        Check("J38d q3 protein indices retain values above 65535", search(["PEPTLDEK"], wideDb, indexed, null)
            .Single().Hits.Single().ProteinId == "protein-65536");

        string longQuery = RandomSequence(1000);
        char[] longMutant = longQuery.ToCharArray();
        for (int i = 1; i < longMutant.Length; i += 5) longMutant[i] = longMutant[i] == 'A' ? 'G' : 'A';
        var saturatedDb = HomologyDatabase.Build(new[] { ("exact", "GG" + longQuery),
            ("mutant", new string(longMutant)), ("Reverse_long", longQuery.Replace('I', 'L')) });
        foreach (bool best in new[] { false, true })
        {
            var options = new HomologyOptions { BestScoreOnly = best, BruteForceProteinLimit = 0, TopHitsPerPeptide = 100 };
#pragma warning disable CS0618
            var expected = HomologyMapper.Search([longQuery], saturatedDb, options);
#pragma warning restore CS0618
            string difference = HomologySearchEquivalence.Difference(expected, search([longQuery], saturatedDb, options, null));
            Check($"J38d q3 diagonal vote counts remain exact above255, best={best}", difference.Length == 0, difference);
        }

        const string canonical = "ARNDCQEGHLKMFPSTWYV";
        char[] fold = HomologyScorer.BuildFoldMap(true, false);
        Check("J38d matrix proves identified-row maxima exactly at the I/L-folded identity class",
            canonical.All(c => PamMatrix.Alphabet.All(other => fold[c] == fold[other]
                ? PamMatrix.Score(c, other) == PamMatrix.Score(c, c)
                : PamMatrix.Score(c, other) < PamMatrix.Score(c, c))));

        var duplicateEntries = Enumerable.Range(0, 25).Select(i => ("lower-" + i, "LLLLLA")).ToList();
        duplicateEntries.Insert(7, ("duplicate", "LLLILL"));
        duplicateEntries.Insert(19, ("duplicate", "ILLLLL"));
        var duplicateDb = HomologyDatabase.Build(duplicateEntries);
        foreach (int top in new[] { 1, 2, 100 })
        {
            var options = new HomologyOptions { BestScoreOnly = true, BruteForceProteinLimit = 0, TopHitsPerPeptide = top };
#pragma warning disable CS0618
            var expected = HomologyMapper.Search(["LLLLLL"], duplicateDb, options);
#pragma warning restore CS0618
            string difference = HomologySearchEquivalence.Difference(expected, search(["LLLLLL"], duplicateDb, options, null));
            Check($"J38d duplicate-identifier equal ranking keys retain original tie order top={top}", difference.Length == 0, difference);
        }

        var randomProteins = Enumerable.Range(0, 100).Select(i =>
            ((i % 3 == 0 ? "Reverse_" : "target-") + i,
             new string(Enumerable.Range(0, 90).Select(_ => canonical[random.Next(canonical.Length)]).ToArray()))).ToList();
        var randomQueries = new List<string> { "PEPTLDEK", "PEPTIDEK", "LLLLLL", "IIIIII" };
        randomProteins.Add(("exact-target", "PEPTLDEK"));
        randomProteins.Add(("Reverse_lower-gate", "PEPTLDAK"));
        randomProteins.Add(("exact-decoy", "LLLLLL"));
        for (int i = 0; i < 80; i++)
        {
            int length = 6 + i % 35;
            char[] query = randomProteins[i].Item2.Substring(3, length).ToCharArray();
            for (int j = 1; j < query.Length; j += 5) query[j] = canonical[random.Next(canonical.Length)];
            randomQueries.Add(new string(query));
        }
        var randomDb = HomologyDatabase.Build(randomProteins);
        foreach (bool foldIL in new[] { false, true })
        foreach (bool foldKQ in new[] { false, true })
        foreach (bool best in new[] { false, true })
        foreach (double threshold in new[] { 0.8, 0.8000000000000002, 0.95, 1.0 })
        {
            var options = new HomologyOptions { ConsiderILSame = foldIL, KQEquivalent = foldKQ,
                BestScoreOnly = best, MinIdentity = threshold, BruteForceProteinLimit = 0, TopHitsPerPeptide = 100 };
#pragma warning disable CS0618
            var expected = HomologyMapper.Search(randomQueries, randomDb, options);
#pragma warning restore CS0618
            string difference = HomologySearchEquivalence.Difference(expected, search(randomQueries, randomDb, options, null));
            Check($"J38d adversarial canonical queries IL={foldIL}, KQ={foldKQ}, best={best}, gate={threshold}",
                difference.Length == 0, difference);
        }
    }
}
