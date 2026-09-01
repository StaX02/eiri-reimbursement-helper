using System.IO;
using System.Windows;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Documents;
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

            SqliteReimbursementWorkspace workspace = new(libraryRoot, TryCreateDocumentProcessor());
            await workspace.InitializeAsync();

            MainWindowViewModel viewModel = new(workspace);
            MainWindow window = new(viewModel);
            MainWindow = window;
            window.Show();
            await viewModel.LoadAsync();
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

    private static IDocumentProcessor? TryCreateDocumentProcessor()
    {
        string? configuredPython = Environment.GetEnvironmentVariable("EIRI_DOCUMENT_WORKER_PYTHON");
        string? configuredScript = Environment.GetEnvironmentVariable("EIRI_DOCUMENT_WORKER_SCRIPT");
        if (!string.IsNullOrWhiteSpace(configuredPython)
            && !string.IsNullOrWhiteSpace(configuredScript)
            && File.Exists(configuredPython)
            && File.Exists(configuredScript))
        {
            return new JsonLinesProcessDocumentProcessor(configuredPython, [configuredScript]);
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string workerRoot = Path.Combine(directory.FullName, "worker", "document-worker");
            string pythonPath = Path.Combine(workerRoot, ".venv", "Scripts", "python.exe");
            string scriptPath = Path.Combine(workerRoot, "src", "eiri_document_worker", "__main__.py");
            if (File.Exists(pythonPath) && File.Exists(scriptPath))
            {
                return new JsonLinesProcessDocumentProcessor(pythonPath, [scriptPath]);
            }

            directory = directory.Parent;
        }

        return null;
    }
}
