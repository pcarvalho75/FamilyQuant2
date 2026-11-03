using FamilyQuant.Ai;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiInterpretationInputCommands() =>
    [new("--test-ai-interpretation-input", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-interpretation-input", (_, _) =>
        {
            AiInterpretationInputTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void AiInterpretationInputTests()
    {
        var (project, manifest) = AiJ45ProjectFixture();
        var value = AiInterpretationInputBuilder.BuildProject(project, manifest);
        Check("J46d AI preview contains user details and the J45 stored result table", value.Text.Contains("Study title", StringComparison.Ordinal)
            && value.Text.Contains("User procedure", StringComparison.Ordinal) && value.Text.Contains("12.5", StringComparison.Ordinal));
        Check("J46d AI number check gets J45's exact numeric cells", value.Cells.Any(cell => cell.Column == "Mean share (%)" && cell.Value == "12.5"));
        Check("J46d AI input does not include raw database sequences", !value.Text.Contains("ACDEFGHIK", StringComparison.Ordinal));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); string br = AiInterpretationInputBuilder.BuildProject(project, manifest).Text;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("J46d AI input is culture-invariant J45 output", br == AiInterpretationInputBuilder.BuildProject(project, manifest).Text
                && br == ReportInterpretationInput.FromProject(project, manifest, AppVersionInfo.Current()));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
