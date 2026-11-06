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
        /// The users' rule (Ana, 2026-10-25): in a family composition a peptide shared by several families
        /// is its own slice, not counted in each and not split. The engine keeps the count-in-every-family
        /// rows it always had and adds the unique parts, the shared bucket and the split reading beside
        /// them; every reading must close on the measured total.
        /// </summary>
        private static void FamilyCompositionTests()
        {
            var rows = new List<QuantRow>
            {
                Qr("identified", "r1", 10, 2, "AAAK", 100, 200),   // family 1 only
                Qr("identified", "r1", 20, 2, "CCCK", 50, 100),    // families 1 and 2
                Qr("identified", "r1", 30, 2, "DDDK", 30, 60),     // no family
                Qr("identified", "r2", 10, 2, "EEEK", 40, 80),     // family 2 only
                Qr("denovo", "r1", 40, 2, "FFFK", 10, 20),         // family 2 only, de novo
            };
            var idFam = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["AAAK"] = new[] { 1 }, ["CCCK"] = new[] { 1, 2 }, ["EEEK"] = new[] { 2 },
            };
            var dnFam = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal) { ["FFFK"] = new[] { 2 } };
            IReadOnlyList<int> none = Array.Empty<int>();
            FamilyEnvelopeQuantResult r = FamilyEnvelopeQuant.Build(rows,
                s => idFam.TryGetValue(s, out var v) ? v : none, s => dnFam.TryGetValue(s, out var v) ? v : none);

            FamilyEnvelopeQuantRow f1 = r.Families.Single(f => f.FamilyUserId == 1);
            FamilyEnvelopeQuantRow f2 = r.Families.Single(f => f.FamilyUserId == 2);
            double dn = f2.UniqueDeNovoEnvelopePrimary;
            bool Near(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(b));

            Check("count-in-every-family is unchanged: the shared peptide sits in both rows",
                Near(f1.TotalEnvelopeReferenceCalibrated, 300) && Near(f2.TotalEnvelopeReferenceCalibrated, 180 + dn),
                f1.TotalEnvelopeReferenceCalibrated + " / " + f2.TotalEnvelopeReferenceCalibrated);
            Check("the unique parts leave the shared peptide out, and it is counted ONCE in the shared bucket",
                Near(f1.UniqueTotalEnvelopeReferenceCalibrated, 200) && Near(f2.UniqueTotalEnvelopeReferenceCalibrated, 80 + dn)
                && Near(r.SharedTotalEnvelopeReferenceCalibrated, 100) && r.SharedIdentifiedPeptides == 1,
                r.SharedTotalEnvelopeReferenceCalibrated.ToString(CultureInfo.InvariantCulture));
            Check("split equally gives each family half of the shared peptide",
                Near(f1.Signal(SharedPeptidePolicy.SplitEqually), 250) && Near(f2.Signal(SharedPeptidePolicy.SplitEqually), 130 + dn));
            Check("unique parts + shared + unattributed close on the measured total",
                Near(r.Families.Sum(f => f.Signal(SharedPeptidePolicy.FamilyUnique)) + r.SharedTotalEnvelopeReferenceCalibrated
                     + r.UnattributedTotalEnvelopeReferenceCalibrated, r.MeasuredTotalEnvelopeReferenceCalibrated)
                && Near(r.UnattributedTotalEnvelopeReferenceCalibrated, 60),
                r.MeasuredTotalEnvelopeReferenceCalibrated.ToString(CultureInfo.InvariantCulture));
            Check("split families + unattributed close on the measured total too",
                Near(r.Families.Sum(f => f.Signal(SharedPeptidePolicy.SplitEqually)) + r.UnattributedTotalEnvelopeReferenceCalibrated,
                     r.MeasuredTotalEnvelopeReferenceCalibrated));
            Check("the composition shares add up to 1",
                Near(r.Families.Sum(r.CompositionShare) + r.SharedShare + r.UnattributedShare, 1));

            FamilyEnvelopeQuantResult again = FamilyEnvelopeQuant.BuildFromPeptides(FamilyEnvelopeQuant.PreparePeptides(rows),
                s => idFam.TryGetValue(s, out var v) ? v : none, s => dnFam.TryGetValue(s, out var v) ? v : none);
            Check("building from prepared peptides gives the same table as building from the rows",
                again.Families.Count == r.Families.Count
                && again.Families.Zip(r.Families, (a, b) => a.FamilyUserId == b.FamilyUserId
                    && a.UniqueTotalEnvelopeReferenceCalibrated == b.UniqueTotalEnvelopeReferenceCalibrated
                    && a.TotalEnvelopeReferenceCalibrated == b.TotalEnvelopeReferenceCalibrated).All(x => x));

            double FractionSum(string csv) => csv.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Select(ParseCsvLine).Sum(c => double.Parse(c[3], CultureInfo.InvariantCulture));
            string unique = CsvReport.BuildFamilyComposition(r, SharedPeptidePolicy.FamilyUnique,
                r.MeasuredTotalEnvelopeReferenceCalibrated, "fraction of the quantified signal");
            Check("the composition CSV has one row per family, a shared row and an unattributed row, adding up to 1",
                unique.Contains("\"shared between families\"", StringComparison.Ordinal)
                && unique.Contains("\"not attributed to any family\"", StringComparison.Ordinal)
                && Near(FractionSum(unique), 1)
                && unique.Contains("family-unique peptides, shared ones in their own row", StringComparison.Ordinal), unique);
            string split = CsvReport.BuildFamilyComposition(r, SharedPeptidePolicy.SplitEqually,
                r.MeasuredTotalEnvelopeReferenceCalibrated, "fraction of the quantified signal");
            Check("split equally has no shared row and still adds up to 1",
                !split.Contains("shared between families", StringComparison.Ordinal) && Near(FractionSum(split), 1), split);
        }

        /// <summary>
        /// Ana (2026-10-25) asked how a family's name is chosen: it was the first member's description, so
        /// a family of metalloproteinases with many names was named after whichever came first, and her
        /// "cellular proteins" label never reached the composition. Now the annotation, else the most
        /// frequent protein name; ties to the first in member order.
        /// </summary>
        private static void FamilyNamingTests()
        {
            FastaItemExtended P(string description, string annotation = "") =>
                new FastaItemExtended { SequenceIdentifier = "sp|" + description.GetHashCode() + "|X",
                    Description = description, UserAnnotation = annotation, UserID = 1 };

            var svmp = new[]
            {
                P("Zinc metalloproteinase-disintegrin-like jararhagin (Fragment) OS=Bothrops jararaca OX=8724"),
                P("Snake venom metalloproteinase (Fragment) OS=Bothrops jararaca OX=8724 GN=a PE=4 SV=1"),
                P("Snake venom metalloproteinase OS=Bothrops jararaca OX=8724 GN=b PE=4 SV=1"),
                P("snake venom metalloproteinase OS=Bothrops atrox OX=8725 GN=c PE=4 SV=1"),
                P("Zinc metalloproteinase-disintegrin-like bothropasin OS=Bothrops jararaca OX=8724"),
            };
            Check("the most common protein name wins over the first member's, ignoring OS=, case and (Fragment)",
                FamilyNaming.Name(svmp) == "Snake venom metalloproteinase OS=Bothrops jararaca OX=8724 GN=b PE=4 SV=1",
                FamilyNaming.Name(svmp));
            Check("and it is reported from a complete entry, not a fragment, when the name has one",
                !FamilyNaming.Name(svmp).Contains("(Fragment)", StringComparison.Ordinal));

            var tie = new[] { P("Beta toxin OS=x"), P("Alpha toxin OS=x"), P("Alpha toxin OS=y"), P("Beta toxin OS=y") };
            Check("a tie goes to the name met first in member order",
                FamilyNaming.Name(tie) == "Beta toxin OS=x", FamilyNaming.Name(tie));
            Check("all names different: the first member, as before",
                FamilyNaming.Name(new[] { P("Gamma OS=x"), P("Delta OS=x") }) == "Gamma OS=x");

            var labelled = new[] { P("Alpha toxin OS=x"), P("Alpha toxin OS=y", "  cellular proteins "), P("Actin OS=x") };
            Check("a User Annotation on any member names the family, trimmed",
                FamilyNaming.Name(labelled) == "cellular proteins", FamilyNaming.Name(labelled));
            var twoLabels = new[] { P("A", "PLA2"), P("B", "cellular"), P("C", "Cellular"), P("D", "") };
            Check("members with different annotations: the most frequent, case-insensitively, first spelling kept",
                FamilyNaming.Name(twoLabels) == "cellular", FamilyNaming.Name(twoLabels));

            Check("a family with no description and no annotation has an empty name",
                FamilyNaming.Name(new[] { P(""), P("   ") }) == string.Empty);
            Check("ProteinName strips OS=, a trailing (Fragments) and spaces",
                FamilyNaming.ProteinName("  Hyaluronidase (Fragments) OS=Bothrops OX=1") == "Hyaluronidase");

            var mixed = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|A|X", Description = "Alpha toxin OS=x", UserID = 1 },
                new FastaItemExtended { SequenceIdentifier = "sp|B|X", Description = "Beta toxin OS=x", UserID = 2 },
                new FastaItemExtended { SequenceIdentifier = "sp|C|X", Description = "Beta toxin OS=y", UserID = 2,
                    UserAnnotation = "PLA2 group" },
            };
            var names = FamilyNaming.Names(mixed);
            Check("Names gives one name per family",
                names.Count == 2 && names[1] == "Alpha toxin OS=x" && names[2] == "PLA2 group",
                string.Join(" | ", names.Select(kv => kv.Key + "=" + kv.Value)));

            // The de novo screen's family table names families by the same rule, over target members only.
            const string peptide = "WNDYEGHKMR";
            var db = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "Reverse_sp|D|X", Description = "Decoy",
                    Sequence = "MA" + new string(peptide.Reverse().ToArray()) + "GG", UserID = 7 },
                new FastaItemExtended { SequenceIdentifier = "sp|T1|X", Description = "Odd name OS=x",
                    Sequence = "MA" + peptide + "GG", UserID = 7 },
                new FastaItemExtended { SequenceIdentifier = "sp|T2|X", Description = "Common name OS=x",
                    Sequence = "MC" + peptide + "SS", UserID = 7 },
                new FastaItemExtended { SequenceIdentifier = "sp|T3|X", Description = "Common name OS=y",
                    Sequence = "MD" + peptide + "TT", UserID = 7 },
            };
            var options = new FamilyQuant.Homology.HomologyOptions();
            var results = FamilyQuant.Homology.HomologySearch.Search(new[] { peptide },
                FamilyQuant.Homology.HomologyDatabase.Build(db), options);
            var familyRows = FamilyQuant.Homology.HomologySearch.CountByFamily(results, db, options.DecoyLabel);
            Check("the de novo family table names the family by the most common target name",
                familyRows.Count == 1 && familyRows[0].Description == "Common name OS=x",
                familyRows.Count == 1 ? familyRows[0].Description : familyRows.Count + " rows");
        }

        /// <summary>
        /// The users asked for the full-XIC numbers in the curation table, not only in Analytics, and for
        /// the Layer 3 options to work without a .plp (2026-10-25). Real homology on exact substrings, so
        /// the column is driven by the same code path the application runs.
        /// </summary>
        private static void FullXicColumnUiTests(string workDir)
        {
            const string u1 = "WNDYEGHKMR", shared = "QPCMNVSTFR", u2 = "HYWTEDGAFK", orphan = "KMMWWPPYYR";
            string fcfPath = Path.Combine(workDir, "fullxiccolumn.fcf");
            new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|P1|A", Description = "Alpha toxin",
                        Sequence = "MA" + u1 + "GG" + shared + "TT", USearchID = 1, UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|P2|B", Description = "Beta toxin",
                        Sequence = "MC" + shared + "PP" + u2 + "SS", USearchID = 2, UserID = 2 },
                }
            }.Save(fcfPath);

            var rows = new List<QuantRow>
            {
                Qr("identified", "r1", 10, 2, u1, 100, 200),
                Qr("identified", "r1", 20, 2, shared, 50, 100),
                Qr("identified", "r1", 30, 2, u2, 40, 80),
                Qr("identified", "r1", 40, 2, orphan, 10, 20),
            };

            var control = new ClusterGenerationControl(fcfPath);
            var fcf = control.fcf;
            var layer2 = UiField<TextBlock>(control, "TextBlockLayer2State")!;
            var layer3 = UiField<TextBlock>(control, "TextBlockLayer3State")!;
            var summary = UiField<TextBlock>(control, "TextBlockFullXicSummary")!;
            var sharedBox = UiField<ComboBox>(control, "ComboBoxSharedPeptides")!;
            var basisBox = UiField<ComboBox>(control, "ComboBoxFullXicBasis")!;
            var exportButton = UiField<Button>(control, "ButtonExportComposition")!;
            FastaItemExtended p1 = fcf.MyExtendedFastaItems.Single(i => i.SequenceIdentifier == "sp|P1|A");
            FastaItemExtended p2 = fcf.MyExtendedFastaItems.Single(i => i.SequenceIdentifier == "sp|P2|B");
            void Refresh() => typeof(ClusterGenerationControl).GetMethod("UpdateGUI",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null);

            Check("without a full XIC the column is empty, the export is off, and Layer 2 is OPTIONAL, not BLOCKED",
                p1.FullXicShare == null && !exportButton.IsEnabled && layer2.Text == "OPTIONAL · no .plp loaded",
                layer2.Text);

            control.SetFullXicQuant(rows, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["r1"] = 1000 });
            Check("with a full XIC and no .plp, Layer 3 is ready and the export is on",
                control.HasFullXic && layer3.Text == "READY" && exportButton.IsEnabled, layer3.Text);
            Check("the column carries each family's unique share of the quantified signal (400 counted once)",
                p1.FullXicShare is double a && Math.Abs(a - 200.0 / 400) < 1e-9
                && p2.FullXicShare is double b && Math.Abs(b - 80.0 / 400) < 1e-9,
                p1.FullXicShare + " / " + p2.FullXicShare);
            Check("and the summary names the shared and the unattributed parts",
                summary.Text.Contains("shared between families 25", StringComparison.Ordinal)
                && summary.Text.Contains("attributed to no family 5", StringComparison.Ordinal), summary.Text);
            Check("the 'show only XIC > 0' filter keeps rows quantified only by the full XIC",
                FamilyFilter.Apply(fcf.MyExtendedFastaItems, "", true, false).Count == 2);

            sharedBox.SelectedIndex = 1;   // split equally
            Check("switching Shared peptides to split equally updates the column at once",
                p1.FullXicShare is double s1 && Math.Abs(s1 - 250.0 / 400) < 1e-9, p1.FullXicShare?.ToString());
            sharedBox.SelectedIndex = 0;

            basisBox.SelectedIndex = 1;    // the TIC
            Check("switching the basis to the TIC divides by the runs' TIC",
                p1.FullXicShare is double t1 && Math.Abs(t1 - 200.0 / 1000) < 1e-9, p1.FullXicShare?.ToString());
            basisBox.SelectedIndex = 0;

            p2.UserID = 1;                 // a regrouping: the shared peptide now reaches one family only
            Refresh();
            Check("a regrouping refreshes the column without a new homology search",
                p1.FullXicShare is double g && Math.Abs(g - 380.0 / 400) < 1e-9 && p2.FullXicShare == p1.FullXicShare,
                p1.FullXicShare?.ToString());
            p2.UserID = 2;
            Refresh();

            string? composition = control.FullXicCompositionCsv();
            Check("the composition export writes the families, the shared row and the unattributed row",
                composition != null && composition.Contains("shared between families", StringComparison.Ordinal)
                && composition.Contains("\"Alpha toxin\"", StringComparison.Ordinal), composition);

            // The annotation names its family (FamilyNaming). Typing one changes no User ID, so the
            // column's regroup gate must see annotations too, or the export kept the old name.
            p1.UserAnnotation = "cellular proteins";
            string? annotated = control.FullXicCompositionCsv();
            Check("an annotation on a member names its family in the composition export, with no regrouping",
                annotated != null && annotated.Contains("\"cellular proteins\"", StringComparison.Ordinal)
                && !annotated.Contains("\"Alpha toxin\"", StringComparison.Ordinal), annotated);
            Check("and the attribution the spectrum table reads carries the same name",
                control.CurrentFullXicAttribution()?.FamilyDescriptions.GetValueOrDefault(1) == "cellular proteins");
            p1.UserAnnotation = string.Empty;
            Check("clearing the annotation gives the family its protein name back",
                control.FullXicCompositionCsv()?.Contains("\"Alpha toxin\"", StringComparison.Ordinal) == true);

            Check("the summary names the quantity as the primary estimate, not an observed de novo envelope",
                summary.Text.Contains("mass-calibrated estimate", StringComparison.Ordinal)
                && summary.Text.Contains("add up to 100%", StringComparison.Ordinal), summary.Text);

            // ---- review fixes (Astra, 2026-10-25) ------------------------------------------------------
            // The TIC basis covers every run the computation recorded, normalized, including a run that
            // kept no row; a quantified run with no TIC switches the basis off instead of narrowing it.
            control.SetFullXicQuant(rows, new Dictionary<string, double> { ["r1.raw"] = 1000, ["r2"] = 500 });
            basisBox.SelectedIndex = 1;
            Check("the TIC basis sums every recorded run (a run with no quant rows included), names normalized",
                p1.FullXicShare is double tAll && Math.Abs(tAll - 200.0 / 1500) < 1e-9, p1.FullXicShare?.ToString());
            basisBox.SelectedIndex = 1;
            control.SetFullXicQuant(rows, new Dictionary<string, double> { ["r9"] = 1000 });
            var ticItem = UiField<ComboBoxItem>(control, "FullXicBasisTic")!;
            Check("a quantified run with no recorded TIC switches the TIC basis off and says why",
                !ticItem.IsEnabled && basisBox.SelectedIndex == 0
                && summary.Text.Contains("TIC basis is unavailable", StringComparison.Ordinal)
                && summary.Text.Contains("r1", StringComparison.Ordinal), summary.Text);

            // The spectrum table's family columns follow a regrouping when refreshed from the current attribution.
            control.SetFullXicQuant(rows, null);
            var spectrumRow = new DeNovo.SpectrumComparisonRow { QuantSide = "identified", QuantSequence = shared,
                FamilyUserIds = "stale", FamilyMappingStatus = "stale" };
            DeNovo.SpectrumComparisonTable.RefreshFamilyAssignments(new[] { spectrumRow }, control.CurrentFullXicAttribution()!);
            Check("a refreshed spectrum row lists every family its peptide reaches",
                spectrumRow.FamilyUserIds == "1;2" && spectrumRow.FamilyUserId == null
                && spectrumRow.FamilyMappingStatus.StartsWith("mapped", StringComparison.Ordinal), spectrumRow.FamilyUserIds);
            p2.UserID = 1;
            Refresh();
            DeNovo.SpectrumComparisonTable.RefreshFamilyAssignments(new[] { spectrumRow }, control.CurrentFullXicAttribution()!);
            Check("and after a merge it lists the merged family alone, as the composition does",
                spectrumRow.FamilyUserIds == "1" && spectrumRow.FamilyUserId == 1, spectrumRow.FamilyUserIds);
            p2.UserID = 2;
            Refresh();

            // The application attaches off the interface thread; a newer attach discards an older one.
            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                control.SetFullXicQuant(null, null);
                using var workerEntered = new ManualResetEventSlim();
                using var releaseWorker = new ManualResetEventSlim();
                int holdWorker = 1;
                EventHandler<FamilyQuant.Operations.OperationProgress> holdAttribution = (_, step) =>
                {
                    if (step.Stage != "attribution-cache" || step.Completed != 0
                        || Interlocked.Exchange(ref holdWorker, 0) != 1) return;
                    workerEntered.Set();
                    if (!releaseWorker.Wait(TimeSpan.FromSeconds(60)))
                        throw new TimeoutException("The attach-state fixture did not release attribution.");
                };
                bool WaitForState(Func<bool> state)
                {
                    var timeout = System.Diagnostics.Stopwatch.StartNew();
                    while (!state() && timeout.Elapsed < TimeSpan.FromSeconds(60))
                    { PumpDispatcher(); Thread.Sleep(1); }
                    return state();
                }
                Task? pending = null;
                control.Operations.ProgressReported += holdAttribution;
                try
                {
                    pending = control.SetFullXicQuantAsync(rows, null);
                    bool pendingVisible = WaitForState(() => workerEntered.IsSet && !pending.IsCompleted
                        && !control.HasFullXic && layer3.Text == "PENDING");
                    releaseWorker.Set();
                    bool readyVisible = WaitForState(() => pending.IsCompleted && control.HasFullXic && layer3.Text == "READY");
                    FinishBusyTask(pending);
                    Check("the background attach fills the column when it finishes, PENDING meanwhile",
                        pendingVisible && readyVisible
                        && p1.FullXicShare is double asyncShare && Math.Abs(asyncShare - 200.0 / 400) < 1e-9,
                        layer3.Text + " / " + p1.FullXicShare);
                }
                finally
                {
                    releaseWorker.Set();
                    control.Operations.ProgressReported -= holdAttribution;
                    if (pending != null && !pending.IsCompleted) FinishBusyTask(pending);
                }

                System.Threading.Tasks.Task superseded = control.SetFullXicQuantAsync(rows, null);
                control.SetFullXicQuant(null, null);
                WaitForState(() => superseded.IsCompleted);
                PumpDispatcher();
                Check("an attach overtaken by a newer one is discarded when it finishes",
                    superseded.IsCompleted && !control.HasFullXic && p1.FullXicShare == null);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            control.SetFullXicQuant(null, null);
            Check("detaching the quantitation empties the column again",
                !control.HasFullXic && p1.FullXicShare == null && !exportButton.IsEnabled);

            // ---- the hit cut keeps its ties for family attribution ----------------------------------------
            const string tied = "GSWHNCPEMR";
            var fourProteins = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|A|X", Sequence = "MK" + tied + "AA", UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|B|X", Sequence = "MK" + tied + "CC", UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|C|X", Sequence = "MK" + tied + "DD", UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|Z|X", Sequence = "MK" + tied + "EE", UserID = 2 },
                }
            };
            var tiedRows = new List<QuantRow> { Qr("identified", "r1", 10, 2, tied, 10, 20) };
            List<FamilyQuant.Homology.HomologyResult> plain = FamilyQuant.Homology.HomologySearch.Search(new[] { tied },
                FamilyQuant.Homology.HomologyDatabase.Build(fourProteins.MyExtendedFastaItems));
            PeptideFamilyAttribution tiedAttribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(tiedRows, fourProteins);
            IReadOnlyList<int> tiedFamilies = tiedAttribution.Resolve("identified", tied);
            Check("the plain search still keeps exactly three hits (the PepExplorer port is unchanged)",
                plain.Count == 1 && plain[0].Hits.Count == 3, plain.Count == 1 ? plain[0].Hits.Count.ToString() : "-");
            Check("family attribution keeps the tie, so four equal proteins over two families make the peptide shared",
                tiedFamilies.SequenceEqual(new[] { 1, 2 }), string.Join(",", tiedFamilies));

            // Astra round 2: a fourth protein a few points LOWER, still past the 80% gate, must count too.
            var lowerFourth = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|A|X", Sequence = "MK" + tied + "AA", UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|B|X", Sequence = "MK" + tied + "CC", UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|C|X", Sequence = "MK" + tied + "DD", UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|Z|X", Sequence = "MKA" + tied.Substring(1) + "EE", UserID = 2 },
                }
            };
            // That holds for a DE NOVO tag. An IDENTIFIED peptide keeps only the proteins containing it (the
            // best-score ties, 2026-10-28, Ana): the fourth protein is a 90% homolog, so it is not shared.
            var lowerRows = new List<QuantRow>
            {
                Qr("identified", "r1", 10, 2, tied, 10, 20),
                Qr("denovo", "r1", 11, 2, tied, 10, 20),
            };
            PeptideFamilyAttribution lowerAttribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(lowerRows, lowerFourth);
            IReadOnlyList<int> lowerFamilies = lowerAttribution.Resolve("identified", tied);
            IReadOnlyList<int> lowerDeNovoFamilies = lowerAttribution.Resolve("denovo", tied);
            Check("a fourth protein scoring lower but past the identity gate still makes a de novo tag shared",
                lowerDeNovoFamilies.SequenceEqual(new[] { 1, 2 }), string.Join(",", lowerDeNovoFamilies));
            Check("but an identified peptide reaches only the proteins containing it, so the homolog does not share it",
                lowerFamilies.SequenceEqual(new[] { 1 }), string.Join(",", lowerFamilies));
            Check("and the old every-past-gate rule is still available for identified peptides (sensitivity studies)",
                FamilyEnvelopeQuant.BuildAttributionFromCuration(lowerRows, lowerFourth, null, identifiedBestScoreOnly: false)
                    .Resolve("identified", tied).SequenceEqual(new[] { 1, 2 }));

            // ---- the stored spectrum families are only re-joined under matching provenance ---------------
            string print = FamilyEnvelopeQuant.ProteinSetFingerprint(fcf);
            p2.UserID = 7;
            Check("the protein-set fingerprint ignores family numbers (a regrouping keeps it)",
                FamilyEnvelopeQuant.ProteinSetFingerprint(fcf) == print);
            p2.UserID = 2;
            Check("and a different protein set changes it", FamilyEnvelopeQuant.ProteinSetFingerprint(fourProteins) != print);

            control.SetFullXicQuant(rows, null);
            DeNovo.SpectrumComparisonRow Stale() => new DeNovo.SpectrumComparisonRow
            {
                QuantSide = "identified", QuantSequence = shared, FamilyUserIds = "9", FamilyUserId = 9,
                FamilyAssignments = "F9", FamilyMappingStatus = "mapped to every target family meeting the configured homology rule",
            };
            PeptideFamilyAttribution At(double identity) => control.CurrentFullXicAttribution()!;

            var legacy = new List<DeNovo.SpectrumComparisonRow> { Stale() };
            string? legacyNote = FamilyEnvelopeQuant.RefreshSpectrumFamilies(legacy, null, fcf, At);
            Check("a project without mapping provenance keeps its families as computed, and says so",
                legacy[0].FamilyUserIds == "9" && legacy[0].FamilyMappingStatus.Contains("KEPT AS COMPUTED", StringComparison.Ordinal)
                && legacyNote != null, legacy[0].FamilyMappingStatus);
            FamilyEnvelopeQuant.RefreshSpectrumFamilies(legacy, null, fcf, At);
            Check("and the note is not stacked on a second save",
                legacy[0].FamilyMappingStatus.Split(new[] { "KEPT AS COMPUTED" }, StringSplitOptions.None).Length == 2);

            var other = new List<DeNovo.SpectrumComparisonRow> { Stale() };
            FamilyEnvelopeQuant.RefreshSpectrumFamilies(other, FamilyEnvelopeQuant.ProvenanceFor(fourProteins, 0.80), fcf, At);
            Check("a mapping made against another protein set is kept, not re-attributed",
                other[0].FamilyUserIds == "9" && other[0].FamilyMappingStatus.Contains("different protein database", StringComparison.Ordinal));

            var current = new List<DeNovo.SpectrumComparisonRow> { Stale() };
            string? currentNote = FamilyEnvelopeQuant.RefreshSpectrumFamilies(current, FamilyEnvelopeQuant.ProvenanceFor(fcf, 0.80), fcf, At);
            Check("with matching provenance the families are re-joined onto the current numbers",
                currentNote == null && current[0].FamilyUserIds == "1;2"
                && current[0].FamilyMappingStatus.StartsWith("mapped", StringComparison.Ordinal), current[0].FamilyUserIds);

            var failing = new List<DeNovo.SpectrumComparisonRow> { Stale() };
            string? failNote = FamilyEnvelopeQuant.RefreshSpectrumFamilies(failing, FamilyEnvelopeQuant.ProvenanceFor(fcf, 0.80), fcf,
                _ => throw new InvalidOperationException("search refused"));
            Check("a refresh that fails empties the families and marks them unavailable instead of leaving them mapped",
                failing[0].FamilyUserIds == string.Empty && failing[0].FamilyUserId == null
                && failing[0].FamilyMappingStatus.StartsWith("family mapping unavailable", StringComparison.Ordinal)
                && failNote != null && failNote.Contains("search refused", StringComparison.Ordinal), failing[0].FamilyMappingStatus);

            // ---- round 3: repeated identifiers, the compact cache, and a save overtaken by a new project ----
            var repeated = new FamilyClusterFile
            {
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended { SequenceIdentifier = "sp|P|X", Sequence = "MK" + u1, UserID = 1 },
                    new FastaItemExtended { SequenceIdentifier = "sp|P|X", Sequence = "MK" + u2, UserID = 2 },
                }
            };
            var repeatedRows = new List<DeNovo.SpectrumComparisonRow> { Stale() };
            FamilyEnvelopeQuant.RefreshSpectrumFamilies(repeatedRows,
                new FamilyMappingProvenance { Method = FamilyEnvelopeQuant.AttributionMethod,
                    ProteinSetSha256 = FamilyEnvelopeQuant.ProteinSetFingerprint(repeated), MinIdentity = 0.8 }, repeated, At);
            Check("a curation repeating a protein identifier records no provenance and its stored families are kept",
                FamilyEnvelopeQuant.ProvenanceFor(repeated, 0.8) == null && repeatedRows[0].FamilyUserIds == "9"
                && repeatedRows[0].FamilyMappingStatus.Contains("repeats a protein identifier", StringComparison.Ordinal),
                repeatedRows[0].FamilyMappingStatus);

            var allHits = new FamilyQuant.Homology.HomologyOptions { TopHitsPerPeptide = int.MaxValue };
            var bestHits = new FamilyQuant.Homology.HomologyOptions { TopHitsPerPeptide = int.MaxValue, BestScoreOnly = true };
            var targetsOnly = lowerFourth.MyExtendedFastaItems.ToList();
            List<FamilyQuant.Homology.FamilyAssignment> MapperJoin(FamilyQuant.Homology.HomologyOptions o) =>
                FamilyQuant.Homology.HomologySearch.MapToFamilies(FamilyQuant.Homology.HomologySearch.Search(new[] { tied },
                    FamilyQuant.Homology.HomologyDatabase.Build(lowerFourth.MyExtendedFastaItems), o), targetsOnly);
            List<FamilyQuant.Homology.FamilyAssignment> viaMapper = MapperJoin(bestHits);
            List<FamilyQuant.Homology.FamilyAssignment> viaMapperDeNovo = MapperJoin(allHits);
            Check("the compact hit cache joins to exactly the families the mapper's own join gives (each side's rule)",
                viaMapper.Count == 1 && viaMapper[0].FamilyIds.OrderBy(i => i).SequenceEqual(lowerFamilies)
                && viaMapperDeNovo.Count == 1 && viaMapperDeNovo[0].FamilyIds.OrderBy(i => i).SequenceEqual(lowerDeNovoFamilies),
                viaMapper.Count == 1 ? string.Join(",", viaMapper[0].FamilyIds) : "-");

            MainWindow main = new MainWindow();
            void SetField(string field, object? value) => typeof(MainWindow)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, value);
            var refreshMethod = typeof(MainWindow).GetMethod("RefreshSpectrumFamiliesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var heldRows = new List<QuantRow>(rows);
            var heldManifest = new QuantManifest
            {
                FamilyMapping = FamilyEnvelopeQuant.ProvenanceFor(fcf, 0.80),
                SpectrumComparisonRows = new List<DeNovo.SpectrumComparisonRow> { Stale() },
            };
            SetField("cgc", control);
            SetField("loadedQuant", heldRows);
            SetField("loadedManifest", heldManifest);
            var savedContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                (bool Completed, string? Note) Await(System.Threading.Tasks.Task<(bool, string?)> t)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (!t.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(60)) { PumpDispatcher(); Thread.Sleep(10); }
                    return t.Result;
                }

                System.Threading.Tasks.Task attach = control.SetFullXicQuantAsync(heldRows, null);
                var overtaken = (System.Threading.Tasks.Task<(bool, string?)>)refreshMethod.Invoke(main, null)!;
                SetField("loadedQuant", new List<QuantRow>(rows));   // the project is recomputed meanwhile
                var r1 = Await(overtaken);
                Check("a refresh overtaken by a recomputation reports it did not complete and touches nothing",
                    !r1.Completed && heldManifest.SpectrumComparisonRows[0].FamilyUserIds == "9");

                SetField("loadedQuant", heldRows);
                var straight = (System.Threading.Tasks.Task<(bool, string?)>)refreshMethod.Invoke(main, null)!;
                var r2 = Await(straight);
                Check("an undisturbed refresh waits for the attach, completes and re-joins the families",
                    r2.Completed && r2.Note == null && heldManifest.SpectrumComparisonRows[0].FamilyUserIds == "1;2",
                    heldManifest.SpectrumComparisonRows[0].FamilyUserIds);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(savedContext);
                main.Close();
            }

            control.SetFullXicQuant(null, null);
        }
    }
}
