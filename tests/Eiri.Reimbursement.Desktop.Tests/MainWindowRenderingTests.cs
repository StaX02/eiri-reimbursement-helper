using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Desktop;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class MainWindowRenderingTests
{
    [Fact]
    public void ImportedMaterialCanBeRenderedWithoutCrashingTheWindow()
    {
        Exception? renderingException = null;
        Thread uiThread = new(() =>
        {
            try
            {
                App application = new();
                application.InitializeComponent();
                SqliteReimbursementWorkspace workspace = new(Path.GetTempPath());
                MainWindowViewModel viewModel = new(workspace)
                {
                    Materials = new ObservableCollection<MaterialItemViewModel>(
                    [
                        new(new ManagedMaterial(
                            ManagedFileId.New(),
                            ManagedFileRole.InvoicePdf,
                            "invoice.pdf",
                            "invoice.pdf",
                            "application/pdf",
                            1024,
                            new string('a', 64),
                            MaterialProcessingState.Pending,
                            DateTimeOffset.UtcNow)),
                    ]),
                };
                MainWindow window = new(viewModel);
                window.Show();
                window.UpdateLayout();
                Assert.NotNull(window.FindName("InvoiceDropZone"));
                Assert.NotNull(window.FindName("SupportingDropZone"));
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
