using System.Net;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Core.Export;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Infrastructure.DingTalk;
using Eiri.Reimbursement.Infrastructure.Export;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class ApprovedReimbursementExporterTests
{
    [Fact]
    public async Task ReadsApprovedInstanceFieldsAndPreservesAccountLeadingZeros()
    {
        var fields = new[] { new { name = "报销人", value = "[\"测试人员\"]" },
            new { name = "总金额（元）", value = "120.00" }, new { name = "收款人账号（或工作证号）", value = "0012345678" },
            new { name = "备注", value = "第一行\n第二行" } };
        using var http = new HttpClient(new Handler(JsonSerializer.Serialize(new { result = new {
            status = "COMPLETED", result = "agree", businessId = "202609110001", originatorDeptName = "研究部", formComponentValues = fields } })));
        var data = await new DingTalkApprovalClient(http).GetApprovedPrintDataAsync("token", "instance");
        Assert.Equal("测试人员", data.Reimburser);
        Assert.Equal(new[] { "审批编号", "创建人", "创建人部门", "研究方向", "申请日期", "报销人", "报销类型", "报销内容", "总金额", "收款人名称", "收款人账号", "开户行名称", "备注" }, data.Fields.Select(f => f.Name));
        Assert.Equal("研究部", data.Fields[2].Value);
        Assert.Equal("120.00", data.Fields[8].Value);
        Assert.Equal("0012345678", data.Fields[10].Value);
        Assert.Equal("第一行\n第二行", data.Fields[12].Value);
    }

    [Theory]
    [InlineData("RUNNING", "agree")]
    [InlineData("COMPLETED", "refuse")]
    public async Task RejectsNonApprovedRemoteInstance(string status, string result)
    {
        using var http = new HttpClient(new Handler(JsonSerializer.Serialize(new { result = new { status, result } })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DingTalkApprovalClient(http).GetApprovedPrintDataAsync("token", "instance"));
    }

    [Fact]
    public async Task ExportsOnlySupportingMaterialsInOrderAndPreservesDestinationOnFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-approved-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workspace = new SqliteReimbursementWorkspace(Path.Combine(root, "library"));
            await workspace.InitializeAsync();
            var first = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
            var second = await workspace.CreateOrderAsync(new(OrderPlatform.Taobao));
            var firstFile = Path.Combine(root, "first.pdf");
            var secondFile = Path.Combine(root, "second.pdf");
            await File.WriteAllTextAsync(firstFile, "%PDF-1.7 first material");
            await File.WriteAllTextAsync(secondFile, "%PDF-1.7 second material");
            await workspace.ImportMaterialsAsync(new(first, [firstFile], ManagedFileRole.OrderScreenshot));
            await workspace.ImportMaterialsAsync(new(second, [secondFile], ManagedFileRole.OrderScreenshot));
            var id = await workspace.CreateReimbursementAsync([first, second]);
            await workspace.BeginApprovalSubmissionAsync(id);
            await workspace.CompleteApprovalSubmissionAsync(id, "approved-instance");
            await workspace.SaveApprovalStatusAsync(id, "approved-instance", "COMPLETED", "agree");
            await workspace.SaveDingTalkConnectionAsync(new("client", "secret", "token", DateTimeOffset.UtcNow.AddHours(1)));
            var writer = new Writer();
            var exporter = new ApprovedReimbursementExporter(workspace, new Details(), writer, Path.Combine(root, "library"));
            var destination = Path.Combine(root, "out.pdf");
            await File.WriteAllTextAsync(destination, "old file");
            writer.Fail = true;
            await Assert.ThrowsAsync<InvalidDataException>(() => exporter.ExportAsync(id, destination, overwrite: true));
            Assert.Equal("old file", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.GetFiles(root, ".eiri-*.pdf"));
            writer.Fail = false;
            await exporter.ExportAsync(id, destination, overwrite: true);
            Assert.Equal("new PDF", await File.ReadAllTextAsync(destination));
            Assert.Equal(new[] { "%PDF-1.7 first material", "%PDF-1.7 second material" }, writer.Contents);
            Assert.Null((await workspace.GetReimbursementAsync(id))!.Form.ExportedAt);
            var managedFile = (await workspace.GetOrderAsync(first))!.Materials.Single().ManagedPath;
            await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(id, managedFile, overwrite: true));
            Assert.Equal("%PDF-1.7 first material", await File.ReadAllTextAsync(managedFile));
            await File.WriteAllTextAsync(destination, "concurrent file");
            await exporter.ExportAsync(id, destination);
            Assert.Equal("concurrent file", await File.ReadAllTextAsync(destination));
            Assert.Equal("new PDF", await File.ReadAllTextAsync(Path.Combine(root, "out-2.pdf")));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
    private sealed class Details : IApprovedReimbursementDetailsClient
    {
        public Task<ApprovedReimbursementPrintData> GetApprovedPrintDataAsync(string accessToken, string instanceId, CancellationToken cancellationToken = default)
        {
            Assert.Equal("approved-instance", instanceId);
            return Task.FromResult(new ApprovedReimbursementPrintData("测试人员", [new("审批编号", "123")]));
        }
    }
    private sealed class Writer : IReimbursementPdfWriter
    {
        public bool Fail { get; set; }
        public List<string> Contents { get; } = [];
        public async Task WriteAsync(string destinationPath, ApprovedReimbursementPrintData data, IReadOnlyList<string> supportingMaterialPaths, CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(destinationPath, "new PDF", cancellationToken);
            if (Fail) throw new InvalidDataException("bad input PDF");
            foreach (var path in supportingMaterialPaths) Contents.Add(await File.ReadAllTextAsync(path, cancellationToken));
        }
    }
}
