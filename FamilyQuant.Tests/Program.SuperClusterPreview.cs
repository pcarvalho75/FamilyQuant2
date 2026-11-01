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
        /// What Create Super Cluster shows before it changes anything. Ana had to guess how proteins are named,
        /// and asked for rows that are proteins, so she can drop a disintegrin without losing the entries that
        /// carry her words.
        /// </summary>
        private static void SuperClusterPreviewTests()
        {
            SuperClusterPreview Preview(string exclude = AnaExcludeBox, ISet<int>? search = null, IEnumerable<int>? locked = null,
                                        string keywords = ViperKeywords, KeywordMatch match = KeywordMatch.Any,
                                        List<FastaItemExtended>? items = null) =>
                new SuperClusterPreview(items ?? Vipers(), search, keywords, exclude, match, null, locked);
            static string Ids(IEnumerable<int> ids) => string.Join(",", ids.OrderBy(i => i));
            static string Loci(SuperClusterPreview from) =>
                string.Join(" ", from.Proteins.Where(x => x.Join).Select(x => x.Locus).OrderBy(x => x, StringComparer.Ordinal));
            static SuperClusterPreviewProtein Row(SuperClusterPreview from, string locus) => from.Proteins.First(x => x.Locus == locus);

            SuperClusterPreview p = Preview();
            Check("the rows are the proteins that would join", Loci(p) == "sp|A|1 sp|B|2 sp|G|7 sp|H|8 sp|I|9", Loci(p));
            Check("an exclude word keeps its protein out and leaves its neighbour in the family alone",
                p.Proteins.All(x => x.Locus != "sp|J|9") && p.JoiningFamilies.Contains(9), Ids(p.JoiningFamilies));
            Check("the headline counts proteins and families, decoys not among them, and says nothing has changed",
                p.Headline.Contains("5 proteins from 5 families", StringComparison.Ordinal)
                && p.Headline.Contains("Nothing has changed", StringComparison.Ordinal) && p.CanCreate, p.Headline);
            Check("each exclude word reports the proteins it kept out, among the matching families (10 never matched)",
                p.KeptOutByWord["inhibitor"] == 2 && p.KeptOutByWord["matrix"] == 1
                && p.KeptOutByWord["thrombospondin"] == 1 && p.KeptOutByWord["atp"] == 1,
                string.Join(", ", p.KeptOutByWord.Select(k => k.Key + "=" + k.Value)));
            Check("a word that kept out nothing is reported as zero, not left out", p.KeptOutByWord["adam"] == 0);
            Check("the note names what each word kept out",
                p.Notes.Contains("Proteins kept out by each exclude word", StringComparison.Ordinal)
                && p.Notes.Contains("inhibitor 2", StringComparison.Ordinal) && p.Notes.Contains("atp 1", StringComparison.Ordinal), p.Notes);
            Check("and says which word kept out nothing, and where to look instead",
                p.Notes.Contains("kept out nothing: adam", StringComparison.Ordinal)
                && p.Notes.Contains("list below", StringComparison.Ordinal), p.Notes);
            Check("no exclude words, no note", WordCluster.DescribeExclusions(new Dictionary<string, int>()).Length == 0);
            SuperClusterPreview species = Preview("crotalus");
            Check("only the text before OS= is read: the species excludes nothing",
                species.KeptOutByWord["crotalus"] == 0 && species.JoiningFamilies.Contains(1), Ids(species.JoiningFamilies));
            Check("a family whose only keyword protein is excluded has nothing left to say it belongs",
                !p.JoiningFamilies.Contains(3) && p.Proteins.All(x => x.Family != 4 && x.Family != 5 && x.Family != 6), Ids(p.JoiningFamilies));

            SuperClusterPreview typed = Preview();
            typed.ExcludeText += ", domain-containing";
            typed.Refresh();
            Check("typing in the preview updates it live", Loci(typed) == "sp|A|1 sp|B|2 sp|H|8 sp|I|9" && typed.KeptOutByWord["domain-containing"] == 1, Loci(typed));

            // The Search box said metallopeptidase; the families named only "metalloprotease" were outside it.
            var searchShows = new HashSet<int> { 1, 7, 10 };
            SuperClusterPreview searched = Preview(search: searchShows);
            Check("with a Search, only the families it shows join", Ids(searched.JoiningFamilies) == "1,7", Ids(searched.JoiningFamilies));
            Check("and the families it hid are counted, excluded proteins not among them",
                searched.HiddenFamilies == 3 && searched.HiddenProteins == 3, searched.HiddenLabel);
            Check("and a family the Search shows without a keyword is reported, singular included",
                searched.SearchFamiliesWithoutKeyword == 1
                && searched.Notes.Contains("1 family shown by the Search box carries no keyword", StringComparison.Ordinal), searched.Notes);
            Check("an exclude word that only hits families the Search hides is still reported as working",
                searched.KeptOutByWord["inhibitor"] == 2 && searched.Notes.Contains("kept out nothing: adam.", StringComparison.Ordinal), searched.Notes);
            searched.IncludeHidden = true;
            searched.Refresh();
            Check("one tick brings the hidden families in", Ids(searched.JoiningFamilies) == "1,2,7,8,9", Ids(searched.JoiningFamilies));

            SuperClusterPreview allHidden = Preview("", search: new HashSet<int> { 10 }, keywords: "metalloproteinase");
            Check("a Search hiding every match says exactly that, not 'no family matches'",
                !allHidden.CanCreate && allHidden.HiddenFamilies == 8
                && allHidden.Headline.StartsWith("Every matching family is hidden", StringComparison.Ordinal), allHidden.Headline);
            allHidden.IncludeHidden = true;
            allHidden.Refresh();
            Check("and the tick it points to solves it", allHidden.CanCreate, allHidden.Headline);
            SuperClusterPreview oneHidden = Preview("", search: new HashSet<int> { 1 }, keywords: "ftsh");
            Check("the hidden label is singular for one protein in one family",
                oneHidden.HiddenLabel == "Also include 1 protein in 1 family that matches but is hidden by the Search box", oneHidden.HiddenLabel);

            SuperClusterPreview hyphen = Preview("", keywords: "domain-containing, adamalysin");
            Check("a hyphenated keyword works, as it does in the exclude box", Ids(hyphen.JoiningFamilies) == "7,8", Ids(hyphen.JoiningFamilies));
            SuperClusterPreview punctuated = new SuperClusterPreview(new List<FastaItemExtended>
            {
                ViperItem("sp|A|1", "Phospholipase A2, group IIG", 1),
                ViperItem("sp|B|2", "Basic phospholipase A2 (Fragment)", 2),
                ViperItem("sp|C|3", "Phospholipase A2", 3),
            }, null, "\"phospholipase a2\"", "fragment", KeywordMatch.Any, null, null);
            Check("punctuation around a word does not hide it from a phrase or an exclude word",
                Ids(punctuated.JoiningFamilies) == "1,3" && punctuated.KeptOutByWord["fragment"] == 1, Ids(punctuated.JoiningFamilies));
            SuperClusterPreview ignoredKeyword = new SuperClusterPreview(Vipers(), null, "metalloproteinase", "", KeywordMatch.Any,
                new[] { "metalloproteinase" }, null);
            Check("a keyword also in the Recluster exclude box is explained, because that box is not on screen here",
                ignoredKeyword.Notes.Contains("Recluster by Words exclude box", StringComparison.Ordinal), ignoredKeyword.Notes);
            var describedDecoys = Vipers();
            describedDecoys.Add(new FastaItemExtended { SequenceIdentifier = "Reverse_sp|Z|99", Description = "Reversed metalloproteinase", UserID = 99, USearchID = 99 });
            Check("a decoy that does carry a description is listed, so it can be unticked",
                Preview("", items: describedDecoys).Proteins.Any(x => x.Locus == "Reverse_sp|Z|99"));
            Check("one with no description is no protein of the class", Preview("").Proteins.All(x => x.Locus != "Reverse_sp|A|1"));

            SuperClusterPreview lockedOne = Preview(locked: new[] { 1 });
            Check("a locked family stays out and the preview says so",
                Ids(lockedOne.JoiningFamilies) == "2,7,8,9" && lockedOne.LockedMatches == 1
                && lockedOne.Notes.Contains("1 matching family is locked", StringComparison.Ordinal), lockedOne.Notes);

            SuperClusterPreview none = Preview(keywords: "nonsense");
            Check("nothing matching says so, and Create is refused",
                !none.CanCreate && none.Headline.StartsWith("No family matches", StringComparison.Ordinal), none.Headline);
            SuperClusterPreview allOfTwo = Preview(keywords: "metalloproteinase zinc nonsense", match: KeywordMatch.All);
            Check("and points out 'match all words' when that is the reason",
                allOfTwo.Headline.Contains("match any word", StringComparison.Ordinal), allOfTwo.Headline);
            Check("an empty keyword box asks for one", !Preview(keywords: " ").CanCreate
                && Preview(keywords: " ").Headline.Contains("at least one keyword", StringComparison.Ordinal));
            SuperClusterPreview one = Preview("", keywords: "ftsh");
            Check("one whole family has nothing to merge with",
                !one.CanCreate && one.Headline.StartsWith("Only one whole family", StringComparison.Ordinal), one.Headline);
            SuperClusterPreview part = Preview("snake disintegrin domain inhibitor matrix", keywords: "metalloproteinase");
            Check("but part of a family can leave to form its own, singular included",
                part.CanCreate && part.Headline.StartsWith("1 protein of one family will become a family of its own", StringComparison.Ordinal), part.Headline);
            SuperClusterPreview allOut = Preview("metalloproteinase metalloprotease");
            Check("excluding everything says that, not 'no family matches'",
                !allOut.CanCreate && allOut.Headline.Contains("leave nothing to join", StringComparison.Ordinal), allOut.Headline);

            // Ana, 2026-10-31: disintegrins listed with none of her words, and no way to drop them alone.
            SuperClusterPreview k = new SuperClusterPreview(Disintegrins(), null, ViperKeywords, "", KeywordMatch.Any, null, null);
            Check("a family with a keyword brings all its proteins, and one without any is not listed",
                Ids(k.JoiningFamilies) == "1181,6458" && k.JoiningProteins == 6 && k.Proteins.All(x => x.Family != 7000),
                Ids(k.JoiningFamilies) + " / " + k.JoiningProteins);
            Check("the proteins with no keyword come first, so they are seen",
                k.Proteins.Take(3).All(x => !x.HasKeyword) && k.Proteins.Skip(3).All(x => x.HasKeyword));
            SuperClusterPreviewProtein viridin = Row(k, "sp|P31987|VM2I_CROVV");
            Check("a row is the protein as the database has it",
                viridin.Description == "Disintegrin viridin OS=Crotalus adamanteus GN=X" && viridin.Family == 1181, viridin.Description);
            Check("and says it has no keyword, and how many of its family do",
                !viridin.HasKeyword && viridin.Keyword == "No" && viridin.FamilyKeywords == "2 of 4" && viridin.FamilyCarriers == 2,
                viridin.Keyword + " " + viridin.FamilyKeywords);
            Check("a fragment keeps its (Fragment), because that is the database's text",
                Row(k, "sp|P0DW30|VM2_METMX").Description.Contains("(Fragment)", StringComparison.Ordinal));
            Check("and the note counts them and gives the reason",
                k.ProteinsWithoutKeyword == 3 && k.Notes.Contains("3 proteins in red carry no keyword: they are here because", StringComparison.Ordinal), k.Notes);

            viridin.Join = false;
            k.ApplyTicks();
            Check("a disintegrin unticked leaves the entries with the words where they were: her request",
                k.JoiningProteins == 5 && Ids(k.JoiningFamilies) == "1181,6458" && Row(k, "tr|Q7T1T3|Q7T1T3_BOTJR").Join, k.Headline);
            Check("and the note says where it stays, singular included",
                k.UntickedProteins == 1 && k.Notes.Contains("1 unticked protein stays in its family.", StringComparison.Ordinal), k.Notes);
            k.ExcludeText = "cc8b";
            k.Refresh();
            Check("a tick survives editing the boxes", !Row(k, "sp|P31987|VM2I_CROVV").Join && k.Proteins.All(x => x.Locus != "sp|P83044|DID8B_CERCE"), Loci(k));
            k.ExcludeText = "";
            k.Refresh();
            k.TickAll();
            Check("and Tick all brings everything back", k.JoiningProteins == 6 && k.UntickedProteins == 0);
            k.UntickWithoutKeyword();
            Check("Untick the red ones leaves exactly the proteins that carry a word",
                k.JoiningProteins == 3 && k.ProteinsWithoutKeyword == 0 && k.Proteins.Where(x => x.Join).All(x => x.HasKeyword), Loci(k));
            k.TickAll();
            k.SetTicks(k.Proteins.Where(x => x.Family == 6458), false);
            Check("ticks can be set on any selection of rows", k.JoiningProteins == 4 && Ids(k.JoiningFamilies) == "1181", Ids(k.JoiningFamilies));
            Check("one whole family alone has nothing to merge with", !k.CanCreate, k.Headline);
            k.SetTicks(new[] { Row(k, "sp|P31987|VM2I_CROVV") }, false);
            Check("but what is left of it can leave it", k.CanCreate && k.Headline.StartsWith("3 proteins of one family", StringComparison.Ordinal), k.Headline);

            SuperClusterPreview both = new SuperClusterPreview(Disintegrins(), null, "zinc metalloproteinase", "", KeywordMatch.All, null, null);
            Check("with 'match all words' a protein carries the keywords only when it carries them all",
                Ids(both.JoiningFamilies) == "1181" && both.Proteins.Count(x => x.HasKeyword) == 1 && Row(both, "sp|O93517|VM3S4_GLOBR").HasKeyword, Loci(both));

            // Domains: what a protein contains, whatever it is called.
            List<FastaItemExtended> withDomains = Disintegrins();
            withDomains[0].Domains = "Reprolysin";
            withDomains[1].Domains = "ADAM_CR; Disintegrin; Reprolysin";
            withDomains[2].Domains = "Disintegrin";
            withDomains[4].Domains = "Reprolysin";
            SuperClusterPreview d = new SuperClusterPreview(withDomains, null, ViperKeywords, "", KeywordMatch.Any, null, null);
            Check("a file with domains says so, and a file without does not", d.HasDomains && !Preview().HasDomains);
            Check("whatever the boxes show right now", new SuperClusterPreview(withDomains, null, "", "", KeywordMatch.Any, null, null).HasDomains);
            Check("the domains on offer are listed, most proteins first",
                string.Join(" ", d.DomainsOnOffer.Select(x => x.Name + ":" + x.Proteins)) == "Reprolysin:3 Disintegrin:2 ADAM_CR:1",
                string.Join(" ", d.DomainsOnOffer.Select(x => x.Name + ":" + x.Proteins)));
            d.KeepDomains("reprolysin");
            Check("Keep domains unticks what has none of them, in any case",
                !Row(d, "sp|P31987|VM2I_CROVV").Join && Row(d, "sp|O93517|VM3S4_GLOBR").Join, Loci(d));
            Check("a protein with no domains, too short for Pfam, stays if it carries a word and goes if it does not",
                !Row(d, "sp|P83044|DID8B_CERCE").Join && !Row(d, "sp|P0DW30|VM2_METMX").Join, Loci(d));
            d.TickAll();
            Row(d, "tr|Q7T1T3|Q7T1T3_BOTJR").Join = false;
            d.KeepDomains("Reprolysin, Disintegrin");
            Check("several domains are alternatives, and a tick taken away by hand is not put back",
                Row(d, "sp|P31987|VM2I_CROVV").Join && !Row(d, "tr|Q7T1T3|Q7T1T3_BOTJR").Join, Loci(d));
            d.KeepDomains("  ");
            Check("an empty box changes nothing", Row(d, "sp|P31987|VM2I_CROVV").Join);
            List<FastaItemExtended> shortOne = Disintegrins();
            shortOne[0].Domains = "Reprolysin";
            shortOne[1].Domains = "Reprolysin";
            shortOne[4].Domains = "Reprolysin";
            shortOne.Add(ViperItem("sp|S|1", "Metalloproteinase (Fragment)", 1181));
            SuperClusterPreview shortFragment = new SuperClusterPreview(shortOne, null, ViperKeywords, "", KeywordMatch.Any, null, null);
            shortFragment.KeepDomains("Reprolysin");
            Check("so a fragment too short for Pfam survives on its words", Row(shortFragment, "sp|S|1").Join);

            // Join: what is on screen is what moves.
            List<FastaItemExtended> joinItems = Disintegrins();
            joinItems.Add(new FastaItemExtended { SequenceIdentifier = "Reverse_sp|X|1", Description = "", UserID = 6458, USearchID = 6458 });
            int Family(List<FastaItemExtended> from, string id) => from.First(i => i.SequenceIdentifier == id).UserID;
            SuperClusterPreview partial = new SuperClusterPreview(joinItems, null, ViperKeywords, "", KeywordMatch.Any, null, null);
            partial.UntickWithoutKeyword();
            SuperClusterJoin moved = partial.Join(joinItems);
            int partialTarget = Family(joinItems, "tr|Q7T1T3|Q7T1T3_BOTJR");
            Check("part of a family moves to a number nobody uses, or it would pull the rest in",
                partialTarget == 7001 && moved.Family == 7001 && moved.SplitFamilies && moved.Moved.Count == 3 && Family(joinItems, "sp|O93517|VM3S4_GLOBR") == 7001 && Family(joinItems, "tr|E9JGH6|E9JGH6_ECHCS") == 7001, partialTarget.ToString());
            Check("and every unticked protein stays in its family",
                Family(joinItems, "sp|P31987|VM2I_CROVV") == 1181 && Family(joinItems, "sp|P83044|DID8B_CERCE") == 6458
                && Family(joinItems, "Reverse_sp|X|1") == 6458 && Family(joinItems, "sp|P0DM77|VM2_DEIAC") == 7000);

            List<FastaItemExtended> wholeItems = Disintegrins();
            wholeItems.Add(new FastaItemExtended { SequenceIdentifier = "Reverse_sp|X|1", Description = "", UserID = 6458, USearchID = 6458 });
            SuperClusterPreview whole = new SuperClusterPreview(wholeItems, null, ViperKeywords, "", KeywordMatch.Any, null, null);
            SuperClusterJoin wholeJoin = whole.Join(wholeItems);
            Check("whole families keep the smallest number, and a decoy goes with its family as it always did",
                wholeItems.Where(i => i.UserID == 1181).Count() == 7 && Family(wholeItems, "sp|P0DM77|VM2_DEIAC") == 7000 && !wholeJoin.SplitFamilies);

            List<FastaItemExtended> staleLock = Disintegrins();
            staleLock.First(i => i.SequenceIdentifier == "sp|P0DM77|VM2_DEIAC").USearchID = 8000;
            SuperClusterPreview stale = new SuperClusterPreview(staleLock, null, ViperKeywords, "", KeywordMatch.Any, null, new[] { 7001, 9000 });
            stale.UntickWithoutKeyword();
            Check("a new number clashes with nothing, not a lock outliving its family and not the USearch column",
                stale.Join(staleLock).Family == 9001, Family(staleLock, "tr|Q7T1T3|Q7T1T3_BOTJR").ToString());
            List<FastaItemExtended> onlyLocks = Disintegrins();
            SuperClusterPreview highId = new SuperClusterPreview(onlyLocks, null, ViperKeywords, "", KeywordMatch.Any, null, new[] { 8000 });
            highId.UntickWithoutKeyword();
            highId.Join(onlyLocks);
            HashSet<int> kept = new HashSet<int> { 8001 };
            List<List<int>> reclustered = new WordCluster(onlyLocks, new List<string>()).GroupByWords(0.10, kept);
            Check("and once locked, a Recluster by Words cannot put back what stayed out, even at a low threshold",
                reclustered.Single(g => g.Contains(8001)).Count == 1 && Family(onlyLocks, "sp|P31987|VM2I_CROVV") != 8001);

            SuperClusterPreview warned = new SuperClusterPreview(Disintegrins(), null, ViperKeywords, "", KeywordMatch.Any, null, null);
            Check("nothing is split, so nothing is said about locks", !warned.Notes.Contains("will be locked", StringComparison.Ordinal), warned.Notes);
            warned.UntickWithoutKeyword();
            Check("a split says the new family will be locked, before Create", warned.Notes.Contains("the new family will be locked", StringComparison.Ordinal), warned.Notes);
            List<FastaItemExtended> annotatedItems = Disintegrins();
            annotatedItems[0].UserAnnotation = "SVMP";
            SuperClusterPreview annotated = new SuperClusterPreview(annotatedItems, null, ViperKeywords, "", KeywordMatch.Any, null, null);
            annotated.UntickWithoutKeyword();
            Check("and warns when the family left behind keeps an annotation that may no longer fit",
                annotated.Notes.Contains("1 family that gives only part carries an annotation", StringComparison.Ordinal), annotated.Notes);

            List<FastaItemExtended> mixedItems = Disintegrins();
            SuperClusterPreview mixed = new SuperClusterPreview(mixedItems, null, ViperKeywords, "", KeywordMatch.Any, null, null);
            Row(mixed, "sp|P31987|VM2I_CROVV").Join = false;
            mixed.ApplyTicks();
            mixed.Join(mixedItems);
            Check("one whole family gives the number while another gives only part, and the part left behind is not pulled in",
                Family(mixedItems, "tr|E9JGH6|E9JGH6_ECHCS") == 6458 && Family(mixedItems, "tr|Q7T1T3|Q7T1T3_BOTJR") == 6458
                && Family(mixedItems, "sp|P31987|VM2I_CROVV") == 1181, Family(mixedItems, "tr|Q7T1T3|Q7T1T3_BOTJR").ToString());
        }

        /// <summary>Families 1181 and 6458 of Ana's Viperidae file, cut down, and a family with no keyword.</summary>
        private static List<FastaItemExtended> Disintegrins() => new List<FastaItemExtended>
        {
            ViperItem("tr|Q7T1T3|Q7T1T3_BOTJR", "Metalloprotease BOJUMET III (Fragment)", 1181),
            ViperItem("sp|O93517|VM3S4_GLOBR", "Zinc metalloproteinase/disintegrin", 1181),
            ViperItem("sp|P31987|VM2I_CROVV", "Disintegrin viridin", 1181),
            ViperItem("sp|P0DW30|VM2_METMX", "Disintegrin atropoimin (Fragment)", 1181),
            ViperItem("tr|E9JGH6|E9JGH6_ECHCS", "Metalloproteinase", 6458),
            ViperItem("sp|P83044|DID8B_CERCE", "Disintegrin CC8B", 6458),
            ViperItem("sp|P0DM77|VM2_DEIAC", "Disintegrin accutin", 7000),
        };

        /// <summary>The real button, the real boxes, the real merge: Cancel changes nothing, and Create does
        /// exactly what was shown, in one undo step.</summary>
        private static void SuperClusterPreviewUiTests(string workDir)
        {
            string fcfPath = Path.Combine(workDir, "super_cluster_preview.fcf");
            new FamilyClusterFile { ClusteringIdentity = 70, MyExtendedFastaItems = Vipers() }.Save(fcfPath);

            var control = new ClusterGenerationControl(fcfPath);
            FamilyClusterFile fcf = control.fcf;
            var keywordBox = UiField<TextBox>(control, "TextBoxSuperClusterWording")!;
            var excludeBox = UiField<TextBox>(control, "TextBoxSuperClusterExclude")!;
            var matchBox = UiField<ComboBox>(control, "ComboBoxSuperClusterMatch")!;
            var searchBox = UiField<TextBox>(control, "TextBoxSearch")!;
            var undoButton = UiField<Button>(control, "ButtonUndo")!;
            var click = typeof(ClusterGenerationControl).GetMethod("ButtonSuperCluster_Click",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var undoClick = typeof(ClusterGenerationControl).GetMethod("ButtonUndo_Click",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            string Numbers() => string.Join(",", fcf.MyExtendedFastaItems.Select(i => i.UserID));
            string before = Numbers();

            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                bool Click(Func<SuperClusterPreview, bool> decide, Func<bool> done)
                {
                    bool asked = false;
                    control.ConfirmSuperClusterOverride = p => { asked = true; return decide(p); };
                    click.Invoke(control, new object?[] { null, null });
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (!(asked && done()) && watch.Elapsed < TimeSpan.FromSeconds(30)) { PumpDispatcher(); Thread.Sleep(5); }
                    PumpDispatcher();
                    return asked;
                }

                keywordBox.Text = ViperKeywords;
                excludeBox.Text = AnaExcludeBox;
                matchBox.SelectedIndex = 1;

                SuperClusterPreview? seen = null;
                bool asked = Click(p => { seen = p; return false; }, () => true);
                Check("Create Super Cluster shows the preview, carrying the boxes as typed",
                    asked && seen != null && seen.ExcludeText == AnaExcludeBox && seen.Match == KeywordMatch.Any);
                Check("the exclude box typed with commas reaches the matcher as clean words",
                    seen != null && seen.ExcludeEntries.SequenceEqual(new[] { "inhibitor", "adam", "matrix", "thrombospondin", "atp" }),
                    seen == null ? "-" : string.Join(" | ", seen.ExcludeEntries));
                Check("Cancel changes nothing: no number moved and nothing to undo",
                    Numbers() == before && !undoButton.IsEnabled && !fcf.IsKeywordSuperCluster, Numbers());

                List<string>? promised = null;
                Click(p =>
                {
                    p.ExcludeText += ", domain-containing";
                    p.Refresh();
                    p.Proteins.First(n => n.Locus == "sp|B|2").Join = false;
                    p.ApplyTicks();
                    promised = p.Proteins.Where(x => x.Join).Select(x => x.Locus).OrderBy(x => x, StringComparer.Ordinal).ToList();
                    return true;
                }, () => fcf.IsKeywordSuperCluster && undoButton.IsEnabled);

                int Family(string id) => fcf.MyExtendedFastaItems.First(i => i.SequenceIdentifier == id).UserID;
                Check("what was promised is these three proteins", promised != null && promised.SequenceEqual(new[] { "sp|A|1", "sp|H|8", "sp|I|9" }),
                    promised == null ? "-" : string.Join(" ", promised));
                Check("Create moves exactly what was shown, and part of a family can come", Family("sp|A|1") == Family("sp|H|8") && Family("sp|A|1") == Family("sp|I|9"), Numbers());
                Check("the unticked protein, the word typed in the preview and the exclude words all stayed where they were",
                    Family("sp|B|2") == 2 && Family("sp|G|7") == 7 && Family("sp|C|3") == 3 && Family("sp|D|4") == 4 && Family("sp|E|5") == 5
                    && Family("sp|F|6") == 6 && Family("sp|J|9") == 9 && Family("sp|K|10") == 10, Numbers());
                Check("a moved protein has both its numbers in step, as after any automatic grouping",
                    fcf.MyExtendedFastaItems.First(i => i.SequenceIdentifier == "sp|I|9").USearchID == Family("sp|I|9"));
                Check("what was typed in the preview stays in the box for next time",
                    excludeBox.Text.EndsWith("domain-containing", StringComparison.Ordinal), excludeBox.Text);
                Check("part of family 9 left it, so the new family is locked, and only that one",
                    fcf.LockedUserIds.SetEquals(new[] { Family("sp|A|1") }), string.Join(",", fcf.LockedUserIds));

                undoClick.Invoke(control, new object?[] { null, null });
                Check("one Undo reverses it, lock included", Numbers() == before && fcf.LockedUserIds.Count == 0, Numbers());

                string keywordsBefore = keywordBox.Text;
                string excludeBefore = excludeBox.Text;
                keywordBox.Text = "snake";
                excludeBox.Text = string.Empty;
                PumpDispatcher();
                Click(p => true, () => fcf.IsKeywordSuperCluster && undoButton.IsEnabled);
                Check("whole families joining lock nothing, as a merge never did", Family("sp|A|1") == Family("sp|H|8") && fcf.LockedUserIds.Count == 0, Numbers());
                undoClick.Invoke(control, new object?[] { null, null });
                keywordBox.Text = keywordsBefore;
                excludeBox.Text = excludeBefore;

                // The Search box shows families 1 and 8; family 2 matches but is hidden, and must not move.
                searchBox.Text = "Snake venom";
                excludeBox.Text = AnaExcludeBox + ", domain-containing";
                PumpDispatcher();
                Click(p => { searchBox.Text = string.Empty; return true; }, () => fcf.IsKeywordSuperCluster && undoButton.IsEnabled);
                FastaItemExtended hiddenFamily = fcf.MyExtendedFastaItems.First(i => i.SequenceIdentifier == "sp|B|2");
                Check("with a Search the merge stays inside it, even if the box is cleared before confirming",
                    Family("sp|A|1") == Family("sp|H|8") && hiddenFamily.UserID == 2 && hiddenFamily.USearchID == 2, Numbers());
                undoClick.Invoke(control, new object?[] { null, null });
                Check("and Undo reverses that one too", Numbers() == before, Numbers());

                searchBox.Text = string.Empty;
                fcf.LockedUserIds.Add(8);
                Click(p => { p.Refresh(); return true; }, () => fcf.IsKeywordSuperCluster && undoButton.IsEnabled);
                Check("a locked family stays out of the real merge, and is still locked",
                    Family("sp|H|8") == 8 && Family("sp|A|1") == Family("sp|B|2") && fcf.LockedUserIds.Contains(8), Numbers());
                undoClick.Invoke(control, new object?[] { null, null });
                fcf.LockedUserIds.Clear();
                Check("and Undo puts the numbers back", Numbers() == before, Numbers());

                // The Search box hides family 2; the preview's tick brings it in and nothing else moves.
                searchBox.Text = "Snake venom";
                PumpDispatcher();
                Click(p => { p.IncludeHidden = true; p.Refresh(); return true; },
                    () => fcf.IsKeywordSuperCluster && undoButton.IsEnabled);
                Check("the hidden family joins when ticked", Family("sp|A|1") == Family("sp|B|2") && Family("sp|A|1") == Family("sp|H|8"), Numbers());
                Check("and a family that matched nothing keeps its number", Family("sp|K|10") == 10, Numbers());
            }
            finally
            {
                control.ConfirmSuperClusterOverride = null;
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            // The window itself: the same sweeps as every other window of ours, and live.
            var preview = new SuperClusterPreview(Vipers(), new HashSet<int> { 1, 7, 10 }, ViperKeywords, AnaExcludeBox,
                KeywordMatch.Any, null, null);
            var window = new SuperClusterPreviewWindow(preview);
            var content = (FrameworkElement)window.Content;
            CheckTooltipCoverage("SuperClusterPreviewWindow", content);
            CheckLayout("the super cluster preview at the smallest screen", content, new Size(window.MinWidth, window.MinHeight), -1);
            CheckLayout("the super cluster preview at its opening size", content, new Size(window.Width, window.Height), -1);
            var windowBoxes = WalkTree(content).OfType<TextBox>().Where(x => x.TemplatedParent == null).ToList();
            var windowTexts = WalkTree(content).OfType<TextBlock>().ToList();
            Check("the window opens with the keywords and the exclude words in its boxes",
                windowBoxes.Any(x => x.Text == ViperKeywords) && windowBoxes.Any(x => x.Text == AnaExcludeBox),
                string.Join(" | ", windowBoxes.Select(x => x.Text)));
            Check("and the headline on top", windowTexts.Any(t => t.Text.Contains("2 proteins from 2 families", StringComparison.Ordinal)));
            var create = WalkTree(content).OfType<Button>().First(b => (b.Content as string) == "Create super cluster");
            Check("Create is enabled while two families would join", create.IsEnabled);

            // The real controls, not the model behind them.
            LayoutAt(content, new Size(window.Width, window.Height), -1);
            var rowTick = WalkTree(content).OfType<CheckBox>().First(x => HasAncestor<DataGridCell>(x));
            rowTick.IsChecked = false;
            rowTick.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("clicking a row's tick follows at once, down to refusing Create", preview.JoiningFamilies.Count == 1 && !create.IsEnabled,
                preview.Headline);
            rowTick.IsChecked = true;
            rowTick.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("and clicking it again brings the row back", preview.JoiningFamilies.Count == 2 && create.IsEnabled);
            var hiddenTick = WalkTree(content).OfType<CheckBox>().First(x => (x.Content as string)?.StartsWith("Also include", StringComparison.Ordinal) == true);
            hiddenTick.IsChecked = true;
            hiddenTick.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("the hidden-families box brings them in", preview.IncludeHidden && preview.JoiningFamilies.Count == 5, preview.Headline);
            hiddenTick.IsChecked = false;
            hiddenTick.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var windowMatch = WalkTree(content).OfType<ComboBox>().First(x => x.Items.Contains("match all words"));
            windowMatch.SelectedIndex = 0;
            Check("the match choice follows too", preview.Match == KeywordMatch.All, preview.Headline);
            windowMatch.SelectedIndex = 1;
            windowBoxes.First(x => x.Text == AnaExcludeBox).Text = AnaExcludeBox + ", snake";
            Check("typing in the window updates it at once, down to refusing Create",
                preview.JoiningFamilies.Count == 1 && !create.IsEnabled, preview.Headline);
            Check("Confirmed starts false", !window.Confirmed);
            create.IsEnabled = true;
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("and Create is what sets it", window.Confirmed);
        }
    }
}
