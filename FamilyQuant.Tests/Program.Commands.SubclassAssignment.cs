using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FamilyQuant.Panels;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SubclassAssignmentCommands() =>
    [
        new("--subclass-leave-one-out", 100, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--subclass-leave-one-out", (args, _) => RunSubclassLeaveOneOut(args)),
    ];

    private static int RunSubclassLeaveOneOut(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Supply a saved project and a new output directory.");
        string project = Path.GetFullPath(args[1]);
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Use a new output directory.");
        string original = TestFileHash.Upper(project);
        var (file, _, _) = ProjectStore.ReadFqproj(project);
        var result = SubclassAssignment.MeasureLeaveOneOutCurrent(file);
        if (TestFileHash.Upper(project) != original) throw new InvalidOperationException("The source project changed during the measurement.");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "subclass-leave-one-out.csv"), SubclassAssignment.BuildLeaveOneOutCsv(result), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        {
            SourceProject = project, SourceSha256 = original, result.PanelFingerprint, result.SourceFingerprint,
            result.Eligible, result.Right, result.Wrong, result.NoPrediction, result.PredictionRate, result.AccuracyAmongPredictions,
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine("Reviewed eligible: " + result.Eligible.ToString(CultureInfo.InvariantCulture)
            + "; right: " + result.Right.ToString(CultureInfo.InvariantCulture)
            + "; wrong: " + result.Wrong.ToString(CultureInfo.InvariantCulture)
            + "; no prediction: " + result.NoPrediction.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Output: " + output);
        return 0;
    }
}
