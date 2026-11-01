using FamilyQuant.DeNovo;
using System.Collections.Generic;
using System.Windows;

namespace FamilyQuant;

public static partial class SpectrumComparisonWindow
{
    public const string SuggestedFileName = "FamilyQuant_spectrum_comparison.csv";
    public static Window Create(IReadOnlyList<SpectrumComparisonRow>? rows, string? existingCsvPath = null,
        DeNovoAcceptedScoreScatter? persistedScoreScatter = null)
    {
        var model = new SpectrumComparisonViewModel(rows, existingCsvPath, persistedScoreScatter);
        var window = new Window { Title = "FamilyQuant: Spectrum comparison table", DataContext = model,
            Width = 1420, Height = 760, MinWidth = 780, MinHeight = 420 };
        SecondaryWindowView.Theme(window);
        window.Content = SecondaryWindowView.Frame(CreateView(model, window));
        return window;
    }
    public static void Show(IReadOnlyList<SpectrumComparisonRow>? rows, Window? owner = null,
        string? existingCsvPath = null, DeNovoAcceptedScoreScatter? persistedScoreScatter = null)
    {
        Window window = Create(rows, existingCsvPath, persistedScoreScatter);
        if (owner != null && owner.IsLoaded) window.Owner = owner;
        window.Show();
    }
}
