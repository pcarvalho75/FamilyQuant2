using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant
{
    /// <summary>
    /// Home: the five numbered steps in Ana's order (the names table, <see cref="WorkflowNames"/>), each
    /// with its one line, its state and one button, then the open project and the recent projects. It is
    /// the default view MainWindow swaps into GridApp.
    ///
    /// It owns no engine and no store. Each step button raises one event; MainWindow subscribes and
    /// calls the EXACT SAME handler the matching menu or sidebar command calls, so Home is pure
    /// navigation and adds no second path into the program. Quantify and Results are lit only when a
    /// project is open, the same condition their sidebar entries use, so Home and the sidebar never
    /// disagree. One button is primary: Open project while none is open, then the step that comes next.
    /// This screen raises no dialog.
    /// </summary>
    public partial class HomeControl : UserControl
    {
        public HomeViewModel ViewModel { get; } = new();
        public event Action? OpenRequested;
        public event Action<string>? OpenPathRequested;
        public event Action<string>? RemoveRecentProjectRequested;
        public event Action? LastResultRequested;

        /// <summary>Step 1: build families. MainWindow wires this to MenuRunUSearch_Click.</summary>
        public event Action? BuildFamiliesRequested;

        /// <summary>Step 2: curate families. MainWindow wires this to ShowCurationOrOpen.</summary>
        public event Action? CurateRequested;

        /// <summary>Step 3: Quantify. MainWindow wires this to MenuItemComputeXic_Click.</summary>
        public event Action? ComputeXicRequested;

        /// <summary>Step 4: Results. MainWindow wires this to MenuItemAnalytics_Click.</summary>
        public event Action? AnalyticsRequested;

        /// <summary>Step 5, optional: De novo review. MainWindow wires this to MenuItemDeNovo_Click.</summary>
        public event Action? DeNovoRequested;

        private readonly (TextBlock Number, TextBlock Name, TextBlock Method, TextBlock State, Button Action)[] stepViews;

        public HomeControl()
        {
            InitializeComponent();
            stepViews = new[]
            {
                (TextBlockHomeStep1Number, TextBlockHomeStep1Name, TextBlockHomeStep1Method, TextBlockHomeStep1State, ButtonHomeBuild),
                (TextBlockHomeStep2Number, TextBlockHomeStep2Name, TextBlockHomeStep2Method, TextBlockHomeStep2State, ButtonHomeCurate),
                (TextBlockHomeStep3Number, TextBlockHomeStep3Name, TextBlockHomeStep3Method, TextBlockHomeStep3State, ButtonHomeCompute),
                (TextBlockHomeStep4Number, TextBlockHomeStep4Name, TextBlockHomeStep4Method, TextBlockHomeStep4State, ButtonHomeAnalytics),
                (TextBlockHomeStep5Number, TextBlockHomeStep5Name, TextBlockHomeStep5Method, TextBlockHomeStep5State, ButtonHomeDeNovo)
            };
            foreach (var (view, step) in stepViews.Zip(WorkflowNames.Steps))
            {
                view.Number.Text = step.Number.ToString(CultureInfo.CurrentCulture);
                view.Name.Text = step.Name;
                view.Method.Text = step.Method;
                view.Action.Content = step.Name;
            }
            TextBlockHomeStep5Optional.Text = WorkflowNames.Optional;
            ViewModel.PropertyChanged += (_, _) => ApplyViewModel();
            RecentProjectsList.ItemsSource = ViewModel.RecentProjects;

            // Opens with no project: Quantify and Results stay dark until one is open.
            SetProjectState(false, "No project open.");
        }

        /// <summary>
        /// Reflects whether a project is currently open, and names it. Quantify and Results are enabled
        /// only when one is, because neither has anything to act on otherwise, which is exactly the
        /// condition the sidebar and menu commands they call already use.
        /// </summary>
        /// <param name="projectLoaded">Whether a .fqproj or .fcf is currently open.</param>
        /// <param name="projectSummary">The line shown on the project card, e.g. the file name and whether
        /// a full XIC has been computed, or that no project is open.</param>
        public void SetProjectState(bool projectLoaded, string projectSummary)
        {
            ViewModel.SetProjectState(projectLoaded, projectSummary);
        }

        public void SetRecentProjects(System.Collections.Generic.IEnumerable<string> paths) => ViewModel.SetRecentProjects(paths);
        public void SetLastResult(string? path, bool currentHasResult) => ViewModel.SetLastResult(path, currentHasResult);

        private void ApplyViewModel()
        {
            TextBlockHomeProject.Text = ViewModel.ProjectSummary;
            foreach (var (view, step) in stepViews.Zip(ViewModel.Steps))
            {
                view.State.Text = step.State;
                view.Action.IsEnabled = step.IsEnabled;
                Lead(view.Action, step.IsNext);
            }
            Lead(ButtonHomeOpen, ViewModel.OpenProjectLeads);
            ButtonHomeLastResult.IsEnabled = ViewModel.HasLastResult;
            TextBlockLastResult.Text = ViewModel.LastResultText;
            TextBlockNoRecents.Visibility = ViewModel.HasRecents ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        /// <summary>The one primary button takes the accent style; every other button keeps the quiet default.</summary>
        private static void Lead(Button button, bool leads)
        {
            if (leads) button.SetResourceReference(StyleProperty, "PrimaryButton");
            else button.ClearValue(StyleProperty);
        }

        private void ButtonHomeOpen_Click(object sender, System.Windows.RoutedEventArgs e) => OpenRequested?.Invoke();
        private void ButtonHomeLastResult_Click(object sender, System.Windows.RoutedEventArgs e) => LastResultRequested?.Invoke();
        private void RecentProject_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button { DataContext: HomeRecentProject { Exists: true } recent }) OpenPathRequested?.Invoke(recent.Path);
        }

        private void RemoveRecentProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: HomeRecentProject recent }) return;
            e.Handled = true;
            RemoveRecentProjectRequested?.Invoke(recent.Path);
        }

        private void RecentProject_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None
                || sender is not FrameworkElement { DataContext: HomeRecentProject recent }) return;
            e.Handled = true;
            RemoveRecentProjectRequested?.Invoke(recent.Path);
        }

        private void ButtonHomeBuild_Click(object sender, System.Windows.RoutedEventArgs e)
            => BuildFamiliesRequested?.Invoke();

        private void ButtonHomeCurate_Click(object sender, System.Windows.RoutedEventArgs e)
            => CurateRequested?.Invoke();

        private void ButtonHomeCompute_Click(object sender, System.Windows.RoutedEventArgs e)
            => ComputeXicRequested?.Invoke();

        private void ButtonHomeAnalytics_Click(object sender, System.Windows.RoutedEventArgs e)
            => AnalyticsRequested?.Invoke();

        private void ButtonHomeDeNovo_Click(object sender, System.Windows.RoutedEventArgs e)
            => DeNovoRequested?.Invoke();
    }
}
