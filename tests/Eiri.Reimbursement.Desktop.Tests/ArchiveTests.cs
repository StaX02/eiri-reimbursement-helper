using System.IO;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class ArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eiri-archive-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task ArchiveIsReadOnlyAndRestoresFormAndEveryLinkedOrder()
    {
        var workspace = new SqliteReimbursementWorkspace(_root);
        await workspace.InitializeAsync();
        var a = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
        var b = await workspace.CreateOrderAsync(new(OrderPlatform.Taobao));
        var form = await workspace.CreateReimbursementAsync([a, b]);
        await workspace.SetReimbursementMilestonesAsync(Enum.GetValues<Milestone>()
            .Select(m => new SetReimbursementMilestoneCommand(form, m, DateTimeOffset.UtcNow)).ToArray());
        var archive = new MainWindowViewModel(workspace, isArchive: true);
        await archive.LoadAsync();
        Assert.Equal(2, archive.Orders.Count);
        await archive.SetSelectedReimbursementsAsync([Assert.Single(archive.Reimbursements)]);
        var editor = Assert.IsType<ReimbursementEditorViewModel>(archive.ReimbursementEditor);
        Assert.False(editor.CanEdit);
        editor.Content = "禁止保存";
        await editor.SaveAsync();
        await editor.ImportAsync(["missing.pdf"]);
        await archive.SetReimbursementsMilestoneAsync([form], Milestone.Submitted, false);
        await archive.DeleteReimbursementsAsync([form]);
        Assert.Equal("", (await workspace.GetReimbursementAsync(form))!.Form.Content);
        Assert.NotNull((await workspace.GetReimbursementAsync(form))!.Form.SubmittedAt);
        await archive.UnarchiveReimbursementsAsync([form]);
        Assert.Empty(archive.Orders);
        Assert.Empty(archive.Reimbursements);
        Assert.Null(archive.ReimbursementEditor);
        var main = new MainWindowViewModel(workspace);
        await main.LoadAsync();
        Assert.Equal(2, main.Orders.Count);
        Assert.All(main.Orders, o => { Assert.Null(o.RefundedAt); Assert.NotNull(o.ExportedAt); Assert.NotNull(o.SubmittedAt); });
        Assert.Null(Assert.Single(main.Reimbursements).Form.RefundedAt);
    }

    [Fact]
    public async Task MainWindowHidesOnlyOrdersAndFormsWithAllThreeMilestones()
    {
        var workspace = new SqliteReimbursementWorkspace(_root);
        await workspace.InitializeAsync();
        var completed = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
        var pending = await workspace.CreateOrderAsync(new(OrderPlatform.Taobao));
        var form = await workspace.CreateReimbursementAsync([completed]);
        await workspace.SetReimbursementMilestonesAsync([
            new(form, Milestone.Submitted, DateTimeOffset.UtcNow),
            new(form, Milestone.Refunded, DateTimeOffset.UtcNow)]);
        var vm = new MainWindowViewModel(workspace);
        await vm.LoadAsync();
        Assert.Equal(2, vm.Orders.Count);
        Assert.Single(vm.Reimbursements);
        await workspace.SetReimbursementMilestonesAsync([new(form, Milestone.Exported, DateTimeOffset.UtcNow)]);
        await vm.LoadAsync();
        Assert.Equal(pending, Assert.Single(vm.Orders).Id);
        Assert.Empty(vm.Reimbursements);
    }

    [Fact]
    public async Task ArchiveFilteringPrecedesPagingAndLastPageFallsBackAfterRestore()
    {
        var workspace = new SqliteReimbursementWorkspace(_root);
        await workspace.InitializeAsync();
        for (int i = 0; i < 101; i++)
        {
            var order = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
            var form = await workspace.CreateReimbursementAsync([order]);
            await workspace.SetReimbursementMilestonesAsync(Enum.GetValues<Milestone>()
                .Select(m => new SetReimbursementMilestoneCommand(form, m, DateTimeOffset.UtcNow)).ToArray());
        }
        var activeOrder = await workspace.CreateOrderAsync(new(OrderPlatform.Taobao));
        var activeForm = await workspace.CreateReimbursementAsync([activeOrder]);
        var archive = new MainWindowViewModel(workspace, isArchive: true);
        await archive.LoadAsync();
        Assert.Equal(100, archive.Reimbursements.Count);
        Assert.DoesNotContain(archive.Reimbursements, r => r.Id == activeForm);
        await archive.ReloadReimbursementsAsync(1);
        Assert.Equal(1, archive.ReimbursementPage);
        var last = Assert.Single(archive.Reimbursements);
        await archive.UnarchiveReimbursementsAsync([last.Id]);
        Assert.Equal(0, archive.ReimbursementPage);
        Assert.Equal(100, archive.Reimbursements.Count);
        var main = new MainWindowViewModel(workspace);
        await main.LoadAsync();
        Assert.Equal(2, main.Reimbursements.Count);
        Assert.Contains(main.Reimbursements, r => r.Id == activeForm);
        Assert.Equal(2, main.Orders.Count);
    }

    [Fact]
    public async Task ArchiveMatchesAllThreeMilestonesAndFailedBulkRestoreIsAtomic()
    {
        var workspace = new SqliteReimbursementWorkspace(_root);
        await workspace.InitializeAsync();
        Guid completed = default;
        for (int mask = 0; mask < 8; mask++)
        {
            var order = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
            var form = await workspace.CreateReimbursementAsync([order]);
            var milestones = new[] { Milestone.Exported, Milestone.Submitted, Milestone.Refunded };
            await workspace.SetReimbursementMilestonesAsync(milestones.Where((_, bit) => (mask & (1 << bit)) != 0)
                .Select(m => new SetReimbursementMilestoneCommand(form, m, DateTimeOffset.UtcNow)).ToArray());
            if (mask == 7) completed = form;
        }
        Assert.Equal(7, (await workspace.SearchOrdersAsync(new(Archived: false))).Count);
        Assert.Single(await workspace.SearchOrdersAsync(new(Archived: true)));
        Assert.Equal(7, (await workspace.ListReimbursementsAsync(archived: false)).Count);
        Assert.Equal(completed, Assert.Single(await workspace.ListReimbursementsAsync(archived: true)).Id);
        var archive = new MainWindowViewModel(workspace, isArchive: true);
        await archive.LoadAsync();
        await archive.UnarchiveReimbursementsAsync([completed, Guid.NewGuid()]);
        Assert.Contains("操作失败", archive.StatusMessage);
        Assert.NotNull((await workspace.GetReimbursementAsync(completed))!.Form.RefundedAt);
        Assert.Single(await workspace.SearchOrdersAsync(new(Archived: true)));
        var reopened = new SqliteReimbursementWorkspace(_root);
        await reopened.InitializeAsync();
        Assert.Equal(completed, Assert.Single(await reopened.ListReimbursementsAsync(archived: true)).Id);
    }

    [Fact]
    public async Task LibraryWithOnlyArchivedOrdersStillReportsExistingDataBeforeRestore()
    {
        var workspace = new SqliteReimbursementWorkspace(_root);
        await workspace.InitializeAsync();
        var order = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
        await workspace.SetMilestonesAsync(Enum.GetValues<Milestone>()
            .Select(m => new SetMilestoneCommand(order, m, DateTimeOffset.UtcNow)).ToArray());
        var main = new MainWindowViewModel(workspace);
        await main.LoadAsync();
        Assert.Empty(main.Orders);
        Assert.True(main.HasOrders);
    }
}
