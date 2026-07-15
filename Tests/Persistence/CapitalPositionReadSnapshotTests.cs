using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Persistence;

public sealed class CapitalPositionReadSnapshotTests
{
    [Fact]
    public void ReadModel_UsesOneConnectionOneQueryOnlyTransactionAndOnlySelectsCurrentInputs()
    {
        string code = ReadRepositoryFile("Infrastructure", "Persistence", "LocalDataRepository.cs");
        string method = Extract(code, "public CapitalPositionReadModel ReadCapitalPositionReadModel()", "public AccountReplayStateRecord? ReadLatestAccountReplayState()");

        Assert.Equal(1, Count(method, "_database.OpenConnection()"));
        Assert.Equal(1, Count(method, "BeginTransaction"));
        Assert.Contains("PRAGMA query_only = ON;", method, StringComparison.Ordinal);
        Assert.Contains("transaction.Commit();", method, StringComparison.Ordinal);
        Assert.DoesNotContain("new LocalDatabase", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadTradeLogs", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadRuntime", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadModel_AccountQueryReadsOnlyLatestRecordInsteadOfHistory()
    {
        string code = ReadRepositoryFile("Infrastructure", "Persistence", "LocalDataRepository.cs");
        string method = Extract(code, "private static AccountReplayStateRecord? ReadCapitalPositionAccount", "private static IReadOnlyList<PositionReplayStateRecord> ReadCapitalPositionPositions");

        Assert.Contains("ORDER BY calculated_at DESC, id DESC", method, StringComparison.Ordinal);
        Assert.Contains("LIMIT 1", method, StringComparison.Ordinal);
        Assert.DoesNotContain("account_replay_snapshot", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadModel_EmptyTemporaryDatabaseReturnsSafeEmptyCollections()
    {
        using var environment = new TestEnvironment();

        CapitalPositionReadModel model = environment.Repository.ReadCapitalPositionReadModel();

        Assert.Null(model.Account);
        Assert.Null(model.LatestDecision);
        Assert.Empty(model.Positions);
        Assert.Empty(model.OtcPositions);
        Assert.Empty(model.Strategies);
        Assert.Empty(model.OtcChannels);
        Assert.Empty(model.Quotes);
        Assert.NotEqual(default, model.ReadAt);
    }

    [Fact]
    public void ReadModel_ReturnsLatestAccountCurrentReplayRowsConfigurationQuotesAndLatestValidDecision()
    {
        using var environment = new TestEnvironment();
        environment.SeedCompleteSnapshot();

        CapitalPositionReadModel model = environment.Repository.ReadCapitalPositionReadModel();

        Assert.NotNull(model.Account);
        Assert.Equal("2026-07-15 10:00:00", model.Account!.CalculatedAt);
        Assert.Equal(2000, model.Account.TotalAssets);
        Assert.Single(model.Positions);
        Assert.Equal("159941", model.Positions[0].ActualCode);
        Assert.Single(model.OtcPositions);
        Assert.Equal("017091", model.OtcPositions[0].ActualCode);
        Assert.Single(model.Strategies);
        Assert.Single(model.OtcChannels);
        Assert.Single(model.Quotes);
        Assert.NotNull(model.LatestDecision);
        Assert.Equal("2026-07-15 09:59:00", model.LatestDecision!.CalculatedAt);
        Assert.Equal(777.0, model.LatestDecision.RealSniperPool);
        Assert.Equal(1.25, model.LatestDecision.BaseCompletionRate);
    }

    [Fact]
    public void ReadModel_DoesNotWriteOrDeleteAnyBusinessRows()
    {
        using var environment = new TestEnvironment();
        environment.SeedCompleteSnapshot();
        Dictionary<string, long> before = environment.ReadCounts();

        _ = environment.Repository.ReadCapitalPositionReadModel();

        Dictionary<string, long> after = environment.ReadCounts();
        Assert.Equal(before, after);
    }

    [Fact]
    public void ReadModel_MissingOptionalDecisionQuotesAndChannelsRemainEmptyWithoutFallback()
    {
        using var environment = new TestEnvironment();
        environment.Execute("""
            INSERT INTO account_replay_state(
                calculated_at, replay_status, cash_balance, principal, known_market_value,
                total_assets, market_value_complete)
            VALUES('2026-07-15 10:00:00', '正常', 1000, 1000, 0, 1000, 1);
            """);

        CapitalPositionReadModel model = environment.Repository.ReadCapitalPositionReadModel();

        Assert.NotNull(model.Account);
        Assert.Null(model.LatestDecision);
        Assert.Empty(model.Quotes);
        Assert.Empty(model.OtcChannels);
    }

    [Fact]
    public void ReadModel_SourceContainsNoWriteSchemaHistoryOrDiagnosticsStatements()
    {
        string code = ReadRepositoryFile("Infrastructure", "Persistence", "LocalDataRepository.cs");
        string section = Extract(code, "public CapitalPositionReadModel ReadCapitalPositionReadModel()", "public AccountReplayStateRecord? ReadLatestAccountReplayState()")
                         + Extract(code, "private static AccountReplayStateRecord? ReadCapitalPositionAccount", "private static AccountReplayStateRecord ReadAccountReplayState");
        string upper = section.ToUpperInvariant();

        Assert.DoesNotContain("INSERT ", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE ", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE ", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("REPLACE ", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM TRADE_LOG", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("RUNTIME_LOG", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("ACCOUNT_REPLAY_SNAPSHOT", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER_DRAFT", upper, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadModel_TestAlwaysUsesIsolatedTemporaryDatabase()
    {
        using var environment = new TestEnvironment();
        string productionSuffix = Path.Combine(LocalDatabase.AppFolderName, LocalDatabase.DatabaseFileName);

        Assert.StartsWith(environment.RootDirectory, environment.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(environment.DatabasePath.EndsWith(productionSuffix, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(new LocalDatabase().DatabasePath, environment.DatabasePath);
    }

    private static int Count(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

    private static string Extract(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to extract source between {startMarker} and {endMarker}.");
        return source[start..end];
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(parts));
    }

    private sealed class TestEnvironment : IDisposable
    {
        private static readonly string[] CountedTables =
        {
            "account_replay_state", "position_replay_state", "otc_position_replay_state",
            "strategy_config", "otc_channel", "market_quote_cache", "strategy_decision_state",
            "trade_log", "runtime_log"
        };

        public TestEnvironment()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "cross-etf-capital-position-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
            DatabasePath = Path.Combine(RootDirectory, "capital-position.db");
            Repository = new LocalDataRepository(new LocalDatabase(DatabasePath));
        }

        public string RootDirectory { get; }
        public string DatabasePath { get; }
        public LocalDataRepository Repository { get; }

        public void SeedCompleteSnapshot()
            => Execute("""
                INSERT INTO account_replay_state(
                    calculated_at, replay_status, cash_balance, principal, known_market_value,
                    total_assets, total_realized_pnl, total_unrealized_pnl, cash_ratio,
                    position_ratio, base_position_ratio, market_value_complete)
                VALUES('2026-07-15 09:00:00', '正常', 500, 1000, 500, 1000, 10, 20, 0.5, 0.5, 0.2, 1);
                INSERT INTO account_replay_state(
                    calculated_at, replay_status, cash_balance, principal, known_market_value,
                    total_assets, total_realized_pnl, total_unrealized_pnl, cash_ratio,
                    position_ratio, base_position_ratio, market_value_complete)
                VALUES('2026-07-15 10:00:00', '正常', 800, 1500, 1200, 2000, 30, 40, 0.4, 0.6, 0.2, 1);

                INSERT INTO strategy_config(code, name, enabled, created_at, updated_at)
                VALUES('159941', '纳指ETF广发', 1, '2026-07-15 09:00:00', '2026-07-15 09:00:00');
                INSERT INTO otc_channel(strategy_code, otc_code, class_type, enabled, daily_limit, priority, min_buy, created_at, updated_at)
                VALUES('159941', '017091', 'A类', 1, 1000, 3, 10, '2026-07-15 09:00:00', '2026-07-15 09:00:00');

                INSERT INTO position_replay_state(
                    calculated_at, strategy_code, actual_code, source, quantity, cost_amount,
                    average_cost, adj_factor, today_buy_quantity, today_buy_amount, market_price,
                    market_value, realized_pnl, unrealized_pnl, return_rate, quote_status)
                VALUES('2026-07-15 10:00:00', '159941', '159941', '场内ETF', 100, 150, 1.5, 1, 0, 0, 1.6, 160, 0, 10, 0.0667, '真实行情');
                INSERT INTO otc_position_replay_state(
                    calculated_at, strategy_code, actual_code, quantity, cost_amount, average_cost,
                    nav, market_value, unrealized_pnl, return_rate, quote_status)
                VALUES('2026-07-15 10:00:00', '159941', '017091', 100, 110, 1.1, 1.2, 120, 10, 0.0909, '真实净值');
                INSERT INTO market_quote_cache(
                    symbol, display_name, market_type, source, price, quote_time, received_at)
                VALUES('159941', '纳指ETF广发', 'ETF', 'TENCENT_QT', 1.6, '2026-07-15 10:00:00', '2026-07-15 10:00:01');

                INSERT INTO strategy_decision_state(calculated_at, strategy_code, base_completion_rate, real_sniper_pool)
                VALUES('2026-07-15 09:58:00', '159941', 1.10, 666);
                INSERT INTO strategy_decision_state(calculated_at, strategy_code, base_completion_rate, real_sniper_pool)
                VALUES('2026-07-15 09:59:00', '159941', 1.25, 777);
                INSERT INTO strategy_decision_state(calculated_at, strategy_code, base_completion_rate, real_sniper_pool)
                VALUES('2026-07-15 10:01:00', '159941', NULL, NULL);
                """);

        public Dictionary<string, long> ReadCounts()
        {
            using SqliteConnection connection = new LocalDatabase(DatabasePath).OpenConnection();
            return CountedTables.ToDictionary(
                table => table,
                table =>
                {
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = $"SELECT COUNT(*) FROM {table};";
                    return Convert.ToInt64(command.ExecuteScalar());
                },
                StringComparer.Ordinal);
        }

        public void Execute(string sql)
        {
            using SqliteConnection connection = new LocalDatabase(DatabasePath).OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }
}
