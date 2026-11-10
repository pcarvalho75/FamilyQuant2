using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlCoverageCatalogCommands() =>
    [new("--test-html-coverage-catalog", 10525, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-coverage-catalog", (args, _) =>
        {
            HtmlCoverageCatalogTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-coverage-catalog"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlCoverageCatalogTests(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        var fixture = HtmlSharePartsFixture();
        var project = new FamilyClusterFile();
        var manifest = fixture.Context.StoredStudies!;
        var localization = new FamilyPeptideLocalizationResult { Method = "Synthetic stored placements", Policy = SharedPeptidePolicy.FamilyUnique };
        const string sequence = "ACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWY";
        void Protein(int family, string id, int rank, double signal, bool placed = true, bool annotations = false)
        {
            var item = new FastaItemExtended { UserID = family, USearchID = family, SequenceIdentifier = id, Sequence = sequence,
                Description = "Stored protein " + id, UserAnnotation = "Stored family " + family.ToString(CultureInfo.InvariantCulture) };
            if (annotations) item.Evidence = new ProteinEvidence { Positions = new ProteinPositionEvidence
            {
                SequenceLength = sequence.Length, SequenceSha256 = ProteinCoverageMapData.SequenceHash(sequence),
                Pfam = new() { new() { Name = "Stored region", Accession = "PF99999", Start = 2, End = 10,
                    Fragments = new() { new() { Start = 2, End = 3 }, new() { Start = 8, End = 10 } } } },
            } };
            project.MyExtendedFastaItems.Add(item);
            var row = localization.Families.SingleOrDefault(row => row.FamilyUserId == family);
            if (row == null) { row = new() { FamilyUserId = family }; localization.Families.Add(row); }
            row.Proteins.Add(new() { Rank = rank, ProteinIdentifier = id, ProteinLength = sequence.Length,
                ProteinSequenceSha256 = ProteinCoverageMapData.SequenceHash(sequence), CreditedSignal = signal });
            localization.Segments.Add(new() { FamilyUserId = family, ProteinIdentifier = id, NormalizedSequence = "ACDE",
                Side = "identified", MatchKind = PeptideLocalizationMatchKind.ExactContainment, Signal = signal,
                Intervals = placed ? new() { new() { Start = 1, End = 4 } } : new() });
        }
        Protein(7, "Protein_7_A", 1, 10, annotations: true);
        Protein(7, "Protein_7_B", 2, 30);
        Protein(7, "Protein_7_unplaced", 3, 1, placed: false);
        Protein(8, "Protein_8_zero", 1, 0);
        Protein(9, "Protein_9_A", 1, 5);
        Protein(9, "Protein_9_unplaced", 2, 1, placed: false);
        localization.Segments.Add(new() { FamilyUserId = 7, ProteinIdentifier = "Protein_7_A", NormalizedSequence = "ACDE",
            Side = "denovo", MatchKind = PeptideLocalizationMatchKind.ExactContainment, Intervals = new() { new() { Start = 1, End = 4 } } });
        localization.Segments.Add(new() { FamilyUserId = 7, ProteinIdentifier = "Protein_7_A", NormalizedSequence = "GHIK",
            Side = "denovo", MatchKind = PeptideLocalizationMatchKind.Aligned, Intervals = new() { new() { Start = 5, End = 6 }, new() { Start = 9, End = 10 } } });
        localization.Segments.Add(new() { FamilyUserId = 7, ProteinIdentifier = "Protein_7_A", NormalizedSequence = "LMNP",
            Side = "identified", MatchKind = PeptideLocalizationMatchKind.ExactContainment, Intervals = new() { new() { Start = 11, End = 14 } } });
        manifest.FamilyPeptideLocalization = localization;
        manifest.FamilyRunShares!.MembershipSha256 = localization.MembershipSha256 = FamilyStudyMembership.Fingerprint(project);
        var members = localization.Families.ToDictionary(family => family.FamilyUserId, family => (IReadOnlyList<int>)new[] { family.FamilyUserId });
        var context = fixture.Context with { SourceUserIds = members,
            CoverageCatalogProvider = selection => HtmlCoverageCatalogBuilder.Build(project, manifest, fixture.Context.SourceGrouping, members, selection: selection) };
        var export = new ReportProjectExport(fixture.Document, new Dictionary<string, string>(), Array.Empty<ReportPlacedFigure>())
            { HtmlContext = context, SupplementSheets = fixture.Sheets };
        string word = Path.Combine(output, "coverage.docx");
        string before = JsonSerializer.Serialize(manifest);
        ReportProjectExporter.WriteHtml(export, word);
        var payload = ReadHtmlPayload(HtmlReportWriter.PathForReport(word));
        var catalog = payload.CoverageCatalog!;
        Check("U1 catalog includes every placed protein and excludes unplaced-only proteins", catalog.Proteins.Count == 4
            && catalog.Proteins.All(protein => !protein.Protein.Contains("unplaced", StringComparison.Ordinal)));
        Check("U1 below-threshold families and Others all remain selectable", catalog.Families.Count == 4
            && catalog.Families.Any(family => family.Id == "family:8") && catalog.Families.Any(family => family.Id == "family:9")
            && catalog.Families.Single(family => family.Id == "others").Proteins.Count == 2);
        Check("U1 chooser orders proteins by stored credited signal", catalog.Families.Single(family => family.Id == "family:7").Proteins
            .Select(index => catalog.Proteins[index].Protein).SequenceEqual(new[] { "Protein_7_B", "Protein_7_A" }));
        Check("U1 distinct peptide count pools both evidence sides without counting gapped blocks twice",
            catalog.Proteins.Single(protein => protein.Protein == "Protein_7_A").PeptideCount == 3);
        Check("U1 retains zero-credit proteins with placed peptides", catalog.Proteins.Any(protein => protein.Protein == "Protein_8_zero" && protein.CreditedSignal == 0));
        var native = ProteinCoverageFigures.DataFromStored(localization, project, int.MaxValue);
        Check("U1 geometry and peptide counts equal the native coverage snapshots", catalog.Proteins.All(protein =>
        {
            var map = native.Single(map => map.FamilyUserId == protein.FamilyUserId && map.ProteinId == protein.Protein);
            return protein.Length == map.SequenceLength && protein.CreditedSignal == map.RankingSignal
                && protein.PeptideCount == map.Peptides.Select(peptide => peptide.Label).Distinct(StringComparer.Ordinal).Count()
                && protein.Peptides.Select(span => (span.Start, span.End, span.Label)).SequenceEqual(map.Peptides.SelectMany(peptide => peptide.Segments.Select(span => (span.Start, span.End, peptide.Label))))
                && protein.Domains.Select(span => (span.Start, span.End, span.Label)).SequenceEqual(map.Annotations.SelectMany(annotation => annotation.Segments.Select(span => (span.Start, span.End, annotation.Label))));
        }));
        var plan = ReportStudyFigures.Plan(manifest, project, topProteinsPerPanelFamily: 1, reportGrouping: fixture.Context.SourceGrouping);
        Check("U1 Word still plans one coverage map per report family", plan.Figures.Count(figure => figure.Plot.Chart is ProteinCoverageMapChart) == 2
            && plan.Figures.Where(figure => figure.Plot.Chart is ProteinCoverageMapChart).Select(figure => ((ProteinCoverageMapChart)figure.Plot.Chart).Data.ProteinId)
                .Order(StringComparer.Ordinal).SequenceEqual(new[] { "Protein_7_B", "Protein_9_A" }));
        Check("U1 projection preserves all stored study values", before == JsonSerializer.Serialize(manifest));
        string omittedId = HtmlReportCoverageBuilder.FigureId(7, "Protein_7_A");
        Check("U1 explicit coverage figure exclusions remain effective", HtmlCoverageCatalogBuilder.Build(project, manifest,
            selection: new() { ExcludedIds = new[] { omittedId } })!.Proteins.All(protein => protein.Protein != "Protein_7_A"));
        Check("U1 excluded Results does not leak a coverage catalog", HtmlCoverageCatalogBuilder.Build(project, manifest,
            selection: new() { ExcludedIds = new[] { ReportSectionIds.Results } })!.Proteins.Count == 0);
        WordReportWriter.Write(word, fixture.Document);
        ReportSupplementWorkbook.Write(ReportSupplementWorkbook.PathForReport(word), fixture.Document, fixture.Sheets);
        Check("U1 catalog traces directly to native stored snapshots", ReportNumberTrace.RunHtmlBindings(payload, word,
            ReportSupplementWorkbook.PathForReport(word), project, manifest, Path.Combine(output, "trace.csv")) == 0);
        var corrupted = payload with { CoverageCatalog = catalog with { Proteins = catalog.Proteins.Select((protein, index) => index == 0
            ? protein with { PeptideCount = protein.PeptideCount + 1 } : protein).ToArray() } };
        Check("U1 trace rejects an invented peptide count", ReportNumberTrace.RunHtmlBindings(corrupted, word,
            ReportSupplementWorkbook.PathForReport(word), project, manifest, Path.Combine(output, "trace-negative.csv")) != 0);
        HtmlReportBrowserChecks(Path.Combine(output, "page"), new[] { ("phone", payload, 390), ("desktop", payload, 1366) }, """
            const checks = {}, p = JSON.parse(d.getElementById('fq-data').textContent), catalog = p.coverageCatalog;
            d.getElementById('tab-coverage').click();
            const family = d.getElementById('coverage-family'), protein = d.getElementById('protein-select');
            checks.allFamilies = family.options.length === catalog.families.length;
            family.value = 'family:7'; family.dispatchEvent(new w.Event('change'));
            checks.order = [...protein.options].map(option => option.textContent.split(' ')[0]).join(',') === 'Protein_7_B,Protein_7_A';
            checks.countLabel = protein.options[1].textContent.includes('3 distinct placed sequences');
            checks.countTooltip = protein.title.includes('distinct placed sequences across identified and de novo');
            checks.noAnnotations = !d.querySelector('#coverage-content .legend').textContent.includes('Positioned annotations');
            protein.value = '1'; protein.dispatchEvent(new w.Event('change'));
            const track = d.querySelector('#coverage-content .coverage-track');
            checks.protein = track.dataset.protein === 'Protein_7_A';
            checks.gaps = track.querySelectorAll('.peptide.is-aligned').length === 2
              && ![...track.querySelectorAll('.peptide')].some(span => span.textContent.includes('GHIK') && parseFloat(span.style.width) > 6);
            checks.sides = !!track.querySelector('[data-side="identified"]') && !!track.querySelector('[data-side="de novo"]');
            checks.annotations = track.querySelectorAll('.domain').length === 2 && d.querySelector('#coverage-content .legend').textContent.includes('Positioned annotations');
            const target = track.querySelector('[data-side="de novo"].is-aligned');
            target.focus(); target.click();
            checks.exactPopup = d.getElementById('dialog-body').textContent.includes('de novo, aligned') && d.getElementById('dialog-body').textContent.includes('5-6');
            d.getElementById('dialog-close').click();
            d.querySelector('input[value="tic"]').click();
            const current = p.composition.families.find(f => f.id === '7');
            checks.normalizedStats = !!d.querySelector('#family-stats [data-metric="' + current.tic.mean + '"]');
            checks.selectionKept = protein.value === '1' && d.querySelector('.coverage-track').dataset.protein === 'Protein_7_A';
            family.value = 'family:8'; family.dispatchEvent(new w.Event('change'));
            checks.belowThreshold = d.querySelector('.coverage-track').dataset.protein === 'Protein_8_zero';
            family.value = 'others'; family.dispatchEvent(new w.Event('change'));
            checks.others = protein.options.length === 2;
            checks.noPicture = !d.getElementById('panel-coverage').querySelector('img');
            const headingBounds = d.querySelector('.protein-selector h3').getBoundingClientRect(), choiceBounds = protein.getBoundingClientRect();
            checks.chooserDoesNotOverlapHeading = choiceBounds.top >= headingBounds.bottom || choiceBounds.left >= headingBounds.right;
            checks.noOverflow = d.documentElement.scrollWidth <= w.innerWidth;
            return checks;
            """);
        CaptureHtmlReportScreens(Path.Combine(output, "screens"), payload, "coverage", """
            document.getElementById('coverage-family').value='family:7';
            document.getElementById('coverage-family').dispatchEvent(new Event('change'));
            document.getElementById('protein-select').value='1';
            document.getElementById('protein-select').dispatchEvent(new Event('change'));
            """);
        HtmlCoverageScaleTests(Path.Combine(output, "scale"));
    }

    private static void HtmlCoverageScaleTests(string output)
    {
        Directory.CreateDirectory(output);
        var project = new FamilyClusterFile();
        var localization = new FamilyPeptideLocalizationResult { Method = "Synthetic scale placements" };
        const string sequence = "ACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWY";
        for (int index = 0; index < 5000; index++)
        {
            int id = index % 300 + 1;
            string protein = "Protein_" + index.ToString("D5", CultureInfo.InvariantCulture);
            project.MyExtendedFastaItems.Add(new() { UserID = id, USearchID = id, SequenceIdentifier = protein, Sequence = sequence,
                Description = "Stored protein", UserAnnotation = "Stored family " + id.ToString(CultureInfo.InvariantCulture) });
            var family = localization.Families.SingleOrDefault(family => family.FamilyUserId == id);
            if (family == null) { family = new() { FamilyUserId = id }; localization.Families.Add(family); }
            family.Proteins.Add(new() { ProteinIdentifier = protein, Rank = family.Proteins.Count + 1, ProteinLength = sequence.Length,
                ProteinSequenceSha256 = ProteinCoverageMapData.SequenceHash(sequence), CreditedSignal = 5000 - index });
            for (int peptide = 0; peptide < 8; peptide++)
                localization.Segments.Add(new() { FamilyUserId = id, ProteinIdentifier = protein,
                    NormalizedSequence = sequence.Substring(peptide * 2, 6), Side = peptide % 2 == 0 ? "identified" : "denovo",
                    MatchKind = peptide % 3 == 0 ? PeptideLocalizationMatchKind.Aligned : PeptideLocalizationMatchKind.ExactContainment,
                    Signal = 1, Intervals = peptide % 3 == 0
                        ? new() { new() { Start = peptide * 2 + 1, End = peptide * 2 + 2 }, new() { Start = peptide * 2 + 5, End = peptide * 2 + 6 } }
                        : new() { new() { Start = peptide * 2 + 1, End = peptide * 2 + 6 } } });
        }
        localization.MembershipSha256 = FamilyStudyMembership.Fingerprint(project);
        var manifest = new QuantManifest { FamilyPeptideLocalization = localization };
        var grouping = new FamilyReportThresholdResult { OtherFamilies = Enumerable.Range(16, 285).Select(id => new FamilyRunShareSummary { FamilyUserId = id }).ToList() };
        var catalog = HtmlCoverageCatalogBuilder.Build(project, manifest, grouping)!;
        Check("U1 realistic catalog contains all 5000 proteins and 300 actual families", catalog.Proteins.Count == 5000
            && catalog.Families.Count(family => family.Id.StartsWith("family:", StringComparison.Ordinal)) == 300);
        Check("U1 realistic Others references existing maps without copying them", catalog.Families.Single(family => family.Id == "others").Proteins.Count > 4500
            && catalog.Families.SelectMany(family => family.Proteins).Distinct().Count() == 5000);
        var payload = new HtmlReportPayload { CoverageCatalog = catalog };
        byte[] bytes = HtmlReportWriter.BuildBytes(payload);
        File.WriteAllBytes(Path.Combine(output, "coverage-5000.html"), bytes);
        Console.WriteLine("U1 synthetic scale HTML size: " + bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes; 300 families, 5000 proteins, 40000 placements.");
        HtmlReportBrowserChecks(Path.Combine(output, "page"), new[] { ("scale", payload, 390) }, """
            const checks = {}, p = JSON.parse(d.getElementById('fq-data').textContent), catalog = p.coverageCatalog;
            d.getElementById('tab-coverage').click();
            const family = d.getElementById('coverage-family'), protein = d.getElementById('protein-select'), seen = new Set();
            let all = true;
            for (const group of catalog.families.filter(group => group.id.startsWith('family:'))) {
              family.value = group.id; family.dispatchEvent(new w.Event('change'));
              all = all && protein.options.length === group.proteins.length;
              for (const index of group.proteins) seen.add(index);
              protein.value = String(protein.options.length - 1); protein.dispatchEvent(new w.Event('change'));
              all = all && d.querySelector('.coverage-track').dataset.protein === catalog.proteins[group.proteins.at(-1)].protein;
            }
            checks.everyProteinReachable = all && seen.size === 5000;
            checks.oneMapAtATime = d.querySelectorAll('#coverage-content .coverage-track').length === 1;
            checks.noPicture = !d.getElementById('panel-coverage').querySelector('img');
            checks.offline = !d.querySelector('script[src],link[rel="stylesheet"]');
            return checks;
            """);
    }
}
