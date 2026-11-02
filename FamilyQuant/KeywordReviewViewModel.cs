using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>Projects the existing pure preview. Matching and joining remain in Core.</summary>
    public sealed class KeywordReviewViewModel : ScreenViewModel
    {
        private string domainText = "";
        private string? chosenDomain;
        public SuperClusterPreview Preview { get; }
        public event EventHandler? Refreshing;
        public event EventHandler? Refreshed;
        public IReadOnlyList<SuperClusterPreviewProtein> Proteins => Preview.Proteins;
        public string Headline => Preview.Headline;
        public string Notes => Preview.Notes;
        public string HiddenLabel => Preview.HiddenLabel;
        public bool HasHidden => Preview.HiddenFamilies > 0;
        public bool HasDomains => Preview.HasDomains;
        public bool NoDomains => !HasDomains;
        public bool CanCreate => Preview.CanCreate;
        public IReadOnlyList<string> DomainChoices => Preview.DomainsOnOffer.Select(d => d.Name + " (" + d.Proteins.ToString("N0") + ")").ToArray();
        public string KeywordsText { get => Preview.KeywordsText; set { if (Preview.KeywordsText == value) return; Preview.KeywordsText = value; Refresh(); } }
        public string ExcludeText { get => Preview.ExcludeText; set { if (Preview.ExcludeText == value) return; Preview.ExcludeText = value; Refresh(); } }
        public int MatchIndex { get => Preview.Match == KeywordMatch.Any ? 1 : 0; set { Preview.Match = value == 1 ? KeywordMatch.Any : KeywordMatch.All; Refresh(); } }
        public bool IncludeHidden { get => Preview.IncludeHidden; set { Preview.IncludeHidden = value; Refresh(); } }
        public string DomainText { get => domainText; set { if (domainText == value) return; domainText = value; Changed(); } }
        public string? ChosenDomain
        {
            get => chosenDomain;
            set
            {
                if (value == null) return;
                string name = value[..value.LastIndexOf(" (", StringComparison.Ordinal)];
                if (!WordCluster.ParseKeywords(DomainText).Contains(name.ToLowerInvariant()))
                    DomainText = DomainText.Length == 0 ? name : DomainText + ", " + name;
                chosenDomain = null; Changed();
            }
        }
        public KeywordReviewViewModel(SuperClusterPreview preview) => Preview = preview ?? throw new ArgumentNullException(nameof(preview));
        public void Refresh()
        {
            Refreshing?.Invoke(this, EventArgs.Empty); Preview.Refresh();
            Changed(nameof(Proteins)); Changed(nameof(DomainChoices)); NotifyState();
            Refreshed?.Invoke(this, EventArgs.Empty);
        }
        public void FollowTicks() { Preview.ApplyTicks(); NotifyState(); }
        public void TickAll() { Preview.TickAll(); NotifyState(); }
        public void UntickWithoutKeyword() { Preview.UntickWithoutKeyword(); NotifyState(); }
        public void SetTicks(IEnumerable<SuperClusterPreviewProtein> rows, bool value) { Preview.SetTicks(rows, value); NotifyState(); }
        public void KeepDomains() { Preview.KeepDomains(DomainText); NotifyState(); }
        private void NotifyState()
        {
            Changed(nameof(Headline)); Changed(nameof(Notes)); Changed(nameof(HiddenLabel));
            Changed(nameof(HasHidden)); Changed(nameof(HasDomains)); Changed(nameof(NoDomains)); Changed(nameof(CanCreate));
        }
    }
}
