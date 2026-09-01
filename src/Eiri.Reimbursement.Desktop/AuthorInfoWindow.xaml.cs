using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace Eiri.Reimbursement.Desktop;

public partial class AuthorInfoWindow : Window
{
    public AuthorInfoWindow()
    {
        InitializeComponent();
    }

    private void GitHubLink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
        e.Handled = true;
    }
}
