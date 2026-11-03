using FamilyQuant.Ai;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiJ45ParityCommands() =>
    [new("--test-ai-j45-parity", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-j45-parity", (_, _) =>
        {
            AiJ45ParityTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static (FamilyClusterFile Project, QuantManifest Manifest) AiJ45ProjectFixture()
    {
        var project = new FamilyClusterFile { ReportDetails = new ReportDetails { Title = "Study title", Workflow = "User procedure" },
            MyExtendedFastaItems = new() { new() { SequenceIdentifier = "fixture-protein", Sequence = "ACDEFGHIK", UserID = 1, USearchID = 1 } } };
        var shares = new FamilyRunSharesResult { MembershipSha256 = FamilyStudyMembership.Fingerprint(project),
            Families = new() { new() { FamilyUserId = 1, MeanShare = .125, AvailableRunCount = 1 } },
            Runs = new() { new() { Run = "run-one", AssignedSignal = 100, MeasuredSignal = 200 } } };
        return (project, new QuantManifest { FamilyRunShares = shares });
    }

    private static void AiJ45ParityTests()
    {
        var data = InterpretationInputFixture();
        var capture = ReportInterpretationInput.Capture(data);
        Check("J46d the captured Markdown is byte-identical to J45", Encoding.UTF8.GetBytes(capture.Text)
            .SequenceEqual(Encoding.UTF8.GetBytes(ReportInterpretationInput.Build(data))));
        Check("J46d cells retain their native column headings", capture.Cells.Contains(("Stored value", "10")));
        Check("J46d raw cells preserve embedded separators and line breaks", capture.Cells.Contains(("Text", "first | second\nthird")));
        Check("J46d number tracing uses the captured cells", InterpretationNumberTrace.Unmatched("Recorded 10 and invented 987654.321", capture.Cells)
            .SequenceEqual(new[] { "987654.321" }));
        try
        {
            var (project, manifest) = AiJ45ProjectFixture();
            string expected = ReportInterpretationInput.FromProject(project, manifest, AppVersionInfo.Current());
            var actual = AiInterpretationInputBuilder.BuildProject(project, manifest);
            Check("J46d AI input is J45's exported Markdown byte for byte", Encoding.UTF8.GetBytes(actual.Text).SequenceEqual(Encoding.UTF8.GetBytes(expected)));
        }
        catch { Check("J46d AI input is J45's exported Markdown byte for byte", false); }
    }
}
