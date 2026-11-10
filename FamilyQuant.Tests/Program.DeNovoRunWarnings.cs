using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeNovoRunWarningCommands() =>
    [new("--test-denovo-run-warnings", 100, TestCommandPhase.Raw,
        args => args[0] == "--test-denovo-run-warnings", (_, _) =>
        {
            DeNovoRunWarningTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void DeNovoRunWarningTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "denovo-run-warnings", Guid.NewGuid().ToString("N"));
        string raw = Path.Combine(root, "raw"), source = Path.Combine(root, "source");
        Directory.CreateDirectory(raw); Directory.CreateDirectory(source);
        foreach (string run in new[] { "run01", "run02" })
            File.WriteAllText(Path.Combine(raw, run + ".raw"), "Synthetic placeholder, never parsed.");
        File.WriteAllText(Path.Combine(source, "run01.raw.denovoOnly.csv"),
            "Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)\n1,PEPTIDEK,99,8,500,2,1,100,99 99 99 99 99 99 99 99\n");
        string package = Path.Combine(root, "fixture.sepr2");
        CreateShellSeproFixture(Array.Empty<(string, int)>()).Save(package);
        var options = new FullXicOptions();
        var stage = typeof(FullXicComputer).Assembly.GetType("FamilyQuant.FullXicInputStage", true)!;
        var prepare = stage.GetMethod("Prepare", BindingFlags.NonPublic | BindingFlags.Static)!;
        var create = stage.GetMethod("CreateResult", BindingFlags.NonPublic | BindingFlags.Static)!;
        var messages = new List<string>();
        var events = new ComputeProgressCollector();
        var input = prepare.Invoke(null, new object?[] { raw, package, source, options,
            (Action<string>)messages.Add, null, new OperationContext(progress: events) });
        var result = (FullXicComputeResult)create.Invoke(null, new[] { input, options })!;
        Check("T5 two-run source names the RAW run missing de novo calls before extraction",
            result.DeNovoSourceWarnings.Count == 1 && result.DeNovoSourceWarnings[0].Contains("run02", StringComparison.Ordinal)
            && messages.Contains(result.DeNovoSourceWarnings[0])
            && events.Items.Any(e => e.Stage == "denovo-run-warning") && result.Rows.Count == 0);
        result.Manifest.PerRunTic = new() { ["run01"] = 1000, ["run02"] = 2000 };
        result.Manifest.DerivedFractions = new()
        {
            new() { Run = "run01", DeNovoPeptides = 1 }, new() { Run = "run02", DeNovoPeptides = 0 },
        };
        var model = new ComputeXicViewModel("fixture", compute: (_, _, _) => Task.FromResult(result))
        { RawDirectory = raw, SeproPackage = package, DeNovoDirectory = source };
        model.ComputeAsync().GetAwaiter().GetResult();
        Check("T5 completion keeps the named missing-source-run warning visible",
            model.Computed && model.Status.Contains("Warning:", StringComparison.Ordinal)
            && model.Status.Contains("run02", StringComparison.Ordinal) && model.DeNovoRunWarning.Contains("run02", StringComparison.Ordinal));
        Check("T5 Run summary lists credited de novo peptide features per run, including zero",
            model.RunSummary.Contains("run01: 1 de novo peptide features.", StringComparison.Ordinal)
            && model.RunSummary.Contains("run02: 0 de novo peptide features.", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(source, "run02.raw.denovoOnly.csv"),
            "Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)\n1,PEPTIDEK,10,8,500,2,1,100,99 99 99 99 99 99 99 99\n");
        var filteredInput = prepare.Invoke(null, new object?[] { raw, package, source, options, null, null, null });
        var filtered = (FullXicComputeResult)create.Invoke(null, new[] { filteredInput, options })!;
        Check("T5 source presence is distinct from having calls pass the configured filters",
            filtered.DeNovoSourceWarnings.Count == 0);
    }
}
