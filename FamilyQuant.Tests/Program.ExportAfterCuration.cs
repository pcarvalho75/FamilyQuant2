using FamilyQuant.Evidence;
using FamilyQuant.Panels;
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
    private static IEnumerable<TestCommand> ExportAfterCurationCommands() =>
    [
        new("--test-export-after-curation", 74, TestCommandPhase.Raw, a => a.Contains("--test-export-after-curation", StringComparer.Ordinal), (_, _) =>
        {
            ExportAfterCurationTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "export-after-curation"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    /// <summary>EX1 and EX2: after Apply grouping, a protein moved with Edit selected proteins, a typed User ID, Group by
    /// keywords or Recluster by words still lets Quantify and the report (Word, workbook, HTML) run; Undo restores both
    /// the membership and its facts; a changed panel or database still refuses.</summary>
    private static void ExportAfterCurationTests(string output)
    {
        Directory.CreateDirectory(output);
        static FastaItemExtended Protein(string id, string name, string source, bool words, string sequence, int userId, params string[] domains) => new()
        {
            SequenceIdentifier = id, Description = name + " OS=Synthetic", Sequence = sequence, UserID = userId, USearchID = userId,
            Evidence = new() { Accession = NearestReviewed.EntryAccession(id), Family = source, Reviewed = id.StartsWith("sp|", StringComparison.Ordinal),
                Keywords = words ? new() { "selected" } : new() { "other" }, Pfam = domains.Select(d => new EvidenceDomain { Accession = d }).ToList() },
        };
        FamilyClusterFile Grouped()
        {
            var file = new FamilyClusterFile
            {
                FamilyPanel = new() { Families = new()
                {
                    new() { Key = "main", DisplayName = "Main group", Pfam = new() { new() { Accession = "domain-a" } } },
                    new() { Key = "other", DisplayName = "Other group", Pfam = new() { new() { Accession = "domain-b" } } },
                } },
                MyExtendedFastaItems = new()
                {
                    Protein("sp|P54001|SYNTHETIC", "Main enzyme one", "Source main family", true, "AAAAAAAKCCCCCCCKGGGGGGGK", 1, "domain-a"),
                    Protein("sp|P54002|SYNTHETIC", "Main enzyme two", "Source main family", true, "AAAAAAAKDDDDDDDKHHHHHHHK", 2, "domain-a"),
                    Protein("sp|P54003|SYNTHETIC", "Other binder", "Source other family", true, "EEEEEEEKFFFFFFFK", 3, "domain-b"),
                    Protein("tr|P54004|SYNTHETIC", "Loose kinase alpha", "", false, "WWWWWWWKYYYYYYYK", 4),
                    Protein("tr|P54005|SYNTHETIC", "Loose kinase beta", "", false, "MMMMMMMKNNNNNNNK", 5),
                },
            };
            var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
            var proposal = PanelEvidenceClassifier.Propose(file, options);
            EvidenceApply.Apply(file, proposal, new EvidenceApplySelection { SourceFingerprint = proposal.SourceFingerprint,
                SelectedProteinIndices = proposal.Proteins.Where(p => p.SuggestedSelected).Select(p => p.ProteinIndex).ToArray(),
                FamilyPanel = FamilyPanelValidation.Clone(file.FamilyPanel!) }, "fixture");
            return file;
        }
        var rows = new List<QuantRow>
        {
            Qr("identified", "run_a", 1, 2, "AAAAAAAK", 10, 10), Qr("identified", "run_a", 2, 2, "CCCCCCCK", 10, 10),
            Qr("identified", "run_a", 3, 2, "EEEEEEEK", 20, 20), Qr("identified", "run_a", 4, 2, "WWWWWWWK", 5, 5),
            Qr("identified", "run_b", 5, 2, "AAAAAAAK", 20, 20), Qr("identified", "run_b", 6, 2, "DDDDDDDK", 20, 20),
            Qr("identified", "run_b", 7, 2, "EEEEEEEK", 40, 40), Qr("identified", "run_b", 8, 2, "MMMMMMMK", 5, 5),
        };
        int exports = 0;
        // Quantify's family studies, then Export report: the Word report, its workbook and the HTML page in one commit.
        string QuantifyAndExport(FamilyClusterFile file)
        {
            try
            {
                var hits = FamilyEnvelopeQuant.SearchCuration(rows, file);
                var assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), FamilyEnvelopeQuant.AttributeHits(hits, file));
                var manifest = new QuantManifest { PerRunTic = new() { ["run_a.raw"] = 400, ["run_b.raw"] = 800 } };
                FamilyCompositionStudy.StoreRunShares(manifest, assigned);
                FamilyCompositionStudy.StoreProteinCounts(manifest, assigned, file.MyExtendedFastaItems);
                FamilyCompositionStudy.StoreSubclassesFromCuration(manifest, assigned, hits, file);
                FamilyCompositionStudy.StorePositionsFromCuration(manifest, assigned, hits, file);
                FamilyUnobservedStudies.Store(file, manifest);
                EffectivePanelFamilyStudy.StoreIfNeeded(manifest, assigned, hits, file);
                string folder = Path.Combine(output, "export-" + (++exports).ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
                var export = ReportProjectExporter.Build(rows, manifest, file, Path.Combine(folder, "figures"),
                    familyResults: FamilyEnvelopeQuant.BuildFromAttributedPeptides(assigned));
                string word = Path.Combine(folder, "report.docx");
                ReportProjectExporter.Write(export, word);
                return File.Exists(word) && File.Exists(ReportSupplementWorkbook.PathForReport(word)) && File.Exists(HtmlReportWriter.PathForReport(word))
                    ? "" : "The report files were not all written.";
            }
            catch (Exception error) { return error.Message; }
        }
        string Audit(FamilyClusterFile file, int index) => PanelAuditCsv.BuildCsv(file).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseCsvLine).ToArray()[index + 1] is var cells ? cells[2] + " | " + cells[5] : "";
        string Facts(FamilyClusterFile file) => JsonSerializer.Serialize(new { file.PanelProteinAssignments, file.PanelProteinAssignmentsFingerprint,
            Recipe = file.EvidenceRecipe?.PanelProteinAssignments, RecipeFingerprint = file.EvidenceRecipe?.PanelProteinAssignmentsFingerprint });

        var baseline = Grouped();
        int main = baseline.MyExtendedFastaItems[0].UserID;
        Check("EX1 the grouped synthetic project exports before any curation", QuantifyAndExport(baseline) == ""
            && baseline.MyExtendedFastaItems[1].UserID == main && PanelAssignmentEvidenceStore.Current(baseline) != null);

        var edited = Grouped(); var model = new CurationViewModel(edited); string before = Facts(edited);
        var (changed, _) = model.ApplyBatch(new[] { edited.MyExtendedFastaItems[3] }, main.ToString(System.Globalization.CultureInfo.InvariantCulture), "");
        string message = QuantifyAndExport(edited);
        if (message.Length > 0) Console.WriteLine("  EX1 Edit selected proteins export message: " + message);
        Check("EX1 Edit selected proteins after Apply grouping, then Quantify and Export report, succeeds", changed && message == "");
        Check("EX2 the moved protein joins the row of the family it was moved into, and the audit says Moved by you",
            Audit(edited, 3).StartsWith("Main group | Moved by you", StringComparison.Ordinal));
        model.Undo();
        Check("EX2 Undo restores both the membership and its facts", edited.MyExtendedFastaItems[3].UserID == 4 && Facts(edited) == before
            && PanelAssignmentEvidenceStore.Current(edited) != null);

        var keywords = Grouped(); model = new CurationViewModel(keywords); before = Facts(keywords);
        model.ApplyKeywordReview(new SuperClusterPreview(keywords.MyExtendedFastaItems, null, "kinase", "", KeywordMatch.All, null, keywords.LockedUserIds));
        message = QuantifyAndExport(keywords);
        if (message.Length > 0) Console.WriteLine("  EX1 Group by keywords export message: " + message);
        Check("EX1 Group by keywords after Apply grouping, then Quantify and Export report, succeeds",
            keywords.MyExtendedFastaItems[3].UserID == keywords.MyExtendedFastaItems[4].UserID && message == "");
        Check("EX2 a protein regrouped by words keeps no list row and the audit says Regrouped by words",
            new[] { Audit(keywords, 3), Audit(keywords, 4) }.Count(text => text.StartsWith(" | Regrouped by words", StringComparison.Ordinal)) == 1);
        model.Undo();
        Check("EX2 Undo after Group by keywords restores the facts", Facts(keywords) == before && PanelAssignmentEvidenceStore.Current(keywords) != null);

        var typed = Grouped(); model = new CurationViewModel(typed);
        model.RecordFamilyEdit(typed.MyExtendedFastaItems[4], main); typed.MyExtendedFastaItems[4].UserID = main; model.NotifyDocumentChanged();
        Check("EX2 a User ID typed in the table keeps the facts current", PanelAssignmentEvidenceStore.Current(typed) != null && QuantifyAndExport(typed) == "");

        var mixed = Grouped(); model = new CurationViewModel(mixed);
        model.ApplyBatch(new[] { mixed.MyExtendedFastaItems[2] }, main.ToString(System.Globalization.CultureInfo.InvariantCulture), "");
        var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(mixed);
        Check("EX2 a family holding two list rows is reported without a link and still exports", map.IsCurrent
            && map.ConflictingUserIds.Contains(main) && Audit(mixed, 2).StartsWith(" | Moved by you", StringComparison.Ordinal)
            && QuantifyAndExport(mixed) == "");

        var words = Grouped(); model = new CurationViewModel(words);
        model.ApplyWordGrouping(new List<List<int>> { new() { 4, 5 } }, null, 0.5);
        Check("EX2 Recluster by words keeps the facts current", PanelAssignmentEvidenceStore.Current(words) != null);

        var panel = Grouped(); model = new CurationViewModel(panel);
        model.ApplyBatch(new[] { panel.MyExtendedFastaItems[3] }, main.ToString(System.Globalization.CultureInfo.InvariantCulture), "");
        panel.FamilyPanel!.Families[0].Pfam.Add(new() { Accession = "domain-z" });
        Check("EX2 a changed list still refuses the report", QuantifyAndExport(panel) == PanelAssignmentEvidenceStore.RebuildMessage);
        var database = Grouped(); model = new CurationViewModel(database);
        model.ApplyBatch(new[] { database.MyExtendedFastaItems[3] }, main.ToString(System.Globalization.CultureInfo.InvariantCulture), "");
        database.MyExtendedFastaItems[1].Evidence!.Pfam.Add(new() { Accession = "domain-b" });
        Check("EX2 changed database evidence still refuses the report", QuantifyAndExport(database) == PanelAssignmentEvidenceStore.RebuildMessage);
    }
}
