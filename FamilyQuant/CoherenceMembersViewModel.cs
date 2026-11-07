using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed record CoherenceMemberRow(CoherenceReviewedMember Member)
{
    public string Accession => Member.Accession;
    public string Species => string.IsNullOrWhiteSpace(Member.Species) ? "Species not recorded" : Member.Species;
    public string SpeciesHelp => Species + "; taxon ID: " + (Member.TaxonomyId?.ToString() ?? "Not recorded");
    public string Source => Member.Source switch { CoherenceMemberSource.File => "Your file", CoherenceMemberSource.ReferencePanel => "Reference panel", _ => "Source not recorded" };
    public int Length => Member.Length;
    public int Cysteines => Member.Cysteines;
    public string Completeness => Member.IsComplete ? "Not marked fragment" : "Marked fragment";
}

public sealed class CoherenceMembersViewModel : ScreenViewModel
{
    private bool completeOnly = true;
    public FamilyProfile Profile { get; }
    public string Title => "Reviewed members: " + Profile.Family;
    public IReadOnlyList<CoherenceMemberRow> Members { get; }
    public bool CompleteOnly { get => completeOnly; set { if (completeOnly == value) return; completeOnly = value; Changed(); Changed(nameof(VisibleMembers)); Changed(nameof(Summary)); } }
    public IEnumerable<CoherenceMemberRow> VisibleMembers => Members.Where(m => !CompleteOnly || m.Member.IsComplete);
    public string Summary => VisibleMembers.Count().ToString("N0") + " shown; " + Profile.CompleteMembers.ToString("N0") + " complete reviewed members, "
        + Profile.Members.ToString("N0") + " reviewed members in total; each accession counted once.";
    public CoherenceMembersViewModel(FamilyProfile profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Members = profile.ReviewedMembers.Select(m => new CoherenceMemberRow(m)).OrderBy(m => m.Member.Source == CoherenceMemberSource.File ? 0 : 1)
            .ThenBy(m => m.Species, StringComparer.Ordinal).ThenBy(m => m.Accession, StringComparer.Ordinal).ToArray();
    }
}
