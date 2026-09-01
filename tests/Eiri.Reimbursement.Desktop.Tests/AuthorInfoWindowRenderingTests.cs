using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Eiri.Reimbursement.Desktop;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class AuthorInfoWindowRenderingTests
{
    [Fact]
    public void AuthorInformationRendersAvatarNameAndGitHubLinkInOrder()
    {
        Exception? renderingException = null;
        Thread uiThread = new(() =>
        {
            try
            {
                AuthorInfoWindow window = new();
                window.Show();
                window.UpdateLayout();

                Image avatar = Assert.IsType<Image>(window.FindName("AuthorAvatar"));
                TextBlock authorName = Assert.IsType<TextBlock>(window.FindName("AuthorName"));
                Hyperlink githubLink = Assert.IsType<Hyperlink>(window.FindName("AuthorGitHubLink"));

                Assert.Equal(HorizontalAlignment.Center, avatar.HorizontalAlignment);
                Assert.Equal("StaX", authorName.Text);
                Assert.Equal("https://github.com/StaX02", githubLink.NavigateUri.AbsoluteUri.TrimEnd('/'));
                Assert.True(Grid.GetRow(avatar) < Grid.GetRow(authorName));
                Assert.True(Grid.GetRow(authorName) < Grid.GetRow(Assert.IsType<TextBlock>(githubLink.Parent)));

                window.Close();
            }
            catch (Exception exception)
            {
                renderingException = exception;
            }
        });
        uiThread.SetApartmentState(ApartmentState.STA);

        uiThread.Start();

        Assert.True(uiThread.Join(TimeSpan.FromSeconds(5)), "UI rendering did not complete in time.");
        Assert.Null(renderingException);
    }
}
