using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class USearchLib
{
    private void ApplyViewModel()
    {
        ButtonInput.IsEnabled = !ViewModel.IsBusy;
        ButtonBuildFamilies.IsEnabled = ViewModel.CanRun;
        ButtonDivideAndConquer.IsEnabled = ViewModel.CanRun;
        ButtonScanIdentities.IsEnabled = ViewModel.CanRun;
        ButtonRemoveRedundancy.IsEnabled = ViewModel.CanRun;
        ButtonCalculateFDR.IsEnabled = ViewModel.CanScore;
        IntegerUpDownIdentity.IsEnabled = !ViewModel.IsBusy;
        IntegerUpDownRedundancyIdentity.IsEnabled = !ViewModel.IsBusy;
        DecimalUpDownMaxFDR.IsEnabled = !ViewModel.IsBusy;
        CheckBoxExhaustiveClustering.IsEnabled = !ViewModel.IsBusy;
        TextBlockBuildResult.Text = ViewModel.ResultSummary;
        TextBlockUSearchState.Text = ViewModel.Status;
        ShellStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private string? ChooseDatabasePath()
    {
        if (SelectDatabasePathOverride != null) return SelectDatabasePathOverride();
        if (PresentationSource.FromVisual(this) == null) return null;
        var dialog = new OpenFileDialog
        {
            Filter = "Protein database (*.fasta;*.fa;*.fas;*.T-R;*.T)|*.fasta;*.fa;*.fas;*.T-R;*.T",
            Title = "Choose the protein database"
        };
        if (!ShellDialogs.ShowFile(this, "build.database", dialog)) return null;
        return dialog.FileName;
    }

    private bool RequireDatabase()
    {
        if (File.Exists(ViewModel.DatabasePath)) return true;
        ViewModel.Report("Choose an existing database before running this action.");
        return false;
    }

    private MessageBoxResult ShowBuildMessage(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        ViewModel.Report(message.Split('\n')[0], message);
        TextBlockUSearchState.Text = ViewModel.Status;
        ShellStatusChanged?.Invoke(this, EventArgs.Empty);
        return ShellMessages.Report(this, message, title, buttons, image, defaultResult,
            showDialog: !(buttons == MessageBoxButton.OK && image == MessageBoxImage.Information));
    }

    private void ButtonBuildHelp_Click(object sender, RoutedEventArgs args)
        => ShowHelp();

    public void ShowHelp()
    {
        if (PresentationSource.FromVisual(this) == null) return;
        TextReportWindow.Show("Build families: identity and mixing", BuildHelpText(), Window.GetWindow(this));
    }

    public string BuildHelpText() =>
        "Choose a database, set identity, and Build families. The Family Cluster File is written beside the database.\n\n" +
        "The fast pass depends on sequence order. The exhaustive pass aligns every pair and closes gaps the fast pass leaves; " +
        "it is on by default, with one confirmation above 10,000 sequences and no automatic size cap.\n\n" +
        "Scan identity range retains the 95 to 10 percent sweep. Find lowest identity retains Divide and Conquer and its allowed mixing fraction. " +
        "Both remain available until their equivalence is measured. Nothing from either search is saved as a family file.\n\n" +
        "The mixing score detects target-decoy joins, not two unrelated target families merging. Its clean identity is a floor, not a recommendation. " +
        "A database without decoys gives zero by construction. Use family counts and biological evidence when choosing above that floor.\n\n" +
        "Remove redundancy writes a separate _NO_REDUNDANCY.fasta, preserving sequence order and headers. " +
        "At 100 percent it removes exact subsets and duplicates; lower values also remove near-identical sequences by alignment.\n\n" +
        HelpWorkflowFacts.Build + ViewModel.Details;
}
