using System.IO;
using Eiri.Reimbursement.Core.Orders;
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
            Assert.False(await editor.SaveAsync());
            Assert.Equal("PCB", editor.Content);
            Assert.True(editor.HasChanges);
            editor.TotalAmount = "5078.74";
            editor.ApplicationDate = "2026-08-25";
            Assert.True(await editor.SaveAsync());
            Assert.False(editor.HasChanges);
            Assert.Equal(507874, (await workspace.GetReimbursementAsync(id))!.Form.TotalMinorUnits);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
