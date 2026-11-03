using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ScopeVisibilityCommands() =>
    [new("--test-scope-visibility", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-scope-visibility", (_, _) =>
        {
            EnsureApplication(); ScopeVisibilityTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void ScopeVisibilityTests()
    {
        Section("J33 scope visibility");
        var file = PanelEvidenceFile();
        var reference = file.MyExtendedFastaItems[0];
        var target = new FastaItemExtended
        {
            SequenceIdentifier = "sp|P00006|OUTSIDE", Sequence = reference.Sequence, UserID = 6, USearchID = 6,
            Evidence = new ProteinEvidence { Accession = "P00006", Reviewed = true, Family = "Source group one",
                Pfam = new() { new() { Accession = "domain-a" }, new() { Accession = "domain-b" } },
                TissueSpecificity = new() { "Expressed in the shared organ" } },
        };
        var unsupported = new FastaItemExtended
        {
            SequenceIdentifier = "sp|P00007|OUTSIDE", Sequence = reference.Sequence, UserID = 7, USearchID = 7,
            Evidence = new ProteinEvidence { Accession = "P00007", Reviewed = true, Family = "Other source without a link" },
        };
        file.MyExtendedFastaItems.Add(target); file.MyExtendedFastaItems.Add(unsupported);
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected", "other scope" } };
        var references = PanelEvidenceClassifier.References(file, options);
        var links = PanelEvidenceClassifier.BuildLinks(file.FamilyPanel!, references, options);
        var ownLink = links.Single(link => link.SourceFamily == "Source group one"
            && link.ArchitectureKey == PanelEvidenceClassifier.ArchitectureKey(reference.Evidence));
        var confirmed = PanelEvidenceClassifier.ConfirmLinks(file.FamilyPanel!, links,
            new[] { new PanelFamilyLinkSelection { LinkKey = ownLink.Key, PanelFamilyKey = "broad" } });
        var search = NearestReviewed.Search(NearestReviewed.Targets(file), references);
        search.Neighbours.RemoveAll(n => n.Query == target.SequenceIdentifier || n.Query == unsupported.SequenceIdentifier);
        var proposal = PanelEvidenceClassifier.Propose(file, options, search, references, confirmed);
        var row = proposal.Proteins.Single(p => p.Identifier == target.SequenceIdentifier);
        Check("J33 excluded own family does not silently change the grouping", row.Family.Length == 0 && row.Tier == EvidenceTier.Unassigned);
        string previousReason = row.Reason;
        var applied = EvidenceApply.Apply(file, proposal, new EvidenceApplySelection
        {
            SourceFingerprint = proposal.SourceFingerprint, FamilyPanel = confirmed, SessionSearch = search, SessionReferences = references,
            SelectedProteinIndices = proposal.Proteins.Where(p => p.SuggestedSelected).Select(p => p.ProteinIndex).ToArray(),
        }, "fixture");
        var audit = ReportCsvTable.Build("audit", "audit", PanelAuditCsv.BuildCsv(file), "");
        int accession = audit.Columns.ToList().IndexOf("Accession"), reason = audit.Columns.ToList().IndexOf("Family reason");
        string targetReason = audit.Rows.Single(r => r[accession] == "P00006")[reason];
        string unsupportedReason = audit.Rows.Single(r => r[accession] == "P00007")[reason];
        Check("J33 audit identifies the excluded linked own family and names every scope phrase",
            targetReason.Contains("scope", StringComparison.OrdinalIgnoreCase)
            && targetReason.Contains("selected", StringComparison.Ordinal) && targetReason.Contains("other scope", StringComparison.Ordinal));
        Check("J33 audit retains the neighbour gate alongside the scope explanation", targetReason.Contains(previousReason, StringComparison.Ordinal));
        Check("J33 an unrelated family retains its actual neighbour-gate reason", unsupportedReason == "No reviewed neighbour passes the search gate.");
        Check("J33 scope explanation is audit-only and preserves recorded recipe reasons", applied.Recipe.PanelProteinAssignments!
            .Single(p => p.Identifier == target.SequenceIdentifier).Reason == previousReason);
        string firstAudit = PanelAuditCsv.BuildCsv(file);
        var reopened = FamilyClusterFile.FromJsonText(file.ToJsonText());
        Check("J33 the same scope reason is reconstructed after reopening", PanelAuditCsv.BuildCsv(reopened) == firstAudit);
        applied.Undo!.Restore(file);
        EvidenceApply.ApplyRecipe(file, applied.Recipe);
        Check("J33 older-style saved assignment reasons still replay and export identically", PanelAuditCsv.BuildCsv(file) == firstAudit);
        var expanded = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected", "shared organ" } };
        var beforeApply = PanelEvidenceFile();
        beforeApply.MyExtendedFastaItems.Add(target);
        var withTissue = PanelEvidenceClassifier.Propose(beforeApply, expanded);
        Check("J33 a user-selected tissue scope permits the existing source-family rule",
            withTissue.Proteins.Single(p => p.Identifier == target.SequenceIdentifier).Tier == EvidenceTier.UniProtFamily);

        string SavedLinkAudit(bool stale)
        {
            var onlySaved = PanelEvidenceFile();
            onlySaved.FamilyPanel = FamilyPanelValidation.Clone(confirmed);
            onlySaved.MyExtendedFastaItems[0].Evidence!.Keywords.Clear();
            onlySaved.MyExtendedFastaItems[4].Evidence!.Keywords.Clear();
            if (stale) onlySaved.FamilyPanel.Families[0].Pfam.RemoveAt(1);
            onlySaved.MyExtendedFastaItems.Add(new FastaItemExtended
            {
                SequenceIdentifier = "sp|P00006|OUTSIDE", Sequence = reference.Sequence, UserID = 6, USearchID = 6,
                Evidence = new ProteinEvidence { Accession = "P00006", Reviewed = true, Family = "Source group one",
                    Pfam = new() { new() { Accession = "domain-a" }, new() { Accession = "domain-b" } } },
            });
            var singleScope = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
            var savedProposal = PanelEvidenceClassifier.Propose(onlySaved, singleScope);
            EvidenceApply.Apply(onlySaved, savedProposal, new EvidenceApplySelection
            {
                SourceFingerprint = savedProposal.SourceFingerprint,
                SelectedProteinIndices = savedProposal.Proteins.Where(p => p.SuggestedSelected).Select(p => p.ProteinIndex).ToArray(),
            }, "fixture");
            if (!stale) Check("J33 the saved-link audit does not change the assignment resolver",
                PanelEvidenceClassifier.CreateCurrentResolver(onlySaved).PanelFamilyFor(onlySaved.MyExtendedFastaItems[^1].Evidence) == null);
            var savedAudit = ReportCsvTable.Build("saved", "saved", PanelAuditCsv.BuildCsv(onlySaved), "");
            return savedAudit.Rows.Single(r => r[accession] == "P00006")[reason];
        }
        Check("J33 audit explains a current saved link even when no reference for that source is in scope",
            SavedLinkAudit(false).Contains("scope", StringComparison.OrdinalIgnoreCase));
        Check("J33 audit does not claim a saved link whose panel definition changed",
            !SavedLinkAudit(true).Contains("scope", StringComparison.OrdinalIgnoreCase));

        var automatic = PanelEvidenceFile();
        automatic.MyExtendedFastaItems.Add(new FastaItemExtended
        {
            SequenceIdentifier = "sp|P00008|OUTSIDE", Sequence = reference.Sequence, UserID = 8, USearchID = 8,
            Evidence = new ProteinEvidence { Accession = "P00008", Reviewed = true, Family = "Source group two",
                Pfam = new() { new() { Accession = "domain-c" } } },
        });
        var automaticProposal = PanelEvidenceClassifier.Propose(automatic, options);
        EvidenceApply.Apply(automatic, automaticProposal, new EvidenceApplySelection
        {
            SourceFingerprint = automaticProposal.SourceFingerprint,
            SelectedProteinIndices = automaticProposal.Proteins.Where(p => p.SuggestedSelected).Select(p => p.ProteinIndex).ToArray(),
        }, "fixture");
        var automaticAudit = ReportCsvTable.Build("automatic", "automatic", PanelAuditCsv.BuildCsv(automatic), "");
        Check("J33 audit also explains an unambiguous evidence-derived link without a saved confirmation",
            automaticAudit.Rows.Single(r => r[accession] == "P00008")[reason].Contains("scope", StringComparison.OrdinalIgnoreCase));

        var task = TaskFile();
        task.MyExtendedFastaItems[0].Evidence!.TissueSpecificity = new() { "Expressed by the shared organ", "Expressed by the shared organ",
            "early organ", "early organ", "early organ", "early organ", "Not detected in forbidden organ, hidden organ" };
        task.MyExtendedFastaItems[1].Evidence!.TissueSpecificity = new() { "Expressed in shared organ", "Not detected in forbidden organ, hidden organ" };
        task.MyExtendedFastaItems[2].Evidence!.TissueSpecificity = new() { "Expressed in irrelevant organ" };
        task.MyExtendedFastaItems[3].Evidence!.TissueSpecificity = new() { "Expressed in irrelevant organ" };
        task.MyExtendedFastaItems[4].Evidence!.TissueSpecificity = new() { "Expressed in irrelevant organ" };
        task.MyExtendedFastaItems.Add(TaskProtein("sp|P00004|FOUR", 21, true, "Other"));
        task.MyExtendedFastaItems[^1].Evidence!.TissueSpecificity = new() { "Expressed in shared organ", "Not detected in forbidden organ, hidden organ" };
        using var handler = new TaskTaxonomyHandler();
        var discovered = EvidenceTaskDiscovery.DiscoverAsync(task, handler).GetAwaiter().GetResult();
        Check("J33 proposal preserves the leading functional keyword first", discovered.ProposedScopeTerms.Count > 0 && discovered.ProposedScopeTerms[0] == "Leading scope");
        Check("J33 proposal adds the most common reviewed tissue phrase", discovered.ProposedScopeTerms.Count == 2
            && discovered.ProposedScopeTerms[1].Equals("shared organ", StringComparison.OrdinalIgnoreCase));
        Check("J33 a suggested tissue phrase actually matches each supporting tissue line", discovered.ProposedScopeTerms.Count > 1
            && task.MyExtendedFastaItems.Take(2).All(p => EvidenceClassifier.IsInScope(p.Evidence, new[] { discovered.ProposedScopeTerms[1] })));
        var alias = TaskProtein("sp|P00001|ALIAS", 21, true, "Leading scope", "Other");
        alias.Evidence!.TissueSpecificity = new() { "Expressed by the SHARED ORGAN" };
        task.MyExtendedFastaItems.Add(alias);
        using var aliasHandler = new TaskTaxonomyHandler();
        var aliasProposal = EvidenceTaskDiscovery.DiscoverAsync(task, aliasHandler).GetAwaiter().GetResult();
        var reordered = FamilyClusterFile.FromJsonText(task.ToJsonText());
        reordered.MyExtendedFastaItems.Reverse();
        using var reorderHandler = new TaskTaxonomyHandler();
        var reorderedProposal = EvidenceTaskDiscovery.DiscoverAsync(reordered, reorderHandler).GetAwaiter().GetResult();
        Check("J33 repeated annotations and duplicate accessions count once and are independent of order",
            aliasProposal.ReviewedProteins == 3 && aliasProposal.ProposedScopeTerms[1].Equals("shared organ", StringComparison.OrdinalIgnoreCase)
            && aliasProposal.ProposedScopeTerms.SequenceEqual(reorderedProposal.ProposedScopeTerms, StringComparer.Ordinal));
        var tissueOnly = TaskFile();
        foreach (var protein in tissueOnly.MyExtendedFastaItems) protein.Evidence!.Keywords.Clear();
        tissueOnly.MyExtendedFastaItems[0].Evidence!.TissueSpecificity = new() { "Observed in single organ" };
        tissueOnly.MyExtendedFastaItems[1].Evidence!.TissueSpecificity = new() { "Observed in single organ" };
        using var tissueOnlyHandler = new TaskTaxonomyHandler();
        var tissueOnlyProposal = EvidenceTaskDiscovery.DiscoverAsync(tissueOnly, tissueOnlyHandler).GetAwaiter().GetResult();
        Check("J33 tissue evidence can supply a scope when no keyword is available", tissueOnlyProposal.ProposedScopeTerms.Count == 1
            && tissueOnlyProposal.ProposedScopeTerms[0].Equals("single organ", StringComparison.OrdinalIgnoreCase));
        tissueOnly.MyExtendedFastaItems[0].Evidence!.TissueSpecificity = new() { "Expressed in outer (level detail) organ" };
        tissueOnly.MyExtendedFastaItems[1].Evidence!.TissueSpecificity = new() { "Expressed in outer (different detail) organ" };
        using var parenthesesHandler = new TaskTaxonomyHandler();
        var parentheses = EvidenceTaskDiscovery.DiscoverAsync(tissueOnly, parenthesesHandler).GetAwaiter().GetResult();
        Check("J33 a phrase cannot bridge words separated by a parenthetical annotation", parentheses.ProposedScopeTerms.Count == 1
            && tissueOnly.MyExtendedFastaItems.Take(2).All(p => EvidenceClassifier.IsInScope(p.Evidence, parentheses.ProposedScopeTerms)));
        using (new TaskCultureScope("pt-BR"))
        using (var localized = new TaskTaxonomyHandler())
            Check("J33 tissue proposals are invariant across cultures", JsonSerializer.Serialize(aliasProposal.ProposedScopeTerms)
                == JsonSerializer.Serialize(EvidenceTaskDiscovery.DiscoverAsync(task, localized).GetAwaiter().GetResult().ProposedScopeTerms));
        var session = new EvidenceGroupingSession(task, "fixture", discoverTask: (_, _) => Task.FromResult(discovered));
        var facts = session.DiscoverTaskAsync().GetAwaiter().GetResult();
        SuperClusterPreview Preview() => new(new List<FastaItemExtended>(), null, "", "", KeywordMatch.All, null, null);
        var model = new GroupingReviewViewModel(Preview(), evidenceMode: true, discoverTask: _ => Task.FromResult(facts));
        FinishReviewWork(model.DiscoverTaskAsync);
        Check("J33 the scope box presents the discovered keyword and tissue phrase", WordCluster.ParseKeywords(model.ScopeText)
            .SequenceEqual(discovered.ProposedScopeTerms, StringComparer.OrdinalIgnoreCase));
        model.ScopeText = "\"User supplied\"";
        FinishReviewWork(model.DiscoverTaskAsync);
        Check("J33 discovery never overwrites a user's edited scope", model.ScopeText == "\"User supplied\"");
    }
}
