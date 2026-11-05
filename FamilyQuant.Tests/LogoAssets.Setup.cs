using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class LogoAssets
{
    private static void WriteSetupAssets(DrawingImage mark, string output)
    {
        var tile = (SolidColorBrush)Application.Current.FindResource("LogoTileBrush");
        var badge = (SolidColorBrush)Application.Current.FindResource("LogoMarkBrush");
        const string arrow = "M188,158 L212,158 212,190 226,190 200,216 174,190 188,190 Z M174,222 L226,222 226,230 174,230 Z";
        var group = new DrawingGroup();
        group.Children.Add(mark.Drawing);
        group.Children.Add(new GeometryDrawing(badge, new Pen(tile, 8), new EllipseGeometry(new Point(200, 200), 50, 50)));
        group.Children.Add(new GeometryDrawing(tile, null, Geometry.Parse(arrow)));
        var setup = new DrawingImage(group);
        var frames = new Dictionary<int, byte[]>();
        foreach (int size in IconSizes)
        {
            byte[] png = Png(setup, size, size);
            frames.Add(size, png);
            if (size is 128 or 256) File.WriteAllBytes(Path.Combine(output, "FamilyQuant-setup-" + size + ".png"), png);
        }
        SaveIcon(Path.Combine(output, "FamilyQuant-Setup.ico"), frames);
        File.WriteAllText(Path.Combine(output, "FamilyQuant-setup.svg"), MarkSvg(mark).Replace("<title>FamilyQuant</title>", "<title>FamilyQuant Setup</title>")
            .Replace("</svg>", $"<circle cx=\"200\" cy=\"200\" r=\"50\" fill=\"{Css(badge)}\" stroke=\"{Css(tile)}\" stroke-width=\"8\"/>\n<path d=\"{arrow}\" fill=\"{Css(tile)}\"/>\n</svg>"));
        Console.WriteLine("Setup brand assets: " + output + "; nine icon frames; app brand tokens only.");
    }
}
