using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.Reimbursements;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public partial class ReimbursementListItemViewModel(ReimbursementForm form) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplicationDateDisplay))]
    [NotifyPropertyChangedFor(nameof(ReimbursementType))]
    [NotifyPropertyChangedFor(nameof(ContentDisplay))]
    [NotifyPropertyChangedFor(nameof(TotalAmount))]
    private ReimbursementForm _form = form;
    public Guid Id => Form.Id;
    public string ApplicationDateDisplay => Form.ApplicationDateDisplay;
    public string ReimbursementType => Form.ReimbursementType;
    public string ContentDisplay => Form.ContentDisplay;
    public decimal? TotalAmount => Form.TotalAmount;
}
