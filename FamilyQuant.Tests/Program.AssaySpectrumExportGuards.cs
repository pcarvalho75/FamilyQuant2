using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void AssaySpectrumExportGuardTests()
    {
        const string source = "        private static void SaveCopy(Button button)\n        {\n"
            + "var dialog = new SaveFileDialog { Title = \"Save table\", DefaultExt = \".csv\" };\n"
            + "status.Text = \"Written \" + rows.Count.ToString(\"N0\", CultureInfo.CurrentCulture) + \" to \" + dialog.FileName;\n        }\n";
        FeatureLedger.Observation Row(string raw) { int offset = source.IndexOf(raw, StringComparison.Ordinal); return new("FQ15-"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("FamilyQuant/SpectrumComparisonWindow.cs|" + offset.ToString(CultureInfo.InvariantCulture) + "|code fact")))[..12],
            "FamilyQuant/SpectrumComparisonWindow.cs", source[..offset].Split('\n').Length, "code fact", raw); }
        var title = Row("\"Save table\""); var status = Row("\"N0\"");
        var actual = new Dictionary<string, string>
        {
            ["csv/dialog-cancel/title"] = "Save table", ["inputs/rows"] = "1234",
            ["csv/success/selected"] = "<output>\\fixture.csv", ["csv/success/status"] = "Written 1,234 to <output>\\fixture.csv"
        };
        Check("spectrum export dialog receipt uses the exact frozen property argument",
            AssaySpectrumExportComparison.Compare(title, source, actual).Passed);
        Check("spectrum status receipt evaluates the complete frozen expression and numeric format",
            AssaySpectrumExportComparison.Compare(status, source, actual).Passed);
        actual["csv/success/status"] = "Written 1234 to <output>\\fixture.csv";
        Check("a changed native numeric display format cannot borrow another status",
            !AssaySpectrumExportComparison.Compare(status, source, actual).Passed);
        actual.Remove("csv/dialog-cancel/title"); actual["plot/dialog-cancel/title"] = "Save table";
        Check("a plot dialog cannot lend its title to the CSV action",
            !AssaySpectrumExportComparison.Compare(title, source, actual).Passed);
    }
}
