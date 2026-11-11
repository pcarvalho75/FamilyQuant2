using FamilyQuant.Panels.Import;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilyListDecisionHintTests(string workbook)
    {
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(ManualChoiceFile())));
        var window = new FamilyListImportWindow(model)
        { Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        var previous = ShellMessages.Presenter;
        int questions = 0;
        ShellMessages.Presenter = (_, request) =>
        { questions++; return MessageBoxResult.Yes; };
        try
        {
            window.Show(); FinishReviewWork(() => model.LoadAsync(workbook));
            FinishReviewWork(async () => { await model.NextAsync(); }); PumpDispatcher();
            var decision = model.Columns.Single(c => c.Role == PanelImportFields.Decision);
            string hint = model.DecisionHint;
            Check("the repeated-label hint is visible without mapping Decision", hint.Contains("DECIS", StringComparison.OrdinalIgnoreCase)
                && decision.Header.Length == 0 && window.FindName("DecisionHintText") is TextBlock { IsVisible: true });
            var forward = (Button)window.FindName("ForwardButton");
            forward.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            Check("default Quantify advances only the draft without another confirmation", questions == 0 && model.Page == 3 && !model.Imported && decision.Header.Length == 0);
            model.Back(); forward.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            Check("revisiting the default does not add a Quantify prompt", questions == 0 && model.Page == 3);
            model.Back();
            var use = window.FindName("UseDecisionHintButton") as Button;
            Check("the hint offers an explicit use-column action", use != null);
            use?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            Check("only choosing the hint maps its column and leaves actions unconfirmed", hint.Length > 0 && decision.Header == hint && model.Decisions.Any(d => !d.Confirmed));
            decision.Header = ""; forward.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
            Check("removing a chosen Decision column restores direct Quantify", questions == 0 && model.Page == 3);
        }
        finally { ShellMessages.Presenter = previous; window.Close(); PumpDispatcher(); }
    }
}
