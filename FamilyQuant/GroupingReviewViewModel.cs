using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using FamilyQuant.Evidence;

namespace FamilyQuant
{
    public sealed class GroupingReviewViewModel : ScreenViewModel
    {
        private int modeIndex;
        private EvidenceReviewViewModel? evidence;
        private string scopeText = "", motifPattern = "", status = "";
        private string proposalScope = "";
        private string checkedMotifPattern = "", motifReport = "";
        private bool isBusy;
        private bool taskScopeExpanded = true;
        private int generation;
        private readonly Func<string, EvidenceReviewInput>? propose;
        private readonly Func<string, string>? motif;
        private readonly Func<CancellationToken, Task<EvidenceTaskScopeInput>>? discoverTask;
        private readonly Func<IReadOnlyList<PanelFamilyLinkSelection>, EvidenceReviewInput>? confirmPanelLinks;
        private CancellationTokenSource? discoveryCancellation;
        public EvidenceTaskScopeInput? TaskScope { get; private set; }
        public bool TaskDiscoveryAvailable => discoverTask != null;
        public bool HasTaskScope => TaskScope != null;
        public bool CanDiscoverTask => discoverTask != null && !IsBusy;
        public bool CanEdit => !IsBusy;
        public bool TaskScopeExpanded { get => taskScopeExpanded; set => Set(ref taskScopeExpanded, value); }
        public KeywordReviewViewModel Keyword { get; }
        public EvidenceReviewViewModel? Evidence => evidence;
        public bool EvidenceAvailable => evidence != null || propose != null;
        public bool IsBusy { get => isBusy; private set { isBusy = value; Changed(); Changed(nameof(CanEdit)); Changed(nameof(CanDiscoverTask)); Changed(nameof(CanUpdateProposal)); Changed(nameof(CanCheckMotif)); Changed(nameof(CanUpdatePanelLinks)); NotifySummary(); } }
        public bool CanUpdateProposal => propose != null && !IsBusy && !string.IsNullOrWhiteSpace(ScopeText);
        public bool CanCheckMotif => motif != null && !IsBusy && Evidence != null && !string.IsNullOrWhiteSpace(MotifPattern);
        public bool CanUpdatePanelLinks => confirmPanelLinks != null && !IsBusy && Evidence?.HasUnappliedPanelLinkChoices == true
            && Evidence.HasInvalidPanelLinkNotes == false;
        public int ModeIndex { get => modeIndex; set { modeIndex = value; Changed(); NotifySummary(); } }
        public string ScopeText { get => scopeText; set { scopeText = value; Changed(); Changed(nameof(CanUpdateProposal)); NotifySummary(); } }
        public string MotifPattern { get => motifPattern; set { motifPattern = value; Changed(); Changed(nameof(CanCheckMotif)); NotifySummary(); } }
        public string MotifReport { get => motifReport; private set { motifReport = value; Changed(); } }
        public string Status { get => status; private set { status = value; Changed(); } }
        public string Summary => ModeIndex == 1 ? Evidence?.Summary ?? "Set the scope and build an evidence proposal." : Keyword.Headline;
        public bool CanConfirm => !IsBusy && (ModeIndex == 1 ? Evidence?.CanConfirm == true && ScopeText == proposalScope
            && (MotifPattern.Length == 0 || checkedMotifPattern == MotifPattern) : Keyword.CanCreate);
        public GroupingReviewViewModel(SuperClusterPreview keyword, EvidenceReviewInput? input = null,
            Func<string, EvidenceReviewInput>? propose = null, Func<string, string>? motif = null,
            string initialScope = "", bool evidenceMode = false,
            Func<CancellationToken, Task<EvidenceTaskScopeInput>>? discoverTask = null,
            Func<IReadOnlyList<PanelFamilyLinkSelection>, EvidenceReviewInput>? confirmPanelLinks = null)
        {
            Keyword = new KeywordReviewViewModel(keyword); this.propose = propose; this.motif = motif;
            this.discoverTask = discoverTask;
            this.confirmPanelLinks = confirmPanelLinks;
            scopeText = initialScope; proposalScope = initialScope;
            Keyword.PropertyChanged += (_, _) => NotifySummary();
            SetEvidence(input);
            modeIndex = input != null || evidenceMode ? 1 : 0;
        }
        public async Task DiscoverTaskAsync()
        {
            if (!CanDiscoverTask) return;
            int request = ++generation;
            using var cancellation = new CancellationTokenSource();
            discoveryCancellation = cancellation;
            IsBusy = true; Status = "Reading the database taxonomy, reviewed keywords and tissue annotations...";
            try
            {
                var facts = await discoverTask!(cancellation.Token);
                if (request != generation || cancellation.IsCancellationRequested) return;
                TaskScope = facts; Changed(nameof(TaskScope)); Changed(nameof(HasTaskScope));
                TaskScopeExpanded = true;
                // Preserve every supplied phrase through the same parser used by the grouping command.
                string suggested = facts.ReviewedProteins == 0 ? ""
                    : string.Join(", ", facts.ProposedScopeTerms.Select(t => "\"" + t + "\""));
                if (string.IsNullOrWhiteSpace(ScopeText)) ScopeText = suggested;
                Status = facts.ReviewedProteins == 0
                    ? "No scope can be suggested: this project has no UniProt evidence yet. Fetch evidence first, or type a scope."
                    : "Review the database taxa, keyword counts and suggested scope, then confirm or edit the scope with Update proposal.";
            }
            catch (OperationCanceledException) { if (request == generation) Status = "Task discovery cancelled. You may enter a manual scope."; }
            catch (Exception ex) { if (request == generation) Status = ex.Message; }
            finally
            {
                if (ReferenceEquals(discoveryCancellation, cancellation)) discoveryCancellation = null;
                if (request == generation) IsBusy = false;
            }
        }
        public async Task UpdateProposalAsync()
        {
            if (!CanUpdateProposal) return;
            string requestedScope = ScopeText;
            int request = ++generation;
            IsBusy = true; Status = "Reading the stored evidence...";
            try
            {
                var input = await Task.Run(() => propose!(requestedScope));
                if (request != generation) return;
                if (ScopeText != requestedScope) { Status = "Scope changed. Update the proposal before applying."; return; }
                proposalScope = requestedScope; SetEvidence(input);
                TaskScopeExpanded = false;
                Status = "Proposal updated. Review the ticks before applying.";
            }
            catch (Exception ex) { if (request == generation) Status = ex.Message; }
            finally { if (request == generation) IsBusy = false; }
        }
        public void CancelPending()
        {
            bool wasBusy = IsBusy;
            generation++; discoveryCancellation?.Cancel(); IsBusy = false;
            if (wasBusy) Status = "Cancelled. Previous review is kept.";
        }
        public async Task UpdatePanelLinksAsync()
        {
            if (!CanUpdatePanelLinks) return;
            var choices = Evidence!.CapturePanelLinkChoices();
            string requestedScope = ScopeText;
            int request = ++generation;
            IsBusy = true; Changed(nameof(CanUpdatePanelLinks)); Status = "Updating the confirmed panel links...";
            try
            {
                var input = await Task.Run(() => confirmPanelLinks!(choices));
                if (request != generation) return;
                if (ScopeText != requestedScope) { Status = "Scope changed. Update the proposal before applying."; return; }
                SetEvidence(input);
                Status = "Links updated. Review the proposal before applying.";
            }
            catch (Exception ex) { if (request == generation) Status = ex.Message; }
            finally { if (request == generation) { IsBusy = false; Changed(nameof(CanUpdatePanelLinks)); } }
        }
        public async Task CheckMotifAsync()
        {
            if (!CanCheckMotif) return;
            string requestedPattern = MotifPattern;
            int request = ++generation;
            IsBusy = true; Status = "Counting the motif in complete proteins...";
            try
            {
                string report = await Task.Run(() => motif!(requestedPattern));
                if (request != generation) return;
                if (MotifPattern != requestedPattern) { Status = "Pattern changed. Check the new pattern before applying."; return; }
                MotifReport = report; checkedMotifPattern = requestedPattern; Status = "Motif counts updated.";
            }
            catch (Exception ex) { if (request == generation) { checkedMotifPattern = ""; Status = ex.Message; } }
            finally { if (request == generation) IsBusy = false; }
        }
        private void SetEvidence(EvidenceReviewInput? input)
        {
            if (evidence != null) evidence.PropertyChanged -= EvidenceChanged;
            evidence = input == null ? null : new EvidenceReviewViewModel(input);
            if (evidence != null) evidence.PropertyChanged += EvidenceChanged;
            checkedMotifPattern = ""; MotifReport = "";
            Changed(nameof(Evidence)); Changed(nameof(EvidenceAvailable)); Changed(nameof(CanCheckMotif)); NotifySummary();
        }
        private void EvidenceChanged(object? sender, PropertyChangedEventArgs e) { Changed(nameof(CanUpdatePanelLinks)); NotifySummary(); }
        private void NotifySummary() { Changed(nameof(Summary)); Changed(nameof(CanConfirm)); }
    }
}
