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
        /// The undo snapshot, pure: capture, mutate the ids and the locks, restore, and every item plus
        /// the lock set is byte-back. Then a two-step stack undone in reverse order.
        /// </summary>
        private static void UndoSnapshotTests()
        {
            var fcf = new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|A|X", USearchID = 1, UserID = 1,
                        Description = "Alpha" },
                    new FastaItemExtended { SequenceIdentifier = "sp|B|X", USearchID = 2, UserID = 2,
                        Description = "Beta" },
                    new FastaItemExtended { SequenceIdentifier = "sp|C|X", USearchID = 3, UserID = 500,
                        Description = "Gamma" },   // a hand renumbered, locked family
                },
                WordSimilarity = null,
                IsKeywordSuperCluster = false
            };
            fcf.LockedUserIds.Add(3);
            fcf.LockedUserIds.Add(500);

            var snap = GroupingSnapshot.Capture(fcf);

            // Mutate everything the snapshot records: family numbers, both columns, the lock set, the flags.
            fcf.MyExtendedFastaItems[0].UserID = 99;
            fcf.MyExtendedFastaItems[1].UserID = 99;
            fcf.MyExtendedFastaItems[1].USearchID = 99;
            fcf.MyExtendedFastaItems[2].UserID = 7;
            fcf.LockedUserIds.Clear();
            fcf.LockedUserIds.Add(12345);
            fcf.WordSimilarity = 0.85;
            fcf.IsKeywordSuperCluster = true;

            snap.Restore(fcf);

            Check("restore puts every item's UserID back",
                fcf.MyExtendedFastaItems.Select(i => i.UserID).SequenceEqual(new[] { 1, 2, 500 }),
                string.Join(",", fcf.MyExtendedFastaItems.Select(i => i.UserID)));
            Check("restore puts every item's USearchID back",
                fcf.MyExtendedFastaItems.Select(i => i.USearchID).SequenceEqual(new[] { 1, 2, 3 }),
                string.Join(",", fcf.MyExtendedFastaItems.Select(i => i.USearchID)));
            Check("restore puts the lock set back exactly",
                fcf.LockedUserIds.SetEquals(new[] { 3, 500 }),
                string.Join(",", fcf.LockedUserIds.OrderBy(i => i)));
            Check("restore puts the grouping-provenance flags back",
                fcf.WordSimilarity == null && !fcf.IsKeywordSuperCluster);

            // A literal restore ignores the lock rule: a locked family's id is restored just the same.
            Check("restore is literal - a locked family's id is put back like any other",
                fcf.MyExtendedFastaItems[2].UserID == 500 && fcf.MyExtendedFastaItems[2].USearchID == 3);

            // Two-step stack, undone in reverse order. Step 1 renumbers A; step 2 renumbers B.
            var stack = new List<GroupingSnapshot>();
            var f2 = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "a", USearchID = 1, UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "b", USearchID = 2, UserID = 2 },
                }
            };

            stack.Add(GroupingSnapshot.Capture(f2));       // before step 1: (1,2)
            f2.MyExtendedFastaItems[0].UserID = 10;        // step 1 -> (10,2)
            stack.Add(GroupingSnapshot.Capture(f2));       // before step 2: (10,2)
            f2.MyExtendedFastaItems[1].UserID = 20;        // step 2 -> (10,20)

            // Undo step 2 first (LIFO): back to (10,2).
            stack[stack.Count - 1].Restore(f2);
            stack.RemoveAt(stack.Count - 1);
            Check("undoing the last of two steps returns the second change only (10,2)",
                f2.MyExtendedFastaItems.Select(i => i.UserID).SequenceEqual(new[] { 10, 2 }),
                string.Join(",", f2.MyExtendedFastaItems.Select(i => i.UserID)));

            // Undo step 1: back to (1,2).
            stack[stack.Count - 1].Restore(f2);
            stack.RemoveAt(stack.Count - 1);
            Check("undoing the first step returns to the original (1,2)",
                f2.MyExtendedFastaItems.Select(i => i.UserID).SequenceEqual(new[] { 1, 2 }),
                string.Join(",", f2.MyExtendedFastaItems.Select(i => i.UserID)));
            Check("the undo stack is empty after both steps are taken back", stack.Count == 0);
        }

        /// <summary>
        /// The two defects reported from the bench on build 1.0.8.0, reproduced as they were seen.
        ///
        /// First: after one word reclustering, the UserID column no longer matched anything, because
        /// every cluster was renumbered densely from zero - including families the operation never
        /// touched. Second: a family separated by hand was re-merged by the next word reclustering,
        /// LEGITIMATELY under its similarity rules, because the toxin's and the inhibitor's
        /// descriptions are nearly identical - which is exactly why the split had to be manual.
        /// </summary>
        private static void CurationLockAndStableIdTests()
        {
            // ---- stable ids: untouched families keep their numbers, merges adopt the survivor's.
            var clusters = new List<List<int>>
            {
                new List<int> { 7 },            // untouched
                new List<int> { 2, 9, 30 },     // merged: survivor is 2
                new List<int> { 500 },          // a manual number, untouched
            };
            var map = WordCluster.BuildStableMapping(clusters);
            Check("an untouched family keeps its exact number", map[7] == 7 && map[500] == 500,
                $"7->{map[7]}, 500->{map[500]}");
            Check("a merge adopts the smallest member's number, so it can be followed",
                map[2] == 2 && map[9] == 2 && map[30] == 2);

            // ---- the Ana scenario: toxin and inhibitor share nearly all description words, so at
            // 0.80 word similarity they WILL merge - unless the manual split locked them.
            List<FastaItemExtended> Venom() => new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|P81243|PA2A_BOTJ",
                    Description = "Acidic phospholipase A2 OS=Bothrops jararaca OX=8724",
                    USearchID = 2, UserID = 2 },
                new FastaItemExtended { SequenceIdentifier = "sp|B1A4N8|PLIA_BOTJ",
                    Description = "Phospholipase A2 inhibitor OS=Bothrops jararaca OX=8724",
                    USearchID = 2, UserID = 500 },   // separated by hand
                new FastaItemExtended { SequenceIdentifier = "sp|P30431|VM3JA_BOTJA",
                    Description = "Snake venom metalloproteinase OS=Bothrops jararaca OX=8724",
                    USearchID = 1, UserID = 1 },
            };

            var unlocked = new WordCluster(Venom(), new List<string>()).GroupByWords(0.3);
            Check("without the lock the similarity rules re-merge what the user separated (the report)",
                unlocked.Any(c => c.Contains(2) && c.Contains(500)),
                string.Join(" | ", unlocked.Select(c => string.Join(",", c))));

            var locks = new HashSet<int> { 2, 500 };
            var lockedRun = new WordCluster(Venom(), new List<string>()).GroupByWords(0.3, locks);
            Check("with the lock the hand-made split survives the same reclustering",
                !lockedRun.Any(c => c.Contains(2) && c.Contains(500)));
            Check("and every family still comes out exactly once",
                lockedRun.SelectMany(c => c).OrderBy(i => i).SequenceEqual(new[] { 1, 2, 500 }),
                string.Join(" | ", lockedRun.Select(c => string.Join(",", c))));

            // ---- a locked family never joins a keyword super-cluster either.
            var super = new WordCluster(Venom(), new List<string>())
                .GroupByKeywords(new List<string> { "phospholipase" }, locks);
            Check("a keyword super cluster does not absorb a locked family",
                !super.Any(c => c.Contains(2) && c.Contains(500)));

            // ---- the locks are part of the file, and an old file without them still loads.
            string workDir = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests");
            Directory.CreateDirectory(workDir);
            string path = Path.Combine(workDir, "locks.fcf");
            var file = new FamilyClusterFile { ClusteringIdentity = 70, MyExtendedFastaItems = Venom() };
            file.LockedUserIds.Add(2);
            file.LockedUserIds.Add(500);
            file.Save(path);
            var loaded = FamilyClusterFile.Load(path);
            Check("curation locks survive a save and reload",
                loaded.LockedUserIds.SetEquals(new[] { 2, 500 }),
                string.Join(",", loaded.LockedUserIds.OrderBy(i => i)));

            string legacy = Path.Combine(workDir, "legacy.fcf");
            File.WriteAllText(legacy, File.ReadAllText(path).Replace("\"LockedUserIds\"", "\"SomethingElse\""));

            // A legacy file can only diverge UserID from USearchID by MANUAL editing (the old builds
            // overwrote both on every reclustering), so the divergence arrives locked: the curation
            // someone did before the locks existed must not be the only curation they do not cover.
            var migrated = FamilyClusterFile.Load(legacy);
            Check("a legacy file's manual edits arrive locked, on both sides of the split",
                migrated.LockedUserIds.SetEquals(new[] { 2, 500 }),
                string.Join(",", migrated.LockedUserIds.OrderBy(i => i)));

            // Build 1.0.8.0 wrote only UserID and renumbered densely, so a file it saved AFTER a
            // reclustering diverges on nearly every row. Inferring curation from that would lock
            // nearly every family permanently - the blocker the final pre-deploy review caught. The
            // persisted reclustering witnesses veto the inference.
            string reclustered = Path.Combine(workDir, "legacy_reclustered.fcf");
            var v108 = new FamilyClusterFile { ClusteringIdentity = 70, MyExtendedFastaItems = Venom() };
            v108.WordSimilarity = 0.8;   // what a 1.0.8.0 word reclustering left behind
            v108.MyExtendedFastaItems[0].UserID = 0;
            v108.MyExtendedFastaItems[1].UserID = 1;
            v108.MyExtendedFastaItems[2].UserID = 2;
            v108.Save(reclustered);
            File.WriteAllText(reclustered,
                File.ReadAllText(reclustered).Replace("\"LockedUserIds\"", "\"SomethingElse\""));
            Check("a legacy file that went through a reclustering gets NO inferred locks",
                FamilyClusterFile.Load(reclustered).LockedUserIds.Count == 0,
                FamilyClusterFile.Load(reclustered).LockedUserIds.Count.ToString());

            string legacyClean = Path.Combine(workDir, "legacy_clean.fcf");
            var untouched = new FamilyClusterFile { ClusteringIdentity = 70, MyExtendedFastaItems = Venom() };
            untouched.MyExtendedFastaItems.ForEach(i => i.UserID = i.USearchID);
            untouched.Save(legacyClean);
            File.WriteAllText(legacyClean, File.ReadAllText(legacyClean).Replace("\"LockedUserIds\"", "\"SomethingElse\""));
            Check("a legacy file with no manual edits arrives with no locks",
                FamilyClusterFile.Load(legacyClean).LockedUserIds.Count == 0);

            // Discriminating on purpose: a current-format file (carries the LockedUserIds field)
            // with an EMPTY lock set and divergent rows. If the inference wrongly ran here it would
            // produce locks; the assertion is that it produces none.
            string currentEmpty = Path.Combine(workDir, "current_empty.fcf");
            var cur = new FamilyClusterFile { ClusteringIdentity = 70, MyExtendedFastaItems = Venom() };
            cur.Save(currentEmpty);
            Check("a current-format file with an empty lock set gets NO inference despite divergence",
                FamilyClusterFile.Load(currentEmpty).LockedUserIds.Count == 0);
            Check("a current-format file with explicit locks keeps exactly them",
                FamilyClusterFile.Load(path).LockedUserIds.SetEquals(new[] { 2, 500 }));

            AutomaticMergeSyncsBothColumnsTests();
        }

        /// <summary>
        /// Ana's second report: after an AUTOMATIC word reclustering (or super cluster), the USearch ID
        /// and User ID columns diverged for the families the PROGRAM merged on its own. Her screenshot:
        /// WordReclustering at 0.85 merged USEARCH families 24, 75 and 114 (all serine proteases) into
        /// User ID 24, but the USearch ID column still showed 24 / 75 / 114.
        ///
        /// The invariant, which is the acceptance criterion: any change the program makes automatically
        /// must renumber BOTH columns equivalently. USearchID may differ from UserID ONLY where the user
        /// renumbered a family by hand, which is exactly a locked family. These tests drive the real
        /// reclustering path (GroupByWords + BuildStableMapping, applied the way UpdateClusterIds does)
        /// and then WordCluster.SyncUnlockedIds, which is the fix.
        /// </summary>
        private static void AutomaticMergeSyncsBothColumnsTests()
        {
            // Three serine-protease families with identical descriptions merge at 0.85; one unrelated
            // family (myoglobin) does not. On import both columns start equal, as USearchTools sets them.
            List<FastaItemExtended> SerineProteases() => new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|S24|SVSP",
                    Description = "Snake venom serine protease OS=Bothrops jararaca OX=8724",
                    USearchID = 24, UserID = 24 },
                new FastaItemExtended { SequenceIdentifier = "sp|S75|SVSP",
                    Description = "Snake venom serine protease OS=Bothrops jararaca OX=8724",
                    USearchID = 75, UserID = 75 },
                new FastaItemExtended { SequenceIdentifier = "sp|S114|SVSP",
                    Description = "Snake venom serine protease OS=Bothrops jararaca OX=8724",
                    USearchID = 114, UserID = 114 },
                new FastaItemExtended { SequenceIdentifier = "sp|M9|MYO",
                    Description = "Myoglobin OS=Physeter catodon OX=9755",
                    USearchID = 9, UserID = 9 },
            };

            // Apply the reclustering exactly as UpdateClusterIds does: key the stable mapping on the
            // curated UserID, write the new UserID, then sync the unlocked families.
            void ApplyRecluster(List<FastaItemExtended> items, double threshold, ISet<int> locks)
            {
                var clusters = new WordCluster(items, new List<string>()).GroupByWords(threshold, locks);
                var mapping = WordCluster.BuildStableMapping(clusters);
                foreach (var item in items)
                {
                    if (mapping.TryGetValue(item.UserID, out int newId)) item.UserID = newId;
                }
                WordCluster.SyncUnlockedIds(items, locks);
            }

            var ana = SerineProteases();
            ApplyRecluster(ana, 0.85, new HashSet<int>());

            var mergedMembers = ana.Where(i => i.SequenceIdentifier.StartsWith("sp|S")).ToList();
            Check("Ana's screenshot: the merged serine families read 24 in BOTH columns for every member",
                mergedMembers.All(i => i.USearchID == 24 && i.UserID == 24),
                string.Join(" | ", mergedMembers.Select(i => i.USearchID + "/" + i.UserID)));
            Check("no merged member still shows 75 or 114 in EITHER column",
                !mergedMembers.Any(i => i.USearchID == 75 || i.USearchID == 114
                                      || i.UserID == 75 || i.UserID == 114));
            Check("the unrelated family the merge never touched keeps its number in both columns",
                ana[3].USearchID == 9 && ana[3].UserID == 9,
                ana[3].USearchID + "/" + ana[3].UserID);
            Check("invariant holds after an automatic merge: USearchID == UserID for every item (nothing locked)",
                ana.All(i => i.USearchID == i.UserID));

            // Manual divergence preserved. A family renumbered by hand (USearchID 5, UserID 500) and
            // present in the lock set stays exactly 5 / 500 through a reclustering, while the unlocked
            // serine families around it sync. The lock also holds it out of the merge itself.
            var mixLocks = new HashSet<int> { 5, 500 };
            var mix = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|A|SVSP",
                    Description = "Snake venom serine protease OS=x OX=1", USearchID = 24, UserID = 24 },
                new FastaItemExtended { SequenceIdentifier = "sp|B|SVSP",
                    Description = "Snake venom serine protease OS=x OX=1", USearchID = 75, UserID = 75 },
                new FastaItemExtended { SequenceIdentifier = "sp|C|HAND",
                    Description = "Phospholipase A2 inhibitor OS=x OX=1", USearchID = 5, UserID = 500 },
            };
            ApplyRecluster(mix, 0.85, mixLocks);

            Check("a hand renumbered locked family keeps its divergent 5 / 500 through the reclustering",
                mix[2].USearchID == 5 && mix[2].UserID == 500,
                mix[2].USearchID + "/" + mix[2].UserID);
            Check("the unlocked families around it sync to 24 in both columns",
                mix[0].USearchID == 24 && mix[0].UserID == 24
                && mix[1].USearchID == 24 && mix[1].UserID == 24,
                mix[0].USearchID + "/" + mix[0].UserID + " , " + mix[1].USearchID + "/" + mix[1].UserID);
            Check("invariant holds with a lock present: columns match unless the family's UserID is locked",
                mix.All(i => i.USearchID == i.UserID || mixLocks.Contains(i.UserID)));

            // Legacy heal. An unlocked family that already carries a stale divergence (USearchID 75,
            // UserID 24) from an older build is brought back into step by SyncUnlockedIds alone, the
            // moment the file is reclustered again.
            var stale = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|Z|SVSP",
                    Description = "Serine protease", USearchID = 75, UserID = 24 },
            };
            WordCluster.SyncUnlockedIds(stale, new HashSet<int>());
            Check("a stale unlocked divergence (75 / 24) heals to 24 / 24",
                stale[0].USearchID == 24 && stale[0].UserID == 24,
                stale[0].USearchID + "/" + stale[0].UserID);
        }

        /// <summary>
        /// Undo at the real control: the button tracks the snapshot stack, a genuine User ID edit pushes a
        /// step, undo restores the pre-edit state (here the locks the edit recorded), and the stack is
        /// bounded so a long session cannot grow it without limit.
        /// </summary>
        /// <summary>
        /// The batch form of the User ID and User Annotation columns. Asked for because joining proteins
        /// or gathering a class (the cellular proteins of a venom database, to read as one "cellular
        /// proteins" family in a chart) meant one edit per row.
        /// </summary>
        private static void BatchCurationTests()
        {
            FamilyClusterFile Fixture() => new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "a1", UserID = 1, USearchID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "a2", UserID = 1, USearchID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "b1", UserID = 2, USearchID = 2 },
                    new FastaItemExtended { SequenceIdentifier = "c1", UserID = 3, USearchID = 3, UserAnnotation = "kept" },
                }
            };

            FamilyClusterFile fcf = Fixture();
            var items = fcf.MyExtendedFastaItems;
            BatchCurationResult join = BatchCuration.Apply(fcf, new[] { items[1], items[2] }, 3, "cellular proteins");
            Check("a batch moves every selected row to the typed family",
                items[1].UserID == 3 && items[2].UserID == 3 && items[0].UserID == 1 && join.Moved == 2,
                join.Moved.ToString(CultureInfo.InvariantCulture));
            Check("and annotates every selected row", items[1].UserAnnotation == "cellular proteins"
                && items[2].UserAnnotation == "cellular proteins" && join.Annotated == 2 && items[3].UserAnnotation == "kept");
            Check("it locks the joined family and the split family, not the one it emptied",
                fcf.LockedUserIds.SetEquals(new[] { 1, 3 }) && join.SourceFamilies.SequenceEqual(new[] { 1, 2 }),
                string.Join(",", fcf.LockedUserIds.OrderBy(i => i)));
            Check("USearch ID is left alone, as a single-cell edit leaves it",
                items[1].USearchID == 1 && items[2].USearchID == 2);

            BatchCurationResult again = BatchCuration.Apply(fcf, new[] { items[1], items[2] }, 3, "cellular proteins");
            Check("applying the same values again changes and locks nothing",
                again.Moved == 0 && again.Annotated == 0 && again.NewlyLocked.Count == 0);

            FamilyClusterFile onlyNote = Fixture();
            BatchCuration.Apply(onlyNote, onlyNote.MyExtendedFastaItems, null, "  venom  ");
            Check("an annotation alone regroups nothing, locks nothing, and is trimmed",
                onlyNote.MyExtendedFastaItems.Select(i => i.UserID).SequenceEqual(new[] { 1, 1, 2, 3 })
                && onlyNote.LockedUserIds.Count == 0
                && onlyNote.MyExtendedFastaItems.All(i => i.UserAnnotation == "venom"));

            FamilyClusterFile onlyId = Fixture();
            BatchCuration.Apply(onlyId, new[] { onlyId.MyExtendedFastaItems[3] }, 1, null);
            Check("a number alone leaves the annotation as it was",
                onlyId.MyExtendedFastaItems[3].UserAnnotation == "kept" && onlyId.MyExtendedFastaItems[3].UserID == 1);
            Check("a negative family number is refused",
                Throws<ArgumentOutOfRangeException>(() => BatchCuration.Apply(Fixture(), Fixture().MyExtendedFastaItems, -1, null)));

            // Undo of a batch puts the annotations of the rows it touched back, and only those: a note typed
            // by hand on another row after the snapshot must survive the undo.
            FamilyClusterFile undoable = Fixture();
            var touched = new[] { undoable.MyExtendedFastaItems[0], undoable.MyExtendedFastaItems[3] };
            GroupingSnapshot before = GroupingSnapshot.Capture(undoable, touched);
            BatchCuration.Apply(undoable, touched, 9, "cellular proteins");
            undoable.MyExtendedFastaItems[2].UserAnnotation = "typed by hand afterwards";
            before.Restore(undoable);
            Check("undoing a batch restores the numbers, the locks and the touched annotations",
                undoable.MyExtendedFastaItems.Select(i => i.UserID).SequenceEqual(new[] { 1, 1, 2, 3 })
                && undoable.LockedUserIds.Count == 0
                && undoable.MyExtendedFastaItems[0].UserAnnotation == string.Empty
                && undoable.MyExtendedFastaItems[3].UserAnnotation == "kept");
            Check("and leaves an annotation on an untouched row alone",
                undoable.MyExtendedFastaItems[2].UserAnnotation == "typed by hand afterwards");
            FamilyClusterFile plain = Fixture();
            GroupingSnapshot noNotes = GroupingSnapshot.Capture(plain);
            plain.MyExtendedFastaItems[3].UserAnnotation = "edited";
            noNotes.Restore(plain);
            Check("a snapshot taken without annotations never touches one",
                plain.MyExtendedFastaItems[3].UserAnnotation == "edited");
        }

        /// <summary>The "Apply to selected rows" button on a headless control: selection, the status line,
        /// one undo step, and the refusals.</summary>
        private static void BatchCurationUiTests(string workDir)
        {
            string fcfPath = Path.Combine(workDir, "batchui.fcf");
            new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|R1|X", Description = "60S ribosomal protein L3", USearchID = 1, UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|H1|X", Description = "Histone H4", USearchID = 2, UserID = 2 },
                    new FastaItemExtended { SequenceIdentifier = "sp|T1|X", Description = "Snake venom metalloproteinase", USearchID = 3, UserID = 3 },
                }
            }.Save(fcfPath);

            var control = new ClusterGenerationControl(fcfPath);
            var fcf = control.fcf;
            var grid = UiField<DataGrid>(control, "DataGridResultGrid")!;
            var idBox = UiField<TextBox>(control, "TextBoxBatchUserId")!;
            var noteBox = UiField<TextBox>(control, "TextBoxBatchAnnotation")!;
            var status = UiField<TextBlock>(control, "TextBlockBatchStatus")!;
            var undoButton = UiField<Button>(control, "ButtonUndo")!;
            var apply = typeof(ClusterGenerationControl).GetMethod("ButtonApplyToSelected_Click",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var undoClick = typeof(ClusterGenerationControl).GetMethod("ButtonUndo_Click",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Apply() => apply.Invoke(control, new object?[] { null, null });

            idBox.Text = "500";
            noteBox.Text = "cellular proteins";
            grid.SelectedItems.Clear();
            Apply();
            Check("with nothing selected the button changes nothing and says how to select",
                fcf.MyExtendedFastaItems.All(i => i.UserID != 500) && status.Text.Contains("no row is selected", StringComparison.Ordinal)
                && !undoButton.IsEnabled, status.Text);

            idBox.Text = "-4";
            grid.SelectedItems.Add(fcf.MyExtendedFastaItems[0]);
            Apply();
            Check("a negative number is refused in the status line", fcf.MyExtendedFastaItems[0].UserID == 1
                && status.Text.Contains("is not a family number", StringComparison.Ordinal), status.Text);

            idBox.Text = "500";
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(fcf.MyExtendedFastaItems.Single(i => i.SequenceIdentifier == "sp|R1|X"));
            grid.SelectedItems.Add(fcf.MyExtendedFastaItems.Single(i => i.SequenceIdentifier == "sp|H1|X"));
            Apply();
            var cellular = fcf.MyExtendedFastaItems.Where(i => i.UserAnnotation == "cellular proteins").ToList();
            Check("two selected rows become one labelled family in one action",
                cellular.Count == 2 && cellular.All(i => i.UserID == 500)
                && fcf.MyExtendedFastaItems.Single(i => i.SequenceIdentifier == "sp|T1|X").UserID == 3, status.Text);
            Check("the status line reports the move and the annotation, and Undo is enabled",
                status.Text.StartsWith("2 row(s) moved to family 500 (now locked); 2 row(s) annotated", StringComparison.Ordinal)
                && undoButton.IsEnabled, status.Text);

            undoClick.Invoke(control, new object?[] { undoButton, null });
            Check("one Undo puts the numbers, the locks and the annotations back",
                fcf.MyExtendedFastaItems.Select(i => i.UserID).OrderBy(i => i).SequenceEqual(new[] { 1, 2, 3 })
                && fcf.LockedUserIds.Count == 0 && fcf.MyExtendedFastaItems.All(i => i.UserAnnotation == string.Empty));
        }

        private static void UndoUiTests(string workDir)
        {
            EnsureApplication();

            string fcfPath = Path.Combine(workDir, "undoui.fcf");
            new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|A|X", Description = "Toxin",
                        USearchID = 2, UserID = 2 },
                    new FastaItemExtended { SequenceIdentifier = "sp|B|Y", Description = "Inhibitor",
                        USearchID = 2, UserID = 2 },
                }
            }.Save(fcfPath);

            var control = new ClusterGenerationControl(fcfPath);
            var fcf = control.fcf;
            var grid = UiField<DataGrid>(control, "DataGridResultGrid")!;
            var column = UiField<DataGridTextColumn>(control, "ColumnUserId")!;
            var undoButton = UiField<Button>(control, "ButtonUndo")!;
            var editHandler = typeof(ClusterGenerationControl).GetMethod("DataGridResultGrid_CellEditEnding",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var undoClick = typeof(ClusterGenerationControl).GetMethod("ButtonUndo_Click",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pushSnapshot = typeof(ClusterGenerationControl).GetMethod("PushUndoSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var stackField = typeof(ClusterGenerationControl).GetField("undoStack",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            void Commit(FastaItemExtended item, string typedText)
            {
                var row = new DataGridRow { Item = item };
                var editor = new TextBox { Text = typedText };
                var args = new DataGridCellEditEndingEventArgs(column, row, editor, DataGridEditAction.Commit);
                editHandler.Invoke(control, new object?[] { grid, args });
            }

            int StackCount() => ((System.Collections.ICollection)stackField.GetValue(control)!).Count;

            Check("Undo is disabled on a freshly loaded file", !undoButton.IsEnabled);

            // An unchanged commit records no snapshot and leaves Undo disabled.
            Commit(fcf.MyExtendedFastaItems[1], "2");
            Check("an unchanged User ID commit pushes no undo step", StackCount() == 0 && !undoButton.IsEnabled,
                StackCount().ToString());

            // A genuine renumber pushes exactly one step and enables Undo. In the harness the binding never
            // runs, so the item's UserID does not change, but the edit DID record the locks {2,500} and a
            // snapshot of the state before them.
            Commit(fcf.MyExtendedFastaItems[1], "500");
            Check("a genuine User ID edit pushes one undo step and enables the button",
                StackCount() == 1 && undoButton.IsEnabled, StackCount().ToString());
            Check("the edit recorded the two locks before the snapshot could be undone",
                fcf.LockedUserIds.SetEquals(new[] { 2, 500 }),
                string.Join(",", fcf.LockedUserIds.OrderBy(i => i)));

            // Undo restores the pre-edit state: the locks are gone again and the stack empties.
            undoClick.Invoke(control, new object?[] { undoButton, null });
            Check("undo restores the pre-edit lock set (empty) ",
                fcf.LockedUserIds.Count == 0, string.Join(",", fcf.LockedUserIds));
            Check("and the stack empties and the button disables again",
                StackCount() == 0 && !undoButton.IsEnabled, StackCount().ToString());

            // Undo on an empty stack is a no-op, not a throw.
            undoClick.Invoke(control, new object?[] { undoButton, null });
            Check("undo on an empty stack does nothing", StackCount() == 0 && !undoButton.IsEnabled);

            // The stack is bounded: pushing well past the cap keeps it at the limit, not growing forever.
            for (int i = 0; i < 60; i++) pushSnapshot.Invoke(control, null);
            Check("the undo stack is bounded to 50 steps", StackCount() == 50 && undoButton.IsEnabled,
                StackCount().ToString());
        }

        /// <summary>
        /// The lock RECORDING, at the actual UI handler, with constructed edit events - the one part
        /// of the curation fix that only agents had read and nothing had executed. Covers the
        /// spurious-lock defect the verification found (an unchanged commit must not lock), the
        /// changed commit locking both sides, and the negative number being cancelled.
        /// </summary>
        private static void CurationLockUiTests(string workDir)
        {
            EnsureApplication();

            string fcfPath = Path.Combine(workDir, "lockui.fcf");
            new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|A|X", Description = "Toxin",
                        USearchID = 2, UserID = 2 },
                    new FastaItemExtended { SequenceIdentifier = "sp|B|Y", Description = "Inhibitor",
                        USearchID = 2, UserID = 2 },
                }
            }.Save(fcfPath);

            var control = new ClusterGenerationControl(fcfPath);
            var fcf = control.fcf;
            var grid = UiField<DataGrid>(control, "DataGridResultGrid")!;
            var column = UiField<DataGridTextColumn>(control, "ColumnUserId")!;
            var lockCount = UiField<Label>(control, "LabelLockCount")!;
            var manageLocks = UiField<Button>(control, "ButtonManageLocks")!;
            var handler = typeof(ClusterGenerationControl).GetMethod("DataGridResultGrid_CellEditEnding",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            void Commit(string typedText)
            {
                var row = new DataGridRow { Item = fcf.MyExtendedFastaItems[1] };
                var editor = new TextBox { Text = typedText };
                var args = new DataGridCellEditEndingEventArgs(column, row, editor, DataGridEditAction.Commit);
                handler.Invoke(control, new object?[] { grid, args });
            }

            // The summary is the whole point of this phase: before it, a lock left no trace on screen.
            Check("with nothing locked the summary says so in words, not as a bare zero",
                (lockCount.Content as string) == "No families locked", lockCount.Content as string);
            Check("and Manage locks is disabled while there is nothing to manage", !manageLocks.IsEnabled);

            Commit("2");
            Check("committing a User ID cell unchanged locks nothing", fcf.LockedUserIds.Count == 0,
                string.Join(",", fcf.LockedUserIds));

            Commit("abc");
            Check("garbage input locks nothing either", fcf.LockedUserIds.Count == 0);

            Commit("-12");
            Check("a negative family number is cancelled and locks nothing",
                fcf.LockedUserIds.Count == 0);

            Check("and none of those three moved the summary or enabled the button",
                (lockCount.Content as string) == "No families locked" && !manageLocks.IsEnabled,
                lockCount.Content as string);

            Commit("500");
            Check("a real renumbering locks the family the rows left and the one they joined",
                fcf.LockedUserIds.SetEquals(new[] { 2, 500 }),
                string.Join(",", fcf.LockedUserIds.OrderBy(i => i)));

            // Synchronously, from the handler itself: the refresh the handler queues on the Dispatcher
            // never runs in a harness with no message loop, so a summary updated only there would look
            // right in the product and be untestable here.
            Check("the summary follows the edit that created the locks",
                (lockCount.Content as string) == "2 families locked", lockCount.Content as string);
            Check("and Manage locks becomes usable", manageLocks.IsEnabled);

            // WPF's binding converter trims whitespace, so "7 " pasted from a spreadsheet DOES
            // renumber the family. The lock parse must accept exactly what the binding accepts, or
            // the renumbering happens without the lock and the next reclustering re-merges it: the
            // original defect resurrected by a trailing space (the pre-deploy review's find).
            Commit("7 ");
            Check("a pasted number with a trailing space still locks",
                fcf.LockedUserIds.SetEquals(new[] { 2, 500, 7 }),
                string.Join(",", fcf.LockedUserIds.OrderBy(i => i)));

            Check("and the summary counts the third lock, in the singular or the plural as needed",
                (lockCount.Content as string) == "3 families locked", lockCount.Content as string);

            LockManagerTests(control, lockCount, manageLocks);
        }

        /// <summary>
        /// The management window, over the control whose locks were just made by the edit handler above.
        ///
        /// Its state at this point is the interesting one: family 2 exists and holds both proteins, while
        /// 7 and 500 do NOT exist as families, because in this harness the grid's binding - which is what
        /// actually renumbers a row - never runs. That is exactly the case the release notes could not
        /// handle: a lock that outlives its family, removable only by hand-editing the JSON.
        /// </summary>
        private static void LockManagerTests(ClusterGenerationControl control, Label lockCount, Button manageLocks)
        {
            var fcf = control.fcf;

            LockManagerWindow window;

            try { window = control.CreateLockManager(); }
            catch (Exception ex)
            {
                Check("the lock manager builds over the open file", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            Check("the manager lists exactly the locked family numbers, in a fixed order",
                window.Rows.Select(r => r.UserId).SequenceEqual(new[] { 2, 7, 500 }),
                string.Join(",", window.Rows.Select(r => r.UserId)));

            Check("a lock whose family exists carries its member count and its first member's description",
                window.Rows[0].Exists && window.Rows[0].Members == 2 && window.Rows[0].Description == "Toxin",
                window.Rows[0].Members + " / " + window.Rows[0].Description);

            Check("a lock whose family is gone is listed rather than hidden, with no members",
                !window.Rows[1].Exists && !window.Rows[2].Exists
                && window.Rows[1].Members == 0 && window.Rows[2].Members == 0);

            Check("and it says so in its own column instead of looking like an empty family",
                window.Rows[1].Presence != window.Rows[0].Presence && window.Rows[1].Presence.Length > 0,
                window.Rows[0].Presence + " vs " + window.Rows[1].Presence);

            var removeButton = UiField<Button>(window, "removeButton")!;
            Check("Remove selected locks is disabled while nothing is selected", !removeButton.IsEnabled);

            window.SelectFamilies(7, 500);
            Check("selecting rows enables it", removeButton.IsEnabled);

            // The button's own path, not a shortcut: this is the method its Click handler invokes.
            int removed = window.RemoveSelectedLocks();

            Check("removing takes exactly the selected locks out of the open file",
                removed == 2 && fcf.LockedUserIds.SetEquals(new[] { 2 }),
                removed + " -> " + string.Join(",", fcf.LockedUserIds.OrderBy(i => i)));

            Check("the manager's own list follows the removal immediately",
                window.Rows.Select(r => r.UserId).SequenceEqual(new[] { 2 }),
                string.Join(",", window.Rows.Select(r => r.UserId)));

            Check("and so does the Layer 1 summary, in the singular",
                (lockCount.Content as string) == "1 family locked", lockCount.Content as string);

            window.SelectFamilies(2);
            window.RemoveSelectedLocks();

            Check("removing the last lock empties the list, the summary and the button",
                fcf.LockedUserIds.Count == 0 && window.Rows.Count == 0
                && (lockCount.Content as string) == "No families locked" && !manageLocks.IsEnabled,
                lockCount.Content as string);

            // Handed a file directly, with no control in front of it: the three facts per row must not
            // depend on who constructed the window.
            var standalone = new LockManagerWindow(new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|P30431|VM3JA_BOTJA",
                        Description = "Snake venom metalloproteinase", USearchID = 1, UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|B1A4N8|PLIA_BOTJ",
                        Description = "Phospholipase A2 inhibitor", USearchID = 2, UserID = 9 },
                },
                LockedUserIds = new HashSet<int> { 9, 42 }
            });

            Check("a directly built manager reports the present family with its own description",
                standalone.Rows.Count == 2 && standalone.Rows[0].UserId == 9 && standalone.Rows[0].Exists
                && standalone.Rows[0].Members == 1
                && standalone.Rows[0].Description == "Phospholipase A2 inhibitor",
                string.Join(" | ", standalone.Rows.Select(r => r.UserId + ":" + r.Members + ":" + r.Presence)));

            Check("and the absent one as absent, with an empty description rather than a borrowed one",
                !standalone.Rows[1].Exists && standalone.Rows[1].UserId == 42
                && standalone.Rows[1].Description.Length == 0);

            // Nothing was saved: the file on disk is the caller's business, and a management window that
            // wrote silently would be worse than none.
            Check("removing a lock never removes a protein from the file",
                fcf.MyExtendedFastaItems.Count == 2, fcf.MyExtendedFastaItems.Count.ToString());
        }
    }
}
