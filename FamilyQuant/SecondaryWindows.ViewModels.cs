using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

public sealed record TextReportViewModel(string Title, string Text, bool Wrap);

public sealed class IdentityScanViewModel
{
    public string Title { get; }
    public List<IdentityScanRow> Rows { get; }
    public string Report { get; }
    public int? CurrentIdentity { get; }
    public bool HasDecoys => Rows.Count > 0 && !Rows.All(r => r.Groups == r.TargetGroups && r.MixedGroups == 0);
    public string Explanation => HasDecoys
        ? "Each point is one USEARCH run. The floor line marks the loosest identity at which no family mixed a target with a decoy: below it, alignments are no better than noise. It is a floor and not an optimum, because two unrelated target families fusing is invisible to this measurement."
        : "Each point is one USEARCH run. This database carries no decoy sequences, so there is no mixing to measure and no floor to mark: choose the threshold from the family counts and from the biology, or re-run on a target-decoy database.";
    public IdentityScanViewModel(string title, List<IdentityScanRow>? rows, string report, int? currentIdentity)
    { Title = title; Rows = rows ?? new(); Report = report; CurrentIdentity = currentIdentity; }
}

public sealed class SpectrumComparisonViewModel
{
    public IReadOnlyList<SpectrumComparisonRow> Rows { get; }
    public DeNovoAcceptedScoreScatter ScoreScatter { get; }
    public string? ExistingCsvPath { get; }
    public bool CanSave => Rows.Count > 0;
    public string CountSummary => Rows.Count.ToString("N0", CultureInfo.CurrentCulture) + " precursor-hypothesis row(s)";
    public string PathSummary => string.IsNullOrWhiteSpace(ExistingCsvPath) ? "No automatic CSV path is available for this view." : "CSV: " + ExistingCsvPath;
    public const string Explanation = "One row per normalized file + MS2 scan + compatible source-reported charge and non-chaining 20-ppm m/z cluster. Alternative sequences share ONE XIC only inside that compatible precursor-hypothesis row; incompatible same-scan metadata stays on separate flagged rows. RAW-selected z/mz remains visible as diagnostic evidence, not a rule that forces all claims in one scan together. Direct M+0 and plot-parity primary quant remain separate.";
    public SpectrumComparisonViewModel(IReadOnlyList<SpectrumComparisonRow>? rows, string? existingCsvPath, DeNovoAcceptedScoreScatter? persistedScoreScatter)
    {
        Rows = rows ?? Array.Empty<SpectrumComparisonRow>(); ExistingCsvPath = existingCsvPath;
        ScoreScatter = persistedScoreScatter ?? DeNovoAcceptedScoreScatterStudy.Build(Rows);
        ScoreScatter.Validate();
    }
    public void SaveCsv(string path) => SpectrumComparisonTable.SaveCsv(path, Rows);
}

public class LockedFamilyRow
{
    public int UserId { get; set; }
    public int Members { get; set; }
    public string Description { get; set; } = "";
    public bool Exists { get; set; }
    public string Presence { get; set; } = "";
}

public sealed class LockManagerViewModel
{
    private readonly FamilyClusterFile file;
    private readonly Action? beforeChange, afterChange;
    public IReadOnlyList<LockedFamilyRow> Rows { get; private set; } = Array.Empty<LockedFamilyRow>();
    public const string Explanation = "These are the families protected from automatic reclustering. A family is locked by typing its number into the User ID column, and by nothing else.\n\nRemoving a lock takes effect at once in the file you have open. Nothing is written to disk here: save the project from the main window to keep the change.\n\nAn unlocked family can be merged again, so the next Recluster by Words or Create Super Cluster may join it to another family.\n\nA row marked no, merged away is a lock that outlived its family. It stays listed because it becomes live again if a family adopts that number.\n\nThe family name is its User Annotation, or else its most common target protein description. If no target remains, the decoy descriptions are used. No protein is removed when a lock is removed.";
    public LockManagerViewModel(FamilyClusterFile file, Action? afterChange = null, Action? beforeChange = null)
    { this.file = file ?? throw new ArgumentNullException(nameof(file)); this.beforeChange = beforeChange; this.afterChange = afterChange; Refresh(); }
    private void Refresh()
    {
        var families = file.MyExtendedFastaItems.GroupBy(i => i.UserID).ToDictionary(g => g.Key, g => g.ToList());
        Rows = file.LockedUserIds.OrderBy(id => id).Select(id =>
        {
            families.TryGetValue(id, out var members);
            bool exists = members?.Count > 0;
            var targets = members?.Where(i => !SequenceLabels.IsDecoy(i.SequenceIdentifier ?? "")).ToList();
            return new LockedFamilyRow { UserId = id, Members = members?.Count ?? 0, Exists = exists,
                Presence = exists ? "yes" : "no, merged away",
                Description = exists ? FamilyNaming.Name(targets!.Count > 0 ? targets : members!) : "" };
        }).ToList();
    }
    public int Remove(IEnumerable<int> ids)
    {
        var displayed = Rows.Select(row => row.UserId).ToHashSet();
        var selected = ids.Distinct().Where(displayed.Contains).ToArray();
        if (selected.Length == 0) return 0;
        beforeChange?.Invoke();
        foreach (int id in selected) file.LockedUserIds.Remove(id);
        Refresh(); afterChange?.Invoke();
        Console.WriteLine("Curation locks removed: " + string.Join(", ", selected.Select(id => id.ToString(CultureInfo.InvariantCulture))));
        return selected.Length;
    }
}
