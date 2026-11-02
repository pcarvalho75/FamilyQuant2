using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int AssaySpectrumRowIsolationTests(string[] args, string workDir)
    {
        const string source = "grid.Columns.Add(Column(\"Scan\", nameof(SpectrumComparisonRow.ScanNumber), 80, \"Scan number.\"));";
        FeatureLedger.Observation Row(string literal) { int offset = source.IndexOf(literal, StringComparison.Ordinal); return new("FQ15-"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("FamilyQuant/SpectrumComparisonWindow.cs|" + offset.ToString(CultureInfo.InvariantCulture) + "|code fact")))[..12],
            "FamilyQuant/SpectrumComparisonWindow.cs", 1, "code fact", literal); }
        var header = Row("\"Scan\""); var tip = Row("\"Scan number.\"");
        var before = new Dictionary<string, string> { ["columns/ScanNumber/header"] = "Scan", ["columns/ScanNumber/tooltip"] = "Scan number." };
        var actual = new Dictionary<string, string>(before) { ["columns/ScanNumber/tooltip"] = "Intentional revised copy." };
        TextWriter original = Console.Out; using var log = new StringWriter(CultureInfo.InvariantCulture);
        List<AssayRuntimeCase> cases;
        try { Console.SetOut(log); cases = SelectSpectrumPropertyCases(new[] { header, tip }, source, before, new(actual), false); }
        finally { Console.SetOut(original); }
        Check("one changed tooltip leaves the unchanged header eligible", cases.Count == 1 && cases[0].Id == header.Id && cases[0].Passed);
        Check("a row-level difference names its frozen ID, property and current value", log.ToString().Contains(tip.Id, StringComparison.Ordinal)
            && log.ToString().Contains("columns/ScanNumber/tooltip", StringComparison.Ordinal) && log.ToString().Contains("Intentional revised copy.", StringComparison.Ordinal));
        var negative = SelectSpectrumPropertyCases(new[] { header, tip }, source, before, new(actual), true);
        Check("negative control still fails every otherwise eligible row without closing the changed tooltip",
            negative.Count == 1 && negative[0].Id == header.Id && !negative[0].Passed);
        var window = new System.Windows.Window();
        AssayViewSnapshot.ConfigureOffscreen(window);
        Check("native proof windows are configured off screen without activation or taskbar entry",
            window.Left == -30000 && window.Top == -30000 && !window.ShowActivated && !window.ShowInTaskbar
            && window.WindowStartupLocation == System.Windows.WindowStartupLocation.Manual && !window.IsVisible && !window.IsActive);
        window.Close();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
