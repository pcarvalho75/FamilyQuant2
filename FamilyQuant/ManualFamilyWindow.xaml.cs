using System.Windows;

namespace FamilyQuant;

public partial class ManualFamilyWindow : Window
{
    public Evidence.EvidenceManualRequest? Choice { get; private set; }
    public ManualFamilyWindow(ManualFamilyViewModel model) { InitializeComponent(); DataContext = model; }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var model = (ManualFamilyViewModel)DataContext;
        if (!model.CanConfirm) return;
        Choice = model.Capture(); DialogResult = true;
    }
}
