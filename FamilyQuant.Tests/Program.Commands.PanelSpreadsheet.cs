using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelSpreadsheetCommandTable() =>
    [
        new("--panel-spreadsheet-live", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--panel-spreadsheet-live", (args, _) => PanelSpreadsheetLive(args)),
        new("--test-panel-spreadsheet", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-panel-spreadsheet", (_, _) =>
        {
            PanelSpreadsheetTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-spreadsheet"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];
}
