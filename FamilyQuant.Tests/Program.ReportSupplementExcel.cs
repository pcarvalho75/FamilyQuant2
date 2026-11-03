using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportSupplementExcelCommandTable() =>
    [new("--test-report-supplement-excel", 433, TestCommandPhase.Raw,
        args => args.Length == 2 && args[0] == "--test-report-supplement-excel", (args, _) => ReportSupplementExcel(args[1]))];

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint ReportExcelProcess(nint window, out uint process);

    private static void ReleaseReportExcel(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    private static int ReportSupplementExcel(string output)
    {
        var type = Type.GetTypeFromProgID("Excel.Application");
        Check("native Excel is available for supplement validation", type != null);
        if (type == null) return 1;
        var fixture = SupplementFixture();
        var hashes = new Dictionary<string, string>();
        object? applicationObject = null, booksObject = null;
        uint processId = 0;
        bool ownsApplication = false;
        try
        {
            applicationObject = Activator.CreateInstance(type)!;
            dynamic application = applicationObject;
            application.Visible = false; application.DisplayAlerts = false; application.AutomationSecurity = 3;
            booksObject = (object)application.Workbooks;
            dynamic books = booksObject;
            if ((int)books.Count != 0) throw new InvalidOperationException("The Excel validation instance is not empty; existing workbooks were left alone.");
            ownsApplication = true;
            ReportExcelProcess((nint)(int)application.Hwnd, out processId);
            Console.WriteLine("Owned hidden Excel validation process: " + processId.ToString(CultureInfo.InvariantCulture));
            foreach (string culture in new[] { "pt-BR", "en-US" })
            {
                string path = Path.GetFullPath(Path.Combine(output, "synthetic_supplement-" + culture + ".xlsx"));
                hashes[path] = FileFingerprint.Sha256(path);
                object? bookObject = null, sheetsObject = null;
                try
                {
                    bookObject = (object)books.Open(Filename: path, UpdateLinks: 0, ReadOnly: true,
                        IgnoreReadOnlyRecommended: true, AddToMru: false, CorruptLoad: 0);
                    dynamic book = bookObject;
                    Check("Excel opens " + culture + " workbook normally and read-only", (bool)book.ReadOnly && (int)book.FileFormat == 51);
                    sheetsObject = (object)book.Worksheets;
                    dynamic sheets = sheetsObject;
                    Check("Excel sees every expected supplement sheet under " + culture, (int)sheets.Count == fixture.Sheets.Length + 1);
                    foreach (var expectedSheet in fixture.Sheets)
                    {
                        object? sheetObject = null, rangeObject = null;
                        try
                        {
                            sheetObject = (object)sheets.Item[expectedSheet.Name];
                            dynamic sheet = sheetObject;
                            rangeObject = (object)sheet.UsedRange;
                            dynamic range = rangeObject;
                            object? raw = range.Value2;
                            var matrix = raw as object[,];
                            var expected = new[] { expectedSheet.Table.Columns }.Concat(expectedSheet.Table.Rows).ToArray();
                            bool same = matrix != null && matrix.GetLength(0) == expected.Length && matrix.GetLength(1) == expectedSheet.Table.Columns.Count;
                            if (same)
                                for (int row = 0; row < expected.Length; row++)
                                    for (int column = 0; column < expected[row].Count; column++)
                                        same &= string.Equals(Convert.ToString(matrix!.GetValue(row + matrix.GetLowerBound(0), column + matrix.GetLowerBound(1)), CultureInfo.InvariantCulture) ?? "",
                                            expected[row][column], StringComparison.Ordinal);
                            Check("Excel cell values equal the native CSV: " + expectedSheet.Name + " / " + culture, same);
                            Check("Excel treats source formula-looking text as literal: " + expectedSheet.Name + " / " + culture, range.HasFormula is bool hasFormula && !hasFormula);
                        }
                        finally { ReleaseReportExcel(rangeObject); ReleaseReportExcel(sheetObject); }
                    }
                }
                finally
                {
                    if (bookObject != null) ((dynamic)bookObject).Close(false);
                    ReleaseReportExcel(sheetsObject); ReleaseReportExcel(bookObject);
                }
            }
        }
        finally
        {
            if (applicationObject != null && ownsApplication) ((dynamic)applicationObject).Quit();
            ReleaseReportExcel(booksObject); ReleaseReportExcel(applicationObject);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); GC.WaitForPendingFinalizers();
            if (ownsApplication && processId != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    bool stopped = process.WaitForExit(20000);
                    Check("the owned Excel validation process exits", stopped);
                    if (!stopped) { process.Kill(); process.WaitForExit(); }
                }
                catch (ArgumentException) { Check("the owned Excel validation process exits", true); }
            }
        }
        Check("native Excel validation leaves both generated workbook bytes unchanged", hashes.Count == 2 && hashes.All(pair => FileFingerprint.Sha256(pair.Key) == pair.Value));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
