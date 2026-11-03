using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant;

public sealed class ReportChoice : ScreenViewModel
{
    private bool included;
    public string Id { get; }
    public string Label { get; }
    public bool IsFigure { get; }
    public bool IsIncluded { get => included; set => Set(ref included, value); }
    internal ReportChoice(string id, string label, bool isFigure, bool included)
    { Id = id; Label = label; IsFigure = isFigure; this.included = included; }
}

/// <summary>Checkbox draft over the report's own stable ids. Unavailable ids remain stored.</summary>
public sealed class ReportExportViewModel : ScreenViewModel
{
    private readonly HashSet<string> excluded;
    private readonly double originalThreshold;
    private readonly decimal originalPercent;
    private decimal? thresholdPercent;
    public decimal? ThresholdPercent { get => thresholdPercent; set => Set(ref thresholdPercent, value); }
    public IReadOnlyList<ReportChoice> Choices { get; }
    public ReportExportViewModel(ReportDocument document, ReportSelection? stored, double threshold = FamilyReportThreshold.DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!double.IsFinite(threshold) || threshold < 0 || threshold > 1) throw new ArgumentOutOfRangeException(nameof(threshold));
        originalThreshold = threshold;
        originalPercent = decimal.Parse(((decimal)threshold * 100m).ToString("G29", CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
        ThresholdPercent = originalPercent;
        excluded = new(stored?.ExcludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var choices = new List<ReportChoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string id, string label, bool figure)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                throw new InvalidDataException("The report contains an empty or repeated section or figure id.");
            choices.Add(new(id, label, figure, !excluded.Contains(id)));
        }
        foreach (var section in document.Sections)
        {
            Add(section.Id, section.Title, false);
            foreach (var figure in section.Blocks.OfType<ReportFigure>()) Add(figure.Id, figure.Title, true);
        }
        Choices = choices.AsReadOnly();
    }
    public bool TryThreshold(out double threshold)
    {
        threshold = originalThreshold;
        if (ThresholdPercent is not decimal percent || percent < 0m || percent > 100m) return false;
        // An untouched localized editor must not round an existing stored double.
        if (percent != originalPercent) threshold = (double)(percent / 100m);
        return true;
    }
    public ReportSelection Selection()
    {
        var selected = new HashSet<string>(excluded, StringComparer.Ordinal);
        foreach (var choice in Choices)
            if (choice.IsIncluded) selected.Remove(choice.Id); else selected.Add(choice.Id);
        return new() { ExcludedIds = selected.OrderBy(id => id, StringComparer.Ordinal).ToArray() };
    }
}
