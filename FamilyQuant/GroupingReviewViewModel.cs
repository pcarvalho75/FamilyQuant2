using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;

namespace FamilyQuant
{
    public sealed partial class GroupingReviewViewModel : ScreenViewModel
    {
        private int modeIndex;
        private int keywordCategoryFilterIndex;
        private EvidenceReviewViewModel? evidence;
        private string scopeText = "", motifPattern = "", status = "";
        private string proposalScope = "";
        private string checkedMotifPattern = "", motifReport = "";
        private bool isBusy;
        private bool taskScopeExpanded = true;
        private int generation;
        private readonly Func<string, EvidenceReviewInput>? propose;
        private readonly Func<string, string>? motif;
        private readonly Func<IProgress<FetchProgress>?, CancellationToken, Task<EvidenceTaskScopeInput>>? discoverTask;
        private readonly Func<IReadOnlyList<PanelFamilyLinkSelection>, EvidenceReviewInput>? confirmPanelLinks;
        private readonly Func<EvidenceManualRequest, EvidenceReviewInput>? addToFamily;
        private readonly Func<IReadOnlyList<int>, EvidenceReviewInput>? removeFromFamily;
        private readonly Func<IReadOnlyList<EvidenceManualChoice>, EvidenceReviewInput>? updateManualChoices;
        private readonly Func<string, OperationContext, Task>? fetchMissingPanel;
        private readonly Func<string, string>? panelCoverage;
        private readonly Func<string, bool>? panelCovered;
        public bool CanFetchMissingPanel => fetchMissingPanel != null && !IsBusy && WordCluster.ParseKeywords(ScopeText).Count > 0 && panelCovered?.Invoke(ScopeText) != true;
        public bool PanelFetchAvailable => fetchMissingPanel != null;
        public string PanelCoverageText => panelCoverage?.Invoke(ScopeText) ?? "";
        private string manualFailure = "";
        private CancellationTokenSource? discoveryCancellation;
        public EvidenceTaskScopeInput? TaskScope { get; private set; }
        public bool TaskDiscoveryAvailable => discoverTask != null;
        public bool HasTaskScope => TaskScope != null;
        public int KeywordCategoryFilterIndex
        {
            get => keywordCategoryFilterIndex;
            set { if (Set(ref keywordCategoryFilterIndex, value == 1 ? 1 : 0)) Changed(nameof(VisibleTaskKeywords)); }
        }
        public IReadOnlyList<ReviewTaskKeyword> VisibleTaskKeywords => TaskScope == null ? Array.Empty<ReviewTaskKeyword>()
            : KeywordCategoryFilterIndex == 1 ? TaskScope.Keywords.Where(keyword => keyword.IsMolecularFunction).ToArray() : TaskScope.Keywords;
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
        public bool CanEditManual => Evidence != null && !IsBusy && ScopeText == proposalScope && addToFamily != null && removeFromFamily != null;
        public int ModeIndex { get => modeIndex; set { modeIndex = value; Changed(); NotifySummary(); } }
        public string ScopeText
        {
            get => scopeText;
            set
            {
                if (!Set(ref scopeText, value)) return;
                Changed(nameof(CanUpdateProposal)); NotifySummary(); RequestScopePreview();
            }
        }
        public string MotifPattern { get => motifPattern; set { motifPattern = value; Changed(); Changed(nameof(CanCheckMotif)); NotifySummary(); } }
        public string MotifReport { get => motifReport; private set { motifReport = value; Changed(); Changed(nameof(MotifSummary)); } }
        public string MotifSummary => MotifReport.Length == 0 ? "Enter a pattern, then Check motif; counts include unticked members and exclude known fragments."
            : "Counts cover all proposed family members, including unticked proteins; known fragments are excluded.";
        public string Status { get => status; private set { status = value; Changed(); } }
        public void AddScopeKeywords(IEnumerable<string> keywords)
        {
            if (IsBusy) return;
            var existing = new HashSet<string>(WordCluster.ParseKeywords(ScopeText), StringComparer.OrdinalIgnoreCase);
            var additions = new List<string>();
            foreach (string keyword in keywords)
            {
                string quoted = QuoteScopeKeyword(keyword);
                string? term = WordCluster.ParseKeywords(quoted).SingleOrDefault();
                if (term != null && existing.Add(term)) additions.Add(quoted);
            }
            if (additions.Count == 0) return;
            string prefix = ScopeText.TrimEnd();
            string normalizedQuotes = prefix.Replace('\u201c', '"').Replace('\u201d', '"');
            if (normalizedQuotes.Count(c => c == '"') % 2 != 0) prefix += "\"";
            ScopeText = string.IsNullOrWhiteSpace(prefix) ? string.Join(", ", additions) : prefix + ", " + string.Join(", ", additions);
        }
        private static string QuoteScopeKeyword(string keyword) => "\""
            + Regex.Replace(keyword.Replace('"', ' ').Replace('\u201c', ' ').Replace('\u201d', ' '), @"\s+", " ").Trim() + "\"";
        public string Summary => ModeIndex == 1 ? Evidence?.Summary ?? "Set the scope and build an evidence proposal." : Keyword.Headline;
        public bool CanConfirm => !IsBusy && (ModeIndex == 1 ? Evidence?.CanConfirm == true && ScopeText == proposalScope
            && (MotifPattern.Length == 0 || checkedMotifPattern == MotifPattern) : Keyword.CanCreate);
        public string ApplyHint
        {
            get
            {
                if (ModeIndex != 1) return "";
                if (IsBusy) return "Please wait for the current review step to finish.";
                if (manualFailure.Length > 0) return manualFailure;
                if (!string.IsNullOrWhiteSpace(ScopeText) && panelCovered?.Invoke(ScopeText) == false) return PanelCoverageText;
                if (Evidence == null) return string.IsNullOrWhiteSpace(ScopeText)
                    ? "Enter scope words, then use Update proposal." : "Use Update proposal to build the family proposal.";
                if (ScopeText != proposalScope) return "Scope changed: use Update proposal before Apply grouping.";
                if (Evidence.HasInvalidPanelLinkNotes) return "Enter a short single-line note for each edited panel link.";
                if (Evidence.HasUnappliedPanelLinkChoices) return "Use link choices to update the family proposal before Apply grouping.";
                if (Evidence.PanelLinks.Any(row => row.Source.NeedsConfirmation)) return "Choose a family or leave outside for each unresolved panel link.";
                if (Evidence.SelectedCount == 0) return "Tick at least one unlocked protein in the family proposal.";
                if (Evidence.Families.Any(f => f.SelectedCount > 0 && string.IsNullOrWhiteSpace(f.Name))) return "Enter a name for every selected family.";
                if (Evidence.Architectures.Any(a => Evidence.Families.Any(f => f.Family == a.Family && f.SelectedCount > 0) && !a.IsValid))
                    return "Enter a name for every ticked split.";
                if (MotifPattern.Length > 0 && checkedMotifPattern != MotifPattern) return "Check the motif, or clear it, before Apply grouping.";
                int hand = Evidence.ManualSelectedCount;
                return hand > 0 ? hand.ToString("N0") + (hand == 1 ? " selected protein added by hand; " : " selected proteins added by hand; ")
                        + "Apply grouping commits the choices, File > Save keeps them."
                    : "Ready to apply the selected proteins; File > Save keeps the change.";
            }
        }
        public GroupingReviewViewModel(SuperClusterPreview keyword, EvidenceReviewInput? input = null,
            Func<string, EvidenceReviewInput>? propose = null, Func<string, string>? motif = null,
            string initialScope = "", bool evidenceMode = false,
            Func<CancellationToken, Task<EvidenceTaskScopeInput>>? discoverTask = null,
            Func<IReadOnlyList<PanelFamilyLinkSelection>, EvidenceReviewInput>? confirmPanelLinks = null,
            Func<IProgress<FetchProgress>?, CancellationToken, Task<EvidenceTaskScopeInput>>? discoverTaskWithProgress = null,
            Func<string, string, CancellationToken, Task<EvidenceScopePreview>>? previewScope = null,
            Func<EvidenceManualRequest, EvidenceReviewInput>? addToFamily = null,
            Func<IReadOnlyList<int>, EvidenceReviewInput>? removeFromFamily = null,
            Func<IReadOnlyList<EvidenceManualChoice>, EvidenceReviewInput>? updateManualChoices = null,
            Func<string, Panels.PanelConfirmedEvidenceMatches, EvidenceReviewInput>? confirmPanelMatches = null,
            Func<string, OperationContext, Task>? fetchMissingPanel = null, Func<string, string>? panelCoverage = null, Func<string, bool>? panelCovered = null)
        {
            Keyword = new KeywordReviewViewModel(keyword); this.propose = propose; this.motif = motif;
            this.discoverTask = discoverTaskWithProgress ?? (discoverTask == null ? null : (_, token) => discoverTask(token));
            this.confirmPanelLinks = confirmPanelLinks;
            this.previewScope = previewScope;
            scopePreviewBaseline = initialScope;
            scopePreviewBaselineCaptured = !string.IsNullOrWhiteSpace(initialScope);
            this.addToFamily = addToFamily; this.removeFromFamily = removeFromFamily; this.updateManualChoices = updateManualChoices;
            this.confirmPanelMatches = confirmPanelMatches;
            this.fetchMissingPanel = fetchMissingPanel; this.panelCoverage = panelCoverage;
            this.panelCovered = panelCovered;
            scopeText = initialScope; proposalScope = initialScope;
            Keyword.PropertyChanged += (_, _) => NotifySummary();
            SetEvidence(input);
            modeIndex = input != null || evidenceMode ? 1 : 0;
        }
        public Task DiscoverTaskAsync() => DiscoverTaskAsync(null);

        public async Task FetchMissingPanelAsync(OperationContext operation)
        {
            if (!CanFetchMissingPanel) return;
            string scope = ScopeText; int request = ++generation;
            try
            {
                IsBusy = true; Status = "Fetching missing panel entries for the chosen scope...";
                await fetchMissingPanel!(scope, operation);
                if (request != generation) return;
                SetEvidence(null); proposalScope = "";
                Status = "Panel stored. Use Update proposal to review the chosen scope.";
            }
            finally { if (request == generation) IsBusy = false; NotifySummary(); }
        }

        internal void ReportOperationFailure(Exception error)
        {
            IsBusy = false;
            Status = "The grouping review could not finish: " + error.Message;
        }

        internal void ReportOperationCancellation()
        {
            IsBusy = false;
            Status = "Cancelled. Previous review is kept.";
        }

        public async Task DiscoverTaskAsync(IProgress<FetchProgress>? progress)
        {
            if (!CanDiscoverTask) return;
            int request = ++generation;
            using var cancellation = new CancellationTokenSource();
            discoveryCancellation = cancellation;
            try
            {
                IsBusy = true; Status = "Reading the database taxonomy, reviewed keywords and tissue annotations...";
                var facts = await discoverTask!(progress, cancellation.Token);
                if (request != generation || cancellation.IsCancellationRequested) return;
                TaskScope = facts; Changed(nameof(TaskScope)); Changed(nameof(HasTaskScope)); Changed(nameof(VisibleTaskKeywords));
                TaskScopeExpanded = true;
                // Preserve every supplied phrase through the same parser used by the grouping command.
                string suggested = facts.ReviewedProteins == 0 ? ""
                    : string.Join(", ", facts.ProposedScopeTerms.Select(t => "\"" + t + "\""));
                suppressScopePreview = true;
                try { if (string.IsNullOrWhiteSpace(ScopeText)) ScopeText = suggested; }
                finally { suppressScopePreview = false; }
                if (!scopePreviewBaselineCaptured) SetScopePreviewBaseline(ScopeText);
                Status = facts.ReviewedProteins == 0
                    ? "No reviewed proteins were found for a suggested scope. Fetch evidence or enter a scope."
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
            var manual = Evidence?.CaptureManualChoices();
            int request = ++generation;
            IsBusy = true; Status = "Reading the stored evidence...";
            try
            {
                if (manual?.Count > 0) updateManualChoices?.Invoke(manual);
                var input = await Task.Run(() => propose!(requestedScope));
                if (request != generation) return;
                if (ScopeText != requestedScope) { Status = "Scope changed. Update the proposal before applying."; return; }
                proposalScope = requestedScope; manualFailure = ""; SetEvidence(input);
                SetScopePreviewBaseline(requestedScope);
                RestoreManualState(manual);
                TaskScopeExpanded = false;
                Status = "Proposal updated. Review the ticks before applying.";
            }
            catch (Exception ex) { if (request == generation) Status = ex.Message; }
            finally { if (request == generation) IsBusy = false; }
        }
        public void CancelPending()
        {
            CancelScopePreview();
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
            string? checklistKey = evidence?.SelectedChecklistFamily?.Key;
            if (evidence != null) evidence.PropertyChanged -= EvidenceChanged;
            evidence = input == null ? null : new EvidenceReviewViewModel(input);
            if (checklistKey != null && evidence != null) evidence.SelectedChecklistFamily = evidence.PanelChecklist.FirstOrDefault(row => row.Key == checklistKey);
            if (evidence != null) evidence.PropertyChanged += EvidenceChanged;
            checkedMotifPattern = ""; MotifReport = "";
            Changed(nameof(Evidence)); Changed(nameof(EvidenceAvailable)); Changed(nameof(CanCheckMotif)); NotifySummary();
            Changed(nameof(CanEditManual));
        }
        private void EvidenceChanged(object? sender, PropertyChangedEventArgs e) { Changed(nameof(CanUpdatePanelLinks)); NotifySummary(); }
        private void NotifySummary() { Changed(nameof(Summary)); Changed(nameof(CanConfirm)); Changed(nameof(ApplyHint)); Changed(nameof(CanEditManual)); Changed(nameof(CanMatchPanelNames)); Changed(nameof(CanFetchMissingPanel)); Changed(nameof(PanelCoverageText)); NotifyChecklistActions(); }

        private void RestoreManualState(IReadOnlyList<EvidenceManualChoice>? choices)
        {
            foreach (var choice in choices ?? Array.Empty<EvidenceManualChoice>())
            {
                var row = Evidence?.Proteins.FirstOrDefault(p => p.ProteinIndex == choice.ProteinIndex);
                if (row != null) row.Selected = choice.Selected;
                var family = Evidence?.Families.FirstOrDefault(f => f.Family == choice.Family);
                if (family != null) family.Name = choice.Name;
            }
        }
        public void AddToFamily(EvidenceManualRequest request)
        {
            if (!CanEditManual) throw new InvalidOperationException("Update proposal before editing hand choices.");
            var previous = Evidence!.CaptureReviewState();
            if (previous.ManualChoices?.Count > 0) updateManualChoices?.Invoke(previous.ManualChoices);
            var input = addToFamily!(request);
            SetEvidence(input); Evidence!.RestoreReviewState(previous, forceOn: request.ProteinIndices);
            SelectManualRow(request.ProteinIndices.First());
            manualFailure = ""; Status = "Added " + request.ProteinIndices.Count.ToString("N0")
                + (request.ProteinIndices.Count == 1 ? " protein by hand; " : " proteins by hand; ") + "review the choices before Apply grouping.";
            NotifySummary();
        }
        public void RemoveFromFamily(IReadOnlyList<int> indices)
        {
            if (!CanEditManual) throw new InvalidOperationException("Update proposal before editing hand choices.");
            var previous = Evidence!.CaptureReviewState();
            if (previous.ManualChoices?.Count > 0) updateManualChoices?.Invoke(previous.ManualChoices);
            var input = removeFromFamily!(indices);
            SetEvidence(input); Evidence!.RestoreReviewState(previous, useDefaults: indices);
            SelectManualRow(indices.First());
            manualFailure = ""; Status = "Hand choices removed; the automatic proposal is restored for those proteins."; NotifySummary();
        }
        private void SelectManualRow(int index)
        {
            var row = Evidence!.Proteins.First(p => p.ProteinIndex == index);
            Evidence.SelectedFamily = Evidence.Families.FirstOrDefault(f => f.Family == row.Family);
            Evidence.SelectedProtein = row;
        }
        public void ReportManualFailure(string message) { manualFailure = "Hand choice not changed: " + message; Status = manualFailure; NotifySummary(); }
    }
}
