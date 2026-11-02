using System;
using System.Globalization;
using System.Windows.Data;

namespace FamilyQuant;

public sealed class AccessionTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        FamilyNaming.Accession(value as string ?? string.Empty);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
