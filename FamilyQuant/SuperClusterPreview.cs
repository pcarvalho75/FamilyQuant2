using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using FamilyQuant.Homology;

namespace FamilyQuant
{
    /// <summary>One protein a super cluster would gather, as the database has it, with a tick of its own.</summary>
    public sealed class SuperClusterPreviewProtein : INotifyPropertyChanged
    {
        private bool join = true;

        public string Locus { get; internal set; } = string.Empty;
        public string Description { get; internal set; } = string.Empty;
        public string Domains { get; internal set; } = string.Empty;
        public int Family { get; internal set; }

        /// <summary>Whether this protein carries a keyword itself. One that does not came with its family, and
        /// is flagged so a curator can decide on it. Ana, 2026-10-31, on disintegrins with none of her words.</summary>
        public bool HasKeyword { get; internal set; }
        public string Keyword => HasKeyword ? "Yes" : "No";

        /// <summary>How many proteins of its family carry a keyword, as "2 of 11": why the protein is here.</summary>
        public string FamilyKeywords { get; internal set; } = string.Empty;

        /// <summary>The first number of <see cref="FamilyKeywords"/>, which is what that column sorts on.</summary>
        public int FamilyCarriers { get; internal set; }

        /// <summary>The tick. Unticked, this protein stays in its family and the rest are not affected.</summary>
        public bool Join
        {
            get => join;
            set
            {
                if (join == value) return;
                join = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Join)));
            }
        }

        /// <summary>Its place in the list the preview was built from, which is how ticks survive a rebuild.</summary>
        internal int Position { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>What a Create did: the proteins that changed family, the family they are in now, and
    /// whether any family gave only part of its proteins.</summary>
    public sealed record SuperClusterJoin(List<FastaItemExtended> Moved, int Family, bool SplitFamilies);

    /// <summary>
    /// What Create Super Cluster is about to do, worked out before anything changes.
    ///
    /// Ana, 2026-10-31: she had to guess how proteins are named, type words, merge, and read the outcome
    /// afterwards. "ADAM" excluded nothing, because those proteins are named "Disintegrin and
    /// metalloproteinase domain-containing protein". Neither that nor a Search box hiding "metalloprotease"
    /// is a matching error, and a synonym table would be biology hardcoded for one class, so the answer is to
    /// SHOW the outcome.
    ///
    /// The rows are proteins, not families or names, because she asked to drop a disintegrin without losing
    /// the entries that carry her words. Keywords find the families; every protein of those families is
    /// listed, those with no keyword flagged, and each has a tick. A protein unticked or carrying an exclude
    /// word stays in its family, and the rest of that family still joins. On her file her exclude list
    /// kept 644 of the 657 SVMPs when it acted on whole families, and keeps 655 acting on proteins.
    ///
    /// Pure: no WPF, so the suite drives it headless and the window only displays it. It matches through
    /// <see cref="WordCluster.MatchKeywords"/>. It searches the whole file and then applies the Search box's
    /// families, which is how it can say what the Search hid.
    /// </summary>
    public sealed class SuperClusterPreview
    {
        private readonly List<FastaItemExtended> items;
        private readonly WordCluster words;
        private readonly HashSet<int>? searchFamilies;
        private readonly HashSet<int> locked;
        private readonly HashSet<string> ignored;
        private readonly Dictionary<int, List<Member>> membersByFamily = new Dictionary<int, List<Member>>();
        private readonly HashSet<int> unticked = new HashSet<int>();

        private List<int> offered = new List<int>();
        private HashSet<int> hidden = new HashSet<int>();
        private HashSet<int> carriers = new HashSet<int>();
        private int matchedAndVisible;

        /// <summary>A protein of the file. Position is its place in <see cref="items"/>, which is how
        /// <see cref="KeywordFamilies.MatchedProteins"/> names it.</summary>
        private sealed record Member(int Position, FastaItemExtended Item);

        /// <param name="allItems">Every protein of the file.</param>
        /// <param name="searchFamilies">The families the Search box shows, or null when it is empty.</param>
        /// <param name="ignoredWords">The Recluster by Words exclude box, which also applies here.</param>
        public SuperClusterPreview(IEnumerable<FastaItemExtended> allItems, ISet<int>? searchFamilies,
            string keywordsText, string excludeText, KeywordMatch match, IEnumerable<string>? ignoredWords,
            IEnumerable<int>? lockedFamilies)
        {
            items = allItems.Where(i => i != null).ToList();
            List<string> ignoredList = (ignoredWords ?? Enumerable.Empty<string>()).ToList();
            ignored = new HashSet<string>(ignoredList, StringComparer.OrdinalIgnoreCase);
            words = new WordCluster(items, ignoredList);
            this.searchFamilies = searchFamilies == null ? null : new HashSet<int>(searchFamilies);
            locked = new HashSet<int>(lockedFamilies ?? Enumerable.Empty<int>());

            // A decoy of our databases carries no description, so it is no protein of the class and is not
            // counted. One that does carry a description can match, so it is listed like any other.
            for (int position = 0; position < items.Count; position++)
            {
                FastaItemExtended item = items[position];
                if (string.IsNullOrWhiteSpace(item.Description)
                    && (item.SequenceIdentifier ?? string.Empty).StartsWith(HomologyOptions.DefaultDecoyLabel, StringComparison.Ordinal))
                    continue;

                if (!membersByFamily.TryGetValue(item.UserID, out List<Member>? members))
                    membersByFamily[item.UserID] = members = new List<Member>();
                members.Add(new Member(position, item));
            }

            HasDomains = items.Any(i => !string.IsNullOrEmpty(i.Domains));
            KeywordsText = keywordsText ?? string.Empty;
            ExcludeText = excludeText ?? string.Empty;
            Match = match;
            Refresh();
        }

        public string KeywordsText { get; set; }
        public string ExcludeText { get; set; }
        public KeywordMatch Match { get; set; }

        /// <summary>Also gather the matching families the Search box hides.</summary>
        public bool IncludeHidden { get; set; }

        public List<string> Keywords { get; private set; } = new List<string>();
        public List<string> ExcludeEntries { get; private set; } = new List<string>();

        /// <summary>Every protein on offer: the ones with no keyword first, then by description.</summary>
        public IReadOnlyList<SuperClusterPreviewProtein> Proteins { get; private set; } = new List<SuperClusterPreviewProtein>();

        /// <summary>The families giving at least one protein.</summary>
        public HashSet<int> JoiningFamilies { get; private set; } = new HashSet<int>();
        public int JoiningProteins { get; private set; }

        /// <summary>Proteins in matching families carrying each exclude entry, zero included. The whole file,
        /// not the Search box: a word that only hits hidden families is working, and must not be reported
        /// as having kept out nothing.</summary>
        public Dictionary<string, int> KeptOutByWord { get; private set; } = new Dictionary<string, int>();

        public int LockedMatches { get; private set; }

        /// <summary>Joining proteins that carry no keyword: they came with a protein of their family that does.</summary>
        public int ProteinsWithoutKeyword { get; private set; }

        /// <summary>Proteins on offer that were unticked, and so stay in their families.</summary>
        public int UntickedProteins { get; private set; }

        public int HiddenFamilies => hidden.Count;
        public int HiddenProteins { get; private set; }

        /// <summary>Families the Search box shows that carry no keyword, so they stay as they are.</summary>
        public int SearchFamiliesWithoutKeyword { get; private set; }

        /// <summary>Whether the file has domains at all, whatever the boxes show right now.</summary>
        public bool HasDomains { get; }

        /// <summary>The domains on offer, most proteins first.</summary>
        public List<(string Name, int Proteins)> DomainsOnOffer => Proteins
            .SelectMany(p => ProteinDomains.Names(p.Domains).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count())).OrderByDescending(d => d.Item2)
            .ThenBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// There is something to do: proteins of two families at least, or part of one family, which leaves
        /// it to form a family of its own. One whole family joining only itself changes nothing.
        /// </summary>
        public bool CanCreate => JoiningFamilies.Count >= 2
            || JoiningFamilies.Count == 1 && JoiningProteins < ProteinCount(JoiningFamilies.First());

        /// <summary>Re-reads the boxes and recomputes everything, ticks kept protein by protein.</summary>
        public void Refresh()
        {
            Keywords = WordCluster.ParseKeywords(KeywordsText);
            ExcludeEntries = WordCluster.ParseKeywords(ExcludeText);

            KeywordFamilies found = Keywords.Count == 0 ? new KeywordFamilies()
                : words.MatchKeywords(Keywords, Match, ExcludeEntries);
            carriers = found.MatchedProteins;
            HashSet<int> excluded = new HashSet<int>(found.ProteinsByExcludeEntry.Values.SelectMany(p => p));

            bool InSearch(int family) => searchFamilies == null || searchFamilies.Contains(family);
            bool Visible(int family) => IncludeHidden || InSearch(family);

            // A family is here on a protein that carries a keyword and no exclude word: with every such
            // protein excluded, nothing is left to say the rest of the family belongs.
            bool Qualifies(int family) => membersByFamily.TryGetValue(family, out List<Member>? members)
                && members.Any(m => carriers.Contains(m.Position) && !excluded.Contains(m.Position));

            List<int> qualifying = found.Matched.Where(Qualifies).ToList();
            List<int> candidates = qualifying.Where(f => !locked.Contains(f)).OrderBy(f => f).ToList();
            hidden = new HashSet<int>(candidates.Where(f => !InSearch(f)));
            HiddenProteins = hidden.Sum(f => MembersOf(f).Count(m => !excluded.Contains(m.Position)));
            offered = candidates.Where(Visible).ToList();

            HashSet<int> inMatchedFamilies = new HashSet<int>(found.Matched.SelectMany(MembersOf).Select(m => m.Position));
            KeptOutByWord = ExcludeEntries
                .Where(found.ProteinsByExcludeEntry.ContainsKey)
                .ToDictionary(e => e, e => found.ProteinsByExcludeEntry[e].Count(inMatchedFamilies.Contains), StringComparer.Ordinal);
            LockedMatches = qualifying.Count(f => locked.Contains(f) && Visible(f));
            matchedAndVisible = found.Matched.Count(Visible);
            SearchFamiliesWithoutKeyword = searchFamilies?.Count(f => !found.Matched.Contains(f)) ?? 0;

            var proteins = new List<SuperClusterPreviewProtein>();
            foreach (int family in offered)
            {
                List<Member> members = MembersOf(family);
                int familyCarriers = members.Count(m => carriers.Contains(m.Position));
                string familyKeywords = familyCarriers.ToString(CultureInfo.CurrentCulture) + " of " +
                                        members.Count.ToString(CultureInfo.CurrentCulture);
                proteins.AddRange(members.Where(m => !excluded.Contains(m.Position)).Select(m => new SuperClusterPreviewProtein
                {
                    Position = m.Position,
                    Locus = m.Item.SequenceIdentifier ?? string.Empty,
                    Description = m.Item.Description ?? string.Empty,
                    Domains = m.Item.Domains ?? string.Empty,
                    Family = family,
                    HasKeyword = carriers.Contains(m.Position),
                    FamilyKeywords = familyKeywords,
                    FamilyCarriers = familyCarriers,
                    Join = !unticked.Contains(m.Position),
                }));
            }
            Proteins = proteins.OrderBy(p => p.HasKeyword).ThenBy(p => p.Description, StringComparer.OrdinalIgnoreCase).ToList();

            ApplyTicks();
        }

        /// <summary>Follows the ticks without rebuilding the list, so a click leaves the rows where they are.</summary>
        public void ApplyTicks()
        {
            foreach (SuperClusterPreviewProtein protein in Proteins)
            {
                if (protein.Join) unticked.Remove(protein.Position);
                else unticked.Add(protein.Position);
            }

            List<SuperClusterPreviewProtein> joining = Proteins.Where(p => p.Join).ToList();
            JoiningProteins = joining.Count;
            JoiningFamilies = new HashSet<int>(joining.Select(p => p.Family));
            ProteinsWithoutKeyword = joining.Count(p => !p.HasKeyword);
            UntickedProteins = Proteins.Count - joining.Count;
        }

        /// <summary>Unticks every protein that carries no keyword, the flagged rows.</summary>
        public void UntickWithoutKeyword()
        {
            foreach (SuperClusterPreviewProtein protein in Proteins.Where(p => !p.HasKeyword)) protein.Join = false;
            ApplyTicks();
        }

        public void TickAll()
        {
            foreach (SuperClusterPreviewProtein protein in Proteins) protein.Join = true;
            ApplyTicks();
        }

        public void SetTicks(IEnumerable<SuperClusterPreviewProtein> proteins, bool join)
        {
            foreach (SuperClusterPreviewProtein protein in proteins) protein.Join = join;
            ApplyTicks();
        }

        /// <summary>
        /// Unticks the proteins with none of these domains, typed as the Domains column writes them, separated
        /// by commas or spaces. A protein with no domains at all is too short for Pfam to call, or unknown to
        /// UniProt, which says nothing against it: its words decide, so it stays ticked if it carries a keyword.
        /// Never ticks anything, so a choice made by hand survives.
        /// </summary>
        public void KeepDomains(string domainsText)
        {
            var wanted = new HashSet<string>(WordCluster.ParseKeywords(domainsText), StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0) return;

            foreach (SuperClusterPreviewProtein protein in Proteins)
            {
                List<string> domains = ProteinDomains.Names(protein.Domains);
                bool keep = domains.Count == 0 ? protein.HasKeyword : domains.Any(wanted.Contains);
                if (!keep) protein.Join = false;
            }
            ApplyTicks();
        }

        /// <summary>
        /// Carries out what is on screen. A family joining whole keeps its number if it is the smallest, and
        /// takes along whatever else carries that number, as a merge always did. Part of a family moves out to
        /// a number nobody uses, because taking the number of a family with proteins left behind would pull
        /// those in too. Nothing is matched again, so the merge is the one that was shown. "Nobody" includes
        /// the USearch column and the locks: a lock can outlive its family, and a new family on that number
        /// would arrive locked by hand curation that never happened.
        /// </summary>
        public SuperClusterJoin Join(IReadOnlyCollection<FastaItemExtended> allItems)
        {
            HashSet<int> joining = new HashSet<int>(Proteins.Where(p => p.Join).Select(p => p.Position));
            List<int> whole = JoiningFamilies.Where(f => MembersOf(f).All(m => joining.Contains(m.Position))).ToList();
            int free = allItems.SelectMany(i => new[] { i.UserID, i.USearchID }).Concat(locked).Max() + 1;
            int target = whole.Count > 0 ? whole.Min() : free;

            var moving = new HashSet<FastaItemExtended>(joining.Select(p => items[p]));
            var wholeFamilies = new HashSet<int>(whole);
            List<FastaItemExtended> moved = allItems
                .Where(i => (moving.Contains(i) || wholeFamilies.Contains(i.UserID)) && i.UserID != target).ToList();
            foreach (FastaItemExtended item in moved) item.UserID = target;
            return new SuperClusterJoin(moved, target, whole.Count < JoiningFamilies.Count);
        }

        private List<Member> MembersOf(int family) =>
            membersByFamily.TryGetValue(family, out List<Member>? members) ? members : new List<Member>();

        private int ProteinCount(int family) => MembersOf(family).Count;

        /// <summary>The joining families that keep some of their proteins.</summary>
        private List<int> FamiliesGivingPart => JoiningFamilies
            .Where(f => MembersOf(f).Count > Proteins.Count(p => p.Family == f && p.Join)).ToList();

        private static string N(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
        private static string Plural(int value, string singular, string plural) => N(value) + " " + (value == 1 ? singular : plural);

        /// <summary>The first line of the window.</summary>
        public string Headline
        {
            get
            {
                if (Keywords.Count == 0) return "Type at least one keyword.";
                if (CanCreate)
                    return JoiningFamilies.Count == 1
                        ? Plural(JoiningProteins, "protein", "proteins") + " of one family will become a family of " + (JoiningProteins == 1 ? "its" : "their") + " own. Nothing has changed yet."
                        : Plural(JoiningProteins, "protein", "proteins") + " from " + N(JoiningFamilies.Count) + " families will become one family. Nothing has changed yet.";

                string hiddenHint = HiddenFamilies > 0 && !IncludeHidden
                    ? " Tick the box below to bring in what the Search box hides." : string.Empty;
                if (JoiningFamilies.Count == 1)
                    return "Only one whole family would be left to join, so there is nothing to merge." + hiddenHint;
                if (matchedAndVisible == 0 && hiddenHint.Length > 0)
                    return "Every matching family is hidden by the Search box, so nothing would join." + hiddenHint;
                if (matchedAndVisible > 0 || HiddenFamilies > 0)
                    return "The exclude words, the locks and the unticked proteins leave nothing to join.";

                return "No family matches the keywords. Matching is on whole words of the description before OS=." +
                       (Match == KeywordMatch.All && Keywords.Count > 1
                           ? " With 'match all words' every keyword must be in the same description; choose " +
                             "'match any word' to gather families carrying any one of them."
                           : string.Empty);
            }
        }

        /// <summary>What the exclude words, the locks and the Search box did, one sentence each.</summary>
        public string Notes
        {
            get
            {
                var notes = new List<string>();
                string exclusions = WordCluster.DescribeExclusions(KeptOutByWord);
                if (exclusions.Length > 0) notes.Add(exclusions);
                List<string> ignoredKeywords = Keywords.Where(ignored.Contains).ToList();
                if (ignoredKeywords.Count > 0)
                    notes.Add("In the Recluster by Words exclude box, so ignored here as a keyword: " +
                              string.Join(", ", ignoredKeywords) + ".");
                if (ProteinsWithoutKeyword > 0)
                    notes.Add(ProteinsWithoutKeyword == 1
                        ? "1 protein in red carries no keyword: it is here because another protein of its family does."
                        : N(ProteinsWithoutKeyword) + " proteins in red carry no keyword: they are here because another protein of their family does.");
                if (UntickedProteins > 0)
                    notes.Add(UntickedProteins == 1 ? "1 unticked protein stays in its family."
                        : N(UntickedProteins) + " unticked proteins stay in their families.");
                List<int> givingPart = FamiliesGivingPart;
                if (givingPart.Count > 0)
                    notes.Add("Part of a family leaves it, so the new family will be locked: a Recluster by Words cannot put back what stays out.");
                int annotated = givingPart.Count(f => MembersOf(f).Any(m => !string.IsNullOrWhiteSpace(m.Item.UserAnnotation)));
                if (annotated > 0)
                    notes.Add(Plural(annotated, "family that gives only part carries", "families that give only part carry") +
                              " an annotation: it stays on the proteins left behind, so check that it still fits them.");
                if (LockedMatches > 0)
                    notes.Add(Plural(LockedMatches, "matching family is", "matching families are") +
                              " locked by hand curation and stay" + (LockedMatches == 1 ? "s" : "") + " out.");
                if (SearchFamiliesWithoutKeyword > 0)
                    notes.Add(Plural(SearchFamiliesWithoutKeyword, "family", "families") + " shown by the Search box carr" +
                              (SearchFamiliesWithoutKeyword == 1 ? "ies" : "y") + " no keyword and stay" +
                              (SearchFamiliesWithoutKeyword == 1 ? "s" : "") + " as " +
                              (SearchFamiliesWithoutKeyword == 1 ? "it is." : "they are."));
                return string.Join("\n", notes);
            }
        }

        /// <summary>The label of the box that gathers what the Search box hides.</summary>
        public string HiddenLabel =>
            "Also include " + Plural(HiddenProteins, "protein", "proteins") + " in " + Plural(HiddenFamilies, "family", "families") +
            " that match" + (HiddenFamilies == 1 ? "es" : "") + " but " + (HiddenFamilies == 1 ? "is" : "are") + " hidden by the Search box";
    }
}
