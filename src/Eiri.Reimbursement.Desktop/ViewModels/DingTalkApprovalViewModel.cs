using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Core.Reimbursements;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public sealed class DingTalkApprovalFieldViewModel
{
    public DingTalkFormFieldViewModel Input { get; }
    public DingTalkFormField Definition => Input.Definition;
    public string Label => (string.IsNullOrWhiteSpace(Definition.Label) ? "说明" : Definition.Label) + (Definition.Required ? "*" : "");
    public bool IsPhoto => Definition.ComponentName == "DDPhotoField";
    public bool IsApplicant => Definition.ComponentName == "InnerContactField" && Definition.Label == "报销人";
    public bool IsNote => Definition.ComponentName == "TextNote";
    public bool IsDeferred => Definition.ComponentName is "RelateField" or "FormRelateField" or "DDAttachment" || Definition.Label == "发送到聊天";
    public bool IsInput => !IsPhoto && !IsApplicant && !IsNote && !IsDeferred;
    public ObservableCollection<DingTalkApprovalImageViewModel> Images { get; } = [];
    public string Note => Definition.Schema is { } schema && schema.GetProperty("props").TryGetProperty("content", out var content)
        ? content.ToString() : "";
    public DingTalkApprovalFieldViewModel(DingTalkFormField definition, DingTalkPrefillValue? saved) => Input = new(definition, saved);

    public string Value => Input.IsSingleChoice ? Input.SelectedChoice?.Value is { } value && Input.SelectedChoice.Key.Length > 0 ? value : ""
        : Input.IsMultipleChoice ? JsonSerializer.Serialize(Input.Choices.Where(c => c.IsSelected).Select(c => c.Value)) : Input.Text;
    public bool IsEmpty => IsPhoto ? Images.Count == 0 : Input.IsMultipleChoice ? !Input.Choices.Any(c => c.IsSelected) : string.IsNullOrWhiteSpace(Value);
    public void SetText(string value)
    {
        if (Input.IsSingleChoice) Input.SelectedChoice = Input.Choices.FirstOrDefault(c => c.Value == value && c.Key.Length > 0);
        else Input.Text = value;
    }
}

public sealed partial class DingTalkApprovalViewModel : ObservableObject
{
    private readonly IReimbursementFormWorkspace _workspace;
    private readonly IDingTalkFormClient _forms;
    private readonly IDingTalkFormPrefillStore _prefills;
    private readonly IDingTalkInvoiceImages _invoices;
    private readonly IDingTalkImagePublisher _publisher;
    private readonly IDingTalkForecastClient _forecast;
    private readonly IDingTalkDirectoryClient _directory;
    private readonly IDingTalkApprovalClient? _approvalClient;
    private readonly IDingTalkApprovalStore? _approvalStore;
    private bool _automaticFlow;
    private CancellationToken _windowToken;
    private CancellationTokenSource? _flowChange;
    public Task PendingAutomaticFlow { get; private set; } = Task.CompletedTask;
    public void EnableAutomaticFlow(CancellationToken token) { _automaticFlow = true; _windowToken = token; }
    public void RefreshAutomaticFlow()
    {
        if (!_automaticFlow || !IsLoaded || _submissionLocked || _windowToken.IsCancellationRequested) return;
        _flowChange?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowToken);
        _flowChange = cancellation;
        FlowJson = "";
        PendingAutomaticFlow = RefreshAfterAsync(PendingAutomaticFlow, cancellation);
    }
    private async Task RefreshAfterAsync(Task previous, CancellationTokenSource cancellation)
    {
        try
        {
            await previous;
            await Task.Delay(200, cancellation.Token);
            await WaitForUploadsAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            await GetFlowAsync(cancellation.Token, allowIncomplete: true);
        }
        catch (OperationCanceledException) { }
        finally { if (_flowChange == cancellation) _flowChange = null; cancellation.Dispose(); }
    }
    private readonly string _token;
    private readonly Guid _id;
    private readonly string _imageDirectory;
    private readonly Func<DateOnly> _today;
    private ReimbursementForm? _original;
    private DingTalkFormTemplate? _template;
    private readonly List<DingTalkApprovalImageViewModel> _uploads = [];
    public DingTalkSubmissionInfoViewModel Directory { get; }
    [ObservableProperty] private IReadOnlyList<DingTalkApprovalFieldViewModel> _fields = [];
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _flowJson = "";
    [ObservableProperty] private IReadOnlyList<DingTalkWorkflowNodeViewModel> _workflowNodes = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit))] private string _instanceRequestJson = "";
    [ObservableProperty] private string _submissionStatus = "";
    [ObservableProperty] private string _instanceId = "";
    [ObservableProperty] private bool _isSubmitting;
    private bool _submissionLocked;
    private bool _submissionUncertain;
    public bool CanSubmit => CanGenerateInstanceRequest && CanForecast && !_submissionLocked && (_automaticFlow || InstanceRequestJson.Length > 0) && _approvalClient is not null && _approvalStore is not null;
    public bool CanResetSubmission => _submissionUncertain && !IsBusy && InstanceId.Length == 0;
    public bool CanSaveSubmission => _submissionUncertain && !IsBusy && InstanceId.Length > 0;
    private void RefreshSubmissionControls()
    {
        OnPropertyChanged(nameof(CanSubmit)); OnPropertyChanged(nameof(CanResetSubmission)); OnPropertyChanged(nameof(CanSaveSubmission));
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanForecast)); OnPropertyChanged(nameof(CanGenerateInstanceRequest));
    }
    partial void OnIsBusyChanged(bool value) => RefreshSubmissionControls();
    private DingTalkForecastRequest? _forecastSnapshot;
    public bool CanGenerateInstanceRequest => _forecastSnapshot is not null && CanEdit && !WorkflowNodes.Any(n => n.IsBusy);
    [ObservableProperty] private string _selfSelectedNodeSummary = "尚未获取流程。";
    partial void OnFlowJsonChanged(string value)
    {
        WorkflowNodes = [];
        InstanceRequestJson = "";
        _forecastSnapshot = null;
        OnPropertyChanged(nameof(CanGenerateInstanceRequest));
        OnPropertyChanged(nameof(CanSubmit));
        if (string.IsNullOrWhiteSpace(value)) { SelfSelectedNodeSummary = "尚未获取流程。"; return; }
        try
        {
            using var document = JsonDocument.Parse(value);
            var analysis = DingTalkForecastAnalysis.Analyze(document.RootElement);
            SelfSelectedNodeSummary = analysis.HasSelfSelectedNodes switch
            {
                true => "有自选节点：" + string.Join("、", analysis.NodeNames),
                false => "无自选节点。",
                null => "无法判断是否有自选节点：流程预测未成功或节点数据不完整。"
            };
        }
        catch (JsonException) { SelfSelectedNodeSummary = "无法判断是否有自选节点：流程 JSON 格式无效。"; }
    }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanGenerateInstanceRequest))] [NotifyPropertyChangedFor(nameof(CanEdit))] [NotifyPropertyChangedFor(nameof(CanForecast))] [NotifyPropertyChangedFor(nameof(CanRetryLoad))] private bool _isBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanForecast))] [NotifyPropertyChangedFor(nameof(CanRetryLoad))] private bool _isLoaded;
    public bool CanEdit => !IsBusy && !_submissionLocked;
    public bool CanRetryLoad => !IsBusy && !_submissionLocked && (!IsLoaded || (_automaticFlow && _forecastSnapshot is null));
    public bool CanForecast => IsLoaded && CanEdit && !Directory.IsBusy && !Fields.SelectMany(f => f.Images).Any(i => i.IsUploading);
    public DingTalkApprovalFieldViewModel? InvalidField { get; private set; }

    public DingTalkApprovalViewModel(IReimbursementFormWorkspace workspace, Guid id, string token,
        IDingTalkFormClient forms, IDingTalkFormPrefillStore prefills, IDingTalkDirectoryClient directory,
        IDingTalkSubmissionInfoStore selectionStore, IDingTalkInvoiceImages invoices, IDingTalkImagePublisher publisher,
        IDingTalkForecastClient forecast, string imageDirectory, Func<DateOnly>? today = null, IDingTalkApprovalClient? approvalClient = null)
    {
        _workspace = workspace; _id = id; _token = token; _forms = forms; _prefills = prefills;
        _invoices = invoices; _publisher = publisher; _forecast = forecast; _imageDirectory = imageDirectory; _directory = directory;
        _approvalClient = approvalClient; _approvalStore = workspace as IDingTalkApprovalStore;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        Directory = new(directory, new DraftSelectionStore(selectionStore), token);
        long? flowDepartment = null;
        string? flowUser = null;
        Directory.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Directory.SelectedDepartment) or nameof(Directory.SelectedUser))
            {
                if (!_automaticFlow) FlowJson = "";
                else if (!Directory.IsBusy && (flowDepartment != Directory.SelectedDepartment?.DeptId || flowUser != Directory.SelectedUser?.UserId))
                {
                    flowDepartment = Directory.SelectedDepartment?.DeptId; flowUser = Directory.SelectedUser?.UserId;
                    RefreshAutomaticFlow();
                }
            }
            OnPropertyChanged(nameof(CanForecast));
        };
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsLoaded) return;
        IsBusy = true; StatusMessage = "正在加载提审表单并转换发票图片…";
        try
        {
            await Directory.InitializeAsync(cancellationToken);
            if (!string.IsNullOrEmpty(Directory.StatusMessage)) throw new InvalidOperationException(Directory.StatusMessage);
            _original = (await _workspace.GetReimbursementAsync(_id, cancellationToken))?.Form
                ?? throw new InvalidOperationException("报销单已不存在。");
            if (_approvalStore is not null)
            {
                var submitted = await _approvalStore.GetApprovalSubmissionAsync(_id, cancellationToken);
                _submissionUncertain = submitted is { InstanceId: null };
                _submissionLocked = _submissionUncertain || !_original.CanResubmitApproval && (submitted is not null || _original.SubmittedAt is not null);
                InstanceId = _submissionLocked ? submitted?.InstanceId ?? "" : "";
                SubmissionStatus = _submissionUncertain ? "上次提交结果待核对，请先到钉钉查看审批记录，勿重复提交。"
                    : _submissionLocked ? "此报销单已提交。" : _original.CanResubmitApproval ? $"上次审批{_original.ApprovalStatusDisplay}，可修改后重新提交。" : "";
            }
            _template = await _forms.GetReimbursementTemplateAsync(_token, cancellationToken);
            var saved = await _prefills.GetFormPrefillAsync(_template.ProcessCode, cancellationToken);
            var invoiceImages = await _invoices.PrepareAsync(_original, _imageDirectory, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var fields = _template.AllFields.Select(f => new DingTalkApprovalFieldViewModel(f, saved.GetValueOrDefault(f.Id))).ToArray();
            foreach (var field in fields)
            {
                switch (field.Definition.Label.Trim())
                {
                    case "申请日期": field.SetText(_today().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
                    case "报销类型": field.SetText(_original.ReimbursementType); break;
                    case "报销内容": field.SetText(_original.Content); break;
                    case "备注": field.SetText(string.Join(" ", new[] { field.Input.Text.Trim() }.Concat(invoiceImages.InvoiceNumbers).Where(s => !string.IsNullOrWhiteSpace(s)))); break;
                }
                if (field.Definition.ComponentName == "MoneyField" && field.Definition.Label.StartsWith("总金额", StringComparison.Ordinal))
                    field.SetText(_original.TotalAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
                field.Input.Changed += () =>
                {
                    if (!_automaticFlow) FlowJson = "";
                    else { InstanceRequestJson = ""; if (field.Definition.Label.Trim() == "研究方向") RefreshAutomaticFlow(); }
                };
                field.Images.CollectionChanged += (_, _) => { if (!_automaticFlow) FlowJson = ""; else InstanceRequestJson = ""; OnPropertyChanged(nameof(CanForecast)); OnPropertyChanged(nameof(CanSubmit)); };
            }
            Fields = fields; IsLoaded = true; StatusMessage = "";
            IsBusy = false;
            foreach (var field in fields.Where(f => f.IsPhoto && f.Definition.Label == "票据"))
                foreach (string path in invoiceImages.ImagePaths) AddImage(field, path, cancellationToken);
            await WaitForUploadsAsync();
            if (_automaticFlow) await GetFlowAsync(_windowToken, allowIncomplete: true);
        }
        catch (OperationCanceledException) { if (!cancellationToken.IsCancellationRequested) StatusMessage = "加载超时，请重试。"; }
        catch (InvalidOperationException ex) { StatusMessage = ex.Message; }
        catch (Exception) { StatusMessage = "加载提审信息失败，请检查网络、资料库及文档处理器后重试。"; }
        finally { if (!IsLoaded) IsBusy = false; }
    }

    public void AddImage(DingTalkApprovalFieldViewModel field, string path, CancellationToken cancellationToken = default)
    {
        if (!CanEdit || !IsLoaded || !Fields.Contains(field) || !field.IsPhoto || cancellationToken.IsCancellationRequested
            || field.Images.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        var image = new DingTalkApprovalImageViewModel(path);
        image.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(CanForecast)); OnPropertyChanged(nameof(CanSubmit)); };
        field.Images.Add(image); _uploads.Add(image);
        _ = image.StartUploadAsync(_publisher, _token, cancellationToken);
    }

    public void RemoveImage(DingTalkApprovalFieldViewModel field, DingTalkApprovalImageViewModel image)
    {
        if (!CanEdit || !field.Images.Contains(image)) return;
        image.Cancel(); field.Images.Remove(image);
    }

    public void CancelUploads() { foreach (var image in _uploads) image.Cancel(); }
    public Task WaitForUploadsAsync() => Task.WhenAll(_uploads.Select(i => i.UploadTask));

    public async Task GetFlowAsync(CancellationToken cancellationToken = default, bool allowIncomplete = false)
    {
        if (!CanForecast || _original is null || _template is null) return;
        IsBusy = true; FlowJson = ""; InvalidField = null; StatusMessage = "正在校验提审信息…";
        bool saved = false;
        try
        {
            var department = Directory.SelectedDepartment ?? throw new InvalidOperationException("请选择报销人所属部门。");
            var user = Directory.SelectedUser ?? throw new InvalidOperationException("请选择报销人。");
            Validate(!allowIncomplete);
            cancellationToken.ThrowIfCancellationRequested();
            await _workspace.UpdateReimbursementAsync(CurrentFormUpdate(), cancellationToken);
            saved = true;
            var values = CurrentValues(user.UserId);
            StatusMessage = "报销单已同步，正在获取流程…";
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new DingTalkForecastRequest(_template.ProcessCode, department.DeptId, user.UserId, values);
            var response = await _forecast.ForecastAsync(_token, snapshot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            FlowJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            bool success = response.GetProperty("result").TryGetProperty("isForecastSuccess", out var flag) && flag.ValueKind == JsonValueKind.True;
            if (success)
            {
                if (!response.GetProperty("result").TryGetProperty("workflowActivityRules", out var rules) || rules.ValueKind != JsonValueKind.Array
                    || rules.EnumerateArray().Any(r => r.ValueKind != JsonValueKind.Object))
                    throw new InvalidOperationException("流程节点数据不完整，请重新获取流程。");
                WorkflowNodes = rules.EnumerateArray().Select(r => new DingTalkWorkflowNodeViewModel(r, _directory, _token)).ToArray();
                foreach (var node in WorkflowNodes) node.Changed += () =>
                {
                    if (!WorkflowNodes.Contains(node)) return;
                    InstanceRequestJson = ""; OnPropertyChanged(nameof(CanGenerateInstanceRequest)); OnPropertyChanged(nameof(CanSubmit));
                };
                _forecastSnapshot = snapshot;
                OnPropertyChanged(nameof(CanGenerateInstanceRequest));
            }
            StatusMessage = success ? "流程已获取，尚未发起审批。" : "钉钉未成功预测流程，请检查表单信息后重试。";
        }
        catch (OperationCanceledException) { if (!cancellationToken.IsCancellationRequested) StatusMessage = "获取流程超时，请重试。"; }
        catch (InvalidOperationException ex) { StatusMessage = (saved ? "报销单已同步。" : "") + ex.Message; }
        catch (Exception) { StatusMessage = (saved ? "报销单已同步。" : "报销单未同步。") + "获取流程失败，请检查网络与资料库访问权限后重试。"; }
        finally { IsBusy = false; }
    }

    public async Task SubmitAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSubmit || _approvalStore is null || _approvalClient is null) return;
        GenerateInstanceRequest();
        if (InstanceRequestJson.Length == 0) return;
        using var request = JsonDocument.Parse(InstanceRequestJson);
        StatusMessage = "";
        IsBusy = true; IsSubmitting = true; SubmissionStatus = "正在提交报销审批，请勿关闭窗口…";
        bool started = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_automaticFlow) await _workspace.UpdateReimbursementAsync(CurrentFormUpdate(), cancellationToken);
            if (!await _approvalStore.BeginApprovalSubmissionAsync(_id, cancellationToken))
            {
                _submissionLocked = true;
                SubmissionStatus = "此报销单已有提交记录或已被删除，请关闭窗口后刷新核对。";
                return;
            }
            started = true; _submissionLocked = true;
            InstanceId = "";
            InstanceId = await _approvalClient.CreateInstanceAsync(_token, request.RootElement, cancellationToken);
            if (string.IsNullOrWhiteSpace(InstanceId)) throw new InvalidOperationException("审批实例 ID 为空。");
            // Once the remote instance exists, closing or cancellation must not discard its local receipt.
            await _approvalStore.CompleteApprovalSubmissionAsync(_id, InstanceId, CancellationToken.None);
            _submissionUncertain = false;
            SubmissionStatus = "报销审批提交成功，已同步本地提交状态。";
        }
        catch (DingTalkApprovalRejectedException ex)
        {
            try
            {
                await _approvalStore.ClearPendingApprovalSubmissionAsync(_id, CancellationToken.None);
                _submissionLocked = false; _submissionUncertain = false; SubmissionStatus = ex.Message;
            }
            catch (Exception) { _submissionUncertain = true; SubmissionStatus = "钉钉拒绝提交，但本地记录清理失败，请核对后重试。"; }
        }
        catch (Exception)
        {
            _submissionUncertain = started;
            SubmissionStatus = !started ? "提交前读取或保存资料库失败，未发送审批请求。"
                : InstanceId.Length > 0 ? "钉钉审批已创建，本地提交记录保存失败。请重试保存记录，勿重复提交。"
                : "提交结果不明，请先到钉钉核对审批记录，勿重复提交。";
        }
        finally { IsSubmitting = false; IsBusy = false; RefreshSubmissionControls(); }
    }

    public async Task ResetSubmissionAfterVerificationAsync()
    {
        if (!CanResetSubmission || _approvalStore is null) return;
        IsBusy = true;
        try
        {
            await _approvalStore.ClearPendingApprovalSubmissionAsync(_id);
            _submissionLocked = false; _submissionUncertain = false;
            FlowJson = "";
            SubmissionStatus = "已解除待核对状态，请重新获取流程后提交。";
        }
        catch (Exception) { SubmissionStatus = "无法清理待核对记录，请检查资料库权限。"; }
        finally { IsBusy = false; }
    }

    public async Task SaveSubmissionReceiptAsync()
    {
        if (!CanSaveSubmission || _approvalStore is null) return;
        IsBusy = true;
        try
        {
            await _approvalStore.CompleteApprovalSubmissionAsync(_id, InstanceId);
            _submissionUncertain = false; SubmissionStatus = "审批实例及本地提交状态已保存。";
        }
        catch (Exception) { SubmissionStatus = "保存提交记录失败，请检查资料库权限后重试。"; }
        finally { IsBusy = false; }
    }

    public void GenerateInstanceRequest()
    {
        InstanceRequestJson = "";
        if (!CanGenerateInstanceRequest || _forecastSnapshot is not { } snapshot) { StatusMessage = "请先获取流程。"; return; }
        try
        {
            if (_automaticFlow)
            {
                Validate();
                snapshot = snapshot with { FormComponentValues = CurrentValues(snapshot.UserId) };
            }
            var selections = WorkflowNodes.Where(n => n.IsSelectable && !n.IsUnkeyedNotifier)
                .Select(n => new { actionerKey = n.ActorKey, actionerUserIds = n.SelectedUserIds() })
                .Where(n => n.actionerUserIds.Length > 0).ToArray();
            if (selections.Length > 20 || selections.Select(s => s.actionerKey).Distinct().Count() != selections.Length)
                throw new InvalidOperationException("自选节点超过接口限制或节点标识重复，请检查模板。");
            Dictionary<string, object> body = new()
            {
                ["originatorUserId"] = snapshot.UserId,
                ["processCode"] = snapshot.ProcessCode,
                ["deptId"] = snapshot.DeptId,
                ["formComponentValues"] = snapshot.FormComponentValues.Select(v => new { name = v.Name, value = v.Value }).ToArray()
            };
            if (selections.Length > 0) body["targetSelectActioners"] = selections;
            var ccList = WorkflowNodes.Where(n => n.IsSelectable && n.IsUnkeyedNotifier).SelectMany(n => n.SelectedUserIds()).Distinct().ToArray();
            if (ccList.Length > 0) body["ccList"] = ccList;
            InstanceRequestJson = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            StatusMessage = "审批实例请求已生成，尚未发送。";
        }
        catch (InvalidOperationException ex) { StatusMessage = ex.Message; }
    }

    private UpdateReimbursementCommand CurrentFormUpdate()
    {
        var date = Fields.FirstOrDefault(f => f.Definition.Label == "申请日期");
        var type = Fields.FirstOrDefault(f => f.Definition.Label == "报销类型");
        var content = Fields.FirstOrDefault(f => f.Definition.Label == "报销内容");
        var money = Fields.FirstOrDefault(f => f.Definition.ComponentName == "MoneyField" && f.Definition.Label.StartsWith("总金额", StringComparison.Ordinal));
        return new(_id,
            date is null ? _original!.ApplicationDate : string.IsNullOrWhiteSpace(date.Value) ? null : DateOnly.ParseExact(date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            type?.Value ?? _original!.ReimbursementType, content?.Value ?? _original!.Content,
            money is null ? _original!.TotalMinorUnits : string.IsNullOrWhiteSpace(money.Value) ? null : checked((long)(decimal.Parse(money.Value, CultureInfo.InvariantCulture) * 100)));
    }

    private IReadOnlyList<DingTalkComponentValue> CurrentValues(string userId) => Fields.Where(f => !f.IsDeferred && !f.IsNote)
        .Select(f => new DingTalkComponentValue(f.Definition.Id, f.Definition.Label,
            f.IsApplicant ? JsonSerializer.Serialize(new[] { userId }) : f.IsPhoto ? JsonSerializer.Serialize(f.Images.Select(i => i.Uploaded!.Url.AbsoluteUri)) : f.Value,
            f.Definition.ComponentName)).ToArray();

    private void Validate(bool requireValues = true)
    {
        if (Fields.Count(f => !f.IsNote && !f.IsDeferred) > 150) throw new InvalidOperationException("表单超过接口允许的 150 项。");
        foreach (var field in Fields.Where(f => !f.IsNote && !f.IsDeferred && !f.IsApplicant))
        {
            InvalidField = field;
            if (requireValues && field.Definition.Required && field.IsEmpty) throw new InvalidOperationException($"请填写“{field.Definition.Label}”。");
            if (field.IsPhoto && field.Images.Any(i => i.Uploaded is null))
                throw new InvalidOperationException($"“{field.Definition.Label}”中有图片未上传成功，请删除失败图片或重新添加后重试。");
            if (field.IsEmpty || field.IsPhoto) continue;
            if (field.Definition.ComponentName == "DDDateField" && !DateOnly.TryParseExact(field.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidOperationException($"“{field.Definition.Label}”请输入 yyyy-MM-dd 格式的有效日期。");
            if (field.Definition.ComponentName is "MoneyField" or "NumberField")
            {
                if (!decimal.TryParse(field.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                    || (field.Definition.ComponentName == "MoneyField" && (decimal.Round(amount, 2) != amount || amount > long.MaxValue / 100m || amount < long.MinValue / 100m)))
                    throw new InvalidOperationException($"“{field.Definition.Label}”请输入有效数字，金额最多两位小数。");
            }
            if (field.Definition.ComponentName is "TableField" or "DDDateRangeField" or "InnerContactField" or "DepartmentField")
            {
                try { using var document = JsonDocument.Parse(field.Value); if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException(); }
                catch (JsonException) { throw new InvalidOperationException($"“{field.Definition.Label}”需要有效的 JSON 数组。"); }
            }
        }
        InvalidField = null;
    }

    // Changing the applicant for one approval must not overwrite reusable defaults.
    private sealed class DraftSelectionStore(IDingTalkSubmissionInfoStore source) : IDingTalkSubmissionInfoStore
    {
        private DingTalkSubmissionInfo? _value;
        public async Task<DingTalkSubmissionInfo> GetSubmissionInfoAsync(CancellationToken cancellationToken = default)
            => _value ??= await source.GetSubmissionInfoAsync(cancellationToken);
        public Task SaveSubmissionInfoAsync(DingTalkSubmissionInfo info, CancellationToken cancellationToken = default) { _value = info; return Task.CompletedTask; }
    }
}
