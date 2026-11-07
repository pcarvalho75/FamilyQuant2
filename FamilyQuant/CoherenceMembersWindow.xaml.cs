using FamilyQuant.Evidence;
using System.Windows;

namespace FamilyQuant;

public partial class CoherenceMembersWindow : Window
{
    public CoherenceMembersWindow(FamilyProfile profile) { InitializeComponent(); DataContext = new CoherenceMembersViewModel(profile); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
