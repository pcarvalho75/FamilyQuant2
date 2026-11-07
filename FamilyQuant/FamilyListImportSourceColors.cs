using FamilyQuant.Panels.Import;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant;

public sealed class ImportHeaderLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string { Length: > 0 } text ? text : "Not used";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ImportSourceVisibleTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not PanelPreviewCell cell || values[1] is not int row) return "";
        string first = cell.MergeRange.Split(':')[0].Replace("$", "", StringComparison.Ordinal);
        return first.Length == 0 || first.Equals(cell.ColumnLetter + row.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase) ? cell.Text : "";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Workbook fills are source data (desk M50), never theme colours or decisions.</summary>
public sealed class ImportSourceFillConverter : IValueConverter
{
    internal static Color? Read(PanelPreviewCell? cell)
    {
        if (cell == null || cell.Fill.Length != 7 || cell.Fill[0] != '#') return null;
        try { return (Color)ColorConverter.ConvertFromString(cell.Fill); }
        catch (FormatException) { return null; }
    }
    internal static bool Neutral(PanelPreviewCell cell, Color color)
    {
        if (cell.FillName.Contains("yellow", StringComparison.OrdinalIgnoreCase) || cell.FillName.Contains("gold", StringComparison.OrdinalIgnoreCase)) return true;
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        if (delta < 0.03) return false;
        double hue = 60 * (max == r ? (g - b) / delta % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return hue >= 30 && hue <= 80;
    }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PanelPreviewCell cell || Read(cell) is not Color color || Neutral(cell, color)) return Application.Current.FindResource("SurfaceBrush");
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ImportSourceInkConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PanelPreviewCell cell || ImportSourceFillConverter.Read(cell) is not Color color || ImportSourceFillConverter.Neutral(cell, color))
            return Application.Current.FindResource("TextBrush");
        double brightness = (color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722) / 255d;
        return Application.Current.FindResource(brightness < 0.5 ? "TextOnDarkBrush"
            : Themes.ThemeManager.Effective == Themes.AppTheme.Dark ? "AccentInkBrush" : "TextBrush");
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ImportSourceDetailConverter : IValueConverter
{
    public static string Describe(PanelPreviewCell cell) => cell.Text
        + (cell.Fill.Length == 0 ? "" : "\nSource fill: " + cell.FillName + " (" + cell.Fill + ")"
            + (ImportSourceFillConverter.Read(cell) is Color color && ImportSourceFillConverter.Neutral(cell, color) ? "; shown neutrally" : ""))
        + (cell.MergeRange.Length == 0 ? "" : "\nMerged cells: " + cell.MergeRange);
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is PanelPreviewCell cell ? Describe(cell) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
