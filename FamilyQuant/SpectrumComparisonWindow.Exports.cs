using FamilyQuant.DeNovo;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant
{
    public static partial class SpectrumComparisonWindow
    {
        private static async void SaveAcceptedScoreScatter(Button button, TextBlock status,
            DeNovoAcceptedScoreScatter scatter, string? existingCsvPath)
        {
            if (PresentationSource.FromVisual(button) == null)
            {
                status.Text = "Nothing was saved because this comparison window is not on a display. " +
                              "Open it from the loaded project and choose Save accepted-call score plot.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save accepted PEAKS versus DeepNovo score plot and exact CSV",
                Filter = "PNG image (*.png)|*.png",
                FileName = PresentationFigures.DeNovoAcceptedScoreScatterFile,
                AddExtension = true,
                DefaultExt = ".png",
                OverwritePrompt = true,
            };
            string? folder = string.IsNullOrWhiteSpace(existingCsvPath)
                ? null : Path.GetDirectoryName(existingCsvPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                dialog.InitialDirectory = folder;
            if (!ShellDialogs.ShowFile(button, "spectrum.score-plot", dialog)) return;
            using var operation = AppOperationHost.For(button).Begin("Export accepted-call score plot");

            try
            {
                scatter.Validate();
                string csvPath = Path.ChangeExtension(dialog.FileName, ".csv");
                await AppOperationFiles.RunSta(() => AppOperationFiles.WriteFolder(Path.GetDirectoryName(dialog.FileName)!, directory =>
                {
                    ChartImageExporter.Save(new DeNovoAcceptedScoreScatterChart(scatter), Path.Combine(directory, Path.GetFileName(dialog.FileName)));
                    File.WriteAllText(Path.Combine(directory, Path.GetFileName(csvPath)),
                        DeNovoAcceptedScoreScatterStudy.ToCsv(scatter), new UTF8Encoding(false));
                }, operation.Context));
                status.Text = "Saved " + dialog.FileName + " and " + csvPath + ". " +
                              scatter.Total.ToString("N0", CultureInfo.CurrentCulture) +
                              " jointly accepted precursor-hypothesis row(s): " +
                              scatter.Matches.ToString("N0", CultureInfo.CurrentCulture) +
                              " strict matches and " +
                              scatter.Mismatches.ToString("N0", CultureInfo.CurrentCulture) +
                              " mismatches.";
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                status.Text = "Cancelled. The previous plot and CSV were kept.";
            }
            catch (Exception ex)
            {
                status.Text = "The accepted-call score plot was not saved. " + ex.Message;
            }
        }

        private static async void SaveCopy(Button button, TextBlock status, SpectrumComparisonViewModel model)
        {
            var rows = model.Rows;
            string? existingCsvPath = model.ExistingCsvPath;
            if (PresentationSource.FromVisual(button) == null)
            {
                status.Text = "Nothing was saved, because this window is not on a display and a file " +
                              "dialog there is a window nothing can close. Open this table in the " +
                              "application and press Save a copy again.";
                return;
            }

            string initialDirectory = string.Empty;
            if (!string.IsNullOrWhiteSpace(existingCsvPath))
            {
                initialDirectory = Path.GetDirectoryName(existingCsvPath) ?? string.Empty;
            }

            var dialog = new SaveFileDialog
            {
                Filter = FileDialogFilters.Csv,
                FileName = SuggestedFileName,
                Title = "Save the spectrum comparison table",
                AddExtension = true,
                DefaultExt = ".csv",
                InitialDirectory = initialDirectory,
            };
            if (!ShellDialogs.ShowFile(button, "spectrum.csv", dialog)) return;
            using var operation = AppOperationHost.For(button).Begin("Export spectrum comparison");

            try
            {
                await System.Threading.Tasks.Task.Run(() => AppOperationFiles.WriteFile(dialog.FileName, model.SaveCsv, operation.Context));
                status.Text = "Written " + rows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                              " spectrum row(s) to " + dialog.FileName +
                              ". The project and its quantitation were not changed.";
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                status.Text = "Cancelled. The previous CSV was kept.";
            }
            catch (Exception ex)
            {
                status.Text = "The CSV could not be written, so nothing was saved. Reason given by the " +
                              "program: " + ex.Message + " Check that the file is not open elsewhere " +
                              "and that you can write to that folder, then save again.";
            }
        }
    }
}
