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

        private static void UiSmokeTests(string? plpPath, string? databasePath, string workDir)
        {
            if (plpPath == null || databasePath == null || !File.Exists(plpPath) || !File.Exists(databasePath))
            {
                Console.WriteLine("  SKIP  UI smoke - pass a .plp and a database to run this section");
                skipped++;
                return;
            }

            EnsureApplication();

            string fcfPath = Path.Combine(workDir, "uismoke.fcf");
            USearchTools.GenerateFCF(new FileInfo(databasePath), 70).Save(fcfPath);

            ClusterGenerationControl control;
            try
            {
                control = new ClusterGenerationControl(fcfPath);
                Check("the cluster view builds from a real Family Cluster File", true);
            }
            catch (Exception ex)
            {
                Check("the cluster view builds from a real Family Cluster File", false,
                    ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var grid = UiField<DataGrid>(control, "DataGridResultGrid");
            var comboMode = UiField<ComboBox>(control, "ComboBoxNormalization");
            var comboShared = UiField<ComboBox>(control, "ComboBoxSharedPeptides");
            var hideContaminants = UiField<CheckBox>(control, "CheckBoxExcludeContaminants");
            var hideDecoys = UiField<CheckBox>(control, "CheckBoxExcludeReverse");
            var onlyQuantified = UiField<CheckBox>(control, "CheckBoxShowXICOnly");
            var familyCount = UiField<Label>(control, "LabelNumberOfGroups");
            var wordLabel = UiField<Label>(control, "LabelWordSimilarity");

            Check("every control the handlers reach exists",
                grid != null && comboMode != null && comboShared != null && hideContaminants != null
                && hideDecoys != null && onlyQuantified != null && familyCount != null);
            Check("the table is filled on load", grid!.ItemsSource != null);
            Check("the family counter is filled", familyCount!.Content?.ToString() != "?");
            Check("a fresh clustering reports no word reclustering", "none".Equals(wordLabel!.Content));

            // Drive the quantitation the way the Load PLP button does.
            var plp = new PatternLabProject(new FileInfo(plpPath));
            control.GetType().GetProperty("CurrentPLP", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(control, plp);
            control.fcf.SetLookupDictionary(plp);

            var profile = QuantitationProfile.Describe(plp);

            // Drive the REAL availability logic rather than hand-setting one item: this is the code the
            // Load PLP button runs, and it is what decides which of the four reporting modes the loaded
            // project can actually support.
            control.GetType().GetMethod("UpdateReportingModeAvailability", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(control, new object[] { profile });

            Check("the identified-signal mode is always available",
                ((ComboBoxItem)comboMode!.Items[0]).IsEnabled);
            Check("the TIC mode is enabled exactly when the profile says it is computable",
                ((ComboBoxItem)comboMode.Items[1]).IsEnabled == profile.CanReportTotalChromatogram);
            Check("every reporting mode carries a tooltip explaining itself",
                comboMode.Items.Cast<ComboBoxItem>().Skip(1).All(i => i.ToolTip != null));

            var recalculate = control.GetType().GetMethod("RecalculateQuantitationInternal",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var refresh = control.GetType().GetMethod("UpdateFilteredResults",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            for (int mode = 0; mode < comboMode.Items.Count; mode++)
            {
                // Skip what the UI itself greys out, instead of asserting a hard-coded mode list.
                if (!((ComboBoxItem)comboMode.Items[mode]).IsEnabled) continue;

                for (int shared = 0; shared <= 2; shared++)
                {
                    comboMode.SelectedIndex = mode;
                    comboShared!.SelectedIndex = shared;

                    try
                    {
                        recalculate.Invoke(control, null);
                        double sum = control.fcf.GroupedFastaItems.Sum(g => g.First().XIC);
                        Check($"recalculates with mode {mode} and shared-peptide policy {shared} (sum {sum:P2})", true);
                    }
                    catch (Exception ex)
                    {
                        Check($"recalculates with mode {mode} and shared-peptide policy {shared}", false,
                            (ex.InnerException ?? ex).Message);
                    }
                }
            }

            int RowCount() => VisibleRows(grid).Count;

            hideDecoys!.IsChecked = false; refresh.Invoke(control, null);
            int all = RowCount();
            hideDecoys!.IsChecked = true; refresh.Invoke(control, null);
            int withoutDecoys = RowCount();
            hideContaminants!.IsChecked = true; refresh.Invoke(control, null);
            int withoutEither = RowCount();
            onlyQuantified!.IsChecked = true; refresh.Invoke(control, null);
            int quantifiedOnly = RowCount();

            Console.WriteLine($"        rows: {all} -> {withoutDecoys} without decoys -> " +
                              $"{withoutEither} without contaminants -> {quantifiedOnly} quantified");

            Check("hiding decoys removes rows", withoutDecoys < all && withoutDecoys > 0);
            Check("hiding contaminants removes more", withoutEither < withoutDecoys && withoutEither > 0);
            Check("the quantified filter narrows further", quantifiedOnly <= withoutEither && quantifiedOnly > 0);

            hideDecoys.IsChecked = false;
            hideContaminants.IsChecked = false;
            onlyQuantified.IsChecked = false;
            refresh.Invoke(control, null);

            Check("the export writes one line per visible row plus a header",
                CsvReport.Build(VisibleRows(grid)).Split('\n').Count(l => l.Trim().Length > 0) == all + 1);

            // Editing User ID regroups the families; XIC User must follow, or an export carries the new
            // grouping beside the old numbers.
            comboMode.SelectedIndex = 0;
            comboShared!.SelectedIndex = 0;
            recalculate.Invoke(control, null);

            // The three places that have to name the denominator, checked against a real project rather
            // than a fixture: the column headings, the permanent status line, and the family counter.
            var shareHeader = UiField<DataGridTextColumn>(control, "ColumnXicUser")!.Header as string;
            var statusLine = UiField<TextBlock>(control, "TextBlockStatusLine")!.Text;

            Console.WriteLine("        status line: " + statusLine);
            Console.WriteLine("        heading: " + shareHeader);
            Console.WriteLine("        counter: " + familyCount.Content);

            string expectedMode = NormalizationMode.IdentifiedSignal.ShortName();

            Check("after recalculating, the share column names the denominator that produced it",
                (shareHeader ?? "").Contains(expectedMode), shareHeader);
            Check("and it says which grouping it was computed over",
                (shareHeader ?? "").Contains("User families"), shareHeader);
            Check("the status line names the same denominator",
                statusLine.Contains(expectedMode), statusLine);
            Check("and the shared-peptide rule that was applied",
                statusLine.Contains(SharedPeptidePolicy.FamilyUnique.ShortName()), statusLine);
            Check("the family counter reports visible and in-file together",
                (familyCount.Content?.ToString() ?? "").Contains("visible")
                && (familyCount.Content?.ToString() ?? "").Contains("in file"),
                familyCount.Content?.ToString());

            // Hiding decoys must move the visible half and leave the in-file half alone - the exact
            // confusion this counter caused.
            string counterAll = familyCount.Content?.ToString() ?? "";
            hideDecoys.IsChecked = true; refresh.Invoke(control, null);
            string counterFiltered = familyCount.Content?.ToString() ?? "";
            hideDecoys.IsChecked = false; refresh.Invoke(control, null);

            Check("hiding decoys changes the visible count but not the file count",
                counterAll != counterFiltered
                && counterAll.Split('·')[1].Trim() == counterFiltered.Split('·')[1].Trim()
                && counterAll.Split('·')[0].Trim() != counterFiltered.Split('·')[0].Trim(),
                counterAll + "  ->  " + counterFiltered);

            var items = control.fcf.MyExtendedFastaItems;
            var quantifiedFamilies = items.Where(i => i.XICUser > 0).Select(i => i.UserID).Distinct().Take(2).ToList();

            if (quantifiedFamilies.Count == 2)
            {
                var kept = items.First(i => i.UserID == quantifiedFamilies[0]);
                double before = kept.XICUser;

                foreach (var i in items.Where(i => i.UserID == quantifiedFamilies[1]).ToList())
                {
                    i.UserID = quantifiedFamilies[0];
                }

                recalculate.Invoke(control, null);

                Check("merging two quantified families through User ID updates XIC User",
                    kept.XICUser > before + 1e-12, $"{before:R} -> {kept.XICUser:R}");
                Check("the User column still adds up to 1 after the edit",
                    Math.Abs(items.GroupBy(i => i.UserID).Sum(g => g.First().XICUser) - 1.0) < 1e-9);
            }

            try { Check("the USearch panel builds", new USearchLib() != null); }
            catch (Exception ex) { Check("the USearch panel builds", false, ex.Message); }

            try { Check("the main window builds", new MainWindow() != null); }
            catch (Exception ex) { Check("the main window builds", false, ex.Message); }
        }

    }
}
