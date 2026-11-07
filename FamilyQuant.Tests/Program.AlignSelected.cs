using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Alignment;
using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AlignSelectedCommands() =>
    [
        new("--test-align-selected", 10100, TestCommandPhase.Raw, args => args.Contains("--test-align-selected", StringComparer.Ordinal), (args, _) =>
        {
            int at = Array.IndexOf(args, "--test-align-selected");
            AlignSelectedTests(at + 1 < args.Length && args[at + 1] != "." ? args[at + 1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>Z12g3 Align selected: the pairwise and center-star alignment, the choice of reviewed relatives,
    /// the button, the window and its copies, on invented sequences only. With <paramref name="output"/>
    /// the window is also saved in both themes at the two harness sizes.</summary>
    private static void AlignSelectedTests(string? output = null)
    {
        Section("Align selected - pairwise alignment with free end gaps");
        const string Base = "MKTAYIAKQRQISFVKSHFSRQ";
        var same = ProteinAlignment.Pairwise(Base, Base);
        Check("identical sequences align without gaps", same.A == Base && same.B == Base);
        string inserted = Base[..10] + "WWWW" + Base[10..];
        var insertion = ProteinAlignment.Pairwise(Base, inserted);
        Check("an insertion opens one gap of its length in the other sequence",
            insertion.A == Base[..10] + "----" + Base[10..] && insertion.B == inserted, insertion.A + " / " + insertion.B);
        var insertionIdentity = ProteinAlignment.Identity(insertion.A, insertion.B);
        Check("an internal gap counts in the identity", insertionIdentity.Identical == 22 && insertionIdentity.Compared == 26,
            insertionIdentity.Identical + "/" + insertionIdentity.Compared);
        string fragment = Base.Substring(5, 12);
        var free = ProteinAlignment.Pairwise(Base, fragment);
        Check("a fragment sits inside the full protein with free end gaps",
            free.A == Base && free.B == new string('-', 5) + fragment + new string('-', 5), free.B);
        var fragmentIdentity = ProteinAlignment.Identity(free.A, free.B);
        Check("end gaps do not count: a fragment of the same protein is 100% identical",
            fragmentIdentity.Percent == 100 && fragmentIdentity.Compared == 12);
        var reversed = ProteinAlignment.Pairwise(fragment, Base);
        Check("the overhang is free on either side", reversed.A == new string('-', 5) + fragment + new string('-', 5) && reversed.B == Base);
        string substituted = Base[..6] + "W" + Base[7..];
        var oneChange = ProteinAlignment.Identity(Base, substituted);
        Check("one substitution in 22 residues gives 21 of 22 identical columns",
            oneChange.Identical == 21 && oneChange.Compared == 22 && Math.Abs(oneChange.Percent!.Value - 2100.0 / 22) < 1e-9);
        Check("two strings that share no residue column have no identity", ProteinAlignment.Identity("AC--", "--DE").Percent == null);
        var repeat = ProteinAlignment.Pairwise(Base, inserted);
        Check("the same inputs always give the same alignment", repeat == insertion);

        Section("Align selected - center star on the first protein");
        string center = "MSDKLCAAGHWERTYPLKCIVNMQEDFG";
        var star = ProteinAlignment.CenterStar(new[]
        {
            Seq("first", center, new AlignmentDomain("PF-a", "Supplied domain", 3, 8)),
            Seq("two", center[..12] + "WW" + center[12..]),
            Seq("three", center[..12] + "YYY" + center[12..]),
            Seq("piece", center.Substring(4, 15)),
        });
        Check("every row has the alignment's width", star.Rows.All(r => r.Gapped.Length == star.Columns) && star.Columns == center.Length + 3);
        Check("every row without its gaps is exactly its input sequence",
            star.Rows.All(r => r.Gapped.Replace("-", "") == r.Input.Sequence));
        Check("the first protein keeps its residues in order and only gains gap columns",
            star.Rows[0].Gapped == center[..12] + "---" + center[12..], star.Rows[0].Gapped);
        Check("insertions of different lengths share the widest gap (once a gap, always a gap)",
            star.Rows[1].Gapped == center[..12] + "WW-" + center[12..] && star.Rows[2].Gapped == center[..12] + "YYY" + center[12..]);
        Check("the first protein has no identity to itself; the others have theirs",
            star.Rows[0].IdentityPercent == null && star.Rows[3].IdentityPercent == 100
            && star.Rows[1].IdenticalColumns == center.Length && star.Rows[1].ComparedColumns == center.Length + 2);
        var cysteineColumns = ProteinAlignment.CysteineColumns(star);
        Check("cysteine columns are every column where a row holds a cysteine",
            cysteineColumns.SequenceEqual(Enumerable.Range(0, star.Columns).Where(c => star.Rows.Any(r => r.Gapped[c] == 'C'))) && cysteineColumns.Count == 2);
        Check("cysteines are counted per sequence", star.Rows[0].Cysteines == 2 && star.Rows[3].Cysteines == 2);
        Check("a domain span is mapped to the columns of its residues",
            star.Rows[0].Domains.Single() is { FirstColumn: 2, LastColumn: 7 } && star.Rows[1].Domains.Count == 0);
        Check("more than 25 sequences are refused", Throws(() => ProteinAlignment.CenterStar(
            Enumerable.Range(0, 26).Select(i => Seq("s" + i, center)).ToArray())));
        Check("a gap character in an input is refused", Throws(() => ProteinAlignment.CenterStar(new[] { Seq("gapped", "MK-LV") })));
        string fasta = star.ToFasta();
        Check("the FASTA has one header and one gapped line per row",
            fasta == string.Concat(star.Rows.Select(r => ">" + r.Input.Label + "\n" + r.Gapped + "\n")));
        string text = star.ToAlignedText(10);
        Check("the aligned text ends each line with the residue number reached",
            text.Split('\n')[0] == "first  MSDKLCAAGH  10" && text.Contains("piece  ----LCAAGH  6", StringComparison.Ordinal), text.Split('\n')[0]);

        Section("Align selected - choosing reviewed relatives");
        var fixture = AlignFixture();
        var file = fixture.File;
        string before = file.ToJsonText();
        var session = new EvidenceGroupingSession(file, "Z12g3 fixture", (_, _) => fixture.Search);
        var input = session.Propose("hydrolase");
        int query = fixture.QueryIndex;
        var queryRow = session.Proposal!.Proteins.Single(p => p.ProteinIndex == query);
        Check("the fixture reproduces the case: the query joins by its neighbour and is flagged for its cysteines",
            queryRow.Family == AlignFamily && queryRow.Tier == EvidenceTier.ReviewedNeighbour
            && queryRow.Flags.Any(f => f.Message.Contains("18 cysteines", StringComparison.Ordinal)),
            queryRow.Tier + "; " + string.Join(" | ", queryRow.Flags.Select(f => f.Message)));
        var set = AlignmentCandidates.Find(file, session.Proposal, new[] { query });
        var byAccession = set.Candidates.ToDictionary(c => c.Accession);
        Check("the highlighted protein comes first, then its nearest reviewed protein",
            set.Candidates[0].Origin == AlignmentOrigin.Highlighted && set.Candidates[0].Accession == "A0A9N0Q001"
            && set.Candidates[1].Accession == "P9N002" && set.Candidates[1].IsNeighbour && set.Candidates[1].Preselected);
        Check("members are the reviewed proteins whose memberships include the family, from the file and the panel",
            set.Candidates.Skip(1).All(c => c.IsMemberOf(AlignFamily) && c.Reviewed)
            && set.Candidates.Count(c => c.Origin == AlignmentOrigin.File) == 3 && set.Candidates.Count(c => c.Origin == AlignmentOrigin.Panel) == 9);
        Check("a membership listed among several counts", byAccession.ContainsKey("P9N108"));
        Check("another family, an unreviewed protein, a decoy and a contaminant are not offered",
            !byAccession.ContainsKey("P9N050") && !byAccession.ContainsKey("P9N150") && !byAccession.ContainsKey("A0A9N0Q002")
            && set.Candidates.All(c => !c.Identifier.StartsWith(SequenceLabels.DecoyPrefix, StringComparison.Ordinal)
                && !SequenceLabels.IsContaminant(c.Identifier)));
        var preselected = set.Candidates.Where(c => c.Preselected).Select(c => c.Accession).ToArray();
        Check("defaults: the protein, its neighbour, then five complete members, the file's first, closest in length",
            preselected.SequenceEqual(new[] { "A0A9N0Q001", "P9N002", "P9N001", "P9N003", "P9N102", "P9N103", "P9N101" }),
            string.Join(",", preselected));
        Check("a fragment is offered but not preselected", byAccession["P9N109"].Fragment && !byAccession["P9N109"].Preselected);
        Check("Pfam spans only where stored positions match the sequence",
            byAccession["P9N001"].Domains.Count == 2 && byAccession["A0A9N0Q001"].Domains.Count == 2
            && byAccession["P9N103"].Domains.Count == 0 && byAccession["P9N103"].DomainNote == "Stored positions are for another sequence"
            && byAccession["P9N002"].DomainNote == "No positions stored");
        var copy = FamilyClusterFile.FromJsonText(before);
        copy.EvidenceSource!.ReferencePanel = null;
        var withoutPanel = AlignmentCandidates.Find(copy, session.Proposal, new[] { query });
        Check("without a stored panel only the file's relatives are offered, and the window says so",
            withoutPanel.Candidates.All(c => c.Origin != AlignmentOrigin.Panel)
            && withoutPanel.Notes.Any(n => n.Contains("No reference panel", StringComparison.Ordinal)));
        var two = AlignmentCandidates.Find(file, session.Proposal, new[] { query, fixture.Indices["P9N003"] });
        Check("several highlighted proteins keep their order, the first one first",
            two.Candidates[0].Accession == "A0A9N0Q001" && two.Candidates[1].Accession == "P9N003" && two.Candidates[1].Origin == AlignmentOrigin.Highlighted);

        var model = new AlignmentViewModel(set);
        var aligned = model.Alignment!;
        Check("the window aligns the preselected sequences in list order", aligned.Rows.Count == 7
            && aligned.Rows.Select(r => r.Input.Accession).SequenceEqual(preselected));
        Check("on the fixture every row still reads as its input", aligned.Rows.All(r => r.Gapped.Replace("-", "") == r.Input.Sequence));
        Check("every column with a cysteine of the query is a cysteine column",
            Enumerable.Range(0, aligned.Columns).Where(c => aligned.Rows[0].Gapped[c] == 'C').All(ProteinAlignment.CysteineColumns(aligned).Contains));
        Check("identities show for ticked rows only, the first marked First",
            model.Candidates[0].Identity == "First" && model.Candidates[1].Identity.EndsWith("%", StringComparison.Ordinal)
            && model.Candidates.Where(c => !c.Ticked).All(c => c.Identity.Length == 0));
        model.Candidates[0].Ticked = false;
        Check("the first protein cannot be unticked", model.Candidates[0].Ticked);
        var fragmentRow = model.Candidates.Single(c => c.Candidate.Accession == "P9N109");
        fragmentRow.Ticked = true;
        Check("ticking a relative realigns with it", model.Alignment!.Rows.Count == 8 && fragmentRow.Identity.Length > 0);
        fragmentRow.Ticked = false;
        Check("unticking it realigns without it", model.Alignment!.Rows.Count == 7 && fragmentRow.Identity.Length == 0);
        var crowded = new AlignmentViewModel(new AlignmentCandidateSet(Enumerable.Range(0, 30).Select(i => set.Candidates[0] with
        {
            Label = "row" + i, Accession = "row" + i, Preselected = i < 25,
        }).ToArray(), set.Families, set.DefaultFamily, Array.Empty<string>()));
        crowded.Candidates[27].Ticked = true;
        Check("a 26th sequence is refused with a message", !crowded.Candidates[27].Ticked && crowded.TickedCount == 25
            && crowded.Status.Contains("at most 25", StringComparison.Ordinal));
        Check("the proposal and the file are unchanged by choosing and aligning", file.ToJsonText() == before);

        Section("Align selected - the button and the window");
        EnsureApplication();
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var review = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null,
                    "", "", KeywordMatch.All, null, null), input, null, initialScope: "hydrolase")
                    { Width = 1366, Height = 820, Left = -30000, Top = -30000,
                        WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false };
                AlignmentWindow? opened = null;
                try
                {
                    var tabs = (TabControl)review.FindName("evidenceTabs");
                    tabs.SelectedIndex = 0;
                    review.Show(); PumpDispatcher();
                    var button = (Button)review.FindName("alignSelectedButton");
                    var grid = (DataGrid)review.FindName("evidenceGrid");
                    var toolbar = (WrapPanel)button.Parent;
                    var buttons = toolbar.Children.OfType<Button>().Select(b => b.Content as string).ToArray();
                    int untick = Array.IndexOf(buttons, "Untick selected");
                    Check(theme + " Align selected sits right after Untick selected", untick >= 0 && untick + 1 < buttons.Length
                        && buttons[untick + 1] == "Align selected", string.Join(",", buttons));
                    Check(theme + " the button is disabled without a source", !button.IsEnabled);
                    review.AlignmentSource = highlighted => AlignmentCandidates.Find(file, session.Proposal, highlighted);
                    grid.SelectedItems.Clear(); PumpDispatcher();
                    Check(theme + " the button is disabled with no highlighted row", !button.IsEnabled);
                    var evidence = ((GroupingReviewViewModel)review.DataContext).Evidence!;
                    evidence.SelectedFamily = null; PumpDispatcher();
                    // Changing the family filter may move the details pane to another row; highlight only the query.
                    grid.SelectedItems.Clear(); PumpDispatcher();
                    var row = grid.Items.OfType<ReviewProteinRow>().Single(p => p.ProteinIndex == query);
                    grid.SelectedItems.Add(row); PumpDispatcher();
                    Check(theme + " only the protein the user highlighted is highlighted", grid.SelectedItems.Count == 1 && ReferenceEquals(grid.SelectedItems[0], row));
                    Check(theme + " the button is enabled once a row is highlighted", button.IsEnabled);
                    bool tickBefore = row.Selected;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                    opened = review.OpenAlignmentWindow;
                    Check(theme + " clicking it opens the alignment window on the highlighted protein", opened != null
                        && opened.Model.Candidates[0].Candidate.Identifier == file.MyExtendedFastaItems[query].SequenceIdentifier && opened.Owner == review);
                    if (opened == null) continue;
                    Check(theme + " opening it changes no tick of the review", row.Selected == tickBefore);
                    string? copied = null;
                    AlignmentWindow.ClipboardOverride = value => copied = value;
                    try
                    {
                        ((Button)opened.FindName("copyFastaButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(theme + " Copy as FASTA copies the gapped rows, first protein first", copied == opened.Model.FastaText
                            && copied!.StartsWith(">A0A9N0Q001\n", StringComparison.Ordinal) && copied.Split('\n').Length == 2 * 7 + 1);
                        ((Button)opened.FindName("copyTextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(theme + " Copy aligned text copies the readable blocks", copied == opened.Model.AlignedText
                            && copied!.StartsWith("A0A9N0Q001  ", StringComparison.Ordinal));
                    }
                    finally { AlignmentWindow.ClipboardOverride = null; }
                    if (theme == Themes.AppTheme.Dark) CheckTooltipCoverage("Align selected window", (FrameworkElement)opened.Content);
                    var view = (AlignmentView)opened.FindName("alignmentView");
                    opened.Left = -30000; opened.Top = -30000; opened.ShowActivated = false;
                    foreach (var (width, height) in new[] { (1366, 820), (1024, 768) })
                    {
                        opened.Width = width; opened.Height = height; PumpDispatcher();
                        var content = (FrameworkElement)opened.Content;
                        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                        Check(theme + " " + width + "x" + height + " the alignment wraps to the width in tens of columns",
                            view.ColumnsPerLine >= 30 && view.ColumnsPerLine % 10 == 0 && view.ActualHeight > 0, view.ColumnsPerLine.ToString());
                        if (output != null)
                        {
                            Directory.CreateDirectory(output);
                            System.Threading.Thread.Sleep(250); PumpDispatcher();
                            ScreenHarness.Save(opened, Path.Combine(output, "z12g3-align-" + theme.ToString().ToLowerInvariant() + "-" + width + "x" + height + ".png"), width, height);
                        }
                    }
                    int firstCysteine = opened.Model.Alignment!.Rows[0].Gapped.IndexOf('C');
                    int cysteineNumber = opened.Model.Alignment.Rows[0].Gapped[..(firstCysteine + 1)].Count(ch => ch != '-');
                    string? under = view.CellCenter(0, firstCysteine) is Point cell ? view.Describe(cell) : null;
                    Check(theme + " the pointer names the residue and column under it (the first extra cysteine, before the first domain)",
                        cysteineNumber == 10 && under == "A0A9N0Q001: C10, column " + (firstCysteine + 1), under);
                    Check(theme + " the pointer outside the residues names nothing", view.Describe(new Point(1, 1)) == null);
                    Check(theme + " the file is unchanged after the window", file.ToJsonText() == before);
                }
                finally
                {
                    opened?.Close();
                    review.Close(); PumpDispatcher();
                }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }

        static AlignmentSequence Seq(string label, string sequence, params AlignmentDomain[] domains) =>
            new(label, label, "test", sequence, domains);
        static bool Throws(Action action)
        {
            try { action(); return false; }
            catch (ArgumentException) { return true; }
        }
    }

    private const string AlignFamily = "5'-nucleotidase family";

    private sealed record AlignFixtureData(FamilyClusterFile File, NeighbourSearch Search, int QueryIndex, IReadOnlyDictionary<string, int> Indices);

    /// <summary>Invented sequences only: a family of eight-cysteine members made from one ancestor, and an
    /// unreviewed protein of the same family carrying ten extra cysteines (18 in all).</summary>
    private static AlignFixtureData AlignFixture()
    {
        uint state = 20261106;
        int Next(int range) { state = state * 1664525 + 1013904223; return (int)((state >> 8) % (uint)range); }
        const string Residues = "ADEFGHIKLMNPQRSTVWY";
        var ancestor = new StringBuilder("M");
        while (ancestor.Length < 330) ancestor.Append(Residues[Next(Residues.Length)]);
        foreach (int site in new[] { 21, 48, 77, 102, 160, 214, 251, 298 }) ancestor[site] = 'C';
        string Variant(int changes, int trimStart = 0, int trimEnd = 0, string? insert = null)
        {
            var copy = new StringBuilder(ancestor.ToString());
            for (int i = 0; i < changes; i++)
            {
                int at = 1 + Next(copy.Length - 1);
                if (copy[at] != 'C') copy[at] = Residues[Next(Residues.Length)];
            }
            if (insert != null) copy.Insert(190, insert);
            string text = copy.ToString();
            return text.Substring(trimStart, text.Length - trimStart - trimEnd);
        }
        var positionsSource = new PositionEvidenceSource { Provider = "fixture", Release = "fixture" };
        ProteinPositionEvidence Positions(string sequence, string? hashOf = null) => new()
        {
            SequenceLength = sequence.Length, SequenceSha256 = ProteinPositionFetcher.SequenceHash(hashOf ?? sequence), DomainSource = positionsSource,
            Pfam = new() { new() { Accession = "PF00149", Name = "Metallophos", Start = 12, End = 180 },
                new() { Accession = "PF02872", Name = "5_nucleotid_C", Start = 205, End = Math.Min(300, sequence.Length) } },
        };
        ProteinEvidence Evidence(string accession, bool reviewed, string family, bool fragment = false, params string[] memberships) => new()
        {
            Accession = accession, Reviewed = reviewed, Family = family, Fragment = fragment, Keywords = new() { "Hydrolase" },
            FamilyMemberships = memberships.Length == 0 ? null : memberships.Select(m => new EvidenceFamilyMembership { Family = m }).ToList(),
            Pfam = new() { new() { Accession = "PF00149", Name = "Metallophos" }, new() { Accession = "PF02872", Name = "5_nucleotid_C" } },
        };
        static ProteinEvidence WithPositions(ProteinEvidence evidence, ProteinPositionEvidence positions) { evidence.Positions = positions; return evidence; }
        FastaItemExtended Protein(string identifier, string sequence, ProteinEvidence? evidence, string description) =>
            new() { SequenceIdentifier = identifier, Description = description, Sequence = sequence, UserID = 1, USearchID = 1, Evidence = evidence };

        string member1 = Variant(40), member2 = Variant(30, insert: "GSG"), member3 = Variant(55, trimEnd: 12);
        var query = new StringBuilder(Variant(70));
        foreach (int site in new[] { 9, 35, 63, 90, 131, 177, 196, 233, 270, 312 }) query[site] = 'C';
        string querySequence = query.ToString();
        var proteins = new List<FastaItemExtended>
        {
            Protein("sp|P9N001|5NT1_SYNTH", member1, WithPositions(Evidence("P9N001", true, AlignFamily), Positions(member1)), "Invented 5'-nucleotidase one"),
            Protein("sp|P9N002|5NT2_SYNTH", member2, Evidence("P9N002", true, AlignFamily), "Invented 5'-nucleotidase two"),
            Protein("tr|A0A9N0Q001|A0A9N0Q001_SYNTH", querySequence, WithPositions(Evidence("A0A9N0Q001", false, ""), Positions(querySequence)), "Invented uncharacterized protein with 18 cysteines"),
            Protein("sp|P9N003|5NT3_SYNTH", member3, Evidence("P9N003", true, AlignFamily), "Invented 5'-nucleotidase three"),
            Protein("sp|P9N050|APY1_SYNTH", Variant(160), Evidence("P9N050", true, "Apyrase family"), "Invented apyrase"),
            Protein("tr|A0A9N0Q002|A0A9N0Q002_SYNTH", Variant(90), Evidence("A0A9N0Q002", false, AlignFamily), "Invented unreviewed relative"),
            Protein(SequenceLabels.DecoyPrefix + "sp|P9N001|5NT1_SYNTH", new string(member1.Reverse().ToArray()), null, "Decoy"),
            Protein(SequenceLabels.ContaminantPrefix + "sp|P9N060|5NT_CONT", Variant(45), Evidence("P9N060", true, AlignFamily), "Invented contaminant"),
        };
        var panel = new ReferencePanel { Provider = "fixture", Release = "fixture", Families = new() { AlignFamily, "Apyrase family" } };
        void Panel(string accession, string sequence, ProteinEvidence evidence, ProteinPositionEvidence? positions = null)
        {
            evidence.Positions = positions;
            panel.Entries.Add(new PanelEntry { Accession = accession, Sequence = sequence, Evidence = evidence });
        }
        Panel("P9N101", Variant(60, trimStart: 4), Evidence("P9N101", true, AlignFamily));
        Panel("P9N102", Variant(65, insert: "DG"), Evidence("P9N102", true, AlignFamily));
        string stale = Variant(62, trimEnd: 3);
        Panel("P9N103", stale, Evidence("P9N103", true, AlignFamily), Positions(stale, hashOf: stale + "K"));
        Panel("P9N104", Variant(66, trimEnd: 6), Evidence("P9N104", true, AlignFamily));
        Panel("P9N105", Variant(75, insert: "NGSGT"), Evidence("P9N105", true, AlignFamily));
        Panel("P9N106", Variant(80, trimStart: 1, trimEnd: 20), Evidence("P9N106", true, AlignFamily));
        Panel("P9N107", Variant(85, insert: "PKDSAGENTTH"), Evidence("P9N107", true, AlignFamily));
        Panel("P9N108", Variant(70, trimEnd: 25), Evidence("P9N108", true, AlignFamily, false, "Apyrase family", AlignFamily));
        Panel("P9N109", Variant(50).Substring(60, 140), Evidence("P9N109", true, AlignFamily, fragment: true));
        Panel("P9N150", Variant(150), Evidence("P9N150", true, "Apyrase family"));
        panel.Entries.Sort((a, b) => string.CompareOrdinal(a.Accession, b.Accession));
        var file = new FamilyClusterFile { MyExtendedFastaItems = proteins, EvidenceSource = new EvidenceProvenance { Provider = "fixture", Release = "fixture", ReferencePanel = panel } };
        var search = new NeighbourSearch
        {
            Neighbours = new List<NearestNeighbour>
            {
                new() { Query = proteins[2].SequenceIdentifier, Accession = "P9N002", Reference = proteins[1].SequenceIdentifier, Family = AlignFamily,
                    IdentityPercent = 72.4, AlignmentLength = 333, QueryLength = querySequence.Length, ReferenceLength = member2.Length,
                    QueryAlignedResidues = querySequence.Length },
            },
        };
        var indices = proteins.Select((p, i) => (p, i)).ToDictionary(x => NearestReviewed.EntryAccession(x.p.SequenceIdentifier) + (NearestReviewed.IsDecoy(x.p) ? "-decoy" : ""), x => x.i);
        return new AlignFixtureData(file, search, 2, indices);
    }
}
