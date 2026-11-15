using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlRingRemainderCommands() =>
    [new("--test-html-ring-remainder", 10731, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-ring-remainder", (args, _) =>
        {
            HtmlRingRemainderTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-ring-remainder"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlRingRemainderTests(string output)
    {
        Directory.CreateDirectory(output);
        var fixture = HtmlSharePartsFixture();
        var manifest = fixture.Context.StoredStudies!;
        HtmlReportPayload Build() => HtmlReportPayloadBuilder.Build(fixture.Document, fixture.Sheets, context: fixture.Context);
        double Value(HtmlReportPayload payload, string? id) => id == null ? double.NaN : payload.Fields[id].Value!.Value;
        bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1e-10;
        string before = JsonSerializer.Serialize(manifest);
        var payload = Build();
        var tic = payload.Composition.RingBases["tic"];
        Check("WD1 native TIC keeps shown and outside shares", Near(Value(payload, tic.Total), 7) && Near(Value(payload, tic.Outside), 93));
        Check("WD1 TIC remainder is mean per-run peptide outside plus unexplained base",
            Near(Value(payload, tic.OutsidePeptide), 8) && Near(Value(payload, tic.OutsideRest), 85));
        Check("WD1 split parts sum to the original TIC outside", Near(Value(payload, tic.OutsidePeptide) + Value(payload, tic.OutsideRest), Value(payload, tic.Outside)));
        Check("WD1 peptide-only bases keep a single outside", new[] { "identifiedOnly", "identifiedAndDeNovo" }.All(key =>
            payload.Composition.RingBases[key] is { OutsidePeptide: null, OutsideRest: null, Outside: not null }));
        var detector = payload.Composition.RingBases["detectorEnvelopes"];
        Check("WD1 independent measured base smaller than peptide signal keeps its original outside",
            Near(Value(payload, detector.Total), 76.25) && Near(Value(payload, detector.Outside), 23.75)
            && detector.OutsidePeptide == null && detector.OutsideRest == null);
        Check("WD1 unsplit trace records the desk reason", payload.Fields[detector.Outside!].Source.ParagraphText == "not split: PSM and de novo exceed this base");
        Check("WD1 projection preserves stored inputs", before == JsonSerializer.Serialize(manifest));
        foreach (var run in manifest.DerivedFractions) run.DetectedPeptideLikeArea *= 3;
        var positive = Build();
        detector = positive.Composition.RingBases["detectorEnvelopes"];
        Check("WD1 positive measured base prepares both nonnegative components", Near(Value(positive, detector.OutsidePeptide), 28.75)
            && Near(Value(positive, detector.OutsideRest), 100d * 11 / 24)
            && Near(Value(positive, detector.OutsidePeptide) + Value(positive, detector.OutsideRest), Value(positive, detector.Outside)));
        Check("WD1 new component fields keep stored study and statistic provenance", positive.Fields[detector.OutsidePeptide!].Source
            is { Kind: "family-ring", Study: "source", Property: "DetectorEnvelopes/peptide-outside", Scale: 100 }
            && positive.Fields[detector.OutsideRest!].Source.Property == "DetectorEnvelopes/rest-outside");
        Check("WD1 TIC keys and values stay stable when another base becomes splittable", tic.Total == positive.Composition.RingBases["tic"].Total
            && tic.Outside == positive.Composition.RingBases["tic"].Outside
            && Value(payload, tic.Total) == Value(positive, tic.Total) && Value(payload, tic.Outside) == Value(positive, tic.Outside));
        string word = Path.Combine(output, "ring.docx"), workbook = ReportSupplementWorkbook.PathForReport(word);
        WordReportWriter.Write(word, fixture.Document);
        ReportSupplementWorkbook.Write(workbook, fixture.Document, fixture.Sheets);
        Check("WD1 split components trace to native totals", ReportNumberTrace.RunHtmlBindings(positive, word, workbook, new(), manifest,
            Path.Combine(output, "trace.csv")) == 0);
        var fields = positive.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        fields[detector.OutsidePeptide!] = fields[detector.OutsidePeptide!] with { Value = 99 };
        Check("WD1 trace refuses a substituted split component", ReportNumberTrace.RunHtmlBindings(positive with { Fields = fields }, word, workbook,
            new(), manifest, Path.Combine(output, "forged-trace.csv")) == 1);
        var dangling = detector with { OutsideRest = "missing-ring-rest" };
        var bases = positive.Composition.RingBases.ToDictionary(pair => pair.Key, pair => pair.Value);
        bases["detectorEnvelopes"] = dangling;
        Check("WD1 trace checks the new field references", ReportNumberTrace.RunHtmlBindings(positive with
            { Composition = positive.Composition with { RingBases = bases } }, word, workbook, new(), manifest, Path.Combine(output, "dangling-trace.csv")) == 1);
        manifest.DerivedFractions.RemoveAt(0);
        Check("WD1 missing measured denominator never fabricates split parts", Build().Composition.RingBases["detectorEnvelopes"] is
            { OutsidePeptide: null, OutsideRest: null });
    }
}
