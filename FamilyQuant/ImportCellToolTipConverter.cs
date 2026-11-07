using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace FamilyQuant;

/// <summary>Keeps the cell's purpose, complete value and row explanation available together.</summary>
public sealed class ImportCellToolTipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        string.Join("\n\n", values.OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
