using System.Windows.Controls;

namespace FamilyQuant;

public partial class ShellNavigationRail : UserControl
{
    public ShellNavigationRail() => InitializeComponent();
    public void SetModel(ShellViewModel model) => NavigationRailItems.ItemsSource = model.Items;
}
