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

        // ======================================================= word clusters

        private static void WordClusterTests()
        {
            DeadPathContractTests();
            var items = new List<FastaItemExtended>();
            string[] descriptions =
            {
                "Snake venom metalloproteinase BJ-PI",
                "Snake venom metalloproteinase BJ-PII",
                "Snake venom metalloprotease jararhagin",
                "Phospholipase A2 basic",
                "Phospholipase A2 acidic",
                "L-amino acid oxidase",
            };

            for (int i = 0; i < descriptions.Length; i++)
            {
                items.Add(new FastaItemExtended
                {
                    SequenceIdentifier = "sp|P" + i + "|X",
                    Description = descriptions[i] + " OS=Bothrops jararaca OX=8724",
                    USearchID = i, UserID = i
                });
                // A decoy with no description at all, as produced for a reversed entry.
                items.Add(new FastaItemExtended
                {
                    SequenceIdentifier = "Reverse_sp|P" + i + "|X",
                    Description = "",
                    USearchID = descriptions.Length + i, UserID = descriptions.Length + i
                });
            }

            int inputFamilies = items.Select(i => i.USearchID).Distinct().Count();

            foreach (double threshold in new[] { 0.1, 0.3, 0.5, 0.8, 0.95, 1.0 })
            {
                var result = new WordCluster(items, new List<string>()).GroupByWords(threshold);
                var emitted = result.SelectMany(c => c).ToList();

                bool partitions = emitted.Count == emitted.Distinct().Count()
                                  && emitted.Count == inputFamilies
                                  && result.Count <= inputFamilies;

                Check($"threshold {threshold:0.##} partitions the families exactly", partitions,
                    $"{result.Count} clusters, {emitted.Count} ids, {emitted.Distinct().Count()} distinct");

                var repeat = new WordCluster(items, new List<string>()).GroupByWords(threshold);
                Check($"threshold {threshold:0.##} is reproducible",
                    result.Count == repeat.Count && result.Zip(repeat, (x, y) => x.SequenceEqual(y)).All(e => e));
            }

            // Descriptionless entries share no words, so they must not be pulled into one family.
            var strict = new WordCluster(items, new List<string>()).GroupByWords(1.0);
            Check("proteins without a description are not merged with each other",
                strict.Count(c => c.Count > 1) == 0, $"{strict.Count} clusters");

            var loose = new WordCluster(items, new List<string>()).GroupByWords(0.3);
            Check("families sharing wording do merge at a loose threshold", loose.Count < inputFamilies,
                $"{loose.Count} of {inputFamilies}");

            var excluded = new WordCluster(items, new List<string> { "Snake", "venom" }).GroupByWords(0.3);
            Check("the exclusion list is honoured regardless of typed case", excluded.Count >= loose.Count,
                $"{excluded.Count} vs {loose.Count}");

            var originalIds = items.Select(i => i.UserID).ToArray();
            var super = new WordCluster(items, new List<string>()).MatchKeywords(new List<string> { "metalloproteinase" });
            Check("keyword matching leaves every family untouched",
                items.Select(i => i.UserID).SequenceEqual(originalIds));
            Check("the same two families carry the keyword", super.Matched.SetEquals(new[] { 0, 1 }));

            ManualSplitSurvivesReclusteringTests();
            IdfWeightingTests();
            CurationLockAndStableIdTests();
            SuperClusterMatchAndExcludeTests();
            SearchScopeReclusterTests();
            SearchTermsAndKeywordPhrasesTests();
            UndoSnapshotTests();
        }

        /// <summary>
        /// The super cluster's match mode (ALL keywords vs ANY) and its own exclude-words list, on
        /// synthetic descriptions. Ana's case is the anchor: "acidic" AND "phospholipase" must gather the
        /// acidic phospholipases and leave the plain ones and the inhibitor behind.
        /// </summary>
        private static void SuperClusterMatchAndExcludeTests()
        {
            // Each protein is its own family, so "family matched" and "protein matched" coincide and the
            // assertions read directly. Descriptions are the ones from the report.
            List<FastaItemExtended> Families() => new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|A|X", USearchID = 0, UserID = 0,
                    Description = "Acidic phospholipase A2 OS=Bothrops jararaca OX=8724" },
                new FastaItemExtended { SequenceIdentifier = "sp|B|X", USearchID = 1, UserID = 1,
                    Description = "Phospholipase A2 inhibitor OS=Bothrops jararaca OX=8724" },
                new FastaItemExtended { SequenceIdentifier = "sp|C|X", USearchID = 2, UserID = 2,
                    Description = "Snake venom phospholipase B OS=Bothrops jararaca OX=8724" },
                new FastaItemExtended { SequenceIdentifier = "sp|D|X", USearchID = 3, UserID = 3,
                    Description = "Zinc metalloproteinase-disintegrin OS=Bothrops jararaca OX=8724" },
            };

            var members = Families();
            var matcher = new WordCluster(members, new List<string>());
            var any = matcher.MatchKeywords(new List<string> { "acidic", "phospholipase" }, KeywordMatch.Any);
            Check("ANY matches every family carrying either keyword (0,1,2), not the metalloproteinase (3)",
                any.Matched.SetEquals(new[] { 0, 1, 2 }));

            var all = matcher.MatchKeywords(new List<string> { "acidic", "phospholipase" }, KeywordMatch.All);
            Check("ALL matches only the acidic phospholipase",
                all.Matched.SetEquals(new[] { 0 }));

            var allShared = matcher.MatchKeywords(new List<string> { "phospholipase" }, KeywordMatch.All);
            Check("ALL on the single shared keyword matches all three phospholipases",
                allShared.Matched.SetEquals(new[] { 0, 1, 2 }));

            var excluded = matcher.MatchKeywords(new List<string> { "phospholipase" }, KeywordMatch.Any,
                new List<string> { "inhibitor" });
            Check("an exclude word identifies the family that otherwise matched (inhibitor removes family 1)",
                excluded.Matched.Except(excluded.Excluded).OrderBy(i => i).SequenceEqual(new[] { 0, 2 }));
            Check("the excluded family remains available without changing its membership",
                excluded.Excluded.SetEquals(new[] { 1 }) && members[1].UserID == 1);

            var excludedCase = matcher.MatchKeywords(new List<string> { "phospholipase" }, KeywordMatch.Any,
                new List<string> { "INHIBITOR" });
            Check("the super-cluster exclude ignores typed case",
                excludedCase.Excluded.SetEquals(excluded.Excluded));

            var defaultMatch = matcher.MatchKeywords(new List<string> { "acidic", "phospholipase" });
            Check("the matcher defaults to ANY (0,1,2 matched)",
                defaultMatch.Matched.SetEquals(new[] { 0, 1, 2 }));

            var lockedResult = new SuperClusterPreview(members, null, "phospholipase", "",
                KeywordMatch.Any, null, new[] { 1 });
            Check("a locked family never joins the preview and is still counted as a locked match",
                lockedResult.LockedMatches == 1 && !lockedResult.JoiningFamilies.Contains(1));
        }

        /// <summary>
        /// Search scopes reclustering: with text in the Search box, a recluster changes ONLY the families
        /// the search shows and leaves every other family's numbers untouched; with an empty search it is
        /// the whole universe. Drives the real scope resolver <see cref="WordCluster.ScopeItems"/> and
        /// applies the result the way UpdateClusterIds does.
        /// </summary>
        private static void SearchScopeReclusterTests()
        {
            // Two serine-protease families that WILL merge at 0.85 (identical descriptions) and one
            // unrelated phospholipase family that must never move. The search "serine" shows only the two
            // serine families.
            List<FastaItemExtended> Db() => new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|S1|X", USearchID = 10, UserID = 10,
                    Description = "Snake venom serine protease OS=x OX=1" },
                new FastaItemExtended { SequenceIdentifier = "sp|S2|X", USearchID = 20, UserID = 20,
                    Description = "Snake venom serine protease OS=x OX=1" },
                new FastaItemExtended { SequenceIdentifier = "sp|P1|X", USearchID = 30, UserID = 30,
                    Description = "Acidic phospholipase A2 OS=x OX=1" },
            };

            // The application, exactly as UpdateClusterIds does it for a scoped operation: build the
            // cluster over the scoped items, map, apply to all (out-of-scope keys are absent), then sync
            // only the scoped items.
            void ApplyScopedWordRecluster(List<FastaItemExtended> items, string search, double threshold)
            {
                var scope = WordCluster.ScopeItems(items, search);
                bool scoped = !string.IsNullOrWhiteSpace(search);
                var clusters = new WordCluster(scope, new List<string>()).GroupByWords(threshold, new HashSet<int>());
                var mapping = WordCluster.BuildStableMapping(clusters);
                foreach (var item in items)
                    if (mapping.TryGetValue(item.UserID, out int id)) item.UserID = id;
                WordCluster.SyncUnlockedIds(scoped ? (IEnumerable<FastaItemExtended>)scope : items, new HashSet<int>());
            }

            var scoped = Db();
            ApplyScopedWordRecluster(scoped, "serine", 0.85);

            Check("scoped recluster merges the two shown serine families to the survivor number 10",
                scoped[0].UserID == 10 && scoped[1].UserID == 10,
                scoped[0].UserID + " , " + scoped[1].UserID);
            Check("the merged serine families sync BOTH columns within the scope",
                scoped[0].USearchID == 10 && scoped[1].USearchID == 10,
                scoped[0].USearchID + " , " + scoped[1].USearchID);
            Check("a family the search hid keeps its UserID untouched",
                scoped[2].UserID == 30, scoped[2].UserID.ToString());
            Check("and the hidden family keeps its USearchID untouched too (not synced)",
                scoped[2].USearchID == 30, scoped[2].USearchID.ToString());

            // Empty search: whole universe. The serine families still merge; nothing is out of scope.
            var whole = Db();
            ApplyScopedWordRecluster(whole, "", 0.85);
            Check("with an empty search the recluster is the whole universe (serine families still merge)",
                whole[0].UserID == 10 && whole[1].UserID == 10 && whole[2].UserID == 30,
                whole[0].UserID + " , " + whole[1].UserID + " , " + whole[2].UserID);

            // ScopeItems itself: it returns the WHOLE family of any matching member, not only the matching
            // proteins. A family whose members' descriptions differ but that shares one searchable member
            // must arrive complete.
            var multi = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|serine1|X", USearchID = 1, UserID = 1,
                    Description = "Serine protease heavy chain" },
                new FastaItemExtended { SequenceIdentifier = "sp|light|X", USearchID = 1, UserID = 1,
                    Description = "light chain fragment" },   // same family, no "serine" in its own text
                new FastaItemExtended { SequenceIdentifier = "sp|other|X", USearchID = 2, UserID = 2,
                    Description = "Myoglobin" },
            };
            var scopeItems = WordCluster.ScopeItems(multi, "serine");
            Check("ScopeItems pulls in the whole family of a matching member, not just the matching row",
                scopeItems.Count == 2 && scopeItems.All(i => i.UserID == 1),
                scopeItems.Count + " items");
            Check("ScopeItems leaves an unrelated family out of the scope",
                !scopeItems.Any(i => i.UserID == 2));
            Check("ScopeItems on an empty search returns the whole set",
                WordCluster.ScopeItems(multi, "").Count == 3);
        }

        /// <summary>
        /// Ana (2026-10-24): "metalloprotease, SVMP" in Search listed nothing, and Super Cluster could not
        /// take "ribosomal subunit" or "ribosomal protein" as phrases. Search now ORs comma-separated
        /// terms; a quoted Super Cluster keyword is matched as consecutive words of the description.
        /// </summary>
        private static void SearchTermsAndKeywordPhrasesTests()
        {
            // Search terms: commas and semicolons separate, spaces inside a term stay, quotes (straight or
            // curly) and empty pieces go, duplicates differing only in case collapse.
            var terms = FamilyFilter.SearchTerms(" metalloprotease, SVMP ;; “ribosomal protein”, svmp,");
            Check("Search splits at commas and semicolons, keeps inner spaces, drops quotes and empties",
                terms.SequenceEqual(new[] { "metalloprotease", "SVMP", "ribosomal protein" }),
                string.Join(" | ", terms));
            Check("one term without a comma is the old single-text search",
                FamilyFilter.SearchTerms("serine protease").SequenceEqual(new[] { "serine protease" }));
            Check("an empty or comma-only search has no terms",
                FamilyFilter.SearchTerms("").Count == 0 && FamilyFilter.SearchTerms(" , ; ").Count == 0);

            List<FastaItemExtended> Db() => new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|M1|X", USearchID = 1, UserID = 1,
                    Description = "Zinc metalloprotease OS=x OX=1" },
                new FastaItemExtended { SequenceIdentifier = "tr|SVMP3|X", USearchID = 2, UserID = 2,
                    Description = "Snake venom metalloproteinase OS=x OX=1" },
                new FastaItemExtended { SequenceIdentifier = "sp|R1|X", USearchID = 3, UserID = 3,
                    Description = "60S ribosomal protein L7 OS=x OX=1" },
                new FastaItemExtended { SequenceIdentifier = "sp|R2|X", USearchID = 4, UserID = 4,
                    Description = "40S ribosomal subunit protein S3 OS=x OX=1" },
                new FastaItemExtended { SequenceIdentifier = "sp|R4|X", USearchID = 7, UserID = 7,
                    Description = "40S ribosomal subunit protein S6 OS=x OX=1" },
                new FastaItemExtended { SequenceIdentifier = "sp|R3|X", USearchID = 5, UserID = 5,
                    Description = "Ribosomal RNA small subunit methyltransferase OS=x OX=1" },
                new FastaItemExtended { SequenceIdentifier = "sp|P1|X", USearchID = 6, UserID = 6,
                    Description = "Acidic phospholipase A2 OS=x OX=1" },
            };

            // The locus counts too: SVMP3 is found by its identifier, its description says
            // "metalloproteinase", which "metalloprotease" does not match.
            var shown = FamilyFilter.Apply(Db(), "metalloprotease, SVMP", false, false);
            Check("\"metalloprotease, SVMP\" shows the rows carrying EITHER term (description or locus)",
                shown.Select(i => i.UserID).OrderBy(i => i).SequenceEqual(new[] { 1, 2 }),
                string.Join(",", shown.Select(i => i.UserID)));
            var phraseSearch = FamilyFilter.Apply(Db(), "ribosomal protein", false, false);
            Check("a term with a space is still searched as that text (no split at spaces)",
                phraseSearch.Select(i => i.UserID).SequenceEqual(new[] { 3 }),
                string.Join(",", phraseSearch.Select(i => i.UserID)));

            var scope = WordCluster.ScopeItems(Db(), "metalloprotease, SVMP");
            Check("reclustering scope follows every comma-separated term",
                scope.Select(i => i.UserID).OrderBy(i => i).SequenceEqual(new[] { 1, 2 }),
                string.Join(",", scope.Select(i => i.UserID)));

            // Keyword parsing: quoted text is one phrase, the rest splits as before.
            var keywords = WordCluster.ParseKeywords("\"Ribosomal  Subunit\", “ribosomal protein” SVMP, toxin");
            Check("ParseKeywords keeps quoted phrases whole and splits the rest at commas and spaces",
                keywords.SequenceEqual(new[] { "ribosomal subunit", "ribosomal protein", "svmp", "toxin" }),
                string.Join(" | ", keywords));
            Check("ParseKeywords without quotes is the old comma/space split",
                WordCluster.ParseKeywords("metalloproteinase, SVMP protease")
                    .SequenceEqual(new[] { "metalloproteinase", "svmp", "protease" }));
            Check("ParseKeywords on an empty box gives no keyword",
                WordCluster.ParseKeywords("  ").Count == 0);

            // ANY of two phrases: the three ribosomal-protein families join; the rRNA methyltransferase,
            // which has "ribosomal" and "subunit" but not together, does not.
            const string twoPhrases = "\"ribosomal subunit\", \"ribosomal protein\"";
            var any = new WordCluster(Db(), new List<string>())
                .MatchKeywords(WordCluster.ParseKeywords(twoPhrases), KeywordMatch.Any);
            Check("ANY of two quoted phrases matches the ribosomal-protein families only (3,4,7, not 5)",
                any.Matched.SetEquals(new[] { 3, 4, 7 }));

            // "protein" is a stop word, so as a single keyword it matches nothing (unchanged), yet inside
            // a phrase it counts: that is why phrases read the full description.
            var stopWord = new WordCluster(Db(), new List<string>())
                .MatchKeywords(WordCluster.ParseKeywords("protein"), KeywordMatch.Any);
            Check("a stop word alone still matches nothing, as before",
                stopWord.Matched.Count == 0);

            // ALL of the two phrases: no single description holds both, so nothing merges.
            var all = new WordCluster(Db(), new List<string>())
                .MatchKeywords(WordCluster.ParseKeywords(twoPhrases), KeywordMatch.All);
            Check("ALL of two phrases needs both in one description, so nothing matches here",
                all.Matched.Count == 0);

            // A phrase must be consecutive and in order: "subunit ribosomal" matches nothing.
            var reversed = new WordCluster(Db(), new List<string>())
                .MatchKeywords(WordCluster.ParseKeywords("\"subunit ribosomal\""), KeywordMatch.Any);
            Check("a quoted phrase is matched in its own word order only",
                reversed.Matched.Count == 0);

            // A phrase and a single word under ALL: both on the same description.
            var mixed = new WordCluster(Db(), new List<string>())
                .MatchKeywords(WordCluster.ParseKeywords("\"ribosomal subunit\" 40s"), KeywordMatch.All);
            Check("ALL with a phrase and a word matches the families carrying both (4,7)",
                mixed.Matched.SetEquals(new[] { 4, 7 }));
            var mixedMiss = new WordCluster(Db(), new List<string>())
                .MatchKeywords(WordCluster.ParseKeywords("\"ribosomal subunit\" 60s"), KeywordMatch.All);
            Check("ALL with a phrase and a word that never meet matches nothing",
                mixedMiss.Matched.Count == 0);
            var mixedAny = new WordCluster(Db(), new List<string>())
                .MatchKeywords(WordCluster.ParseKeywords("\"ribosomal protein\" metalloprotease"), KeywordMatch.Any);
            Check("ANY with a phrase and a single word finds families matching either (1,3)",
                mixedAny.Matched.SetEquals(new[] { 1, 3 }));

            // An exclude word still disqualifies a family a phrase matched.
            var excluded = new WordCluster(Db(), new List<string>())
                .MatchKeywords(WordCluster.ParseKeywords(twoPhrases), KeywordMatch.Any,
                                 new List<string> { "60s" });
            Check("an exclude word removes a family a phrase matched (60S drops family 3)",
                excluded.Matched.Except(excluded.Excluded).OrderBy(i => i).SequenceEqual(new[] { 4, 7 }));

            SuperClusterExcludeBoxTests();
        }

        /// <summary>
        /// Ana, 2026-10-31: the Super Cluster exclude box split on whitespace only, so "inhibitor, ADAM,
        /// matrix, thrombospondin, ATP" reached the matcher as "inhibitor," and so on, and only the last
        /// word, the one without a comma, excluded anything. The names below are real Viperidae entries.
        /// </summary>
        private static void SuperClusterExcludeBoxTests()
        {
            List<int> Joined(List<FastaItemExtended> items, string excludeBox, List<string>? reclusterExclude = null)
            {
                var found = new WordCluster(items, reclusterExclude ?? new List<string>()).MatchKeywords(
                    WordCluster.ParseKeywords(ViperKeywords), KeywordMatch.Any, WordCluster.ParseKeywords(excludeBox));
                return found.Matched.Except(found.Excluded).OrderBy(i => i).ToList();
            }

            Check("the exclude box reaches the matcher as clean words when typed with commas",
                WordCluster.ParseKeywords(AnaExcludeBox).SequenceEqual(new[] { "inhibitor", "adam", "matrix", "thrombospondin", "atp" }),
                string.Join(" | ", WordCluster.ParseKeywords(AnaExcludeBox)));
            Check("every kind of separator splits it the same way, a pasted tab or no-break space included",
                WordCluster.ParseKeywords("inhibitor;matrix\tatp thrombospondin\nadam")
                    .OrderBy(w => w, StringComparer.Ordinal)
                    .SequenceEqual(new[] { "adam", "atp", "inhibitor", "matrix", "thrombospondin" }));

            List<int> joined = Joined(Vipers(), AnaExcludeBox);
            Check("Ana's list keeps out the inhibitor, matrix, thrombospondin and ATP families",
                joined.SequenceEqual(new[] { 1, 2, 7, 8 }), string.Join(",", joined));
            Check("the family-level merge: one member carrying an exclude word keeps its whole family out (family 9)",
                !joined.Contains(9));
            Check("matching stays on whole words: 'adam' does not remove the adamalysin SVMP (family 8)",
                joined.Contains(8));

            // The ADAM proteins carry no word "ADAM" before OS=; this is how to name them.
            List<int> hyphen = Joined(Vipers(), AnaExcludeBox + ", domain-containing");
            Check("a hyphenated exclude word matches, split as the descriptions are (family 7 out)",
                hyphen.SequenceEqual(new[] { 1, 2, 8 }), string.Join(",", hyphen));
            List<int> quoted = Joined(Vipers(), AnaExcludeBox + ", \"domain containing\"");
            Check("and so does the same thing as a quoted phrase",
                quoted.SequenceEqual(new[] { 1, 2, 8 }), string.Join(",", quoted));
            List<int> outOfOrder = Joined(Vipers(), "\"containing domain\"");
            Check("a phrase in the wrong order excludes nothing", outOfOrder.Contains(7), string.Join(",", outOfOrder));

            List<int> stopWord = Joined(Vipers(), "protein");
            Check("a stop word excludes too: it is ignored for comparing, not for this rule (family 7 out)",
                !stopWord.Contains(7) && stopWord.Contains(1), string.Join(",", stopWord));
            List<int> alsoIgnored = Joined(Vipers(), "inhibitor", new List<string> { "inhibitor" });
            Check("a word also listed in the Recluster exclude box still keeps its families out",
                !alsoIgnored.Contains(3) && !alsoIgnored.Contains(9), string.Join(",", alsoIgnored));
        }

        private const string ViperKeywords = "metalloproteinase, metalloprotease";
        private const string AnaExcludeBox = "inhibitor, ADAM, matrix, thrombospondin, ATP";

        private static FastaItemExtended ViperItem(string id, string description, int family) => new FastaItemExtended
        {
            SequenceIdentifier = id, Description = description + " OS=Crotalus adamanteus GN=X", UserID = family, USearchID = family,
        };

        /// <summary>Real Viperidae names, as UniProt writes them.</summary>
        private static List<FastaItemExtended> Vipers() => new List<FastaItemExtended>
        {
            ViperItem("sp|A|1", "Snake venom metalloproteinase", 1),
            ViperItem("sp|B|2", "Zinc metalloproteinase/disintegrin", 2),
            ViperItem("sp|C|3", "Metalloproteinase inhibitor 1", 3),
            ViperItem("sp|D|4", "Matrix metalloproteinase-14", 4),
            ViperItem("sp|E|5", "A disintegrin and metalloproteinase with thrombospondin motifs 5", 5),
            ViperItem("sp|F|6", "ATP-dependent zinc metalloprotease FTSH", 6),
            ViperItem("sp|G|7", "Disintegrin and metalloproteinase domain-containing protein 10", 7),
            ViperItem("sp|H|8", "Snake venom metalloproteinase adamalysin-2", 8),
            ViperItem("sp|I|9", "Metalloproteinase", 9),
            ViperItem("sp|J|9", "Metalloproteinase inhibitor 3", 9),
            ViperItem("sp|K|10", "Serine protease inhibitor", 10),
            new FastaItemExtended { SequenceIdentifier = "Reverse_sp|A|1", Description = "", UserID = 1, USearchID = 1 },
        };

        /// <summary>
        /// The inverse document frequency has to stay positive.
        ///
        /// log(N / (1 + df)) reaches exactly zero when a term sits in N-1 of the N families, so a family
        /// whose whole description is made of such words gets an all-zero vector. The guard that skips
        /// normalising a zero-length vector then leaves it at zero, every dot product against it is zero,
        /// and the family cannot merge with anything at any threshold the interface offers, not even with
        /// a family whose description is byte for byte the same.
        /// </summary>
        private static void IdfWeightingTests()
        {
            // Five families. "venom" appears in four of them, which is exactly the N-1 that zeroes the
            // weight. The first two carry no other word.
            var items = new[]
            {
                ("Venom", 0), ("Venom", 1), ("Venom alpha", 2), ("Venom beta", 3), ("Keratin", 4),
            }.Select(t => new FastaItemExtended
            {
                SequenceIdentifier = "sp|P" + t.Item2 + "|X",
                Description = t.Item1 + " OS=Bothrops jararaca OX=8724",
                USearchID = t.Item2,
                UserID = t.Item2,
            }).ToList();

            var clusters = new WordCluster(items, new List<string>()).GroupByWords(1.0);
            bool together = clusters.Any(c => c.Contains(0) && c.Contains(1));

            Check("two families with the same description merge even when their only word is common",
                together, string.Join(" | ", clusters.Select(c => string.Join(",", c))));

            // The mirror: sharing one word out of several must NOT be enough at the strictest setting.
            var loose = new[]
            {
                ("Snake venom metalloproteinase jararhagin precursor", 0), ("Snake myoglobin", 1),
            }.Select(t => new FastaItemExtended
            {
                SequenceIdentifier = "sp|Q" + t.Item2 + "|X",
                Description = t.Item1 + " OS=Bothrops jararaca OX=8724",
                USearchID = t.Item2,
                UserID = t.Item2,
            }).ToList();

            var strict = new WordCluster(loose, new List<string>()).GroupByWords(1.0);
            Check("sharing one word out of several does not merge at the strictest threshold",
                strict.Count == 2, string.Join(" | ", strict.Select(c => string.Join(",", c))));
        }

        /// <summary>
        /// Reported from the bench: a family holding both a toxin and its inhibitor was split by hand,
        /// giving the inhibitor rows a new family number; a keyword super cluster on an UNRELATED family
        /// then put them back together and the manual number was gone.
        ///
        /// The cause was that reclustering read USearchID, where the split does not exist, and wrote its
        /// result into UserID, where the split did exist. Both halves are checked here.
        /// </summary>
        private static void ManualSplitSurvivesReclusteringTests()
        {
            // Family 2 as it arrives from USEARCH: phospholipase A2 together with its inhibitor.
            // Family 1 is unrelated and is the one the super cluster will target.
            var items = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|P81243|PA2A_BOTJ",
                    Description = "Acidic phospholipase A2 OS=Bothrops jararaca OX=8724",
                    USearchID = 2, UserID = 2 },
                new FastaItemExtended { SequenceIdentifier = "sp|Q9PRZ0|PA2_BOTJ",
                    Description = "Phospholipase A2 OS=Bothrops jararaca OX=8724",
                    USearchID = 2, UserID = 2 },
                new FastaItemExtended { SequenceIdentifier = "tr|A0A481S6S6|A0A481",
                    Description = "Phospholipase A2 inhibitor OS=Bothrops jararaca OX=8724",
                    USearchID = 2, UserID = 2 },
                new FastaItemExtended { SequenceIdentifier = "sp|B1A4N8|PLIA_BOTJ",
                    Description = "Phospholipase A2 inhibitor OS=Bothrops jararaca OX=8724",
                    USearchID = 2, UserID = 2 },
                new FastaItemExtended { SequenceIdentifier = "sp|P30431|VM3JA_BOTJA",
                    Description = "Snake venom metalloproteinase OS=Bothrops jararaca OX=8724",
                    USearchID = 1, UserID = 1 },
            };

            // The curator separates the two inhibitors into a family of their own, exactly as done in the
            // grid: the editable column writes UserID and leaves USearchID alone.
            items[2].UserID = 500;
            items[3].UserID = 500;

            var beforeSplit = FamilyFilter.CountFamilies(items);
            Check("a manual split is visible to the family counter", beforeSplit == 3, $"{beforeSplit}");

            // Everything that asks the file "what are the families" has to give the same answer, or the
            // FDR button and the counter disagree about the same file. Neither of the two regressions
            // this covers was caught by any existing check: both were found by reading.
            var split = new FamilyClusterFile { MyExtendedFastaItems = items };
            Check("the file's own grouping follows the manual split too",
                split.GroupedFastaItems.Count == 3, $"{split.GroupedFastaItems.Count}");
            Check("and so does the target/decoy grouping score",
                split.CalculateGroupingScore().TotalGroups == 3,
                $"{split.CalculateGroupingScore().TotalGroups}");

            var matches = new WordCluster(items, new List<string>())
                .MatchKeywords(new List<string> { "phospholipase" });
            Check("keyword matching sees the hand-made family, not the USEARCH one",
                matches.Matched.Contains(500));
            Check("the toxin and the inhibitor are matched as two separate families",
                matches.Matched.SetEquals(new[] { 2, 500 }));

            var unrelated = new SuperClusterPreview(items, null, "metalloproteinase", "",
                KeywordMatch.Any, null, null);
            unrelated.Join(items);

            int toxin = items[0].UserID;
            int inhibitor = items[2].UserID;
            Check("after the unrelated super cluster the split still holds", toxin != inhibitor,
                $"toxin {toxin}, inhibitor {inhibitor}");
            Check("both inhibitor rows stayed together", items[2].UserID == items[3].UserID);
            Check("both toxin rows stayed together", items[0].UserID == items[1].UserID);
            Check("the original USEARCH grouping is left untouched as a record",
                items.All(i => i.USearchID == (i.SequenceIdentifier.Contains("VM3JA") ? 1 : 2)));

            // And the benign direction: a keyword that DOES match the split-off family must still absorb it.
            var items2 = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "A", Description = "Acidic phospholipase A2",
                    USearchID = 2, UserID = 2 },
                new FastaItemExtended { SequenceIdentifier = "B", Description = "Phospholipase A2 inhibitor",
                    USearchID = 2, UserID = 500 },
            };
            var joined = new SuperClusterPreview(items2, null, "phospholipase", "", KeywordMatch.Any, null, null);
            joined.Join(items2);
            Check("a keyword that matches both still merges them",
                items2.Select(i => i.UserID).Distinct().Count() == 1);
        }
    }
}
