using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public sealed class DingTalkFormChoiceViewModel(DingTalkFormOption option) : ObservableObject
{
    private bool _isSelected;
    public string Key => option.Key;
    public string Value => option.Value;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed class DingTalkFormFieldViewModel : ObservableObject
{
    public DingTalkFormField Definition { get; }
    public string Label => Definition.Label;
    public bool IsSingleChoice => Definition.ComponentName == "DDSelectField";
    public bool IsMultipleChoice => Definition.ComponentName == "DDMultiSelectField";
    public bool IsText => !IsSingleChoice && !IsMultipleChoice;
    public bool IsMultiline => Definition.ComponentName == "TextareaField";
    public IReadOnlyList<DingTalkFormChoiceViewModel> Choices { get; }
    public string SelectionSummary => string.Join("、", Choices.Where(c => c.IsSelected).Select(c => c.Value)) is { Length: > 0 } summary ? summary : "待选择...";
    private string _text = "";
    private DingTalkFormChoiceViewModel? _selectedChoice;
    public event Action? Changed;
    public string Text { get => _text; set { if (SetProperty(ref _text, value)) Changed?.Invoke(); } }
    public DingTalkFormChoiceViewModel? SelectedChoice { get => _selectedChoice; set { if (SetProperty(ref _selectedChoice, value)) Changed?.Invoke(); } }

    public DingTalkFormFieldViewModel(DingTalkFormField field, DingTalkPrefillValue? saved)
    {
        Definition = field;
        var options = IsSingleChoice ? new[] { new DingTalkFormOption("", "待选择...") }.Concat(field.Options) : field.Options;
        Choices = options.Select(option => new DingTalkFormChoiceViewModel(option)).ToArray();
        if (saved?.ComponentName == field.ComponentName)
        {
            if (IsText) _text = saved.Values.FirstOrDefault() ?? "";
            else if (IsSingleChoice) _selectedChoice = Choices.FirstOrDefault(c => saved.Values.Contains(c.Key));
            else foreach (var choice in Choices) choice.IsSelected = saved.Values.Contains(choice.Key);
        }
        foreach (var choice in Choices) choice.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(SelectionSummary)); Changed?.Invoke(); };
    }

    public DingTalkPrefillValue GetValue() => new(Definition.ComponentName,
        IsSingleChoice ? SelectedChoice is null || SelectedChoice.Key.Length == 0 ? [] : [SelectedChoice.Key]
        : IsMultipleChoice ? Choices.Where(c => c.IsSelected).Select(c => c.Key).ToArray()
        : string.IsNullOrEmpty(Text) ? [] : [Text]);
}
