using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant;

/// <summary>Shared source and filter settings for Quantify and De novo. No score conversion.</summary>
public sealed class DeNovoFilterViewModel : ScreenViewModel
{
    private string mode = "peaks-only", peaks = "80", deep = "80", length = "7", stretch = "5", floor = "90";
    public string ModeTag { get => mode; set { if (Set(ref mode, value)) { Changed(nameof(UsesPeaks)); Changed(nameof(UsesDeepNovo)); } } }
    public string PeaksMinimumAlc { get => peaks; set => Set(ref peaks, value); }
    public string DeepNovoMinimumScore { get => deep; set => Set(ref deep, value); }
    public string MinimumLength { get => length; set => Set(ref length, value); }
    public string StretchLength { get => stretch; set => Set(ref stretch, value); }
    public string StretchFloor { get => floor; set => Set(ref floor, value); }
    public bool UsesPeaks => ModeTag is "peaks-only" or "peaks-all" or "union-only-deepnovo" or "union-all-deepnovo";
    public bool UsesDeepNovo => ModeTag is "deepnovo" or "union-only-deepnovo" or "union-all-deepnovo";

    public DeNovoFilterViewModel(QuantManifest? manifest = null)
    {
        var inputs = manifest?.DeNovoInputs ?? new();
        bool only = inputs.Any(i => i.SourceId == DeNovoSource.DenovoOnly.Id);
        bool all = inputs.Any(i => i.SourceId == DeNovoSource.DeNovoAll.Id);
        bool dn = inputs.Any(i => i.SourceId == DeNovoSource.DeepNovo.Id);
        ModeTag = dn ? only ? "union-only-deepnovo" : all ? "union-all-deepnovo" : "deepnovo"
            : all ? "peaks-all" : "peaks-only";
        var p = inputs.FirstOrDefault(i => i.SourceId == DeNovoSource.DenovoOnly.Id || i.SourceId == DeNovoSource.DeNovoAll.Id);
        var d = inputs.FirstOrDefault(i => i.SourceId == DeNovoSource.DeepNovo.Id);
        if (p != null) PeaksMinimumAlc = p.MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture);
        if (d != null) DeepNovoMinimumScore = d.MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture);
        var saved = manifest?.DeNovoSourceScoreDistributions.FirstOrDefault();
        if (saved != null)
        {
            MinimumLength = saved.MinLength.ToString(CultureInfo.InvariantCulture);
            StretchLength = saved.StretchLength.ToString(CultureInfo.InvariantCulture);
            StretchFloor = saved.StretchFloor.ToString(CultureInfo.InvariantCulture);
        }
        else if (manifest?.DeNovoScoreDistribution is { } legacy)
        {
            MinimumLength = legacy.MinLength.ToString(CultureInfo.InvariantCulture);
            StretchLength = legacy.StretchLength.ToString(CultureInfo.InvariantCulture);
            StretchFloor = legacy.StretchFloor.ToString(CultureInfo.InvariantCulture);
        }
    }

    public bool TryBuild(out DeNovoFilterCriteria criteria, out IReadOnlyList<DeNovoSourceSelection> selections, out string error)
    {
        criteria = new();
        selections = Array.Empty<DeNovoSourceSelection>();
        error = "";
        if (!UsesPeaks && !UsesDeepNovo) { error = "Choose a de novo source."; return false; }
        if (!int.TryParse(MinimumLength, out int min) || min < 1 || min > 100)
        { error = "Minimum length must be a whole number from 1 to 100."; return false; }
        if (!int.TryParse(StretchLength, out int need) || need < 1 || need > 100)
        { error = "Stretch length must be a whole number from 1 to 100."; return false; }
        if (!Number(StretchFloor, out double local))
        { error = "Stretch confidence must be a number from 0 to 100."; return false; }
        double alc = 80, score = 80;
        if (UsesPeaks && !Number(PeaksMinimumAlc, out alc))
        { error = "PEAKS minimum ALC must be a number from 0 to 100."; return false; }
        if (UsesDeepNovo && !Number(DeepNovoMinimumScore, out score))
        { error = "DeepNovo minimum SCORE must be a number from 0 to 100."; return false; }
        criteria = new() { MinLength = min, MinAverageLocalConfidence = UsesPeaks ? alc : score,
            StretchLength = need, StretchFloor = (int)Math.Round(local, MidpointRounding.AwayFromZero) };
        var selected = new List<DeNovoSourceSelection>();
        if (UsesPeaks) selected.Add(new(ModeTag is "peaks-all" or "union-all-deepnovo"
            ? DeNovoSource.DeNovoAll : DeNovoSource.DenovoOnly, alc, 0));
        if (UsesDeepNovo) selected.Add(new(DeNovoSource.DeepNovo, score, selected.Count));
        selections = selected;
        return true;
    }

    private static bool Number(string text, out double value) =>
        (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        && double.IsFinite(value) && value >= 0 && value <= 100;
}
