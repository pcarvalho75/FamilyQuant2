using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using FamilyQuant.Evidence;

namespace FamilyQuant.Report;

/// <summary>Reads project facts only. Missing provenance stays explicitly unavailable.</summary>
public static class ReportMethods
{
    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static ReportSection Build(FamilyClusterFile curation, QuantManifest manifest,
        ReportDetails? details = null, ReportInputProvenance? facts = null)
    {
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(manifest);
        details ??= curation.ReportDetails;
        facts ??= curation.ReportInputProvenance;
        var blocks = new List<ReportBlock>();
        var rows = new List<IReadOnlyList<string>>();
        void Add(string key, string? value) => rows.Add(new[] { key, Value(value) });
        Add("Sample", details?.SampleDescription);
        Add("Workflow", details?.Workflow);
        Add("Database kind", details?.DatabaseKind);
        Add("Report family names", curation.ReportFamilyNames == ReportFamilyNameChoice.OtherLanguageName ? "Other-language name, with Name fallback" : "Name");
        Add("Database input", facts?.DatabaseFileName);
        Add("Chosen database file SHA-256", facts?.DatabaseFileSha256);
        Add("SEPro input", facts?.SeproFileName);
        Add("Database source SHA-256", curation.SourceSha256);
        Add("Source database records at clustering", curation.SourceSequenceCount > 0 ? Number(curation.SourceSequenceCount) : null);
        var proteins = curation.MyExtendedFastaItems;
        Add("Records in the saved database", Number(proteins.Count));
        Add("Targets (including target contaminants)", Number(proteins.Count(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier))));
        Add("Decoys", Number(proteins.Count(p => SequenceLabels.IsDecoy(p.SequenceIdentifier))));
        Add("Target contaminants", Number(proteins.Count(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier) && SequenceLabels.IsContaminant(p.SequenceIdentifier))));
        Add("Decoy contaminants", Number(proteins.Count(p => SequenceLabels.IsDecoy(p.SequenceIdentifier) && SequenceLabels.IsContaminant(p.SequenceIdentifier))));
        Add("Decoy label", SequenceLabels.DecoyPrefix);
        Add("Contaminant label", SequenceLabels.ContaminantPrefix);
        Add("Label rule", "Exact-case prefix; unwrap one decoy prefix before checking the contaminant prefix.");
        Add("Clustering identity (%)", curation.ClusteringIdentity > 0 ? Number(curation.ClusteringIdentity) : null);
        Add("Clustering method", curation.ClusteringMethod);
        Add("Domain source", curation.DomainSource);
        blocks.Add(new ReportTable("methods-project", "Project and database", new[] { "Fact", "Stored value" }, rows,
            "Database classes are counted from the saved identifiers using the program's label rules. Source records and saved records can differ after curation."));

        var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in manifest.ScientificInputFingerprints) inputs[input.Key] = input.Value;
        foreach (var input in manifest.DeNovoInputs) inputs[input.FilePath] = input.Sha256;
        if (!string.IsNullOrWhiteSpace(facts?.SearchParameterFileName)) inputs[facts.SearchParameterFileName] = facts.SearchParameterSha256;
        if (!string.IsNullOrWhiteSpace(facts?.DatabaseFileName)) inputs[facts.DatabaseFileName] = facts.DatabaseFileSha256;
        if (curation.FamilyPanel?.Import is { } panelInput) inputs[panelInput.SourceName] = panelInput.SourceSha256;
        blocks.Add(new ReportTable("methods-inputs", "Scientific inputs", new[] { "Input name or path", "SHA-256" },
            inputs.Select(p => (IReadOnlyList<string>)new[] { p.Key, Value(p.Value) }).ToArray(),
            inputs.Count == 0 ? "Input names and SHA-256 values were not recorded in this project." : "Content fingerprints recorded by the program for the inputs used in this project."));

        if (curation.FamilyPanel?.SourceNotes is { Count: > 0 } sourceNotes)
            blocks.Add(new ReportTable("methods-panel-source-notes", "Panel source notes", new[] { "Source sheet", "Retained text" },
                sourceNotes.Select(note => (IReadOnlyList<string>)new[] { note.SheetName,
                    "Original text and cell addresses are kept with the panel and shown in the panel editor." }).ToArray(),
                "Source notes are kept as text; they do not define families, matching rules or measured quantities."));

        rows = new List<IReadOnlyList<string>>();
        Add("Search parameter input", facts?.SearchParameterFileName);
        if (facts?.SearchParameters.Count > 0)
            foreach (var setting in facts.SearchParameters) Add(setting.Key, setting.Value);
        else Add("Search settings", null);
        Add("Extraction ppm", manifest.Ppm > 0 ? Number(manifest.Ppm) : null);
        Add("MS1 tolerance", manifest.Ms1Tolerance > 0 ? Number(manifest.Ms1Tolerance) : null);
        Add("Isotope ceiling", manifest.IsotopeCount > 0 ? Number(manifest.IsotopeCount) : null);
        Add("Minimum points per cluster/isotope", manifest.MinPointsForQuant > 0 ? Number(manifest.MinPointsForQuant) : null);
        Add("Derived fractions method", manifest.DerivedMethod);
        Add("De novo union rule", manifest.DeNovoUnionResolutionMethod);
        Add("Same-scan de novo conflict rule", manifest.PatternLabWinsScanConflicts.HasValue
            ? manifest.PatternLabWinsScanConflicts.Value ? DeNovo.SpectrumComparisonTable.PatternLabConflictRule : DeNovo.SpectrumComparisonTable.KeepConflictingCallsRule
            : null);
        Add("Family mapping method", manifest.FamilyMapping?.Method);
        Add("Family mapping minimum identity (fraction)", manifest.FamilyMapping == null ? null : Number(manifest.FamilyMapping.MinIdentity));
        Add("Family mapping protein-set SHA-256", manifest.FamilyMapping?.ProteinSetSha256);
        Add("Quantitation application version", manifest.AppVersion);
        blocks.Add(new ReportTable("methods-rules", "Search and quantitation rules", new[] { "Setting", "Stored value" }, rows,
            "These are the settings recorded with the computation. Missing settings are not replaced by current defaults."));

        if (curation.FamilyPanel is { } panel && panel.EvidenceLinks.Count > 0)
        {
            string definition = PanelEvidenceClassifier.DefinitionFingerprint(panel);
            string Context(string? architecture)
            {
                if (string.IsNullOrEmpty(architecture)) return "Any observed combination";
                try { return string.Join(" + ", JsonSerializer.Deserialize<string[]>(architecture) ?? Array.Empty<string>()); }
                catch (JsonException) { return architecture; }
            }
            string Family(string? key)
            {
                if (key == null) return "Leave outside panel";
                var family = panel.Families.FirstOrDefault(f => f.Key == key);
                string? name = family == null ? null : ReportFamilyNames.DisplayName(panel, family, curation.ReportFamilyNames);
                return string.IsNullOrWhiteSpace(name) ? key : name;
            }
            var links = panel.EvidenceLinks.OrderBy(link => link.SourceFamily, StringComparer.Ordinal)
                .ThenBy(link => link.ArchitectureKey, StringComparer.Ordinal).Select(link => (IReadOnlyList<string>)new[]
                {
                    link.SourceFamily ?? string.Empty, Context(link.ArchitectureKey),
                    Family(link.PanelFamilyKey),
                    link.Source ?? string.Empty,
                    link.PanelDefinitionFingerprint != definition ? "Panel definition changed" :
                        link.PanelFamilyKey != null && !panel.Families.Any(f => f.Key == link.PanelFamilyKey) ? "Panel family missing" : "Confirmed",
                }).ToArray();
            blocks.Add(new ReportTable("methods-panel-links", "Saved panel link choices",
                new[] { "Source family", "Domain combination", "Panel family choice", "User note", "Status" }, links,
                "These are the user's saved choices and notes. A changed panel definition makes a choice out of date; a choice does not prove an assignment was applied."));
        }

        if (curation.DatabaseUpdates is { Count: > 0 } updates)
        {
            blocks.Add(new ReportParagraph("methods-database-update", "Database updates retain existing curation and place additions without merging existing families; exact sources, matching/placement methods and name choices are retained in the saved update records."));
            var updateRows = updates.Select(update => (IReadOnlyList<string>)new[]
            {
                update.UpdatedAtUtc == default ? "Not recorded" : update.UpdatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Value(update.Previous.FileName), Value(update.Previous.Sha256), Value(update.New.FileName), Value(update.New.Sha256), Value(update.Method),
                Number(update.Counts.Added), Number(update.Counts.Removed), Number(update.Counts.SequenceChanged), Number(update.Counts.NamesKept)
            }).ToArray();
            blocks.Add(new ReportTable("methods-database-updates", "Saved database updates",
                new[] { "Updated at UTC", "Previous database", "Previous SHA-256", "New database", "New SHA-256", "Recorded method", "Targets added", "Targets removed", "Target sequences changed", "Names kept" },
                updateRows, "Counts refer to targets; decoy changes remain in the update record and per-row CSV. Values come from saved records; current defaults do not replace missing methods or source hashes."));
        }

        if (curation.EvidenceRecipe?.ManualChoices is { Count: > 0 } handChoices)
        {
            var recipe = curation.EvidenceRecipe;
            var assignments = recipe.Assignments.Where(a => a.Tier == EvidenceTier.AddedByHand).ToDictionary(a => a.ProteinIndex);
            var handRows = handChoices.OrderBy(c => c.ProteinIndex).Select(choice =>
            {
                bool same = choice.ProteinIndex >= 0 && choice.ProteinIndex < curation.MyExtendedFastaItems.Count;
                var protein = same ? curation.MyExtendedFastaItems[choice.ProteinIndex] : null;
                same = same && protein!.SequenceIdentifier == choice.Identifier && ManualFamilyChoices.SequenceHash(protein.Sequence ?? "") == choice.SequenceSha256;
                string status = !same ? "Source changed" : !choice.Selected ? "Not selected" : assignments.TryGetValue(choice.ProteinIndex, out var assignment)
                    && protein!.UserID == assignment.FamilyId ? "Applied" : "Grouping changed";
                string name = recipe.Families.FirstOrDefault(f => f.Family == choice.Family)?.Name ?? choice.Name;
                // The warnings shown when the choice was applied, as stored with the hand assignment.
                string warnings = !assignments.TryGetValue(choice.ProteinIndex, out var applied) || applied.Identifier != choice.Identifier ? ""
                    : applied.Flags == null ? "Not recorded" : applied.Flags.Count == 0 ? "None"
                    : string.Join(" | ", applied.Flags.Select(f => f.Message.Length > 0 ? f.Message : f.Code));
                return (IReadOnlyList<string>)new[] { choice.Identifier, name, choice.Note ?? "", status, warnings };
            }).ToArray();
            blocks.Add(new ReportTable("methods-hand-families", "Saved hand-added family choices",
                new[] { "Protein", "Chosen family", "User note", "Status", "Warnings at the choice" }, handRows,
                "These memberships are explicit user choices, not automatic evidence. The grouping recipe retains notes and the original automatic evidence; status is checked against the current identifier, sequence and family. Warnings at the choice are the flags the review showed when the choice was applied."));
            // Per family: how many current members are there because the user added them by hand.
            var handMembers = assignments.Values.Where(a => a.ProteinIndex >= 0 && a.ProteinIndex < curation.MyExtendedFastaItems.Count
                    && curation.MyExtendedFastaItems[a.ProteinIndex].SequenceIdentifier == a.Identifier
                    && curation.MyExtendedFastaItems[a.ProteinIndex].UserID == a.FamilyId)
                .GroupBy(a => a.FamilyId).OrderBy(g => g.Key).ToArray();
            if (handMembers.Length > 0)
            {
                var targets = curation.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier ?? string.Empty)).ToArray();
                var familyNames = FamilyNaming.Names(targets);
                var countRows = handMembers.Select(g => (IReadOnlyList<string>)new[]
                {
                    familyNames.GetValueOrDefault(g.Key, string.Empty), Number(g.Key),
                    Number(targets.Count(p => p.UserID == g.Key)), Number(g.Count()),
                }).ToArray();
                blocks.Add(new ReportTable("methods-hand-family-counts", "Members added by hand, per family",
                    new[] { "Family", "Family number", "Members", "Added by hand" }, countRows,
                    "Members counts the family's current target proteins; Added by hand counts those that are members because of a saved hand choice."));
            }
            blocks.Add(new ReportParagraph("methods-hand-quantified", "Proteins added by hand are quantified as full members of the chosen family."));
        }

        var deNovoRows = manifest.DeNovoInputs.OrderBy(i => i.SourceId, StringComparer.Ordinal).ThenBy(i => i.FilePath, StringComparer.Ordinal)
            .Select(i => (IReadOnlyList<string>)new[] { i.SourceDisplayName, i.ConfidenceField, Number(i.MinimumConfidence),
                Number(i.SourcePriority), Number(i.RawRows), Number(i.RowsPassingSourceFilter), i.FilePath }).ToArray();
        blocks.Add(new ReportTable("methods-denovo", "De novo sources and cutoffs",
            new[] { "Source", "Score field", "Minimum score", "Priority", "Input rows", "Passing rows", "Input" }, deNovoRows,
            "Each confidence cutoff belongs to its source's native score scale. Source filtering is not an identification error estimate."));
        rows = new List<IReadOnlyList<string>>();
        foreach (var source in manifest.DeNovoSourceScoreDistributions.OrderBy(i => i.SourceId, StringComparer.Ordinal))
        {
            Add(source.SourceDisplayName + " minimum length", Number(source.MinLength));
            Add(source.SourceDisplayName + " consecutive-residue requirement", Number(source.StretchLength));
            Add(source.SourceDisplayName + " local confidence floor", Number(source.StretchFloor));
        }
        if (manifest.DeNovoOverlap is { } overlap)
        {
            Add("Overlap source scope", overlap.SourceScopeLabel);
            Add("Overlap source filters", overlap.SourceFilterDescription);
            Add("Overlap precursor tolerance (ppm)", Number(overlap.PrecursorTolerancePpm));
        }
        blocks.Add(new ReportTable("methods-denovo-gates", "De novo length and local confidence", new[] { "Gate", "Stored value" }, rows,
            rows.Count == 0 ? "Length and local-confidence gates were not recorded in this project." : "Source gates recorded with the stored score distributions and overlap analysis."));
        blocks.Add(new ReportParagraph("methods-unavailable", "An unavailable measurement is not a zero. Abundance and the number of supported database entries describe different properties. User-written interpretation appears separately."));
        rows = new List<IReadOnlyList<string>>();
        if (manifest.FamilyRunShares is { } runStudy) Add("Per-run composition and statistics", runStudy.Method);
        if (manifest.EffectivePanelFamilies is { } effective) Add("Effective panel-family studies", effective.Method
            + " Source membership SHA-256: " + effective.MembershipSha256 + ". Panel-map SHA-256: " + effective.PanelMapSha256 + ".");
        if (manifest.FamilyProteinCounts is { } proteinStudy) Add("Database-protein diversity", proteinStudy.Method);
        if (manifest.FamilySubclassParts is { } subclassStudy) Add("Pooled subclass allocation", subclassStudy.Method);
        if (manifest.FamilyDomainParts is { } domainStudy) Add("Pooled domain and mature-product allocation", domainStudy.Method);
        if (manifest.FamilySensitivity is { } sensitivityStudy) Add("Stored sensitivity comparison", sensitivityStudy.Method);
        if (rows.Count > 0)
            blocks.Add(new ReportTable("methods-stored-studies", "Family study rules", new[] { "Study", "Stored method" }, rows,
                "Headline composition uses the mean of per-run shares, sample SD and CV. Within-family subclass, domain and mature-product parts use all runs pooled. Pooled within-family fractions are never multiplied by the per-run mean. Greedy protein cover is an estimate over database entries, distinct from gene or proteoform counts."));
        return new ReportSection(ReportSectionIds.Methods, "Methods", blocks);
    }
}
