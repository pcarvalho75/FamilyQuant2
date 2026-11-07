using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Evidence;

public sealed record PanelEvidenceSuggestion(PanelEvidenceMatchKind Kind, string Value, string Name,
    int DatabaseTargets, int ReferenceEntries, string Reason, int ReviewedTargets = 0)
{
    public string Key => JsonSerializer.Serialize(new[] { Kind.ToString(), Value });
}

/// <summary>Detached stored-label suggestions and exact target coverage. No suggested label is a classification rule.</summary>
public sealed partial class PanelEvidenceMatching
{
    private sealed record Label(PanelEvidenceMatchKind Kind, string Value, string Name)
    {
        internal (PanelEvidenceMatchKind, string) Key => (Kind, Value.ToUpperInvariant());
    }
    private sealed record Entry(bool Database, bool Reviewed, IReadOnlyList<Label> Labels, HashSet<string> Words);
    private readonly Entry[] entries;
    private readonly Dictionary<(PanelEvidenceMatchKind, string), PanelEvidenceSuggestion> labels;
    private readonly Dictionary<string, int> wordFrequency;
    private readonly string sourceFingerprint;
    private readonly string sourceEvidence;
    public bool? ReferencePanelComplete { get; }

    public PanelEvidenceMatching(FamilyClusterFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        sourceFingerprint = EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems);
        sourceEvidence = JsonSerializer.Serialize(file.EvidenceSource);
        ReferencePanelComplete = file.EvidenceSource?.ReferencePanel?.IsComplete;
        var database = file.MyExtendedFastaItems.Where(protein => !NearestReviewed.IsDecoy(protein)
            && !SequenceLabels.IsContaminant(protein.SequenceIdentifier))
            .Select(protein => MakeEntry(true, FamilyNaming.ProteinName(protein.Description), protein.Evidence));
        var references = (file.EvidenceSource?.ReferencePanel?.Entries ?? new List<PanelEntry>())
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Accession) && !SequenceLabels.IsDecoy(entry.Accession) && !SequenceLabels.IsContaminant(entry.Accession))
            .GroupBy(entry => entry.Accession, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .Select(entry => MakeEntry(false, "", entry.Evidence));
        entries = database.Concat(references).ToArray();
        wordFrequency = entries.SelectMany(entry => entry.Words).GroupBy(word => word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        labels = entries.SelectMany(entry => entry.Labels.GroupBy(label => label.Key).Select(group => (Entry: entry, Label: group.First())))
            .GroupBy(item => item.Label.Key).ToDictionary(group => group.Key, group =>
            {
                var first = group.Select(item => item.Label).OrderBy(label => label.Value, StringComparer.Ordinal).First();
                string name = group.Select(item => item.Label.Name).Where(value => value.Length > 0).OrderBy(value => value, StringComparer.Ordinal).FirstOrDefault() ?? "";
                return new PanelEvidenceSuggestion(first.Kind, first.Value, name, group.Count(item => item.Entry.Database), group.Count(item => !item.Entry.Database), "",
                    group.Count(item => item.Entry.Database && item.Entry.Reviewed));
            });
    }

    private static HashSet<string> Words(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}", RegexOptions.CultureInvariant)
        .Cast<Match>().Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    private static Entry MakeEntry(bool database, string description, ProteinEvidence? evidence)
    {
        var observed = PanelEvidenceClassifier.SourceFamilies(evidence).Select(value => new Label(PanelEvidenceMatchKind.UniProtFamily, value, ""))
            .Concat((evidence?.Keywords ?? new List<string>()).Select(value => new Label(PanelEvidenceMatchKind.Keyword, value, "")))
            .Concat((evidence?.TissueSpecificity ?? new List<string>()).Select(value => new Label(PanelEvidenceMatchKind.TissuePhrase, value, "")))
            .Concat((evidence?.Pfam ?? new List<EvidenceDomain>()).Select(domain => new Label(PanelEvidenceMatchKind.Pfam, domain.Accession, domain.Name)))
            .Where(label => !string.IsNullOrWhiteSpace(label.Value)).Select(label => label with { Value = Normalize(label.Value) }).ToArray();
        return new Entry(database, evidence?.Reviewed == true, observed, Words(description + " " + string.Join(" ", observed.Select(label => label.Value + " " + label.Name))));
    }

    public bool IsCurrent(FamilyClusterFile file) => sourceFingerprint == EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems)
        && sourceEvidence == JsonSerializer.Serialize(file.EvidenceSource);

    public IReadOnlyList<PanelEvidenceSuggestion> Suggest(IEnumerable<string> names)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var present = Words(name).Where(wordFrequency.ContainsKey).ToArray();
            if (present.Length == 0) continue;
            int rarest = present.Min(word => wordFrequency[word]);
            anchors.UnionWith(present.Where(word => wordFrequency[word] == rarest));
        }
        var seeds = entries.Where(entry => entry.Words.Overlaps(anchors)).ToArray();
        var seedLabels = seeds.SelectMany(entry => entry.Labels).Select(label => label.Key).ToHashSet();
        return labels.Where(pair => seedLabels.Contains(pair.Key)).Select(pair =>
        {
            var label = pair.Value;
            bool direct = Words(label.Value + " " + label.Name).Overlaps(anchors);
            string reason = direct ? "Stored label shares name words." : "Co-occurs in stored entries that share name words.";
            return label with { Reason = reason };
        }).OrderByDescending(label => label.Reason.StartsWith("Stored", StringComparison.Ordinal))
            .ThenByDescending(label => label.DatabaseTargets).ThenByDescending(label => label.ReferenceEntries)
            .ThenBy(label => label.Kind).ThenBy(label => label.Value, StringComparer.Ordinal).ToArray();
    }

    public PanelEvidenceSuggestion Coverage(PanelEvidenceMatchKind kind, string value) =>
        labels.GetValueOrDefault((kind, Normalize(value).ToUpperInvariant())) ?? new(kind, Normalize(value), "", 0, 0, "No exact stored label matches this value.");

    private static string Normalize(string value) => Regex.Replace(value ?? "", @"\s+", " ").Trim();
    private static bool ValidChoice(PanelEvidenceChoice choice) => Enum.IsDefined(choice.Kind)
        && choice.Value.Length > 0 && !choice.Value.Any(char.IsControl);

    public static PanelEvidenceChoice ChoiceFromKey(string key)
    {
        try
        {
            var parts = JsonSerializer.Deserialize<string[]>(key);
            if (parts is { Length: 2 } && Enum.TryParse<PanelEvidenceMatchKind>(parts[0], out var kind))
            {
                var choice = new PanelEvidenceChoice(kind, Normalize(parts[1]));
                if (ValidChoice(choice)) return choice;
            }
        }
        catch (JsonException) { }
        throw new ArgumentException("The evidence suggestion key is invalid.", nameof(key));
    }

    public static PanelConfirmedEvidenceMatches Confirm(IEnumerable<string> names, IEnumerable<PanelEvidenceChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(names); ArgumentNullException.ThrowIfNull(choices);
        var selected = choices.Select(choice => choice with { Value = Normalize(choice.Value) })
            .GroupBy(choice => (choice.Kind, choice.Value.ToUpperInvariant())).Select(group => group.First())
            .OrderBy(choice => choice.Kind).ThenBy(choice => choice.Value, StringComparer.Ordinal).ToArray();
        if (selected.Any(choice => !ValidChoice(choice)))
            throw new ArgumentException("Choose a match kind and enter an evidence label.", nameof(choices));
        return new() { UserConfirmed = true, NamesUsed = names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray(), Choices = selected };
    }

    public static FamilyPanel WithConfirmed(FamilyPanel panel, string familyKey, PanelConfirmedEvidenceMatches matches)
    {
        if (!matches.UserConfirmed) throw new ArgumentException("Confirm the evidence matches before using them.", nameof(matches));
        var result = FamilyPanelValidation.Clone(panel);
        result.ConfirmedEvidenceMatches ??= new(StringComparer.Ordinal);
        result.ConfirmedEvidenceMatches[familyKey] = Confirm(matches.NamesUsed, matches.Choices);
        ValidateConfirmed(result);
        return result;
    }

    public static void ValidateConfirmed(FamilyPanel panel)
    {
        foreach (var pair in panel.ConfirmedEvidenceMatches ?? new Dictionary<string, PanelConfirmedEvidenceMatches>())
        {
            if (!panel.Families.Any(family => family.Key == pair.Key) || pair.Value?.UserConfirmed != true)
                throw new ArgumentException("Evidence matches need an existing panel family and explicit user confirmation.", nameof(panel));
            Confirm(pair.Value.NamesUsed, pair.Value.Choices);
        }
    }

    public static bool Matches(ProteinEvidence? evidence, PanelEvidenceChoice choice) => choice.Kind switch
    {
        PanelEvidenceMatchKind.UniProtFamily => PanelEvidenceClassifier.SourceFamilies(evidence).Any(value => Normalize(value).Equals(Normalize(choice.Value), StringComparison.OrdinalIgnoreCase)),
        PanelEvidenceMatchKind.Keyword => evidence?.Keywords?.Any(value => Normalize(value).Equals(Normalize(choice.Value), StringComparison.OrdinalIgnoreCase)) == true,
        PanelEvidenceMatchKind.TissuePhrase => evidence?.TissueSpecificity?.Any(value => Normalize(value).Equals(Normalize(choice.Value), StringComparison.OrdinalIgnoreCase)) == true,
        PanelEvidenceMatchKind.Pfam => evidence?.Pfam?.Any(domain => Normalize(domain.Accession).Equals(Normalize(choice.Value), StringComparison.OrdinalIgnoreCase)) == true,
        _ => false,
    };
}
