using System.IO;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.DingTalk;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class DingTalkApprovalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyRangeUsesDepartmentRowsAndKeepsDefaults(bool multi)
    {
        using var json = JsonDocument.Parse("""
            {"activityType":"target_select","workflowActor":{"actorKey":"k","allowedMulti":
            """ + multi.ToString().ToLowerInvariant() + """
            ,"actorSelectionRange":{}},"activityActioners":[{"userId":"a","name":"甲"},{"userId":"b","name":"乙"}]}
            """);
        var node = new DingTalkWorkflowNodeViewModel(json.RootElement, new WorkflowDirectoryStub(), "test-token");
        Assert.True(node.UsesDepartment);
        Assert.Equal(multi ? new[] { "a", "b" } : new[] { "a" }, node.SelectedUserIds());
        var row = node.People[0];
        Assert.False(row.ShowDepartment); Assert.Equal(!multi, row.CanModify);
        row.ModifyDefault(); Assert.Equal(!multi, row.ShowDepartment);
        Assert.False(row.CanSelectPerson);
        await row.LoadDepartmentsAsync(default);
        Assert.Equal("a", row.SelectedCandidate!.UserId);
        row.SelectedDepartment = row.Departments[0];
        Assert.Null(row.SelectedCandidate); Assert.Empty(row.Candidates);
        await row.LoadPeopleAsync(default);
        row.SelectedCandidate = row.Candidates[0];
        Assert.Equal("dept-1", row.SelectedCandidate.UserId);
        row.SelectedDepartment = row.Departments[1];
        Assert.Null(row.SelectedCandidate); Assert.Empty(row.Candidates);
        await row.LoadPeopleAsync(default);
        Assert.Equal("dept-2", Assert.Single(row.Candidates).UserId);
        node.AddPerson();
        Assert.Equal(multi ? 3 : 1, node.People.Count);
        node.RemovePerson(row);
        Assert.Equal(multi ? 2 : 1, node.People.Count);
    }

    [Fact]
    public void NonemptyRangeWithoutApprovalsDoesNotEnableDirectoryAndFirstDefaultIsLiteral()
    {
        using var json = JsonDocument.Parse("""
            {"activityType":"target_select","workflowActor":{"actorKey":"k","required":false,
              "actorSelectionRange":{"approvals":[{"workNo":"a","userName":"甲"}]}},
              "activityActioners":[{"userId":"outside","name":"外部"},{"userId":"a","name":"甲"}]}
            """);
        var node = new DingTalkWorkflowNodeViewModel(json.RootElement);
        Assert.False(node.UsesDepartment); Assert.Null(node.People[0].SelectedCandidate);
        using var labels = JsonDocument.Parse("""
            {"activityType":"target_select","workflowActor":{"actorSelectionRange":{"labels":["role"]}}}
            """);
        var restricted = new DingTalkWorkflowNodeViewModel(labels.RootElement);
        Assert.False(restricted.UsesDepartment); Assert.Empty(restricted.Candidates);
    }

    [Fact]
    public async Task UnkeyedNotifierDefaultsGenerateCcListAndRowChangesInvalidateRequest()
    {
        using var context = new ApprovalTestContext();
        using var json = JsonDocument.Parse("""
            {"result":{"isForecastSuccess":true,"workflowActivityRules":[{
              "activityType":"target_select","workflowActor":{"actorType":"notifier","allowedMulti":true,"required":false,"actorSelectionRange":{}},
              "activityActioners":[{"userId":"a","name":"甲"},{"userId":"b","name":"乙"}]
            }]}}
            """);
        context.Forecast.Response = json.RootElement.Clone();
        var vm = context.Create(); await vm.LoadAsync(); await vm.GetFlowAsync(); vm.GenerateInstanceRequest();
        using var body = JsonDocument.Parse(vm.InstanceRequestJson);
        Assert.Equal(new[] { "a", "b" }, body.RootElement.GetProperty("ccList").EnumerateArray().Select(u => u.GetString()));
        var node = vm.WorkflowNodes[0]; node.AddPerson();
        Assert.Empty(vm.InstanceRequestJson);
        vm.GenerateInstanceRequest(); Assert.Empty(vm.InstanceRequestJson);
        var row = node.People[^1];
        await row.LoadDepartmentsAsync(default); row.SelectedDepartment = row.Departments[0];
        await row.LoadPeopleAsync(default); row.SelectedCandidate = row.Candidates[0];
        vm.GenerateInstanceRequest(); Assert.NotEmpty(vm.InstanceRequestJson);
        node.RemovePerson(row); Assert.Empty(vm.InstanceRequestJson);
    }

    private sealed class WorkflowDirectoryStub : IDingTalkDirectoryClient
    {
        public Task<IReadOnlyList<DingTalkDepartment>> GetDepartmentsAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DingTalkDepartment>>([new(1, "部门一"), new(2, "部门二")]);
        public Task<IReadOnlyList<DingTalkUser>> GetUsersAsync(string accessToken, long deptId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DingTalkUser>>([new($"dept-{deptId}", "成员")]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForecastDefaultsSelectMatchingCandidatesAndPopulateRequest(bool multi)
    {
        using var context = new ApprovalTestContext();
        context.Forecast.Response = JsonSerializer.SerializeToElement(new { result = new {
            isForecastSuccess = true, workflowActivityRules = new[] { new {
                activityType = "target_select", workflowActor = new {
                    actorKey = "default-node", required = true, allowedMulti = multi,
                    actorSelectionRange = new { approvals = new[] {
                        new { workNo = "a", userName = "同名人员" }, new { workNo = "b", userName = "同名人员" }
                    } }
                }, activityActioners = new[] {
                    new { userId = "b", name = "旧姓名" }, new { userId = "outside", name = "同名人员" },
                    new { userId = "a", name = "同名人员" }, new { userId = "b", name = "旧姓名" }
                }
            } }
        }});
        var vm = context.Create(); await vm.LoadAsync(); await vm.GetFlowAsync();
        var node = Assert.Single(vm.WorkflowNodes);
        Assert.Equal(multi ? new[] { "b", "a" } : new[] { "b" }, node.SelectedUserIds());
        if (!multi) Assert.Same(node.Candidates[1], node.People[0].SelectedCandidate);
        vm.GenerateInstanceRequest();
        using var json = JsonDocument.Parse(vm.InstanceRequestJson);
        var ids = json.RootElement.GetProperty("targetSelectActioners")[0].GetProperty("actionerUserIds");
        Assert.Equal(node.SelectedUserIds(), ids.EnumerateArray().Select(id => id.GetString()));
        if (multi) node.RemovePerson(node.People[0]);
        else node.People[0].SelectedCandidate = node.Candidates[0];
        Assert.Empty(vm.InstanceRequestJson);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"userId\":\"outside\",\"name\":\"候选人\"}]")]
    [InlineData("null")]
    public void MissingOrOutOfRangeDefaultsLeaveSelectionEmpty(string defaults)
    {
        using var json = JsonDocument.Parse("""
            {"activityType":"target_select","workflowActor":{"actorKey":"k","required":false,
              "actorSelectionRange":{"approvals":[{"workNo":"a","userName":"候选人"}]}},
              "activityActioners":
            """ + defaults + "}");
        var node = new DingTalkWorkflowNodeViewModel(json.RootElement);
        Assert.Null(node.People[0].SelectedCandidate);
        Assert.Empty(node.SelectedUserIds());
    }

    [Fact]
    public async Task FixedAndOptionalNodesPreserveTemplateAndFailedRefreshClearsRequest()
    {
        using var context = new ApprovalTestContext();
        context.Forecast.Response = JsonDocument.Parse("""
            {"result":{"isForecastSuccess":true,"workflowActivityRules":[
              {"activityName":"主管审批","activityType":"target_approval","activityActioners":[{"userId":"boss","name":"主管"}]},
              {"activityName":"抄送","activityType":"target_select","workflowActor":{"actorKey":"cc","required":false}}
            ]}}
            """).RootElement.Clone();
        var vm = context.Create(); await vm.LoadAsync(); await vm.GetFlowAsync();
        Assert.False(vm.WorkflowNodes[0].IsSelectable);
        Assert.Contains("boss", vm.WorkflowNodes[0].Details);
        vm.GenerateInstanceRequest();
        using var json = JsonDocument.Parse(vm.InstanceRequestJson);
        Assert.False(json.RootElement.TryGetProperty("approvers", out _));
        Assert.False(json.RootElement.TryGetProperty("targetSelectActioners", out _));
        context.Forecast.Error = true; await vm.GetFlowAsync();
        Assert.Empty(vm.InstanceRequestJson); Assert.Empty(vm.WorkflowNodes);
        Assert.False(vm.CanGenerateInstanceRequest);
    }
    [Fact]
    public async Task InstanceRequestRequiresCandidatesAndInvalidatesAfterEdits()
    {
        using var context = new ApprovalTestContext();
        var vm = context.Create(); await vm.LoadAsync(); await vm.GetFlowAsync();
        Assert.True(vm.CanGenerateInstanceRequest);
        vm.GenerateInstanceRequest(); Assert.Empty(vm.InstanceRequestJson);
        var node = Assert.Single(vm.WorkflowNodes);
        node.People[0].SelectedCandidate = new("outsider", "外部人员");
        vm.GenerateInstanceRequest(); Assert.Empty(vm.InstanceRequestJson);
        node.People[0].SelectedCandidate = node.Candidates[0];
        vm.GenerateInstanceRequest();
        using var json = JsonDocument.Parse(vm.InstanceRequestJson);
        var body = json.RootElement;
        Assert.Equal("applicant", body.GetProperty("originatorUserId").GetString());
        Assert.Equal("process", body.GetProperty("processCode").GetString());
        Assert.Equal(2, body.GetProperty("deptId").GetInt64());
        Assert.False(body.TryGetProperty("approvers", out _));
        Assert.False(body.TryGetProperty("accessToken", out _));
        var selection = body.GetProperty("targetSelectActioners")[0];
        Assert.Equal("actor-1", selection.GetProperty("actionerKey").GetString());
        Assert.Equal("u1", selection.GetProperty("actionerUserIds")[0].GetString());
        Assert.All(body.GetProperty("formComponentValues").EnumerateArray(), f => Assert.Equal(2, f.EnumerateObject().Count()));
        node.People[0].SelectedCandidate = null; Assert.Empty(vm.InstanceRequestJson);
        context.Field(vm, "报销内容").SetText("新内容");
        Assert.Empty(vm.WorkflowNodes); Assert.False(vm.CanGenerateInstanceRequest);
        Assert.Null((await context.Workspace.GetReimbursementAsync(context.Id))!.Form.SubmittedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WorkflowSelectionRespectsMultiplicityAndMissingCandidates(bool multi)
    {
        var rule = JsonSerializer.SerializeToElement(new { activityType = "target_select", workflowActor = new {
            actorKey = "k", required = true, allowedMulti = multi,
            actorSelectionRange = new { approvals = new[] { new { workNo = "a", userName = "甲" }, new { workNo = "b", userName = "乙" } } }
        }});
        var node = new DingTalkWorkflowNodeViewModel(rule);
        Assert.Throws<InvalidOperationException>(() => node.SelectedUserIds());
        node.People[0].SelectedCandidate = node.Candidates[0];
        node.AddPerson();
        if (multi) node.People[1].SelectedCandidate = node.Candidates[1];
        Assert.Equal(multi ? 2 : 1, node.SelectedUserIds().Length);
        var missing = new DingTalkWorkflowNodeViewModel(JsonSerializer.SerializeToElement(new { activityType = "target_select" }));
        Assert.Throws<InvalidOperationException>(() => missing.SelectedUserIds());
    }
    [Fact]
    public async Task FullSchemaRestoresDefaultsAndUsesReimbursementValues()
    {
        using var context = new ApprovalTestContext();
        var vm = context.Create();
        await vm.LoadAsync();
        Assert.True(vm.IsLoaded, vm.StatusMessage);
        Assert.Equal(11, vm.Fields.Count);
        Assert.Equal("报销类型*", context.Field(vm, "报销类型").Label);
        Assert.Equal("2026-09-09", context.Field(vm, "申请日期").Value);
        Assert.Equal("材料费", context.Field(vm, "报销类型").Value);
        Assert.Equal("现有报销内容", context.Field(vm, "报销内容").Value);
        Assert.Equal("12.34", context.Field(vm, "总金额（元）").Value);
        Assert.Equal("默认备注 1001 1002", context.Field(vm, "备注").Value);
        Assert.Equal("方向一", context.Field(vm, "研究方向").Value);
        Assert.Single(context.Field(vm, "票据").Images);
        Assert.Equal("applicant", vm.Directory.SelectedUser!.UserId);
        Assert.Contains(vm.Fields, f => f.IsDeferred);
        Assert.Contains(vm.Fields, f => f.IsNote);
    }

    [Fact]
    public async Task ForecastSyncsEditsPublishesImagesAndDoesNotSubmitOrChangeDefaults()
    {
        using var context = new ApprovalTestContext();
        var vm = context.Create(); await vm.LoadAsync();
        context.Field(vm, "报销类型").SetText("其他");
        context.Field(vm, "报销内容").SetText("修改后内容");
        await vm.GetFlowAsync();
        Assert.Contains("isForecastSuccess", vm.FlowJson);
        Assert.Equal("有自选节点：部门审批", vm.SelfSelectedNodeSummary);
        var form = (await context.Workspace.GetReimbursementAsync(context.Id))!.Form;
        Assert.Equal("其他", form.ReimbursementType);
        Assert.Equal("修改后内容", form.Content);
        Assert.Equal(new DateOnly(2026, 9, 9), form.ApplicationDate);
        Assert.Null(form.SubmittedAt);
        var request = Assert.IsType<DingTalkForecastRequest>(context.Forecast.Request);
        Assert.Equal("process", request.ProcessCode); Assert.Equal(2, request.DeptId); Assert.Equal("applicant", request.UserId);
        Assert.Equal("其他", request.FormComponentValues.Single(f => f.Name == "报销类型").Value);
        Assert.Equal("[\"applicant\"]", request.FormComponentValues.Single(f => f.Name == "报销人").Value);
        Assert.Equal("[\"https://static.dingtalk.com/media/abc_1_1.png\"]", request.FormComponentValues.Single(f => f.Name == "票据").Value);
        Assert.DoesNotContain(request.FormComponentValues, f => f.ComponentType is "TextNote" or "RelateField" or "DDAttachment");
        Assert.Single(context.Publisher.Paths!);
        Assert.Equal("默认备注", (await context.Workspace.GetFormPrefillAsync("process"))["notes"].Values.Single());
        context.Field(vm, "报销内容").SetText("再次编辑");
        Assert.Empty(vm.FlowJson);
        Assert.Equal("尚未获取流程。", vm.SelfSelectedNodeSummary);
    }

    [Theory]
    [InlineData("报销内容", "")]
    [InlineData("申请日期", "2026-02-30")]
    [InlineData("总金额（元）", "1.234")]
    public async Task InvalidFieldsDoNotSyncPublishOrRequest(string label, string value)
    {
        using var context = new ApprovalTestContext();
        var vm = context.Create(); await vm.LoadAsync();
        context.Field(vm, label).SetText(value);
        await vm.GetFlowAsync();
        Assert.Contains(label, vm.StatusMessage);
        Assert.Single(context.Publisher.Paths); Assert.Null(context.Forecast.Request);
        Assert.Null((await context.Workspace.GetReimbursementAsync(context.Id))!.Form.ApplicationDate);
    }

    [Fact]
    public async Task FailedUploadStaysVisibleAndBlocksForecastUntilRemovedAndReadded()
    {
        using var context = new ApprovalTestContext();
        context.Publisher.Error = true;
        var vm = context.Create(); await vm.LoadAsync();
        context.Field(vm, "报销内容").SetText("等待图片发布");
        await vm.GetFlowAsync();
        Assert.Contains("未上传成功", vm.StatusMessage);
        Assert.Equal("现有报销内容", (await context.Workspace.GetReimbursementAsync(context.Id))!.Form.Content);
        var field = context.Field(vm, "票据");
        var image = Assert.Single(field.Images);
        Assert.Contains("上传失败", image.Status);
        Assert.True(vm.CanForecast); Assert.Null(context.Forecast.Request); Assert.Empty(vm.FlowJson);
        vm.RemoveImage(field, image); Assert.Empty(field.Images);
        context.Publisher.Error = false;
        vm.AddImage(field, image.Path); await vm.WaitForUploadsAsync();
        Assert.Equal("上传成功", field.Images[0].Status);
        await vm.GetFlowAsync(); Assert.NotEmpty(vm.FlowJson);
    }

    [Fact]
    public async Task ImagesUploadOnAdditionAndLateRemovedResponsesAreIgnored()
    {
        using var context = new ApprovalTestContext();
        var vm = context.Create(); await vm.LoadAsync();
        var field = context.Field(vm, "票据");
        Assert.Equal("abc", field.Images[0].Uploaded!.MediaId);
        Assert.Single(context.Publisher.Paths);
        context.Publisher.Wait = true;
        vm.AddImage(field, "second.png");
        var second = field.Images[1];
        Assert.True(second.IsUploading); Assert.False(vm.CanForecast); Assert.True(vm.CanEdit);
        Assert.Equal(2, context.Publisher.Paths.Count);
        vm.RemoveImage(field, second);
        context.Publisher.Release.TrySetResult();
        await vm.WaitForUploadsAsync();
        Assert.Null(second.Uploaded); Assert.Single(field.Images); Assert.True(vm.CanForecast);
        await vm.GetFlowAsync();
        Assert.Equal(2, context.Publisher.Paths.Count);
        Assert.DoesNotContain("second", vm.FlowJson);
    }

    [Fact]
    public async Task ClosingCancelsUploadsWithoutSavingLateMedia()
    {
        using var context = new ApprovalTestContext();
        var vm = context.Create(); await vm.LoadAsync();
        context.Publisher.Wait = true;
        var field = context.Field(vm, "票据");
        vm.AddImage(field, "pending.png");
        vm.CancelUploads(); context.Publisher.Release.TrySetResult(); await vm.WaitForUploadsAsync();
        Assert.Null(field.Images[1].Uploaded);
        Assert.Equal("上传已取消", field.Images[1].Status);
    }

    [Fact]
    public async Task EmptyReimbursementFieldsStayEmptyDespiteOldPrefills()
    {
        using var context = new ApprovalTestContext();
        await context.Workspace.UpdateReimbursementAsync(new(context.Id, null, "", "", null));
        await context.Workspace.SaveFormPrefillAsync("process", new Dictionary<string, DingTalkPrefillValue>
        { ["type"] = new("DDSelectField", ["material"]), ["content"] = new("TextareaField", ["过期预填"]) });
        var vm = context.Create(); await vm.LoadAsync();
        Assert.Empty(context.Field(vm, "报销类型").Value); Assert.Empty(context.Field(vm, "报销内容").Value);
        Assert.Empty(context.Field(vm, "总金额（元）").Value);
    }

    [Fact]
    public async Task ForecastFailureCanBeRetriedAndFalsePredictionStillShowsJson()
    {
        using var context = new ApprovalTestContext();
        var vm = context.Create(); await vm.LoadAsync();
        context.Forecast.Error = true;
        await vm.GetFlowAsync();
        Assert.Empty(vm.FlowJson); Assert.True(vm.CanForecast);
        context.Forecast.Error = false; context.Forecast.Success = false;
        await vm.GetFlowAsync();
        Assert.Contains("false", vm.FlowJson); Assert.Contains("未成功预测", vm.StatusMessage);
        Assert.Contains("无法判断", vm.SelfSelectedNodeSummary);
    }

    [Fact]
    public async Task DuplicateRequestsAreBlockedAndCancellationRestoresControls()
    {
        using var context = new ApprovalTestContext();
        context.Forecast.Wait = true;
        var vm = context.Create(); await vm.LoadAsync();
        using var cancellation = new CancellationTokenSource();
        var pending = vm.GetFlowAsync(cancellation.Token);
        await context.Forecast.Started.Task;
        Assert.False(vm.CanForecast);
        await vm.GetFlowAsync();
        Assert.Equal(1, context.Forecast.Calls);
        cancellation.Cancel(); await pending;
        Assert.True(vm.CanForecast); Assert.Empty(vm.FlowJson);
    }
}

internal sealed class ApprovalTestContext : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "eiri-approval-tests", Guid.NewGuid().ToString("N"));
    public SqliteReimbursementWorkspace Workspace { get; }
    public Guid Id { get; }
    public ForecastStub Forecast { get; } = new();
    public PublisherStub Publisher { get; } = new();
    public ApprovalTestContext()
    {
        Workspace = new(Root); Workspace.InitializeAsync().GetAwaiter().GetResult();
        Workspace.SaveDingTalkConnectionAsync(new("test-client", "test-secret", "test-token")).GetAwaiter().GetResult();
        var order = Workspace.CreateOrderAsync(new(OrderPlatform.Taobao)).GetAwaiter().GetResult();
        Id = Workspace.CreateReimbursementAsync([order]).GetAwaiter().GetResult();
        Workspace.UpdateReimbursementAsync(new(Id, null, "材料费", "现有报销内容", 1234)).GetAwaiter().GetResult();
        Workspace.SaveSubmissionInfoAsync(new(new(2, "研发部门"), new("applicant", "测试报销人"))).GetAwaiter().GetResult();
        Workspace.SaveFormPrefillAsync("process", new Dictionary<string, DingTalkPrefillValue>
        { ["notes"] = new("TextField", ["默认备注"]), ["direction"] = new("DDSelectField", ["one"]) }).GetAwaiter().GetResult();
    }
    public DingTalkApprovalViewModel Create(IDingTalkImagePublisher? publisher = null, IDingTalkFormClient? forms = null, IDingTalkApprovalClient? approval = null) => new(Workspace, Id, "test-token", forms ?? new FormStub(), Workspace,
        new DirectoryStub(), Workspace, new ImagesStub(Root), publisher ?? Publisher, Forecast, Path.Combine(Root, "images"), () => new(2026, 9, 9), approval);
    public DingTalkApprovalFieldViewModel Field(DingTalkApprovalViewModel vm, string label) => vm.Fields.Single(f => f.Definition.Label == label);
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    private sealed class FormStub : IDingTalkFormClient
    {
        public Task<DingTalkFormTemplate> GetReimbursementTemplateAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            using var json = JsonDocument.Parse("""
                {"result":{"schemaContent":{"items":[
                {"componentName":"DDSelectField","props":{"id":"direction","label":"研究方向","required":true,"options":[{"key":"one","value":"方向一"}]}},
                {"componentName":"DDDateField","props":{"id":"date","label":"申请日期","required":true}},
                {"componentName":"InnerContactField","props":{"id":"user","label":"报销人","required":true}},
                {"componentName":"DDSelectField","props":{"id":"type","label":"报销类型","required":true,"options":[{"key":"material","value":"材料费"},{"key":"other","value":"其他"}]}},
                {"componentName":"TextNote","props":{"id":"note","content":"测试说明"}},
                {"componentName":"TextareaField","props":{"id":"content","label":"报销内容","required":true}},
                {"componentName":"MoneyField","props":{"id":"amount","label":"总金额（元）","required":true}},
                {"componentName":"DDPhotoField","props":{"id":"photos","label":"票据","required":true}},
                {"componentName":"RelateField","props":{"id":"related","label":"关联采购审批单"}},
                {"componentName":"DDAttachment","props":{"id":"attachments","label":"附件"}},
                {"componentName":"TextField","props":{"id":"notes","label":"备注"}}
                ]}}}
                """);
            return Task.FromResult(DingTalkFormSchema.Parse("process", json.RootElement));
        }
    }
    private sealed class ImagesStub(string root) : IDingTalkInvoiceImages
    {
        public Task<DingTalkInvoiceImages> PrepareAsync(ReimbursementForm form, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            string path = Path.Combine(root, "sample.png");
            File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            return Task.FromResult(new DingTalkInvoiceImages([path], ["1001", "1002"]));
        }
    }
    private sealed class DirectoryStub : IDingTalkDirectoryClient
    {
        public Task<IReadOnlyList<DingTalkDepartment>> GetDepartmentsAsync(string accessToken, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DingTalkDepartment>>([new(2, "研发部门"), new(3, "其他部门")]);
        public Task<IReadOnlyList<DingTalkUser>> GetUsersAsync(string accessToken, long deptId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DingTalkUser>>([new("applicant", "测试报销人")]);
    }
    internal sealed class PublisherStub : IDingTalkImagePublisher
    {
        public List<string> Paths { get; } = [];
        public bool Error { get; set; }
        public bool Wait { get; set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DingTalkUploadedImage> UploadAsync(string accessToken, string imagePath, CancellationToken cancellationToken = default)
        {
            Paths.Add(imagePath);
            if (Wait) await Release.Task;
            if (Error) throw new InvalidOperationException("测试上传失败");
            return DingTalkUploadedImage.Create("@#123abc", new(1, 1, "png", "image/png"));
        }
    }
    internal sealed class ForecastStub : IDingTalkForecastClient
    {
        public DingTalkForecastRequest? Request { get; private set; }
        public JsonElement? Response { get; set; }
        public bool Error { get; set; }
        public bool Success { get; set; } = true;
        public bool Wait { get; set; }
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JsonElement> ForecastAsync(string accessToken, DingTalkForecastRequest request, CancellationToken cancellationToken = default)
        {
            Calls++; Request = request; Started.TrySetResult();
            if (Wait) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Error) throw new InvalidOperationException("测试流程请求失败");
            if (Response is { } response) return response;
            return JsonSerializer.SerializeToElement(new { result = new { isForecastSuccess = Success, workflowActivityRules = new[] { new { activityName = "部门审批", activityType = "target_select", workflowActor = new {
                actorKey = "actor-1", required = true, allowedMulti = false,
                actorSelectionRange = new { approvals = new[] { new { workNo = "u1", userName = "审批人甲" }, new { workNo = "u2", userName = "审批人乙" } } }
            } } } } });
        }
    }
}
