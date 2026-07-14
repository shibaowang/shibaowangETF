using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Persistence;

public sealed class DatabaseBackupServiceTests
{
    [Fact]
    public async Task CreateBackup_UsesOnlineBackupAndPreservesCoreData()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();

        DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Backup);
        Assert.True(result.Backup.IsValid);
        Assert.Equal("ok", result.Backup.IntegrityResult);
        Assert.Equal(1, ReadScalar<long>(result.Backup.FilePath, "SELECT COUNT(*) FROM trade_log;"));
        Assert.Equal("159509", ReadScalar<string>(result.Backup.FilePath, "SELECT code FROM strategy_config LIMIT 1;"));
        Assert.Equal("seed-value", ReadScalar<string>(result.Backup.FilePath, "SELECT value FROM app_settings WHERE key = 'seed-key';"));
    }

    [Fact]
    public async Task CreateBackup_IncludesCommittedWalDataWhileWriterConnectionRemainsOpen()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        using SqliteConnection writer = new LocalDatabase(environment.DatabasePath).OpenConnection();
        using (SqliteTransaction transaction = writer.BeginTransaction())
        using (SqliteCommand command = writer.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO app_settings(key, value, updated_at) VALUES('wal-key', 'committed-in-wal', '2026-07-14 14:35:01');";
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        Assert.True(result.Success, result.Message);
        Assert.Equal("committed-in-wal", ReadScalar<string>(result.Backup!.FilePath, "SELECT value FROM app_settings WHERE key = 'wal-key';"));
    }

    [Fact]
    public void Implementation_DoesNotCopyTheActiveDatabaseWithFileCopy()
    {
        string code = ReadRepositoryFile("Infrastructure", "Persistence", "DatabaseBackupService.cs");

        Assert.Contains("source.BackupDatabase(destination)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Copy(DatabasePath", code, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Copy(activeDatabasePath", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBackup_PromotesValidatedTemporaryFileAndLeavesNoTmpFile()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();

        DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        Assert.True(result.Success, result.Message);
        Assert.EndsWith(".db", result.Backup!.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(result.Backup.FilePath + ".tmp"));
        Assert.Empty(Directory.EnumerateFiles(environment.BackupDirectory, "*.tmp"));
    }

    [Fact]
    public void ValidateBackup_CorruptDatabaseIsInvalid()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        string path = Path.Combine(environment.BackupDirectory, "cross_etf_terminal_20260714_143501123_V8.3.0_manual.db");
        Directory.CreateDirectory(environment.BackupDirectory);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });

        DatabaseBackupValidationResult result = environment.Service.ValidateBackup(path);

        Assert.False(result.IsValid);
        Assert.False(result.CanRestore);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ValidateBackup_MissingRequiredTablesIsInvalid()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        string path = Path.Combine(environment.BackupDirectory, "cross_etf_terminal_20260714_143501123_V8.3.0_manual.db");
        Directory.CreateDirectory(environment.BackupDirectory);
        using (SqliteConnection connection = OpenWritable(path))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE strategy_config(id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        DatabaseBackupValidationResult result = environment.Service.ValidateBackup(path);

        Assert.False(result.IsValid);
        Assert.Contains("trade_log", result.Error, StringComparison.Ordinal);
        Assert.Contains("app_settings", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DatabaseBackupKind.Daily, "daily")]
    [InlineData(DatabaseBackupKind.Manual, "manual")]
    [InlineData(DatabaseBackupKind.PreUpgrade, "preupgrade")]
    [InlineData(DatabaseBackupKind.PreRestore, "prerestore")]
    public async Task CreateBackup_FileNameContainsTimestampVersionAndAllowedKind(DatabaseBackupKind kind, string kindText)
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();

        DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(kind);

        Assert.True(result.Success, result.Message);
        Assert.Matches(
            $"^cross_etf_terminal_20260714_143501123_V8\\.3\\.0_{kindText}\\.db$",
            result.Backup!.FileName);
        Assert.Equal("8.3.0", result.Backup.Version);
        Assert.Equal(kind, result.Backup.BackupKind);
    }

    [Fact]
    public async Task CreateBackup_SameMillisecondStillUsesUniqueNamesAndNeverOverwrites()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();

        DatabaseBackupOperationResult first = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        DatabaseBackupOperationResult second = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.NotEqual(first.Backup!.FileName, second.Backup!.FileName);
        Assert.True(File.Exists(first.Backup.FilePath));
        Assert.True(File.Exists(second.Backup.FilePath));
    }

    [Fact]
    public async Task CreateBackup_NeverWritesOutsideControlledBackupDirectory()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();

        DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            Path.GetFullPath(environment.BackupDirectory),
            Path.GetDirectoryName(Path.GetFullPath(result.Backup!.FilePath)),
            ignoreCase: true);
    }

    [Fact]
    public async Task CreateBackup_MissingDatabaseDoesNotCreateEmptyBackup()
    {
        using var environment = TestDatabaseEnvironment.CreateEmpty();

        DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(DatabaseBackupKind.PreUpgrade);

        Assert.False(result.Success);
        Assert.Empty(Directory.Exists(environment.BackupDirectory)
            ? Directory.EnumerateFiles(environment.BackupDirectory, "*.db")
            : Array.Empty<string>());
    }

    [Fact]
    public async Task EnsureDailyBackup_CreatesOncePerLocalNaturalDay()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();

        DatabaseDailyBackupResult first = await environment.Service.EnsureDailyBackupAsync();
        DatabaseDailyBackupResult second = await environment.Service.EnsureDailyBackupAsync();

        Assert.True(first.Success);
        Assert.True(first.Created);
        Assert.True(second.Success);
        Assert.False(second.Created);
        Assert.Single(environment.Service.ReadBackupList().Where(item => item.IsValid));
    }

    [Fact]
    public async Task EnsureDailyBackup_ExistingPreUpgradeSatisfiesDailyRequirement()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        DatabaseBackupOperationResult preUpgrade = await environment.Service.CreateBackupAsync(DatabaseBackupKind.PreUpgrade);

        DatabaseDailyBackupResult daily = await environment.Service.EnsureDailyBackupAsync();

        Assert.True(preUpgrade.Success);
        Assert.True(daily.Success);
        Assert.False(daily.Created);
        Assert.Equal(DatabaseBackupKind.PreUpgrade, daily.Backup!.BackupKind);
        Assert.Single(environment.Service.ReadBackupList().Where(item => item.IsValid));
    }

    [Fact]
    public async Task EnsureDailyBackup_ManualBackupDoesNotSatisfyAutomaticRequirement()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        Assert.True((await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual)).Success);

        DatabaseDailyBackupResult daily = await environment.Service.EnsureDailyBackupAsync();

        Assert.True(daily.Success);
        Assert.True(daily.Created);
        Assert.Equal(2, environment.Service.ReadBackupList().Count(item => item.IsValid));
    }

    [Fact]
    public async Task EnsureDailyBackup_NextNaturalDayCreatesAnotherBackup()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        Assert.True((await environment.Service.EnsureDailyBackupAsync()).Created);
        environment.Now = environment.Now.AddDays(1);

        DatabaseDailyBackupResult nextDay = await environment.Service.EnsureDailyBackupAsync();

        Assert.True(nextDay.Created);
        Assert.Equal(2, environment.Service.ReadBackupList().Count(item => item.IsValid));
    }

    [Fact]
    public async Task RetentionPolicy_KeepsNewestThirtyValidBackups()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        for (int index = 0; index < 31; index++)
        {
            DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
            Assert.True(result.Success, result.Message);
            environment.Now = environment.Now.AddSeconds(1);
        }

        DatabaseBackupValidationResult[] backups = environment.Service.ReadBackupList().Where(item => item.IsValid).ToArray();

        Assert.Equal(DatabaseBackupService.MaximumValidBackupCount, backups.Length);
        Assert.DoesNotContain(backups, item => item.CreatedAt.LocalDateTime == new DateTime(2026, 7, 14, 14, 35, 1, 123));
    }

    [Fact]
    public async Task RetentionPolicy_DoesNotDeleteUnknownFilesOrPendingRestoreFiles()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        Directory.CreateDirectory(environment.BackupDirectory);
        Directory.CreateDirectory(environment.RestoreDirectory);
        string unknown = Path.Combine(environment.BackupDirectory, "user-note.db");
        string pending = Path.Combine(environment.RestoreDirectory, DatabaseBackupService.PendingRestoreDatabaseFileName);
        File.WriteAllText(unknown, "not a controlled backup");
        File.WriteAllText(pending, "pending evidence");

        for (int index = 0; index < 31; index++)
        {
            Assert.True((await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual)).Success);
            environment.Now = environment.Now.AddSeconds(1);
        }

        Assert.True(File.Exists(unknown));
        Assert.True(File.Exists(pending));
    }

    [Fact]
    public async Task RetentionPolicy_ProtectsBackupReferencedByPendingRestoreMarker()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        DatabaseBackupOperationResult protectedBackup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        Assert.True(protectedBackup.Success);
        DatabaseRestoreStageResult stage = await environment.Service.StageRestoreAsync(protectedBackup.Backup!.FilePath);
        Assert.True(stage.Success, stage.Message);

        for (int index = 0; index < 31; index++)
        {
            environment.Now = environment.Now.AddSeconds(1);
            Assert.True((await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual)).Success);
        }

        Assert.True(File.Exists(protectedBackup.Backup.FilePath));
    }

    [Fact]
    public async Task RetentionPolicy_DeleteFailureDoesNotFailNewBackup()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        DatabaseBackupOperationResult? oldest = null;
        for (int index = 0; index < 30; index++)
        {
            DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
            oldest ??= result;
            Assert.True(result.Success);
            environment.Now = environment.Now.AddSeconds(1);
        }

        using FileStream heldOpen = new(oldest!.Backup!.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        DatabaseBackupOperationResult newest = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        Assert.True(newest.Success, newest.Message);
        Assert.True(File.Exists(newest.Backup!.FilePath));
        Assert.Contains(newest.Warnings, warning => warning.Contains(oldest.Backup.FileName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CrossProcessLockUnavailable_ReturnsBusyWithoutCreatingBackup()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        Directory.CreateDirectory(environment.BackupDirectory);
        string lockPath = Path.Combine(environment.BackupDirectory, ".backup.lock");
        using FileStream heldLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        Assert.False(result.Success);
        Assert.Contains("另一备份或恢复任务", result.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(environment.BackupDirectory, "*.db"));
    }

    [Fact]
    public async Task OperationFailure_ReleasesProcessAndFileLocks()
    {
        using var environment = TestDatabaseEnvironment.CreateEmpty();
        DatabaseBackupOperationResult failed = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        environment.SeedDatabase();

        DatabaseBackupOperationResult succeeded = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        Assert.False(failed.Success);
        Assert.True(succeeded.Success, succeeded.Message);
    }

    [Fact]
    public async Task SameProcessConcurrentBackups_AreSerializedAndBothRemainValid()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();

        DatabaseBackupOperationResult[] results = await Task.WhenAll(
            environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual),
            environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual));

        Assert.All(results, result => Assert.True(result.Success, result.Message));
        Assert.Equal(2, results.Select(result => result.Backup!.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(environment.Service.ReadBackupList(), item => Assert.True(item.IsValid, item.Error));
    }

    [Fact]
    public async Task StageRestore_ValidControlledBackupWritesPendingThenMarkerWithSha256()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        DatabaseRestoreStageResult result = await environment.Service.StageRestoreAsync(backup.Backup!.FilePath);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(environment.Service.PendingRestoreDatabasePath));
        Assert.True(File.Exists(environment.Service.PendingRestoreMarkerPath));
        Assert.Equal(DatabaseBackupService.ComputeSha256(environment.Service.PendingRestoreDatabasePath), result.Marker!.Sha256);
        Assert.Equal(DatabaseBackupService.PendingRestoreDatabaseFileName, result.Marker.PendingFileName);
        Assert.Equal(backup.Backup.FileName, result.Marker.SourceBackupFileName);
        Assert.False(File.Exists(environment.Service.PendingRestoreDatabasePath + ".tmp"));
        Assert.False(File.Exists(environment.Service.PendingRestoreMarkerPath + ".tmp"));
    }

    [Fact]
    public async Task StageRestore_InvalidBackupDoesNotWritePendingOrMarker()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        Directory.CreateDirectory(environment.BackupDirectory);
        string invalid = Path.Combine(environment.BackupDirectory, "cross_etf_terminal_20260714_143501123_V8.3.0_manual.db");
        File.WriteAllText(invalid, "broken");

        DatabaseRestoreStageResult result = await environment.Service.StageRestoreAsync(invalid);

        Assert.False(result.Success);
        Assert.False(File.Exists(environment.Service.PendingRestoreDatabasePath));
        Assert.False(File.Exists(environment.Service.PendingRestoreMarkerPath));
    }

    [Fact]
    public async Task StageRestore_ExternalPathIsRejectedEvenWhenDatabaseIsValid()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        string external = Path.Combine(environment.RootDirectory, "external.db");
        File.Copy(backup.Backup!.FilePath, external);

        DatabaseRestoreStageResult result = await environment.Service.StageRestoreAsync(external);

        Assert.False(result.Success);
        Assert.Contains("受控目录", result.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(environment.Service.PendingRestoreMarkerPath));
    }

    [Fact]
    public async Task StageRestore_ExistingRequestIsNotOverwritten()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        DatabaseBackupOperationResult firstBackup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        Assert.True((await environment.Service.StageRestoreAsync(firstBackup.Backup!.FilePath)).Success);
        string originalMarker = File.ReadAllText(environment.Service.PendingRestoreMarkerPath);
        environment.Now = environment.Now.AddSeconds(1);
        DatabaseBackupOperationResult secondBackup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        DatabaseRestoreStageResult secondStage = await environment.Service.StageRestoreAsync(secondBackup.Backup!.FilePath);

        Assert.False(secondStage.Success);
        Assert.Equal(originalMarker, File.ReadAllText(environment.Service.PendingRestoreMarkerPath));
    }

    [Fact]
    public async Task StageRestore_OrphanPendingWithoutMarkerIsQuarantinedBeforeNewRequest()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        Directory.CreateDirectory(environment.RestoreDirectory);
        File.WriteAllText(environment.Service.PendingRestoreDatabasePath, "orphan evidence");

        DatabaseRestoreStageResult result = await environment.Service.StageRestoreAsync(backup.Backup!.FilePath);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(environment.Service.PendingRestoreMarkerPath));
        Assert.NotEmpty(Directory.EnumerateFiles(environment.RestoreDirectory, "pending_restore.db.orphan-*"));
    }

    [Fact]
    public async Task ReadBackupList_ShowsInvalidFilesButMarksThemNotRestorable()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        Assert.True((await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual)).Success);
        string invalid = Path.Combine(environment.BackupDirectory, "unknown.db");
        File.WriteAllText(invalid, "invalid");

        IReadOnlyList<DatabaseBackupValidationResult> rows = environment.Service.ReadBackupList();

        Assert.Equal(2, rows.Count);
        Assert.Single(rows, item => item.CanRestore);
        Assert.Single(rows, item => !item.CanRestore);
    }

    [Fact]
    public async Task ReadSummary_ReportsValidCountLatestTimeAndAutomaticStatus()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();
        Assert.True((await environment.Service.CreateBackupAsync(DatabaseBackupKind.Daily)).Success);

        DatabaseBackupSummary summary = environment.Service.ReadSummary();

        Assert.Equal(environment.DatabasePath, summary.DatabasePath);
        Assert.Equal(environment.BackupDirectory, summary.BackupDirectory);
        Assert.Equal(1, summary.ValidBackupCount);
        Assert.NotNull(summary.LatestValidBackupAt);
        Assert.True(summary.HasValidAutomaticBackupToday);
        Assert.Contains("已完成", summary.AutomaticBackupStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void Paths_KeepProductionDatabaseNameAndUseSiblingControlledDirectories()
    {
        string databasePath = new LocalDatabase().DatabasePath;
        string appDirectory = Path.GetDirectoryName(databasePath)!;

        DatabaseBackupService service = DatabaseBackupService.CreateDefault("8.3.0");

        Assert.EndsWith(Path.Combine(LocalDatabase.AppFolderName, LocalDatabase.DatabaseFileName), service.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(appDirectory, "backups"), service.BackupDirectory, ignoreCase: true);
        Assert.Equal(Path.Combine(appDirectory, "restore"), service.RestoreDirectory, ignoreCase: true);
    }

    [Fact]
    public void Validation_DoesNotInitializeOrWriteTheBackupDatabase()
    {
        string code = ReadRepositoryFile("Infrastructure", "Persistence", "DatabaseBackupService.cs");

        Assert.Contains("Mode = SqliteOpenMode.ReadOnly", code, StringComparison.Ordinal);
        Assert.Contains("PRAGMA query_only=ON", code, StringComparison.Ordinal);
        Assert.Contains("PRAGMA integrity_check", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase.Initialize", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteRuntimeLog", code, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupImplementation_DoesNotCheckpointOrChangeTheActiveDatabaseJournalMode()
    {
        string code = ReadRepositoryFile("Infrastructure", "Persistence", "DatabaseBackupService.cs");

        Assert.DoesNotContain("wal_checkpoint", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VACUUM", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DatabasePath + \"-wal\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabasePath + \"-shm\"", code, StringComparison.Ordinal);
        Assert.Contains("NormalizeBackupJournalMode(destination)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeBackupJournalMode(source)", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBackup_ProducesStandaloneDatabaseWithoutWalOrShmSidecars()
    {
        using var environment = TestDatabaseEnvironment.CreateSeeded();

        DatabaseBackupOperationResult result = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);

        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(result.Backup!.FilePath + "-wal"));
        Assert.False(File.Exists(result.Backup.FilePath + "-shm"));
        Assert.False(File.Exists(result.Backup.FilePath + ".tmp-wal"));
        Assert.False(File.Exists(result.Backup.FilePath + ".tmp-shm"));
    }

    [Fact]
    public void RestoreStaging_WritesPendingDatabaseBeforeMarker()
    {
        string code = ReadRepositoryFile("Infrastructure", "Persistence", "DatabaseBackupService.cs");
        int pendingMove = code.IndexOf("File.Move(pendingTemporaryPath, PendingRestoreDatabasePath", StringComparison.Ordinal);
        int markerWrite = code.IndexOf("WriteJsonAtomically(PendingRestoreMarkerPath, marker)", StringComparison.Ordinal);

        Assert.True(pendingMove >= 0 && pendingMove < markerWrite);
    }

    [Fact]
    public void BackupKinds_AcceptOnlyFourControlledValues()
    {
        Assert.True(DatabaseBackupKindNames.TryParse("daily", out _));
        Assert.True(DatabaseBackupKindNames.TryParse("manual", out _));
        Assert.True(DatabaseBackupKindNames.TryParse("preupgrade", out _));
        Assert.True(DatabaseBackupKindNames.TryParse("prerestore", out _));
        Assert.False(DatabaseBackupKindNames.TryParse("external", out _));
        Assert.False(DatabaseBackupKindNames.TryParse("../manual", out _));
    }

    private static T ReadScalar<T>(string databasePath, string sql)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    private static SqliteConnection OpenWritable(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
    }

    internal sealed class TestDatabaseEnvironment : IDisposable
    {
        private TestDatabaseEnvironment(bool seed)
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "cross_etf_backup_tests_" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(RootDirectory, LocalDatabase.DatabaseFileName);
            BackupDirectory = Path.Combine(RootDirectory, DatabaseBackupService.BackupDirectoryName);
            RestoreDirectory = Path.Combine(RootDirectory, DatabaseBackupService.RestoreDirectoryName);
            Now = new DateTimeOffset(2026, 7, 14, 14, 35, 1, 123, TimeSpan.FromHours(8));
            Service = new DatabaseBackupService(DatabasePath, BackupDirectory, RestoreDirectory, "8.3.0", () => Now);
            if (seed)
            {
                SeedDatabase();
            }
        }

        public string RootDirectory { get; }

        public string DatabasePath { get; }

        public string BackupDirectory { get; }

        public string RestoreDirectory { get; }

        public DateTimeOffset Now { get; set; }

        public DatabaseBackupService Service { get; }

        public static TestDatabaseEnvironment CreateSeeded() => new(true);

        public static TestDatabaseEnvironment CreateEmpty() => new(false);

        public void SeedDatabase()
        {
            var repository = new LocalDataRepository(new LocalDatabase(DatabasePath));
            if (repository.ReadStrategyConfigs().Count == 0)
            {
                repository.SaveStrategyConfig(new StrategyConfigRecord
                {
                    Code = "159509",
                    Name = "备份测试ETF",
                    Enabled = true
                });
            }

            if (repository.ReadTradeLogs().Count == 0)
            {
                repository.SaveTradeLog(new TradeLogRecord
                {
                    Time = "2026-07-14 10:00:00",
                    StrategyCode = "159509",
                    ActualCode = "159509",
                    Action = "买入",
                    Price = 1.25,
                    Quantity = 100,
                    Amount = 125,
                    Source = "场内ETF"
                });
            }

            repository.SaveAppSetting("seed-key", "seed-value");
        }

        public LocalDataRepository OpenRepository() => new(new LocalDatabase(DatabasePath));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootDirectory))
                {
                    Directory.Delete(RootDirectory, recursive: true);
                }
            }
            catch
            {
                // A failed test may still hold a SQLite handle; the OS temp directory can reclaim it later.
            }
        }
    }
}
