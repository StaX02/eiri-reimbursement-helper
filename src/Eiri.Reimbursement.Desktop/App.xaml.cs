using System.IO;
using System.Windows;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Export;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Documents;
using Eiri.Reimbursement.Infrastructure.Export;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            string libraryRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EiriReimbursementHelper");

            IDocumentProcessor? documentProcessor = DocumentProcessorBootstrap.TryCreate(AppContext.BaseDirectory);
            SqliteReimbursementWorkspace workspace = new(libraryRoot, documentProcessor);
            await workspace.InitializeAsync();

            IReimbursementBatchExporter? batchExporter = documentProcessor is IPdfPageRenderer pdfPageRenderer
                ? new ReimbursementBatchExporter(workspace, pdfPageRenderer)
                : null;
            MainWindowViewModel viewModel = new(workspace, batchExporter);
            MainWindow window = new(viewModel);
            MainWindow = window;
            window.Show();
            await viewModel.LoadAsync();
            window.RefreshOrdersList();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"应用初始化失败：{exception.Message}",
                "发票报销助手",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

}
