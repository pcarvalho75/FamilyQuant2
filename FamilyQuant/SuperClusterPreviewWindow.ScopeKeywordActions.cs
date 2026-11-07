using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    public static readonly RoutedUICommand AddTaskKeywordsToScope = new("Add to scope", nameof(AddTaskKeywordsToScope), typeof(SuperClusterPreviewWindow));
    private void AddScopeKeywords_CanExecute(object sender, CanExecuteRoutedEventArgs args)
    { args.CanExecute = model.CanEdit && taskKeywordsGrid.SelectedItems.OfType<ReviewTaskKeyword>().Any(); args.Handled = true; }
    private void AddScopeKeywords_Executed(object sender, ExecutedRoutedEventArgs args)
    {
        var selected = new HashSet<object>(taskKeywordsGrid.SelectedItems.Cast<object>());
        model.AddScopeKeywords(taskKeywordsGrid.Items.OfType<ReviewTaskKeyword>().Where(selected.Contains).Select(keyword => keyword.Keyword));
        args.Handled = true;
    }
    private void TaskKeyword_DoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(taskKeywordsGrid, source) is DataGridRow { Item: ReviewTaskKeyword keyword })
        { model.AddScopeKeywords(new[] { keyword.Keyword }); args.Handled = true; }
    }
}
