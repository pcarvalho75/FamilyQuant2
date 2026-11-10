using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> QuantifyIsotopeDefaultCommands() =>
    [new("--test-quantify-isotope-default", 100, TestCommandPhase.Raw,
        args => args[0] == "--test-quantify-isotope-default", (args, _) =>
        {
            QuantifyIsotopeDefaultTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "quantify-isotope-default"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void QuantifyIsotopeDefaultTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var model = new ComputeXicViewModel("fixture", familyCuration: new FamilyClusterFile());
        Check("T6 a new project's Quantify model starts at six isotopes",
            model.IsotopeCeiling == 6 && model.IsotopeCount == 6 && model.MinimumPoints == 3);
        var screen = new QuantifyControl(model);
        var window = OffscreenProofWindow(new Window { Content = screen, Width = 1366, Height = 820 });
        window.Show();
        try
        {
            PumpDispatcher();
            var isotopes = (IntegerUpDown)screen.FindName("IntegerUpDownIsotopes");
            var points = (IntegerUpDown)screen.FindName("IntegerUpDownMinPoints");
            Check("T6 the rendered new-project Quantify control displays ceiling six and minimum points three",
                isotopes.Value == 6 && points.Value == 3 && model.IsotopeCeiling == 6);
            ScreenHarness.Save(window, Path.Combine(output, "new-project-quantify.png"), 1366, 820);
        }
        finally { window.Close(); }
        model.IsotopeCeiling = null;
        Check("T6 clearing the pending ceiling uses the full-envelope default six", model.IsotopeCount == 6);

        var stored = new QuantManifest { IsotopeCount = 3, MinPointsForQuant = 1 };
        string before = JsonSerializer.Serialize(stored);
        var reopened = new ComputeXicViewModel("fixture", stored);
        Check("T6 an older three-isotope manifest does not restore three into the next Quantify request",
            reopened.IsotopeCeiling == 6 && reopened.IsotopeCount == 6 && reopened.MinPointsForQuant == 3
            && before == JsonSerializer.Serialize(stored));
        var host = OffscreenProofWindow(new ComputeXicWindow("fixture", stored));
        host.Show();
        try
        {
            PumpDispatcher();
            Check("T6 the compatibility window also opens at six", host.IsotopeCount == 6
                && ((IntegerUpDown)host.Screen.FindName("IntegerUpDownIsotopes")).Value == 6);
        }
        finally { host.Close(); }

        string package = Path.Combine(output, "fixture.sepr2"); File.WriteAllText(package, "Synthetic input; never parsed.");
        int requested = 0;
        var compute = new ComputeXicViewModel("fixture", compute: (request, _, _) =>
        {
            requested = request.Options.IsotopeCount;
            return Task.FromResult(new FullXicComputeResult());
        }) { RawDirectory = output, SeproPackage = package };
        compute.ComputeAsync().GetAwaiter().GetResult();
        Check("T6 a new project's full-XIC request explicitly passes six to the extraction engine", requested == 6 && compute.Computed);
        Check("T6 the distinct lower-level compatibility default cannot change the Quantify default",
            new DeNovoXicOptions().IsotopeCount == 3 && new FullXicOptions().IsotopeCount == 6
            && HelpWorkflowFacts.Quantify.Contains("default 6", StringComparison.Ordinal));
    }
}
