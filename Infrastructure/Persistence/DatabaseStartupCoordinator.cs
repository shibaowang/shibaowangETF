using CrossETF.Terminal.UiShell.Reference.Core.Models;
using System.IO;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

public sealed class DatabaseStartupCoordinator
{
    private readonly DatabaseBackupService _backupService;
    private readonly DatabaseRestoreBootstrap _restoreBootstrap;
    private readonly Action<string, string, Exception?> _startupLogger;

    public DatabaseStartupCoordinator(
        DatabaseBackupService backupService,
        DatabaseRestoreBootstrap? restoreBootstrap = null)
        : this(backupService, restoreBootstrap, DatabaseRestoreBootstrap.WriteStartupFileLog)
    {
    }

    internal DatabaseStartupCoordinator(
        DatabaseBackupService backupService,
        DatabaseRestoreBootstrap? restoreBootstrap,
        Action<string, string, Exception?> startupLogger)
    {
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _restoreBootstrap = restoreBootstrap ?? new DatabaseRestoreBootstrap(backupService);
        _startupLogger = startupLogger ?? throw new ArgumentNullException(nameof(startupLogger));
    }

    public DatabaseRestoreResult? PendingRestoreResult { get; private set; }

    public DatabaseStartupPreflightResult RunPreInitialize()
    {
        DatabaseRestoreResult? restoreResult;
        try
        {
            restoreResult = _restoreBootstrap.ProcessPendingRestoreAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            string message = "启动前恢复检查失败，当前数据库未初始化：" + ex.Message;
            _startupLogger("CRITICAL", message, ex);
            return new DatabaseStartupPreflightResult(false, message, null, null);
        }

        PendingRestoreResult = restoreResult ?? _restoreBootstrap.ReadPendingResult();
        if (restoreResult?.StartupBlocked == true)
        {
            return new DatabaseStartupPreflightResult(false, restoreResult.Message, restoreResult, null);
        }

        if (!File.Exists(_backupService.DatabasePath))
        {
            return new DatabaseStartupPreflightResult(
                true,
                "首次安装：正式数据库尚不存在，无需创建升级前空备份。",
                restoreResult,
                null);
        }

        string? recordedVersion;
        try
        {
            recordedVersion = ReadVersionSettingWithoutInitialization(_backupService.DatabasePath);
        }
        catch (Exception ex)
        {
            string message = "升级前版本检查失败，已阻止数据库初始化：" + ex.Message;
            _startupLogger("ERROR", message, ex);
            return new DatabaseStartupPreflightResult(false, message, restoreResult, null);
        }

        if (string.Equals(
                DatabaseBackupService.NormalizeVersion(recordedVersion),
                _backupService.ApplicationVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return new DatabaseStartupPreflightResult(true, "数据库版本与当前程序一致。", restoreResult, null);
        }

        DatabaseBackupOperationResult backupResult = _backupService
            .CreateBackupAsync(DatabaseBackupKind.PreUpgrade)
            .GetAwaiter()
            .GetResult();
        if (!backupResult.Success || backupResult.Backup is null)
        {
            string message = "升级前安全备份失败，已阻止数据库初始化：" + backupResult.Message;
            _startupLogger("ERROR", message, null);
            return new DatabaseStartupPreflightResult(false, message, restoreResult, null);
        }

        return new DatabaseStartupPreflightResult(
            true,
            "升级前安全备份已完成。",
            restoreResult,
            backupResult.Backup);
    }

    public DatabaseStartupCompletionResult CompleteAfterDatabaseInitialization(LocalDataRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        bool versionRecorded = false;
        string? warning = null;

        try
        {
            repository.SaveAppSetting(
                DatabaseBackupService.LastSuccessfulVersionSettingKey,
                _backupService.ApplicationVersion);
            versionRecorded = true;
        }
        catch (Exception ex)
        {
            warning = "数据库版本状态写入失败：" + ex.Message;
            TryWriteRuntimeLog(repository, "ERROR", warning, ex);
        }

        DatabaseDailyBackupResult dailyResult = _backupService
            .EnsureDailyBackupAsync()
            .GetAwaiter()
            .GetResult();
        if (!dailyResult.Success)
        {
            string dailyWarning = dailyResult.Message;
            warning = string.IsNullOrWhiteSpace(warning)
                ? dailyWarning
                : warning + Environment.NewLine + dailyWarning;
            TryWriteRuntimeLog(repository, "WARN", dailyWarning, null);
        }

        if (PendingRestoreResult is { Success: false } restoreFailure)
        {
            TryWriteRuntimeLog(repository, "ERROR", restoreFailure.Message, null);
        }

        return new DatabaseStartupCompletionResult(versionRecorded, dailyResult, warning);
    }

    public void AcknowledgeRestoreResult()
    {
        _restoreBootstrap.AcknowledgePendingResult();
        PendingRestoreResult = null;
    }

    public static string? ReadVersionSettingWithoutInitialization(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using (SqliteCommand tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'app_settings';";
            long tableCount = Convert.ToInt64(tableCommand.ExecuteScalar());
            if (tableCount == 0)
            {
                return null;
            }
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", DatabaseBackupService.LastSuccessfulVersionSettingKey);
        object? value = command.ExecuteScalar();
        return value is null || value == DBNull.Value ? null : Convert.ToString(value);
    }

    private void TryWriteRuntimeLog(
        LocalDataRepository repository,
        string level,
        string message,
        Exception? exception)
    {
        try
        {
            repository.WriteRuntimeLog(level, "DatabaseBackupRestore", message, exception?.ToString());
        }
        catch
        {
            _startupLogger(level, message, exception);
        }
    }
}
