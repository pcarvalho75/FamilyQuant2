using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LedgerDiagnosticCommands() =>
    [new("--test-ledger-diagnostics", 21708, TestCommandPhase.Raw,
        args => args is ["--test-ledger-diagnostics"], (_, _) =>
        {
            LedgerDiagnosticTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void LedgerDiagnosticTests()
    {
        Section("Z17a8 capped ledger lists name their full total");
        var errors = Enumerable.Range(1, 7).Select(index => "issue-" + index).ToArray();
        string detail = LedgerDiagnostics.Capped(errors);
        Check("Z17a8 seven errors show five entries and the real total", detail == "showing the first 5 of 7: issue-1; issue-2; issue-3; issue-4; issue-5");
        Check("Z17a8 omitted errors are not printed as if the list were complete", !detail.Contains("issue-6", StringComparison.Ordinal)
            && !detail.Contains("issue-7", StringComparison.Ordinal));
        var console = Console.Out; using var captured = new StringWriter(CultureInfo.InvariantCulture);
        int beforePassed = passed, beforeFailed = failed;
        try { Console.SetOut(captured); ReportFeatureLedgerValidation(errors); }
        finally { Console.SetOut(console); passed = beforePassed; failed = beforeFailed; }
        Check("Z17a8 the actual ledger harness failure message carries the capped total", captured.ToString().Contains(
            "feature ledger scan equals rows and every row names a known check", StringComparison.Ordinal)
            && captured.ToString().Contains("showing the first 5 of 7", StringComparison.Ordinal));
        Check("Z17a8 exactly five errors still report their total", LedgerDiagnostics.Capped(errors.Take(5)).StartsWith("showing the first 5 of 5:", StringComparison.Ordinal));
        Check("Z17a8 fewer than the cap report the actual shown count", LedgerDiagnostics.Capped(new[] { "one" }) == "showing the first 1 of 1: one");
        Check("Z17a8 an empty list reports zero shown of zero", LedgerDiagnostics.Capped(Array.Empty<string>()) == "showing the first 0 of 0: ");
        Check("Z17a8 duplicate issues still count as separate validation results", LedgerDiagnostics.Capped(Enumerable.Repeat("same issue", 9)).StartsWith("showing the first 5 of 9:", StringComparison.Ordinal));
        Check("Z17a8 existing four-entry receipt diagnostics retain their cap", LedgerDiagnostics.Capped(errors, 4).EndsWith("issue-4", StringComparison.Ordinal)
            && LedgerDiagnostics.Capped(errors, 4).StartsWith("showing the first 4 of 7:", StringComparison.Ordinal));
        Check("Z17a8 existing six-entry imports retain their cap", LedgerDiagnostics.Capped(errors, 6).EndsWith("issue-6", StringComparison.Ordinal)
            && LedgerDiagnostics.Capped(errors, 6).StartsWith("showing the first 6 of 7:", StringComparison.Ordinal));
        Check("Z17a8 capped member/screen lists retain their own separator", LedgerDiagnostics.Capped(errors, 2, ", ") == "showing the first 2 of 7: issue-1, issue-2");
        int enumerations = 0;
        IEnumerable<string> Once() { enumerations++; if (enumerations > 1) throw new InvalidOperationException("Enumerated twice."); foreach (var error in errors) yield return error; }
        string once = LedgerDiagnostics.Capped(Once());
        Check("Z17a8 the real total comes from one complete enumeration", enumerations == 1 && once.Contains("first 5 of 7", StringComparison.Ordinal));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Check("Z17a8 total counts use invariant ungrouped integers", LedgerDiagnostics.Capped(Enumerable.Repeat("issue", 1234)).StartsWith("showing the first 5 of 1234:", StringComparison.Ordinal));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Check("Z17a8 a nonpositive diagnostic cap is rejected", Throws<ArgumentOutOfRangeException>(() => LedgerDiagnostics.Capped(errors, 0)));
    }
}
