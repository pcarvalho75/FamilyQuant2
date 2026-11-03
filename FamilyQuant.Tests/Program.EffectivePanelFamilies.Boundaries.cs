using FamilyQuant.Panels;
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
    private static void EffectivePanelFamilyBoundaries(FamilyClusterFile file, List<QuantRow> rows, CurationHomologyHits hits,
        FamilyPeptideAttribution assigned, QuantManifest manifest, EffectivePanelFamilyResult result, string? output)
    {
        var main = result.Families.Single(f => f.PanelFamilyKey == "main");
        var subclasses = result.SubclassParts!.Families.Single(f => f.FamilyUserId == main.StudyFamilyId);
        Check("pooled subclasses resolve own peptides and retain shared within-key ambiguity", subclasses.FamilySignal == 60
            && subclasses.Parts.Single(p => p.PartKey == "a").Signal == 10 && subclasses.Parts.Single(p => p.PartKey == "b").Signal == 20
            && subclasses.Parts.Single(p => p.IsUnresolved).Signal == 30);
        var products = result.Parts.Where(p => p.PanelFamilyKey == "main" && p.Kind == EffectivePanelPartKind.Product).ToArray();
        Check("native product keys give one availability row per named part", products.GroupBy(p => p.Label).All(g => g.Count() == 1)
            && products.Single(p => p.Label == "Peptide: Shared region").Signal == 30
            && products.Single(p => p.Label == "Peptide: First segment").Signal == 10
            && products.Single(p => p.Label == "Peptide: Second segment").Signal == 20);
        Check("unobserved named subclasses, domains and products are explicitly unmeasured, never guessed zero", new[]
            { "Unseen part", "domain-unseen", "Peptide: Unseen segment" }.All(label => result.Parts.Any(p => p.PanelFamilyKey == "main"
                && p.Label == label && p.State == EffectivePanelPartState.NotMeasured && p.Signal == null && p.ShareOfFamily == null && p.Reason.Length > 0)));
        string native = JsonSerializer.Serialize(result);
        string path = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.EffectivePanel", "stored.fqproj");
        ProjectStore.WriteFqproj(path, file, rows, manifest);
        var reopened = ProjectStore.ReadFqproj(path);
        Check("effective studies, local slot identities, named parts and availability survive project storage", native == JsonSerializer.Serialize(reopened.manifest.EffectivePanelFamilies)
            && FamilyStudyMembership.Problem(reopened.fcf, reopened.manifest) == null);
        var absent = new QuantManifest();
        Check("a never-computed effective bundle remains absent from legacy manifests", !JsonSerializer.Serialize(absent).Contains("EffectivePanelFamilies", StringComparison.Ordinal));

        int user = file.MyExtendedFastaItems[0].UserID;
        file.MyExtendedFastaItems[0].UserID = 900;
        Check("changed source membership refuses a stored effective bundle", FamilyStudyMembership.Problem(file, manifest) != null);
        file.MyExtendedFastaItems[0].UserID = user;
        file.FamilyPanel!.Families.Single(f => f.Key == "alias").MergeTargetFamilyKey = "other";
        Check("changed panel merge mapping refuses old effective totals without retagging them", EffectivePanelFamilyStudy.Problem(file, result) != null
            && result.PanelMapSha256.Length == 64 && JsonSerializer.Serialize(result) == native);
        file.FamilyPanel.Families.Single(f => f.Key == "alias").MergeTargetFamilyKey = "main";
        var parts = result.Parts; result.Parts = new();
        Check("missing stored part availability refuses publication instead of inventing zero parts", FamilyStudyMembership.Problem(file, manifest) != null);
        result.Parts = parts;
        string membership = result.RunShares.MembershipSha256!;
        result.RunShares.MembershipSha256 = new string('0', 64);
        Check("nested studies cannot silently carry a different source membership", FamilyStudyMembership.Problem(file, manifest) != null);
        result.RunShares.MembershipSha256 = membership;

        var labels = file.ProteinSubclassLabels; file.ProteinSubclassLabels = null;
        var missingLabels = EffectivePanelFamilyStudy.Build(assigned, hits, file, null, manifest.PerRunTic);
        Check("uncomputed subclass labels retain explicit unavailable named parts", missingLabels.SubclassParts == null
            && missingLabels.Parts.Any(p => p.Label == "First part" && p.State == EffectivePanelPartState.NotComputed && p.Signal == null));
        file.ProteinSubclassLabels = labels;

        var cross = EffectivePanelFixture();
        cross.File.MyExtendedFastaItems[2].Sequence = "AAAAAAAKEEEEEEEK";
        cross.File.PanelProteinAssignments![2].PanelFamilyKey = null; cross.File.PanelProteinAssignments[2].IsApplied = false;
        cross = PrepareEffectivePanelFixture(cross.File, cross.Rows);
        var unique = EffectivePanelFamilyStudy.Build(cross.Assigned, cross.Hits, cross.File, null, cross.Manifest.PerRunTic);
        var uniqueMain = unique.Families.Single(f => f.PanelFamilyKey == "main");
        Check("outside candidates remain competitors after within-key deduplication", unique.Families.Any(f => f.Kind == EffectivePanelFamilyKind.OutsidePanel)
            && unique.RunShares.Families.Single(f => f.FamilyUserId == uniqueMain.StudyFamilyId).PooledSignal == 30);
        var split = EffectivePanelFamilyStudy.Build(cross.Assigned, cross.Hits, cross.File, null, cross.Manifest.PerRunTic, SharedPeptidePolicy.SplitEqually);
        Check("split-equally uses distinct collapsed keys, including outside competitors", split.RunShares.Families.Single(f => f.FamilyUserId == uniqueMain.StudyFamilyId)
            is { PooledSignal: 45, MeanShare: .375 });

        var oneObserved = EffectivePanelFixture();
        oneObserved.Rows.RemoveAll(r => r.Sequence != "DDDDDDDK" && r.Sequence != "EEEEEEEK");
        oneObserved = PrepareEffectivePanelFixture(oneObserved.File, oneObserved.Rows);
        var positive = EffectivePanelFamilyStudy.Build(oneObserved.Assigned, oneObserved.Hits, oneObserved.File, null, oneObserved.Manifest.PerRunTic);
        int positiveId = positive.Families.Single(f => f.PanelFamilyKey == "main").StudyFamilyId;
        Check("a positive source component prevents a family-wide no-peptide claim", positive.RunShares.Families.Single(f => f.FamilyUserId == positiveId)
            is { NoPeptideObserved: false, PooledSignal: > 0 });
        var empty = EffectivePanelFixture(); empty.Rows.RemoveAll(r => r.Sequence != "EEEEEEEK");
        empty = PrepareEffectivePanelFixture(empty.File, empty.Rows);
        var zero = EffectivePanelFamilyStudy.Build(empty.Assigned, empty.Hits, empty.File, null, empty.Manifest.PerRunTic);
        int zeroId = zero.Families.Single(f => f.PanelFamilyKey == "main").StudyFamilyId;
        Check("a genuinely unobserved collapsed family gets a zero total while named parts remain unmeasured", zero.RunShares.Families.Single(f => f.FamilyUserId == zeroId)
            is { NoPeptideObserved: true, MeanShare: 0 } && zero.Parts.Where(p => p.PanelFamilyKey == "main" && p.PartKey != null).All(p => p.Signal == null));

        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var pt = EffectivePanelFamilyStudy.Build(assigned, hits, file, new[] { "run_b.raw", "run_a.raw" }, manifest.PerRunTic);
            Check("effective native data repeat byte-for-byte across cultures", native == JsonSerializer.Serialize(pt));
            var ptCsv = ReportEffectivePanelExports.Build(pt);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("effective-key exports are culture invariant", ptCsv.All(p => ReportEffectivePanelExports.Build(result)[p.Key] == p.Value));
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }

        var sourceNames = FamilyNaming.Names(file.MyExtendedFastaItems);
        var data = AnalyticsBuilder.Build(rows, manifest.PerRunTic);
        var pooled = FamilyEnvelopeQuant.BuildFromAttributedPeptides(assigned);
        var grouping = FamilyReportThreshold.Build(manifest.FamilyRunShares!, file);
        var current = ReportCurrentResults.Build(data, manifest, pooled);
        manifest.EffectivePanelFamilies = null;
        var before = ReportStoredResults.Apply(current, manifest, sourceNames, grouping);
        manifest.EffectivePanelFamilies = result;
        var after = ReportStoredResults.Apply(current, manifest, sourceNames, grouping);
        Check("old UserID CSV names, order and bytes remain unchanged beside additive effective exports", before.CsvExports.All(p => after.CsvExports[p.Key] == p.Value)
            && after.CsvExports.Keys.Take(before.CsvExports.Count).SequenceEqual(before.CsvExports.Keys)
            && after.CsvExports.Keys.Count(k => k.StartsWith("effective_panel_", StringComparison.Ordinal)) == 6);
        Check("the headline and composition data retain their UserID basis", JsonSerializer.Serialize(before.Sections) == JsonSerializer.Serialize(after.Sections));
        var hidden = FamilyReportThreshold.Build(manifest.FamilyRunShares!, .2);
        Check("effective briefs honor the unchanged UserID Others membership", ReportPanelSections.BuildFromResults(file, manifest, hidden)
            .Single(s => s.Id == "family:main").Blocks.OfType<ReportTable>().Where(t => t.Id.Contains(":effective:", StringComparison.Ordinal)).All(t => t.SupplementOnly));

        EnsureApplication();
        string directory = output ?? Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.EffectivePanel", "report");
        var export = ReportProjectExporter.Build(rows, manifest, file, Path.Combine(directory, "figures"), familyResults: pooled);
        var brief = export.Document.Sections.Single(s => s.Id == "family:main").Blocks.OfType<ReportParagraph>().Select(p => p.Text).ToArray();
        Check("compact Word prints the one stored total and its unmeasured named parts", brief.Count(t => t.StartsWith("Effective panel-family share:", StringComparison.Ordinal)) == 1
            && brief.Any(t => t.Contains("Mean assigned share = 0.5", StringComparison.Ordinal))
            && brief.Any(t => t.Contains("Unseen part: Not measured", StringComparison.Ordinal))
            && brief.Any(t => t.Contains("Peptide: Unseen segment: Not measured", StringComparison.Ordinal)));
        Check("the report states its headline unit once for a multi-UserID panel family", export.Document.Sections.SelectMany(s => s.Blocks).OfType<ReportParagraph>()
            .Count(p => p.Text == ReportCompositionBasis.PanelNotice) == 1);
        ReportProjectExporter.Write(export, Path.Combine(directory, "effective-panel.docx"));
        Check("the exported report retains all original CSVs and adds effective-key native values", before.CsvExports.All(p => export.CsvExports[p.Key] == p.Value)
            && export.CsvExports["effective_panel_families.csv"].Contains("\"0.5\"", StringComparison.Ordinal));
    }
}
