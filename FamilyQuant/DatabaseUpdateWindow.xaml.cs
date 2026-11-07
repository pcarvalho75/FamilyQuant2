using FamilyQuant.DatabaseUpdate;
using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace FamilyQuant;

/// <summary>The Update database preview. It shows what the core computed and returns the user's choice;
/// the shell compares again or applies. Nothing here changes the project.</summary>
public partial class DatabaseUpdateWindow : Window
{
    /// <summary>Tests read the copied text here instead of the system clipboard.</summary>
    public static Action<string>? ClipboardOverride { get; set; }

    public DatabaseUpdateViewModel Model { get; }

    public DatabaseUpdateWindow(DatabaseUpdateViewModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        InitializeComponent();
        DataContext = model;
        // Fits a 1024 x 768 screen as it opens, and never outgrows the working area of a smaller one.
        Rect area = SystemParameters.WorkArea;
        MaxWidth = area.Width;
        MaxHeight = area.Height;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => Finish(DatabaseUpdateOutcome.Apply);
    private void CompareAgain_Click(object sender, RoutedEventArgs e) => Finish(DatabaseUpdateOutcome.CompareAgain);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(DatabaseUpdateOutcome.Cancel);

    private void Finish(DatabaseUpdateOutcome outcome)
    {
        if (Model.Choose(outcome)) Close();
    }

    private void CopyRows_Click(object sender, RoutedEventArgs e)
    {
        string text = Model.VisibleCsv();
        if (ClipboardOverride != null) { ClipboardOverride(text); Model.Tell("The rows shown were copied as CSV."); return; }
        try { Clipboard.SetText(text); Model.Tell("The rows shown were copied as CSV."); }
        catch (ExternalException) { Model.Tell("Nothing was copied: another program is holding the clipboard. Try again in a moment."); }
    }

    private void SaveCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = FileDialogFilters.Csv, AddExtension = true, DefaultExt = ".csv",
            FileName = "database_update_preview.csv", Title = "Save every row of the update preview as CSV",
        };
        if (!ShellDialogs.ShowFile(this, "database-update.csv", dialog)) return;
        try
        {
            DatabaseUpdateCsv.Write(dialog.FileName, Model.Preview.Rows);
            Model.Tell("Saved " + Path.GetFileName(dialog.FileName) + ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Model.Tell("The CSV could not be saved: " + ex.Message);
        }
    }
}
