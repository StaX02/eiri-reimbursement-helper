using System.Windows;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop;

public partial class DingTalkConnectionWindow : Window
{
    public bool ReimportRequested { get; private set; }
    public DingTalkConnectionWindow(DingTalkConnectionViewModel state)
    {
        InitializeComponent(); DataContext = state;
    }
    private void Reimport_OnClick(object sender, RoutedEventArgs e) { ReimportRequested = true; Close(); }
}
