using FamilyQuant.Evidence;
using FamilyQuant.Homology;
using FamilyQuant.Panels;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelHeadlineProofCommands() =>
    [
        new("--check-panel-headline", 446, TestCommandPhase.Raw,
            args => args.Length >= 3 && args[0] == "--check-panel-headline", (args, _) => PanelHeadlineProof(args)),
        new("--inspect-panel-headline-inputs", 447, TestCommandPhase.Raw,
            args => args.Length == 3 && args[0] == "--inspect-panel-headline-inputs", (args, _) => InspectPanelHeadlineInputs(args)),
    ];

    private static int InspectPanelHeadlineInputs(string[] args)
    {
        Directory.CreateDirectory(args[2]);
        var facts = new List<object>();
        foreach (string path in Directory.EnumerateFiles(args[1], "*.fqproj").Order(StringComparer.Ordinal))
        {
            string hash = FileFingerprint.Sha256(path);
            using var zip = ZipFile.OpenRead(path);
            using var json = JsonDocument.Parse(StoredCurationJson(zip));
            bool panel = json.RootElement.TryGetProperty("FamilyPanel", out var p) && p.ValueKind != JsonValueKind.Null;
            bool applied = json.RootElement.TryGetProperty("PanelProteinAssignments", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0;
            bool current = false; int multiple = 0;
            if (panel && applied)
            {
                var file = FamilyClusterFile.FromJsonText(json.RootElement.GetRawText());
                var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(file);
                current = map.IsCurrent;
                if (current) multiple = map.UserFamilies.Values.Where(f => !f.HasConflict && f.PanelFamilyKey != null)
                    .GroupBy(f => f.PanelFamilyKey, StringComparer.Ordinal).Count(g => g.Count() > 1);
            }
            facts.Add(new { Project = Path.GetFileName(path), Sha256 = hash, Panel = panel, AppliedAssignments = applied, Current = current, MultipleSourceKeys = multiple });
            Console.WriteLine(Path.GetFileName(path) + ": panel=" + panel + ", applied=" + applied + ", current=" + current + ", multiple=" + multiple.ToString(CultureInfo.InvariantCulture));
            if (hash != FileFingerprint.Sha256(path)) throw new InvalidDataException("Panel-case inspection changed its source copy.");
        }
        File.WriteAllText(Path.Combine(args[2], "panel-case-inventory.json"), JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static int PanelHeadlineProof(string[] args)
    {
        string project = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        bool counterfactual = args.Contains("--counterfactual", StringComparer.Ordinal);
        string? cache = args.Length > 3 && args[3] != "--counterfactual" ? Path.GetFullPath(args[3]) : null;
        Directory.CreateDirectory(output);
        string hash = FileFingerprint.Sha256(project);
        var (file, rows, manifest) = ProjectStore.ReadFqproj(project);
        manifest = FamilyUnobservedStudies.View(file, manifest);
        object? split = null;
        if (counterfactual)
        {
            if (cache == null) throw new ArgumentException("A counterfactual proof requires an owned retained-hit cache folder.");
            var options = new HomologyOptions();
            if (manifest.FamilyMapping != null) options.MinIdentity = manifest.FamilyMapping.MinIdentity;
            var hits = CurationHomologyCache.GetOrSearch(rows, file, cache, options,
                progress: hit => { if (!hit) throw new OperationCanceledException("Retained hit cache missed; no search was started."); });
            var map = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(file);
            if (!map.IsCurrent) throw new InvalidDataException(map.Message);
            var choice = map.UserFamilies.Values.Where(f => !f.HasConflict && f.PanelFamilyKey != null
                && f.Decision is FamilyPanelDecision.Quantify or FamilyPanelDecision.ReportUnderOthers)
                .OrderBy(f => f.PanelFamilyKey, StringComparer.Ordinal).ThenBy(f => f.FamilyUserId)
                .Select(f => (Family: f, Members: file.MyExtendedFastaItems.Select((p, i) => (Protein: p, Index: i))
                    .Where(p => p.Protein.UserID == f.FamilyUserId && !SequenceLabels.IsDecoy(p.Protein.SequenceIdentifier))
                    .OrderBy(p => p.Protein.SequenceIdentifier, StringComparer.Ordinal).ToArray())).First(f => f.Members.Length >= 2);
            int newId = checked(file.MyExtendedFastaItems.Max(p => p.UserID) + 1);
            int move = choice.Members.Length / 2;
            foreach (var member in choice.Members.Take(move)) member.Protein.UserID = newId;
            // This is explicitly new counterfactual curation metadata, not a retag of old numerical studies.
            // Protein panel keys/evidence remain inherited; only their recorded working-group IDs change.
            var recipe = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(file.EvidenceRecipe))!;
            foreach (var assignment in file.PanelProteinAssignments!)
            {
                assignment.FamilyUserId = file.MyExtendedFastaItems[assignment.ProteinIndex].UserID;
                assignment.Reason = "Counterfactual manual grouping; inherited protein panel assignment unchanged. " + assignment.Reason;
            }
            recipe.PanelProteinAssignments = JsonSerializer.Deserialize<List<PanelProteinAssignment>>(JsonSerializer.Serialize(file.PanelProteinAssignments))!;
            var fingerprint = typeof(PanelAssignmentEvidenceStore).GetMethod("Fingerprint", BindingFlags.Static | BindingFlags.NonPublic)!;
            string value = (string)fingerprint.Invoke(null, new object?[] { file, file.PanelProteinAssignments, recipe, file.EvidenceSource, null })!;
            file.PanelProteinAssignmentsFingerprint = recipe.PanelProteinAssignmentsFingerprint = value;
            file.EvidenceRecipe = recipe;
            SubclassAssignment.CommitCandidate(file, SubclassAssignment.ComputeCurrent(file));
            manifest.FamilyRunShares = null; manifest.FamilyProteinCounts = null; manifest.FamilySubclassParts = null;
            manifest.FamilyDomainParts = null; manifest.FamilyPeptideLocalization = null; manifest.FamilySensitivity = null;
            manifest.FamilyWorldResult = null; manifest.EffectivePanelFamilies = null;
            var attribution = FamilyEnvelopeQuant.AttributeHits(hits, file);
            DeNovo.SpectrumComparisonTable.RefreshFamilyAssignments(manifest.SpectrumComparisonRows, attribution);
            var assigned = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), attribution);
            FamilyCompositionStudy.StoreRunShares(manifest, assigned);
            FamilyCompositionStudy.StoreProteinCounts(manifest, assigned, file.MyExtendedFastaItems);
            FamilyCompositionStudy.StoreSubclassesFromCuration(manifest, assigned, hits, file);
            FamilyCompositionStudy.StorePositionsFromCuration(manifest, assigned, hits, file);
            FamilyUnobservedStudies.Store(file, manifest);
            EffectivePanelFamilyStudy.Store(manifest, assigned, hits, file);
            file.ReportDetails = (file.ReportDetails ?? new ReportDetails()) with { Title = "Counterfactual grouping proof" };
            split = new { Kind = "Counterfactual, not an observed grouping", PanelKey = choice.Family.PanelFamilyKey,
                OriginalUserId = choice.Family.FamilyUserId, CounterfactualUserId = newId, MovedProteins = move,
                Rule = "First half of ordinal protein identifiers; no signal-based choice or answer fitting" };
            ProjectStore.WriteFqproj(Path.Combine(output, "counterfactual.fqproj"), file, rows, manifest);
        }
        var labels = FamilyNaming.Names(file.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)));
        var oldGrouping = FamilyReportThreshold.Build(manifest.FamilyRunShares!, file);
        string before = ReportStudyExports.Headline(oldGrouping, manifest.FamilyProteinCounts, labels);
        var basis = ReportCompositionBasis.Select(file, manifest, labels);
        string after = ReportStudyExports.Headline(basis.Grouping!, basis.Studies.FamilyProteinCounts, basis.Labels);
        var oldRows = ReportCsvTable.Build("before", "Before", before, "");
        var newRows = ReportCsvTable.Build("after", "After", after, "");
        bool identical = oldRows.Rows.Count == newRows.Rows.Count && oldRows.Rows.Zip(newRows.Rows)
            .All(p => p.First.Where((_, i) => i != 1).SequenceEqual(p.Second.Where((_, i) => i != 1)));
        if (!counterfactual && (!basis.IsIdentity || !identical)) throw new InvalidDataException("The one-to-one headline changed a stored number or row identity.");
        File.WriteAllText(Path.Combine(output, "before-userid.csv"), before);
        File.WriteAllText(Path.Combine(output, "after-panel.csv"), after);
        bool unchanged = hash == FileFingerprint.Sha256(project);
        if (!unchanged) throw new InvalidDataException("The source project copy changed.");
        File.WriteAllText(Path.Combine(output, "proof.json"), JsonSerializer.Serialize(new
        { SourceSha256 = hash, SourceUnchanged = unchanged, Counterfactual = counterfactual, Split = split, basis.IsIdentity,
            BeforeRows = oldRows.Rows.Count, AfterRows = newRows.Rows.Count, AllNonLabelCellsByteIdentical = identical,
            Mode = "Stored studies and verified retained hits only; no RAW extraction, fresh search, or figures" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: source unchanged; identity=" + basis.IsIdentity + "; non-label headline cells identical=" + identical + "; counterfactual=" + counterfactual);
        return 0;
    }
}
