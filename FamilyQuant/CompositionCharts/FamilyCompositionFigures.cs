using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace FamilyQuant;

/// <summary>Screen/report seam over a single immutable snapshot; report PNGs always use pinned Light.</summary>
public static partial class FamilyCompositionFigures
{
    public static List<AnalyticsFigure> Build(FamilyCompositionData data) => new()
    {
        new("family_composition.png", new FamilyCompositionChart(data), Math.Max(540, 190 + data.Families.Count * 52)),
        new("family_subclasses.png", new FamilySubclassChart(data),
            Math.Max(540, 190 + data.Families.Sum(r => Math.Max(64, 37 + r.Subclasses.Count * 18))))
    };

    public static byte[] RenderPng(AnalyticsFigure figure)
    {
        var bitmap = ChartImageExporter.Render(figure.Chart, 960, figure.ScreenHeight, variant: ChartPaletteVariant.Light);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream(); encoder.Save(memory); return memory.ToArray();
    }

    public static IReadOnlyList<string> Save(FamilyCompositionData data, string folder)
    {
        Directory.CreateDirectory(folder);
        return Build(data).Select(f =>
        {
            string path = Path.Combine(folder, f.FileName); File.WriteAllBytes(path, RenderPng(f)); return path;
        }).ToArray();
    }
}
