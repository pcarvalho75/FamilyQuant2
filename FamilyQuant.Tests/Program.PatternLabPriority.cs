using FamilyQuant.DeNovo;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static QuantRow PriorityQuant(string side, int scan, string sequence, double mz = 500, int charge = 2, double m0 = 4) => new()
    {
        Run = "fixture", Side = side, Scan = scan, Sequence = sequence, PrecursorMz = mz, Charge = charge,
        IsotopeAreas = new[] { m0, m0 }, EnvelopeSum = 2 * m0, PatternLabXScore = side == NativeQuantFractions.SideIdentified ? 4 : null,
    };

    private static void PatternLabPriorityTests(string workDir)
    {
        EnsureApplication(); Directory.CreateDirectory(workDir);
        var psm = new PatternLabPsm { FileName = "fixture", ScanNumber = 10, Sequence = "AAAAAAAK", ChargeState = 2, PrecursorMz = 500, XCorr = 4 };
        var identified = PriorityQuant(NativeQuantFractions.SideIdentified, 10, psm.Sequence, m0: 10);
        var deNovo = PriorityQuant(NativeQuantFractions.SideDeNovo, 10, "PEPTIDEK");
        var rows = new List<QuantRow> { identified, deNovo };
        string original = JsonSerializer.Serialize(rows);
        var enabled = SpectrumComparisonTable.ApplyPatternLabConflictRule(Array.Empty<PeaksDeNovoRow>(), new[] { psm }, rows, true);
        var disabled = SpectrumComparisonTable.ApplyPatternLabConflictRule(Array.Empty<PeaksDeNovoRow>(), new[] { psm }, rows, false);
        Check("Z6c PatternLab wins a different-sequence same-precursor call without altering measured input anchors",
            enabled.Rows.SequenceEqual(new[] { identified }) && enabled.ExcludedAnchors == 1
            && enabled.ExcludedByRun["fixture"] == 1 && JsonSerializer.Serialize(rows) == original);
        Check("Z6c OFF preserves all prior quantities and row bytes", disabled.ExcludedAnchors == 0 && JsonSerializer.Serialize(disabled.Rows) == original);
        foreach (string change in new[] { "scan", "charge", "mass", "unknown charge", "unknown mass", "equal", "PTM" })
        {
            var q = PriorityQuant(NativeQuantFractions.SideDeNovo, change == "scan" ? 11 : 10,
                change == "equal" ? "AAAAAAAK" : change == "PTM" ? "AAAAAAAK(+15.99)" : "PEPTIDEK",
                mz: change == "mass" ? 600 : change == "unknown mass" ? 0 : 500,
                charge: change == "charge" ? 3 : change == "unknown charge" ? 0 : 2);
            var kept = SpectrumComparisonTable.ApplyPatternLabConflictRule(Array.Empty<PeaksDeNovoRow>(), new[] { psm }, new[] { identified, q }, true);
            Check("Z6c " + change + " does not become a different-sequence same-precursor exclusion", kept.Rows.Contains(q) && kept.ExcludedAnchors == 0);
        }
        var ilPsm = new PatternLabPsm { FileName = "fixture", ScanNumber = 10, Sequence = "PEPTLDEK", ChargeState = 2, PrecursorMz = 500, XCorr = 4 };
        Check("Z6c I and L are equivalent for the scan rule", SpectrumComparisonTable.ApplyPatternLabConflictRule(
            Array.Empty<PeaksDeNovoRow>(), new[] { ilPsm }, new[] { deNovo }, true).ExcludedAnchors == 0);
        var isotope = PriorityQuant(NativeQuantFractions.SideDeNovo, 10, "PEPTIDEK", 500 + PeptidoformIdentity.Carbon13SpacingDa / 2);
        Check("Z6c isotope-selection offsets use the native same-precursor join", SpectrumComparisonTable.ApplyPatternLabConflictRule(
            Array.Empty<PeaksDeNovoRow>(), new[] { psm }, new[] { isotope }, true).ExcludedAnchors == 1);
        var near = PriorityQuant(NativeQuantFractions.SideDeNovo, 10, "PEPTIDEK", 500.009);
        var far = PriorityQuant(NativeQuantFractions.SideDeNovo, 10, "WWWWWWWK", 500.018);
        var nonChaining = SpectrumComparisonTable.ApplyPatternLabConflictRule(Array.Empty<PeaksDeNovoRow>(), new[] { psm }, new[] { near, far }, true);
        Check("Z6c a chain of near precursor masses does not merge incompatible hypotheses", !nonChaining.Rows.Contains(near) && nonChaining.Rows.Contains(far));
        Check("Z6c an identification wins even when it yielded no stored identified XIC", SpectrumComparisonTable.ApplyPatternLabConflictRule(
            Array.Empty<PeaksDeNovoRow>(), new[] { psm }, new[] { deNovo }, true).Rows.Count == 0);
        var nonconflictingAnchor = PriorityQuant(NativeQuantFractions.SideDeNovo, 11, deNovo.Sequence, m0: 2);
        var remaining = SpectrumComparisonTable.ApplyPatternLabConflictRule(Array.Empty<PeaksDeNovoRow>(), new[] { psm }, new[] { identified, deNovo, nonconflictingAnchor }, true);
        var tics = new Dictionary<string, double> { ["fixture"] = 100 };
        var before = NativeQuantFractions.Compute(new[] { identified, deNovo, nonconflictingAnchor }, tics).Single(f => !f.IsGrouped);
        var after = NativeQuantFractions.Compute(remaining.Rows, tics).Single(f => !f.IsGrouped);
        Check("Z6c removing a conflicting representative lets a nonconflicting anchor supply the remaining peptide signal",
            before.DeNovoEnvelopeReferenceCalibrated == 8 && after.DeNovoEnvelopeReferenceCalibrated == 4 && after.DeNovoPeptides == 1);
        var defaultModel = new ComputeXicViewModel("fixture"); var defaultScreen = new QuantifyControl(defaultModel);
        PumpDispatcher();
        Check("Z6c new Compute and the visible option default ON", new FullXicOptions().PatternLabWinsScanConflicts
            && defaultModel.PatternLabWinsScanConflicts
            && ((CheckBox)defaultScreen.FindName("CheckBoxPatternLabWinsScanConflicts")).IsChecked == true);
        foreach (bool choice in new[] { false, true })
        {
            var manifest = new QuantManifest { IsotopeCount = 2, MinPointsForQuant = 3, PatternLabWinsScanConflicts = choice,
                PerRunTic = tics, PatternLabConflictExcludedAnchorsByRun = enabled.ExcludedByRun };
            string path = Path.Combine(workDir, "scan-rule-" + choice.ToString() + ".fqproj");
            ProjectStore.WriteFqproj(path, new FamilyClusterFile(), rows, manifest);
            var saved = ProjectStore.ReadFqproj(path);
            Check("Z6c the " + choice.ToString() + " choice and exclusion counts survive reopening",
                saved.manifest.PatternLabWinsScanConflicts == choice && saved.manifest.PatternLabConflictExcludedAnchorsByRun?["fixture"] == 1
                && new ComputeXicViewModel("fixture", saved.manifest).PatternLabWinsScanConflicts == choice);
            var rule = ReportMethods.Build(saved.fcf, saved.manifest).Blocks.OfType<ReportTable>().Single(t => t.Id == "methods-rules")
                .Rows.Single(r => r[0] == "Same-scan de novo conflict rule")[1];
            Check("Z6c the report states the recorded " + choice.ToString() + " rule", rule == (choice ? SpectrumComparisonTable.PatternLabConflictRule : SpectrumComparisonTable.KeepConflictingCallsRule));
        }
        var legacy = new QuantManifest { IsotopeCount = 2, PerRunTic = tics };
        string legacyBefore = JsonSerializer.Serialize(legacy), rowBefore = JsonSerializer.Serialize(rows);
        var nextCompute = new ComputeXicViewModel("fixture", legacy); nextCompute.PatternLabWinsScanConflicts = false;
        Check("Z6c opening or editing the next Compute option never changes old stored results", legacy.PatternLabWinsScanConflicts == null
            && JsonSerializer.Serialize(legacy) == legacyBefore && JsonSerializer.Serialize(rows) == rowBefore);
        string raw = Path.Combine(workDir, "requests"), package = Path.Combine(workDir, "priority.sepr2");
        Directory.CreateDirectory(raw); CreateShellSeproFixture(Array.Empty<(string, int)>()).Save(package);
        foreach (bool choice in new[] { false, true })
        {
            bool? requested = null;
            var model = new ComputeXicViewModel("fixture", legacy, compute: (request, _, _) =>
            {
                requested = request.Options.PatternLabWinsScanConflicts;
                return Task.FromResult(new FullXicComputeResult { Manifest = new QuantManifest { PatternLabWinsScanConflicts = requested } });
            }) { RawDirectory = raw, SeproPackage = package, PatternLabWinsScanConflicts = choice };
            var task = model.ComputeAsync(); FinishBusyTask(task); task.GetAwaiter().GetResult();
            Check("Z6c the real Compute request forwards the " + choice.ToString() + " scan-rule choice", model.Computed && requested == choice
                && legacy.PatternLabWinsScanConflicts == null && JsonSerializer.Serialize(legacy) == legacyBefore);
        }
    }
}
