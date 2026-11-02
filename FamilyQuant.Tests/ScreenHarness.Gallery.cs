using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class ScreenHarness
{
    internal static int FinishGallery(string root)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "screens.json")));
        var frames = manifest.RootElement.GetProperty("FinishCaptures").EnumerateArray().ToArray();
        string output = Path.Combine(root, "gallery"); Directory.CreateDirectory(output);
        int count = 0;
        foreach (var group in frames.GroupBy(frame => frame.GetProperty("Theme").GetString() + "-"
            + (frame.GetProperty("Scale").GetDouble() * 100).ToString(CultureInfo.InvariantCulture) + "-"
            + ((frame.TryGetProperty("MinimumScenario", out var minimum) ? minimum.GetBoolean() : frame.GetProperty("DipWidth").GetInt32() < 1366) ? "minimum" : "large")))
        {
            const int columns = 4, tileWidth = 450, tileHeight = 340;
            var items = group.ToArray(); int rows = (int)Math.Ceiling(items.Length / (double)columns);
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, columns * tileWidth, rows * tileHeight));
                for (int index = 0; index < items.Length; index++)
                {
                    string file = items[index].GetProperty("File").GetString()!;
                    var bitmap = BitmapFrame.Create(new Uri(Path.GetFullPath(Path.Combine(root, file))), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    double factor = Math.Min(tileWidth / (double)bitmap.PixelWidth, (tileHeight - 35) / (double)bitmap.PixelHeight);
                    double left = index % columns * tileWidth, top = index / columns * tileHeight;
                    dc.DrawImage(bitmap, new Rect(left, top + 30, bitmap.PixelWidth * factor, bitmap.PixelHeight * factor));
                    dc.DrawText(new FormattedText(Path.GetFileNameWithoutExtension(file), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.Black, 1), new Point(left + 4, top + 4));
                }
            }
            var image = new RenderTargetBitmap(columns * tileWidth, rows * tileHeight, 96, 96, PixelFormats.Pbgra32); image.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(output, group.Key.Replace('/', '-') + ".png"))) encoder.Save(stream);
            count++;
        }
        Console.WriteLine("Native review gallery: " + count + " sheets from " + frames.Length + " unchanged source PNGs.");
        return 0;
    }
}
