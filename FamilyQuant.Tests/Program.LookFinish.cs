using System.Windows;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    internal static List<QuantRow> FinishQuantRows() => SyntheticAnalyticsRows();

    internal static bool VerifyFinishLayout(string name, FrameworkElement root, Size size)
    {
        int before = failed;
        CheckLayout(name, root, size, -1);
        return before == failed;
    }
}
