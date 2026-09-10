using System.Globalization;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public sealed partial class DingTalkSubmissionInfoViewModel
{
    private IReadOnlyList<DingTalkFormFieldViewModel> _formFields = [];
    private DingTalkFormTemplate? _template;
    public IReadOnlyList<DingTalkFormFieldViewModel> FormFields { get => _formFields; private set => SetProperty(ref _formFields, value); }
    public string? ProcessCode => _template?.ProcessCode;
    public string? ApplicantFieldId => _template?.ApplicantFieldId;
    public Task<bool> PendingFormSave { get; private set; } = Task.FromResult(true);

    public Task LoadFormAsync(CancellationToken cancellationToken = default)
    {
        if (formClient is null || formStore is null) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            if (_template is not null && !await SaveFormAsync()) return;
            var template = await formClient.GetReimbursementTemplateAsync(accessToken, cancellationToken);
            var saved = await formStore.GetFormPrefillAsync(template.ProcessCode, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var fields = template.Fields.Select(field => new DingTalkFormFieldViewModel(field, saved.GetValueOrDefault(field.Id))).ToArray();
            _template = template;
            FormFields = fields;
            foreach (var field in fields) field.Changed += QueueFormSave;
            OnPropertyChanged(nameof(ProcessCode));
            OnPropertyChanged(nameof(ApplicantFieldId));
            StatusMessage = fields.Length == 0 ? "模板没有可预填的字段。" : "";
        }, "正在获取审批模板…", cancellationToken);
    }

    private void QueueFormSave() => _ = SaveFormAsync();

    public Task<bool> SaveFormAsync()
    {
        if (_template is null || formStore is null) return PendingFormSave;
        var code = _template.ProcessCode;
        var fields = FormFields.ToDictionary(field => field.Definition.Id, field => field.GetValue());
        bool valid = FormFields.All(field => field.Definition.ComponentName != "NumberField" || string.IsNullOrWhiteSpace(field.Text)
            || decimal.TryParse(field.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out _));
        PendingFormSave = SaveAfterAsync(PendingFormSave, code, fields, valid);
        return PendingFormSave;
    }

    private async Task<bool> SaveAfterAsync(Task<bool> previous, string code, IReadOnlyDictionary<string, DingTalkPrefillValue> values, bool valid)
    {
        await previous;
        if (!valid) { StatusMessage = "数字字段的值无效，请修正或清空。"; return false; }
        try
        {
            await formStore!.SaveFormPrefillAsync(code, values);
            StatusMessage = "已保存。";
            return true;
        }
        catch (Exception) { StatusMessage = "预填信息保存失败，请检查资料库权限后点击保存重试。"; return false; }
    }
}
