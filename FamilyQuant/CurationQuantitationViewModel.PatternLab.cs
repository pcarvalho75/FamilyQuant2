using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class CurationQuantitationViewModel
{
    private PatternLabProject? currentPLP;
    public PatternLabProject? CurrentPLP
    {
        get => currentPLP;
        set { if (Set(ref currentPLP, value)) NotifyPresentationState(); }
    }
    public QuantitationProfile? CurrentProfile { get; set; }
    public NormalizationMode? DisplayedMode { get; set; }
    public SharedPeptidePolicy DisplayedSharedPolicy { get; set; } = SharedPeptidePolicy.FamilyUnique;

    internal sealed record PatternLabPrepared(PatternLabProject Project, QuantitationProfile Profile,
        FamilyClusterFile Calculated, NormalizationMode Mode, SharedPeptidePolicy Policy,
        IReadOnlyList<(FastaItemExtended Item, int Family, string Annotation)> Source);

    internal Task<PatternLabPrepared> PreparePatternLabAsync(string path, NormalizationMode requested,
        SharedPeptidePolicy policy, CancellationToken cancellation) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        var loaded = new PatternLabProject(new FileInfo(path));
        var diagnostic = new StringBuilder();
        diagnostic.AppendLine("=== PLP NormalizationFactor Diagnostic ===");
        diagnostic.AppendLine($"Secondary Labels Count: {loaded.SecondaryLabels?.Count ?? 0}");
        if (loaded.SecondaryLabels != null)
            foreach (var label in loaded.SecondaryLabels)
                diagnostic.AppendLine($"  File: {Path.GetFileNameWithoutExtension(label.Name)}, NormalizationFactor: {label.NormalizationFactor}");
        Console.WriteLine(diagnostic.ToString());
        cancellation.ThrowIfCancellationRequested();
        var profile = QuantitationProfile.Describe(loaded);
        Console.WriteLine(profile.Summary());
        var mode = requested;
        bool available = mode switch
        {
            NormalizationMode.TotalChromatogram => profile.CanReportTotalChromatogram,
            NormalizationMode.IdentifiedPeptideSignal => NormalizationPlan.For(loaded, NormalizationInfo.TagIdentified).IsAvailable,
            NormalizationMode.ChargeRestrictedSignal => NormalizationPlan.For(loaded, NormalizationInfo.TagChargeRestricted).IsAvailable,
            _ => true
        };
        if (!available) mode = NormalizationMode.IdentifiedSignal;
        var source = Document.MyExtendedFastaItems.Select(p => (p, p.UserID, p.UserAnnotation ?? "")).ToArray();
        var staged = Document.DeepCopy();
        cancellation.ThrowIfCancellationRequested();
        staged.SetLookupDictionary(loaded);
        staged.AttributeXICToGroups(loaded, mode, policy);
        cancellation.ThrowIfCancellationRequested();
        return new PatternLabPrepared(loaded, profile, staged, mode, policy, source);
    }, cancellation);

    internal void PublishPatternLab(PatternLabPrepared prepared)
    {
        if (Document.MyExtendedFastaItems.Count != prepared.Source.Count
            || Document.MyExtendedFastaItems.Where((p, i) => !ReferenceEquals(p, prepared.Source[i].Item)
                || p.UserID != prepared.Source[i].Family || (p.UserAnnotation ?? "") != prepared.Source[i].Annotation).Any())
            throw new InvalidOperationException("The curation changed while PatternLab was loading. Load it again for the current families.");
        Document.SetLookupDictionary(prepared.Project);
        for (int i = 0; i < prepared.Source.Count; i++)
            Document.MyExtendedFastaItems[i].XICUser = prepared.Calculated.MyExtendedFastaItems[i].XICUser;
        CurrentPLP = prepared.Project; CurrentProfile = prepared.Profile;
        DisplayedMode = prepared.Mode; DisplayedSharedPolicy = prepared.Policy;
        WriteRecalculationDiagnostic(prepared.Mode, prepared.Policy);
    }

    public void Recalculate(NormalizationMode mode, SharedPeptidePolicy policy)
    {
        if (CurrentPLP == null) return;
        Document.AttributeXICToGroups(CurrentPLP, mode, policy);
        DisplayedMode = mode; DisplayedSharedPolicy = policy;
        WriteRecalculationDiagnostic(mode, policy);
    }

    private void WriteRecalculationDiagnostic(NormalizationMode mode, SharedPeptidePolicy shared)
    {
        var diagnostic = new StringBuilder();
        diagnostic.AppendLine("=== Recalculation Diagnostic ===");
        diagnostic.AppendLine($"Mode: {mode.DisplayName()}");
        diagnostic.AppendLine($"Shared peptides: {shared.DisplayName()}");
        diagnostic.AppendLine($"Items with XIC > 0: {Document.MyExtendedFastaItems.Count(p => p.XIC > 0)}");
        diagnostic.AppendLine($"Items with XICUser > 0: {Document.MyExtendedFastaItems.Count(p => p.XICUser > 0)}");
        diagnostic.AppendLine($"Total XIC sum: {Document.MyExtendedFastaItems.Sum(p => p.XIC):F6}");
        diagnostic.AppendLine($"Total XICUser sum: {Document.MyExtendedFastaItems.Sum(p => p.XICUser):F6}");
        diagnostic.AppendLine($"Max XIC: {Document.MyExtendedFastaItems.Select(p => p.XIC).DefaultIfEmpty(0).Max():F6}");
        diagnostic.AppendLine($"Max XICUser: {Document.MyExtendedFastaItems.Select(p => p.XICUser).DefaultIfEmpty(0).Max():F6}");
        diagnostic.AppendLine("\nTop 5 by XIC:");
        foreach (var protein in Document.MyExtendedFastaItems.OrderByDescending(p => p.XIC).Take(5))
            diagnostic.AppendLine($"  {protein.SequenceIdentifier}: XIC={protein.XIC:F6}, XICUser={protein.XICUser:F6}");
        Console.WriteLine(diagnostic.ToString());
    }
}
