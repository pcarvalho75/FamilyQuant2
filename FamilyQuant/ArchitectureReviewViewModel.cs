using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    public sealed record ReviewArchitectureGroupInput(string Key, string SuggestedName, string DominantDomain,
        IReadOnlyList<string> Domains, IReadOnlyList<int> ProteinIndices, int ReviewedProteins, IReadOnlyList<int>? InferredProteinIndices = null);
    public sealed record ReviewArchitectureUndeterminedInput(int ProteinIndex, string Reason);
    public sealed record ReviewArchitectureInput(string Family, bool CanSplit, IReadOnlyList<ReviewArchitectureGroupInput> Groups,
        IReadOnlyList<ReviewArchitectureUndeterminedInput>? Undetermined = null);
    public sealed record ReviewArchitectureChoice(string Family, string ArchitectureKey, string Name, bool Split);

    public sealed record ReviewArchitectureMember(ReviewProteinInput Source, bool Inferred, string Placement)
    {
        public int ProteinIndex => Source.ProteinIndex;
        public string Identifier => Source.Identifier;
        public string Accession { get { string value = FamilyNaming.Accession(Identifier); return value.Length == 0 ? Identifier : value; } }
        public string Description => Source.Description;
        public bool? Reviewed => Source.Reviewed;
        public int? Length => Source.Length;
        public int? Cysteines => Source.Cysteines;
        public string DomainCalls => Source.HasPfamCalls switch { true => "Pfam calls", false => "No Pfam calls", _ => "Not supplied" };
    }

    public sealed class ReviewArchitectureRow : ScreenViewModel
    {
        private string name;
        private bool split;
        private readonly Action changed;
        public ReviewArchitectureGroupInput Source { get; }
        public bool CanSplit { get; }
        public bool Split { get => split; set { if (!CanSplit || split == value) return; split = value; Changed(); changed(); } }
        public string Key => Source.Key;
        public string Name { get => name; set { if (name == value) return; name = value; Changed(); changed(); } }
        public string Domains => Source.Domains.Count == 0 ? "No domain calls" : string.Join("; ", Source.Domains);
        public string DominantDomain => Source.DominantDomain;
        public int Proteins => Source.ProteinIndices.Count;
        public int ReviewedProteins => Source.ReviewedProteins;
        public int InferredProteins => Source.InferredProteinIndices?.Count ?? 0;
        public IReadOnlyList<ReviewArchitectureMember> Members { get; }
        internal ReviewArchitectureRow(ReviewArchitectureGroupInput source, IReadOnlyDictionary<int, ReviewProteinInput> proteins, bool canSplit, Action changed)
        {
            CanSplit = canSplit;
            Source = source with { Domains = source.Domains.ToArray(), ProteinIndices = source.ProteinIndices.ToArray(), InferredProteinIndices = source.InferredProteinIndices?.ToArray() };
            var inferred = (Source.InferredProteinIndices ?? Array.Empty<int>()).ToHashSet();
            Members = Source.ProteinIndices.Select(i => new ReviewArchitectureMember(proteins[i], inferred.Contains(i),
                inferred.Contains(i) ? "Placed by length and cysteines" : Source.Domains.Count == 0 ? "No domain calls" : "Domain combination")).ToArray();
            name = source.SuggestedName; this.changed = changed;
        }
    }

    /// <summary>The user chooses whether to use the supplied architecture groups. No domain rules live here.</summary>
    public sealed class ArchitectureReviewViewModel : ScreenViewModel
    {
        private readonly Action changed;
        public string Family { get; }
        public bool CanSplit { get; }
        // Retain the existing bulk API for callers that choose every supplied architecture.
        public bool Split
        {
            get => Groups.Count > 0 && Groups.All(g => g.Split);
            set { if (CanSplit) foreach (var group in Groups) group.Split = value; }
        }
        public bool? SplitAll
        {
            get => Split ? true : Groups.Any(g => g.Split) ? null : false;
            set => Split = value == true;
        }
        public IReadOnlyList<ReviewArchitectureRow> Groups { get; }
        private ReviewArchitectureRow? selectedGroup;
        public ReviewArchitectureRow? SelectedGroup { get => selectedGroup; set { selectedGroup = value; Changed(); Changed(nameof(VisibleMembers)); } }
        public IReadOnlyList<ReviewArchitectureMember> VisibleMembers => SelectedGroup?.Members ?? Array.Empty<ReviewArchitectureMember>();
        public IReadOnlyList<ReviewArchitectureMember> Undetermined { get; }
        private ReviewArchitectureMember? selectedUndetermined;
        public ReviewArchitectureMember? SelectedUndetermined { get => selectedUndetermined; set { selectedUndetermined = value; Changed(); } }
        public bool HasUndetermined => Undetermined.Count > 0;
        public string UndeterminedLabel => "Not placed in a domain group (" + Undetermined.Count.ToString("N0") + ")";
        public bool IsValid => Groups.Where(g => g.Split).All(g => !string.IsNullOrWhiteSpace(g.Name));
        public ArchitectureReviewViewModel(ReviewArchitectureInput input, IReadOnlyDictionary<int, ReviewProteinInput> proteins, Action changed)
        {
            Family = input.Family; CanSplit = input.CanSplit; this.changed = changed;
            Groups = input.Groups.Select(g => new ReviewArchitectureRow(g, proteins, CanSplit, GroupChanged)).ToArray();
            selectedGroup = Groups.FirstOrDefault();
            Undetermined = (input.Undetermined ?? Array.Empty<ReviewArchitectureUndeterminedInput>())
                .Select(p => new ReviewArchitectureMember(proteins[p.ProteinIndex], false, p.Reason)).ToArray();
            selectedUndetermined = Undetermined.FirstOrDefault();
        }
        private void GroupChanged()
        {
            Changed(nameof(Split)); Changed(nameof(SplitAll)); Changed(nameof(IsValid)); changed();
        }
        public IReadOnlyList<ReviewArchitectureChoice> Capture() => Groups.Select(g =>
            new ReviewArchitectureChoice(Family, g.Key, g.Name.Trim(), g.Split)).ToArray();
    }
}
