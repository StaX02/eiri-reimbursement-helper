using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Eiri.Reimbursement.Core;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Invoices;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public sealed record OrderPlatformOption(OrderPlatform Value, string DisplayName);

public partial class MainWindowViewModel(IReimbursementWorkspace workspace) : ObservableObject
{
    private static readonly IReadOnlyList<OrderPlatformOption> AvailablePlatformOptions =
    [
        new(OrderPlatform.Taobao, OrderPlatform.Taobao.ToDisplayName()),
        new(OrderPlatform.JD, OrderPlatform.JD.ToDisplayName()),
        new(OrderPlatform.Other, OrderPlatform.Other.ToDisplayName()),
    ];
    private readonly IReimbursementWorkspace _workspace = workspace;
    private int _selectionVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrderCountText))]
    private ObservableCollection<OrderListItem> _orders = [];

    [ObservableProperty]
    private ObservableCollection<MaterialItemViewModel> _materials = [];

    [ObservableProperty]
    private ObservableCollection<InvoiceEditorViewModel> _invoices = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedOrderHeading))]
    [NotifyPropertyChangedFor(nameof(SelectedOrderSubmissionStatusDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedOrderRefundStatusDisplay))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(CanDeleteOrder))]
    [NotifyPropertyChangedFor(nameof(CanEditOrderMilestones))]
    private OrderListItem? _selectedOrder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveInvoiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeInvoiceCommand))]
    private InvoiceEditorViewModel? _selectedInvoice;

    [ObservableProperty]
    private string _extractedText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "正在加载订单…";

    [ObservableProperty]
    private bool _isSelectedOrderSubmitted;

    [ObservableProperty]
    private bool _isSelectedOrderRefunded;

    [ObservableProperty]
    private OrderPlatformOption _selectedPlatform = AvailablePlatformOptions[^1];

    [ObservableProperty]
    private OrderPlatformOption _selectedOrderPlatform = AvailablePlatformOptions[^1];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveInvoiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeInvoiceCommand))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(CanDeleteOrder))]
    [NotifyPropertyChangedFor(nameof(CanEditOrderMilestones))]
    private bool _isBusy;

    public string OrderCountText => $"共 {Orders.Count} 个订单";

    public IReadOnlyList<OrderPlatformOption> PlatformOptions => AvailablePlatformOptions;

    public bool CanImport => SelectedOrder is not null && !IsBusy;

    public bool CanDeleteOrder => SelectedOrder is not null && !IsBusy;

    public bool CanEditOrderMilestones => SelectedOrder is not null && !IsBusy;

    public string SelectedOrderHeading => SelectedOrder switch
    {
        null => "选择一个订单",
        { ExternalOrderNumber: { Length: > 0 } number } => number,
        { } order => $"订单 {order.Id.ToString()[..8]}",
    };

    public string SelectedOrderSubmissionStatusDisplay => SelectedOrder?.SubmittedAt is { } submittedAt
        ? $"已提交 · {submittedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "未提交";

    public string SelectedOrderRefundStatusDisplay => SelectedOrder?.RefundedAt is { } refundedAt
        ? $"已返款 · {refundedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "未返款";

    public Task LoadAsync() => RefreshAsync();

    public async Task ImportFilesAsync(
        IReadOnlyList<string> sourcePaths,
        ManagedFileRole role)
    {
        if (SelectedOrder is null || sourcePaths.Count == 0 || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = role == ManagedFileRole.InvoicePdf
            ? $"正在导入并解析 {sourcePaths.Count} 份发票…"
            : $"正在保存 {sourcePaths.Count} 份辅助材料…";
        OrderId orderId = SelectedOrder.Id;

        try
        {
            ImportMaterialsResult result = await _workspace.ImportMaterialsAsync(
                new ImportMaterialsCommand(orderId, sourcePaths, role));
            await LoadOrderDetailAsync(orderId, ++_selectionVersion);
            await ReloadOrdersAsync(orderId);

            int duplicateCount = result.Items.Count(item => item.Outcome == MaterialImportOutcome.Duplicate);
            int rejectedCount = result.Items.Count(item => item.Outcome == MaterialImportOutcome.Rejected);
            StatusMessage = $"导入完成：新增 {result.ImportedCount}，重复 {duplicateCount}，拒绝 {rejectedCount}。";
            if (result.AnalysisFailureCount > 0)
            {
                StatusMessage += $" {result.AnalysisFailureCount} 份发票解析失败，可稍后重试。";
            }
            AppendImportIssues(result);
        }
        catch (Exception exception)
        {
            StatusMessage = $"导入失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task DeleteSelectedOrderAsync() => SelectedOrder is null
        ? Task.CompletedTask
        : DeleteOrdersAsync([SelectedOrder.Id]);

    public async Task DeleteOrdersAsync(IReadOnlyList<OrderId> orderIds)
    {
        OrderId[] distinctOrderIds = orderIds.Distinct().ToArray();
        if (distinctOrderIds.Length == 0 || IsBusy)
        {
            return;
        }

        OrderId? selectedOrderId = SelectedOrder?.Id;
        bool deletesSelectedOrder = selectedOrderId is not null
            && distinctOrderIds.Contains(selectedOrderId.Value);
        IsBusy = true;
        StatusMessage = distinctOrderIds.Length == 1
            ? "正在删除订单及其受管材料…"
            : $"正在删除 {distinctOrderIds.Length} 个订单及其受管材料…";

        try
        {
            foreach (OrderId orderId in distinctOrderIds)
            {
                await _workspace.DeleteOrderAsync(orderId);
            }

            if (deletesSelectedOrder)
            {
                SelectedOrder = null;
                Materials = [];
                Invoices = [];
                SelectedInvoice = null;
            }

            await ReloadOrdersAsync(deletesSelectedOrder ? null : selectedOrderId);
            StatusMessage = $"已删除 {distinctOrderIds.Length} 个订单及其受管材料。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"删除订单失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetOrdersMilestoneAsync(
        IReadOnlyList<OrderId> orderIds,
        Milestone milestone,
        bool isReached)
    {
        OrderId[] distinctOrderIds = orderIds.Distinct().ToArray();
        if (distinctOrderIds.Length == 0 || IsBusy)
        {
            return;
        }

        OrderId? selectedOrderId = SelectedOrder?.Id;
        DateTimeOffset? occurredAt = isReached ? DateTimeOffset.UtcNow : null;
        string milestoneName = milestone switch
        {
            Milestone.Submitted => isReached ? "已提交" : "未提交",
            Milestone.Refunded => isReached ? "已返款" : "未返款",
            Milestone.Exported => isReached ? "已导出" : "未导出",
            _ => throw new ArgumentOutOfRangeException(nameof(milestone)),
        };

        IsBusy = true;
        StatusMessage = $"正在将 {distinctOrderIds.Length} 个订单设为{milestoneName}…";
        try
        {
            foreach (OrderId orderId in distinctOrderIds)
            {
                await _workspace.SetMilestoneAsync(
                    new SetMilestoneCommand(orderId, milestone, occurredAt));
            }

            await ReloadOrdersAsync(selectedOrderId);
            StatusMessage = $"已将 {distinctOrderIds.Length} 个订单设为{milestoneName}。";
        }
        catch (Exception exception)
        {
            IsSelectedOrderSubmitted = SelectedOrder?.SubmittedAt is not null;
            IsSelectedOrderRefunded = SelectedOrder?.RefundedAt is not null;
            StatusMessage = $"更新订单状态失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ClearOrdersSubmissionAndRefundAsync(IReadOnlyList<OrderId> orderIds)
    {
        OrderId[] distinctOrderIds = orderIds.Distinct().ToArray();
        if (distinctOrderIds.Length == 0 || IsBusy)
        {
            return;
        }

        OrderId? selectedOrderId = SelectedOrder?.Id;
        IsBusy = true;
        StatusMessage = $"正在清空 {distinctOrderIds.Length} 个订单的提交及返款状态…";
        try
        {
            foreach (OrderId orderId in distinctOrderIds)
            {
                await _workspace.SetMilestoneAsync(
                    new SetMilestoneCommand(orderId, Milestone.Submitted, null));
                await _workspace.SetMilestoneAsync(
                    new SetMilestoneCommand(orderId, Milestone.Refunded, null));
            }

            await ReloadOrdersAsync(selectedOrderId);
            StatusMessage = $"已清空 {distinctOrderIds.Length} 个订单的提交及返款状态。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"清空订单状态失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task CreateOrderAsync()
    {
        IsBusy = true;
        StatusMessage = "正在创建订单…";

        try
        {
            OrderId orderId = await _workspace.CreateOrderAsync(new CreateOrderCommand(SelectedPlatform.Value));
            await ReloadOrdersAsync(orderId);
            StatusMessage = "订单已创建。请选择发票区或辅助材料区拖放文件。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"创建订单失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "正在加载订单…";

        try
        {
            await ReloadOrdersAsync(SelectedOrder?.Id);
            StatusMessage = Orders.Count == 0
                ? "还没有订单。点击“新建订单”开始整理报销材料。"
                : "订单列表已更新。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"加载订单失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditInvoice))]
    private async Task SaveInvoiceAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        if (!TryParseMinorUnits(SelectedInvoice.AmountText, out long totalMinorUnits))
        {
            StatusMessage = "发票金额格式无效，请输入例如 159.90 或 -20.00。";
            return;
        }

        IsBusy = true;
        StatusMessage = "正在保存平台及发票字段…";
        try
        {
            InvoiceLineCorrection[] lines = SelectedInvoice.ProductNamesText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => new InvoiceLineCorrection(name))
                .ToArray();
            await _workspace.UpdateInvoiceAsync(new UpdateInvoiceCommand(
                SelectedInvoice.Id,
                SelectedInvoice.MerchantName,
                SelectedInvoice.InvoiceNumber,
                totalMinorUnits,
                lines));

            OrderId? orderId = SelectedOrder?.Id;
            if (orderId is not null)
            {
                await _workspace.UpdateOrderPlatformAsync(new UpdateOrderPlatformCommand(
                    orderId.Value,
                    SelectedOrderPlatform.Value));
                await LoadOrderDetailAsync(orderId.Value, ++_selectionVersion, SelectedInvoice.Id);
                await ReloadOrdersAsync(orderId.Value);
            }

            StatusMessage = "平台及发票字段已保存。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"保存平台及发票失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditInvoice))]
    private async Task AnalyzeInvoiceAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        IsBusy = true;
        ExtractedText = string.Empty;
        StatusMessage = "正在隔离进程中提取 PDF 文本…";
        try
        {
            InvoiceId invoiceId = SelectedInvoice.Id;
            DocumentAnalysis analysis = await _workspace.AnalyzeInvoiceAsync(invoiceId);
            ExtractedText = string.Join(
                Environment.NewLine + Environment.NewLine,
                analysis.TextBlocks.Select(block => block.Text));

            if (SelectedOrder is { } order)
            {
                await LoadOrderDetailAsync(order.Id, ++_selectionVersion, invoiceId);
            }

            StatusMessage = analysis.TextBlocks.Count == 0
                ? "PDF 没有可读取的文本层，后续需要 OCR 回退。"
                : $"已提取 {analysis.TextBlocks.Count} 个文本块。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"PDF 文本提取失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedOrderChanged(OrderListItem? value)
    {
        int version = ++_selectionVersion;
        ExtractedText = string.Empty;
        SelectedOrderPlatform = value is null
            ? AvailablePlatformOptions[^1]
            : AvailablePlatformOptions.Single(option => option.Value == value.Platform);
        IsSelectedOrderSubmitted = value?.SubmittedAt is not null;
        IsSelectedOrderRefunded = value?.RefundedAt is not null;
        if (value is null)
        {
            Materials = [];
            Invoices = [];
            SelectedInvoice = null;
            return;
        }

        _ = LoadOrderDetailSafelyAsync(value.Id, version);
    }

    partial void OnSelectedInvoiceChanged(InvoiceEditorViewModel? value)
    {
        ExtractedText = string.Empty;
    }

    private bool CanRunCommand() => !IsBusy;

    private bool CanEditInvoice() => SelectedInvoice is not null && !IsBusy;

    private async Task ReloadOrdersAsync(OrderId? selectedOrderId)
    {
        IReadOnlyList<OrderListItem> items = await _workspace.SearchOrdersAsync(new OrderQuery());
        Orders = new ObservableCollection<OrderListItem>(items);
        OnPropertyChanged(nameof(OrderCountText));

        SelectedOrder = selectedOrderId is null
            ? null
            : Orders.FirstOrDefault(order => order.Id == selectedOrderId.Value);
    }

    private async Task LoadOrderDetailSafelyAsync(OrderId orderId, int version)
    {
        try
        {
            await LoadOrderDetailAsync(orderId, version);
        }
        catch (Exception exception)
        {
            if (version == _selectionVersion)
            {
                StatusMessage = $"加载订单详情失败：{exception.Message}";
            }
        }
    }

    private async Task LoadOrderDetailAsync(
        OrderId orderId,
        int version,
        InvoiceId? selectedInvoiceId = null)
    {
        selectedInvoiceId ??= SelectedInvoice?.Id;
        OrderDetail? detail = await _workspace.GetOrderAsync(orderId);
        if (version != _selectionVersion)
        {
            return;
        }

        Materials = detail is null
            ? []
            : new ObservableCollection<MaterialItemViewModel>(
                detail.Materials.Select(material => new MaterialItemViewModel(material)));
        Invoices = detail is null
            ? []
            : new ObservableCollection<InvoiceEditorViewModel>(
                detail.Invoices.Select(invoice => new InvoiceEditorViewModel(invoice)));
        SelectedInvoice = selectedInvoiceId is null
            ? Invoices.FirstOrDefault()
            : Invoices.FirstOrDefault(invoice => invoice.Id == selectedInvoiceId.Value);
    }

    private void AppendImportIssues(ImportMaterialsResult result)
    {
        string[] issues = result.Items
            .Where(item => item.Outcome != MaterialImportOutcome.Imported)
            .Take(2)
            .Select(item => $"{Path.GetFileName(item.SourcePath)}：{item.Message}")
            .ToArray();
        if (issues.Length > 0)
        {
            StatusMessage += $" {string.Join("；", issues)}";
        }
    }

    private static bool TryParseMinorUnits(string text, out long minorUnits)
    {
        bool parsed = decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal amount)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        if (!parsed)
        {
            minorUnits = 0;
            return false;
        }

        try
        {
            minorUnits = decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
            return true;
        }
        catch (OverflowException)
        {
            minorUnits = 0;
            return false;
        }
    }
}

public sealed record MaterialItemViewModel(ManagedMaterial Material)
{
    public string OriginalFileName => Material.OriginalFileName;

    public string RoleDisplay => Material.Role switch
    {
        ManagedFileRole.InvoicePdf => "发票 PDF",
        ManagedFileRole.OrderScreenshot => "订单截图等辅助材料",
        _ => Material.Role.ToString(),
    };

    public string SizeDisplay => Material.ByteLength switch
    {
        >= 1_048_576 => $"{Material.ByteLength / 1_048_576d:N1} MB",
        >= 1_024 => $"{Material.ByteLength / 1_024d:N1} KB",
        _ => $"{Material.ByteLength} B",
    };

    public string ProcessingStateDisplay => Material.ProcessingState switch
    {
        MaterialProcessingState.Pending => "待处理",
        MaterialProcessingState.Processing => "处理中",
        MaterialProcessingState.Processed => "已处理",
        MaterialProcessingState.Stored => "已保存",
        MaterialProcessingState.Failed => "处理失败",
        _ => Material.ProcessingState.ToString(),
    };
}

public partial class InvoiceEditorViewModel : ObservableObject
{
    public InvoiceEditorViewModel(InvoiceDetail invoice)
    {
        Id = invoice.Id;
        ManagedFileId = invoice.ManagedFileId;
        OriginalFileName = invoice.OriginalFileName;
        NeedsReview = invoice.NeedsReview;
        MerchantName = invoice.MerchantName;
        InvoiceNumber = invoice.InvoiceNumber;
        AmountText = invoice.TotalAmount.ToString("0.00", CultureInfo.CurrentCulture);
        ProductNamesText = string.Join(
            Environment.NewLine,
            invoice.Lines.Where(line => line.IsEffective).OrderBy(line => line.Sequence).Select(line => line.Name));
    }

    public InvoiceId Id { get; }

    public ManagedFileId ManagedFileId { get; }

    public string OriginalFileName { get; }

    public bool NeedsReview { get; }

    [ObservableProperty]
    private string _merchantName;

    [ObservableProperty]
    private string _invoiceNumber;

    [ObservableProperty]
    private string _amountText;

    [ObservableProperty]
    private string _productNamesText;
}
