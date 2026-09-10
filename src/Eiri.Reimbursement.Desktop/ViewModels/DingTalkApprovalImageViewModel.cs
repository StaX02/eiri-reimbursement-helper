using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public sealed class DingTalkApprovalImageViewModel(string path) : ObservableObject
{
    private readonly CancellationTokenSource _removed = new();
    private bool _isUploading = true;
    private string _status = "正在上传…";
    private DingTalkUploadedImage? _uploaded;
    public string Path { get; } = path;
    public string FileName => System.IO.Path.GetFileName(Path);
    public bool IsUploading { get => _isUploading; private set => SetProperty(ref _isUploading, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public DingTalkUploadedImage? Uploaded { get => _uploaded; private set => SetProperty(ref _uploaded, value); }
    public Task UploadTask { get; private set; } = Task.CompletedTask;
    public void Cancel() => _removed.Cancel();
    public Task StartUploadAsync(IDingTalkImagePublisher publisher, string token, CancellationToken cancellationToken)
        => UploadTask = UploadCoreAsync(publisher, token, cancellationToken);
    private async Task UploadCoreAsync(IDingTalkImagePublisher publisher, string token, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _removed.Token);
        try
        {
            var result = await publisher.UploadAsync(token, Path, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            Uploaded = result; Status = "上传成功";
        }
        catch (OperationCanceledException) { Status = linked.IsCancellationRequested ? "上传已取消" : "上传超时，请删除后重新添加。"; }
        catch (InvalidOperationException ex) { Status = "上传失败：" + ex.Message; }
        catch (IOException) { Status = "上传失败：无法读取图片，请检查文件后重新添加。"; }
        catch (Exception) { Status = "上传失败，请检查网络与连接后删除并重新添加。"; }
        finally { IsUploading = false; }
    }
}
