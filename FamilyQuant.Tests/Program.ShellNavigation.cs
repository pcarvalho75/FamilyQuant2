using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ShellNavigationTests(string workDir)
    {
        var model = new ShellViewModel();
        int requested = 0;
        ShellDestination last = ShellDestination.Start;
        model.NavigationRequested += route => { requested++; last = route; };
        Check("navigation refuses Quantify and Results without a project", !model.RequestNavigate(ShellDestination.Quantify)
            && !model.RequestNavigate(ShellDestination.Results) && requested == 0);
        Check("disabled rail entries explain the missing project", model.Items.Where(i => !i.IsEnabled)
            .All(i => i.StateText == "Open a project first"));
        Check("Families and De novo remain available without a project", model.CanNavigate(ShellDestination.Families)
            && model.CanNavigate(ShellDestination.DeNovo));
        model.UpdateProjectState(true, false);
        model.RequestNavigate(ShellDestination.Results);
        model.SetCurrent(ShellDestination.Results);
        Check("navigation emits the requested route once and selects only the displayed route", requested == 1
            && last == ShellDestination.Results && model.Items.Count(i => i.IsSelected) == 1);
        model.UpdateProjectState(true, true);
        Check("navigation blocks route changes during a guarded operation", !model.RequestNavigate(ShellDestination.Start)
            && model.Items.All(i => !i.IsEnabled && i.StateText == "Operation in progress"));
        model.UpdateProjectState(true, false);
        Check("navigation restores readiness after an operation", model.Items.All(i => i.IsEnabled));
        Check("navigation refuses undefined destinations", !model.RequestNavigate((ShellDestination)999));

        string path = Path.Combine(workDir, "rail.fcf");
        new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new FastaItemExtended { SequenceIdentifier = "rail_fixture", Description = "Navigation fixture", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 }
        } }.Save(path);
        var originalOpen = MainWindow.SelectOpenPathOverride;
        var originalCompute = MainWindow.ComputeDialogOverride;
        string? originalSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "rail-preferences.json");
        MainWindow.SelectOpenPathOverride = () => path;
        int computeDialogs = 0;
        MainWindow.ComputeDialogOverride = _ => { computeDialogs++; return false; };
        var main = new MainWindow();
        try
        {
            void Route(ShellDestination route) => MainWindow.NavigateShellCommand.Execute(route, main);
            var host = (Grid)main.FindName("GridApp");
            Check("rail lists the five numbered steps in Home's order, Build families first", main.NavigationModel.Items.Select(i => i.Destination).SequenceEqual(new[]
                { ShellDestination.Build, ShellDestination.Families, ShellDestination.Quantify, ShellDestination.Results, ShellDestination.DeNovo })
                && main.NavigationModel.Items.Select(i => i.Number).SequenceEqual(new[] { 1, 2, 3, 4, 5 })
                && main.NavigationModel.Items.Select(i => i.Label).SequenceEqual(WorkflowNames.Steps.Select(step => step.Name)));
            var routeKeys = Enumerable.Range(0, 6).Select(index => main.InputBindings.OfType<KeyBinding>()
                .SingleOrDefault(binding => binding.Key == Key.D0 + index && binding.Modifiers == ModifierKeys.Alt
                    && binding.Command == MainWindow.NavigateShellCommand)?.CommandParameter).ToArray();
            Check("Alt+0 is Home and Alt+1 to Alt+5 are steps 1 to 5", routeKeys.SequenceEqual(new object?[] { ShellDestination.Start,
                ShellDestination.Build, ShellDestination.Families, ShellDestination.Quantify, ShellDestination.Results, ShellDestination.DeNovo })
                && main.NavigationModel.Items.All(i => i.Shortcut == "Alt+" + i.Number), string.Join(", ", routeKeys));
            var railText = WalkTree((ShellNavigationRail)main.FindName("NavigationRail")).OfType<Button>().Select(button => button.Content as string)
                .Where(text => text != null).ToArray();
            Check("the sidebar has no separate Build button beside step 1", railText.SequenceEqual(new[] { WorkflowNames.Home }), string.Join(", ", railText));
            Check("keyboard Quantify is disabled before a project is open", !MainWindow.NavigateShellCommand.CanExecute(ShellDestination.Quantify, main));
            Route(ShellDestination.Build);
            Check("sidebar step 1 opens the Build families screen and marks itself current", host.Children.OfType<USearchLib>().Any()
                && main.NavigationModel.Current == ShellDestination.Build && main.NavigationModel.Items[0].IsSelected
                && main.NavigationModel.Items[0].StateText == "Current" && main.StatusModel.Text.StartsWith(WorkflowNames.BuildFamilies + " |", StringComparison.Ordinal),
                main.StatusModel.Text);
            int asked = 0;
            MainWindow.CurateWithoutProjectChoiceOverride = () => { asked++; return CurateWithoutProjectChoice.Cancel; };
            Route(ShellDestination.Start);
            Route(ShellDestination.Families);
            Check("with no project, Curate families asks first and Cancel keeps the current screen", asked == 1
                && host.Children.OfType<HomeControl>().Any() && !main.NavigationModel.HasProject);
            MainWindow.CurateWithoutProjectChoiceOverride = () => { asked++; return CurateWithoutProjectChoice.BuildFamilies; };
            Route(ShellDestination.Families);
            Check("with no project, Curate families offers Build families", asked == 2 && host.Children.OfType<USearchLib>().Any()
                && main.NavigationModel.Current == ShellDestination.Build);
            MainWindow.CurateWithoutProjectChoiceOverride = () => { asked++; return CurateWithoutProjectChoice.OpenProject; };
            Route(ShellDestination.Families);
            FinishBusyOperation(main.Operations);
            var project = host.Children.OfType<ClusterGenerationControl>().Single();
            Check("with no project, Curate families opens a project through the existing Open flow", asked == 3);
            MainWindow.CurateWithoutProjectChoiceOverride = () => { asked++; return CurateWithoutProjectChoice.Cancel; };
            Check("rail Families uses the existing Open flow", main.NavigationModel.Current == ShellDestination.Families
                && main.NavigationModel.HasProject);
            Check("shell model exposes the same selected document and file", ReferenceEquals(main.NavigationModel.Document, project.fcf)
                && main.NavigationModel.ProjectPath == Path.GetFullPath(path));
            Check("Curate families names the screen, its sidebar step, View and the status bar",
                ((TextBlock)project.FindName("TextBlockCurateTitle")).Text == WorkflowNames.CurateFamilies
                && main.NavigationModel.Items[1].Label == WorkflowNames.CurateFamilies
                && ((MenuItem)main.FindName("MenuItemFamilies")).Header as string == WorkflowNames.CurateFamilies
                && main.StatusModel.Text.StartsWith(WorkflowNames.CurateFamilies + " |", StringComparison.Ordinal), main.StatusModel.Text);
            static IEnumerable<FrameworkElement> InOrder(DependencyObject node)
            {
                if (node is FrameworkElement element) yield return element;
                foreach (object child in LogicalTreeHelper.GetChildren(node))
                    if (child is DependencyObject next) foreach (var inner in InOrder(next)) yield return inner;
            }
            string[] parts = { "TextBlockPartGroupByEvidence", "ButtonFetchDomains", "ButtonEvidenceGrouping", "ButtonFetchFamilyWorld",
                "ButtonComputeFullXic", "TextBlockPartGroupByWords", "ButtonSuperCluster", "ButtonWordCluster" };
            var order = InOrder(project).Select(element => element.Name).Where(parts.Contains).ToArray();
            Check("part a and part b are labelled and the guided step order is unchanged",
                ((TextBlock)project.FindName("TextBlockPartGroupByEvidence")).Text == "a " + WorkflowNames.GroupByEvidence
                && ((TextBlock)project.FindName("TextBlockPartGroupByWords")).Text == "b " + WorkflowNames.GroupByWords
                && order.SequenceEqual(parts) && ((Button)project.FindName("ButtonComputeFullXic")).Content as string == WorkflowNames.Quantify,
                string.Join(", ", order));
            Route(ShellDestination.Start);
            Route(ShellDestination.Results);
            PumpDispatcher();
            Check("rail Results reaches Results under its one name and preserves the open project", host.Children.OfType<AnalyticsControl>().Any()
                && main.NavigationModel.Current == ShellDestination.Results && main.StatusModel.Text.StartsWith(WorkflowNames.Results + " |", StringComparison.Ordinal)
                && ((MenuItem)main.FindName("MenuItemAnalytics")).Header as string == WorkflowNames.Results, main.StatusModel.Text);
            Route(ShellDestination.DeNovo);
            Check("rail De novo review reaches its existing screen under its one name", host.Children.OfType<DeNovoControl>().Any()
                && main.NavigationModel.Current == ShellDestination.DeNovo && main.StatusModel.Text.StartsWith(WorkflowNames.DeNovoReview + " |", StringComparison.Ordinal)
                && ((MenuItem)main.FindName("MenuItemDeNovo")).Header as string == WorkflowNames.DeNovoReview, main.StatusModel.Text);
            Route(ShellDestination.Quantify);
            Check("rail Quantify reaches the existing dialog without starting a computation", computeDialogs == 1
                && host.Children.OfType<DeNovoControl>().Any());
            Route(ShellDestination.Families);
            Check("returning by rail restores the same curation instance", host.Children.Count == 1 && ReferenceEquals(project, host.Children[0]));
            Route(ShellDestination.Start);
            Check("Home remains reachable by the keyboard route", host.Children.OfType<HomeControl>().Any()
                && main.NavigationModel.Current == ShellDestination.Start);
            var hosted = host.Children.OfType<HomeControl>().Single();
            Check("hosted Home shows the five numbered steps beside the sidebar", new[] { "ButtonHomeBuild", "ButtonHomeCurate",
                "ButtonHomeCompute", "ButtonHomeAnalytics", "ButtonHomeDeNovo" }.Select(name => (Button)hosted.FindName(name))
                .Select(button => button.Visibility == Visibility.Visible ? button.Content as string : null).SequenceEqual(WorkflowNames.Steps.Select(step => step.Name))
                && ((FrameworkElement)hosted.FindName("BorderHomeSteps")).Visibility == Visibility.Visible);
            var rail = (ShellNavigationRail)main.FindName("NavigationRail");
            var homeButton = WalkTree(rail).OfType<Button>().First(button => Equals(button.CommandParameter, ShellDestination.Start));
            var homeMenu = (MenuItem)main.FindName("MenuItemHome");
            Check("Home replaces Start on the sidebar, in View and on the status bar", homeButton.Content as string == WorkflowNames.Home
                && homeMenu.Header as string == WorkflowNames.Home && main.StatusModel.Text.StartsWith(WorkflowNames.Home + " |", StringComparison.Ordinal),
                homeButton.Content + "; " + homeMenu.Header + "; " + main.StatusModel.Text);
        }
        finally
        {
            main.Close();
            MainWindow.CurateWithoutProjectChoiceOverride = null;
            MainWindow.SelectOpenPathOverride = originalOpen;
            MainWindow.ComputeDialogOverride = originalCompute;
            MainWindow.ShellSettingsPathOverride = originalSettings;
        }
    }

    private static void ShellProjectOperationTests(string workDir)
    {
        var curation = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new FastaItemExtended { SequenceIdentifier = "same", Sequence = "PEPTIDEK", UserID = 7, USearchID = 1 },
            new FastaItemExtended { SequenceIdentifier = "same", Sequence = "PEPTIDEK", UserID = 9, USearchID = 2 },
            new FastaItemExtended { SequenceIdentifier = "other", Sequence = "PEPTIDEK", UserID = 3, USearchID = 3 }
        } };
        var plan = ShellProjectOperations.PlanReannotation(curation, new[] { "same", "other" });
        Check("headless reannotation preserves first occurrence and curated family IDs", plan.Missing.Count == 0
            && plan.Families["same"] == 7 && plan.Families["other"] == 3);
        var refused = ShellProjectOperations.PlanReannotation(curation, new[] { "absent", "absent", "Absent" });
        Check("headless reannotation preserves ordinal matching and distinct missing order",
            refused.Missing.SequenceEqual(new[] { "absent", "Absent" }) && curation.MyExtendedFastaItems[0].UserID == 7);
        var manifest = ShellProjectOperations.CreateManifest(AppVersionInfo.Current());
        Check("headless manifest defaults use the existing production constants", manifest.IsotopeCount == FamilyQuant.DeNovo.DeNovoXicExtractor.DefaultEnvelopeIsotopeCount
            && manifest.MinPointsForQuant == FamilyQuant.DeNovo.DeNovoXicExtractor.DefaultMinPointsForQuant
            && manifest.Ppm == FamilyQuant.DeNovo.DeNovoXicExtractor.ProductionPpm);
        string path = Path.Combine(workDir, "operations.fcf");
        curation.Save(path);
        var loaded = ShellProjectOperations.Read(path);
        Check("headless shell load keeps legacy curation and no invented quantitation", loaded.Rows.Count == 0 && loaded.Manifest == null
            && loaded.Curation.MyExtendedFastaItems.Select(i => i.UserID).SequenceEqual(new[] { 7, 9, 3 }));
    }
}
