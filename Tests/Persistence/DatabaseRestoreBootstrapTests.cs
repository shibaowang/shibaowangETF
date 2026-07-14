using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using TestEnvironment = CrossETF.Terminal.UiShell.Reference.Tests.Persistence.DatabaseBackupServiceTests.TestDatabaseEnvironment;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Persistence;

public sealed class DatabaseRestoreBootstrapTests
{
    [Fact]
    public async Task NoMarker_DoesNotModifyDatabaseOrCreateResult()
    {
        using var environment = TestEnvironment.CreateSeeded();
        string originalHash = DatabaseBackupService.ComputeSha256(environment.DatabasePath);
        DatabaseRestoreBootstrap bootstrap = CreateBootstrap(environment);

        DatabaseRestoreResult? result = await bootstrap.ProcessPendingRestoreAsync();

        Assert.Null(result);
        Assert.Equal(originalHash, DatabaseBackupService.ComputeSha256(environment.DatabasePath));
        Assert.False(File.Exists(environment.Service.RestoreResultPath));
    }

    [Fact]
    public async Task SuccessfulRestore_ReplacesDatabaseWithSelectedBackupAndConsumesRequest()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        AddTrade(environment, "2026-07-14 11:00:00");
        Assert.Equal(2, environment.OpenRepository().ReadTradeLogs().Count);
        Assert.True((await environment.Service.StageRestoreAsync(backup.Backup!.FilePath)).Success);

        DatabaseRestoreResult? result = await CreateBootstrap(environment).ProcessPendingRestoreAsync();

        Assert.NotNull(result);
        Assert.True(result.Success, result.Message);
        Assert.False(result.StartupBlocked);
        Assert.Single(environment.OpenRepository().ReadTradeLogs());
        Assert.False(File.Exists(environment.Service.PendingRestoreMarkerPath));
        Assert.False(File.Exists(environment.Service.PendingRestoreDatabasePath));
        Assert.True(File.Exists(environment.Service.RestoreResultPath));
    }

    [Fact]
    public async Task SuccessfulRestore_CreatesAndRetainsPreRestoreSafetyBackup()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult selected = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        AddTrade(environment, "2026-07-14 11:00:00");
        Assert.True((await environment.Service.StageRestoreAsync(selected.Backup!.FilePath)).Success);

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(
            await CreateBootstrap(environment).ProcessPendingRestoreAsync());

        Assert.True(result.Success);
        Assert.NotNull(result.SafetyBackupFileName);
        DatabaseBackupValidationResult safety = Assert.Single(
            environment.Service.ReadBackupList(),
            item => item.FileName == result.SafetyBackupFileName);
        Assert.Equal(DatabaseBackupKind.PreRestore, safety.BackupKind);
        Assert.Equal(2, ReadTradeLogCount(safety.FilePath));
    }

    [Fact]
    public async Task SuccessfulRestore_PreservesTradeLogIdsTimesActionsAndAmounts()
    {
        using var environment = TestEnvironment.CreateSeeded();
        TradeLogRecord expected = Assert.Single(environment.OpenRepository().ReadTradeLogs());
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        AddTrade(environment, "2026-07-14 11:00:00");
        Assert.True((await environment.Service.StageRestoreAsync(backup.Backup!.FilePath)).Success);

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(
            await CreateBootstrap(environment).ProcessPendingRestoreAsync());
        TradeLogRecord actual = Assert.Single(environment.OpenRepository().ReadTradeLogs());

        Assert.True(result.Success);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Action, actual.Action);
        Assert.Equal(expected.Amount, actual.Amount);
    }

    [Fact]
    public async Task MalformedMarker_DoesNotModifyCurrentDatabaseAndIsQuarantined()
    {
        using var environment = TestEnvironment.CreateSeeded();
        string originalHash = DatabaseBackupService.ComputeSha256(environment.DatabasePath);
        Directory.CreateDirectory(environment.RestoreDirectory);
        File.WriteAllText(environment.Service.PendingRestoreMarkerPath, "{not-json");

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(
            await CreateBootstrap(environment).ProcessPendingRestoreAsync());

        Assert.False(result.Success);
        Assert.False(result.StartupBlocked);
        Assert.Equal(originalHash, DatabaseBackupService.ComputeSha256(environment.DatabasePath));
        Assert.False(File.Exists(environment.Service.PendingRestoreMarkerPath));
        Assert.NotEmpty(Directory.EnumerateFiles(environment.RestoreDirectory, "pending_restore.json.invalid-*"));
    }

    [Fact]
    public async Task MissingPendingDatabase_DoesNotModifyCurrentDatabase()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        Assert.True((await environment.Service.StageRestoreAsync(backup.Backup!.FilePath)).Success);
        File.Delete(environment.Service.PendingRestoreDatabasePath);
        string originalHash = DatabaseBackupService.ComputeSha256(environment.DatabasePath);

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(
            await CreateBootstrap(environment).ProcessPendingRestoreAsync());

        Assert.False(result.Success);
        Assert.Contains("不存在", result.Message, StringComparison.Ordinal);
        Assert.Equal(originalHash, DatabaseBackupService.ComputeSha256(environment.DatabasePath));
    }

    [Fact]
    public async Task HashMismatch_DoesNotModifyCurrentDatabase()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        Assert.True((await environment.Service.StageRestoreAsync(backup.Backup!.FilePath)).Success);
        File.AppendAllText(environment.Service.PendingRestoreDatabasePath, "tampered");
        string originalHash = DatabaseBackupService.ComputeSha256(environment.DatabasePath);

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(
            await CreateBootstrap(environment).ProcessPendingRestoreAsync());

        Assert.False(result.Success);
        Assert.Contains("SHA-256", result.Message, StringComparison.Ordinal);
        Assert.Equal(originalHash, DatabaseBackupService.ComputeSha256(environment.DatabasePath));
    }

    [Fact]
    public async Task PendingIntegrityFailure_DoesNotModifyCurrentDatabase()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        Assert.True((await environment.Service.StageRestoreAsync(backup.Backup!.FilePath)).Success);
        File.WriteAllText(environment.Service.PendingRestoreDatabasePath, "corrupt database");
        DatabaseRestoreMarker marker = DatabaseBackupService.ReadJson<DatabaseRestoreMarker>(environment.Service.PendingRestoreMarkerPath)!;
        marker = marker with { Sha256 = DatabaseBackupService.ComputeSha256(environment.Service.PendingRestoreDatabasePath) };
        DatabaseBackupService.WriteJsonAtomically(environment.Service.PendingRestoreMarkerPath, marker);
        string originalHash = DatabaseBackupService.ComputeSha256(environment.DatabasePath);

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(
            await CreateBootstrap(environment).ProcessPendingRestoreAsync());

        Assert.False(result.Success);
        Assert.Contains("无效", result.Message, StringComparison.Ordinal);
        Assert.Equal(originalHash, DatabaseBackupService.ComputeSha256(environment.DatabasePath));
    }

    [Fact]
    public async Task FutureBackupVersion_IsRejectedWithoutModifyingCurrentDatabase()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        Assert.True((await environment.Service.StageRestoreAsync(backup.Backup!.FilePath)).Success);
        DatabaseRestoreMarker marker = DatabaseBackupService.ReadJson<DatabaseRestoreMarker>(environment.Service.PendingRestoreMarkerPath)!;
        DatabaseBackupService.WriteJsonAtomically(
            environment.Service.PendingRestoreMarkerPath,
            marker with { SourceBackupVersion = "99.0.0" });
        string originalHash = DatabaseBackupService.ComputeSha256(environment.DatabasePath);

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(
            await CreateBootstrap(environment).ProcessPendingRestoreAsync());

        Assert.False(result.Success);
        Assert.Contains("高于", result.Message, StringComparison.Ordinal);
        Assert.Equal(originalHash, DatabaseBackupService.ComputeSha256(environment.DatabasePath));
    }

    [Fact]
    public async Task MarkerCannotSpecifyAbsolutePendingPath()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        Assert.True((await environment.Service.StageRestoreAsync(backup.Backup!.FilePath)).Success);
        DatabaseRestoreMarker marker = DatabaseBackupService.ReadJson<DatabaseRestoreMarker>(environment.Service.PendingRestoreMarkerPath)!;
        DatabaseBackupService.WriteJsonAtomically(
            environment.Service.PendingRestoreMarkerPath,
            marker with { PendingFileName = Path.Combine(environment.RootDirectory, "external.db") });
        string originalHash = DatabaseBackupService.ComputeSha256(environment.DatabasePath);

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(
            await CreateBootstrap(environment).ProcessPendingRestoreAsync());

        Assert.False(result.Success);
        Assert.Contains("不受控", result.Message, StringComparison.Ordinal);
        Assert.Equal(originalHash, DatabaseBackupService.ComputeSha256(environment.DatabasePath));
    }

    [Fact]
    public async Task PostReplacementValidationFailure_RollsBackToPreRestoreDatabase()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult selected = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        AddTrade(environment, "2026-07-14 11:00:00");
        Assert.True((await environment.Service.StageRestoreAsync(selected.Backup!.FilePath)).Success);
        DatabaseRestoreBootstrap bootstrap = CreateBootstrap(
            environment,
            validation => validation with { IsValid = false, Error = "injected post-replacement failure" });

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(await bootstrap.ProcessPendingRestoreAsync());

        Assert.False(result.Success);
        Assert.True(result.RollbackAttempted);
        Assert.True(result.RollbackSucceeded);
        Assert.False(result.StartupBlocked);
        Assert.Equal(2, environment.OpenRepository().ReadTradeLogs().Count);
        Assert.NotNull(result.SafetyBackupFileName);
        Assert.True(File.Exists(Path.Combine(environment.BackupDirectory, result.SafetyBackupFileName!)));
    }

    [Fact]
    public async Task RollbackFailure_BlocksStartupAndKeepsRecoveryEvidence()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult selected = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        AddTrade(environment, "2026-07-14 11:00:00");
        Assert.True((await environment.Service.StageRestoreAsync(selected.Backup!.FilePath)).Success);
        DatabaseRestoreBootstrap bootstrap = CreateBootstrap(
            environment,
            validation => validation with { IsValid = false, Error = "injected post-replacement failure" },
            () => throw new IOException("injected rollback failure"));

        DatabaseRestoreResult result = Assert.IsType<DatabaseRestoreResult>(await bootstrap.ProcessPendingRestoreAsync());

        Assert.False(result.Success);
        Assert.True(result.RollbackAttempted);
        Assert.False(result.RollbackSucceeded);
        Assert.True(result.StartupBlocked);
        Assert.True(File.Exists(environment.Service.PendingRestoreMarkerPath));
        Assert.True(File.Exists(environment.Service.PendingRestoreDatabasePath));
        Assert.NotEmpty(Directory.EnumerateFiles(environment.RestoreDirectory));
    }

    [Fact]
    public async Task RestoreResult_IsReadOnceAndDeletedAfterAcknowledgement()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseBackupOperationResult backup = await environment.Service.CreateBackupAsync(DatabaseBackupKind.Manual);
        Assert.True((await environment.Service.StageRestoreAsync(backup.Backup!.FilePath)).Success);
        DatabaseRestoreBootstrap bootstrap = CreateBootstrap(environment);
        Assert.NotNull(await bootstrap.ProcessPendingRestoreAsync());

        DatabaseRestoreResult? pending = bootstrap.ReadPendingResult();
        bootstrap.AcknowledgePendingResult();

        Assert.NotNull(pending);
        Assert.False(File.Exists(environment.Service.RestoreResultPath));
        Assert.Null(bootstrap.ReadPendingResult());
    }

    [Fact]
    public void StartupPreflight_MissingDatabaseSkipsEmptyPreUpgradeBackup()
    {
        using var environment = TestEnvironment.CreateEmpty();
        DatabaseStartupCoordinator coordinator = CreateCoordinator(environment);

        DatabaseStartupPreflightResult result = coordinator.RunPreInitialize();

        Assert.True(result.CanContinue);
        Assert.Null(result.PreUpgradeBackup);
        Assert.False(Directory.Exists(environment.BackupDirectory)
                     && Directory.EnumerateFiles(environment.BackupDirectory, "*.db").Any());
    }

    [Fact]
    public void StartupPreflight_MissingVersionKeyCreatesPreUpgradeBackupBeforeInitialization()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseStartupCoordinator coordinator = CreateCoordinator(environment);

        DatabaseStartupPreflightResult result = coordinator.RunPreInitialize();

        Assert.True(result.CanContinue, result.Message);
        Assert.NotNull(result.PreUpgradeBackup);
        Assert.Equal(DatabaseBackupKind.PreUpgrade, result.PreUpgradeBackup.BackupKind);
        Assert.Null(DatabaseStartupCoordinator.ReadVersionSettingWithoutInitialization(environment.DatabasePath));
    }

    [Fact]
    public void StartupPreflight_DifferentVersionCreatesPreUpgradeBackup()
    {
        using var environment = TestEnvironment.CreateSeeded();
        environment.OpenRepository().SaveAppSetting(DatabaseBackupService.LastSuccessfulVersionSettingKey, "8.2.1");

        DatabaseStartupPreflightResult result = CreateCoordinator(environment).RunPreInitialize();

        Assert.True(result.CanContinue, result.Message);
        Assert.Equal(DatabaseBackupKind.PreUpgrade, result.PreUpgradeBackup!.BackupKind);
    }

    [Fact]
    public void StartupPreflight_SameVersionDoesNotCreateDuplicatePreUpgradeBackup()
    {
        using var environment = TestEnvironment.CreateSeeded();
        environment.OpenRepository().SaveAppSetting(DatabaseBackupService.LastSuccessfulVersionSettingKey, "8.3.0");

        DatabaseStartupPreflightResult result = CreateCoordinator(environment).RunPreInitialize();

        Assert.True(result.CanContinue);
        Assert.Null(result.PreUpgradeBackup);
        Assert.False(Directory.Exists(environment.BackupDirectory)
                     && Directory.EnumerateFiles(environment.BackupDirectory, "*.db").Any());
    }

    [Fact]
    public void StartupPreflight_PreUpgradeValidationFailureBlocksInitialization()
    {
        using var environment = TestEnvironment.CreateEmpty();
        Directory.CreateDirectory(environment.RootDirectory);
        using (SqliteConnection connection = OpenWritable(environment.DatabasePath))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE app_settings(key TEXT PRIMARY KEY, value TEXT, updated_at TEXT); INSERT INTO app_settings VALUES('database.last_successful_version', '8.2.1', '2026-07-14');";
            command.ExecuteNonQuery();
        }

        DatabaseStartupPreflightResult result = CreateCoordinator(environment).RunPreInitialize();

        Assert.False(result.CanContinue);
        Assert.Contains("阻止", result.Message, StringComparison.Ordinal);
        Assert.Null(result.PreUpgradeBackup);
    }

    [Fact]
    public void StartupCompletion_RecordsVersionOnlyAfterRepositoryInitializationAndCreatesDailyBackup()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseStartupCoordinator coordinator = CreateCoordinator(environment);
        LocalDataRepository repository = environment.OpenRepository();

        DatabaseStartupCompletionResult result = coordinator.CompleteAfterDatabaseInitialization(repository);

        Assert.True(result.VersionRecorded);
        Assert.True(result.DailyBackupResult.Success);
        Assert.True(result.DailyBackupResult.Created);
        Assert.Equal("8.3.0", repository.ReadAppSetting(DatabaseBackupService.LastSuccessfulVersionSettingKey));
    }

    [Fact]
    public void StartupCompletion_PreUpgradeAlreadyCreatedSkipsDuplicateDailyBackup()
    {
        using var environment = TestEnvironment.CreateSeeded();
        DatabaseStartupCoordinator coordinator = CreateCoordinator(environment);
        DatabaseStartupPreflightResult preflight = coordinator.RunPreInitialize();
        Assert.NotNull(preflight.PreUpgradeBackup);

        DatabaseStartupCompletionResult completion = coordinator.CompleteAfterDatabaseInitialization(environment.OpenRepository());

        Assert.True(completion.DailyBackupResult.Success);
        Assert.False(completion.DailyBackupResult.Created);
        Assert.Single(environment.Service.ReadBackupList().Where(item => item.IsValid));
    }

    [Fact]
    public void StartupOrder_SourceProcessesRestoreAndPreUpgradeBeforeBaseOnStartup()
    {
        string code = ReadRepositoryFile("App.xaml.cs");
        int preflight = code.IndexOf("RunPreInitialize()", StringComparison.Ordinal);
        int baseStartup = code.IndexOf("base.OnStartup(e)", StringComparison.Ordinal);

        Assert.True(preflight >= 0 && preflight < baseStartup);
        Assert.Contains("if (!preflight.CanContinue)", code, StringComparison.Ordinal);
        Assert.Contains("Shutdown(-1)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupOrder_DailyBackupCompletesBeforeFirstRefreshAndNetworkScheduling()
    {
        string mainWindow = ReadRepositoryFile("MainWindow.xaml.cs");
        int completion = mainWindow.IndexOf("CompleteDatabaseStartup(_repository)", StringComparison.Ordinal);
        int initializeComponent = mainWindow.IndexOf("InitializeComponent()", StringComparison.Ordinal);
        int firstRefresh = mainWindow.IndexOf("RefreshLocalDataAndUi();", StringComparison.Ordinal);

        Assert.True(completion >= 0 && completion < initializeComponent);
        Assert.True(completion < firstRefresh);
    }

    [Fact]
    public void RestoreImplementation_DoesNotInitializeDatabaseBeforeReplacement()
    {
        string code = ReadRepositoryFile("Infrastructure", "Persistence", "DatabaseRestoreBootstrap.cs");

        Assert.DoesNotContain("LocalDatabase.Initialize", code, StringComparison.Ordinal);
        Assert.Contains("File.Replace", code, StringComparison.Ordinal);
        Assert.Contains("-wal", code, StringComparison.Ordinal);
        Assert.Contains("-shm", code, StringComparison.Ordinal);
        Assert.Contains("Rollback", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreImplementation_AlwaysTargetsConfiguredProductionDatabasePath()
    {
        string code = ReadRepositoryFile("Infrastructure", "Persistence", "DatabaseRestoreBootstrap.cs");

        Assert.Contains("_backupService.DatabasePath", code, StringComparison.Ordinal);
        Assert.DoesNotContain("marker.DatabasePath", code, StringComparison.Ordinal);
        Assert.DoesNotContain("marker.Target", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreResultModel_ContainsSafetyAndRollbackAuditFields()
    {
        DatabaseRestoreResult result = new(
            false,
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            "source.db",
            "safety.db",
            "failed",
            true,
            true,
            false);

        Assert.Equal("source.db", result.SourceBackupFileName);
        Assert.Equal("safety.db", result.SafetyBackupFileName);
        Assert.True(result.RollbackAttempted);
        Assert.True(result.RollbackSucceeded);
        Assert.False(result.StartupBlocked);
    }

    private static DatabaseRestoreBootstrap CreateBootstrap(
        TestEnvironment environment,
        Func<DatabaseBackupValidationResult, DatabaseBackupValidationResult>? postReplacementValidation = null,
        Action? beforeRollback = null)
        => new(
            environment.Service,
            () => environment.Now,
            postReplacementValidation,
            beforeRollback,
            (_, _, _) => { });

    private static DatabaseStartupCoordinator CreateCoordinator(TestEnvironment environment)
    {
        DatabaseRestoreBootstrap bootstrap = CreateBootstrap(environment);
        return new DatabaseStartupCoordinator(environment.Service, bootstrap, (_, _, _) => { });
    }

    private static void AddTrade(TestEnvironment environment, string time)
    {
        environment.OpenRepository().SaveTradeLog(new TradeLogRecord
        {
            Time = time,
            StrategyCode = "159509",
            ActualCode = "159509",
            Action = "卖出",
            Price = 1.30,
            Quantity = 10,
            Amount = 13,
            Source = "场内ETF"
        });
    }

    private static int ReadTradeLogCount(string databasePath)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM trade_log;";
        return Convert.ToInt32(command.ExecuteScalar());
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
}
