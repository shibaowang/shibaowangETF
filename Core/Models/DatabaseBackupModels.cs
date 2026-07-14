namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public enum DatabaseBackupKind
{
    Daily,
    Manual,
    PreUpgrade,
    PreRestore
}

public static class DatabaseBackupKindNames
{
    public const string Daily = "daily";
    public const string Manual = "manual";
    public const string PreUpgrade = "preupgrade";
    public const string PreRestore = "prerestore";

    public static string ToStorageValue(DatabaseBackupKind kind)
        => kind switch
        {
            DatabaseBackupKind.Daily => Daily,
            DatabaseBackupKind.Manual => Manual,
            DatabaseBackupKind.PreUpgrade => PreUpgrade,
            DatabaseBackupKind.PreRestore => PreRestore,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported database backup kind.")
        };

    public static bool TryParse(string? value, out DatabaseBackupKind kind)
    {
        kind = value?.Trim().ToLowerInvariant() switch
        {
            Daily => DatabaseBackupKind.Daily,
            Manual => DatabaseBackupKind.Manual,
            PreUpgrade => DatabaseBackupKind.PreUpgrade,
            PreRestore => DatabaseBackupKind.PreRestore,
            _ => default
        };

        return value?.Trim().ToLowerInvariant() is Daily or Manual or PreUpgrade or PreRestore;
    }

    public static string ToDisplayText(DatabaseBackupKind? kind)
        => kind switch
        {
            DatabaseBackupKind.Daily => "每日自动",
            DatabaseBackupKind.Manual => "手动",
            DatabaseBackupKind.PreUpgrade => "升级前",
            DatabaseBackupKind.PreRestore => "恢复前",
            _ => "未知"
        };
}

public sealed record DatabaseBackupValidationResult(
    bool IsValid,
    string IntegrityResult,
    string FilePath,
    string FileName,
    long FileSize,
    DateTimeOffset CreatedAt,
    string Version,
    DatabaseBackupKind? BackupKind,
    string? Error,
    bool IsControlledName)
{
    public bool CanRestore => IsValid && IsControlledName;

    public string BackupKindText => DatabaseBackupKindNames.ToDisplayText(BackupKind);

    public string IntegrityText => IsValid ? "有效" : "无效";

    public string CreatedAtText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string FileSizeText => FileSize < 1024 * 1024
        ? $"{FileSize / 1024d:F1} KB"
        : $"{FileSize / 1024d / 1024d:F2} MB";
}

public sealed record DatabaseBackupOperationResult(
    bool Success,
    string Message,
    DatabaseBackupValidationResult? Backup,
    IReadOnlyList<string> Warnings)
{
    public static DatabaseBackupOperationResult Failed(string message)
        => new(false, message, null, Array.Empty<string>());
}

public sealed record DatabaseDailyBackupResult(
    bool Success,
    bool Created,
    string Message,
    DatabaseBackupValidationResult? Backup);

public sealed record DatabaseBackupSummary(
    string DatabasePath,
    string BackupDirectory,
    int ValidBackupCount,
    DateTimeOffset? LatestValidBackupAt,
    bool HasValidAutomaticBackupToday,
    string AutomaticBackupStatus);

public sealed record DatabaseRestoreMarker(
    DateTimeOffset RequestedAt,
    string SourceBackupFileName,
    string SourceBackupVersion,
    string SourceBackupKind,
    string PendingFileName,
    string Sha256,
    string RequestedByVersion);

public sealed record DatabaseRestoreStageResult(
    bool Success,
    string Message,
    DatabaseRestoreMarker? Marker);

public sealed record DatabaseRestoreResult(
    bool Success,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    string SourceBackupFileName,
    string? SafetyBackupFileName,
    string Message,
    bool RollbackAttempted,
    bool RollbackSucceeded,
    bool StartupBlocked);

public sealed record DatabaseStartupPreflightResult(
    bool CanContinue,
    string Message,
    DatabaseRestoreResult? RestoreResult,
    DatabaseBackupValidationResult? PreUpgradeBackup);

public sealed record DatabaseStartupCompletionResult(
    bool VersionRecorded,
    DatabaseDailyBackupResult DailyBackupResult,
    string? WarningMessage);
