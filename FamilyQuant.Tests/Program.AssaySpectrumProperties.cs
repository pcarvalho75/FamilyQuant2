using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void AssaySpectrumPropertyTests()
    {
        const string source = "grid.Columns.Add(Column(\"Scan\", nameof(SpectrumComparisonRow.ScanNumber), 80, \"Scan\" + \" number.\"));";
        FeatureLedger.Observation Row(string literal, int offset) => new("FQ15-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("FamilyQuant/SpectrumComparisonWindow.cs|" + offset.ToString(CultureInfo.InvariantCulture) + "|code fact")))[..12],
            "FamilyQuant/SpectrumComparisonWindow.cs", 1, "code fact", literal);
        var row = Row("\"Scan\"", source.IndexOf("\"Scan\"", StringComparison.Ordinal));
        var before = new Dictionary<string, string> { ["columns/ScanNumber/header"] = "Scan", ["columns/ScanNumber/tooltip"] = "Scan number." };
        var current = new Dictionary<string, string>(before);
        Check("spectrum runtime property comparison uses the frozen column binding and full native header",
            AssaySpectrumPropertyComparison.Compare(row, source, before, current).Passed);
        var tooltip = Row("\"Scan\"", source.LastIndexOf("\"Scan\"", StringComparison.Ordinal));
        var result = AssaySpectrumPropertyComparison.Compare(tooltip, source, before, current);
        Check("spectrum frozen ID separates equal same-line header and tooltip literals", result.Passed
            && result.Key == "columns/ScanNumber/tooltip" && result.Expected == "Scan number.");
        current.Remove("columns/ScanNumber/header"); current["columns/OtherScan/header"] = "Scan";
        Check("a different column cannot lend its matching header to a missing binding",
            !AssaySpectrumPropertyComparison.Compare(row, source, before, current).Passed);
        current = new(before); current["columns/ScanNumber/tooltip"] = "Scan numbers.";
        Check("one changed native tooltip character keeps its observation OPEN",
            !AssaySpectrumPropertyComparison.Compare(tooltip, source, before, current).Passed);
        current = new(before); var changedFrozen = new Dictionary<string, string>(before) { ["columns/ScanNumber/header"] = "Other" };
        Check("the frozen native view must agree with its source property anchor",
            !AssaySpectrumPropertyComparison.Compare(row, source, changedFrozen, current).Passed);
        Check("runtime column properties do not claim event handler behavior",
            !AssaySpectrumPropertyComparison.Compare(row with { Kind = "handler" }, source, before, current).Passed);
    }
}
