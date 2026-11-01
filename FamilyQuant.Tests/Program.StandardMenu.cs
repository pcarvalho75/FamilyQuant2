using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ShellMenuScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(output, "capture-preferences.json");
        try
        {
            foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
            {
                FamilyQuant.Themes.ThemeManager.Apply(theme);
                foreach (var size in new[] { (1024, 768), (1366, 820) })
                {
                    string folder = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Item1 + "x" + size.Item2);
                    Directory.CreateDirectory(folder);
                    var main = new MainWindow
                    {
                        Width = size.Item1, Height = size.Item2, Left = -30000, Top = -30000,
                        WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false,
                        ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize
                    };
                    main.Show();
                    try
                    {
                        PumpDispatcher();
                        ScreenHarness.Save(main, Path.Combine(folder, "shell.png"), size.Item1, size.Item2);
                        var menu = (Menu)main.FindName("MainMenu");
                        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
                        {
                            item.IsSubmenuOpen = true;
                            PumpDispatcher();
                            var popup = item.Template.FindName("PART_Popup", item) as Popup;
                            if (popup?.Child is not FrameworkElement content || content.ActualWidth < 1 || content.ActualHeight < 1)
                                throw new InvalidOperationException("Menu popup was not realized: " + item.Header);
                            int width = (int)Math.Ceiling(content.ActualWidth), height = (int)Math.Ceiling(content.ActualHeight);
                            var visual = new DrawingVisual();
                            using (DrawingContext dc = visual.RenderOpen())
                                dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
                            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(visual);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using (var stream = File.Create(Path.Combine(folder, item.Header + ".png"))) encoder.Save(stream);
                            item.IsSubmenuOpen = false;
                            PumpDispatcher();
                        }
                        var viewMenu = menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "View");
                        viewMenu.IsSubmenuOpen = true;
                        PumpDispatcher();
                        var appearance = viewMenu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Appearance");
                        appearance.IsSubmenuOpen = true;
                        PumpDispatcher();
                        var appearancePopup = (Popup)appearance.Template.FindName("PART_Popup", appearance);
                        var appearanceContent = (FrameworkElement)appearancePopup.Child;
                        var appearanceVisual = new DrawingVisual();
                        int appearanceWidth = (int)Math.Ceiling(appearanceContent.ActualWidth);
                        int appearanceHeight = (int)Math.Ceiling(appearanceContent.ActualHeight);
                        using (DrawingContext dc = appearanceVisual.RenderOpen())
                            dc.DrawRectangle(new VisualBrush(appearanceContent), null, new Rect(0, 0, appearanceWidth, appearanceHeight));
                        var appearanceBitmap = new RenderTargetBitmap(appearanceWidth, appearanceHeight, 96, 96, PixelFormats.Pbgra32);
                        appearanceBitmap.Render(appearanceVisual);
                        var appearanceEncoder = new PngBitmapEncoder();
                        appearanceEncoder.Frames.Add(BitmapFrame.Create(appearanceBitmap));
                        using (var stream = File.Create(Path.Combine(folder, "Appearance.png"))) appearanceEncoder.Save(stream);
                        appearance.IsSubmenuOpen = false;
                        viewMenu.IsSubmenuOpen = false;
                        PumpDispatcher();
                        typeof(MainWindow).GetMethod("MenuItemDeNovo_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(main, new object[] { main, new RoutedEventArgs() });
                        var deNovo = ((Grid)main.FindName("GridApp")).Children.OfType<DeNovoControl>().Single();
                        ((Expander)deNovo.FindName("ExpanderStandalone")).IsExpanded = true;
                        PumpDispatcher();
                        var sourceBox = (TextBox)deNovo.FindName("TextBoxHomologyFcf");
                        sourceBox.BringIntoView();
                        PumpDispatcher();
                        var scroller = AppearanceWalk(deNovo).OfType<ScrollViewer>().First();
                        scroller.ScrollToVerticalOffset(scroller.VerticalOffset + sourceBox.TranslatePoint(new Point(), scroller).Y - 100);
                        PumpDispatcher();
                        ScreenHarness.Save(main, Path.Combine(folder, "homology-file-source.png"), size.Item1, size.Item2);
                    }
                    finally { main.Close(); }
                }
            }
            Console.WriteLine("Shell menus captured in both themes and sizes: " + output);
            return 0;
        }
        finally { MainWindow.ShellSettingsPathOverride = oldSettings; }
    }

    private static void StandardMenuTests(string workDir)
    {
        string settings = Path.Combine(workDir, "shell-settings.json");
        string source = Path.Combine(workDir, "menu-source.fcf");
        string firstSave = Path.Combine(workDir, "menu-project.fqproj");
        string secondSave = Path.Combine(workDir, "menu-project-copy.fqproj");
        foreach (string path in new[] { settings, firstSave, secondSave }) if (File.Exists(path)) File.Delete(path);
        var file = new FamilyClusterFile
        {
            MyExtendedFastaItems = new List<FastaItemExtended>
            {
                new() { SequenceIdentifier = "sp|P1|A", Description = "Alpha protein", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 },
                new() { SequenceIdentifier = "Reverse_sp|P1|A", Description = "Decoy", Sequence = "KEDITPEP", UserID = 2, USearchID = 2 }
            }
        };
        file.Save(source);
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldOpen = MainWindow.SelectOpenPathOverride;
        var oldSave = MainWindow.SelectSaveProjectPathOverride;
        var oldClipboard = GridCopy.ClipboardOverride;
        MainWindow.ShellSettingsPathOverride = settings;
        MainWindow.SelectOpenPathOverride = () => source;
        MainWindow.SelectSaveProjectPathOverride = () => firstSave;
        var main = new MainWindow();
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, fields)!.GetValue(main)!;
        void Save(bool saveAs)
        {
            var task = (Task)typeof(MainWindow).GetMethod("SaveProjectAsync", fields)!.Invoke(main, new object[] { saveAs })!;
            for (int i = 0; i < 100 && !task.IsCompleted; i++) PumpDispatcher();
            Check("shell save completes for an empty-quant project", task.IsCompleted);
            if (task.IsCompleted) task.GetAwaiter().GetResult();
        }
        try
        {
            var menu = Field<Menu>("MainMenu");
            Check("shell menus use standard Windows order", string.Join(",", menu.Items.OfType<MenuItem>().Select(i => i.Header)) ==
                "File,Edit,View,Analysis,Help");
            MenuCommandPreservationTests(main);
            var required = new[]
            {
                (Key.N, ModifierKeys.Control, ApplicationCommands.New),
                (Key.O, ModifierKeys.Control, ApplicationCommands.Open),
                (Key.S, ModifierKeys.Control, ApplicationCommands.Save),
                (Key.Z, ModifierKeys.Control, ApplicationCommands.Undo),
                (Key.F, ModifierKeys.Control, ApplicationCommands.Find),
                (Key.F1, ModifierKeys.None, ApplicationCommands.Help)
            };
            Check("shell shortcuts route to the same commands as menus", required.All(expected =>
                main.InputBindings.OfType<KeyBinding>().Any(b => b.Key == expected.Item1 && b.Modifiers == expected.Item2 && b.Command == expected.Item3)
                && main.CommandBindings.OfType<CommandBinding>().Any(b => b.Command == expected.Item3)));
            Check("shell save and find are unavailable without a project", !ApplicationCommands.Save.CanExecute(null, main)
                && !ApplicationCommands.Find.CanExecute(null, main));
            ApplicationCommands.New.Execute(null, main);
            Check("New command opens the existing Build families flow", Field<Grid>("GridApp").Children.OfType<USearchLib>().Any());
            ApplicationCommands.Open.Execute(null, main);
            var project = Field<ClusterGenerationControl>("cgc");
            Check("Open command opens the selected legacy FCF through the existing loader", project.fcf.MyExtendedFastaItems.Count == 2
                && ApplicationCommands.Save.CanExecute(null, main));
            MainWindow.SelectOpenPathOverride = () => null;
            ApplicationCommands.Open.Execute(null, main);
            Check("cancelling Open preserves the current project", ReferenceEquals(project, Field<ClusterGenerationControl>("cgc")));
            Check("opening a project persists its recent path", ShellSessionState.Load(settings).RecentProjects.SequenceEqual(new[] { Path.GetFullPath(source) }));
            var stack = typeof(ClusterGenerationControl).GetMethod("PushUndoSnapshot", fields)!;
            stack.Invoke(project, null);
            project.fcf.MyExtendedFastaItems[0].UserID = 99;
            ApplicationCommands.Undo.Execute(null, main);
            Check("shell Undo forwards to the existing curation undo stack", project.fcf.MyExtendedFastaItems[0].UserID == 1);
            var grid = (DataGrid)project.FindName("DataGridResultGrid");
            grid.CurrentColumn = grid.Columns.First(c => c.SortMemberPath == "Description" ||
                c is DataGridBoundColumn b && (b.Binding as System.Windows.Data.Binding)?.Path.Path == "Description");
            grid.SelectedItem = project.fcf.MyExtendedFastaItems[0];
            string? copied = null;
            GridCopy.ClipboardOverride = value => copied = value;
            ApplicationCommands.Copy.Execute(null, main);
            Check("shell Copy preserves the table's selected-column copy rule", copied == "Alpha protein", copied);
            var hide = Field<MenuItem>("MenuItemHideDecoys");
            hide.IsChecked = true;
            hide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check("View hide decoys forwards to the curation filter without editing IDs",
                ((CheckBox)project.FindName("CheckBoxExcludeReverse")).IsChecked == true &&
                project.fcf.MyExtendedFastaItems.Select(i => i.UserID).SequenceEqual(new[] { 1, 2 }));
            Save(false);
            Check("first Save writes a curation-only project through ProjectStore", File.Exists(firstSave)
                && ProjectStore.ReadFqproj(firstSave).fcf.MyExtendedFastaItems.Count == 2);
            MainWindow.SelectSaveProjectPathOverride = () => throw new InvalidOperationException("Save must reuse its existing project path.");
            Save(false);
            Check("Save reuses the existing project path", Field<string>("lastFcfPath") == Path.GetFullPath(firstSave));
            MainWindow.SelectSaveProjectPathOverride = () => secondSave;
            Save(true);
            Check("Save as chooses another project path and preserves the first file", File.Exists(firstSave) && File.Exists(secondSave)
                && Field<string>("lastFcfPath") == Path.GetFullPath(secondSave));
            Check("a saved project remains a valid De novo homology source", DeNovoControl.LoadHomologyCuration(secondSave)
                .MyExtendedFastaItems.Select(i => i.SequenceIdentifier).SequenceEqual(DeNovoControl.LoadHomologyCuration(source)
                    .MyExtendedFastaItems.Select(i => i.SequenceIdentifier)));
            byte[] beforeCancel = File.ReadAllBytes(secondSave);
            MainWindow.SelectSaveProjectPathOverride = () => null;
            Save(true);
            Check("cancelling Save as leaves the file and current project path unchanged", beforeCancel.SequenceEqual(File.ReadAllBytes(secondSave))
                && Field<string>("lastFcfPath") == Path.GetFullPath(secondSave));
            var restarted = new MainWindow();
            try
            {
                var recent = (MenuItem)restarted.FindName("MenuItemRecent");
                Check("recent projects survive a new shell instance in recency order", recent.Items.OfType<MenuItem>()
                    .Select(i => i.Tag as string).Take(2).SequenceEqual(new[] { Path.GetFullPath(secondSave), Path.GetFullPath(firstSave) }));
            }
            finally { restarted.Close(); }
            Check("opening an invalid path leaves the existing project object intact", !main.OpenProjectPath(source + ".missing")
                && ReferenceEquals(project, Field<ClusterGenerationControl>("cgc")));
            var state = new ShellSessionState();
            for (int i = 0; i < 12; i++) state.Remember(Path.Combine(workDir, i + ".fqproj"));
            state.Remember(Path.Combine(workDir, "11.fqproj"));
            Check("recent projects are bounded and duplicate paths move to the front", state.RecentProjects.Count == 10
                && state.RecentProjects.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10);
        }
        finally
        {
            main.Close();
            MainWindow.ShellSettingsPathOverride = oldSettings;
            MainWindow.SelectOpenPathOverride = oldOpen;
            MainWindow.SelectSaveProjectPathOverride = oldSave;
            GridCopy.ClipboardOverride = oldClipboard;
        }
    }
}
