using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FamilyQuant.Evidence.Positions;

namespace FamilyQuant.Evidence.Alignment;

/// <summary>Where a sequence offered to the alignment comes from.</summary>
public enum AlignmentOrigin
{
    /// <summary>A protein the user highlighted in the review.</summary>
    Highlighted,
    /// <summary>A reviewed protein of the user's own file.</summary>
    File,
    /// <summary>A reviewed protein of the stored reference panel (other species, never quantified).</summary>
    Panel,
}

/// <summary>One sequence the alignment can include. Read only: nothing here is written back.</summary>
public sealed record AlignmentCandidate(string Label, string Accession, string Identifier, AlignmentOrigin Origin,
    string Family, string Sequence, bool Reviewed, bool Fragment, bool IsNeighbour, bool Preselected,
    IReadOnlyList<AlignmentDomain> Domains, string DomainNote, IReadOnlyList<string> Memberships)
{
    /// <summary>Whether this candidate's evidence names <paramref name="family"/>, ignoring case.</summary>
    public bool IsMemberOf(string family) => Memberships.Contains(family, StringComparer.OrdinalIgnoreCase);

    public int Length => Sequence.Length;
    public int Cysteines => CoherenceCheck.Cysteines(Sequence);

    public string OriginText => Origin switch
    {
        AlignmentOrigin.Highlighted => "Highlighted",
        AlignmentOrigin.File => "Your file",
        _ => "Reference panel",
    };

    public AlignmentSequence ToSequence() => new(Label, Accession, OriginText, Sequence, Domains);
}

/// <summary>The sequences offered for one alignment, in the order they are listed and aligned. The
/// first highlighted protein is first and is the protein every other one is aligned to.</summary>
public sealed record AlignmentCandidateSet(IReadOnlyList<AlignmentCandidate> Candidates, IReadOnlyList<string> Families,
    string DefaultFamily, IReadOnlyList<string> Notes);

/// <summary>
/// Chooses the reviewed relatives to align with the proteins highlighted in the evidence review: the
/// reviewed targets of the file and the stored reference panel entries whose family memberships
/// include a family of a highlighted protein (its proposed family, its nearest reviewed protein's
/// family, or its own UniProt family). Preselected: every highlighted protein, each one's nearest
/// reviewed protein, then up to <see cref="DefaultMembers"/> complete reviewed members of the first
/// protein's family, the file's before the panel's, closest in length to the first protein, ties by
/// accession. Fragments are offered but not preselected. Pfam spans are given only where positions are
/// stored for exactly that sequence (same length and SHA-256). Reads the file; changes nothing.
/// </summary>
public static class AlignmentCandidates
{
    public const int DefaultMembers = 5;

    public static AlignmentCandidateSet Find(FamilyClusterFile file, EvidenceProposal? proposal, IReadOnlyList<int> highlighted)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(highlighted);
        var proteins = file.MyExtendedFastaItems;
        var notes = new List<string>();
        var rows = proposal?.Proteins.ToDictionary(p => p.ProteinIndex) ?? new Dictionary<int, EvidenceProteinProposal>();

        var chosen = highlighted.Where(i => i >= 0 && i < proteins.Count && !string.IsNullOrEmpty(proteins[i].Sequence))
            .Distinct().ToList();
        if (chosen.Count == 0) throw new ArgumentException("Highlight at least one protein with a sequence.", nameof(highlighted));
        if (chosen.Count > ProteinAlignment.MaximumSequences)
        {
            notes.Add("Only the first " + ProteinAlignment.MaximumSequences.ToString(CultureInfo.CurrentCulture)
                + " highlighted proteins are offered; an alignment holds at most that many sequences.");
            chosen = chosen.Take(ProteinAlignment.MaximumSequences).ToList();
        }
        var chosenSet = chosen.ToHashSet();

        var targets = Enumerable.Range(0, proteins.Count).Where(i => !NearestReviewed.IsDecoy(proteins[i])
            && !SequenceLabels.IsContaminant(proteins[i].SequenceIdentifier) && !string.IsNullOrEmpty(proteins[i].Sequence)).ToList();
        var fileAccessions = targets.Select(i => NearestReviewed.EntryAccession(proteins[i].SequenceIdentifier))
            .Where(a => a.Length > 0).ToHashSet(StringComparer.Ordinal);
        ReferencePanel? panel = file.EvidenceSource?.ReferencePanel;
        if (panel == null) notes.Add("No reference panel is stored with this file, so only the file's own reviewed proteins are offered.");
        else if (!panel.IsComplete) notes.Add("The stored reference panel is incomplete, so some reviewed relatives may be missing.");
        var panelEntries = (panel?.Entries ?? new List<PanelEntry>())
            .Where(e => e.Sequence.Length > 0 && e.Evidence?.Reviewed != false && !fileAccessions.Contains(e.Accession)).ToList();

        // Families of each highlighted protein, most specific first.
        var familiesOf = chosen.ToDictionary(i => i, i =>
        {
            rows.TryGetValue(i, out EvidenceProteinProposal? row);
            return new[] { row?.Family, row?.Neighbour?.Family, OwnFamily(proteins[i].Evidence) }
                .Select(f => (f ?? string.Empty).Trim()).Where(f => f.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        });
        var families = chosen.SelectMany(i => familiesOf[i]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var familySet = families.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var list = new List<AlignmentCandidate>();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        string Key(AlignmentOrigin origin, string id) => (origin == AlignmentOrigin.Panel ? "panel:" : "file:") + id;

        // Each highlighted protein's nearest reviewed protein, as the proposal found it.
        var neighbourKeys = new HashSet<string>(StringComparer.Ordinal);
        var neighbours = new List<AlignmentCandidate>();
        foreach (int index in chosen)
        {
            if (!rows.TryGetValue(index, out EvidenceProteinProposal? row) || row.Neighbour == null) continue;
            NearestNeighbour neighbour = row.Neighbour;
            if (neighbour.Reference.Length > 0)
            {
                int found = targets.FirstOrDefault(i => proteins[i].SequenceIdentifier == neighbour.Reference, -1);
                if (found < 0) continue;
                if (chosenSet.Contains(found)) { neighbourKeys.Add(Key(AlignmentOrigin.File, proteins[found].SequenceIdentifier)); continue; }
                if (neighbourKeys.Add(Key(AlignmentOrigin.File, proteins[found].SequenceIdentifier)))
                    neighbours.Add(FromFile(proteins[found], AlignmentOrigin.File, isNeighbour: true, preselected: true));
            }
            else
            {
                PanelEntry? entry = panelEntries.FirstOrDefault(e => e.Accession == neighbour.Accession);
                if (entry == null) continue;
                if (neighbourKeys.Add(Key(AlignmentOrigin.Panel, entry.Accession)))
                    neighbours.Add(FromPanel(entry, isNeighbour: true, preselected: true));
            }
        }

        foreach (int index in chosen)
        {
            bool isNeighbour = neighbourKeys.Contains(Key(AlignmentOrigin.File, proteins[index].SequenceIdentifier));
            var highlightedProtein = FromFile(proteins[index], AlignmentOrigin.Highlighted, isNeighbour, preselected: true);
            list.Add(highlightedProtein with { Family = familiesOf[index].FirstOrDefault() ?? highlightedProtein.Family });
            listed.Add(Key(AlignmentOrigin.File, proteins[index].SequenceIdentifier));
        }
        foreach (AlignmentCandidate neighbour in neighbours)
        {
            list.Add(neighbour);
            listed.Add(Key(neighbour.Origin, neighbour.Origin == AlignmentOrigin.Panel ? neighbour.Accession : neighbour.Identifier));
        }

        // Members of the highlighted proteins' families from the file, then from the panel.
        int firstLength = proteins[chosen[0]].Sequence.Length;
        var fileMembers = targets.Where(i => !chosenSet.Contains(i) && proteins[i].Evidence?.Reviewed == true
                && !listed.Contains(Key(AlignmentOrigin.File, proteins[i].SequenceIdentifier))
                && MemberOf(proteins[i].Evidence, familySet))
            .Select(i => FromFile(proteins[i], AlignmentOrigin.File, false, false)).ToList();
        var panelMembers = panelEntries.Where(e => !listed.Contains(Key(AlignmentOrigin.Panel, e.Accession)) && MemberOf(e.Evidence, familySet))
            .Select(e => FromPanel(e, false, false)).ToList();
        IEnumerable<AlignmentCandidate> ByLength(IEnumerable<AlignmentCandidate> members) => members
            .OrderBy(c => Math.Abs(c.Length - firstLength)).ThenBy(c => c.Accession, StringComparer.Ordinal)
            .ThenBy(c => c.Identifier, StringComparer.Ordinal);
        var members = ByLength(fileMembers).Concat(ByLength(panelMembers)).ToList();

        // Defaults: complete members of the first protein's first family that has any.
        string defaultFamily = familiesOf[chosen[0]].FirstOrDefault(f => members.Any(c => c.IsMemberOf(f))) ?? string.Empty;
        int room = ProteinAlignment.MaximumSequences - list.Count;
        var preselect = defaultFamily.Length == 0 ? new HashSet<AlignmentCandidate>()
            : members.Where(c => !c.Fragment && c.IsMemberOf(defaultFamily)).Take(Math.Max(0, Math.Min(DefaultMembers, room))).ToHashSet();
        foreach (AlignmentCandidate member in members.Where(preselect.Contains)) list.Add(member with { Preselected = true });
        list.AddRange(members.Where(c => !preselect.Contains(c)));

        if (families.Count == 0) notes.Add("No family is known for the highlighted proteins, so only their nearest reviewed proteins are offered.");
        // Keep at most the alignment's limit preselected, in list order.
        int ticked = 0;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Preselected && ++ticked > ProteinAlignment.MaximumSequences) list[i] = list[i] with { Preselected = false };
        // Labels name the rows of the FASTA and the aligned text, so a repeated accession falls back to the identifier.
        var repeated = list.GroupBy(c => c.Label, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        for (int i = 0; i < list.Count; i++)
            if (repeated.Contains(list[i].Label) && list[i].Identifier.Length > 0) list[i] = list[i] with { Label = list[i].Identifier };
        return new AlignmentCandidateSet(list, families, defaultFamily, notes);
    }

    /// <summary>The families a protein's evidence names, by the panel's own rule
    /// (<see cref="ReferencePanelFetcher.FamiliesOf"/>: each membership, or the single family).</summary>
    public static IReadOnlyList<string> Memberships(ProteinEvidence? evidence) => evidence == null ? Array.Empty<string>()
        : ReferencePanelFetcher.FamiliesOf(evidence).Select(f => f.Trim()).Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Pfam spans for this exact sequence, or none with the reason.</summary>
    public static (IReadOnlyList<AlignmentDomain> Domains, string Note) Domains(ProteinEvidence? evidence, string sequence)
    {
        ProteinPositionEvidence? positions = evidence?.Positions;
        if (positions == null || positions.DomainSource == null && positions.Pfam.Count == 0)
            return (Array.Empty<AlignmentDomain>(), "No positions stored");
        if (!ProteinPositionFetcher.MatchesSequence(positions, sequence))
            return (Array.Empty<AlignmentDomain>(), "Stored positions are for another sequence");
        var spans = positions.Pfam.Where(d => d.Start >= 1 && d.End >= d.Start && d.End <= sequence.Length)
            .OrderBy(d => d.Start).ThenBy(d => d.End).ThenBy(d => d.Accession, StringComparer.Ordinal)
            .Select(d => new AlignmentDomain(d.Accession, d.Name, d.Start, d.End)).ToArray();
        return (spans, spans.Length == 0 ? "No Pfam domain" : spans.Length.ToString(CultureInfo.CurrentCulture)
            + (spans.Length == 1 ? " domain" : " domains"));
    }

    private static bool MemberOf(ProteinEvidence? evidence, HashSet<string> families) =>
        Memberships(evidence).Any(families.Contains);

    private static string OwnFamily(ProteinEvidence? evidence)
    {
        var memberships = Memberships(evidence);
        return memberships.Count == 1 ? memberships[0] : string.Empty;
    }

    private static AlignmentCandidate FromFile(FastaItemExtended protein, AlignmentOrigin origin, bool isNeighbour, bool preselected)
    {
        string identifier = protein.SequenceIdentifier ?? string.Empty;
        string accession = NearestReviewed.EntryAccession(identifier);
        string sequence = protein.Sequence ?? string.Empty;
        var (domains, note) = Domains(protein.Evidence, sequence);
        bool fragment = protein.Evidence?.Fragment == true
            || (protein.Description ?? string.Empty).IndexOf("(Fragment", StringComparison.OrdinalIgnoreCase) >= 0;
        return new AlignmentCandidate(accession.Length > 0 ? accession : identifier, accession, identifier, origin,
            OwnFamily(protein.Evidence), sequence, protein.Evidence?.Reviewed == true, fragment, isNeighbour, preselected, domains, note,
            Memberships(protein.Evidence));
    }

    private static AlignmentCandidate FromPanel(PanelEntry entry, bool isNeighbour, bool preselected)
    {
        var (domains, note) = Domains(entry.Evidence, entry.Sequence);
        return new AlignmentCandidate(entry.Accession, entry.Accession, string.Empty, AlignmentOrigin.Panel,
            OwnFamily(entry.Evidence), entry.Sequence, true, entry.Evidence?.Fragment == true, isNeighbour, preselected, domains, note,
            Memberships(entry.Evidence));
    }
}
