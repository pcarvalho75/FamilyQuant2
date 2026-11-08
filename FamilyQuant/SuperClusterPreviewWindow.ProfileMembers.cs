using System.Windows;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    private void ProfileMembers_Click(object sender, RoutedEventArgs e)
    {
        if (model.Evidence?.SelectedProfile is not { } profile || !model.Evidence.CanShowProfileMembers) return;
        new CoherenceMembersWindow(profile) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }
}
