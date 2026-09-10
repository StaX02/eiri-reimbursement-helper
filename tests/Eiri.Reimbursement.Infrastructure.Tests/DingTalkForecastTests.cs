using System.Net;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Infrastructure.DingTalk;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class DingTalkForecastTests
{
    [Fact]
    public async Task ForecastPostsCamelCaseValuesWithTokenOnlyInHeader()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.dingtalk.com/v1.0/workflow/processes/forecast", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-token", request.Headers.GetValues("x-acs-dingtalk-access-token").Single());
            string body = await request.Content!.ReadAsStringAsync();
            Assert.DoesNotContain("test-token", body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("code", json.RootElement.GetProperty("processCode").GetString());
            Assert.Equal(2, json.RootElement.GetProperty("deptId").GetInt64());
            Assert.Equal("user", json.RootElement.GetProperty("userId").GetString());
            var value = json.RootElement.GetProperty("formComponentValues")[0];
            Assert.Equal("id", value.GetProperty("id").GetString());
            Assert.Equal("研究方向", value.GetProperty("name").GetString());
            Assert.Equal("方向一", value.GetProperty("value").GetString());
            Assert.Equal("DDSelectField", value.GetProperty("componentType").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"result":{"isForecastSuccess":true,"workflowActivityRules":[]}}""") };
        }));
        var response = await new DingTalkForecastClient(http).ForecastAsync("test-token", Request());
        Assert.True(response.GetProperty("result").GetProperty("isForecastSuccess").GetBoolean());
    }

    [Theory]
    [InlineData(403, "private-body-with-token")]
    [InlineData(200, "not-json")]
    [InlineData(200, "{}")]
    public async Task ErrorsAreActionableAndNeverEchoResponseSecrets(int status, string body)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) })));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new DingTalkForecastClient(http).ForecastAsync("test-token", Request()));
        Assert.DoesNotContain("private-body", error.Message); Assert.DoesNotContain("test-token", error.Message);
    }

    [Fact]
    public async Task InvoicePreparationRendersAllPagesOfEveryOrderInvoiceOnly()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-approval-images-tests", Guid.NewGuid().ToString("N"));
        try
        {
            SqliteReimbursementWorkspace workspace = new(root); await workspace.InitializeAsync();
            List<OrderId> orders = [];
            for (int i = 0; i < 2; i++)
            {
                var order = await workspace.CreateOrderAsync(new(OrderPlatform.Taobao)); orders.Add(order);
                string pdf = Path.Combine(root, $"source-{i}.pdf"); await File.WriteAllTextAsync(pdf, $"%PDF-1.4 test {i}");
                await workspace.ImportMaterialsAsync(new(order, [pdf], ManagedFileRole.InvoicePdf));
                string extra = Path.Combine(root, $"extra-{i}.png"); await File.WriteAllTextAsync(extra, "extra");
                await workspace.ImportMaterialsAsync(new(order, [extra], ManagedFileRole.OrderScreenshot));
            }
            var id = await workspace.CreateReimbursementAsync(orders);
            var form = (await workspace.GetReimbursementAsync(id))!.Form;
            Renderer renderer = new();
            var result = await new DingTalkInvoiceImagePreparer(workspace, renderer).PrepareAsync(form, Path.Combine(root, "render"));
            Assert.Equal(2, renderer.Calls); Assert.Equal(4, result.ImagePaths.Count);
            Assert.Equal(4, result.ImagePaths.Distinct().Count());
            Assert.All(result.ImagePaths, path => Assert.True(File.Exists(path)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static DingTalkForecastRequest Request() => new("code", 2, "user", [new("id", "研究方向", "方向一", "DDSelectField")]);
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    private sealed class Renderer : IPdfPageRenderer
    {
        public int Calls { get; private set; }
        public async Task<IReadOnlyList<string>> RenderAsync(string pdfPath, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            Calls++; Directory.CreateDirectory(destinationDirectory);
            var pages = new[] { Path.Combine(destinationDirectory, "1.png"), Path.Combine(destinationDirectory, "2.png") };
            foreach (var page in pages) await File.WriteAllTextAsync(page, "rendered", cancellationToken);
            return pages;
        }
    }
}
