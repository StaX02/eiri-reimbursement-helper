using System.IO;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class ReimbursementEditorTests
{
    [Fact]
    public async Task SavesCorrectionsAndRejectsOverflowWithoutLosingEdits()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-editor-test", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root);
            await workspace.InitializeAsync();
            var order = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
            Guid id = await workspace.CreateReimbursementAsync([order]);
            var editor = new ReimbursementEditorViewModel(workspace, id);
            await editor.LoadAsync();
            editor.Content = "PCB";
            editor.TotalAmount = "79228162514264337593543950335";
            Assert.False(await editor.PendingSave);
            Assert.Equal("PCB", editor.Content);
            Assert.True(editor.HasChanges);
            editor.TotalAmount = "5078.74";
            editor.ApplicationDate = "2026-08-25";
            Assert.True(await editor.PendingSave);
            Assert.False(editor.HasChanges);
            Assert.Equal(507874, (await workspace.GetReimbursementAsync(id))!.Form.TotalMinorUnits);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task QueuedEditsPersistTheLatestValuesWithoutChangingInputText()
    {
        DelayedForms workspace = new();
        var editor = new ReimbursementEditorViewModel(workspace, workspace.Form.Id);
        await editor.LoadAsync();
        workspace.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Content = "较早输入";
        editor.Content = "最新输入";
        editor.TotalAmount = "12.3";
        Assert.False(editor.PendingSave.IsCompleted);
        workspace.SaveGate.SetResult(true);
        Assert.True(await editor.PendingSave);
        Assert.Equal("最新输入", workspace.Form.Content);
        Assert.Equal(1230, workspace.Form.TotalMinorUnits);
        Assert.Equal("12.3", editor.TotalAmount);
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public async Task TracksAttachmentImportUntilProcessingFinishes()
    {
        DelayedForms workspace = new();
        var editor = new ReimbursementEditorViewModel(workspace, workspace.Form.Id);
        await editor.LoadAsync();
        workspace.ImportGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task importing = editor.ImportAsync(["form.pdf"]);
        Assert.True(editor.IsBusy);
        Assert.False(editor.PendingImport.IsCompleted);
        workspace.ImportGate.SetResult(true);
        await importing;
        Assert.True(editor.PendingImport.IsCompletedSuccessfully);
        Assert.False(editor.IsBusy);
    }

    [Fact]
    public async Task SelectionAndCachedOrderSummaryStayCurrentWhileEditsSaveImmediately()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-sidebar-test", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root);
            await workspace.InitializeAsync();
            var firstOrder = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
            var secondOrder = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
            Guid id = await workspace.CreateReimbursementAsync([firstOrder, secondOrder]);
            var vm = new MainWindowViewModel(workspace);
            await vm.LoadAsync();
            var row = Assert.Single(vm.Reimbursements);
            await vm.SetSelectedReimbursementsAsync([row]);
            var editor = Assert.IsType<ReimbursementEditorViewModel>(vm.ReimbursementEditor);
            editor.Content = "即刻更新";
            Assert.True(await editor.PendingSave);
            Assert.Equal("即刻更新", row.ContentDisplay);
            Assert.All(vm.Orders, order => Assert.Equal("即刻更新", order.ReimbursementDisplay));
            editor.TotalAmount = "无效金额";
            Assert.False(await editor.PendingSave);
            Assert.True(vm.HasPendingReimbursementWrites);
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.FlushReimbursementChangesAsync());
            await vm.SetSelectedReimbursementsAsync([]);
            Assert.False(vm.HasReimbursementSelection);
            await workspace.DeleteOrderAsync(firstOrder);
            await vm.ReloadReimbursementsAsync();
            await vm.SetSelectedReimbursementsAsync([row]);
            Assert.Same(editor, vm.ReimbursementEditor);
            Assert.StartsWith("已绑定 1 个订单", editor.OrderSummary);
            Assert.DoesNotContain(firstOrder.ToString(), editor.OrderSummary);
            Assert.Equal("无效金额", editor.TotalAmount);
            editor.TotalAmount = "0";
            await vm.FlushReimbursementChangesAsync();
            Assert.False(vm.HasPendingReimbursementWrites);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FormActionsSynchronizeOrdersAndFailedExportDoesNotMarkExported()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-lifecycle-test", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root);
            await workspace.InitializeAsync();
            var order = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
            Guid id = await workspace.CreateReimbursementAsync([order]);
            var exporter = new Exporter();
            var vm = new MainWindowViewModel(workspace, exporter);
            await vm.LoadAsync();
            await vm.SetSelectedReimbursementsAsync([Assert.Single(vm.Reimbursements)]);
            Assert.True(vm.CanExport);
            await vm.ExportReimbursementsAsync([id], root);
            Assert.Null((await workspace.GetReimbursementAsync(id))!.Form.ExportedAt);
            Assert.Null(Assert.Single(await workspace.SearchOrdersAsync(new())).ExportedAt);
            exporter.Fail = false;
            await vm.ExportReimbursementsAsync([id], root);
            Assert.Equal(id, Assert.Single(exporter.Command!.ReimbursementIds!));
            Assert.NotNull((await workspace.GetReimbursementAsync(id))!.Form.ExportedAt);
            Assert.True(vm.ReimbursementEditor!.IsExported);
            await vm.SetReimbursementsMilestoneAsync([id], Milestone.Submitted, true);
            await vm.SetReimbursementsMilestoneAsync([id], Milestone.Refunded, true);
            Assert.NotNull(Assert.Single(await workspace.SearchOrdersAsync(new())).SubmittedAt);
            Assert.Null(vm.ReimbursementEditor);
            Assert.Empty(vm.Reimbursements);
            Assert.NotNull((await workspace.GetReimbursementAsync(id))!.Form.RefundedAt);
            await vm.ClearReimbursementsStatusesAsync([id]);
            await vm.SetSelectedReimbursementsAsync([Assert.Single(vm.Reimbursements)]);
            Assert.Null(Assert.Single(await workspace.SearchOrdersAsync(new())).RefundedAt);
            Assert.False(vm.ReimbursementEditor!.IsSubmitted);
            Assert.NotNull(Assert.Single(await workspace.SearchOrdersAsync(new())).ExportedAt);
            vm.ReimbursementEditor.TotalAmount = "invalid";
            Assert.False(await vm.ReimbursementEditor.PendingSave);
            await vm.DeleteReimbursementsAsync([id]);
            Assert.Null(await workspace.GetReimbursementAsync(id));
            Assert.Null(Assert.Single(await workspace.SearchOrdersAsync(new())).ReimbursementId);
            Assert.False(vm.CanExport);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Exporter : Eiri.Reimbursement.Core.Export.IReimbursementBatchExporter
    {
        public bool Fail { get; set; } = true;
        public Eiri.Reimbursement.Core.Export.ExportBatchCommand? Command { get; private set; }
        public Task<Eiri.Reimbursement.Core.Export.ExportBatchResult> ExportAsync(Eiri.Reimbursement.Core.Export.ExportBatchCommand command, CancellationToken cancellationToken = default)
        {
            Command = command;
            if (Fail) throw new IOException("render failed");
            return Task.FromResult(new Eiri.Reimbursement.Core.Export.ExportBatchResult(1, 0, 0, 0, "test.csv"));
        }
    }

    private sealed class DelayedForms : IReimbursementFormWorkspace
    {
        public Task SetReimbursementMilestonesAsync(IReadOnlyList<SetReimbursementMilestoneCommand> commands, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteReimbursementsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ReimbursementForm Form { get; private set; } = new(Guid.NewGuid(), null, "", "", 0, []);
        public TaskCompletionSource<bool>? SaveGate { get; set; }
        public TaskCompletionSource<bool>? ImportGate { get; set; }
        public Task<ReimbursementDetail?> GetReimbursementAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ReimbursementDetail?>(new(Form, []));
        public async Task UpdateReimbursementAsync(UpdateReimbursementCommand command, CancellationToken cancellationToken = default)
        {
            if (SaveGate is not null) await SaveGate.Task;
            Form = Form with { ApplicationDate = command.ApplicationDate, Content = command.Content, ReimbursementType = command.ReimbursementType, TotalMinorUnits = command.TotalMinorUnits };
        }
        public async Task<ReimbursementImportResult> ImportReimbursementFilesAsync(Guid id, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            if (ImportGate is not null) await ImportGate.Task;
            return new(1, []);
        }
        public Task<Guid> CreateReimbursementAsync(IReadOnlyList<OrderId> orderIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReimbursementForm>> ListReimbursementsAsync(int offset = 0, int limit = 100, CancellationToken cancellationToken = default, bool? archived = null) => Task.FromResult<IReadOnlyList<ReimbursementForm>>([Form]);
    }
}
