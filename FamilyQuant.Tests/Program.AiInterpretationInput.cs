using FamilyQuant.Ai;
using FamilyQuant.Report;
using FamilyQuant.Panels;
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
        var details = new ReportDetails { Title = "Study title", SampleDescription = "Sample note", Workflow = "User procedure" };
        var table = new ReportTable("fixture", "Observed numbers", new[] { "family", "share_fraction", "count" },
            new IReadOnlyList<string>[] { new[] { "User group", "0.125", "3" } }, "Measured values");
        var sections = new[] { new ReportSection("methods", "Analysis choices", new ReportBlock[] { new ReportParagraph("p", "User choice"), table }) };
        try
        {
            var value = AiInterpretationInputBuilder.Build(details, sections);
            Check("J46 preview contains the user details, choice and exact table data", new[] { "Study title", "Sample note", "User procedure", "Analysis choices", "User choice", "User group", "0.125", "Measured values" }
                .All(item => value.Text.Contains(item, StringComparison.Ordinal)));
            Check("J46 numeric trace uses the same exact table cells sent in preview", value.Cells.Contains(("share_fraction", "0.125")) && value.Cells.Contains(("count", "3")));
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); string br = AiInterpretationInputBuilder.Build(details, sections).Text;
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Check("J46 preview bytes do not change with current culture", br == AiInterpretationInputBuilder.Build(details, sections).Text);
            }
            finally { CultureInfo.CurrentCulture = previous; }
            var project = new FamilyClusterFile { ReportDetails = details,
                FamilyPanel = new FamilyPanel { Families = new() { new() { Key = "family-a", DisplayName = "User group", TextFields = new() { new() { Name = "Notes", Text = "Panel note" } } } } },
                MyExtendedFastaItems = new() { new() { SequenceIdentifier = "fixture-protein", Sequence = "SENSITIVESEQUENCE", UserID = 1 } } };
            var projectInput = AiInterpretationInputBuilder.BuildProject(project, new QuantManifest());
            Check("J46 project input includes the user's panel and methods without raw sequences", projectInput.Text.Contains("Panel note", StringComparison.Ordinal)
                && projectInput.Text.Contains("User procedure", StringComparison.Ordinal) && !projectInput.Text.Contains("SENSITIVESEQUENCE", StringComparison.Ordinal));
        }
        catch
        {
            Check("J46 preview contains the user details, choice and exact table data", false);
            Check("J46 numeric trace uses the same exact table cells sent in preview", false);
            Check("J46 preview bytes do not change with current culture", false);
            Check("J46 project input includes the user's panel and methods without raw sequences", false);
        }
    }
}
