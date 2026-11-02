using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SourceCaptionCommands() =>
    [new("--source-caption-check", 10380, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--source-caption-check", (args, _) => SourceCaptionCheck(args))];

    private static int SourceCaptionCheck(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply a fresh screenshot/report directory.");
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        Directory.CreateDirectory(output); EnsureApplication();
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(FindLedgerRoot()!, "docs/feature-ledger-scan.json")))!;
        var cases = new[] { ("FQ15-E710DB6ACB71", "peaks-only"), ("FQ15-883A4216AD4A", "peaks-all"),
            ("FQ15-78E657532626", "deepnovo"), ("FQ15-F7D6D6F41346", "union-only-deepnovo"), ("FQ15-88CE928F642E", "union-all-deepnovo") };
        var report = new List<object>(); var pictures = new List<(int Column, int Row, BitmapSource Image)>();
        int column = 0;
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        {
            Themes.ThemeManager.Apply(theme);
            var control = new DeNovoFilterControl(); control.Attach(new DeNovoFilterViewModel());
            var picker = (ComboBox)control.FindName("ComboBoxDeNovoSource");
            var panel = new Grid { Background = (Brush)Application.Current.FindResource("CanvasBrush") };
            panel.Children.Add(control);
            var window = new Window { Content = panel, Width = 760, Height = 300, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
            var facts = new List<(string Id, string Expected, string Actual, bool Visible)>();
            try
            {
                window.Show(); PumpDispatcher(); int row = 0;
                foreach (var item in cases)
                {
                    string frozen = inventory.Observations.Single(observation => observation.Id == item.Item1).Detail;
                    string expected = frozen[(frozen.IndexOf(" = ", StringComparison.Ordinal) + 3)..];
                    var option = picker.Items.OfType<ComboBoxItem>().Single(choice => choice.Tag as string == item.Item2);
                    picker.SelectedItem = option; PumpDispatcher(); panel.UpdateLayout();
                    string actual = option.Content?.ToString() ?? "";
                    bool visible = picker.IsVisible && picker.IsEnabled && ReferenceEquals(picker.SelectedItem, option);
                    facts.Add((item.Item1, expected, actual, visible));
                    var bitmap = new RenderTargetBitmap(760, 300, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel); bitmap.Freeze();
                    string file = theme + "-" + item.Item2 + ".png"; Save(bitmap, Path.Combine(output, file));
                    pictures.Add((column, row++, bitmap));
                    report.Add(new { theme = theme.ToString(), id = item.Item1, mode = item.Item2, expected, actual, visible, image = file });
                }
                Check("Y3 all five source facts are shown by the compiled control in " + theme,
                    facts.Count == 5 && facts.All(fact => fact.Visible && fact.Expected == fact.Actual));
            }
            finally { window.Close(); }
            column++;
        }
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            foreach (var picture in pictures)
                context.DrawImage(new CroppedBitmap(picture.Image, new Int32Rect(0, 0, 760, 65)), new Rect(picture.Column * 760, picture.Row * 65, 760, 65));
        var contact = new RenderTargetBitmap(1520, 325, 96, 96, PixelFormats.Pbgra32); contact.Render(drawing);
        Save(contact, Path.Combine(output, "both-themes.png"));
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "source-captions.json"), report);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
        static void Save(BitmapSource image, string path)
        {
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(path); png.Save(file);
        }
    }
}
