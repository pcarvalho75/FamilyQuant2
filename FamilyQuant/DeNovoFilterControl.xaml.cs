using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FamilyQuant;

public partial class DeNovoFilterControl : UserControl
{
    public DeNovoFilterControl() => InitializeComponent();
    public void Attach(DeNovoFilterViewModel model)
    {
        DataContext = model;
        ComboBoxDeNovoSource.SetCurrentValue(Selector.SelectedValueProperty, model.ModeTag);
    }
}
