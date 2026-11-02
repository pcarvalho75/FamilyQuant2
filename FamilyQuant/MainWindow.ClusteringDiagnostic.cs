using FamilyQuant.Operations;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

// Shell-owned request/result contracts. These deliberately do not guess the native writer's return type.
public sealed record ClusteringDiagnosticRequest(string DocumentPath, string OutputFolder, int IdentityPercent,
    bool SmallExhaustive = false, string? MatchingFasta = null);
public sealed record ClusteringDiagnosticShellResult(string Text, string OutputFolder);

public partial class MainWindow
{
    public Func<ClusteringDiagnosticRequest, OperationContext, Task<ClusteringDiagnosticShellResult>>? ClusteringDiagnosticRunnerOverride { get; set; }
    public Func<int?>? ClusteringDiagnosticIdentityChoiceOverride { get; set; }
    public Action<string, string>? ClusteringDiagnosticReportPresenterOverride { get; set; }
    public Action<string>? ClusteringDiagnosticFolderOpenerOverride { get; set; }

    private bool DiagnosticBusy => Operations.IsBusy || saveInProgress || analyticsInProgress || spectrumViewInProgress || IsQuantifyBusy;
    private bool CanRunClusteringDiagnostic => cgc != null && !DocumentState.IsModified && !DiagnosticBusy
        && SavedDiagnosticPath() != null;

    private string? SavedDiagnosticPath()
    {
        if (string.IsNullOrWhiteSpace(lastFcfPath) || !File.Exists(lastFcfPath)) return null;
        string extension = Path.GetExtension(lastFcfPath);
        return extension.Equals(".fcf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fqproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(lastFcfPath) : null;
    }

    private async void MenuItemClusteringDiagnostic_Click(object sender, RoutedEventArgs e) => await RunClusteringDiagnosticAsync();
    private void MenuAnalysis_SubmenuOpened(object sender, RoutedEventArgs e) => RefreshShellMenuState();

    public Task<bool> RunClusteringDiagnosticAsync(string? outputParent = null) =>
        AppOperationHost.OnUi(this, () => RunClusteringDiagnosticCoreAsync(outputParent));

    private async Task<bool> RunClusteringDiagnosticCoreAsync(string? outputParent)
    {
        bool Refuse(string reason)
        {
            ShellMessages.Report(this, reason, "Clustering diagnostic", image: MessageBoxImage.Information, showDialog: false);
            return false;
        }
        if (DiagnosticBusy) return Refuse("Wait for the current operation to finish before running a clustering diagnostic.");
        if (cgc == null) return Refuse("Open a saved .fcf or .fqproj before running a clustering diagnostic.");
        string? path = SavedDiagnosticPath();
        if (path == null) return Refuse("The saved .fcf or .fqproj is unavailable. Save the project, then run the diagnostic again.");
        var control = cgc;
        var document = control.fcf;
        if (DocumentState.CheckForChanges(document))
            return Refuse("Save your changes before running this diagnostic. It reads the saved .fcf or .fqproj.");
        string fingerprint = ShellDocumentState.Fingerprint(document);
        bool SameDocument() => ReferenceEquals(cgc, control) && ReferenceEquals(cgc?.fcf, document)
            && string.Equals(lastFcfPath, path, StringComparison.OrdinalIgnoreCase) && !DocumentState.IsModified
            && ShellDocumentState.Fingerprint(document) == fingerprint;

        string? preparedOutput = null;
        using var operation = Operations.Begin("Clustering diagnostic");
        try
        {
            operation.Context.Report("diagnostic-input", "Reading the saved clustering metadata.");
            // The writer and metadata reader may open this path for reading. The shell never grants
            // a write/delete share while a diagnostic owns the saved input.
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int savedIdentity = await Task.Run(() => ShellProjectOperations.Read(path, operation.Context).Curation.ClusteringIdentity);
            operation.Context.ThrowIfCancellationRequested();
            if (!SameDocument()) return Refuse("The open project changed. Save it and run the diagnostic again. No report was opened.");
            int? identity = savedIdentity is >= 1 and <= 100 ? savedIdentity : ChooseDiagnosticIdentity();
            operation.Context.ThrowIfCancellationRequested();
            if (identity == null) return Refuse("Diagnostic cancelled: no identity threshold was chosen. The saved input was kept.");
            if (identity is < 1 or > 100) return Refuse("Choose a whole-number identity threshold from 1 to 100. No diagnostic was run.");
            if (outputParent == null)
            {
                var dialog = new OpenFolderDialog { Title = "Choose a parent folder for the clustering diagnostic" };
                if (!ShellDialogs.ShowFolder(this, "clustering.diagnostic", dialog))
                    return Refuse("Diagnostic cancelled: no output folder was chosen. The saved input was kept.");
                outputParent = dialog.FolderName;
            }
            operation.Context.ThrowIfCancellationRequested();
            string parent = Path.GetFullPath(outputParent);
            if (!Directory.Exists(parent)) return Refuse("Choose an existing output parent folder. No diagnostic was run.");
            RememberFolder(parent);
            string output = Path.Combine(parent, "Clustering-diagnostic-" + Guid.NewGuid().ToString("N"));
            preparedOutput = output;
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("The diagnostic output folder must be fresh.");
            if (!SameDocument()) return Refuse("The open project changed. Save it and run the diagnostic again. No diagnostic was run.");
            operation.Context.Report("diagnostic-report", "Preparing the clustering diagnostic report.");
            var request = new ClusteringDiagnosticRequest(path, output, identity.Value);
            var result = await (ClusteringDiagnosticRunnerOverride ?? WriteClusteringDiagnosticReportAsync)(request, operation.Context);
            // A completed native Commit owns publication. A cancellation during its publish callback
            // must not relabel a successfully written report as cancelled.
            operation.Context.ThrowIfCancellationRequested();
            if (!SameDocument()) return Refuse("The open project changed while the diagnostic ran. No report or folder was opened.");
            if (result == null || string.IsNullOrWhiteSpace(result.Text)
                || !string.Equals(Path.GetFullPath(result.OutputFolder), output, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(output))
                throw new InvalidDataException("The diagnostic did not return report text in its requested output folder.");
            operation.Dispose();
            string message = "Clustering diagnostic report written to\n" + output + "\nThe saved input and open project were kept.";
            try
            {
                if (ClusteringDiagnosticReportPresenterOverride != null)
                    ClusteringDiagnosticReportPresenterOverride("Clustering diagnostic", result.Text);
                else if (ShellDialogs.IsDisplayed(this)) TextReportWindow.Show("Clustering diagnostic", result.Text, this, wrap: true);
                if (ClusteringDiagnosticFolderOpenerOverride != null) ClusteringDiagnosticFolderOpenerOverride(output);
                else if (ShellDialogs.IsDisplayed(this)) Process.Start(new ProcessStartInfo(output) { UseShellExecute = true });
            }
            catch (Exception ex) { message += "\nThe report was written, but a report or folder window could not be opened: " + ex.Message; }
            ShellMessages.Report(this, message, "Clustering diagnostic", showDialog: false);
            return true;
        }
        catch (OperationCanceledException) when (operation.Context.IsCancellationRequested && !operation.Context.HasCommitted)
        {
            return Refuse("Cancelled. The saved input and open project were kept. No report or folder was opened.");
        }
        catch (Exception ex)
        {
            string message = operation.Context.HasCommitted
                ? "The diagnostic output was written, but its report could not be opened.\n" + preparedOutput + "\n" + ex.Message
                : "The clustering diagnostic did not finish. No report or folder was opened.\n" + ex.Message;
            ShellMessages.Report(this, message,
                "Clustering diagnostic", image: MessageBoxImage.Error, showDialog: false);
            return false;
        }
    }

    private static Task<ClusteringDiagnosticShellResult> WriteClusteringDiagnosticReportAsync(
        ClusteringDiagnosticRequest request, OperationContext operation)
    {
        // BINDING REQUIRED: adapt the actual return type from Diagnostics.ClusteringDiagnostics.WriteReport
        // on an owned Task.Run, passing request.DocumentPath, request.OutputFolder, request.IdentityPercent,
        // smallExhaustive: false, matchingFasta: null, operation: operation. Return its report text and folder.
        // Do not use reflection or invent the native result shape while the core lane supplies its contract.
        return Task.FromException<ClusteringDiagnosticShellResult>(new NotSupportedException("Clustering diagnostics are not available in this build."));
    }

    private int? ChooseDiagnosticIdentity()
    {
        if (ClusteringDiagnosticIdentityChoiceOverride != null) return ClusteringDiagnosticIdentityChoiceOverride();
        if (!ShellDialogs.IsDisplayed(this)) return null;
        int? selected = null;
        var input = new TextBox { Width = 100, HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Enter a whole-number identity percentage from 1 to 100." };
        var run = new Button { Content = "Run diagnostic", IsDefault = true, IsEnabled = false };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(run); buttons.Children.Add(cancel);
        var layout = new StackPanel();
        layout.Children.Add(SecondaryWindowView.Text("This saved file does not record a valid clustering identity. Choose the threshold to examine. This choice does not regroup or save the project."));
        layout.Children.Add(new Label { Content = "_Identity threshold (%)", Target = input });
        layout.Children.Add(input); layout.Children.Add(buttons);
        var dialog = new Window { Title = "Choose diagnostic threshold", Owner = this, Width = 460, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = SecondaryWindowView.Frame(layout), ShowInTaskbar = false };
        SecondaryWindowView.Theme(dialog);
        bool Valid(out int value) => int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value is >= 1 and <= 100;
        input.TextChanged += (_, _) => run.IsEnabled = Valid(out _);
        run.Click += (_, _) => { if (Valid(out int value)) { selected = value; dialog.DialogResult = true; } };
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? selected : null;
    }
}
