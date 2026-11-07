using System.Windows;

namespace FamilyQuant;

public partial class SuperClusterPreviewWindow
{
    private async void UnlockEvidenceFamilies_Click(object sender, RoutedEventArgs args) =>
        await RunReviewAsync("Unlock evidence families", model.UnlockEvidenceFamiliesAsync);
}
