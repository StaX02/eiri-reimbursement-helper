using System.Windows.Controls;

namespace Eiri.Reimbursement.Desktop;

public partial class DingTalkScalarFieldView : UserControl
{
    public DingTalkScalarFieldView() => InitializeComponent();
    private void MultiChoice_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo) combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateTarget();
    }
}
