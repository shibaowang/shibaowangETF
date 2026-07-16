using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Persistence;

public sealed class T1T6ChartCenterReadModelTests
{
    [Fact]
    public void Read_UsesModeReadOnlyQueryOnlyOneConnectionAndOneDeferredTransaction()
    {
        string source = ReadRepositoryFile("Infrastructure", "Persistence", "LocalDataRepository.cs");
        string section = Extract(
            source,
            "public T1T6ChartCenterReadModel ReadT1T6ChartCenterReadModel()",
            "public IReadOnlyList<StrategyConfigRecord> ReadStrategyConfigs()");

        Assert.Equal(2, Count(section, "OpenT1T6ChartCenterReadOnlyConnection()"));
        Assert.Equal(1, Count(section, "new SqliteConnection(builder.ToString())"));
        Assert.Equal(1, Count(section, "connection.BeginTransaction(deferred: true)"));
        Assert.Contains("Mode = SqliteOpenMode.ReadOnly", section, StringComparison.Ordinal);
        Assert.Contains("PRAGMA query_only = ON;", section, StringComparison.Ordinal);
        Assert.Contains("Pooling = false", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_UsesFourBatchQueriesWithoutNPlusOne()
    {
        string section = ReadT1T6RepositorySection();

        Assert.Equal(4, Count(section, "SELECT "));
        Assert.Contains("FROM strategy_config", section, StringComparison.Ordinal);
        Assert.Contains("WHERE enabled = 1", section, StringComparison.Ordinal);
        Assert.Contains("FROM strategy_decision_state", section, StringComparison.Ordinal);
        Assert.Contains("WHERE strategy_code IN", section, StringComparison.Ordinal);
        Assert.Contains("FROM market_quote_cache", section, StringComparison.Ordinal);
        Assert.Contains("symbol IN", section, StringComparison.Ordinal);
        Assert.Contains("FROM market_source_status", section, StringComparison.Ordinal);
        Assert.Contains("source IN", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ReturnsEnabledStrategiesLatestExactDecisionsAndOnlyRelatedQuotes()
    {
        using var environment = new TestEnvironment();
        environment.Seed();

        T1T6ChartCenterReadModel model = environment.Repository.ReadT1T6ChartCenterReadModel();

        Assert.Null(model.ReadError);
        Assert.Equal(new[] { "159941", "sz159941" }, model.EnabledStrategies.Select(item => item.Code));
        Assert.DoesNotContain(model.EnabledStrategies, item => item.Code == "513110");
        Assert.Equal(2, model.LatestDecisions.Count);
        Assert.Equal("new-first", model.LatestDecisions.Single(item => item.StrategyCode == "159941").ActionInstruction);
        Assert.Equal("new-second-by-id", model.LatestDecisions.Single(item => item.StrategyCode == "sz159941").ActionInstruction);
        Assert.All(model.RelatedQuotes, quote => Assert.Equal("159941", quote.Symbol));
        Assert.DoesNotContain(model.RelatedQuotes, quote => quote.Symbol == "513110");
    }

    [Fact]
    public void Read_DoesNotMergeSameEtfDecisionsByNormalizedCode()
    {
        using var environment = new TestEnvironment();
        environment.Seed();

        T1T6ChartCenterReadModel model = environment.Repository.ReadT1T6ChartCenterReadModel();

        Assert.Contains(model.LatestDecisions, item => item.StrategyCode == "159941" && item.TargetTier == "狙击一档");
        Assert.Contains(model.LatestDecisions, item => item.StrategyCode == "sz159941" && item.TargetTier == "狙击三档");
    }

    [Fact]
    public void Read_ReturnsOnlyRelatedSourceStatuses()
    {
        using var environment = new TestEnvironment();
        environment.Seed();

        T1T6ChartCenterReadModel model = environment.Repository.ReadT1T6ChartCenterReadModel();

        MarketSourceStatusRecord status = Assert.Single(model.RelatedSourceStatuses);
        Assert.Equal("TENCENT_QT", status.Source);
        Assert.Equal("OK", status.Status);
    }

    [Fact]
    public void Read_EmptyInitializedDatabaseReturnsSafeEmptyModel()
    {
        using var environment = new TestEnvironment();

        T1T6ChartCenterReadModel model = environment.Repository.ReadT1T6ChartCenterReadModel();

        Assert.Null(model.ReadError);
        Assert.Empty(model.EnabledStrategies);
        Assert.Empty(model.LatestDecisions);
        Assert.Empty(model.RelatedQuotes);
        Assert.Empty(model.RelatedSourceStatuses);
    }

    [Fact]
    public void Read_MissingRequiredTableReturnsControlledError()
    {
        using var environment = new TestEnvironment();
        environment.Execute("""
            INSERT INTO strategy_config(code, name, enabled, created_at, updated_at)
            VALUES('159941', '策略甲', 1, '2026-07-16 08:00:00', '2026-07-16 08:00:00');
            """);
        environment.Execute("DROP TABLE strategy_decision_state;");

        T1T6ChartCenterReadModel model = environment.Repository.ReadT1T6ChartCenterReadModel();

        Assert.NotNull(model.ReadError);
        Assert.Empty(model.EnabledStrategies);
    }

    [Fact]
    public void Read_DoesNotChangeAnyTableContent()
    {
        using var environment = new TestEnvironment();
        environment.Seed();
        string before = environment.ReadStableDatabaseDigest();

        _ = environment.Repository.ReadT1T6ChartCenterReadModel();

        Assert.Equal(before, environment.ReadStableDatabaseDigest());
    }

    [Fact]
    public void Read_SourceContainsNoWriteNetworkReplayTradeLogHistoryOrDraftAccess()
    {
        string section = ReadT1T6RepositorySection().ToUpperInvariant();
        string[] forbidden =
        {
            "INSERT ", "UPDATE ", "DELETE ", "REPLACE ", "TRADE_LOG", "ACCOUNT_REPLAY_STATE",
            "ACCOUNT_REPLAY_SNAPSHOT", "POSITION_REPLAY_STATE", "OTC_POSITION_REPLAY_STATE",
            "ORDER_DRAFT_STATE", "ORDER_DRAFT_LEG_STATE", "MARKET_HISTORY_CACHE", "CHART_INTRADAY_CACHE",
            "RUNTIME_LOG", "HTTPCLIENT", "MARKETDATAREFRESH", "STRATEGYDECISIONSERVICE", "ACCOUNTREPLAYSERVICE"
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, section, StringComparison.Ordinal));
    }

    [Fact]
    public void TestsUseRandomTemporaryDatabaseNotUserDatabase()
    {
        using var environment = new TestEnvironment();
        string productionSuffix = Path.Combine(LocalDatabase.AppFolderName, LocalDatabase.DatabaseFileName);

        Assert.StartsWith(environment.RootDirectory, environment.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(environment.DatabasePath.EndsWith(productionSuffix, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(new LocalDatabase().DatabasePath, environment.DatabasePath);
    }

    private static string ReadT1T6RepositorySection()
    {
        string source = ReadRepositoryFile("Infrastructure", "Persistence", "LocalDataRepository.cs");
        return Extract(
            source,
            "public T1T6ChartCenterReadModel ReadT1T6ChartCenterReadModel()",
            "public IReadOnlyList<StrategyConfigRecord> ReadStrategyConfigs()");
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
        public TestEnvironment()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "cross-etf-t1t6-read-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
            DatabasePath = Path.Combine(RootDirectory, "t1t6.db");
            Repository = new LocalDataRepository(new LocalDatabase(DatabasePath));
        }

        public string RootDirectory { get; }
        public string DatabasePath { get; }
        public LocalDataRepository Repository { get; }

        public void Seed()
        {
            Execute("""
                INSERT INTO strategy_config(code, name, index_sec_id, enabled, created_at, updated_at)
                VALUES('159941', '策略甲', '100.NDX100', 1, '2026-07-16 08:00:00', '2026-07-16 08:00:00');
                INSERT INTO strategy_config(code, name, index_sec_id, enabled, created_at, updated_at)
                VALUES('sz159941', '策略乙', '251.NDXTMC', 1, '2026-07-16 08:00:00', '2026-07-16 08:00:00');
                INSERT INTO strategy_config(code, name, index_sec_id, enabled, created_at, updated_at)
                VALUES('513110', '停用策略', '100.NDX100', 0, '2026-07-16 08:00:00', '2026-07-16 08:00:00');

                INSERT INTO strategy_decision_state(calculated_at, strategy_code, action_instruction, preferred_source, target_tier, index_drawdown)
                VALUES('2026-07-16 09:00:00', '159941', 'old-first', 'TENCENT_QT', '狙击一档', -0.06);
                INSERT INTO strategy_decision_state(calculated_at, strategy_code, action_instruction, preferred_source, target_tier, index_drawdown)
                VALUES('2026-07-16 09:30:00', '159941', 'new-first', 'TENCENT_QT', '狙击一档', -0.07);
                INSERT INTO strategy_decision_state(calculated_at, strategy_code, action_instruction, preferred_source, target_tier, index_drawdown)
                VALUES('2026-07-16 09:40:00', 'sz159941', 'old-second-by-id', 'TENCENT_QT', '狙击三档', -0.16);
                INSERT INTO strategy_decision_state(calculated_at, strategy_code, action_instruction, preferred_source, target_tier, index_drawdown)
                VALUES('2026-07-16 09:40:00', 'sz159941', 'new-second-by-id', 'TENCENT_QT', '狙击三档', -0.17);
                INSERT INTO strategy_decision_state(calculated_at, strategy_code, action_instruction, preferred_source, target_tier, index_drawdown)
                VALUES('2026-07-16 09:50:00', '513110', 'unrelated', 'TENCENT_QT', '狙击六档', -0.31);

                INSERT INTO market_quote_cache(symbol, display_name, market_type, source, price, quote_time, received_at)
                VALUES('159941', '纳指ETF广发', 'ETF', 'TENCENT_QT', 1.68, '2026-07-16 09:59:00', '2026-07-16 09:59:01');
                INSERT INTO market_quote_cache(symbol, display_name, market_type, source, price, quote_time, received_at)
                VALUES('513110', '历史停用缓存', 'ETF', 'TENCENT_QT', 1.21, '2026-07-16 09:59:00', '2026-07-16 09:59:01');
                INSERT INTO market_quote_cache(symbol, display_name, market_type, source, price, quote_time, received_at)
                VALUES('251.NDXTMC', '无关指数', 'INDEX', 'EASTMONEY_PUSH2', 2900, '2026-07-16 09:59:00', '2026-07-16 09:59:01');

                INSERT INTO market_source_status(source, status, failure_count, updated_at)
                VALUES('TENCENT_QT', 'OK', 0, '2026-07-16 09:59:01');
                INSERT INTO market_source_status(source, status, failure_count, updated_at)
                VALUES('SINA_FUND', 'ERROR', 3, '2026-07-16 09:59:01');

                INSERT INTO trade_log(time, strategy_code, actual_code, action, quantity, price, amount, fee, net_cash_impact, principal, cash_balance, total_assets)
                VALUES('2026-07-16 08:30:00', '159941', '159941', '买入', 100, 1.6, 160, 0, -160, 100000, 99840, 100000);
                INSERT INTO runtime_log(time, level, module, message)
                VALUES('2026-07-16 09:00:00', 'WARN', 'TEST', 'must remain unread');
                """);
        }

        public void Execute(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWrite
            }.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public string ReadStableDatabaseDigest()
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            connection.Open();
            using SqliteCommand tablesCommand = connection.CreateCommand();
            tablesCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using SqliteDataReader tableReader = tablesCommand.ExecuteReader();
            var tables = new List<string>();
            while (tableReader.Read())
            {
                tables.Add(tableReader.GetString(0));
            }

            var content = new StringBuilder();
            foreach (string table in tables)
            {
                content.AppendLine(table);
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\" ORDER BY rowid;";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    for (int index = 0; index < reader.FieldCount; index++)
                    {
                        object value = reader.GetValue(index);
                        content.Append(value is DBNull ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture));
                        content.Append('\u001f');
                    }

                    content.AppendLine();
                }
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide the assertion result.
            }
        }
    }
}
