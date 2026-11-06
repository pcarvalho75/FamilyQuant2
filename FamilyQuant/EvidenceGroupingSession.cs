using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FamilyQuant.Panels;

namespace FamilyQuant;

/// <summary>A read-only analysis session followed by the Core grouping transaction on confirmation.</summary>
public sealed class EvidenceGroupingSession
{
    private readonly FamilyClusterFile file;
    private readonly string programVersion;
    private readonly string sourceFingerprint;
    private readonly string sourceMetadata;
    private readonly HashSet<int> sourceLocks;
    private readonly string sourcePanel;
    private FamilyPanel? reviewPanel;
    private string lastScope = "";
    private readonly Func<IReadOnlyList<FastaItemExtended>, IReadOnlyList<NeighbourReference>, NeighbourSearch> search;
    private readonly Func<FamilyClusterFile, CancellationToken, Task<EvidenceTaskProposal>>? discoverTask;
    private EvidenceTaskProposal? taskProposal;
    private EvidenceTaskChoice? confirmedTask;
    public EvidenceProposal? Proposal { get; private set; }
    public NeighbourSearch? Search { get; private set; }
    public IReadOnlyList<NeighbourReference>? References { get; private set; }
    private string? checkedMotif;

    public EvidenceGroupingSession(FamilyClusterFile file, string programVersion,
        Func<IReadOnlyList<FastaItemExtended>, IReadOnlyList<NeighbourReference>, NeighbourSearch>? search = null,
        Func<FamilyClusterFile, CancellationToken, Task<EvidenceTaskProposal>>? discoverTask = null)
    {
        this.file = file ?? throw new ArgumentNullException(nameof(file));
        this.programVersion = programVersion;
        sourceFingerprint = EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems);
        sourceMetadata = JsonSerializer.Serialize(file.EvidenceSource);
        sourceLocks = new HashSet<int>(file.LockedUserIds);
        sourcePanel = JsonSerializer.Serialize(file.FamilyPanel);
        reviewPanel = file.FamilyPanel == null ? null : FamilyPanelValidation.Clone(file.FamilyPanel);
        this.search = search ?? ((queries, references) => NearestReviewed.Search(queries, references));
        this.discoverTask = discoverTask;
    }

    public Task<EvidenceTaskScopeInput> DiscoverTaskAsync(CancellationToken cancellationToken = default) =>
        DiscoverTaskAsync(null, cancellationToken);

    public async Task<EvidenceTaskScopeInput> DiscoverTaskAsync(IProgress<FetchProgress>? progress, CancellationToken cancellationToken = default)
    {
        EnsureCurrent();
        var task = discoverTask == null
            ? await EvidenceTaskDiscovery.DiscoverAsync(file, progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await discoverTask(file, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrent();
        taskProposal = task;
        return new EvidenceTaskScopeInput(task.Clade.ScientificName + " (" + task.Clade.Rank + ", "
            + task.Clade.TaxonId.ToString(CultureInfo.CurrentCulture) + ")",
            task.Taxa.Select(t => new ReviewTaskTaxon(t.ScientificName, t.TaxonId)).ToArray(),
            task.Keywords.Select(k => new ReviewTaskKeyword(k.Keyword, k.ReviewedProteins)).ToArray(),
            task.ReviewedProteins, task.ProposedScopeTerms.ToArray(), task.Release,
            "Keyword evidence releases: " + string.Join(", ", task.KeywordEvidenceReleases),
            task.TargetProteins, task.IgnoredContaminants);
    }

    public EvidenceReviewInput Propose(string scope)
    {
        EnsureCurrent();
        var options = new EvidenceClassificationOptions { ScopeTerms = WordCluster.ParseKeywords(scope) };
        if (options.ScopeTerms.Count == 0) throw new ArgumentException("Type at least one scope word or phrase.", nameof(scope));
        var taskChoice = taskProposal == null ? null : EvidenceTaskDiscovery.Confirm(file, taskProposal, options.ScopeTerms);
        var references = reviewPanel == null ? NearestReviewed.References(file, e => EvidenceClassifier.IsInScope(e, options.ScopeTerms))
            : PanelEvidenceClassifier.References(file, options);
        var result = search(NearestReviewed.Targets(file), references);
        IReadOnlyDictionary<string, FamilyProfile>? profiles = null;
        EvidenceProposal proposal;
        if (reviewPanel == null)
        {
            var classified = EvidenceClassifier.Propose(file.MyExtendedFastaItems, options, result.ByQuery(), NearestReviewed.ReferenceEvidence(file));
            profiles = CoherenceCheck.Learn(file);
            proposal = CoherenceCheck.Flag(classified, file.MyExtendedFastaItems, profiles);
        }
        else proposal = PanelEvidenceClassifier.Propose(file, options, result, references, reviewPanel);
        var presentation = EvidenceProposalPresentation.Present(proposal, file, profiles);
        EnsureCurrent();
        Search = result; References = references.ToArray(); Proposal = proposal; checkedMotif = null; confirmedTask = taskChoice;
        lastScope = scope;
        return presentation;
    }

    public EvidenceReviewInput ConfirmPanelLinks(IReadOnlyList<PanelFamilyLinkSelection> choices)
    {
        EnsureCurrent();
        if (reviewPanel == null || Proposal?.PanelContext == null) throw new InvalidOperationException("Build a panel proposal first.");
        var previous = reviewPanel;
        reviewPanel = PanelEvidenceClassifier.ConfirmLinks(reviewPanel, Proposal.PanelContext.Links, choices);
        try { return Propose(lastScope); }
        catch { reviewPanel = previous; throw; }
    }

    public string CountMotif(string pattern)
    {
        EnsureCurrent();
        string report = EvidenceProposalPresentation.CountMotif(Proposal ?? throw new InvalidOperationException("Build a proposal first."),
            file.MyExtendedFastaItems, pattern);
        checkedMotif = pattern;
        return report;
    }

    public EvidenceApplyResult Apply(EvidenceReviewSelection selection)
    {
        EnsureCurrent();
        var proposal = Proposal ?? throw new InvalidOperationException("Build a proposal before applying its selection.");
        if (selection.MotifPattern.Length > 0 && checkedMotif != selection.MotifPattern)
            throw new InvalidOperationException("Check the motif against this proposal before applying it.");
        List<MotifPattern>? motifs = null;
        if (selection.MotifPattern.Length > 0)
        {
            motifs = (file.Motifs ?? new List<MotifPattern>()).Select(m => new MotifPattern { Name = m.Name, Pattern = m.Pattern }).ToList();
            if (!motifs.Any(m => m.Pattern == selection.MotifPattern))
                motifs.Add(new MotifPattern { Name = selection.MotifPattern, Pattern = selection.MotifPattern });
        }
        var native = new EvidenceApplySelection
        {
            SourceFingerprint = selection.SourceFingerprint,
            SelectedProteinIndices = selection.ProteinIndices,
            FamilyNames = selection.Names,
            ProteinOverrides = selection.ProteinOverrides ?? Array.Empty<EvidenceProteinOverride>(),
            SessionSearch = Search,
            SessionReferences = References,
            CheckedMotifs = motifs,
            ConfirmedTask = confirmedTask,
            FamilyPanel = Proposal?.PanelContext == null ? null : reviewPanel,
            ArchitectureChoices = (selection.ArchitectureChoices ?? Array.Empty<ReviewArchitectureChoice>()).Select(a =>
                new EvidenceArchitectureChoice { Family = a.Family, ArchitectureKey = a.ArchitectureKey, Name = a.Name, Split = a.Split }).ToArray()
        };
        return EvidenceApply.Apply(file, proposal, native, programVersion);
    }

    private void EnsureCurrent()
    {
        if (sourceFingerprint != EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems)
            || sourceMetadata != JsonSerializer.Serialize(file.EvidenceSource) || !sourceLocks.SetEquals(file.LockedUserIds)
            || sourcePanel != JsonSerializer.Serialize(file.FamilyPanel))
            throw new InvalidOperationException("The document changed while the review was open. Close it and build a new proposal.");
    }
}
