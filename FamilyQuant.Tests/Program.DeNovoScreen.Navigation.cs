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
        /// The menu entry that reaches the screen, and the fields that say what is on display. Swapping
        /// views left the previous control's field pointing at it once before, and File · Save then
        /// wrote the wrong file; this entry point must not reintroduce that.
        /// </summary>
        private static void DeNovoScreenNavigationTests()
        {
            MainWindow main;

            try { main = new MainWindow(); PumpDispatcher(); }
            catch (Exception ex)
            {
                Check("the main window builds for the de novo navigation check", false,
                    ex.ToString());
                return;
            }

            var guide = UiField<TextBlock>(main, "TextBlockGuide")!;
            var app = UiField<Grid>(main, "GridApp")!;
            var save = UiField<MenuItem>(main, "MenuItemSaveFCF")!;
            var reanotate = UiField<MenuItem>(main, "MenuItemReanotateSEPro")!;
            var deNovoItem = UiField<MenuItem>(main, "MenuItemDeNovo")!;

            Check("the menu offers the de novo screen, with a tooltip like every other command",
                deNovoItem.ToolTip is string tip && tip.Length > 100, deNovoItem.ToolTip as string);

            var topLevel = WalkTree(main).OfType<Menu>().First().Items.Cast<MenuItem>()
                .Select(m => m.Header as string).ToList();
            Check("File leads the standard menu order and View holds De novo review with the other steps",
                topLevel.SequenceEqual(WorkflowNames.MenuOrder)
                && deNovoItem.Parent is MenuItem viewMenu && viewMenu.Header as string == "View" && deNovoItem.Header as string == WorkflowNames.DeNovoReview,
                string.Join(" | ", topLevel));

            typeof(MainWindow).GetMethod("MenuItemDeNovo_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(main, new object?[] { null, null });

            Check("choosing it puts the de novo screen on display",
                app.Children.Count == 1 && app.Children[0] is DeNovoControl);
            Check("the status names de novo while Help keeps its purpose and return path",
                guide.Text.StartsWith(WorkflowNames.DeNovoReview + " |", StringComparison.Ordinal) && main.StatusModel.ContextHelp.Contains("View > Families")
                && main.StatusModel.ContextHelp.Contains("File > Open")
                && main.StatusModel.ContextHelp.Contains("separate workflow", StringComparison.OrdinalIgnoreCase),
                guide.Text);
            Check("and the two commands that write a Family Cluster File are disabled, "
                + "since there is no longer one on screen",
                !save.IsEnabled && !reanotate.IsEnabled);

            // Two workflows, two help pages. While this screen is on display the Help menu must reach
            // the de novo page: the family page's text is about numbers this screen does not show.
            var screen = (DeNovoControl)app.Children[0];
            var screenStatus = UiField<TextBlock>(screen, "TextBlockDeNovoStatus")!;

            typeof(MainWindow).GetMethod("MenuItemHowToRead_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(main, new object?[] { null, null });

            Check("Help · How to read these numbers reaches the de novo page while that screen is open",
                screenStatus.Text.Contains("help window") && screenStatus.Text.Contains("not on a display"),
                screenStatus.Text);

            // The order of work has to mention the screen, or it is discoverable only by trying the menu.
            string workflow = (string)typeof(MainWindow)
                .GetMethod("WorkflowText", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(main, null)!;

            Check("the order of work carries the de novo flow as an OPTIONAL block, not as a fifth step",
                workflow.Contains("OPTIONAL · 5 De novo review")
                && !workflow.Contains("STEP 5"), workflow.Length.ToString());
            Check("and that block says it is a separate workflow that changes no family",
                workflow.Contains("A separate workflow, not a step of the four above")
                && workflow.Contains("changes no family"));
            Check("and lists both core steps plus current quantitation and attribution paths",
                new[] { "     1. ", "     2. ", "Quantify", "Results", "attribution remains available" }
                    .All(workflow.Contains));
        }

    }
}
