using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant
{
    public partial class USearchLib : UserControl
    {
        public FamilyClusterFile? fcf { get; private set; }
        public BuildFamiliesViewModel ViewModel { get; } = new();
        public static Func<string?>? SelectDatabasePathOverride { get; set; }
        public static Func<string, bool>? ConfirmOverwriteOverride { get; set; }
        public static Func<string, int, bool, Task<FamilyClusterFile>>? BuildOverride { get; set; }
        private bool busy => ViewModel.IsBusy;
        private readonly BuildActions actions;
        public string ShellStatusText => busy ? "Running USEARCH" : ViewModel.Status;
        public string ShellHelpText => BuildHelpText() + "\n\n" + TextBlockUSearchState.Text;
        public event EventHandler? ShellStatusChanged;
        public bool UseShellStatusBar
        {
            get => BorderUSearchStatus.Visibility == Visibility.Collapsed;
            set => BorderUSearchStatus.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
        public USearchLib()
        {
            InitializeComponent();
            actions = new BuildActions(this);
            DataContext = ViewModel;
            ViewModel.PropertyChanged += (_, _) => ApplyViewModel();
            ApplyViewModel();
        }
        private bool BeginRun(string what)
        {
            if (!ViewModel.TryBegin(what)) return false;
            Mouse.OverrideCursor = Cursors.Wait;
            TextBlockUSearchState.Text = "Working: " + what + " This runs USEARCH and cannot be interrupted; the buttons return when it finishes.";
            ShellStatusChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        private void EndRun()
        {
            ViewModel.End(fcf != null);
            Mouse.OverrideCursor = null;
            UpdateState();
        }
        private void UpdateState()
        {
            TextBlockUSearchState.Text = ViewModel.Status;
            ShellStatusChanged?.Invoke(this, EventArgs.Empty);
        }
        private bool ConfirmLargeExhaustive(string inputFile, bool exhaustive) => actions.ConfirmLargeExhaustive(inputFile, exhaustive);
        private void ButtonInput_Click(object sender, RoutedEventArgs e) => actions.ButtonInput_Click(sender, e);
        private void ButtonBuildFamilies_Click(object sender, RoutedEventArgs e) => actions.ButtonBuildFamilies_Click(sender, e);
        private void ButtonCalculateFDR_Click(object sender, RoutedEventArgs e) => actions.ButtonCalculateFDR_Click(sender, e);
        private void ButtonDivideAndConquer_Click(object sender, RoutedEventArgs e) => actions.ButtonDivideAndConquer_Click(sender, e);
        private void ButtonScanIdentities_Click(object sender, RoutedEventArgs e) => actions.ButtonScanIdentities_Click(sender, e);
        private void ButtonRemoveRedundancy_Click(object sender, RoutedEventArgs e) => actions.ButtonRemoveRedundancy_Click(sender, e);
    }
}
