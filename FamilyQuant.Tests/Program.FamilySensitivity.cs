using FamilyQuant.DeNovo;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilySensitivityTests(string output)
    {
        Directory.CreateDirectory(output);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "sp|P00001|ONE", Sequence = "ACDEFGHIKLMNPQRSTV", UserID = 1, UserAnnotation = "Family, one" },
            new() { SequenceIdentifier = "sp|P00002|TWO", Sequence = "LMNPQRSTVWWWWWWW", UserID = 2, UserAnnotation = "Family two" },
        }};
        var rows = new[] { Row("ACDEFGHIK", NativeQuantFractions.SideIdentified, 10), Row("LMNPQRSTV", NativeQuantFractions.SideIdentified, 20), Row("WWWWWWW", NativeQuantFractions.SideDeNovo, 5) };
        var mapping = new PeptideFamilyAttribution();
        mapping.IdentifiedBySequence[DeNovoReconciler.NormalizeSequence("ACDEFGHIK")] = new[] { 1 };
        mapping.IdentifiedBySequence[DeNovoReconciler.NormalizeSequence("LMNPQRSTV")] = new[] { 1, 2 };
        mapping.DeNovoBySequence[DeNovoReconciler.NormalizeSequence("WWWWWWW")] = new[] { 2 };
        var source = Source(80); string before = JsonSerializer.Serialize(rows);
        var study = FamilySensitivityStudy.Build(rows, source, file, mapping);
        var expected = FamilyEnvelopeQuant.BuildFromPeptides(FamilyEnvelopeQuant.PreparePeptides(rows),
            s => mapping.Resolve(NativeQuantFractions.SideIdentified, s), s => mapping.Resolve(NativeQuantFractions.SideDeNovo, s));
        Check("J5e: every policy cell equals the existing production family result", study.Variants.Single().Policies.All(p => p.Families.All(f =>
            f.Signal == expected.Families.Single(e => e.FamilyUserId == f.FamilyUserId).Signal(p.Policy)
            && f.ShareOfMeasuredSignal == f.Signal / expected.MeasuredTotalEnvelopeReferenceCalibrated)));
        Check("J5e: uncomputed cutoff cells are unavailable, never zero", FamilySensitivityTable.Rows(study).All(r => r.SecondShare == null));
        Check("J5e: overlapping full counts are not presented as a normalized composition", study.Variants[0].Policies.Single(p => p.Policy == SharedPeptidePolicy.CountInEveryFamily).PerRunShares == null
            && study.Variants[0].Policies.Where(p => p.Policy != SharedPeptidePolicy.CountInEveryFamily).All(p => p.PerRunShares != null));
        var compared = FamilySensitivityStudy.Compare(study, rows.Take(2).ToArray(), Source(90), file, new string('A', 64), mapping);
        Check("J5e: two actual computed cutoff variants form the six policy columns", compared.Variants.Count == 2
            && FamilySensitivityTable.Rows(compared).All(r => r.FirstShare.HasValue && r.SecondShare.HasValue));
        var badSource = Source(90); badSource.Ppm = 99;
        Check("J5e: differing extraction settings cannot be labelled as cutoff-only sensitivity", Throws<InvalidDataException>(() =>
            FamilySensitivityStudy.Compare(study, rows, badSource, file, new string('A', 64), mapping)));
        var changedIdentity = Source(90); changedIdentity.FamilyMapping!.MinIdentity = 0.9;
        Check("J5e: a different recorded mapping gate is not confused with a cutoff effect", Throws<InvalidDataException>(() =>
            FamilySensitivityStudy.Compare(study, rows, changedIdentity, file, new string('A', 64), mapping))
            && study.Variants[0].Mapping?.MinIdentity == 0);
        Check("J5e: an identical cutoff does not pretend to be a second condition", Throws<InvalidDataException>(() =>
            FamilySensitivityStudy.Compare(study, rows, Source(80), file, new string('A', 64), mapping)));
        var changed = FamilyClusterFile.FromJsonText(file.ToJsonText()); changed.MyExtendedFastaItems[0].UserID = 5;
        Check("J5e: changed family grouping refuses a stale comparison", Throws<InvalidDataException>(() =>
            FamilySensitivityStudy.Compare(study, rows, Source(90), changed, new string('A', 64), mapping)));
        var manifest = Source(80); manifest.FamilySensitivity = compared;
        manifest.FamilyPeptideLocalization = new() { Method = "Synthetic localization fixture", Policy = SharedPeptidePolicy.FamilyUnique,
            MembershipSha256 = FamilyStudyMembership.Fingerprint(file),
            Segments = new() { new() { FamilyUserId = 1, ProteinIdentifier = "sp|P00001|ONE", Side = NativeQuantFractions.SideIdentified,
                NormalizedSequence = "ACDEFGHIK", FamilyMembershipCount = 1, MatchKind = PeptideLocalizationMatchKind.ExactContainment,
                Signal = 10, Intervals = new() { new() { Start = 1, End = 9 } } } } };
        string project = Path.Combine(output, "sensitivity.fqproj");
        ProjectStore.WriteFqproj(project, file, rows, manifest);
        var reopenedManifest = ProjectStore.ReadFqproj(project).manifest;
        var reopened = reopenedManifest.FamilySensitivity;
        Check("J5e: the complete study reopens offline with exact values and provenance", JsonSerializer.Serialize(compared) == JsonSerializer.Serialize(reopened));
        Check("J5e/J4b: both populated optional manifest parts survive one project roundtrip", JsonSerializer.Serialize(manifest.FamilyPeptideLocalization)
            == JsonSerializer.Serialize(reopenedManifest.FamilyPeptideLocalization) && reopened != null && reopenedManifest.FamilyPeptideLocalization?.Segments.Count == 1);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); string first = FamilySensitivityTable.BuildCsv(compared);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("J5e: CSV bytes are culture independent and quote user labels", first == FamilySensitivityTable.BuildCsv(compared) && first.Contains("\"Family, one\"", StringComparison.Ordinal));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Check("J5e: computing the study changes no extraction row", before == JsonSerializer.Serialize(rows));
        string exportedPolicies = FamilySensitivityTable.BuildCsv(compared);
        Check("J5e: exported policy labels equal the wording displayed by the interface", Enum.GetValues<SharedPeptidePolicy>()
            .All(policy => exportedPolicies.Contains(policy.DisplayName(), StringComparison.Ordinal)));
        Check("J5e: an absent study is omitted from legacy manifest JSON", !JsonSerializer.Serialize(new QuantManifest()).Contains("FamilySensitivity", StringComparison.Ordinal));
        Check("J5e: a cancelled preparation publishes no study", Throws<OperationCanceledException>(() => FamilySensitivityStudy.Build(rows, source, file, mapping, new System.Threading.CancellationToken(true))));
        var missingHashes = Source(90); missingHashes.ScientificInputFingerprints.Clear();
        Check("J5e: missing recorded inputs refuse a claimed comparable cutoff variant", Throws<InvalidDataException>(() =>
            FamilySensitivityStudy.Compare(study, rows, missingHashes, file, new string('A', 64), mapping)));
        var corrupt = JsonSerializer.Deserialize<FamilySensitivityResult>(JsonSerializer.Serialize(compared))!;
        corrupt.Variants[0].Policies[0].Families[0].ShareOfMeasuredSignal = 999;
        Check("J5e: a corrupted stored share is refused rather than drawn", Throws<InvalidDataException>(() => FamilySensitivityTable.Rows(corrupt)));
        EnsureApplication(); int dirty = 0;
        var model = new FamilySensitivityViewModel(rows, manifest, file, mapping, () => dirty++);
        var control = new FamilySensitivityControl(model);
        var window = new System.Windows.Window { Content = control, Width = 1200, Height = 600, ShowInTaskbar = false, Left = -12000, Top = -12000 };
        window.Show();
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme); PumpDispatcher();
                var grid = (System.Windows.Controls.DataGrid)control.Template.FindName("SensitivityGrid", control);
                Check("J5e: " + theme + " native table reads the persisted six policy conditions", grid.Items.Count == 6 && model.Rows.All(r => r.First != "Unavailable" && r.Second != "Unavailable"));
                Check("J5e: " + theme + " sensitivity actions belong to the section template", new[] { "CalculateCurrent", "LoadComparison", "ExportTable" }
                    .Select(name => (System.Windows.Controls.Button)control.Template.FindName(name, control)).All(button => ReferenceEquals(button.TemplatedParent, control)));
            }
            var comparisonSource = Source(90);
            string comparisonProject = Path.Combine(output, "computed-comparison.fqproj");
            ProjectStore.WriteFqproj(comparisonProject, file, rows.Take(2), comparisonSource);
            var host = AppOperationHost.For(control);
            var calculate = (System.Windows.Controls.Button)control.Template.FindName("CalculateCurrent", control);
            FinishBusyTask(AppOperationHost.OnUi(control, () =>
            {
                calculate.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                return System.Threading.Tasks.Task.CompletedTask;
            }));
            FinishBusyOperation(host);
            Check("J5e: the section template calculate action still publishes a current study", dirty == 1 && manifest.FamilySensitivity?.Variants.Count == 1);
            FinishBusyTask(AppOperationHost.OnUi(control, () => model.CalculateAsync(comparisonProject, host)));
            Check("J5e: native comparison stores actual project provenance and marks the project changed", dirty == 2 && model.HasData
                && manifest.FamilySensitivity?.Variants[1].SourceSha256 == TestFileHash.Upper(comparisonProject));
            string exactCsv = model.BuildCsv(); var previous = manifest.FamilySensitivity;
            var wrong = Source(90); wrong.Ppm = 99;
            string wrongProject = Path.Combine(output, "incompatible-comparison.fqproj");
            ProjectStore.WriteFqproj(wrongProject, file, rows, wrong);
            FinishBusyTask(AppOperationHost.OnUi(control, () => model.CalculateAsync(wrongProject, host)));
            Check("J5e: an incompatible native comparison retains every prior value and dirty count", ReferenceEquals(previous, manifest.FamilySensitivity)
                && exactCsv == model.BuildCsv() && dirty == 2);
            var edited = FamilyClusterFile.FromJsonText(file.ToJsonText()); edited.MyExtendedFastaItems[0].UserID = 9;
            var stale = new FamilySensitivityViewModel(rows, manifest, edited, mapping, () => { });
            Check("J5e: the table hides a study stale after regrouping", !stale.HasData && stale.Status.Contains("family membership", StringComparison.Ordinal));
        }
        finally { window.Close(); Themes.ThemeManager.Apply(Themes.AppTheme.Dark); }
    }

    private static QuantRow Row(string sequence, string side, double area) => new() { Run = "run", Sequence = sequence, Side = side,
        Charge = 2, IsotopeAreas = new[] { area }, EnvelopeSum = area,
        PrecursorMz = 500 + area, Scan = (int)area + 1,
        DeNovoEvidence = side == NativeQuantFractions.SideDeNovo ? new() { new() { SourceId = DeNovoSource.DeNovoAll.Id, Sequence = sequence, ConfidenceValue = 85 } } : new() };

    private static QuantManifest Source(double cutoff) => new()
    {
        IsotopeCount = 6, MinPointsForQuant = 3, Ppm = 10, Ms1Tolerance = 2,
        PerRunTic = new() { ["run"] = 1000 }, ScientificInputFingerprints = new() { ["raw"] = new string('B', 64) },
        FamilyMapping = new() { MinIdentity = 0, Method = FamilyEnvelopeQuant.AttributionMethod },
        DeNovoInputs = new() { new() { SourceId = DeNovoSource.DeNovoAll.Id, ConfidenceField = "ALC", MinimumConfidence = cutoff, Sha256 = new string('C', 64) } },
    };
}
