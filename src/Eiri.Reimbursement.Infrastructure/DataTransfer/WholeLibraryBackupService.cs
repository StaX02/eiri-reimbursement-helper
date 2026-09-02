using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Eiri.Reimbursement.Core.DataTransfer;
using Microsoft.Data.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.DataTransfer;

public sealed class WholeLibraryBackupService : IWholeLibraryBackupService
{
    private const string FormatId = "eiri-reimbursement-backup";
    private const int FormatVersion = 1;
    private const string ManifestEntryName = "manifest.json";
    private const string DatabaseEntryName = "library.db";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _libraryRoot;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public WholeLibraryBackupService(string libraryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        _libraryRoot = Path.GetFullPath(libraryRoot);
    }

    public async Task CreateBackupAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        string fullDestinationPath = Path.GetFullPath(destinationPath);
        string destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new ArgumentException("备份包保存路径无效。", nameof(destinationPath));
        Directory.CreateDirectory(destinationDirectory);

        await _operationLock.WaitAsync(cancellationToken);
        string stagingRoot = CreateSiblingTemporaryPath(_libraryRoot, "export");
        string temporaryPackagePath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(stagingRoot);
            await CreateDatabaseSnapshotAsync(
                Path.Combine(stagingRoot, DatabaseEntryName),
                cancellationToken);
            await CopyManagedFilesAsync(
                Path.Combine(stagingRoot, DatabaseEntryName),
                _libraryRoot,
                stagingRoot,
                cancellationToken);
            await ValidateManagedFilesAsync(
                Path.Combine(stagingRoot, DatabaseEntryName),
                stagingRoot,
                cancellationToken);

            List<BackupFileManifest> files = [];
            foreach (string filePath in Directory.EnumerateFiles(
                         stagingRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = NormalizeEntryPath(Path.GetRelativePath(stagingRoot, filePath));
                FileInfo file = new(filePath);
                files.Add(new BackupFileManifest(
                    relativePath,
                    file.Length,
                    await ComputeSha256Async(filePath, cancellationToken)));
            }

            files.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
            BackupManifest manifest = new(
                FormatId,
                FormatVersion,
                DateTimeOffset.UtcNow,
                files);

            await using (FileStream packageStream = new(
                             temporaryPackagePath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous))
            using (ZipArchive archive = new(packageStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (BackupFileManifest file in files)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                    await using Stream entryStream = entry.Open();
                    await using FileStream sourceStream = File.OpenRead(
                        Path.Combine(stagingRoot, file.Path.Replace('/', Path.DirectorySeparatorChar)));
                    await sourceStream.CopyToAsync(entryStream, cancellationToken);
                }

                ZipArchiveEntry manifestEntry = archive.CreateEntry(
                    ManifestEntryName,
                    CompressionLevel.Optimal);
                await using Stream manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(
                    manifestStream,
                    manifest,
                    JsonOptions,
                    cancellationToken);
            }

            File.Move(temporaryPackagePath, fullDestinationPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPackagePath);
            TryDeleteDirectory(stagingRoot);
            _operationLock.Release();
        }
    }

    public async Task RestoreBackupAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("找不到要导入的备份包。", fullSourcePath);
        }

        await _operationLock.WaitAsync(cancellationToken);
        string stagingRoot = CreateSiblingTemporaryPath(_libraryRoot, "restore");
        string previousRoot = CreateSiblingTemporaryPath(_libraryRoot, "previous");
        try
        {
            Directory.CreateDirectory(stagingRoot);
            await ExtractAndValidateAsync(fullSourcePath, stagingRoot, cancellationToken);
            await ValidateDatabaseAsync(Path.Combine(stagingRoot, DatabaseEntryName), cancellationToken);
            await ValidateManagedFilesAsync(
                Path.Combine(stagingRoot, DatabaseEntryName),
                stagingRoot,
                cancellationToken);
            Sqlite.SqliteReimbursementWorkspace stagedWorkspace = new(stagingRoot);
            await stagedWorkspace.InitializeAsync(cancellationToken);
            Directory.CreateDirectory(Path.Combine(stagingRoot, "originals"));
            Directory.CreateDirectory(Path.Combine(stagingRoot, "cache"));
            Directory.CreateDirectory(Path.Combine(stagingRoot, "staging"));
            Directory.CreateDirectory(Path.Combine(stagingRoot, "logs"));

            bool movedPreviousLibrary = false;
            try
            {
                if (Directory.Exists(_libraryRoot))
                {
                    Directory.Move(_libraryRoot, previousRoot);
                    movedPreviousLibrary = true;
                }

                Directory.Move(stagingRoot, _libraryRoot);
            }
            catch
            {
                if (movedPreviousLibrary
                    && !Directory.Exists(_libraryRoot)
                    && Directory.Exists(previousRoot))
                {
                    Directory.Move(previousRoot, _libraryRoot);
                }

                throw;
            }

            TryDeleteDirectory(previousRoot);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("所选文件不是有效的 Eiri 数据备份包。", exception);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            _operationLock.Release();
        }
    }

    private async Task CreateDatabaseSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        string databasePath = Path.Combine(_libraryRoot, DatabaseEntryName);
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException("资料库数据库尚未初始化。");
        }

        await using SqliteConnection source = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        await using SqliteConnection destination = new(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        await using SqliteCommand journalMode = destination.CreateCommand();
        journalMode.CommandText = "PRAGMA journal_mode = DELETE;";
        await journalMode.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task ExtractAndValidateAsync(
        string sourcePath,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        await using FileStream packageStream = File.OpenRead(sourcePath);
        using ZipArchive archive = new(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestEntryName);
        if (manifestEntry is null
            || archive.Entries.Count(entry => string.Equals(
                entry.FullName,
                ManifestEntryName,
                StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new InvalidDataException("所选文件不是有效的 Eiri 数据备份包：缺少清单。");
        }

        BackupManifest? manifest;
        await using (Stream manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                manifestStream,
                JsonOptions,
                cancellationToken);
        }

        if (manifest is null
            || manifest.FormatId != FormatId
            || manifest.FormatVersion != FormatVersion
            || manifest.Files is null)
        {
            throw new InvalidDataException("所选文件不是受支持的 Eiri 数据备份包。");
        }

        Dictionary<string, BackupFileManifest> expectedFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (BackupFileManifest file in manifest.Files)
        {
            ValidateEntryPath(file.Path);
            if ((!string.Equals(file.Path, DatabaseEntryName, StringComparison.OrdinalIgnoreCase)
                    && !file.Path.StartsWith("originals/", StringComparison.OrdinalIgnoreCase))
                || file.Length < 0
                || string.IsNullOrWhiteSpace(file.Sha256)
                || !expectedFiles.TryAdd(file.Path, file))
            {
                throw new InvalidDataException("备份包清单包含无效或重复的文件记录。");
            }
        }

        if (!expectedFiles.ContainsKey(DatabaseEntryName))
        {
            throw new InvalidDataException("备份包中缺少资料库数据库。");
        }

        Dictionary<string, ZipArchiveEntry> payloadEntries = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry == manifestEntry || string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            string entryPath = NormalizeEntryPath(entry.FullName);
            ValidateEntryPath(entryPath);
            if (!payloadEntries.TryAdd(entryPath, entry))
            {
                throw new InvalidDataException("备份包包含重复文件。");
            }
        }

        if (!expectedFiles.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(payloadEntries.Keys))
        {
            throw new InvalidDataException("备份包文件与完整性清单不一致。");
        }

        foreach ((string relativePath, BackupFileManifest expected) in expectedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = payloadEntries[relativePath];
            if (entry.Length != expected.Length)
            {
                throw new InvalidDataException($"备份包中的文件大小校验失败：{relativePath}");
            }

            string destinationPath = ResolveExtractionPath(stagingRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using (Stream source = entry.Open())
            await using (FileStream destination = new(
                             destinationPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            string actualHash = await ComputeSha256Async(destinationPath, cancellationToken);
            if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"备份包中的文件完整性校验失败：{relativePath}");
            }
        }
    }

    private static async Task ValidateDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT (SELECT user_version FROM pragma_user_version), " +
                "(SELECT quick_check FROM pragma_quick_check), " +
                "EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'orders'), " +
                "EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'managed_files');";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)
                || reader.GetInt32(0) is < 1 or > Sqlite.Schema.CurrentVersion
                || !string.Equals(reader.GetString(1), "ok", StringComparison.OrdinalIgnoreCase)
                || reader.GetInt32(2) != 1
                || reader.GetInt32(3) != 1)
            {
                throw new InvalidDataException("备份包中的资料库数据库无效或版本不受支持。");
            }
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("备份包中的资料库数据库无法读取。", exception);
        }
    }

    private static async Task ValidateManagedFilesAsync(
        string databasePath,
        string payloadRoot,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedFileRecord> records = await ReadManagedFileRecordsAsync(
            databasePath,
            cancellationToken);
        HashSet<string> expectedPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ManagedFileRecord record in records)
        {
            expectedPaths.Add(record.RelativePath);
            string managedPath = ResolveExtractionPath(payloadRoot, record.RelativePath);
            await ValidateManagedFileAsync(record, managedPath, cancellationToken);
        }

        string originalsRoot = Path.Combine(payloadRoot, "originals");
        HashSet<string> actualPaths = Directory.Exists(originalsRoot)
            ? Directory.EnumerateFiles(originalsRoot, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeEntryPath(Path.GetRelativePath(payloadRoot, path)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!expectedPaths.SetEquals(actualPaths))
        {
            throw new InvalidDataException("备份包中的原始材料与数据库记录不一致。");
        }
    }

    private static async Task CopyManagedFilesAsync(
        string databasePath,
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(destinationRoot, "originals"));
        IReadOnlyList<ManagedFileRecord> records = await ReadManagedFileRecordsAsync(
            databasePath,
            cancellationToken);
        foreach (ManagedFileRecord record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourcePath = ResolveExtractionPath(sourceRoot, record.RelativePath);
            await ValidateManagedFileAsync(record, sourcePath, cancellationToken);
            string destinationPath = ResolveExtractionPath(destinationRoot, record.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using FileStream source = File.OpenRead(sourcePath);
            await using FileStream destination = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<ManagedFileRecord>> ReadManagedFileRecordsAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        List<ManagedFileRecord> records = [];
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        await using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path, byte_length, sha256 FROM managed_files;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string relativePath = NormalizeEntryPath(reader.GetString(0));
            ValidateEntryPath(relativePath);
            if (!relativePath.StartsWith("originals/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"数据库引用了备份范围之外的受管文件：{relativePath}");
            }
            if (!seenPaths.Add(relativePath))
            {
                throw new InvalidDataException($"数据库包含重复的受管文件路径：{relativePath}");
            }

            records.Add(new ManagedFileRecord(relativePath, reader.GetInt64(1), reader.GetString(2)));
        }

        return records;
    }

    private static async Task ValidateManagedFileAsync(
        ManagedFileRecord record,
        string managedPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(managedPath))
        {
            throw new InvalidDataException($"数据库引用的受管文件缺失：{record.RelativePath}");
        }

        FileInfo file = new(managedPath);
        if (file.Length != record.Length)
        {
            throw new InvalidDataException(
                $"受管文件大小与数据库记录不一致：{record.RelativePath}");
        }

        string actualHash = await ComputeSha256Async(managedPath, cancellationToken);
        if (!string.Equals(actualHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"受管文件内容与数据库记录不一致：{record.RelativePath}");
        }
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string ResolveExtractionPath(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root);
        string destinationPath = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("备份包包含越界文件路径。");
        }

        return destinationPath;
    }

    private static void ValidateEntryPath(string path)
    {
        string[] segments = path.Split('/');
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || path.Contains(':', StringComparison.Ordinal)
            || segments.Any(segment =>
                segment is "" or "." or ".."
                || segment.EndsWith(' ')
                || segment.EndsWith('.')
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException("备份包包含无效文件路径。");
        }
    }

    private static string NormalizeEntryPath(string path) => path.Replace('\\', '/');

    private static string CreateSiblingTemporaryPath(string root, string purpose)
    {
        string parent = Path.GetDirectoryName(root)
            ?? throw new InvalidOperationException("资料库路径没有有效的父目录。");
        return Path.Combine(parent, $".{Path.GetFileName(root)}.{purpose}.{Guid.NewGuid():N}");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record BackupManifest(
        string FormatId,
        int FormatVersion,
        DateTimeOffset CreatedAt,
        IReadOnlyList<BackupFileManifest> Files);

    private sealed record BackupFileManifest(string Path, long Length, string Sha256);

    private sealed record ManagedFileRecord(string RelativePath, long Length, string Sha256);
}
