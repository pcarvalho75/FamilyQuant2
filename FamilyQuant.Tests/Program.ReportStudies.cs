using FamilyQuant.DeNovo;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ReportStudyBoundaryTests()
    {
        Section("Report studies - only harness-only operations leave Core");
        Check("X9 commands resolve through their feature provider", TestCommandRegistry.Resolve(
            new[] { "--report-study-snapshot", "fixture-output" }, TestCommandPhase.Raw)?.Run.Method.Name == nameof(RunReportStudySnapshot)
            && TestCommandRegistry.Resolve(new[] { "--test-report-study-boundary" }, TestCommandPhase.Raw)?.Run.Method.Name == nameof(RunReportStudyBoundary));
        var harness = typeof(Program).Assembly;
        var core = typeof(FamilyClusterFile).Assembly;
        Check("score-scatter study operations belong to the report harness", typeof(DeNovoScoreScatterStudy).Assembly == harness
            && core.GetType(typeof(DeNovoScoreScatterStudy).FullName!) == null);
        Check("set-novelty study operations belong to the report harness", typeof(SetNoveltyStudy).Assembly == harness
            && core.GetType(typeof(SetNoveltyStudy).FullName!) == null);
        Check("chart model types stay in Core", typeof(DeNovoScoreScatter).Assembly == core && typeof(ScoreScatterPoint).Assembly == core
            && typeof(SetNovelty).Assembly == core && typeof(SetNoveltyRegion).Assembly == core);
        Check("screen-called studies and the B1 counterpart dependency stay in Core", new[]
        {
            typeof(DeNovoAgreementStudy), typeof(DeNovoSourceVennStudy), typeof(IdentificationConcordanceStudy),
            typeof(DeNovoAcceptedScoreScatterStudy), typeof(DeNovoOverlapStudy), typeof(ReferenceEnvelopeEstimator),
            typeof(PsmDeNovoCounterpartStudy)
        }.All(t => t.Assembly == core));
        Check("screen evidence pack and presentation figures remain in App", typeof(PresentationEvidencePack).Assembly == typeof(App).Assembly
            && typeof(PresentationFigures).Assembly == typeof(App).Assembly);
    }

    private static int ReportStudySnapshot(string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Choose a new or empty fixture folder.");
        Directory.CreateDirectory(output);
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            PeaksDeNovoRow Row(string sequence, double confidence) => new() { Peptide = sequence, Alc = confidence };
            var peaks = new[] { Row("ACDEFGHIK", 95), Row("PEPTIDEK", 82), Row("MNPQRSTV", 73), Row("PEPTIDEK", 85) };
            var deep = new[] { Row("ACDEFGHLK", 88), Row("PEPTLDEK", 67), Row("YYYYYYYY", 90) };
            var scatter = DeNovoScoreScatterStudy.Build(peaks, DeNovoSource.DeNovoAll, deep, DeNovoSource.DeepNovo);
            var novelty = SetNoveltyStudy.Build(peaks.Take(2), peaks, deep);
            void Write(string name, string content) => File.WriteAllText(Path.Combine(output, name), content, new UTF8Encoding(false));
            Write("score-scatter.csv", DeNovoScoreScatterStudy.BuildSharedCsv(scatter));
            Write("score-scatter.md", DeNovoScoreScatterStudy.BuildSummaryMarkdown(scatter));
            Write("set-novelty.csv", SetNoveltyStudy.BuildRegionsCsv(novelty));
            Write("set-novelty.md", SetNoveltyStudy.BuildSummaryMarkdown(novelty));
            PresentationFigures.SaveScoreScatter(scatter, Path.Combine(output, "figures"));
            PresentationFigures.SaveSetNovelty(novelty, Path.Combine(output, "figures"));
            Console.WriteLine("Synthetic study fixtures: " + Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Count()
                .ToString(CultureInfo.InvariantCulture) + " native files; no scientific deliverable.");
            return 0;
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }
}
