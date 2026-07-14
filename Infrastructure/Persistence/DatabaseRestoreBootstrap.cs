using System.Globalization;
using System.IO;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

public sealed class DatabaseRestoreBootstrap
{
    private readonly DatabaseBackupService _backupService;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<DatabaseBackupValidationResult, DatabaseBackupValidationResult>? _postReplacementValidation;
    private readonly Action? _beforeRollback;
    private readonly Action<string, string, Exception?> _startupLogger;

    public DatabaseRestoreBootstrap(DatabaseBackupService backupService, Func<DateTimeOffset>? clock = null)
        : this(backupService, clock, null, null, WriteStartupFileLog)
    {
    }

    internal DatabaseRestoreBootstrap(
        DatabaseBackupService backupService,
        Func<DateTimeOffset>? clock,
        Func<DatabaseBackupValidationResult, DatabaseBackupValidationResult>? postReplacementValidation,
        Action? beforeRollback,
        Action<string, string, Exception?> startupLogger)
    {
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _clock = clock ?? (() => DateTimeOffset.Now);
        _postReplacementValidation = postReplacementValidation;
        _beforeRollback = beforeRollback;
        _startupLogger = startupLogger ?? throw new ArgumentNullException(nameof(startupLogger));
    }

    public async Task<DatabaseRestoreResult?> ProcessPendingRestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_backupService.PendingRestoreMarkerPath))
        {
            return null;
        }

        try
        {
            return await _backupService.ExecuteExclusiveAsync(
                () => Task.Run(ProcessPendingRestoreCore, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DatabaseRestoreResult result = CreateResult(
                success: false,
                requestedAt: _clock(),
                sourceBackupFileName: string.Empty,
                safetyBackupFileName: null,
                message: "恢复启动处理失败，原数据库未被替换；请关闭其它实例后重试：" + ex.Message,
                rollbackAttempted: false,
                rollbackSucceeded: false,
                startupBlocked: true);
            TryWriteRestoreResult(result);
            _startupLogger("ERROR", result.Message, ex);
            return result;
        }
    }

    public DatabaseRestoreResult? ReadPendingResult()
    {
        try
        {
            return File.Exists(_backupService.RestoreResultPath)
                ? DatabaseBackupService.ReadJson<DatabaseRestoreResult>(_backupService.RestoreResultPath)
                : null;
        }
        catch (Exception ex)
        {
            _startupLogger("WARN", "恢复结果文件读取失败。", ex);
            return null;
        }
    }

    public void AcknowledgePendingResult()
    {
        try
        {
            if (File.Exists(_backupService.RestoreResultPath))
            {
                File.Delete(_backupService.RestoreResultPath);
            }
        }
        catch (Exception ex)
        {
            _startupLogger("WARN", "恢复结果文件删除失败。", ex);
        }
    }

    private DatabaseRestoreResult ProcessPendingRestoreCore()
    {
        DatabaseRestoreMarker? marker;
        try
        {
            marker = DatabaseBackupService.ReadJson<DatabaseRestoreMarker>(_backupService.PendingRestoreMarkerPath);
        }
        catch (Exception ex)
        {
            return RejectInvalidRequest(null, "恢复请求标记无法解析：" + ex.Message, ex);
        }

        if (marker is null)
        {
            return RejectInvalidRequest(null, "恢复请求标记为空。", null);
        }

        string sourceBackupFileName = marker.SourceBackupFileName ?? string.Empty;
        if (Path.GetFileName(sourceBackupFileName) != sourceBackupFileName
            || Path.GetFileName(marker.PendingFileName) != marker.PendingFileName
            || !string.Equals(
                marker.PendingFileName,
                DatabaseBackupService.PendingRestoreDatabaseFileName,
                StringComparison.Ordinal)
            || !DatabaseBackupKindNames.TryParse(marker.SourceBackupKind, out _))
        {
            return RejectInvalidRequest(marker, "恢复请求包含不受控的文件路径。", null);
        }

        if (!File.Exists(_backupService.PendingRestoreDatabasePath))
        {
            return RejectInvalidRequest(marker, "恢复暂存数据库不存在。", null);
        }

        if (!IsSupportedVersion(marker.SourceBackupVersion, _backupService.ApplicationVersion))
        {
            return RejectInvalidRequest(marker, "备份版本高于当前程序支持版本，已拒绝恢复。", null);
        }

        string pendingHash;
        try
        {
            pendingHash = DatabaseBackupService.ComputeSha256(_backupService.PendingRestoreDatabasePath);
        }
        catch (Exception ex)
        {
            return RejectInvalidRequest(marker, "无法计算恢复暂存数据库SHA-256：" + ex.Message, ex);
        }

        if (!string.Equals(marker.Sha256, pendingHash, StringComparison.OrdinalIgnoreCase))
        {
            return RejectInvalidRequest(marker, "恢复暂存数据库SHA-256与请求标记不一致。", null);
        }

        DatabaseBackupValidationResult pendingValidation = _backupService.ValidateDatabaseFile(
            _backupService.PendingRestoreDatabasePath,
            _backupService.RestoreDirectory,
            requireControlledName: false);
        if (!pendingValidation.IsValid)
        {
            return RejectInvalidRequest(
                marker,
                "恢复暂存数据库无效：" + (pendingValidation.Error ?? pendingValidation.IntegrityResult),
                null);
        }

        DatabaseBackupValidationResult? safetyBackup = null;
        if (File.Exists(_backupService.DatabasePath))
        {
            DatabaseBackupOperationResult safetyResult = _backupService.CreateBackupUnderAcquiredLock(DatabaseBackupKind.PreRestore);
            if (!safetyResult.Success || safetyResult.Backup is null)
            {
                DatabaseRestoreResult safetyFailure = CreateResult(
                    false,
                    marker.RequestedAt,
                    sourceBackupFileName,
                    null,
                    "恢复前安全备份失败，当前数据库保持不变：" + safetyResult.Message,
                    false,
                    false,
                    false);
                TryWriteRestoreResult(safetyFailure);
                _startupLogger("ERROR", safetyFailure.Message, null);
                ConsumeRestoreRequest(keepEvidence: true);
                return safetyFailure;
            }

            safetyBackup = safetyResult.Backup;
        }

        return ReplaceDatabase(marker, safetyBackup);
    }

    private DatabaseRestoreResult ReplaceDatabase(
        DatabaseRestoreMarker marker,
        DatabaseBackupValidationResult? safetyBackup)
    {
        string databaseDirectory = Path.GetDirectoryName(_backupService.DatabasePath)
            ?? throw new InvalidOperationException("无法解析正式数据库目录。");
        Directory.CreateDirectory(databaseDirectory);
        Directory.CreateDirectory(_backupService.RestoreDirectory);

        string operationId = Guid.NewGuid().ToString("N");
        string candidatePath = Path.Combine(
            databaseDirectory,
            Path.GetFileName(_backupService.DatabasePath) + ".restore-" + operationId + ".tmp");
        string originalMainEvidencePath = Path.Combine(
            _backupService.RestoreDirectory,
            "original_database_" + operationId + ".db");
        string failedRestoredEvidencePath = Path.Combine(
            _backupService.RestoreDirectory,
            "failed_restored_database_" + operationId + ".db");
        var movedSidecars = new List<(string Original, string Evidence)>();
        bool databaseReplaced = false;

        try
        {
            DatabaseBackupService.CopyFileDurably(_backupService.PendingRestoreDatabasePath, candidatePath);
            DatabaseBackupValidationResult candidateValidation = _backupService.ValidateDatabaseFile(
                candidatePath,
                databaseDirectory,
                requireControlledName: false);
            if (!candidateValidation.IsValid)
            {
                throw new InvalidDataException(
                    "恢复候选数据库校验失败：" + (candidateValidation.Error ?? candidateValidation.IntegrityResult));
            }

            MoveSidecarToEvidence(_backupService.DatabasePath + "-wal", operationId, movedSidecars);
            MoveSidecarToEvidence(_backupService.DatabasePath + "-shm", operationId, movedSidecars);

            if (File.Exists(_backupService.DatabasePath))
            {
                File.Replace(candidatePath, _backupService.DatabasePath, originalMainEvidencePath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(candidatePath, _backupService.DatabasePath, overwrite: false);
            }

            databaseReplaced = true;
            DatabaseBackupValidationResult restoredValidation = _backupService.ValidateDatabaseFile(
                _backupService.DatabasePath,
                databaseDirectory,
                requireControlledName: false);
            if (_postReplacementValidation is not null)
            {
                restoredValidation = _postReplacementValidation(restoredValidation);
            }

            if (!restoredValidation.IsValid)
            {
                throw new InvalidDataException(
                    "恢复后数据库校验失败：" + (restoredValidation.Error ?? restoredValidation.IntegrityResult));
            }

            DatabaseRestoreResult success = CreateResult(
                true,
                marker.RequestedAt,
                marker.SourceBackupFileName,
                safetyBackup?.FileName,
                "数据库恢复成功，恢复前安全备份已保留。",
                false,
                false,
                false);
            ConsumeRestoreRequest(keepEvidence: false);
            SafeDelete(originalMainEvidencePath);
            foreach ((_, string evidence) in movedSidecars)
            {
                SafeDelete(evidence);
            }

            TryWriteRestoreResult(success);
            return success;
        }
        catch (Exception ex)
        {
            SafeDelete(candidatePath);
            if (!databaseReplaced)
            {
                RestoreMovedSidecars(movedSidecars);
                DatabaseRestoreResult unchanged = CreateResult(
                    false,
                    marker.RequestedAt,
                    marker.SourceBackupFileName,
                    safetyBackup?.FileName,
                    "恢复失败，当前数据库未被替换：" + ex.Message,
                    false,
                    false,
                    false);
                ConsumeRestoreRequest(keepEvidence: true);
                TryWriteRestoreResult(unchanged);
                _startupLogger("ERROR", unchanged.Message, ex);
                return unchanged;
            }

            return RollBackAfterReplacementFailure(
                marker,
                safetyBackup,
                failedRestoredEvidencePath,
                originalMainEvidencePath,
                movedSidecars,
                ex);
        }
    }

    private DatabaseRestoreResult RollBackAfterReplacementFailure(
        DatabaseRestoreMarker marker,
        DatabaseBackupValidationResult? safetyBackup,
        string failedRestoredEvidencePath,
        string originalMainEvidencePath,
        IReadOnlyList<(string Original, string Evidence)> movedSidecars,
        Exception restoreException)
    {
        if (safetyBackup is null || !File.Exists(safetyBackup.FilePath))
        {
            DatabaseRestoreResult blocked = CreateResult(
                false,
                marker.RequestedAt,
                marker.SourceBackupFileName,
                safetyBackup?.FileName,
                "恢复后校验失败且没有可用的恢复前安全备份，启动已阻断。",
                true,
                false,
                true);
            TryWriteRestoreResult(blocked);
            _startupLogger("CRITICAL", blocked.Message, restoreException);
            return blocked;
        }

        string databaseDirectory = Path.GetDirectoryName(_backupService.DatabasePath)!;
        string rollbackCandidate = Path.Combine(
            databaseDirectory,
            Path.GetFileName(_backupService.DatabasePath) + ".rollback-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            _beforeRollback?.Invoke();
            SafeDelete(_backupService.DatabasePath + "-wal");
            SafeDelete(_backupService.DatabasePath + "-shm");
            DatabaseBackupService.CopyFileDurably(safetyBackup.FilePath, rollbackCandidate);
            DatabaseBackupValidationResult candidateValidation = _backupService.ValidateDatabaseFile(
                rollbackCandidate,
                databaseDirectory,
                requireControlledName: false);
            if (!candidateValidation.IsValid)
            {
                throw new InvalidDataException("回滚候选数据库校验失败。" + candidateValidation.Error);
            }

            if (File.Exists(_backupService.DatabasePath))
            {
                File.Replace(
                    rollbackCandidate,
                    _backupService.DatabasePath,
                    failedRestoredEvidencePath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(rollbackCandidate, _backupService.DatabasePath, overwrite: false);
            }

            DatabaseBackupValidationResult rollbackValidation = _backupService.ValidateDatabaseFile(
                _backupService.DatabasePath,
                databaseDirectory,
                requireControlledName: false);
            if (!rollbackValidation.IsValid)
            {
                throw new InvalidDataException(
                    "回滚后的数据库校验失败：" + (rollbackValidation.Error ?? rollbackValidation.IntegrityResult));
            }

            DatabaseRestoreResult rolledBack = CreateResult(
                false,
                marker.RequestedAt,
                marker.SourceBackupFileName,
                safetyBackup.FileName,
                "恢复失败，原数据库已安全恢复。",
                true,
                true,
                false);
            ConsumeRestoreRequest(keepEvidence: false);
            SafeDelete(originalMainEvidencePath);
            SafeDelete(failedRestoredEvidencePath);
            foreach ((_, string evidence) in movedSidecars)
            {
                SafeDelete(evidence);
            }

            TryWriteRestoreResult(rolledBack);
            _startupLogger("ERROR", rolledBack.Message, restoreException);
            return rolledBack;
        }
        catch (Exception rollbackException)
        {
            DatabaseRestoreResult blocked = CreateResult(
                false,
                marker.RequestedAt,
                marker.SourceBackupFileName,
                safetyBackup.FileName,
                "恢复失败且原数据库回滚失败，启动已阻断。",
                true,
                false,
                true);
            TryWriteRestoreResult(blocked);
            _startupLogger(
                "CRITICAL",
                blocked.Message + Environment.NewLine + "恢复错误：" + restoreException.Message,
                rollbackException);
            return blocked;
        }
    }

    private DatabaseRestoreResult RejectInvalidRequest(
        DatabaseRestoreMarker? marker,
        string message,
        Exception? exception)
    {
        DatabaseRestoreResult result = CreateResult(
            false,
            marker?.RequestedAt ?? _clock(),
            marker?.SourceBackupFileName ?? string.Empty,
            null,
            message + " 当前数据库保持不变。",
            false,
            false,
            false);
        QuarantineInvalidRequest();
        TryWriteRestoreResult(result);
        _startupLogger("ERROR", result.Message, exception);
        return result;
    }

    private void MoveSidecarToEvidence(
        string sidecarPath,
        string operationId,
        ICollection<(string Original, string Evidence)> movedSidecars)
    {
        if (!File.Exists(sidecarPath))
        {
            return;
        }

        string evidencePath = Path.Combine(
            _backupService.RestoreDirectory,
            "original_" + Path.GetFileName(sidecarPath) + "_" + operationId);
        File.Move(sidecarPath, evidencePath, overwrite: false);
        movedSidecars.Add((sidecarPath, evidencePath));
    }

    private static void RestoreMovedSidecars(IEnumerable<(string Original, string Evidence)> movedSidecars)
    {
        foreach ((string original, string evidence) in movedSidecars.Reverse())
        {
            try
            {
                if (File.Exists(evidence) && !File.Exists(original))
                {
                    File.Move(evidence, original, overwrite: false);
                }
            }
            catch
            {
                // The startup result and file log preserve the primary failure for manual recovery.
            }
        }
    }

    private void ConsumeRestoreRequest(bool keepEvidence)
    {
        if (keepEvidence)
        {
            QuarantineInvalidRequest();
            return;
        }

        SafeDelete(_backupService.PendingRestoreMarkerPath);
        SafeDelete(_backupService.PendingRestoreDatabasePath);
        SafeDelete(_backupService.PendingRestoreMarkerPath + ".tmp");
        SafeDelete(_backupService.PendingRestoreDatabasePath + ".tmp");
    }

    private void QuarantineInvalidRequest()
    {
        string suffix = ".invalid-" + _clock().ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
        MoveToQuarantine(_backupService.PendingRestoreMarkerPath, suffix);
        MoveToQuarantine(_backupService.PendingRestoreDatabasePath, suffix);
    }

    private static void MoveToQuarantine(string sourcePath, string suffix)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return;
            }

            string targetPath = sourcePath + suffix;
            int attempt = 0;
            while (File.Exists(targetPath))
            {
                attempt++;
                targetPath = sourcePath + suffix + "." + attempt.ToString(CultureInfo.InvariantCulture);
            }

            File.Move(sourcePath, targetPath, overwrite: false);
        }
        catch
        {
            // Leave the evidence in place if it cannot be quarantined.
        }
    }

    private void TryWriteRestoreResult(DatabaseRestoreResult result)
    {
        try
        {
            DatabaseBackupService.WriteJsonAtomically(_backupService.RestoreResultPath, result);
        }
        catch (Exception ex)
        {
            _startupLogger("ERROR", "恢复结果文件写入失败。", ex);
        }
    }

    private DatabaseRestoreResult CreateResult(
        bool success,
        DateTimeOffset requestedAt,
        string sourceBackupFileName,
        string? safetyBackupFileName,
        string message,
        bool rollbackAttempted,
        bool rollbackSucceeded,
        bool startupBlocked)
        => new(
            success,
            requestedAt,
            _clock(),
            sourceBackupFileName,
            safetyBackupFileName,
            message,
            rollbackAttempted,
            rollbackSucceeded,
            startupBlocked);

    private static bool IsSupportedVersion(string backupVersion, string currentVersion)
    {
        return Version.TryParse(DatabaseBackupService.NormalizeVersion(backupVersion), out Version? backup)
               && Version.TryParse(DatabaseBackupService.NormalizeVersion(currentVersion), out Version? current)
               && backup <= current;
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
            // Cleanup is best effort. Recovery evidence is never force-deleted.
        }
    }

    internal static void WriteStartupFileLog(string level, string message, Exception? exception)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                LocalDatabase.AppFolderName,
                "logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"startup-{DateTime.Now:yyyyMMdd}.log");
            string entry = string.Join(Environment.NewLine, new[]
            {
                "============================================================",
                $"time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}",
                $"level: {level}",
                "context: DatabaseBackupRestore",
                $"message: {message}",
                exception is null ? string.Empty : exception.ToString(),
                string.Empty
            });
            File.AppendAllText(path, entry);
        }
        catch
        {
            // Startup file logging must never mutate or initialize the database.
        }
    }
}
