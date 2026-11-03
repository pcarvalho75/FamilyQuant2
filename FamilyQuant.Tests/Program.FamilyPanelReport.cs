using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FamilyQuant.Panels;
using FamilyQuant.Report;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilyPanelReportTests()
    {
        Section("J6c - panel family sections and stored observations");
        var file = new FamilyClusterFile
        {
            FamilyPanel = new FamilyPanel { Families = new()
            {
                new() { Key = "first", DisplayName = "User family A", SourceDecision = "Choice A",
                    TextFields = new() { new() { Name = "Literature", Text = "1,5%; original\nSecond line" },
                        new() { Name = "Notes", Text = "User's note" }, new() { Name = "Sources", Text = "Source A; Source B" } } },
                new() { Key = "second", DisplayName = "User family B" },
                new() { Key = "merged", DisplayName = "Merged row", Decision = FamilyPanelDecision.MergeIntoAnotherFamily,
                    MergeTargetFamilyKey = "first" },
                new() { Key = "excluded", DisplayName = "Excluded row", Decision = FamilyPanelDecision.NotAFamily },
            } },
            ProteinSubclassLabels = new()
            {
                new() { FamilyUserId = 11, PanelFamilyKey = "first" },
                new() { FamilyUserId = 22, PanelFamilyKey = "first" },
                new() { FamilyUserId = 22, PanelFamilyKey = "second" },
                new() { FamilyUserId = 33, PanelFamilyKey = "second" },
            },
        };
        var manifest = new QuantManifest
        {
            FamilyRunShares = new FamilyRunSharesResult { Method = "Stored method details", Policy = SharedPeptidePolicy.SplitEqually, Families = new()
            {
                new() { FamilyUserId = 11, MeanShare = .125, StandardDeviation = .025, AvailableRunCount = 2,
                    Runs = new() { new() { Run = "run-one", Signal = 123.5, ShareOfAssignedSignal = .125, ShareOfMeasuredSignal = .075 },
                        new() { Run = "run-two", Signal = 0, ShareOfAssignedSignal = null, ShareOfMeasuredSignal = 0 } } },
                new() { FamilyUserId = 22, MeanShare = .8, AvailableRunCount = 1 },
            } },
            FamilyProteinCounts = new FamilyProteinCountsResult { Families = new()
            {
                new() { FamilyUserId = 11, GreedyProteinCount = 3, UniquePeptideProteinCount = 2, ObservedPeptideCount = 5 },
                new() { FamilyUserId = 22, GreedyProteinCount = 9 },
            } },
        };
        manifest.FamilyRunShares!.MembershipSha256 = FamilyStudyMembership.Fingerprint(file);
        manifest.FamilyProteinCounts!.MembershipSha256 = FamilyStudyMembership.Fingerprint(file);
        var method = typeof(ReportPanelSections).GetMethod("BuildFromResults", new[] { typeof(FamilyClusterFile), typeof(QuantManifest) });
        Check("J6c builds from the project's typed stored results", method != null);
        var sections = method?.Invoke(null, new object[] { file, manifest }) as IReadOnlyList<ReportSection>;
        Check("J6c sections retain panel source order and stable keys", sections != null && sections.Select(s => s.Id)
            .SequenceEqual(new[] { "family:first", "family:second", "family:merged", "family:excluded" }));
        var allText = sections?.SelectMany(s => s.Blocks).OfType<ReportTable>().SelectMany(t => t.Rows)
            .SelectMany(r => r).ToArray() ?? Array.Empty<string>();
        Check("J6c prints panel decision, literature, notes and sources verbatim", new[]
            { "Choice A", "1,5%; original\nSecond line", "User's note", "Source A; Source B" }.All(allText.Contains));
        var firstValues = sections?.FirstOrDefault()?.Blocks.OfType<ReportTable>().SelectMany(t => t.Rows)
            .SelectMany(r => r).ToArray() ?? Array.Empty<string>();
        Check("J6c includes exact stored run and protein values", new[] { "run-one", "123.5", "0.125", "0.075", "3", "2", "5" }.All(firstValues.Contains));
        Check("J6c reports the stored share method and shared-peptide policy", sections != null && sections[0].Blocks
            .OfType<ReportTable>().Any(t => t.Caption.Contains("Stored method details", StringComparison.Ordinal)
                && t.Caption.Contains(SharedPeptidePolicy.SplitEqually.DisplayName(), StringComparison.Ordinal)));
        foreach (var policy in Enum.GetValues<SharedPeptidePolicy>())
        {
            manifest.FamilyRunShares!.Policy = policy;
            var policySections = ReportPanelSections.BuildFromResults(file, manifest);
            Check("J6c uses the screen name for " + policy, policySections[0].Blocks.OfType<ReportTable>()
                .Any(t => t.Title == "Observed family share" && t.Caption.Contains(policy.DisplayName(), StringComparison.Ordinal)));
        }
        manifest.FamilyRunShares!.Policy = SharedPeptidePolicy.SplitEqually;
        var runTwo = sections?.FirstOrDefault()?.Blocks.OfType<ReportTable>().Where(t => t.Title == "Observed run shares")
            .SelectMany(t => t.Rows).FirstOrDefault(r => r.Contains("run-two"));
        Check("J6c distinguishes recorded zero from an unavailable share", runTwo != null
            && runTwo.SequenceEqual(new[] { "11", "run-two", "0", "Not available", "0" }));
        var secondTables = sections?.Skip(1).FirstOrDefault()?.Blocks.OfType<ReportTable>().ToArray() ?? Array.Empty<ReportTable>();
        Check("J6c reports each missing study as unavailable for its mapped User ID", secondTables
            .Count(t => t.Rows.Any(r => r.SequenceEqual(new[] { "33", "Not available" }))) == 2);
        Check("J6c does not attribute a shared UserID to either panel family", sections != null
            && sections.Take(2).SelectMany(s => s.Blocks).OfType<ReportTable>().SelectMany(t => t.Rows).SelectMany(r => r)
                .All(v => v != "0.8" && v != "9")
            && sections.Take(2).SelectMany(s => s.Blocks).OfType<ReportParagraph>()
                .Any(p => p.Text.Contains("shared", StringComparison.OrdinalIgnoreCase)));
        Check("J6c explicitly handles merged and excluded rows", sections != null && sections.Skip(2)
            .All(s => s.Blocks.OfType<ReportParagraph>().Any())
            && sections.Skip(2).All(s => !s.Blocks.OfType<ReportTable>().Any(t => t.Title.Contains("observed", StringComparison.OrdinalIgnoreCase))));
        Check("J6c does not invent sections for an absent panel", ReportPanelSections.BuildFromResults(new FamilyClusterFile(), new QuantManifest()).Count == 0);
        var duplicate = new QuantManifest { FamilyRunShares = new FamilyRunSharesResult { Families = new()
            { new() { FamilyUserId = 11 }, new() { FamilyUserId = 11 } } } };
        duplicate.FamilyRunShares.MembershipSha256 = FamilyStudyMembership.Fingerprint(file);
        bool refused = false;
        try { ReportPanelSections.BuildFromResults(file, duplicate); }
        catch (InvalidOperationException) { refused = true; }
        Check("J6c refuses duplicated stored family results instead of publishing duplicate block IDs", refused);
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var english = method?.Invoke(null, new object[] { file, manifest }) as IReadOnlyList<ReportSection>;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var translated = method?.Invoke(null, new object[] { file, manifest }) as IReadOnlyList<ReportSection>;
            string Tables(IReadOnlyList<ReportSection>? value) => System.Text.Json.JsonSerializer.Serialize(value?
                .SelectMany(s => s.Blocks).OfType<ReportTable>().Select(t => new { t.Id, t.Rows }).ToArray());
            Check("J6c observed result strings are invariant across cultures", translated != null && english != null
                && Tables(translated) == Tables(english));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }
}
