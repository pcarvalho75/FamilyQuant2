using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;

namespace FamilyQuant.Panels;

public sealed record SubclassLeaveOneOutRow(int ProteinIndex, string Identifier, string PanelFamilyKey,
    string ExpectedSubclassKey, string? PredictedSubclassKey, string Outcome, string Reason);

/// <summary>Reviewed targets with a panel-matched source subfamily; prediction coverage and accuracy use distinct denominators.</summary>
public sealed class SubclassLeaveOneOutResult
{
    public IReadOnlyList<SubclassLeaveOneOutRow> Rows { get; init; } = Array.Empty<SubclassLeaveOneOutRow>();
    public int Eligible => Rows.Count;
    public int Right => Rows.Count(r => r.Outcome == "Right");
    public int Wrong => Rows.Count(r => r.Outcome == "Wrong");
    public int NoPrediction => Rows.Count(r => r.Outcome == "No prediction");
    public double? PredictionRate => Eligible == 0 ? null : (Right + Wrong) / (double)Eligible;
    public double? AccuracyAmongPredictions => Right + Wrong == 0 ? null : Right / (double)(Right + Wrong);
    public string PanelFingerprint { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
}

public static partial class SubclassAssignment
{
    /// <summary>Invariant per-target audit; a missing prediction is not counted as a wrong prediction.</summary>
    public static string BuildLeaveOneOutCsv(SubclassLeaveOneOutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var csv = new StringBuilder("Protein index,Identifier,Panel family,Expected subclass,Predicted subclass,Outcome,Reason\r\n");
        foreach (var row in result.Rows)
        {
            var fields = new[] { row.ProteinIndex.ToString(CultureInfo.InvariantCulture), row.Identifier,
                row.PanelFamilyKey, row.ExpectedSubclassKey, row.PredictedSubclassKey, row.Outcome, row.Reason };
            csv.Append(string.Join(",", fields.Select(CsvFormatting.Guarded))).Append("\r\n");
        }
        return csv.ToString();
    }

    /// <summary>Measure only on a project whose applied family assignment evidence is current.</summary>
    public static SubclassLeaveOneOutResult MeasureLeaveOneOutCurrent(FamilyClusterFile file, OperationContext? operation = null)
    {
        PanelAssignmentEvidenceStore.RequireCurrent(file);
        string panel = PanelDerivedDataCurrentness.Fingerprint(file.FamilyPanel!);
        string before = SourceFingerprint(file, file.EvidenceRecipe!.Options, panel);
        var resolver = PanelEvidenceClassifier.CreateCurrentResolver(file);
        var result = MeasureLeaveOneOutCore(file, file.EvidenceRecipe.Options, operation, resolver);
        PanelAssignmentEvidenceStore.RequireCurrent(file);
        if (result.SourceFingerprint != before
            || !resolver.IsCurrent(file)
            || SourceFingerprint(file, file.EvidenceRecipe!.Options, PanelDerivedDataCurrentness.Fingerprint(file.FamilyPanel!)) != before)
            throw new InvalidOperationException("Subclass inputs changed during the leave-one-out measurement.");
        return result;
    }

    /// <summary>Mask each reviewed query's own subclass, and search all other reviewed references together.
    /// The result measures neighbour transfer only; it is a candidate until the family assignments' provenance is checked.</summary>
    public static SubclassLeaveOneOutResult MeasureLeaveOneOutCandidate(FamilyClusterFile file, EvidenceClassificationOptions options) =>
        MeasureLeaveOneOutCandidate(file, options, null);

    public static SubclassLeaveOneOutResult MeasureLeaveOneOutCandidate(FamilyClusterFile file, EvidenceClassificationOptions options,
        OperationContext? operation) => MeasureLeaveOneOutCore(file, options, operation, null);

    private static SubclassLeaveOneOutResult MeasureLeaveOneOutCore(FamilyClusterFile file, EvidenceClassificationOptions options,
        OperationContext? operation, PanelFamilyResolver? resolver)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);
        // Validate panel, assignments and target identities before identifying the scored cohort.
        var baseline = BuildCandidateCore(file, options, operation, new HashSet<int>(), resolver);
        var baselineRows = baseline.Labels.ToDictionary(r => r.ProteinIndex);
        var panel = file.FamilyPanel!;
        var families = panel.Families.ToDictionary(f => f.Key, StringComparer.Ordinal);
        var truth = new Dictionary<int, (string Family, string Subclass)>();
        foreach (var assignment in file.PanelProteinAssignments!)
        {
            operation?.ThrowIfCancellationRequested();
            var protein = file.MyExtendedFastaItems[assignment.ProteinIndex];
            var evidence = protein.Evidence;
            if (!assignment.IsApplied || string.IsNullOrEmpty(assignment.PanelFamilyKey) || evidence?.Reviewed != true
                || baselineRows[assignment.ProteinIndex].PanelFamilyKey == null) continue;
            string? familyKey = FamilyPanelValidation.ResolveFamilyKey(panel, assignment.PanelFamilyKey);
            if (familyKey == null) continue;
            var textOnly = new ProteinEvidence { Family = evidence.Family, Subfamily = evidence.Subfamily, FamilyAnnotation = evidence.FamilyAnnotation,
                FamilyMemberships = evidence.FamilyMemberships };
            var expected = ClassifyDirect(families[familyKey], textOnly);
            if (expected.Status == ProteinSubclassStatus.Assigned && expected.SubclassKey != null)
                truth.Add(assignment.ProteinIndex, (familyKey, expected.SubclassKey));
        }
        var candidate = BuildCandidateCore(file, options, operation, truth.Keys.ToHashSet(), resolver);
        if (candidate.SourceFingerprint != baseline.SourceFingerprint || candidate.PanelFingerprint != baseline.PanelFingerprint)
            throw new InvalidOperationException("Subclass inputs changed during the leave-one-out measurement.");
        var byIndex = candidate.Labels.ToDictionary(r => r.ProteinIndex);
        var rows = truth.OrderBy(t => t.Key).Select(t =>
        {
            var predicted = byIndex[t.Key];
            string? actual = predicted.Status == ProteinSubclassStatus.Assigned ? predicted.SubclassKey : null;
            string outcome = actual == null ? "No prediction" : actual == t.Value.Subclass ? "Right" : "Wrong";
            return new SubclassLeaveOneOutRow(t.Key, predicted.Identifier, t.Value.Family, t.Value.Subclass,
                actual, outcome, predicted.Reason);
        }).ToArray();
        return new SubclassLeaveOneOutResult { Rows = rows, PanelFingerprint = candidate.PanelFingerprint,
            SourceFingerprint = candidate.SourceFingerprint };
    }
}
