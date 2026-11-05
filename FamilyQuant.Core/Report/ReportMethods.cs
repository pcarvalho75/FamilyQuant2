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
                string? name = panel.Families.FirstOrDefault(f => f.Key == key)?.DisplayName;
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
