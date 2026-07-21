using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Diagnostics;

public sealed class MarketDiagnosticsSnapshotServiceTests
{
    [Fact]
    public void BuildSnapshot_ReadsLocalMarketStatusRuntimeLogAndEnvironment()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            SaveStrategy(repository, "159941", "100.NDX100", enabled: true);
            repository.SaveMarketQuote(new MarketQuoteRecord
            {
                Symbol = "159941",
                DisplayName = "Nasdaq ETF",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.622,
                LastClose = 1.614,
                QuoteTime = "2026-07-08 10:30:00",
                ReceivedAt = "2026-07-08 10:30:05"
            });
            repository.SaveMarketSourceStatus(new MarketSourceStatusRecord
            {
                Source = "TENCENT_QT",
                Status = "OK",
                LastSuccessAt = "2026-07-08 10:30:05",
                FailureCount = 0,
                UpdatedAt = "2026-07-08 10:30:05"
            });
            InsertRuntimeLog(repository, "2026-07-08 08:00:00", "ERROR", "TENCENT_QT", "quote error", "timeout");

            var service = new MarketDiagnosticsSnapshotService(
                repository,
                () => new DateTime(2026, 7, 8, 10, 31, 0));

            MarketDiagnosticsSnapshot snapshot = service.BuildSnapshot();

            DiagnosticsMarketRow marketRow = Assert.Single(snapshot.MarketRows);
            Assert.Equal("159941", marketRow.Code);
            Assert.Equal("正常", marketRow.QuoteStatus);
            Assert.Equal("OK", marketRow.SourceStatus);
            Assert.Single(snapshot.RuntimeLogs);
            Assert.Equal(databasePath, snapshot.Environment.DatabasePath);
            Assert.Equal("V8.10.6", snapshot.Environment.AppVersion);
            Assert.Contains("8.10.6", snapshot.Environment.AssemblyInformationalVersion);
            Assert.Equal(1, snapshot.Overview.HistoricalRuntimeLogCount);
            Assert.Equal(0, snapshot.Overview.RecentErrorCount);
            Assert.Equal("正常", snapshot.Overview.OverallStatus);
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void BuildSnapshot_OrphanedQuoteIsNotDisplayedCountedOrDeleted()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveMarketQuote(Quote(
                "600001",
                "ETF",
                "TENCENT_QT",
                "2026-06-15 13:35:24",
                "2026-06-15 13:35:27"));

            MarketDiagnosticsSnapshot snapshot = CreateService(repository).BuildSnapshot();

            Assert.Empty(snapshot.MarketRows);
            Assert.Equal(0, snapshot.Overview.StaleQuoteCount);
            Assert.Equal("正常", snapshot.Overview.OverallStatus);
            MarketQuoteRecord cached = Assert.Single(repository.ReadMarketQuoteCache());
            Assert.Equal("600001", cached.Symbol);

            string serviceCode = ReadRepositoryFile(Path.Combine("Core", "Services", "MarketDiagnosticsSnapshotService.cs"));
            Assert.DoesNotContain("513110", serviceCode, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void BuildSnapshot_EnabledStrategyStaleQuoteStillProducesWarning()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            SaveStrategy(repository, "159941", "100.NDX100", enabled: true);
            repository.SaveMarketQuote(Quote(
                "159941",
                "ETF",
                "TENCENT_QT",
                "2026-07-06 15:00:00",
                "2026-07-06 15:00:05"));

            MarketDiagnosticsSnapshot snapshot = CreateService(repository).BuildSnapshot();

            DiagnosticsMarketRow row = Assert.Single(snapshot.MarketRows);
            Assert.Equal("159941", row.Code);
            Assert.Equal("过期", row.QuoteStatus);
            Assert.Equal(1, snapshot.Overview.StaleQuoteCount);
            Assert.Equal("警告", snapshot.Overview.OverallStatus);
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void BuildSnapshot_EnabledStrategyIndexAndEnabledOtcChannelAreActive()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            SaveStrategy(repository, "159941", "100.SPX", enabled: true);
            repository.SaveOtcChannel(new OtcChannelRecord
            {
                StrategyCode = "159941",
                OtcCode = "270042",
                ClassType = "A类",
                Enabled = true,
                Priority = 1
            });
            repository.SaveMarketQuote(Quote(
                "100.SPX",
                "INDEX",
                "EASTMONEY_PUSH2",
                "2026-07-08 10:30:00",
                "2026-07-08 10:30:05"));
            repository.SaveMarketQuote(Quote(
                "270042",
                "OTC",
                "SINA_FUND",
                "2026-07-08 00:00:00",
                "2026-07-08 10:30:05"));

            MarketDiagnosticsSnapshot snapshot = CreateService(repository).BuildSnapshot();

            Assert.Contains(snapshot.MarketRows, row => row.Code == "100.SPX" && row.MarketType == "INDEX");
            Assert.Contains(snapshot.MarketRows, row => row.Code == "270042" && row.MarketType == "OTC");
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void BuildSnapshot_DisabledStrategyAndItsOtcChannelRemainHistoricalCache()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            SaveStrategy(repository, "600002", "100.SPX", enabled: false);
            repository.SaveOtcChannel(new OtcChannelRecord
            {
                StrategyCode = "600002",
                OtcCode = "000001",
                ClassType = "A类",
                Enabled = true,
                Priority = 1
            });
            repository.SaveMarketQuote(Quote(
                "600002",
                "ETF",
                "TENCENT_QT",
                "2026-06-15 15:00:00",
                "2026-06-15 15:00:05"));
            repository.SaveMarketQuote(Quote(
                "000001",
                "OTC",
                "SINA_FUND",
                "2026-06-15 00:00:00",
                "2026-06-15 20:00:00"));

            MarketDiagnosticsSnapshot snapshot = CreateService(repository).BuildSnapshot();

            Assert.DoesNotContain(snapshot.MarketRows, row => row.Code is "600002" or "000001");
            Assert.Equal(0, snapshot.Overview.StaleQuoteCount);
            Assert.Equal(2, repository.ReadMarketQuoteCache().Count);
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void BuildSnapshot_ExchangePositionActualCodeIsActiveWithoutStrategyConfig()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SavePositionState(new PositionStateRecord
            {
                StrategyCode = "legacy",
                ActualCode = "600003",
                Source = "场内ETF",
                Quantity = 100,
                CostAmount = 100,
                AdjFactor = 1
            });
            repository.SaveMarketQuote(Quote(
                "600003",
                "ETF",
                "TENCENT_QT",
                "2026-07-08 10:30:00",
                "2026-07-08 10:30:05"));

            MarketDiagnosticsSnapshot snapshot = CreateService(repository).BuildSnapshot();

            DiagnosticsMarketRow row = Assert.Single(snapshot.MarketRows);
            Assert.Equal("600003", row.Code);
            Assert.Equal("正常", row.QuoteStatus);
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void BuildSnapshot_RecentWarningProducesWarningWithoutBecomingPermanentError()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveMarketSourceStatus(new MarketSourceStatusRecord
            {
                Source = "TENCENT_QT",
                Status = "OK",
                LastSuccessAt = "2026-07-08 10:30:05",
                FailureCount = 0,
                UpdatedAt = "2026-07-08 10:30:05"
            });
            InsertRuntimeLog(repository, "2026-07-08 10:20:00", "WARN", "TENCENT_QT", "rate limited", null);

            var service = new MarketDiagnosticsSnapshotService(
                repository,
                () => new DateTime(2026, 7, 8, 10, 31, 0),
                () => new DateTime(2026, 7, 8, 9, 0, 0));

            MarketDiagnosticsSnapshot snapshot = service.BuildSnapshot();

            Assert.Equal("警告", snapshot.Overview.OverallStatus);
            Assert.Equal(0, snapshot.Overview.RecentErrorCount);
            Assert.Equal(1, snapshot.Overview.RecentWarningCount);
            Assert.Equal(0, snapshot.Overview.ProcessErrorCount);
            Assert.Equal(1, snapshot.Overview.HistoricalRuntimeLogCount);
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void BuildSnapshot_CurrentSourceErrorProducesAbnormalStatus()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveMarketSourceStatus(new MarketSourceStatusRecord
            {
                Source = "TENCENT_QT",
                Status = "ERROR",
                LastFailureAt = "2026-07-08 10:30:05",
                FailureCount = 1,
                LastError = "timeout",
                UpdatedAt = "2026-07-08 10:30:05"
            });

            var service = new MarketDiagnosticsSnapshotService(
                repository,
                () => new DateTime(2026, 7, 8, 10, 31, 0),
                () => new DateTime(2026, 7, 8, 9, 0, 0));

            MarketDiagnosticsSnapshot snapshot = service.BuildSnapshot();

            Assert.Equal("异常", snapshot.Overview.OverallStatus);
            Assert.Equal(0, snapshot.Overview.NormalSourceCount);
            Assert.Equal(1, snapshot.Overview.AbnormalSourceCount);
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void BuildSnapshot_UsesSamePnlEvaluationTotalAsMainWindowPath()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveMarketQuote(new MarketQuoteRecord
            {
                Symbol = "159941",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.622,
                LastClose = 1.614,
                QuoteTime = "2026-07-08 10:30:00",
                ReceivedAt = "2026-07-08 10:30:05"
            });
            var replayResult = new AccountReplayResult
            {
                Account = new AccountReplayStateRecord
                {
                    CalculatedAt = "2026-07-08 10:30:10",
                    ReplayStatus = "正常",
                    MarketValueComplete = true
                }
            };
            replayResult.Positions.Add(new PositionReplayStateRecord
            {
                CalculatedAt = "2026-07-08 10:30:10",
                StrategyCode = "159941",
                ActualCode = "159941",
                Source = "\u573a\u5185ETF",
                Quantity = 3900,
                CostAmount = 5800,
                AverageCost = 1.48,
                AdjFactor = 1,
                DailyPnl = null,
                RealizedPnl = 0,
                QuoteStatus = "正常"
            });
            repository.SaveAccountReplayResult(replayResult);

            var service = new MarketDiagnosticsSnapshotService(
                repository,
                () => new DateTime(2026, 7, 8, 10, 31, 0));

            MarketDiagnosticsSnapshot snapshot = service.BuildSnapshot();

            Assert.Equal("一致", snapshot.PnlSummary.ConsistencyStatus);
            Assert.Equal(31.20, snapshot.PnlSummary.DiagnosticsIncludedTotal!.Value, 2);
            Assert.Equal(snapshot.PnlSummary.MainWindowPathTotal, snapshot.PnlSummary.DiagnosticsIncludedTotal);
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void BuildSnapshot_MergesReplayAndOtcStateForTheSameBusinessEvent()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveMarketQuote(new MarketQuoteRecord
            {
                Symbol = "017091",
                DisplayName = "OTC replacement",
                MarketType = "OTC",
                Source = "SINA_FUND",
                Price = 2.8409,
                LastClose = 2.8755,
                QuoteTime = "2026-07-02",
                ReceivedAt = "2026-07-02 20:00:00"
            });
            var replayResult = new AccountReplayResult
            {
                Account = new AccountReplayStateRecord
                {
                    CalculatedAt = "2026-07-02 20:00:10",
                    ReplayStatus = "正常",
                    MarketValueComplete = true
                }
            };
            replayResult.Positions.Add(new PositionReplayStateRecord
            {
                CalculatedAt = "2026-07-02 20:00:10",
                StrategyCode = "159941",
                ActualCode = "017091",
                Source = "场外替代",
                Quantity = 100,
                DailyPnl = 269.12,
                QuoteStatus = "正常"
            });
            replayResult.OtcPositions.Add(new OtcPositionReplayStateRecord
            {
                CalculatedAt = "2026-07-02 20:00:10",
                StrategyCode = "159941",
                ActualCode = "017091",
                Quantity = 100,
                DailyPnl = 269.12
            });
            repository.SaveAccountReplayResult(replayResult);

            var service = new MarketDiagnosticsSnapshotService(
                repository,
                () => new DateTime(2026, 7, 2, 20, 1, 0),
                () => new DateTime(2026, 7, 2, 9, 0, 0));

            MarketDiagnosticsSnapshot snapshot = service.BuildSnapshot();

            NaturalDayPnlEvaluationItem item = Assert.Single(snapshot.PnlItems);
            Assert.Equal("159941", item.StrategyCode);
            Assert.Equal("017091", item.ActualCode);
            Assert.True(item.IncludedToday);
            Assert.Equal(269.12, snapshot.PnlSummary.DiagnosticsIncludedTotal);
            Assert.Equal(snapshot.PnlSummary.MainWindowPathTotal, snapshot.PnlSummary.DiagnosticsIncludedTotal);
        }
        finally
        {
            TryDeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void SnapshotService_DoesNotContainWriteDeleteOrMarketRefreshCalls()
    {
        string code = ReadRepositoryFile(Path.Combine("Core", "Services", "MarketDiagnosticsSnapshotService.cs"));

        Assert.DoesNotContain(".Save", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Write", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Delete", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Probe", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpClient", code, StringComparison.Ordinal);
    }

    private static string CreateTempDatabasePath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "crossetf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, LocalDatabase.DatabaseFileName);
    }

    private static MarketDiagnosticsSnapshotService CreateService(LocalDataRepository repository)
        => new(
            repository,
            () => new DateTime(2026, 7, 8, 10, 31, 0),
            () => new DateTime(2026, 7, 8, 9, 0, 0));

    private static void SaveStrategy(
        LocalDataRepository repository,
        string code,
        string? indexSecId,
        bool enabled)
        => repository.SaveStrategyConfig(new StrategyConfigRecord
        {
            Code = code,
            Name = code,
            IndexSecId = indexSecId,
            Enabled = enabled
        });

    private static MarketQuoteRecord Quote(
        string symbol,
        string marketType,
        string source,
        string quoteTime,
        string receivedAt)
        => new()
        {
            Symbol = symbol,
            DisplayName = symbol,
            MarketType = marketType,
            Source = source,
            Price = 1.0,
            LastClose = 1.0,
            QuoteTime = quoteTime,
            ReceivedAt = receivedAt
        };

    private static void InsertRuntimeLog(
        LocalDataRepository repository,
        string time,
        string level,
        string module,
        string message,
        string? detail)
    {
        string databasePath = repository.DatabasePath;
        var database = new LocalDatabase(databasePath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runtime_log(time, level, module, message, detail)
            VALUES ($time, $level, $module, $message, $detail);
            """;
        command.Parameters.AddWithValue("$time", time);
        command.Parameters.AddWithValue("$level", level);
        command.Parameters.AddWithValue("$module", module);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void TryDeleteDatabase(string databasePath)
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for SQLite WAL files.
            }
        }
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "CrossETF.Terminal.UiShell.Reference.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Cannot locate repository root.");
    }
}
