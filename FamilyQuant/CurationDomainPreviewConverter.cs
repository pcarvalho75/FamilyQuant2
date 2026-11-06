using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace FamilyQuant;

public sealed class CurationDomainPreviewConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not FastaItemExtended protein) return "";
        return values.Length > 1 && values[1] is IReadOnlyDictionary<FastaItemExtended, string> pending
            && pending.TryGetValue(protein, out string? domains) ? domains : protein.Domains;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
