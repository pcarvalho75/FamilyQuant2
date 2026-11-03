using FamilyQuant.Report;
using System;
using System.Collections.Generic;
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
public sealed class ReportExportViewModel
{
    private readonly HashSet<string> excluded;
    public IReadOnlyList<ReportChoice> Choices { get; }
    public ReportExportViewModel(ReportDocument document, ReportSelection? stored)
    {
        ArgumentNullException.ThrowIfNull(document);
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
    public ReportSelection Selection()
    {
        var selected = new HashSet<string>(excluded, StringComparer.Ordinal);
        foreach (var choice in Choices)
            if (choice.IsIncluded) selected.Remove(choice.Id); else selected.Add(choice.Id);
        return new() { ExcludedIds = selected.OrderBy(id => id, StringComparer.Ordinal).ToArray() };
    }
}
