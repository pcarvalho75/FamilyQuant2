using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> QuantifyWarningDispatcherCommands() =>
    [new("--test-quantify-warning-dispatcher", 100, TestCommandPhase.Raw,
        args => args[0] == "--test-quantify-warning-dispatcher", (_, _) =>
        {
            QuantifyWarningDispatcherTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void QuantifyWarningDispatcherTests()
    {
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "quantify-warning-dispatcher", Guid.NewGuid().ToString("N"));
        string raw = Path.Combine(root, "raw"), source = Path.Combine(root, "source");
        Directory.CreateDirectory(raw); Directory.CreateDirectory(source);
        foreach (string run in new[] { "run01", "run02" })
            File.WriteAllText(Path.Combine(raw, run + ".raw"), "Synthetic placeholder, never parsed.");
        File.WriteAllText(Path.Combine(source, "run01.raw.denovoOnly.csv"),
            "Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)\n1,PEPTIDEK,99,8,500,2,1,100,99 99 99 99 99 99 99 99\n");
        string package = Path.Combine(root, "fixture.sepr2");
        CreateShellSeproFixture(Array.Empty<(string, int)>()).Save(package);
        var stage = typeof(FullXicComputer).Assembly.GetType("FamilyQuant.FullXicInputStage", true)!;
        var prepare = stage.GetMethod("Prepare", BindingFlags.NonPublic | BindingFlags.Static)!;
        var create = stage.GetMethod("CreateResult", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool workerReported = false, warningTouchedUi = false, detailTouchedUi = false;
        var grid = new Grid(); var child = new Border(); grid.Children.Add(child);
        var model = new ComputeXicViewModel("Synthetic dispatcher fixture", compute: (request, progress, token) => Task.Run(() =>
        {
            workerReported = !grid.Dispatcher.CheckAccess();
            progress.Report("Synthetic worker detail");
            try
            {
                var input = prepare.Invoke(null, new object?[] { raw, package, source, request.Options, null, null, request.Operation });
                return (FullXicComputeResult)create.Invoke(null, new[] { input, request.Options })!;
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }, token)) { RawDirectory = raw, SeproPackage = package, DeNovoDirectory = source };
        model.PropertyChanged += (_, change) =>
        {
            grid.Children.Contains(child);
            if (change.PropertyName == nameof(ComputeXicViewModel.Status) && model.Status == "Synthetic worker detail") detailTouchedUi = true;
            if (change.PropertyName == nameof(ComputeXicViewModel.DeNovoRunWarning) && model.DeNovoRunWarning.Length > 0)
                warningTouchedUi = grid.Children.Contains(child);
        };
        Task work = AppOperationHost.OnUi(grid, model.ComputeAsync);
        FinishBusyTask(work); work.GetAwaiter().GetResult(); PumpDispatcher();
        Check("QX1 warning originates on a worker thread", workerReported);
        Check("QX1 Quantify completes with UI-touching warning and detail handlers: " + model.Status,
            model.Computed && warningTouchedUi && detailTouchedUi);
        Check("QX1 the missing-run warning text stays intact", model.Computed
            && model.DeNovoRunWarning == "No de novo calls in the selected source(s) for RAW run run02.");
    }
}
