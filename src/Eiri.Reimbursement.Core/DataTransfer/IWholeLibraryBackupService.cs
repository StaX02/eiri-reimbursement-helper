namespace Eiri.Reimbursement.Core.DataTransfer;

public interface IWholeLibraryBackupService
{
    Task CreateBackupAsync(
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task RestoreBackupAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
