using System;
using System.Windows.Controls;

namespace FamilyQuant
{
    /// <summary>
    /// The task-oriented start screen, and the fifth view MainWindow swaps into GridApp. It is the default:
    /// when none of the cluster, USearch, de novo or analytics views is on screen, this one is.
    ///
    /// It owns no engine and no store. Each card is a Button whose Click raises one event; MainWindow
    /// subscribes and calls the EXACT SAME menu handler the corresponding File or top-level command calls,
    /// so the home is pure navigation and adds no second path into the program. Steps 3 and 4 are lit only
    /// when a project is open, which is the same condition the matching menu commands use, so the home and
    /// the menu never disagree. This screen raises no dialog of its own.
    /// </summary>
    public partial class HomeControl : UserControl
    {
        public HomeViewModel ViewModel { get; } = new();
        public event Action? OpenRequested;
        public event Action<string>? OpenPathRequested;
        public event Action? LastResultRequested;
        /// <summary>Step 1: build families. MainWindow wires this to MenuRunUSearch_Click.</summary>
        public event Action? BuildFamiliesRequested;

        /// <summary>Step 2: curate families. MainWindow wires this to MenuItemCluster_Click.</summary>
        public event Action? CurateRequested;

        /// <summary>Step 3: compute the full XIC. MainWindow wires this to MenuItemComputeXic_Click.</summary>
        public event Action? ComputeXicRequested;

        /// <summary>Step 4: analytics and report. MainWindow wires this to MenuItemAnalytics_Click.</summary>
        public event Action? AnalyticsRequested;

        /// <summary>The optional side-branch. MainWindow wires this to MenuItemDeNovo_Click.</summary>
        public event Action? DeNovoRequested;

        public HomeControl()
        {
            InitializeComponent();
            ViewModel.PropertyChanged += (_, _) => ApplyViewModel();
            RecentProjectsList.ItemsSource = ViewModel.RecentProjects;

            // Opens with no project: steps 3 and 4 are dark until one is loaded in step 2.
            SetProjectState(false, "No project open.");
        }

        /// <summary>
        /// Reflects whether a project is currently open, and names it. Steps 3 and 4 (Compute and
        /// Analytics) are enabled only when one is, because neither has anything to act on otherwise, which
        /// is exactly the condition the File-menu commands they call already use.
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
            TextBlockProjectReason.Text = ViewModel.ProjectReason;
            ButtonHomeCompute.IsEnabled = ViewModel.HasProject;
            ButtonHomeAnalytics.IsEnabled = ViewModel.HasProject;
            ButtonHomeLastResult.IsEnabled = ViewModel.HasLastResult;
            TextBlockLastResult.Text = ViewModel.LastResultText;
            TextBlockNoRecents.Visibility = ViewModel.HasRecents ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private void ButtonHomeOpen_Click(object sender, System.Windows.RoutedEventArgs e) => OpenRequested?.Invoke();
        private void ButtonHomeLastResult_Click(object sender, System.Windows.RoutedEventArgs e) => LastResultRequested?.Invoke();
        private void RecentProject_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button { DataContext: HomeRecentProject { Exists: true } recent }) OpenPathRequested?.Invoke(recent.Path);
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
