using FamilyQuant.Themes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests
{
    internal static class LogoAssets
    {
        internal const string Argument = "--logo-assets";
        internal static readonly int[] IconSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        internal static bool IsRequested(string[] args) => args.Contains(Argument, StringComparer.OrdinalIgnoreCase);

        internal static int Run(string[] args)
        {
            App? app = null;
            try
            {
                int flag = Array.FindIndex(args, a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase));
                if (flag < 0 || flag + 1 >= args.Length || args[flag + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException(Argument + " requires an output directory.");
                string output = Path.GetFullPath(args[flag + 1]);
                Directory.CreateDirectory(output);
                app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                ThemeManager.Apply(AppTheme.Dark);
                var mark = (DrawingImage)app.FindResource("StartPageLogo");
                if (app.FindResource("AppWindowIcon") is not BitmapFrame icon
                    || icon.Decoder == null || icon.Decoder.Frames.Count != IconSizes.Length)
                    throw new InvalidDataException("AppWindowIcon did not load the multi-size ICO resource.");
                var images = new Dictionary<int, byte[]>();
                foreach (int size in IconSizes.Concat(new[] { 512, 1024 }))
                {
                    byte[] png = Png(mark, size, size);
                    File.WriteAllBytes(Path.Combine(output, "FamilyQuant-mark-" + size + ".png"), png);
                    if (size <= 256) images.Add(size, png);
                }
                SaveIcon(Path.Combine(output, "FamilyQuant.ico"), images);
                File.WriteAllText(Path.Combine(output, "FamilyQuant-mark.svg"), MarkSvg(mark));
                foreach (AppTheme mode in new[] { AppTheme.Dark, AppTheme.Light })
                {
                    ThemeManager.Apply(mode);
                    Brush ink = (Brush)app.FindResource("TextBrush");
                    var lockup = Lockup(mark, ink);
                    string suffix = mode.ToString().ToLowerInvariant();
                    File.WriteAllBytes(Path.Combine(output, "FamilyQuant-lockup-" + suffix + ".png"), Png(lockup, 1024, 512));
                    File.WriteAllText(Path.Combine(output, "FamilyQuant-lockup-" + suffix + ".svg"), LockupSvg(mark, ink));
                }
                string preview = args.FirstOrDefault(a => a.StartsWith("--preview=", StringComparison.OrdinalIgnoreCase))
                    ?.Substring("--preview=".Length) ?? Path.Combine(output, "preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(preview))!);
                Preview(mark, preview);
                Console.WriteLine("Logo assets: " + output + "; ICO frames " + string.Join(", ", IconSizes));
                Console.WriteLine("Preview: " + preview);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("Logo asset export failed: " + ex); return 1; }
            finally { app?.Shutdown(); }
        }

        internal static byte[] Png(ImageSource source, int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, width, height));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static void SaveIcon(string path, IReadOnlyDictionary<int, byte[]> frames)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Count);
            int offset = 6 + frames.Count * 16;
            foreach (var frame in frames)
            {
                writer.Write((byte)(frame.Key == 256 ? 0 : frame.Key));
                writer.Write((byte)(frame.Key == 256 ? 0 : frame.Key));
                writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frame.Value.Length); writer.Write(offset);
                offset += frame.Value.Length;
            }
            foreach (var frame in frames) writer.Write(frame.Value);
        }

        private static DrawingImage Lockup(DrawingImage mark, Brush ink)
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing((Brush)Application.Current.FindResource("ClearBrush"), null,
                new RectangleGeometry(new Rect(0, 0, 1024, 512))));
            var symbol = new DrawingGroup { Transform = new TranslateTransform(384, 16) };
            symbol.Children.Add(mark.Drawing);
            group.Children.Add(symbol);
            Geometry text = WordGeometry();
            Rect bounds = text.Bounds;
            text.Transform = new TranslateTransform((1024 - bounds.Width) / 2 - bounds.Left, 312 - bounds.Top);
            group.Children.Add(new GeometryDrawing(ink, null, text));
            return new DrawingImage(group);
        }

        private static FormattedText WordText() => new FormattedText("FamilyQuant", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal,
                FontWeights.SemiBold, FontStretches.Normal), 128, (Brush)Application.Current.FindResource("TextBrush"), 1);

        private static Geometry WordGeometry() => WordText().BuildGeometry(new Point(0, 0));

        private static string MarkSvg(DrawingImage mark)
        {
            var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" width=\"256\" height=\"256\" role=\"img\"><title>FamilyQuant</title>\n");
            svg.Append(MarkShapes(mark));
            return svg.Append("</svg>\n").ToString();
        }

        private static string MarkShapes(DrawingImage mark)
        {
            var svg = new StringBuilder();
            foreach (var drawing in ((DrawingGroup)mark.Drawing).Children.OfType<GeometryDrawing>())
            {
                var brush = (SolidColorBrush)drawing.Brush;
                if (brush.Color.A == 0) continue;
                var rectangle = (RectangleGeometry)drawing.Geometry;
                Rect r = rectangle.Rect;
                svg.AppendFormat(CultureInfo.InvariantCulture,
                    "<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" rx=\"{4}\" fill=\"{5}\"/>\n",
                    r.X, r.Y, r.Width, r.Height, rectangle.RadiusX, Css(brush));
            }
            return svg.ToString();
        }

        private static string LockupSvg(DrawingImage mark, Brush ink)
        {
            var text = WordText();
            Rect bounds = text.BuildGeometry(new Point(0, 0)).Bounds;
            string x = ((1024 - bounds.Width) / 2 - bounds.Left).ToString(CultureInfo.InvariantCulture);
            string baseline = (312 - bounds.Top + text.Baseline).ToString(CultureInfo.InvariantCulture);
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1024 512\" width=\"1024\" height=\"512\" role=\"img\"><title>FamilyQuant</title>\n"
                + "<g transform=\"translate(384 16)\">" + MarkShapes(mark) + "</g>\n"
                + "<text x=\"" + x + "\" y=\"" + baseline
                + "\" font-family=\"Segoe UI Variable Display, Segoe UI, sans-serif\" font-size=\"128\" font-weight=\"600\" fill=\""
                + Css((SolidColorBrush)ink) + "\">FamilyQuant</text>\n</svg>\n";
        }

        private static string Css(SolidColorBrush brush) => "#" + brush.Color.R.ToString("X2")
            + brush.Color.G.ToString("X2") + brush.Color.B.ToString("X2");

        private static void Preview(DrawingImage mark, string path)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (var dc = visual.RenderOpen())
            {
                int column = 0;
                foreach (AppTheme mode in new[] { AppTheme.Dark, AppTheme.Light })
                {
                    ThemeManager.Apply(mode);
                    double left = column++ * 480;
                    dc.DrawRectangle((Brush)Application.Current.FindResource("CanvasBrush"), null, new Rect(left, 0, 480, 580));
                    void Label(string text, double x, double y, double size) => dc.DrawText(new FormattedText(text,
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size,
                        (Brush)Application.Current.FindResource("TextBrush"), 1), new Point(left + x, y));
                    Label(mode.ToString(), 24, 18, 18);
                    dc.DrawImage(mark, new Rect(left + 32, 72, 16, 16)); Label("16 px", 24, 98, 12);
                    dc.DrawImage(mark, new Rect(left + 96, 64, 32, 32)); Label("32 px", 92, 110, 12);
                    using var small = new MemoryStream(Png(mark, 16, 16));
                    var enlarged = BitmapFrame.Create(small, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var smallPixels = new FormatConvertedBitmap(enlarged, PixelFormats.Bgra32, null, 0);
                    var rgba = new byte[16 * 16 * 4]; smallPixels.CopyPixels(rgba, 16 * 4, 0);
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                        {
                            int index = (y * 16 + x) * 4;
                            var colour = Color.FromArgb(rgba[index + 3], rgba[index + 2], rgba[index + 1], rgba[index]);
                            dc.DrawRectangle(new SolidColorBrush(colour), null, new Rect(left + 24 + x * 8, 160 + y * 8, 8, 8));
                        }
                    Label("16 px at 8x", 24, 298, 12);
                    dc.DrawImage(mark, new Rect(left + 160, 64, 256, 256)); Label("256 px", 244, 328, 12);
                    dc.DrawImage(Lockup(mark, (Brush)Application.Current.FindResource("TextBrush")), new Rect(left + 24, 350, 432, 216));
                }
            }
            var bitmap = new RenderTargetBitmap(960, 580, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(path); encoder.Save(output);
        }
    }
}
