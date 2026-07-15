using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Persistence;

public sealed class IndicatorDrawdownReadModelTests
{
    [Fact]
    public void InitialRead_UsesIsolatedReadOnlyConnectionQueryOnlyAndOneTransaction()
    {
        string source = ReadRepositoryFile("Infrastructure", "Persistence", "LocalDataRepository.IndicatorDrawdown.cs");
        string method = Extract(source, "public IndicatorDrawdownReadModel ReadIndicatorDrawdownReadModel()", "public IndicatorDrawdownRealtimeReadModel");

        Assert.Equal(1, Count(method, "OpenIndicatorDrawdownReadOnlyConnection()"));
        Assert.Equal(1, Count(method, "BeginTransaction"));
        Assert.Contains("EnableIndicatorDrawdownQueryOnly(connection)", method, StringComparison.Ordinal);
        Assert.Contains("Mode = SqliteOpenMode.ReadOnly", source, StringComparison.Ordinal);
        Assert.Contains("PRAGMA query_only = ON;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialRead_BatchesOnlyEnabledEtfsAndTwoFixedIndexes()
    {
        using var environment = new TestEnvironment();
        environment.Seed();

        IndicatorDrawdownReadModel model = environment.Repository.ReadIndicatorDrawdownReadModel();

        Assert.Null(model.ReadError);
        Assert.Equal(new[] { "251.NDXTMC", "100.NDX100", "159941" }, model.Instruments.Select(item => item.Code));
        Assert.DoesNotContain(model.Instruments, item => item.Code == "513110");
        Assert.DoesNotContain(model.Quotes, quote => quote.Symbol == "513110");
        Assert.DoesNotContain(model.HistoryCandidates, history => history.Symbol == "513110");
    }

    [Fact]
    public void InitialRead_EmptyInitializedDatabaseReturnsFixedIndexesAndControlledMissingHistory()
    {
        using var environment = new TestEnvironment();

        IndicatorDrawdownReadModel model = environment.Repository.ReadIndicatorDrawdownReadModel();

        Assert.Null(model.ReadError);
        Assert.Equal(new[] { "251.NDXTMC", "100.NDX100" }, model.Instruments.Select(item => item.Code));
        Assert.Empty(model.Quotes);
        Assert.Empty(model.HistoryCandidates);
    }

    [Fact]
    public void InitialRead_MissingRequiredTableReturnsControlledReadError()
    {
        using var environment = new TestEnvironment();
        environment.DropHistoryTableForControlledFailure();

        IndicatorDrawdownReadModel model = environment.Repository.ReadIndicatorDrawdownReadModel();

        Assert.NotNull(model.ReadError);
        Assert.Empty(model.HistoryCandidates);
    }

    [Fact]
    public void InitialRead_ReturnsAllHistoryCandidatesForSafeFallbackWithoutReadingUnrelatedRows()
    {
        using var environment = new TestEnvironment();
        environment.Seed();

        IndicatorDrawdownReadModel model = environment.Repository.ReadIndicatorDrawdownReadModel();

        Assert.Equal(2, model.HistoryCandidates.Count(item => item.Symbol == "159941"));
        Assert.All(model.HistoryCandidates, item => Assert.Contains(item.Symbol, new[] { "159941", "251.NDXTMC", "100.NDX100" }));
        Assert.All(model.HistoryCandidates, item => Assert.Equal(item.RawPayload?.Length ?? 0, item.PayloadLength));
    }

    [Fact]
    public void InitialRead_ReadsOnlyFourRelatedSourceStatuses()
    {
        using var environment = new TestEnvironment();
        environment.Seed();

        IndicatorDrawdownReadModel model = environment.Repository.ReadIndicatorDrawdownReadModel();

        Assert.DoesNotContain(model.SourceStatuses, status => status.Source == MarketSources.SinaFund);
        Assert.Equal(4, model.SourceStatuses.Count);
    }

    [Fact]
    public void RealtimeRead_DoesNotReadHistoryMetadataOnOrdinaryTwoSecondTick()
    {
        using var environment = new TestEnvironment();
        environment.Seed();

        IndicatorDrawdownRealtimeReadModel model = environment.Repository.ReadIndicatorDrawdownRealtimeState();

        Assert.Empty(model.HistoryMetadata);
        Assert.NotEmpty(model.Quotes);
        Assert.NotEmpty(model.Instruments);
    }

    [Fact]
    public void RealtimeRead_ReadsMetadataWithoutPayloadWhenThirtySecondCheckIsRequested()
    {
        using var environment = new TestEnvironment();
        environment.Seed();

        IndicatorDrawdownRealtimeReadModel model = environment.Repository.ReadIndicatorDrawdownRealtimeState(includeHistoryMetadata: true);

        Assert.NotEmpty(model.HistoryMetadata);
        Assert.All(model.HistoryMetadata, item => Assert.True(item.PayloadLength > 0));
        string source = ReadRepositoryFile("Infrastructure", "Persistence", "LocalDataRepository.IndicatorDrawdown.cs");
        string metadataMethod = Extract(source, "private static IReadOnlyList<IndicatorDrawdownHistoryMetadata>", "private static string AddInstrumentTargetPredicate");
        Assert.DoesNotContain("SELECT raw_payload", metadataMethod, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("length(COALESCE(raw_payload, ''))", metadataMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedHistoryRead_ReadsOnlyRequestedSymbols()
    {
        using var environment = new TestEnvironment();
        environment.Seed();
        var target = new IndicatorDrawdownInstrument(
            "ETF|159941", "场内 ETF", "ETF", "159941", "纳指ETF广发", "159941",
            MarketSources.TencentHistory, MarketSources.Tencent, 2);

        IReadOnlyList<IndicatorDrawdownHistoryCandidate> candidates = environment.Repository.ReadIndicatorDrawdownHistoryCandidates(new[] { target });

        Assert.NotEmpty(candidates);
        Assert.All(candidates, item => Assert.Equal("159941", item.Symbol));
    }

    [Fact]
    public void InitialAndRealtimeReads_DoNotChangeAnyBusinessOrCacheRowCount()
    {
        using var environment = new TestEnvironment();
        environment.Seed();
        Dictionary<string, long> before = environment.ReadCounts();

        _ = environment.Repository.ReadIndicatorDrawdownReadModel();
        _ = environment.Repository.ReadIndicatorDrawdownRealtimeState(true);

        Assert.Equal(before, environment.ReadCounts());
    }

    [Fact]
    public void SourceContainsNoInsertUpdateDeleteSaveNetworkTradeLogOrReplayAccess()
    {
        string source = ReadRepositoryFile("Infrastructure", "Persistence", "LocalDataRepository.IndicatorDrawdown.cs");
        string upper = source.ToUpperInvariant();

        Assert.DoesNotContain("INSERT ", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE ", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE ", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("REPLACE ", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("TRADE_LOG", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("ACCOUNT_REPLAY", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("STRATEGY_DECISION", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER_DRAFT", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("RUNTIME_LOG", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTPCLIENT", upper, StringComparison.Ordinal);
        Assert.DoesNotContain("MARKETDATAREFRESH", upper, StringComparison.Ordinal);
    }

    [Fact]
    public void TestsAlwaysUseExplicitTemporaryDatabaseInsteadOfUserDatabase()
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
            "strategy_config", "market_quote_cache", "market_history_cache", "market_source_status",
            "trade_log", "runtime_log", "account_replay_state", "position_replay_state", "strategy_decision_state", "order_draft_state"
        };

        public TestEnvironment()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "cross-etf-indicator-read-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
            DatabasePath = Path.Combine(RootDirectory, "indicator.db");
            Repository = new LocalDataRepository(new LocalDatabase(DatabasePath));
        }

        public string RootDirectory { get; }
        public string DatabasePath { get; }
        public LocalDataRepository Repository { get; }

        public void Seed()
        {
            Execute("""
                INSERT INTO strategy_config(code, name, enabled, created_at, updated_at)
                VALUES('159941', '纳指ETF广发', 1, '2026-07-15 09:00:00', '2026-07-15 09:00:00');
                INSERT INTO strategy_config(code, name, enabled, created_at, updated_at)
                VALUES('513110', '已停用策略', 0, '2026-07-15 09:00:00', '2026-07-15 09:00:00');

                INSERT INTO market_quote_cache(symbol, display_name, market_type, source, price, quote_time, received_at)
                VALUES('159941', '纳指ETF广发', 'ETF', 'TENCENT_QT', 1.6, '2026-07-15 10:00:00', '2026-07-15 10:00:01');
                INSERT INTO market_quote_cache(symbol, display_name, market_type, source, price, quote_time, received_at)
                VALUES('251.NDXTMC', '纳斯达克科技指数', 'INDEX', 'EASTMONEY_PUSH2', 2921, '2026-07-15 10:00:00', '2026-07-15 10:00:01');
                INSERT INTO market_quote_cache(symbol, display_name, market_type, source, price, quote_time, received_at)
                VALUES('100.NDX100', '纳斯达克100', 'INDEX', 'EASTMONEY_PUSH2', 29800, '2026-07-15 10:00:00', '2026-07-15 10:00:01');
                INSERT INTO market_quote_cache(symbol, display_name, market_type, source, price, quote_time, received_at)
                VALUES('513110', '历史缓存', 'ETF', 'TENCENT_QT', 1.2, '2026-07-15 10:00:00', '2026-07-15 10:00:01');

                INSERT INTO market_source_status(source, status, failure_count, updated_at) VALUES('TENCENT_QT', 'OK', 0, '2026-07-15 10:00:00');
                INSERT INTO market_source_status(source, status, failure_count, updated_at) VALUES('EASTMONEY_PUSH2', 'OK', 0, '2026-07-15 10:00:00');
                INSERT INTO market_source_status(source, status, failure_count, updated_at) VALUES('TENCENT_DAILY_QFQ', 'OK', 0, '2026-07-15 10:00:00');
                INSERT INTO market_source_status(source, status, failure_count, updated_at) VALUES('EASTMONEY_HISTORY', 'OK', 0, '2026-07-15 10:00:00');
                INSERT INTO market_source_status(source, status, failure_count, updated_at) VALUES('SINA_FUND', 'OK', 0, '2026-07-15 10:00:00');
                """);
            InsertHistory("159941", "ETF", MarketSources.TencentHistory, 1, "2026-07-15 09:00:00");
            InsertHistory("159941", "ETF", MarketSources.TencentHistory, 2, "2026-07-15 10:00:00");
            InsertHistory("251.NDXTMC", "INDEX", MarketSources.EastMoneyHistory, 3, "2026-07-15 10:00:00");
            InsertHistory("100.NDX100", "INDEX", MarketSources.EastMoneyHistory, 4, "2026-07-15 10:00:00");
            InsertHistory("513110", "ETF", MarketSources.TencentHistory, 5, "2026-07-15 10:00:00");
        }

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

        public void DropHistoryTableForControlledFailure()
            => Execute("DROP TABLE market_history_cache;");

        private void InsertHistory(string symbol, string marketType, string source, long seed, string updatedAt)
        {
            string payload = Payload(seed);
            using SqliteConnection connection = new LocalDatabase(DatabasePath).OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO market_history_cache(symbol, market_type, source, cache_date, raw_payload, updated_at)
                VALUES($symbol, $market_type, $source, $cache_date, $payload, $updated_at);
                """;
            command.Parameters.AddWithValue("$symbol", symbol);
            command.Parameters.AddWithValue("$market_type", marketType);
            command.Parameters.AddWithValue("$source", source);
            command.Parameters.AddWithValue("$cache_date", $"2026-07-{10 + seed:00}");
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$updated_at", updatedAt);
            command.ExecuteNonQuery();
        }

        private static string Payload(long seed)
        {
            string[] lines = Enumerable.Range(0, 4).Select(index =>
            {
                DateTime date = new DateTime(2026, 7, 10).AddDays(index);
                double close = seed + index + 10;
                return $"\"{date:yyyy-MM-dd},{close.ToString(CultureInfo.InvariantCulture)},{close.ToString(CultureInfo.InvariantCulture)},{close.ToString(CultureInfo.InvariantCulture)},{close.ToString(CultureInfo.InvariantCulture)},0,0\"";
            }).ToArray();
            return "{\"data\":{\"klines\":[" + string.Join(",", lines) + "]}}";
        }

        private void Execute(string sql)
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
