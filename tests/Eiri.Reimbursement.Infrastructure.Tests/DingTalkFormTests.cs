using System.Net;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Infrastructure.DingTalk;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class DingTalkFormTests
{
    [Fact]
    public void SchemaFiltersNonPrefillControlsAndDecodesOptions()
    {
        using var json = JsonDocument.Parse("""
            {"result":{"schemaContent":{"items":[
              {"componentName":"DDSelectField","props":{"id":"choice","label":"研究方向","options":["{\"key\":\"one\",\"value\":\"方向一\"}",{"key":"two","value":"方向二"},"其他"]}},
              {"componentName":"TextareaField","props":{"id":"text","label":"报销内容"}},
              {"componentName":"InnerContactField","props":{"id":"applicant","label":"报销人"}},
              {"componentName":"DDDateField","props":{"id":"date","label":"日期"}},
              {"componentName":"DDDateRangeField","props":{"id":"range","label":"时间段"}},
              {"componentName":"MoneyField","props":{"id":"money","label":"总额"}},
              {"componentName":"DDPhotoField","props":{"id":"photo","label":"票据"}},
              {"componentName":"RelateField","props":{"id":"relate","label":"采购单"}},
              {"componentName":"FormRelateField","props":{"id":"form","label":"其他表单"}},
              {"componentName":"DDAttachment","props":{"id":"file","label":"文件"}},
              {"componentName":"TextNote","props":{"id":"note"}},
              {"componentName":"TextField","props":{"id":"hidden","label":"隐藏项","hidden":true}}
            ]}}}
            """);
        var schema = DingTalkFormSchema.Parse("process", json.RootElement);
        Assert.Equal(["choice", "text"], schema.Fields.Select(f => f.Id));
        Assert.Equal(["方向一", "方向二", "其他"], schema.Fields[0].Options.Select(o => o.Value));
        Assert.Equal("applicant", schema.ApplicantFieldId);
    }

    [Fact]
    public async Task TemplateNameResolvesCodeBeforeFetchingSchemaWithTokenHeader()
    {
        int count = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("test-token", request.Headers.GetValues("x-acs-dingtalk-access-token").Single());
            Assert.DoesNotContain("test-token", request.RequestUri!.ToString());
            count++;
            if (count == 1)
            {
                Assert.Equal("/v1.0/workflow/processCentres/schemaNames/processCodes", request.RequestUri.AbsolutePath);
                Assert.Equal("?name=日常报销（电子发票）", Uri.UnescapeDataString(request.RequestUri.Query));
                return Json("""{"result":{"processCode":"PROC-test+code"}}""");
            }
            Assert.Equal("/v1.0/workflow/forms/schemas/processCodes", request.RequestUri.AbsolutePath);
            Assert.Equal("?processCode=PROC-test%2Bcode", request.RequestUri.Query);
            return Json("""{"result":{"schemaContent":{"items":[]}}}""");
        }));
        var template = await new DingTalkFormClient(http).GetReimbursementTemplateAsync("test-token");
        Assert.Equal("PROC-test+code", template.ProcessCode);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MissingTemplateDoesNotFetchSchemaOrExposeBody()
    {
        int count = 0;
        using var http = new HttpClient(new Handler(_ => { count++; return new(HttpStatusCode.BadRequest) { Content = new StringContent("secret-body") }; }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new DingTalkFormClient(http).GetReimbursementTemplateAsync("test-token"));
        Assert.DoesNotContain("secret-body", error.Message);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PrefillPersistsByTemplateAndIsRemovedWhenChangingApplication()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-form-tests", Guid.NewGuid().ToString("N"));
        try
        {
            SqliteReimbursementWorkspace workspace = new(root);
            await workspace.InitializeAsync();
            await workspace.SaveDingTalkConnectionAsync(new("app", "test-secret", "test-token"));
            await workspace.SaveFormPrefillAsync("code-one", new Dictionary<string, DingTalkPrefillValue> { ["field"] = new("DDSelectField", ["option-one"]) });
            SqliteReimbursementWorkspace reopened = new(root);
            await reopened.InitializeAsync();
            Assert.Equal("option-one", (await reopened.GetFormPrefillAsync("code-one"))["field"].Values.Single());
            Assert.Empty(await reopened.GetFormPrefillAsync("code-two"));
            await reopened.SaveDingTalkConnectionAsync(new("other-app", "secret", "token"));
            Assert.Empty(await reopened.GetFormPrefillAsync("code-one"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
