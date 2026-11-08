using System;
using System.IO;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int RunResultsExportFixture(string output, bool timeoutProbe = false)
    {
        Directory.CreateDirectory(output);
        if (NativeProofDesktop.IsPrivate) return ResultsExportFixtureWorker(output, timeoutProbe);
        string[] arguments = timeoutProbe
            ? ["--test-results-export-facts", output, "--timeout-probe"]
            : ["--test-results-export-facts", output];
        try
        {
            return NativeProofDesktop.Run(arguments, Path.Combine(output, "native-results-export.log"),
                timeoutProbe ? 2_000u : 120_000u);
        }
        catch (TimeoutException error)
        {
            // NativeProofDesktop has terminated and joined its exact owned child before throwing.
            Console.WriteLine(error.Message);
            return 124;
        }
    }

    private static int ResultsExportFixtureCommand(string[] args)
    {
        string output = Path.GetFullPath(args[1]);
        bool timeoutProbe = args.Length == 3;
        if (NativeProofDesktop.IsPrivate) return RunResultsExportFixture(output, timeoutProbe);
        Check("native Results export fixture propagates its worker exit status",
            RunResultsExportFixture(output, timeoutProbe) == 0);
        if (timeoutProbe) Check("the next fixture check runs after the owned Results worker times out", true);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int ResultsExportFixtureWorker(string output, bool timeoutProbe)
    {
        using var log = new StreamWriter(Path.Combine(output, "native-results-export.log")) { AutoFlush = true };
        var previousOut = Console.Out;
        var previousError = Console.Error;
        Console.SetOut(log); Console.SetError(log);
        try
        {
            if (timeoutProbe) Thread.Sleep(Timeout.Infinite);
            ResultsExportFactTests(output);
        }
        catch (Exception error)
        {
            Check("native Results export fixture finishes within its operation deadline", false, error.ToString());
        }
        finally
        {
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            Console.SetOut(previousOut); Console.SetError(previousError);
        }
        return failed == 0 ? 0 : 1;
    }
}
