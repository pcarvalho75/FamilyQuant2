using System;
using System.ComponentModel;
using System.Threading.Tasks;

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
        private int generation;
        private readonly Func<string, EvidenceReviewInput>? propose;
        private readonly Func<string, string>? motif;
        public KeywordReviewViewModel Keyword { get; }
        public EvidenceReviewViewModel? Evidence => evidence;
        public bool EvidenceAvailable => evidence != null || propose != null;
        public bool IsBusy { get => isBusy; private set { isBusy = value; Changed(); Changed(nameof(CanUpdateProposal)); Changed(nameof(CanCheckMotif)); NotifySummary(); } }
        public bool CanUpdateProposal => propose != null && !IsBusy && !string.IsNullOrWhiteSpace(ScopeText);
        public bool CanCheckMotif => motif != null && !IsBusy && Evidence != null && !string.IsNullOrWhiteSpace(MotifPattern);
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
            string initialScope = "", bool evidenceMode = false)
        {
            Keyword = new KeywordReviewViewModel(keyword); this.propose = propose; this.motif = motif;
            scopeText = initialScope; proposalScope = initialScope;
            Keyword.PropertyChanged += (_, _) => NotifySummary();
            SetEvidence(input);
            modeIndex = input != null || evidenceMode ? 1 : 0;
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
                Status = "Proposal updated. Review the ticks before applying.";
            }
            catch (Exception ex) { if (request == generation) Status = ex.Message; }
            finally { if (request == generation) IsBusy = false; }
        }
        public void CancelPending() { generation++; IsBusy = false; }
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
        private void EvidenceChanged(object? sender, PropertyChangedEventArgs e) => NotifySummary();
        private void NotifySummary() { Changed(nameof(Summary)); Changed(nameof(CanConfirm)); }
    }
}
