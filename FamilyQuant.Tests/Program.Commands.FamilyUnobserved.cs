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
    private static IEnumerable<TestCommand> FamilyUnobservedInspectionCommands() =>
    [
        new("--inspect-family-unobserved", 441, TestCommandPhase.Raw,
            args => args.Length == 4 && args[0] == "--inspect-family-unobserved", (args, _) =>
            {
                // Storage-only: never invoke SearchCuration, app opening, extraction or figure rendering.
                string project = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
                int id = int.Parse(args[2], CultureInfo.InvariantCulture);
                Directory.CreateDirectory(output);
                string before = FileFingerprint.Sha256(project);
                var (file, quant, manifest) = ProjectStore.ReadFqproj(project);
                var shares = manifest.FamilyRunShares ?? throw new InvalidDataException("The saved project has no run-share study.");
                var counts = manifest.FamilyProteinCounts ?? throw new InvalidDataException("The saved project has no protein-count study.");
                var oldShares = shares.Families.ToList();
                var oldCounts = counts.Families.ToList();
                var shareIds = oldShares.Select(f => f.FamilyUserId).ToHashSet();
                var countIds = oldCounts.Select(f => f.FamilyUserId).ToHashSet();
                string shareJson = JsonSerializer.Serialize(oldShares), countJson = JsonSerializer.Serialize(oldCounts);
                var labels = FamilyNaming.Names(file.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)));
                string shareCsv = ReportStudyExports.MeanShares(new() { Families = oldShares }, labels);
                string countCsv = ReportStudyExports.ProteinCounts(new() { Families = oldCounts }, labels);
                FamilyUnobservedStudies.Store(file, manifest);
                bool oldRowsUnchanged = shareJson == JsonSerializer.Serialize(shares.Families.Where(f => shareIds.Contains(f.FamilyUserId)).ToList())
                    && countJson == JsonSerializer.Serialize(counts.Families.Where(f => countIds.Contains(f.FamilyUserId)).ToList());
                bool oldCsvUnchanged = shareCsv == ReportStudyExports.MeanShares(new()
                    { Families = shares.Families.Where(f => shareIds.Contains(f.FamilyUserId)).ToList() }, labels)
                    && countCsv == ReportStudyExports.ProteinCounts(new()
                    { Families = counts.Families.Where(f => countIds.Contains(f.FamilyUserId)).ToList() }, labels);
                var share = shares.Families.SingleOrDefault(f => f.FamilyUserId == id);
                var count = counts.Families.SingleOrDefault(f => f.FamilyUserId == id);
                var sections = ReportPanelSections.BuildFromResults(file, manifest);
                bool unchanged = before == FileFingerprint.Sha256(project);
                File.WriteAllText(Path.Combine(output, "observations.json"), JsonSerializer.Serialize(new
                {
                    ProjectSha256 = before, ProjectUnchanged = unchanged, UserId = id, QuantRows = quant.Count,
                    StoredShareRowBefore = shareIds.Contains(id), StoredCountRowBefore = countIds.Contains(id),
                    OldShareRows = oldShares.Count, AddedShareRows = shares.Families.Count - oldShares.Count,
                    OldCountRows = oldCounts.Count, AddedCountRows = counts.Families.Count - oldCounts.Count,
                    ExistingRowsUnchanged = oldRowsUnchanged, ExistingCsvBytesUnchanged = oldCsvUnchanged, Share = share, Count = count,
                }, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(output, "panel-sections.json"), JsonSerializer.Serialize(sections, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(output, "family_run_means.csv"), ReportStudyExports.MeanShares(shares, labels));
                File.WriteAllText(Path.Combine(output, "family_protein_counts.csv"), ReportStudyExports.ProteinCounts(counts, labels));
                Check("stored project remains byte-identical after native no-peptide inspection", unchanged);
                Check("all existing native study rows remain literal", oldRowsUnchanged);
                Check("all existing native share and count CSV bytes remain identical", oldCsvUnchanged);
                Check("requested no-peptide family has explicit zero shares and counts", share is { NoPeptideObserved: true, MeanShare: 0 }
                    && count is { ObservedPeptideCount: 0, GreedyProteinCount: 0, UniquePeptideProteinCount: 0 });
                Check("native panel report prints the no-peptide status for the requested family", sections.SelectMany(s => s.Blocks).OfType<ReportTable>()
                    .Any(t => t.Title == "Family observation status" && t.Rows.Any(r => r.SequenceEqual(new[] { id.ToString(CultureInfo.InvariantCulture), FamilyUnobservedStudies.Status }))));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];
}
