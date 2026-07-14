using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

public sealed partial class DatabaseBackupService
{
    public const int MaximumValidBackupCount = 30;
    public const string BackupDirectoryName = "backups";
    public const string RestoreDirectoryName = "restore";
    public const string PendingRestoreDatabaseFileName = "pending_restore.db";
    public const string PendingRestoreMarkerFileName = "pending_restore.json";
    public const string RestoreResultFileName = "restore_result.json";
    public const string LastSuccessfulVersionSettingKey = "database.last_successful_version";

    private static readonly string[] RequiredTables = { "strategy_config", "trade_log", "app_settings" };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _processGate;

    public DatabaseBackupService(
        string databasePath,
        string backupDirectory,
        string restoreDirectory,
        string applicationVersion,
        Func<DateTimeOffset>? clock = null)
    {
        DatabasePath = Path.GetFullPath(databasePath ?? throw new ArgumentNullException(nameof(databasePath)));
        BackupDirectory = Path.GetFullPath(backupDirectory ?? throw new ArgumentNullException(nameof(backupDirectory)));
        RestoreDirectory = Path.GetFullPath(restoreDirectory ?? throw new ArgumentNullException(nameof(restoreDirectory)));
        ApplicationVersion = NormalizeVersion(applicationVersion);
        _clock = clock ?? (() => DateTimeOffset.Now);
        _processGate = ProcessGates.GetOrAdd(BackupDirectory, _ => new SemaphoreSlim(1, 1));
    }

    public string DatabasePath { get; }

    public string BackupDirectory { get; }

    public string RestoreDirectory { get; }

    public string ApplicationVersion { get; }

    public string PendingRestoreDatabasePath => Path.Combine(RestoreDirectory, PendingRestoreDatabaseFileName);

    public string PendingRestoreMarkerPath => Path.Combine(RestoreDirectory, PendingRestoreMarkerFileName);

    public string RestoreResultPath => Path.Combine(RestoreDirectory, RestoreResultFileName);

    public static DatabaseBackupService CreateDefault(string applicationVersion, Func<DateTimeOffset>? clock = null)
    {
        string databasePath = new LocalDatabase().DatabasePath;
        string applicationDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("无法解析本地数据库目录。");
        return new DatabaseBackupService(
            databasePath,
            Path.Combine(applicationDirectory, BackupDirectoryName),
            Path.Combine(applicationDirectory, RestoreDirectoryName),
            applicationVersion,
            clock);
    }

    public async Task<DatabaseBackupOperationResult> CreateBackupAsync(
        DatabaseBackupKind kind,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteExclusiveAsync(
                () => Task.Run(() => CreateBackupCore(kind), cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DatabaseBackupBusyException ex)
        {
            return DatabaseBackupOperationResult.Failed(ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DatabaseBackupOperationResult.Failed("数据库备份失败：" + ex.Message);
        }
    }

    public async Task<DatabaseDailyBackupResult> EnsureDailyBackupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteExclusiveAsync(
                () => Task.Run(EnsureDailyBackupCore, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DatabaseBackupBusyException ex)
        {
            return new DatabaseDailyBackupResult(false, false, ex.Message, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DatabaseDailyBackupResult(false, false, "每日自动备份失败：" + ex.Message, null);
        }
    }

    public IReadOnlyList<DatabaseBackupValidationResult> ReadBackupList()
    {
        Directory.CreateDirectory(BackupDirectory);
        return Directory
            .EnumerateFiles(BackupDirectory, "*.db", SearchOption.TopDirectoryOnly)
            .Select(path => ValidateDatabaseFile(path, BackupDirectory, requireControlledName: true))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DatabaseBackupSummary ReadSummary()
    {
        return BuildSummary(ReadBackupList());
    }

    public DatabaseBackupSummary BuildSummary(IReadOnlyList<DatabaseBackupValidationResult> backups)
    {
        ArgumentNullException.ThrowIfNull(backups);
        DatabaseBackupValidationResult[] valid = backups.Where(item => item.IsValid).ToArray();
        DateTime today = _clock().LocalDateTime.Date;
        bool hasAutomaticToday = valid.Any(item =>
            item.CreatedAt.LocalDateTime.Date == today
            && item.BackupKind is DatabaseBackupKind.Daily or DatabaseBackupKind.PreUpgrade);

        return new DatabaseBackupSummary(
            DatabasePath,
            BackupDirectory,
            valid.Length,
            valid.OrderByDescending(item => item.CreatedAt).FirstOrDefault()?.CreatedAt,
            hasAutomaticToday,
            hasAutomaticToday ? "今日自动备份已完成" : "今日尚无自动备份");
    }

    public DatabaseBackupValidationResult ValidateBackup(string backupPath)
        => ValidateDatabaseFile(backupPath, BackupDirectory, requireControlledName: true);

    public async Task<DatabaseRestoreStageResult> StageRestoreAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteExclusiveAsync(
                () => Task.Run(() => StageRestoreCore(backupPath), cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DatabaseBackupBusyException ex)
        {
            return new DatabaseRestoreStageResult(false, ex.Message, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DatabaseRestoreStageResult(false, "恢复请求准备失败：" + ex.Message, null);
        }
    }

    internal Task<T> ExecuteExclusiveAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        => ExecuteExclusiveCoreAsync(operation, cancellationToken);

    internal DatabaseBackupOperationResult CreateBackupUnderAcquiredLock(DatabaseBackupKind kind)
        => CreateBackupCore(kind);

    internal DatabaseBackupValidationResult ValidateDatabaseFile(
        string filePath,
        string allowedDirectory,
        bool requireControlledName)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            return InvalidValidation(filePath, "文件路径无效：" + ex.Message);
        }

        string fileName = Path.GetFileName(fullPath);
        bool isWithinDirectory = IsDirectChildOf(fullPath, allowedDirectory);
        bool parsed = TryParseBackupFileName(fileName, out DateTimeOffset parsedCreatedAt, out string version, out DatabaseBackupKind kind);
        DateTimeOffset createdAt = parsed
            ? parsedCreatedAt
            : SafeGetLastWriteTime(fullPath);
        long fileSize = SafeGetLength(fullPath);

        if (!isWithinDirectory)
        {
            return new DatabaseBackupValidationResult(
                false,
                "not checked",
                fullPath,
                fileName,
                fileSize,
                createdAt,
                parsed ? version : "--",
                parsed ? kind : null,
                "文件不在受控目录中。",
                parsed);
        }

        if (!File.Exists(fullPath))
        {
            return new DatabaseBackupValidationResult(
                false,
                "not checked",
                fullPath,
                fileName,
                0,
                createdAt,
                parsed ? version : "--",
                parsed ? kind : null,
                "备份文件不存在。",
                parsed);
        }

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            return new DatabaseBackupValidationResult(
                false,
                "not checked",
                fullPath,
                fileName,
                fileSize,
                createdAt,
                parsed ? version : "--",
                parsed ? kind : null,
                "不允许使用符号链接或重解析点作为备份文件。",
                parsed);
        }

        string walPath = fullPath + "-wal";
        if (File.Exists(walPath) && SafeGetLength(walPath) > 0)
        {
            return new DatabaseBackupValidationResult(
                false,
                "not checked",
                fullPath,
                fileName,
                fileSize,
                createdAt,
                parsed ? version : "--",
                parsed ? kind : null,
                "备份依赖非空WAL旁文件，不是可独立恢复的单文件快照。",
                parsed);
        }

        if (requireControlledName && !parsed)
        {
            return new DatabaseBackupValidationResult(
                false,
                "not checked",
                fullPath,
                fileName,
                fileSize,
                createdAt,
                "--",
                null,
                "文件名不符合受控备份格式。",
                false);
        }

        try
        {
            using var connection = OpenReadOnlyConnection(fullPath);
            string integrityResult = ReadIntegrityResult(connection);
            if (!string.Equals(integrityResult, "ok", StringComparison.Ordinal))
            {
                return new DatabaseBackupValidationResult(
                    false,
                    integrityResult,
                    fullPath,
                    fileName,
                    fileSize,
                    createdAt,
                    parsed ? version : "--",
                    parsed ? kind : null,
                    "PRAGMA integrity_check 未返回严格的 ok。",
                    parsed);
            }

            string[] missingTables = ReadMissingRequiredTables(connection);
            if (missingTables.Length > 0)
            {
                return new DatabaseBackupValidationResult(
                    false,
                    integrityResult,
                    fullPath,
                    fileName,
                    fileSize,
                    createdAt,
                    parsed ? version : "--",
                    parsed ? kind : null,
                    "缺少基础表：" + string.Join(", ", missingTables),
                    parsed);
            }

            return new DatabaseBackupValidationResult(
                true,
                integrityResult,
                fullPath,
                fileName,
                fileSize,
                createdAt,
                parsed ? version : ApplicationVersion,
                parsed ? kind : null,
                null,
                parsed);
        }
        catch (Exception ex)
        {
            return new DatabaseBackupValidationResult(
                false,
                "error",
                fullPath,
                fileName,
                fileSize,
                createdAt,
                parsed ? version : "--",
                parsed ? kind : null,
                ex.Message,
                parsed);
        }
        finally
        {
            CleanupReadOnlyValidationSidecars(fullPath);
        }
    }

    internal static string ComputeSha256(string filePath)
    {
        using FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static void CopyFileDurably(string sourcePath, string destinationPath)
    {
        using FileStream source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using FileStream destination = File.Open(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
    }

    internal static void WriteJsonAtomically<T>(string finalPath, T value)
    {
        string directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("无法解析JSON文件目录。");
        Directory.CreateDirectory(directory);
        string temporaryPath = finalPath + ".tmp";
        SafeDelete(temporaryPath);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        using (FileStream stream = File.Open(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(json);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, finalPath, overwrite: true);
    }

    internal static T? ReadJson<T>(string path)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions);
    }

    internal static bool IsDirectChildOf(string filePath, string directoryPath)
    {
        string fullFilePath = Path.GetFullPath(filePath);
        string? parent = Path.GetDirectoryName(fullFilePath);
        string fullDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return parent is not null
               && string.Equals(
                   parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   fullDirectoryPath,
                   StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeVersion(string? value)
    {
        string version = string.IsNullOrWhiteSpace(value) ? "0.0.0" : value.Trim();
        int metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            version = version[..metadataIndex];
        }

        int prereleaseIndex = version.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0)
        {
            version = version[..prereleaseIndex];
        }

        return version.TrimStart('v', 'V');
    }

    private async Task<T> ExecuteExclusiveCoreAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        FileStream? crossProcessLock = null;
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            string lockPath = Path.Combine(BackupDirectory, ".backup.lock");
            try
            {
                crossProcessLock = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.WriteThrough);
            }
            catch (IOException ex)
            {
                throw new DatabaseBackupBusyException("另一备份或恢复任务正在运行。", ex);
            }

            return await operation().ConfigureAwait(false);
        }
        finally
        {
            crossProcessLock?.Dispose();
            _processGate.Release();
        }
    }

    private DatabaseBackupOperationResult CreateBackupCore(DatabaseBackupKind kind)
    {
        if (!File.Exists(DatabasePath))
        {
            return DatabaseBackupOperationResult.Failed("当前数据库不存在，未创建空备份。");
        }

        Directory.CreateDirectory(BackupDirectory);
        CleanupOrphanTemporaryBackupArtifacts();
        string finalPath = CreateUniqueBackupPath(kind);
        string temporaryPath = finalPath + ".tmp";
        var warnings = new List<string>();

        try
        {
            using (SqliteConnection source = OpenReadOnlyConnection(DatabasePath))
            using (SqliteConnection destination = OpenWritableConnection(temporaryPath))
            {
                source.BackupDatabase(destination);
                NormalizeBackupJournalMode(destination);
            }

            DatabaseBackupValidationResult temporaryValidation = ValidateDatabaseFile(
                temporaryPath,
                BackupDirectory,
                requireControlledName: false);
            if (!temporaryValidation.IsValid)
            {
                SafeDelete(temporaryPath);
                return DatabaseBackupOperationResult.Failed(
                    "备份完整性校验失败：" + (temporaryValidation.Error ?? temporaryValidation.IntegrityResult));
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            if (!TryParseBackupFileName(
                    Path.GetFileName(finalPath),
                    out DateTimeOffset finalCreatedAt,
                    out string finalVersion,
                    out DatabaseBackupKind finalKind))
            {
                SafeDelete(finalPath);
                return DatabaseBackupOperationResult.Failed("备份最终文件名不符合受控格式。");
            }

            var finalValidation = temporaryValidation with
            {
                FilePath = finalPath,
                FileName = Path.GetFileName(finalPath),
                FileSize = new FileInfo(finalPath).Length,
                CreatedAt = finalCreatedAt,
                Version = finalVersion,
                BackupKind = finalKind,
                IsControlledName = true
            };

            warnings.AddRange(ApplyRetentionPolicy(finalValidation.FilePath));
            string message = $"备份成功：{finalValidation.FileName}";
            return new DatabaseBackupOperationResult(true, message, finalValidation, warnings);
        }
        catch (Exception ex)
        {
            SafeDelete(temporaryPath);
            return DatabaseBackupOperationResult.Failed("数据库备份失败：" + ex.Message);
        }
    }

    private DatabaseDailyBackupResult EnsureDailyBackupCore()
    {
        DateTime today = _clock().LocalDateTime.Date;
        Directory.CreateDirectory(BackupDirectory);
        CleanupOrphanTemporaryBackupArtifacts();
        DatabaseBackupValidationResult? existing = Directory
            .EnumerateFiles(BackupDirectory, "*.db", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                string fileName = Path.GetFileName(path);
                return TryParseBackupFileName(fileName, out DateTimeOffset createdAt, out _, out DatabaseBackupKind kind)
                       && createdAt.LocalDateTime.Date == today
                       && kind is DatabaseBackupKind.Daily or DatabaseBackupKind.PreUpgrade
                    ? ValidateDatabaseFile(path, BackupDirectory, requireControlledName: true)
                    : null;
            })
            .Where(item => item?.IsValid == true)
            .OrderByDescending(item => item!.CreatedAt)
            .FirstOrDefault();
        if (existing is not null)
        {
            return new DatabaseDailyBackupResult(true, false, "今日自动备份已存在。", existing);
        }

        DatabaseBackupOperationResult result = CreateBackupCore(DatabaseBackupKind.Daily);
        return new DatabaseDailyBackupResult(
            result.Success,
            result.Success,
            result.Message,
            result.Backup);
    }

    private DatabaseRestoreStageResult StageRestoreCore(string backupPath)
    {
        DatabaseBackupValidationResult validation = ValidateDatabaseFile(
            backupPath,
            BackupDirectory,
            requireControlledName: true);
        if (!validation.CanRestore || validation.BackupKind is null)
        {
            return new DatabaseRestoreStageResult(
                false,
                "只能恢复受控目录中的有效备份：" + (validation.Error ?? validation.IntegrityResult),
                null);
        }

        Directory.CreateDirectory(RestoreDirectory);
        if (File.Exists(PendingRestoreMarkerPath))
        {
            return new DatabaseRestoreStageResult(false, "已有待处理的恢复请求，请先重新启动程序完成处理。", null);
        }

        if (File.Exists(PendingRestoreDatabasePath))
        {
            string orphanPath = PendingRestoreDatabasePath
                                + ".orphan-"
                                + _clock().ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
            try
            {
                File.Move(PendingRestoreDatabasePath, orphanPath, overwrite: false);
            }
            catch (Exception ex)
            {
                return new DatabaseRestoreStageResult(
                    false,
                    "发现没有marker的恢复暂存文件且无法隔离：" + ex.Message,
                    null);
            }
        }

        string pendingTemporaryPath = PendingRestoreDatabasePath + ".tmp";
        string markerTemporaryPath = PendingRestoreMarkerPath + ".tmp";
        SafeDelete(pendingTemporaryPath);
        SafeDelete(markerTemporaryPath);

        try
        {
            string sourceHash = ComputeSha256(validation.FilePath);
            CopyFileDurably(validation.FilePath, pendingTemporaryPath);
            DatabaseBackupValidationResult pendingValidation = ValidateDatabaseFile(
                pendingTemporaryPath,
                RestoreDirectory,
                requireControlledName: false);
            if (!pendingValidation.IsValid)
            {
                throw new InvalidDataException(
                    "恢复暂存副本校验失败：" + (pendingValidation.Error ?? pendingValidation.IntegrityResult));
            }

            string pendingHash = ComputeSha256(pendingTemporaryPath);
            if (!string.Equals(sourceHash, pendingHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("恢复暂存副本SHA-256与源备份不一致。");
            }

            File.Move(pendingTemporaryPath, PendingRestoreDatabasePath, overwrite: false);
            var marker = new DatabaseRestoreMarker(
                _clock(),
                validation.FileName,
                validation.Version,
                DatabaseBackupKindNames.ToStorageValue(validation.BackupKind.Value),
                PendingRestoreDatabaseFileName,
                sourceHash,
                ApplicationVersion);
            WriteJsonAtomically(PendingRestoreMarkerPath, marker);
            return new DatabaseRestoreStageResult(true, "恢复请求已安全暂存。", marker);
        }
        catch
        {
            SafeDelete(pendingTemporaryPath);
            SafeDelete(markerTemporaryPath);
            if (!File.Exists(PendingRestoreMarkerPath))
            {
                SafeDelete(PendingRestoreDatabasePath);
            }

            throw;
        }
    }

    private IReadOnlyList<string> ApplyRetentionPolicy(string newestBackupPath)
    {
        var warnings = new List<string>();
        string[] controlledFiles = Directory
            .EnumerateFiles(BackupDirectory, "*.db", SearchOption.TopDirectoryOnly)
            .Where(path => TryParseBackupFileName(Path.GetFileName(path), out _, out _, out _))
            .ToArray();
        if (controlledFiles.Length <= MaximumValidBackupCount)
        {
            return warnings;
        }

        string? protectedFileName = ReadProtectedSourceBackupFileName();
        DatabaseBackupValidationResult[] validBackups = controlledFiles
            .Select(path => ValidateDatabaseFile(path, BackupDirectory, requireControlledName: true))
            .Where(item => item.IsValid && item.IsControlledName)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        HashSet<string> keep = validBackups
            .Take(MaximumValidBackupCount)
            .Select(item => item.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        keep.Add(Path.GetFullPath(newestBackupPath));
        if (!string.IsNullOrWhiteSpace(protectedFileName))
        {
            string protectedPath = Path.Combine(BackupDirectory, protectedFileName);
            if (IsDirectChildOf(protectedPath, BackupDirectory))
            {
                keep.Add(Path.GetFullPath(protectedPath));
            }
        }

        foreach (DatabaseBackupValidationResult oldBackup in validBackups.Where(item => !keep.Contains(item.FilePath)))
        {
            try
            {
                File.Delete(oldBackup.FilePath);
            }
            catch (Exception ex)
            {
                warnings.Add($"旧备份清理失败：{oldBackup.FileName}；{ex.Message}");
            }
        }

        return warnings;
    }

    private string? ReadProtectedSourceBackupFileName()
    {
        try
        {
            if (!File.Exists(PendingRestoreMarkerPath))
            {
                return null;
            }

            DatabaseRestoreMarker? marker = ReadJson<DatabaseRestoreMarker>(PendingRestoreMarkerPath);
            return marker is null || Path.GetFileName(marker.SourceBackupFileName) != marker.SourceBackupFileName
                ? null
                : marker.SourceBackupFileName;
        }
        catch
        {
            return null;
        }
    }

    private string CreateUniqueBackupPath(DatabaseBackupKind kind)
    {
        DateTimeOffset timestamp = _clock();
        string kindText = DatabaseBackupKindNames.ToStorageValue(kind);
        for (int attempt = 0; attempt < 10_000; attempt++)
        {
            DateTimeOffset candidateTimestamp = timestamp.AddMilliseconds(attempt);
            string fileName = $"cross_etf_terminal_{candidateTimestamp.LocalDateTime:yyyyMMdd_HHmmssfff}_V{ApplicationVersion}_{kindText}.db";
            string path = Path.Combine(BackupDirectory, fileName);
            if (!File.Exists(path) && !File.Exists(path + ".tmp"))
            {
                return path;
            }
        }

        throw new IOException("无法生成唯一备份文件名。");
    }

    private static SqliteConnection OpenReadOnlyConnection(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static SqliteConnection OpenWritableConnection(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void NormalizeBackupJournalMode(SqliteConnection destination)
    {
        using SqliteCommand command = destination.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE;";
        string? result = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "delete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("无法将备份文件规范为独立的单文件SQLite数据库。");
        }
    }

    private static string ReadIntegrityResult(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }

        return rows.Count == 1 ? rows[0] : string.Join(" | ", rows);
    }

    private static string[] ReadMissingRequiredTables(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        using SqliteDataReader reader = command.ExecuteReader();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                existing.Add(reader.GetString(0));
            }
        }

        return RequiredTables.Where(table => !existing.Contains(table)).ToArray();
    }

    private static DatabaseBackupValidationResult InvalidValidation(string filePath, string error)
    {
        string fileName;
        try
        {
            fileName = Path.GetFileName(filePath);
        }
        catch
        {
            fileName = string.Empty;
        }

        return new DatabaseBackupValidationResult(
            false,
            "not checked",
            filePath,
            fileName,
            0,
            DateTimeOffset.MinValue,
            "--",
            null,
            error,
            false);
    }

    private static DateTimeOffset SafeGetLastWriteTime(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTime(path) : DateTimeOffset.MinValue;
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static long SafeGetLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best effort; callers still report the primary operation error.
        }
    }

    private void CleanupOrphanTemporaryBackupArtifacts()
    {
        foreach (string path in Directory.EnumerateFiles(
                     BackupDirectory,
                     "cross_etf_terminal_*.db.tmp*",
                     SearchOption.TopDirectoryOnly))
        {
            SafeDelete(path);
        }
    }

    private static void CleanupReadOnlyValidationSidecars(string databasePath)
    {
        string walPath = databasePath + "-wal";
        string sharedMemoryPath = databasePath + "-shm";
        if (File.Exists(walPath) && SafeGetLength(walPath) > 0)
        {
            return;
        }

        SafeDelete(walPath);
        SafeDelete(sharedMemoryPath);
    }

    private static bool TryParseBackupFileName(
        string fileName,
        out DateTimeOffset createdAt,
        out string version,
        out DatabaseBackupKind kind)
    {
        createdAt = DateTimeOffset.MinValue;
        version = string.Empty;
        kind = default;
        Match match = BackupFileNameRegex().Match(fileName);
        if (!match.Success
            || !DateTime.TryParseExact(
                match.Groups["timestamp"].Value,
                "yyyyMMdd_HHmmssfff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime localTime)
            || !DatabaseBackupKindNames.TryParse(match.Groups["kind"].Value, out kind))
        {
            return false;
        }

        createdAt = new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Local));
        version = match.Groups["version"].Value;
        return true;
    }

    [GeneratedRegex(
        "^cross_etf_terminal_(?<timestamp>\\d{8}_\\d{9})_V(?<version>\\d+\\.\\d+\\.\\d+)_(?<kind>daily|manual|preupgrade|prerestore)\\.db$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BackupFileNameRegex();

    private sealed class DatabaseBackupBusyException : IOException
    {
        public DatabaseBackupBusyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
