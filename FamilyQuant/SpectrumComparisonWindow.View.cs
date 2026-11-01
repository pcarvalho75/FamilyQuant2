using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public static partial class SpectrumComparisonWindow
{
    private static Grid CreateView(SpectrumComparisonViewModel model, Window window)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(SecondaryWindowView.Text("Compare the source calls and the two quantitation bases for each precursor hypothesis.", true));
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10), VerticalAlignment = VerticalAlignment.Center };
        var count = SecondaryWindowView.Text(model.CountSummary);
        count.Margin = new Thickness(0, 0, 16, 0); count.VerticalAlignment = VerticalAlignment.Center;
        count.ToolTip = "One scan can have several rows, one per compatible precursor hypothesis. A blank is missing evidence, not a zero score or a disagreement.";
        bar.Children.Add(count);
        var save = new Button { Name = "ButtonSaveSpectrumComparisonCsv", Content = "Save a copy (CSV)", IsEnabled = model.CanSave,
            ToolTip = "Save exactly these rows as CSV. Scores stay in separate columns, missing values stay blank, and the project is not changed." };
        save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        bar.Children.Add(save);
        var score = new Button { Name = "ButtonSaveAcceptedScoreScatter", Content = "Save accepted-call score plot",
            DataContext = model.ScoreScatter, IsEnabled = model.ScoreScatter.Total > 0,
            ToolTip = "Save a 1920 x 1080 PNG and its CSV: one dot per row where both PEAKS and DeepNovo passed their filters. Blue circles are sequence matches; red diamonds are mismatches." };
        bar.Children.Add(score);
        bar.Children.Add(SecondaryWindowView.Help("Spectrum comparison help", SpectrumComparisonViewModel.Explanation, window));
        Grid.SetRow(bar, 1); root.Children.Add(bar);
        var table = BuildGrid(model.Rows); Grid.SetRow(table, 2); root.Children.Add(table);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var path = SecondaryWindowView.Text(model.PathSummary, true);
        path.ToolTip = "The CSV saved automatically by the latest computation, if any. Saving a copy does not change the project.";
        footer.Children.Add(path);
        var status = SecondaryWindowView.Text("", true);
        status.ToolTip = "Where the export was saved, or why it could not be written.";
        footer.Children.Add(status); Grid.SetRow(footer, 3); root.Children.Add(footer);
        save.Click += (_, _) => SaveCopy(save, status, model);
        score.Click += (_, _) => SaveAcceptedScoreScatter(score, status, model.ScoreScatter, model.ExistingCsvPath);
        return root;
    }
}
