using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilyPanelSubclassTests()
    {
        Section("J3a - panel-defined subclass evidence");
        var family = new FamilyPanelFamily { Key = "family-a", Subclasses = new()
        {
            new() { Key = "part-a", RequiredPfam = new() { "domain-a" }, AbsentPfam = new() { "domain-b" },
                UniProtSubfamilies = new() { "Alpha type" } },
            new() { Key = "part-b", RequiredPfam = new() { "domain-a", "domain-b" }, UniProtSubfamilies = new() { "Beta type" } },
            new() { Key = "fragment", RequiredPfam = new() { "domain-c" }, ReportAsSubclassKey = "part-b" },
        } };
        static ProteinEvidence Evidence(string[] domains, string subfamily = "", string annotation = "") => new()
        {
            Pfam = domains.Select(d => new EvidenceDomain { Accession = d }).ToList(),
            Subfamily = subfamily, FamilyAnnotation = annotation,
        };
        var type = typeof(FamilyPanel).Assembly.GetType("FamilyQuant.Panels.SubclassAssignment");
        var method = type?.GetMethod("ClassifyDirect", new[] { typeof(FamilyPanelFamily), typeof(ProteinEvidence) });
        Check("J3a provides a panel-defined direct classifier", method != null);
        ProteinSubclassLabel? Direct(FamilyPanelFamily source, ProteinEvidence facts) =>
            method?.Invoke(null, new object[] { source, facts }) as ProteinSubclassLabel;
        var onlyA = Direct(family, Evidence(new[] { "domain-a" }));
        var both = Direct(family, Evidence(new[] { "domain-a", "domain-b" }));
        var fragment = Direct(family, Evidence(new[] { "domain-c" }));
        Check("J3a requires every present and absent domain in each user definition", onlyA?.SubclassKey == "part-a"
            && both?.SubclassKey == "part-b" && onlyA.Status == ProteinSubclassStatus.Assigned && both.Status == ProteinSubclassStatus.Assigned);
        Check("J3a follows the user report-as target and retains the original definition", fragment?.SubclassKey == "part-b"
            && fragment.DefinitionKey == "fragment");
        Check("J3a matches a user subfamily term against the source subfamily or annotation component",
            Direct(family, Evidence(Array.Empty<string>(), "Beta type"))?.SubclassKey == "part-b"
            && Direct(family, Evidence(Array.Empty<string>(), annotation: "Example family, Beta type; other text"))?.SubclassKey == "part-b");
        var partial = Direct(family, Evidence(Array.Empty<string>(), "Beta type extended"));
        Check("J3a does not turn a partial subfamily string into a match", partial?.Status == ProteinSubclassStatus.Unknown
            && !string.IsNullOrWhiteSpace(partial.Reason));
        var conflict = Direct(family, Evidence(new[] { "domain-a" }, "Beta type"));
        Check("J3a keeps conflicting domain and subfamily evidence Unknown", conflict?.Status == ProteinSubclassStatus.Unknown
            && conflict.SubclassKey == null && !string.IsNullOrWhiteSpace(conflict.Reason));
        var negativeOnly = new FamilyPanelFamily { Key = "family-b", Subclasses = new()
            { new() { Key = "negative", AbsentPfam = new() { "domain-x" } } } };
        var missing = Direct(negativeOnly, Evidence(Array.Empty<string>()));
        Check("J3a does not infer absent domains from missing domain evidence", missing?.Status == ProteinSubclassStatus.Unknown
            && !string.IsNullOrWhiteSpace(missing.Reason));
        var workbookShape = new FamilyPanel { Families = new()
        {
            new() { Key = "SVMP", Subclasses = new()
            {
                new() { Key = "P-I", RequiredPfam = new() { "PF01421" }, AbsentPfam = new() { "PF00200", "PF08516" } },
                new() { Key = "P-II", RequiredPfam = new() { "PF01421", "PF00200" }, AbsentPfam = new() { "PF08516" } },
                new() { Key = "P-III", RequiredPfam = new() { "PF01421", "PF00200", "PF08516" } },
                new() { Key = "DC", RequiredPfam = new() { "PF00200", "PF08516" }, AbsentPfam = new() { "PF01421" }, ReportAsSubclassKey = "P-III" },
            } },
            new() { Key = "PLA2", Subclasses = new()
            {
                new() { Key = "D49", UniProtSubfamilies = new() { "D49" } },
                new() { Key = "K49", UniProtSubfamilies = new() { "K49" } },
            } },
        } };
        var architectures = workbookShape.Families[0];
        Check("J3a accepts the requested four user-entered architecture rule shapes",
            Direct(architectures, Evidence(new[] { "PF01421" }))?.SubclassKey == "P-I"
            && Direct(architectures, Evidence(new[] { "PF01421", "PF00200" }))?.SubclassKey == "P-II"
            && Direct(architectures, Evidence(new[] { "PF01421", "PF00200", "PF08516" }))?.SubclassKey == "P-III"
            && Direct(architectures, Evidence(new[] { "PF00200", "PF08516" }))?.SubclassKey == "P-III");
        var subfamilies = workbookShape.Families[1];
        Check("J3a matches a user-entered subfamily token to the complete ranked source text",
            Direct(subfamilies, Evidence(Array.Empty<string>(), "D49 subfamily"))?.SubclassKey == "D49"
            && Direct(subfamilies, Evidence(Array.Empty<string>(), "K49 sub-subfamily"))?.SubclassKey == "K49"
            && Direct(subfamilies, Evidence(Array.Empty<string>(), "K490 sub-subfamily"))?.Status == ProteinSubclassStatus.Unknown);
        var incomplete = Evidence(new[] { "domain-a" }); incomplete.Fragment = true;
        Check("J3a does not infer an absent domain from a recorded protein fragment",
            Direct(family, incomplete)?.Status == ProteinSubclassStatus.Unknown);

        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
        var build = type?.GetMethod("BuildCandidate", new[] { typeof(FamilyClusterFile), typeof(EvidenceClassificationOptions) });
        Check("J3a provides a target-by-target candidate builder", build != null);
        string sequence = string.Concat(Enumerable.Repeat("ACDEFGHIKLMNPQRSTVWY", 5));
        FastaItemExtended Protein(string id, bool reviewed, bool scope, params string[] domains) => new()
        {
            SequenceIdentifier = id, Sequence = sequence, UserID = 11, USearchID = 11,
            Evidence = new ProteinEvidence { Accession = id, Reviewed = reviewed, Family = reviewed ? "Source family" : "",
                Keywords = scope ? new() { "selected" } : new(),
                Pfam = domains.Select(d => new EvidenceDomain { Accession = d }).ToList() },
        };
        FamilyClusterFile Fixture(bool tied)
        {
            var file = new FamilyClusterFile { FamilyPanel = FamilyPanelValidation.Clone(new FamilyPanel { Families = new() { family } }),
                MyExtendedFastaItems = new()
                {
                    Protein("R-one", true, true, "common", "domain-a"),
                    Protein("Q-one", false, true, "common"),
                    Protein("Q-no-domains", false, false),
                    Protein("Q-outside", false, true, "common"),
                    Protein(SequenceLabels.DecoyPrefix + "D-one", false, true, "common"),
                } };
            file.MyExtendedFastaItems[3].UserID = 99;
            if (tied) file.MyExtendedFastaItems.Add(Protein("R-two", true, true, "common", "domain-a", "domain-b"));
            file.PanelProteinAssignments = file.MyExtendedFastaItems.Select((p, i) => (p, i))
                .Where(x => !NearestReviewed.IsDecoy(x.p)).Select(x => new PanelProteinAssignment
                {
                    ProteinIndex = x.i, Identifier = x.p.SequenceIdentifier, FamilyUserId = x.p.UserID,
                    IsApplied = x.p.SequenceIdentifier != "Q-outside", PanelFamilyKey = x.p.SequenceIdentifier == "Q-outside" ? null : family.Key,
                }).ToList();
            file.PanelProteinAssignmentsPanelFingerprint = PanelDerivedDataCurrentness.Fingerprint(file.FamilyPanel);
            file.PanelProteinAssignmentsFingerprint = "synthetic-stand-in";
            return file;
        }
        object? Candidate(FamilyClusterFile file) => build?.Invoke(null, new object[] { file, options });
        IReadOnlyList<ProteinSubclassLabel> Labels(object? result) =>
            result?.GetType().GetProperty("Labels")?.GetValue(result) as IReadOnlyList<ProteinSubclassLabel> ?? Array.Empty<ProteinSubclassLabel>();
        var fixture = Fixture(false);
        var result = Candidate(fixture);
        var labels = Labels(result);
        Check("J3a emits one explicit row per target, excluding decoys", labels.Count == 4
            && labels.Select(r => r.ProteinIndex).SequenceEqual(new[] { 0, 1, 2, 3 })
            && labels.All(r => !string.IsNullOrWhiteSpace(r.Reason)));
        Check("J3a carries a qualifying reviewed subclass to an in-scope unreviewed target", labels
            .Any(r => r.Identifier == "Q-one" && r.SubclassKey == "part-a" && r.Status == ProteinSubclassStatus.Assigned));
        Check("J3a requires the existing domain gate for an unannotated target", labels
            .Any(r => r.Identifier == "Q-no-domains" && r.Status == ProteinSubclassStatus.Unknown));
        Check("J3a leaves unapplied family assignments Unknown", labels
            .Any(r => r.Identifier == "Q-outside" && r.Status == ProteinSubclassStatus.Unknown));
        Check("J3a records the current panel fingerprint", result?.GetType().GetProperty("PanelFingerprint")?.GetValue(result) as string
            == fixture.PanelProteinAssignmentsPanelFingerprint);
        var tiedLabels = Labels(Candidate(Fixture(true)));
        Check("J3a refuses equally near reviewed references with different subclasses", tiedLabels
            .Any(r => r.Identifier == "Q-one" && r.Status == ProteinSubclassStatus.Unknown && !string.IsNullOrWhiteSpace(r.Reason)));
        var same = Fixture(true);
        same.MyExtendedFastaItems.Last().Evidence!.Pfam.RemoveAll(d => d.Accession == "domain-b");
        Check("J3a can use equally near references when they agree on the reported subclass", Labels(Candidate(same))
            .Any(r => r.Identifier == "Q-one" && r.SubclassKey == "part-a"));
        var differentSourceFamilies = Fixture(true);
        differentSourceFamilies.MyExtendedFastaItems.Last().Evidence!.Pfam.RemoveAll(d => d.Accession == "domain-b");
        differentSourceFamilies.MyExtendedFastaItems.Last().Evidence!.Family = "Other source family";
        Check("J3a preserves the existing refusal of ties across source families", Labels(Candidate(differentSourceFamilies))
            .Any(r => r.Identifier == "Q-one" && r.Status == ProteinSubclassStatus.Unknown));
        var differentRules = Fixture(true);
        differentRules.FamilyPanel!.Families[0].Subclasses.Single(s => s.Key == "fragment").ReportAsSubclassKey = "part-a";
        differentRules.MyExtendedFastaItems.Last().Evidence!.Pfam = new()
            { new() { Accession = "common" }, new() { Accession = "domain-c" } };
        differentRules.PanelProteinAssignmentsPanelFingerprint = PanelDerivedDataCurrentness.Fingerprint(differentRules.FamilyPanel);
        Check("J3a does not choose one tied source rule when the same reported subclass has different rules", Labels(Candidate(differentRules))
            .Any(r => r.Identifier == "Q-one" && r.Status == ProteinSubclassStatus.Unknown));
        var unannotated = Fixture(false);
        unannotated.MyExtendedFastaItems[1].Evidence!.Keywords.Clear();
        Check("J3a accepts an unannotated target only with a compatible observed domain", Labels(Candidate(unannotated))
            .Any(r => r.Identifier == "Q-one" && r.SubclassKey == "part-a"));
        unannotated.MyExtendedFastaItems[1].Evidence!.Pfam[0].Accession = "unrelated";
        Check("J3a refuses an unannotated target when its domains are incompatible", Labels(Candidate(unannotated))
            .Any(r => r.Identifier == "Q-one" && r.Status == ProteinSubclassStatus.Unknown));
        var contradicted = Fixture(false);
        contradicted.MyExtendedFastaItems[1].Evidence!.Pfam.Add(new EvidenceDomain { Accession = "domain-b" });
        Check("J3a refuses a carried subclass when the target has a domain the rule forbids", Labels(Candidate(contradicted))
            .Any(r => r.Identifier == "Q-one" && r.Status == ProteinSubclassStatus.Unknown));
        var fragmentTarget = Fixture(false);
        fragmentTarget.MyExtendedFastaItems[1].Evidence!.Fragment = true;
        Check("J3a does not carry an absence-dependent subclass to a recorded fragment", Labels(Candidate(fragmentTarget))
            .Any(r => r.Identifier == "Q-one" && r.Status == ProteinSubclassStatus.Unknown));
        var stale = Fixture(false);
        stale.FamilyPanel!.Families[0].Subclasses[0].RequiredPfam.Add("new-rule");
        bool refused = false;
        try { Candidate(stale); }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is InvalidOperationException) { refused = true; }
        Check("J3a refuses family assignments from an older panel fingerprint", refused);
        var conflictId = Fixture(false);
        conflictId.MyExtendedFastaItems[3].UserID = 11;
        conflictId.PanelProteinAssignments!.Single(a => a.Identifier == "Q-outside").FamilyUserId = 11;
        var conflictLabels = Labels(Candidate(conflictId));
        Check("J3a flags each target in a conflicting current UserID instead of placing it in a panel family", conflictLabels.Count == 4
            && conflictLabels.All(l => l.Status == ProteinSubclassStatus.Unknown && l.PanelFamilyKey == null
                && l.Reason.Contains("conflict", StringComparison.OrdinalIgnoreCase)));
        var replayFile = Fixture(false);
        replayFile.EvidenceRecipe = new() { FamilyPanel = FamilyPanelValidation.Clone(replayFile.FamilyPanel!) };
        string inputJson = JsonSerializer.Serialize(replayFile.FamilyPanel);
        string folder = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.Subclass", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string project = Path.Combine(folder, "panel.fqproj");
        ProjectStore.WriteFqproj(project, replayFile, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(project).fcf;
        reopened.FamilyPanel = FamilyPanelValidation.Clone(reopened.EvidenceRecipe!.FamilyPanel!);
        Check("J3a user-entered subclass definitions survive project reopen and recipe replay", inputJson == JsonSerializer.Serialize(reopened.FamilyPanel)
            && JsonSerializer.Serialize(Labels(Candidate(replayFile))) == JsonSerializer.Serialize(Labels(Candidate(reopened))));
        var shaped = new FamilyClusterFile { FamilyPanel = workbookShape,
            EvidenceRecipe = new EvidenceRecipe { FamilyPanel = FamilyPanelValidation.Clone(workbookShape) } };
        string shapedPath = Path.Combine(folder, "user-rule-shapes.fqproj");
        ProjectStore.WriteFqproj(shapedPath, shaped, Array.Empty<QuantRow>(), new QuantManifest());
        var shapedReplay = ProjectStore.ReadFqproj(shapedPath).fcf.EvidenceRecipe!.FamilyPanel!;
        Check("J3a requested user rule shapes survive project and recipe replay", JsonSerializer.Serialize(workbookShape) == JsonSerializer.Serialize(shapedReplay)
            && Direct(shapedReplay.Families[0], Evidence(new[] { "PF00200", "PF08516" }))?.SubclassKey == "P-III"
            && Direct(shapedReplay.Families[1], Evidence(Array.Empty<string>(), "K49 sub-subfamily"))?.SubclassKey == "K49");

        void MakeAssignmentsCurrent(FamilyClusterFile file)
        {
            MakePanelAssignmentsCurrent(file, options);
        }
        var current = Fixture(false); MakeAssignmentsCurrent(current);
        Check("J3a stand-in exercises J2a's current-assignment accessor", PanelAssignmentEvidenceStore.Current(current) != null);
        var commitMethod = type?.GetMethod("CommitCandidate", new[] { typeof(FamilyClusterFile), typeof(SubclassAssignmentCandidate) });
        var currentMethod = type?.GetMethod("IsCurrent", new[] { typeof(FamilyClusterFile) });
        Check("J3a provides guarded candidate publication and currentness checks", commitMethod != null && currentMethod != null);
        object? fresh = Candidate(current);
        object? applied = commitMethod?.Invoke(null, new[] { current, fresh });
        Check("J3a publishes every label and its fingerprints to project and recipe", applied != null
            && current.ProteinSubclassLabels?.Count == 4 && current.ProteinSubclassLabelsPanelFingerprint == current.PanelProteinAssignmentsPanelFingerprint
            && current.ProteinSubclassLabelsFingerprint != null && current.EvidenceRecipe?.ProteinSubclassLabelsFingerprint == current.ProteinSubclassLabelsFingerprint
            && JsonSerializer.Serialize(current.ProteinSubclassLabels) == JsonSerializer.Serialize(current.EvidenceRecipe?.ProteinSubclassLabels)
            && (bool?)currentMethod?.Invoke(null, new object[] { current }) == true);
        string currentProject = Path.Combine(folder, "current-subclasses.fqproj");
        ProjectStore.WriteFqproj(currentProject, current, Array.Empty<QuantRow>(), new QuantManifest());
        var currentReopened = ProjectStore.ReadFqproj(currentProject).fcf;
        Check("J3a current subclass labels survive project reopen", (bool?)currentMethod?.Invoke(null, new object[] { currentReopened }) == true
            && JsonSerializer.Serialize(current.ProteinSubclassLabels) == JsonSerializer.Serialize(currentReopened.ProteinSubclassLabels));
        currentReopened.MyExtendedFastaItems[1].Evidence!.Keywords.Add("changed");
        Check("J3a stored labels become unavailable when evidence changes", (bool?)currentMethod?.Invoke(null, new object[] { currentReopened }) == false);
        var undo = applied?.GetType().GetProperty("Undo")?.GetValue(applied) as GroupingSnapshot;
        undo?.Restore(current);
        Check("J3a undo restores the prior missing labels and recipe", undo != null && current.ProteinSubclassLabels == null
            && current.EvidenceRecipe?.ProteinSubclassLabels == null && current.ProteinSubclassLabelsFingerprint == null);
        var staleSource = Fixture(false); MakeAssignmentsCurrent(staleSource);
        var staleCandidate = Candidate(staleSource);
        staleSource.MyExtendedFastaItems[1].Evidence!.Keywords.Add("changed source");
        bool staleRefused = false;
        try { commitMethod?.Invoke(null, new[] { staleSource, staleCandidate }); }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is InvalidOperationException) { staleRefused = true; }
        Check("J3a refuses publication after the family evidence source changes", staleRefused && staleSource.ProteinSubclassLabels == null);
        var tampered = Fixture(false); MakeAssignmentsCurrent(tampered);
        var tamperedCandidate = Candidate(tampered);
        if (Labels(tamperedCandidate).Count > 0) Labels(tamperedCandidate)[0].SubclassKey = "altered";
        bool tamperRefused = false;
        try { commitMethod?.Invoke(null, new[] { tampered, tamperedCandidate }); }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is InvalidOperationException) { tamperRefused = true; }
        Check("J3a refuses a candidate modified after computation", tamperRefused && tampered.ProteinSubclassLabels == null);
        var cancelled = Fixture(false); MakeAssignmentsCurrent(cancelled);
        var cancelledCandidate = Candidate(cancelled);
        var commitWithContext = type?.GetMethod("CommitCandidate", new[] { typeof(FamilyClusterFile), typeof(SubclassAssignmentCandidate),
            typeof(FamilyQuant.Operations.OperationContext) });
        bool cancelObserved = false;
        try { commitWithContext?.Invoke(null, new object?[] { cancelled, cancelledCandidate,
            new FamilyQuant.Operations.OperationContext(new System.Threading.CancellationToken(true)) }); }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is OperationCanceledException) { cancelObserved = true; }
        Check("J3a cancellation before final commit publishes no labels", cancelObserved && cancelled.ProteinSubclassLabels == null);
        MakeAssignmentsCurrent(conflictId);
        var currentConflict = Candidate(conflictId);
        var conflictApplied = commitMethod?.Invoke(null, new[] { conflictId, currentConflict });
        Check("J3a keeps a conflicting current UserID current and explicitly unresolved", PanelAssignmentEvidenceStore.Current(conflictId) != null
            && conflictApplied != null && (bool?)currentMethod?.Invoke(null, new object[] { conflictId }) == true
            && conflictId.ProteinSubclassLabels!.All(l => l.PanelFamilyKey == null && l.Reason.Contains("conflict", StringComparison.OrdinalIgnoreCase)));

        var looMethod = type?.GetMethod("MeasureLeaveOneOutCandidate", new[] { typeof(FamilyClusterFile), typeof(EvidenceClassificationOptions) });
        Check("J3a provides a reviewed-subfamily leave-one-out measurement", looMethod != null);
        object? Measure(FamilyClusterFile file) => looMethod?.Invoke(null, new object[] { file, options });
        int? Count(object? measurement, string property) => measurement?.GetType().GetProperty(property)?.GetValue(measurement) as int?;
        var one = Fixture(false);
        one.MyExtendedFastaItems[0].Evidence!.Subfamily = "Alpha type";
        var noSelf = Measure(one);
        Check("J3a leave-one-out never predicts a reviewed protein from itself", Count(noSelf, "Eligible") == 1
            && Count(noSelf, "NoPrediction") == 1 && Count(noSelf, "Right") == 0 && Count(noSelf, "Wrong") == 0);
        var agree = Fixture(true);
        agree.MyExtendedFastaItems[0].Evidence!.Subfamily = "Alpha type";
        agree.MyExtendedFastaItems.Last().Evidence!.Pfam.RemoveAll(d => d.Accession == "domain-b");
        agree.MyExtendedFastaItems.Last().Evidence!.Subfamily = "Alpha type";
        var agreed = Measure(agree);
        Check("J3a leave-one-out counts matching held-out subclasses", Count(agreed, "Eligible") == 2
            && Count(agreed, "Right") == 2 && Count(agreed, "Wrong") == 0 && Count(agreed, "NoPrediction") == 0);
        var disagree = Fixture(true);
        disagree.MyExtendedFastaItems[0].Evidence!.Subfamily = "Alpha type";
        disagree.MyExtendedFastaItems.Last().Evidence!.Subfamily = "Beta type";
        var measured = Measure(disagree);
        Check("J3a leave-one-out records wrong predictions separately from no prediction", Count(measured, "Eligible") == 2
            && Count(measured, "Right") == 0 && Count(measured, "Wrong") == 1 && Count(measured, "NoPrediction") == 1);
        var csvMethod = type?.GetMethod("BuildLeaveOneOutCsv", new[] { typeof(SubclassLeaveOneOutResult) });
        Check("J3a provides a native saved-project leave-one-out command", TestCommandRegistry.Commands
            .Any(command => command.Flag == "--subclass-leave-one-out"));
        string? Csv(object? value) => csvMethod?.Invoke(null, new[] { value }) as string;
        var priorCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            string? englishCsv = Csv(measured);
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            Check("J3a leave-one-out export preserves each outcome and invariant bytes", englishCsv != null
                && englishCsv.Contains("Wrong", StringComparison.Ordinal) && englishCsv.Contains("No prediction", StringComparison.Ordinal)
                && englishCsv == Csv(measured));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = priorCulture; }
        var commandInput = Fixture(true);
        commandInput.MyExtendedFastaItems[0].Evidence!.Subfamily = "Alpha type";
        commandInput.MyExtendedFastaItems.Last().Evidence!.Subfamily = "Beta type";
        MakeAssignmentsCurrent(commandInput);
        string looProject = Path.Combine(folder, "loo-input.fqproj");
        ProjectStore.WriteFqproj(looProject, commandInput, Array.Empty<QuantRow>(), new QuantManifest());
        string looHash = TestFileHash.Upper(looProject), looOutput = Path.Combine(folder, "loo-output");
        int exit = RunSubclassLeaveOneOut(new[] { "--subclass-leave-one-out", looProject, looOutput });
        using (var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(looOutput, "summary.json"))))
            Check("J3a native saved-project measurement exports audited outcomes without changing input", exit == 0
                && summary.RootElement.GetProperty("Eligible").GetInt32() == 2
                && summary.RootElement.GetProperty("Wrong").GetInt32() == 1
                && summary.RootElement.GetProperty("NoPrediction").GetInt32() == 1
                && File.ReadAllText(Path.Combine(looOutput, "subclass-leave-one-out.csv")).Contains("No prediction", StringComparison.Ordinal)
                && TestFileHash.Upper(looProject) == looHash);
        bool existingRefused = false;
        try { RunSubclassLeaveOneOut(new[] { "--subclass-leave-one-out", looProject, looOutput }); }
        catch (IOException) { existingRefused = true; }
        Check("J3a native measurement refuses an existing output directory", existingRefused);

        var staleConsumers = Fixture(false); MakeAssignmentsCurrent(staleConsumers);
        var staleLabels = Candidate(staleConsumers);
        commitMethod!.Invoke(null, new[] { staleConsumers, staleLabels });
        staleConsumers.MyExtendedFastaItems[0].Evidence!.Keywords.Add("changed after subclass computation");
        var staleManifest = new QuantManifest { FamilySubclassParts = new FamilySubclassPartsResult(),
            FamilyRunShares = new FamilyRunSharesResult { Families = new() { new() { FamilyUserId = 11, MeanShare = .125 } } } };
        var emptyAttribution = FamilyEnvelopeQuant.AttributePeptides(Array.Empty<NativePeptideQuant>(), new PeptideFamilyAttribution());
        Check("J3a stale subclass labels cannot feed the quantitation adapter or retain prior parts",
            !FamilyCompositionStudy.StoreSubclassesFromCuration(staleManifest, emptyAttribution, new CurationHomologyHits(), staleConsumers)
            && staleManifest.FamilySubclassParts == null);
        var staleReport = FamilyQuant.Report.ReportPanelSections.BuildFromResults(staleConsumers, staleManifest);
        var staleReportText = JsonSerializer.Serialize(staleReport.SelectMany(s => s.Blocks).Select(b => b switch
        {
            FamilyQuant.Report.ReportParagraph p => p.Text,
            FamilyQuant.Report.ReportTable t => JsonSerializer.Serialize(t.Rows),
            _ => "",
        }));
        Check("J3a stale labels cannot map report observations and are reported unavailable",
            !staleReportText.Contains("0.125", StringComparison.Ordinal)
            && staleReportText.Contains("out of date", StringComparison.OrdinalIgnoreCase));
        string staleAudit = PanelAuditCsv.BuildCsv(staleConsumers);
        Check("J3a audit does not export stale subclass labels as current", staleAudit.Contains("out of date", StringComparison.OrdinalIgnoreCase)
            && !staleAudit.Contains("Source evidence matches the user-defined subclass rule", StringComparison.Ordinal));
        var staleOnlySubclass = Fixture(false);
        staleOnlySubclass.FamilyPanel!.Families[0].DisplayName = "User family";
        MakeAssignmentsCurrent(staleOnlySubclass);
        var freshLabels = Candidate(staleOnlySubclass);
        commitMethod.Invoke(null, new[] { staleOnlySubclass, freshLabels });
        staleOnlySubclass.ProteinSubclassLabels![0].SubclassKey = "altered";
        var validFamilyReport = FamilyQuant.Report.ReportPanelSections.BuildFromResults(staleOnlySubclass, staleManifest);
        string validFamilyText = JsonSerializer.Serialize(validFamilyReport.SelectMany(s => s.Blocks).Select(b => b switch
        {
            FamilyQuant.Report.ReportParagraph p => p.Text,
            FamilyQuant.Report.ReportTable t => JsonSerializer.Serialize(t.Rows),
            _ => "",
        }));
        Check("J3a report keeps current family observations while marking only subclass labels stale",
            PanelAssignmentEvidenceStore.Current(staleOnlySubclass) != null && !SubclassAssignment.IsCurrent(staleOnlySubclass)
            && validFamilyText.Contains("0.125", StringComparison.Ordinal) && validFamilyText.Contains("out of date", StringComparison.OrdinalIgnoreCase));
        string familyAudit = PanelAuditCsv.BuildCsv(staleOnlySubclass);
        Check("J3a audit keeps current family assignment while marking subclass labels stale", familyAudit.Contains("User family", StringComparison.Ordinal)
            && familyAudit.Contains("out of date", StringComparison.OrdinalIgnoreCase) && !familyAudit.Contains("altered", StringComparison.Ordinal));

        EnsureApplication();
        bool RunRunner(ClusterGenerationControl host)
        {
            try
            {
                var task = SubclassAssignmentRunner.RecomputeSubclassesAsync(host);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(30))
                {
                    PumpDispatcher(); System.Threading.Thread.Sleep(5);
                }
                if (!task.IsCompleted) throw new TimeoutException("Subclass runner did not finish.");
                return task.GetAwaiter().GetResult();
            }
            catch (NotImplementedException) { return false; }
        }
        var native = Fixture(false); MakeAssignmentsCurrent(native);
        var screen = new ClusterGenerationControl(native);
        bool changed = false; screen.DocumentChanged += (_, _) => changed = true;
        bool ran = RunRunner(screen);
        Check("J3a native runner commits current labels with one Undo and document notification", ran
            && SubclassAssignment.IsCurrent(native) && screen.ViewModel.UndoStack.Count == 1 && changed && !screen.Operations.IsBusy);
        if (screen.ViewModel.CanUndo) screen.ViewModel.Undo();
        Check("J3a native runner's Undo restores the previous subclass state", ran && native.ProteinSubclassLabels == null);
        var cancelFile = Fixture(false); MakeAssignmentsCurrent(cancelFile);
        var cancelScreen = new ClusterGenerationControl(cancelFile);
        bool requestedCancel = false;
        System.ComponentModel.PropertyChangedEventHandler cancel = (_, _) =>
        {
            if (!requestedCancel && cancelScreen.Operations.IsBusy) { requestedCancel = true; cancelScreen.Operations.Cancel(); }
        };
        cancelScreen.Operations.PropertyChanged += cancel;
        bool cancelResult;
        try { cancelResult = RunRunner(cancelScreen); }
        finally { cancelScreen.Operations.PropertyChanged -= cancel; }
        Check("J3a native runner honors shared Cancel without publication or Undo", requestedCancel && !cancelResult
            && cancelFile.ProteinSubclassLabels == null && cancelScreen.ViewModel.UndoStack.Count == 0 && !cancelScreen.Operations.IsBusy);
    }
}
