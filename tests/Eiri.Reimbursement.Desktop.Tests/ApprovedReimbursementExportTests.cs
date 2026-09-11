using System.IO;
using Eiri.Reimbursement.Core.Export;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class ApprovedReimbursementExportTests
{
    [Fact]
    public async Task BatchUsesOneFolderAvoidsCollisionsAndMarksOnlySuccessfulExports()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-approved-batch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workspace = new SqliteReimbursementWorkspace(Path.Combine(root, "library"));
            await workspace.InitializeAsync();
            List<Guid> ids = [];
            for (int i = 0; i < 2; i++)
            {
                var id = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
                await workspace.BeginApprovalSubmissionAsync(id);
                await workspace.CompleteApprovalSubmissionAsync(id, id.ToString());
                await workspace.SaveApprovalStatusAsync(id, id.ToString(), "COMPLETED", "agree", businessId: "same-number");
                ids.Add(id);
            }
            var exporter = new Exporter();
            var vm = new MainWindowViewModel(workspace, approvedExporter: exporter);
            await vm.LoadAsync();
            await vm.ExportApprovedReimbursementsAsync(ids, root);
            Assert.Equal(2, exporter.Paths.Count);
            Assert.All(exporter.Paths, path => Assert.Equal(root, Path.GetDirectoryName(path)));
            Assert.NotEqual(exporter.Paths[0], exporter.Paths[1]);
            Assert.All(await workspace.ListReimbursementsAsync(), f => Assert.NotNull(f.ExportedAt));
            exporter.Fail = true;
            var failed = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
            await vm.ExportApprovedReimbursementAsync(failed, Path.Combine(root, "failed.pdf"));
            Assert.Null((await workspace.GetReimbursementAsync(failed))!.Form.ExportedAt);
            Assert.Contains("操作失败", vm.StatusMessage);
            Assert.False(vm.IsBusy);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Exporter : IApprovedReimbursementExporter
    {
        public bool Fail { get; set; }
        public List<string> Paths { get; } = [];
        public async Task ExportAsync(Guid reimbursementId, string destinationPath, CancellationToken cancellationToken = default, bool overwrite = false)
        {
            if (Fail) throw new InvalidDataException("生成失败");
            Paths.Add(destinationPath);
            await File.WriteAllTextAsync(destinationPath, "PDF", cancellationToken);
        }
    }
}
