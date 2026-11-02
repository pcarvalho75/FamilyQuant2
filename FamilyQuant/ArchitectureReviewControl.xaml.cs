using System.Windows.Controls;

namespace FamilyQuant;

public partial class ArchitectureReviewControl : UserControl
{
    public ArchitectureReviewControl()
    {
        InitializeComponent();
        GridCopy.Attach(memberGrid, row => (row as ReviewArchitectureMember)?.Identifier, row => (row as ReviewArchitectureMember)?.Description);
        GridCopy.Attach(undeterminedGrid, row => (row as ReviewArchitectureMember)?.Identifier, row => (row as ReviewArchitectureMember)?.Description);
    }
}
