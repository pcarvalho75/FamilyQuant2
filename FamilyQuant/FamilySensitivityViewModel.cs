using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed record FamilySensitivityDisplayRow(int FamilyUserId, string Family, string Policy, string First, string Second);

/// <summary>Thin presentation over the persisted sensitivity study. Input projects are read-only.</summary>
public sealed class FamilySensitivityViewModel : ScreenViewModel
{
    private readonly IReadOnlyList<QuantRow>? rows;
    private readonly QuantManifest? manifest;
    private readonly FamilyClusterFile? curation;
    private readonly PeptideFamilyAttribution? mapping;
    private readonly Action changed;
    private string status = "";
    public string Status { get => status; private set => Set(ref status, value); }
    public IReadOnlyList<FamilySensitivityDisplayRow> Rows { get; private set; } = Array.Empty<FamilySensitivityDisplayRow>();
    public string FirstHeader => manifest?.FamilySensitivity?.Variants.FirstOrDefault()?.Label ?? "Current result";
    public string SecondHeader => manifest?.FamilySensitivity?.Variants.Skip(1).FirstOrDefault()?.Label ?? "Comparison unavailable";
    public bool CanCalculate => rows is { Count: > 0 } && manifest != null && curation != null;
    public bool HasData => Rows.Count > 0;
    public string Explanation => FamilySensitivityTable.Explanation;

    public FamilySensitivityViewModel(IReadOnlyList<QuantRow>? rows, QuantManifest? manifest, FamilyClusterFile? curation,
        PeptideFamilyAttribution? mapping, Action changed)
    {
        this.rows = rows; this.manifest = manifest; this.curation = curation; this.mapping = mapping; this.changed = changed;
        Refresh();
    }

    public void Refresh()
    {
        try
        {
            var stored = manifest?.FamilySensitivity;
            if (stored != null && curation != null
                && FamilyStudyMembership.Problem(curation, new QuantManifest { FamilySensitivity = stored }) is string membershipProblem)
                throw new InvalidDataException(membershipProblem + " Choose Calculate to refresh this sensitivity study.");
            if (stored != null && curation != null && stored.CurationFingerprint != FamilySensitivityStudy.CurationFingerprint(curation))
                throw new InvalidDataException("Stored sensitivity belongs to a different family grouping. Calculate it again.");
            if (stored != null && CanCalculate && stored.Variants.FirstOrDefault()?.SourceSha256 != FamilySensitivityStudy.CurrentFingerprint(rows!, manifest!, curation!))
                throw new InvalidDataException("The stored sensitivity no longer matches the current quantitation. Calculate it again.");
            Rows = FamilySensitivityTable.Rows(stored).Select(r => new FamilySensitivityDisplayRow(r.FamilyUserId, r.Description,
                r.Policy.DisplayName(), Display(r.FirstShare), Display(r.SecondShare))).ToArray();
            Status = stored == null ? "No sensitivity study is stored. Calculate the current result or compare a saved project."
                : Rows.Count == 0 ? "No measurable family signal is stored for these variants." : "";
        }
        catch (InvalidDataException ex) { Rows = Array.Empty<FamilySensitivityDisplayRow>(); Status = ex.Message; }
        Changed(nameof(Rows)); Changed(nameof(FirstHeader)); Changed(nameof(SecondHeader)); Changed(nameof(HasData));
    }

    public async Task CalculateAsync(string? comparisonPath, AppOperationHost operations)
    {
        if (!CanCalculate || operations.IsBusy) return;
        using var operation = operations.Begin("Prepare sensitivity table");
        var previous = manifest!.FamilySensitivity;
        bool committed = false;
        try
        {
            FamilySensitivityResult prepared = await Task.Run(() =>
            {
                operation.Context.Report("sensitivity", "Reading stored quantitation and preparing family shares");
                var current = FamilySensitivityStudy.Build(rows!, manifest, curation!, mapping, operation.Context.CancellationToken);
                if (comparisonPath == null) return current;
                operation.Context.Report("sensitivity", "Reading the computed comparison project");
                string before = ProjectHash(comparisonPath);
                var comparison = ProjectStore.ReadFqproj(comparisonPath, operation.Context);
                if (before != ProjectHash(comparisonPath)) throw new IOException("The comparison project changed while it was read.");
                return FamilySensitivityStudy.Compare(current, comparison.quant, comparison.manifest, comparison.fcf, before,
                    cancellationToken: operation.Context.CancellationToken);
            }, operation.Context.CancellationToken);
            operation.Context.Commit(() =>
            {
                if (!ReferenceEquals(manifest.FamilySensitivity, previous)
                    || prepared.Variants[0].SourceSha256 != FamilySensitivityStudy.CurrentFingerprint(rows!, manifest, curation!))
                    throw new InvalidOperationException("The current project changed while the sensitivity table was prepared.");
                manifest.FamilySensitivity = prepared;
                committed = true;
            });
            changed();
            Refresh();
            if (HasData) Status = "Sensitivity table stored. Save the project to keep it.";
        }
        catch (OperationCanceledException) when (!committed) { Status = "Cancelled. The previous sensitivity table was kept."; }
        catch (Exception ex) { Status = committed ? "The sensitivity table was stored, but the view could not refresh: " + ex.Message
            : "The sensitivity table was not changed: " + ex.Message; }
    }

    public string BuildCsv()
    {
        Refresh();
        if (!HasData || manifest?.FamilySensitivity == null) throw new InvalidOperationException("No current sensitivity table is available.");
        return FamilySensitivityTable.BuildCsv(manifest.FamilySensitivity);
    }

    public void ShowStatus(string message) => Status = message;
    private static string ProjectHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string Display(double? value) => value?.ToString("P2", CultureInfo.CurrentCulture) ?? "Unavailable";
}
