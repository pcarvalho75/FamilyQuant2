using FamilyQuant.Examples;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DisabledTableCommands() =>
    [new("--test-disabled-tables", 100, TestCommandPhase.Raw, args => args.Contains("--test-disabled-tables", StringComparer.Ordinal), (args, _) =>
    {
        DisabledTableTests(args.Length == 2 ? args[1] : null);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void DisabledTableTests(string? output = null)
    {
        Section("Z12p5f7 disabled native tables retain theme surfaces and readable text");
        EnsureApplication();
        string work = Path.Combine(Path.GetTempPath(), "fq-disabled-tables-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        string workbook = Path.Combine(work, "example.xlsx"); ExampleFamilyList.Write(workbook);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())), copyProject: (source, context) =>
                { started.TrySetResult(); context.CancellationToken.WaitHandle.WaitOne(); context.ThrowIfCancellationRequested(); return source.DeepCopy(context); });
                var wizard = new FamilyListImportWindow(model) { Left = -30000, Top = -30000, Width = 1366, Height = 768,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                wizard.Show(); Task<bool>? pending = null;
                try
                {
                    ReadyImportPreparation(model, workbook);
                    FinishReviewWork(async () =>
                    {
                        pending = model.NextAsync(); await started.Task; PumpDispatcher();
                        CheckDisabledTable(wizard, (DataGrid)wizard.FindName("SpecialRowsGrid"), theme + " wizard");
                        if (output != null) { Directory.CreateDirectory(output); ScreenHarness.Save(wizard, Path.Combine(output, theme + "-wizard.png"), 1366, 768); }
                        await model.CancelReadAsync(); await pending;
                    });
                }
                finally
                {
                    if (model.IsBusy) FinishReviewWork(model.CancelReadAsync);
                    if (pending != null) FinishReviewWork(async () => { await pending; });
                    wizard.Close(); PumpDispatcher();
                }
                var panel = new FamilyPanel { Name = "Supplied list", Families = new() { new() { Key = "one", DisplayName = "Supplied family" } } };
                var editor = new PanelEditorWindow(panel) { Left = -30000, Top = -30000, Width = 1366, Height = 768,
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                try
                {
                    editor.Show(); var grid = (DataGrid)editor.FindName("FamilyGrid");
                    var enabledInk = new DisabledTableInk { Ink = (Brush)editor.FindResource("TextBrush") };
                    var binding = new Binding(nameof(DisabledTableInk.Ink)) { Source = enabledInk };
                    BindingOperations.SetBinding(grid, Control.ForegroundProperty, binding);
                    object selected = grid.Items[0]; grid.SelectedItem = selected;
                    grid.IsEnabled = false; PumpDispatcher();
                    CheckDisabledTable(editor, grid, theme + " panel editor");
                    if (output != null) ScreenHarness.Save(editor, Path.Combine(output, theme + "-panel-editor.png"), 1366, 768);
                    if (theme == Themes.AppTheme.Dark)
                    {
                        Themes.ThemeManager.Apply(Themes.AppTheme.Light); PumpDispatcher();
                        CheckDisabledTable(editor, grid, "disabled editor theme switch to Light");
                        Themes.ThemeManager.Apply(Themes.AppTheme.Dark); PumpDispatcher();
                        CheckDisabledTable(editor, grid, "disabled editor theme switch to Dark");
                    }
                    grid.IsEnabled = true; PumpDispatcher();
                    Check(theme + " panel editor remains editable after leaving disabled state", grid.IsEnabled && !grid.IsReadOnly);
                    Check(theme + " leaving disabled state restores the caller's foreground binding", ReferenceEquals(BindingOperations.GetBindingBase(grid, Control.ForegroundProperty), binding)
                        && ReferenceEquals(grid.Foreground, enabledInk.Ink));
                    Check(theme + " leaving disabled state retains the selection", ReferenceEquals(grid.SelectedItem, selected));
                }
                finally { editor.Close(); PumpDispatcher(); }
                CheckDefaultTableForeground(theme);
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }

    private static void CheckDefaultTableForeground(Themes.AppTheme theme)
    {
        var panel = new FamilyPanel { Name = "Default table fixture", Families = new() { new() { Key = "one", DisplayName = "Supplied family" } } };
        var editor = new PanelEditorWindow(panel) { Left = -30000, Top = -30000, Width = 1080, Height = 720,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            var grid = (DataGrid)editor.FindName("FamilyGrid");
            Check("Z16a15 " + theme + " default table starts with no local foreground", grid.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue);
            editor.Show(); PumpDispatcher();
            Check("Z16a15 " + theme + " default table receives TextBrush on load", ReferenceEquals(grid.Foreground, editor.FindResource("TextBrush")));
            var other = theme == Themes.AppTheme.Dark ? Themes.AppTheme.Light : Themes.AppTheme.Dark;
            Themes.ThemeManager.Apply(other); PumpDispatcher();
            Check("Z16a15 " + theme + " default table follows a switch to " + other, ReferenceEquals(grid.Foreground, editor.FindResource("TextBrush")));
            Themes.ThemeManager.Apply(theme); PumpDispatcher();
            Check("Z16a15 " + theme + " default table follows the switch back", ReferenceEquals(grid.Foreground, editor.FindResource("TextBrush")));
        }
        finally { editor.Close(); PumpDispatcher(); }
    }

    private static void CheckDisabledTable(Window window, DataGrid grid, string label)
    {
        window.UpdateLayout(); grid.UpdateLayout();
        int width = (int)Math.Ceiling(grid.ActualWidth), height = (int)Math.Ceiling(grid.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(grid);
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(width / 2, height - 22, 1, 1), pixel, 4, 0);
        var surface = ((SolidColorBrush)window.FindResource("SurfaceBrush")).Color;
        var muted = ((SolidColorBrush)window.FindResource("MutedBrush")).Color;
        Check(label + " disabled table paints the theme surface", !grid.IsEnabled && pixel[3] == 255
            && Math.Abs(pixel[2] - surface.R) <= 2 && Math.Abs(pixel[1] - surface.G) <= 2 && Math.Abs(pixel[0] - surface.B) <= 2,
            "pixel=" + Convert.ToHexString(pixel) + "; surface=" + surface + "; grid=" + grid.Background);
        Check(label + " disabled table uses the muted text token", grid.Foreground is SolidColorBrush ink && ink.Color == muted,
            "foreground=" + grid.Foreground + "; muted=" + muted);
    }

    private sealed class DisabledTableInk { public Brush Ink { get; init; } = null!; }
}
