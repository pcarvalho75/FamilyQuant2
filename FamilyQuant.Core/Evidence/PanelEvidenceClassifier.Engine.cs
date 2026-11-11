using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Evidence;

/// <summary>Panel families are user data. Reviewed entries connect source annotations and domain
/// combinations to that data. Equal domain coverage requires a saved user choice, never a vote or a name rule.</summary>
public static partial class PanelEvidenceClassifier
{
    public const int MaximumLinkNoteLength = 512;

    public static bool IsValidLinkNote(string? note) => note == null || note.Length <= MaximumLinkNoteLength
        && !note.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    internal sealed record Resolution(string? Family, string Code, string Reason, IReadOnlyList<string>? LinkKeys = null, bool Confirmed = false);

    public static List<NeighbourReference> References(FamilyClusterFile file, EvidenceClassificationOptions options) =>
        NearestReviewed.References(file, e => EvidenceClassifier.IsInScope(e, options.ScopeTerms), options.ScopeTerms)
            .Where(r => !SequenceLabels.IsContaminant(r.Identifier) && !SequenceLabels.IsDecoy(r.Identifier)).ToList();

    public static EvidenceProposal Propose(FamilyClusterFile file, EvidenceClassificationOptions options,
        NeighbourSearch? search = null, IReadOnlyList<NeighbourReference>? references = null, FamilyPanel? panel = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        EvidenceClassifier.Validate(options);
        panel = FamilyPanelValidation.Clone(panel ?? file.FamilyPanel
            ?? throw new InvalidOperationException("Import a family panel before proposing panel families."));
        FamilyPanelValidation.Validate(panel);
        var known = References(file, options);
        var knownByKey = known.ToDictionary(ReferenceKey, StringComparer.Ordinal);
        var used = references == null ? known : references.Select(r => knownByKey.TryGetValue(ReferenceKey(r), out var current)
            ? current : throw new InvalidOperationException("A panel reference is not current reviewed evidence in the user scope.")).ToList();
        if (used.Select(ReferenceKey).Distinct(StringComparer.Ordinal).Count() != used.Count)
            throw new ArgumentException("Reviewed references must be distinct.", nameof(references));
        search ??= file.EvidenceSource?.NearestReviewed;
        if (search != null)
        {
            if (search.TieMethod != NearestReviewed.ReferenceTieMethod)
                throw new InvalidOperationException("Run the reviewed-neighbour search again to record all equally close references for panel assignment.");
            if (search.MinQueryAlignedPercent != options.MinQueryAlignedPercent)
                throw new InvalidOperationException("Run the reviewed-neighbour search with the current query-residue coverage gate.");
            EvidenceApply.ValidateSearch(file, search, used, options, null, allowPanelReferences: true);
        }
        var neighbours = search?.ByQuery() ?? new Dictionary<string, NearestNeighbour>(StringComparer.Ordinal);
        var links = BuildLinks(panel, used, options);
        var context = new ClassificationContext(panel, links);
        var targets = file.MyExtendedFastaItems.Select((protein, index) => (protein, index))
            .Where(row => !NearestReviewed.IsDecoy(row.protein)).ToArray();
        if (targets.Select(r => r.protein.SequenceIdentifier).Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw new ArgumentException("Target identifiers must be unique.", nameof(file));
        var facts = new List<PanelProteinAssignment>(targets.Length);
        var rows = targets.Select(row => Classify(row.protein, row.index, options, context,
            neighbours.GetValueOrDefault(row.protein.SequenceIdentifier), used, search != null, trace: facts.Add)).ToArray();
        return new EvidenceProposal
        {
            SourceFingerprint = EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems),
            Options = EvidenceApply.Clone(options), Proteins = rows,
            PanelAssignments = facts,
            Families = rows.Where(r => r.Family.Length > 0).GroupBy(r => r.Family, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(group => new EvidenceFamilyProposal
                {
                    Family = group.Key,
                    SuggestedName = panel.Families.Single(f => f.Key == group.Key).DisplayName is { Length: > 0 } name ? name : group.Key,
                    Proteins = group.Count(), ReviewedProteins = group.Count(r => file.MyExtendedFastaItems[r.ProteinIndex].Evidence?.Reviewed == true),
                }).ToArray(),
            PanelContext = new PanelEvidenceContext
            {
                SourcePanelFingerprint = file.FamilyPanel == null ? "" : PanelDerivedDataCurrentness.FamilyFingerprint(file.FamilyPanel),
                AppliedPanelFingerprint = PanelDerivedDataCurrentness.FamilyFingerprint(panel),
                ReferenceFingerprint = Hash(used.Select(r => new { r.Accession, r.Identifier, r.Family, r.Sequence, r.Evidence })),
                SearchFingerprint = Hash(search), Links = links,
            },
        };
    }

    // Hand-created rows are not reviewed-source candidates; link stamps use the unextended source panel.
    public static string DefinitionFingerprint(FamilyPanel panel) => Hash(panel.Families
        .Where(f => f.SourceDecision != "Added by hand").OrderBy(f => f.Key, StringComparer.Ordinal)
        .Select(f => new { f.Key, f.Decision, f.MergeTargetFamilyKey,
            Domains = f.Pfam.Select(d => d.Accession).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal) }));

    public static string ArchitectureKey(ProteinEvidence? evidence) => JsonSerializer.Serialize(EvidenceDomains.Ordered(evidence));
    public static string LinkKey(string sourceFamily, string architectureKey) => JsonSerializer.Serialize(new[] { sourceFamily.Trim().ToUpperInvariant(), architectureKey });
    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string ReferenceKey(NeighbourReference reference) => JsonSerializer.Serialize(new[] { reference.Accession, reference.Identifier, reference.Family, reference.Sequence });
    internal static string[] SourceFamilies(ProteinEvidence? evidence) => evidence == null ? Array.Empty<string>()
        : ReferencePanelFetcher.FamiliesOf(evidence).Select(f => f.Trim()).Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<PanelFamilyLinkProposal> BuildLinks(FamilyPanel panel,
        IReadOnlyList<NeighbourReference> references, EvidenceClassificationOptions options) => BuildLinks(panel, references, options, true);

    private static IReadOnlyList<PanelFamilyLinkProposal> BuildLinks(FamilyPanel panel,
        IReadOnlyList<NeighbourReference> references, EvidenceClassificationOptions options, bool usePfamCoverage)
    {
        FamilyPanelValidation.Validate(panel);
        EvidenceClassifier.Validate(options);
        string definition = DefinitionFingerprint(panel);
        var groups = new Dictionary<string, LinkSupport>(StringComparer.Ordinal);
        var candidateCache = new Dictionary<string, DomainCandidates>(StringComparer.Ordinal);
        var namedFamilies = panel.Families.SelectMany(f => f.UniProtFamilies.Select(name =>
                (Name: name.Trim(), Key: FamilyPanelValidation.ResolveFamilyKey(panel, f.Key) ?? f.Key)))
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,
                g => g.Select(row => row.Key).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
        foreach (NeighbourReference reference in references)
        {
            if (reference.Evidence?.Reviewed != true || !EvidenceClassifier.IsInScope(reference.Evidence, options.ScopeTerms)
                || SequenceLabels.IsContaminant(reference.Identifier) || SequenceLabels.IsDecoy(reference.Identifier)) continue;
            string architecture = ArchitectureKey(reference.Evidence);
            if (!candidateCache.TryGetValue(architecture, out var candidates))
                candidateCache[architecture] = candidates = Candidates(panel, EvidenceDomains.Ordered(reference.Evidence));
            var sourceBefore = new HashSet<string>(StringComparer.Ordinal);
            var sourceAfter = new HashSet<string>(StringComparer.Ordinal);
            string[] sourceFamilies = SourceFamilies(reference.Evidence);
            foreach (string family in sourceFamilies)
            {
                string[] named = namedFamilies.TryGetValue(family, out var owners)
                    ? candidates.All.Where(owners.Contains).ToArray() : Array.Empty<string>();
                string[] before = named.Length > 0 ? named : candidates.All;
                string[] after = named.Length > 0 ? named : candidates.Coverage;
                Add(family, "", candidates.All, before, after, reference.Accession);
                if (architecture != "[]") Add(family, architecture, candidates.All, before, after, reference.Accession);
                sourceBefore.UnionWith(before); sourceAfter.UnionWith(after);
            }
            if (architecture != "[]") Add("", architecture, candidates.All,
                sourceFamilies.Length > 0 ? sourceBefore : candidates.All,
                sourceFamilies.Length > 0 ? sourceAfter : candidates.Coverage, reference.Accession);
        }
        var confirmed = panel.EvidenceLinks.GroupBy(c => LinkKey(c.SourceFamily, c.ArchitectureKey), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.Single() : throw new ArgumentException("A panel link has more than one saved choice."), StringComparer.Ordinal);
        return groups.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            string[] candidates = pair.Value.Candidates.Order(StringComparer.Ordinal).ToArray();
            confirmed.TryGetValue(pair.Key, out var choice);
            bool current = choice != null && choice.PanelDefinitionFingerprint == definition
                && (choice.PanelFamilyKey == null || candidates.Contains(choice.PanelFamilyKey, StringComparer.Ordinal));
            bool coverageResolved = !current && usePfamCoverage && pair.Value.BeforeCoverageCandidates.Count > 1
                && pair.Value.CoverageCandidates.Count == 1;
            if (!current) candidates = (usePfamCoverage ? pair.Value.CoverageCandidates : pair.Value.BeforeCoverageCandidates)
                .Order(StringComparer.Ordinal).ToArray();
            return new PanelFamilyLinkProposal
            {
                Key = pair.Key, SourceFamily = pair.Value.SourceFamily, ArchitectureKey = pair.Value.Architecture,
                CandidateFamilyKeys = candidates, SupportingAccessions = pair.Value.Accessions.Order(StringComparer.Ordinal).ToArray(),
                IsConfirmed = current, SelectedFamilyKey = current ? choice!.PanelFamilyKey : candidates.Length == 1 ? candidates[0] : null,
                IsPfamCoverageResolved = coverageResolved,
                SourceNote = current ? choice!.Source ?? string.Empty : string.Empty,
            };
        }).ToArray();

        void Add(string family, string architecture, IEnumerable<string> candidates,
            IEnumerable<string> before, IEnumerable<string> after, string accession)
        {
            string key = LinkKey(family, architecture);
            if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new(family, architecture));
            group.Candidates.UnionWith(candidates);
            group.BeforeCoverageCandidates.UnionWith(before);
            group.CoverageCandidates.UnionWith(after);
            group.Accessions.Add(accession);
        }
    }

    private sealed class LinkSupport(string sourceFamily, string architecture)
    {
        internal string SourceFamily { get; } = sourceFamily;
        internal string Architecture { get; } = architecture;
        internal HashSet<string> Candidates { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> BeforeCoverageCandidates { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> CoverageCandidates { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> Accessions { get; } = new(StringComparer.Ordinal);
    }

    private sealed record DomainCandidates(string[] All, string[] Coverage);

    private static DomainCandidates Candidates(FamilyPanel panel, IReadOnlyCollection<string> observed)
    {
        if (observed.Count == 0) return new(Array.Empty<string>(), Array.Empty<string>());
        var scores = panel.Families.Select(f =>
        {
            string[] domains = f.Pfam.Select(d => d.Accession).Distinct(StringComparer.Ordinal).ToArray();
            return (Family: f, Count: domains.Count(observed.Contains), Total: domains.Length);
        }).ToArray();
        int best = scores.Select(s => s.Count).DefaultIfEmpty().Max();
        if (best == 0) return new(Array.Empty<string>(), Array.Empty<string>());
        var tied = scores.Where(s => s.Count == best).ToArray();
        // The numerator is equal within this tie, so the smallest denominator covers most.
        int complete = tied.Min(s => s.Total);
        return new(tied.Select(s => FamilyPanelValidation.ResolveFamilyKey(panel, s.Family.Key) ?? s.Family.Key)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), tied.Where(s => s.Total == complete)
            .Select(s => FamilyPanelValidation.ResolveFamilyKey(panel, s.Family.Key) ?? s.Family.Key)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    public static FamilyPanel ConfirmLinks(FamilyPanel panel, IReadOnlyList<PanelFamilyLinkProposal> proposals,
        IReadOnlyList<PanelFamilyLinkSelection> selections)
    {
        FamilyPanelValidation.Validate(panel);
        var copy = FamilyPanelValidation.Clone(panel);
        var known = proposals.ToDictionary(p => p.Key, StringComparer.Ordinal);
        if (selections.Select(s => s.LinkKey).Distinct(StringComparer.Ordinal).Count() != selections.Count)
            throw new ArgumentException("A source context may be confirmed only once per update.", nameof(selections));
        foreach (var selection in selections)
        {
            if (!IsValidLinkNote(selection.Source))
                throw new ArgumentException("A link note must be a single line of at most "
                    + MaximumLinkNoteLength.ToString(CultureInfo.InvariantCulture) + " characters.", nameof(selections));
            if (!known.TryGetValue(selection.LinkKey, out var link)
                || (selection.PanelFamilyKey != null && !link.CandidateFamilyKeys.Contains(selection.PanelFamilyKey, StringComparer.Ordinal)))
                throw new ArgumentException("A selected link is outside its evidence-derived candidates.", nameof(selections));
            copy.EvidenceLinks.RemoveAll(c => LinkKey(c.SourceFamily, c.ArchitectureKey) == selection.LinkKey);
            copy.EvidenceLinks.Add(new FamilyPanelEvidenceLink
            {
                SourceFamily = link.SourceFamily, ArchitectureKey = link.ArchitectureKey,
                PanelFamilyKey = selection.PanelFamilyKey, PanelDefinitionFingerprint = DefinitionFingerprint(panel),
                SupportingAccessions = link.SupportingAccessions.ToList(),
                Source = selection.Source ?? string.Empty,
            });
        }
        copy.EvidenceLinks = copy.EvidenceLinks.OrderBy(c => LinkKey(c.SourceFamily, c.ArchitectureKey), StringComparer.Ordinal).ToList();
        return copy;
    }

    internal sealed class ClassificationContext
    {
        internal FamilyPanel Panel { get; }
        internal Dictionary<string, PanelFamilyLinkProposal> Links { get; }
        private readonly Dictionary<string, HashSet<string>> domainOwners;

        internal ClassificationContext(FamilyPanel panel, IReadOnlyList<PanelFamilyLinkProposal> links)
        {
            Panel = panel;
            Links = links.ToDictionary(l => l.Key, StringComparer.Ordinal);
            domainOwners = panel.Families.SelectMany(f => f.Pfam.Select(d => (Domain: d.Accession,
                    Family: FamilyPanelValidation.ResolveFamilyKey(panel, f.Key) ?? f.Key)))
                .GroupBy(d => d.Domain, StringComparer.Ordinal).ToDictionary(g => g.Key,
                    g => g.Select(d => d.Family).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        }

        internal Resolution Resolve(ProteinEvidence? evidence)
        {
            string[] families = SourceFamilies(evidence);
            if (families.Length == 0) families = new[] { "" };
            string architecture = ArchitectureKey(evidence);
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            var used = new List<string>();
            bool confirmed = false;
            Resolution Result(string? family, string code, string reason) => new(family, code, reason, used.ToArray(), confirmed);
            foreach (string family in families)
            {
                Links.TryGetValue(LinkKey(family, architecture), out var link);
                if (link?.IsPfamCoverageResolved == true && family.Length > 0
                    && Links.TryGetValue(LinkKey(family, ""), out var saved) && saved.IsConfirmed) link = saved;
                if ((link == null || link.CandidateFamilyKeys.Count == 0 && !link.IsConfirmed) && family.Length > 0)
                    Links.TryGetValue(LinkKey(family, ""), out link);
                if (link == null || (!link.IsConfirmed && link.CandidateFamilyKeys.Count == 0))
                    return Result(null, "panel_link_unsupported", "Reviewed evidence does not connect this source context to a panel family.");
                used.Add(link.Key); confirmed |= link.IsConfirmed;
                if (link.NeedsConfirmation)
                    return Result(null, "panel_link_ambiguous", "This source family or domain combination links to several panel families; confirm the link once.");
                if (link.SelectedFamilyKey == null)
                    return Result(null, "", "The user chose to leave this source context outside the panel.");
                string? target = FamilyPanelValidation.ResolveFamilyKey(Panel, link.SelectedFamilyKey);
                if (target == null) return Result(null, "", "The panel decision excludes this family.");
                resolved.Add(target);
            }
            if (resolved.Count != 1) return Result(null, "panel_link_conflict", "The source memberships link to different panel families.");
            string assigned = resolved.Single();
            return Conflicts(EvidenceDomains.Ordered(evidence), assigned)
                ? Result(null, "panel_domain_conflict", "The accompanying domains support a different panel context.")
                : Result(assigned, "", "Reviewed source families and domain combinations connect to one panel family.");
        }

        internal bool Conflicts(string[] observed, string family)
        {
            if (observed.Any(domain => domainOwners.TryGetValue(domain, out var owners) && owners.Count == 1 && !owners.Contains(family))) return true;
            string[] candidates = Candidates(Panel, observed).All;
            return candidates.Length > 0 && !candidates.Contains(family, StringComparer.Ordinal);
        }

        internal bool Compatible(string[] observed, string family, ProteinEvidence? reference)
        {
            var expected = Panel.Families.Single(f => f.Key == family).Pfam.Select(d => d.Accession)
                .Concat(EvidenceDomains.Ordered(reference)).ToHashSet(StringComparer.Ordinal);
            return observed.Length > 0 && observed.Any(expected.Contains) && !Conflicts(observed, family);
        }
    }

    internal static EvidenceProteinProposal Classify(FastaItemExtended protein, int index, EvidenceClassificationOptions options,
        ClassificationContext context, NearestNeighbour? neighbour, IReadOnlyList<NeighbourReference> references,
        bool searched, bool maskSourceLabel = false, Action<PanelProteinAssignment>? trace = null)
    {
        bool annotated = EvidenceClassifier.IsInScope(protein.Evidence, options.ScopeTerms);
        string family = "", reason;
        EvidenceTier tier = EvidenceTier.Unassigned;
        var flags = new List<EvidenceFlag>();
        var facts = trace == null ? null : new PanelProteinAssignment
        {
            ProteinIndex = index, Identifier = protein.SequenceIdentifier, FamilyUserId = protein.UserID,
            ObservedPfam = EvidenceDomains.Ordered(protein.Evidence).ToList(),
        };
        void Record(Resolution resolution)
        {
            if (facts == null) return;
            facts.LinkKeys = facts.LinkKeys.Concat(resolution.LinkKeys ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
            if (facts.LinkKeys.Count > 0) facts.EvidenceKind |= PanelAssignmentEvidenceKind.DomainArchitecture;
            if (resolution.Confirmed) facts.EvidenceKind |= PanelAssignmentEvidenceKind.ConfirmedLink;
        }
        void Flag(string code, string message) { if (code.Length > 0) flags.Add(new() { Code = code, Message = message }); }
        if (SequenceLabels.IsContaminant(protein.SequenceIdentifier)) reason = "A database contaminant stays outside panel families.";
        else if (annotated && !maskSourceLabel && SourceFamilies(protein.Evidence).Length > 0)
        {
            Resolution link = context.Resolve(protein.Evidence);
            if (facts != null) { facts.EvidenceKind |= PanelAssignmentEvidenceKind.UniProtFamily; facts.SourceFamilies = SourceFamilies(protein.Evidence).ToList(); }
            Record(link);
            reason = "Source family annotation in the user scope. " + link.Reason;
            if (link.Family != null) { family = link.Family; tier = EvidenceTier.UniProtFamily; }
            else Flag(link.Code, link.Reason);
        }
        else
        {
            NeighbourReference? reference = neighbour == null ? null : references.FirstOrDefault(r => r.Accession == neighbour.Accession
                && r.Identifier == neighbour.Reference && r.Family == neighbour.Family);
            if (facts != null && neighbour != null)
            {
                facts.EvidenceKind |= PanelAssignmentEvidenceKind.NearestReviewed;
                facts.NeighbourAccession = neighbour.Accession; facts.IdentityPercent = neighbour.IdentityPercent;
                facts.QueryCoveragePercent = neighbour.QueryCoveragePercent;
                facts.SourceFamilies = SourceFamilies(reference?.Evidence).ToList();
                facts.ReferencePfam = EvidenceDomains.Ordered(reference?.Evidence).ToList();
            }
            bool valid = neighbour != null && reference?.Evidence?.Reviewed == true
                && EvidenceClassifier.IsInScope(reference.Evidence, options.ScopeTerms)
                && neighbour.Query == protein.SequenceIdentifier && reference.Identifier != protein.SequenceIdentifier
                && NearestReviewed.EntryAccession(protein.SequenceIdentifier) != NearestReviewed.EntryAccession(reference.Accession)
                && (protein.Evidence?.Accession.Length is not > 0 || NearestReviewed.EntryAccession(protein.Evidence.Accession) != NearestReviewed.EntryAccession(reference.Accession))
                && neighbour.QueryLength == protein.Sequence.Length && neighbour.ReferenceLength == reference.Sequence.Length
                && double.IsFinite(neighbour.IdentityPercent) && neighbour.IdentityPercent <= 100
                && neighbour.QueryAlignedResidues is int covered && NearestReviewed.PassesGate(neighbour.IdentityPercent, covered,
                    neighbour.QueryLength, annotated ? options.AnnotatedIdentityPercent : options.UnannotatedIdentityPercent, options.MinQueryAlignedPercent);
            if (!valid)
            {
                reason = neighbour == null ? searched ? "No reviewed neighbour passes the search gate." : "No reviewed-neighbour result is available for this protein."
                    : "The reviewed neighbour fails identity, query-residue coverage, scope, reference provenance or self-exclusion.";
                if (neighbour != null) Flag("neighbour_refused", reason);
            }
            else
            {
                Resolution link = context.Resolve(reference!.Evidence);
                Record(link);
                reason = link.Reason;
                if (link.Family != null && neighbour!.TiedReferences is { Count: > 0 } alternatives)
                {
                    foreach (var alternative in alternatives)
                    {
                        var other = references.FirstOrDefault(r => r.Accession == alternative.Accession && r.Identifier == alternative.Reference && r.Family == alternative.Family);
                        var otherResolution = context.Resolve(other?.Evidence);
                        Record(otherResolution);
                        if (facts != null) facts.SourceFamilies = facts.SourceFamilies.Concat(SourceFamilies(other?.Evidence)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
                        if (other?.Evidence?.Reviewed != true || !EvidenceClassifier.IsInScope(other.Evidence, options.ScopeTerms)
                            || otherResolution.Family != link.Family)
                        {
                            link = new(null, "neighbour_refused", "Equally close reviewed entry " + alternative.Accession
                                + " links to a different or unresolved panel family.");
                            break;
                        }
                    }
                }
                else if (link.Family != null && neighbour!.TiedFamilies.Count > 0)
                {
                    link = new(null, "neighbour_refused", "The equally close reference identities were not recorded. Run the search again.");
                }
                if (link.Family == null) { reason = link.Reason; Flag(link.Code, link.Reason); }
                else if (context.Conflicts(EvidenceDomains.Ordered(protein.Evidence), link.Family))
                { reason = "Observed accompanying domains conflict with the neighbour's panel family."; Flag("panel_domain_conflict", reason); }
                else if (!annotated && !context.Compatible(EvidenceDomains.Ordered(protein.Evidence), link.Family, reference.Evidence))
                { reason = "The neighbour qualifies, but no compatible observed domains support an unannotated protein."; Flag("domain_compatibility", reason); }
                else
                {
                    family = link.Family;
                    tier = annotated ? EvidenceTier.ReviewedNeighbour : EvidenceTier.UnannotatedNeighbour;
                    reason = "Reviewed neighbour " + neighbour!.Accession + ": " + neighbour.IdentityPercent.ToString("0.##", CultureInfo.InvariantCulture)
                        + "% identity, " + neighbour.QueryCoveragePercent!.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        + "% of query residues aligned. " + link.Reason;
                }
            }
        }
        if (facts != null)
        {
            facts.PanelFamilyKey = family.Length == 0 ? null : family; facts.Reason = reason; trace!(facts);
        }
        return new EvidenceProteinProposal
        {
            ProteinIndex = index, Identifier = protein.SequenceIdentifier, PreviousFamilyId = protein.UserID,
            Family = family, Tier = tier, Reason = reason, Flags = flags, HasScopeAnnotation = annotated, Neighbour = neighbour,
        };
    }
}
