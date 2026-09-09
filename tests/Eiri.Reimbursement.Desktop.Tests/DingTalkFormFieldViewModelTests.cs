using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class DingTalkFormFieldViewModelTests
{
    [Fact]
    public void RemovedOptionsAndChangedControlTypesDoNotRestoreOldValues()
    {
        DingTalkFormField definition = new("id", "选择", "DDSelectField", [new("new", "新选项")]);
        var field = new DingTalkFormFieldViewModel(definition, new("DDSelectField", ["old"]));
        Assert.Null(field.SelectedChoice);
        Assert.Empty(field.GetValue().Values);
        var changed = new DingTalkFormFieldViewModel(definition, new("TextField", ["new"]));
        Assert.Null(changed.SelectedChoice);
    }

    [Fact]
    public void MultipleSelectionRetainsAllOptionKeys()
    {
        var field = new DingTalkFormFieldViewModel(new("id", "选择", "DDMultiSelectField", [new("a", "甲"), new("b", "乙")]), null);
        field.Choices[0].IsSelected = true;
        field.Choices[1].IsSelected = true;
        Assert.Equal(["a", "b"], field.GetValue().Values);
        Assert.Equal("甲、乙", field.SelectionSummary);
        field.Choices[0].IsSelected = false;
        Assert.Equal(["b"], field.GetValue().Values);
    }
}
