using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant;

public partial class CurationQuantitationPanel : UserControl
{
    public event RoutedEventHandler? LoadRequested;
    public event RoutedEventHandler? RecalculateRequested;
    public event SelectionChangedEventHandler? ChoiceChanged;
    public event SelectionChangedEventHandler? BasisChanged;
    public CurationQuantitationPanel() => InitializeComponent();
    private void Load_Click(object sender, RoutedEventArgs e) => LoadRequested?.Invoke(sender, e);
    private void Recalculate_Click(object sender, RoutedEventArgs e) => RecalculateRequested?.Invoke(sender, e);
    private void Choice_Changed(object sender, SelectionChangedEventArgs e) => ChoiceChanged?.Invoke(sender, e);
    private void Basis_Changed(object sender, SelectionChangedEventArgs e) => BasisChanged?.Invoke(sender, e);
}
