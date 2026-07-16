using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Persistence;

public sealed class TradeLogAtomicSaveTests
{
    private static readonly string[] CoreTables =
    {
        "trade_log",
        "account_replay_state",
        "account_replay_snapshot",
        "position_replay_state",
        "otc_position_replay_state"
    };

    public static IEnumerable<object[]> AtomicFailureStages()
        => Enum.GetValues<TradeLogAtomicSaveStage>()
            .Select(stage => new object[] { stage.ToString() });

    [Fact]
    public void AtomicSave_CommitsFactsReplaySnapshotAndBothPositionTablesTogether()
    {
        using SeededScenario scenario = CreateSeededScenario();
        string beforeHash = CoreStateHash(scenario.DatabasePath);

        TradeLogAtomicSaveResult result = scenario.Repository.SaveTradeLogsAndReplayAtomically(
            new[] { scenario.DeleteId },
            scenario.CandidateRecords,
            scenario.Quotes);

        Assert.True(result.Committed);
        Assert.Equal("正常", result.ReplayResult.Account.ReplayStatus);
        Assert.Equal(scenario.CandidateRecords.Count, result.Identities.Count);
        Assert.All(result.Identities, identity => Assert.True(identity.PersistedId > 0));
        Assert.Equal(scenario.CandidateRecords.Count, scenario.Repository.ReadTradeLogs().Count);
        Assert.NotNull(scenario.Repository.ReadLatestAccountReplayState());
        Assert.NotEmpty(scenario.Repository.ReadAccountReplaySnapshots());
        Assert.Equal(2, scenario.Repository.ReadPositionReplayStates().Count);
        Assert.Single(scenario.Repository.ReadOtcPositionReplayStates());
        Assert.Equal(0, CountRows(scenario.DatabasePath, "runtime_log"));
        Assert.NotEqual(beforeHash, CoreStateHash(scenario.DatabasePath));
    }

    [Theory]
    [MemberData(nameof(AtomicFailureStages))]
    public void EveryInjectedFailure_RollsBackAllFiveCoreTables(string stageName)
    {
        TradeLogAtomicSaveStage stage = Enum.Parse<TradeLogAtomicSaveStage>(stageName);
        using SeededScenario scenario = CreateSeededScenario();
        string beforeHash = CoreStateHash(scenario.DatabasePath);
        long originalNewId = scenario.CandidateRecords[^1].Id;

        var fault = new ThrowingFaultInjector(stage);
        Assert.Throws<InjectedTradeLogAtomicSaveException>(() =>
            scenario.Repository.SaveTradeLogsAndReplayAtomically(
                new[] { scenario.DeleteId },
                scenario.CandidateRecords,
                scenario.Quotes,
                new DateTime(2026, 7, 16, 15, 30, 0),
                fault,
                commitAction: null));

        Assert.Equal(beforeHash, CoreStateHash(scenario.DatabasePath));
        Assert.Equal(originalNewId, scenario.CandidateRecords[^1].Id);
    }

    [Fact]
    public void CommitFailure_RollsBackAllFiveCoreTablesAndRestoresTransientIds()
    {
        using SeededScenario scenario = CreateSeededScenario();
        string beforeHash = CoreStateHash(scenario.DatabasePath);

        Assert.Throws<InjectedTradeLogAtomicSaveException>(() =>
            scenario.Repository.SaveTradeLogsAndReplayAtomically(
                new[] { scenario.DeleteId },
                scenario.CandidateRecords,
                scenario.Quotes,
                new DateTime(2026, 7, 16, 15, 30, 0),
                faultInjector: null,
                _ => throw new InjectedTradeLogAtomicSaveException("commit")));

        Assert.Equal(beforeHash, CoreStateHash(scenario.DatabasePath));
        Assert.Equal(0, scenario.CandidateRecords[^1].Id);
    }

    [Fact]
    public void FinancialError_RollsBackFactsAndAllDerivedState()
    {
        using SeededScenario scenario = CreateSeededScenario();
        string beforeHash = CoreStateHash(scenario.DatabasePath);
        scenario.CandidateRecords.Add(Log(
            "2026-07-16 11:00:00",
            "159941",
            "卖出",
            actualCode: "159941",
            quantity: 1000,
            amount: 1200,
            source: "场内ETF"));

        TradeLogFinancialReplayException error = Assert.Throws<TradeLogFinancialReplayException>(() =>
            scenario.Repository.SaveTradeLogsAndReplayAtomically(
                new[] { scenario.DeleteId },
                scenario.CandidateRecords,
                scenario.Quotes));

        Assert.Contains("财务异常", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeHash, CoreStateHash(scenario.DatabasePath));
        Assert.Equal(0, scenario.CandidateRecords[^1].Id);
    }

    [Fact]
    public void IncompleteValuation_CommitsWithoutWritingRuntimeLog()
    {
        using var database = new TemporaryDatabase();
        var records = new List<TradeLogRecord>
        {
            Log("2026-07-16 09:30:00", "159941", "买入", "159941", 1, 100, 100, source: "场内ETF")
        };

        TradeLogAtomicSaveResult result = database.Repository.SaveTradeLogsAndReplayAtomically(
            Array.Empty<long>(),
            records,
            Array.Empty<MarketQuoteRecord>());

        Assert.True(result.Committed);
        Assert.Equal("估值不完整", result.ReplayResult.Account.ReplayStatus);
        Assert.Single(database.Repository.ReadTradeLogs());
        Assert.Single(database.Repository.ReadPositionReplayStates());
        Assert.Equal(0, CountRows(database.DatabasePath, "runtime_log"));
    }

    [Fact]
    public void NewAndExistingRows_ReturnSnapshotIndexedDatabaseIdentities()
    {
        using var database = new TemporaryDatabase();
        TradeLogRecord existing = Log("2026-07-16 09:00:00", "CASH", "CASH", cashBalance: 1000, principal: 1000);
        database.Repository.SaveTradeLog(existing);
        long existingId = existing.Id;
        TradeLogRecord added = Log("2026-07-16 09:01:00", "159941", "分红", "159941", amount: 5, source: "场内ETF");

        TradeLogAtomicSaveResult result = database.Repository.SaveTradeLogsAndReplayAtomically(
            Array.Empty<long>(),
            new[] { Clone(existing), added },
            Array.Empty<MarketQuoteRecord>());

        Assert.Collection(
            result.Identities,
            identity =>
            {
                Assert.Equal(0, identity.SnapshotIndex);
                Assert.Equal(existingId, identity.OriginalId);
                Assert.Equal(existingId, identity.PersistedId);
            },
            identity =>
            {
                Assert.Equal(1, identity.SnapshotIndex);
                Assert.Equal(0, identity.OriginalId);
                Assert.True(identity.PersistedId > existingId);
            });
    }

    [Fact]
    public void IdenticalLegalTrades_AreInsertedSeparatelyAndNeverContentDeduplicated()
    {
        using var database = new TemporaryDatabase();
        TradeLogRecord first = Log("2026-07-16 10:00:00", "159941", "分红", "159941", amount: 5, source: "场内ETF");
        TradeLogRecord second = Clone(first);

        TradeLogAtomicSaveResult result = database.Repository.SaveTradeLogsAndReplayAtomically(
            Array.Empty<long>(),
            new[] { first, second },
            Array.Empty<MarketQuoteRecord>());

        Assert.Equal(2, database.Repository.ReadTradeLogs().Count);
        Assert.Equal(2, result.Identities.Select(identity => identity.PersistedId).Distinct().Count());
        Assert.Equal(10, result.ReplayResult.Account.CashBalance!.Value, 2);
    }

    [Fact]
    public void MissingUpdateTarget_ThrowsAndDoesNotFallbackToInsert()
    {
        using var database = new TemporaryDatabase();
        string beforeHash = CoreStateHash(database.DatabasePath);
        TradeLogRecord missing = Log("2026-07-16 10:00:00", "159941", "分红", "159941", amount: 5, source: "场内ETF");
        missing.Id = 987654;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            database.Repository.SaveTradeLogsAndReplayAtomically(
                Array.Empty<long>(),
                new[] { missing },
                Array.Empty<MarketQuoteRecord>()));

        Assert.Contains("更新失败", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeHash, CoreStateHash(database.DatabasePath));
        Assert.Empty(database.Repository.ReadTradeLogs());
    }

    [Fact]
    public void MissingDeleteTarget_ThrowsAndRollsBack()
    {
        using var database = new TemporaryDatabase();
        string beforeHash = CoreStateHash(database.DatabasePath);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            database.Repository.SaveTradeLogsAndReplayAtomically(
                new[] { 987654L },
                Array.Empty<TradeLogRecord>(),
                Array.Empty<MarketQuoteRecord>()));

        Assert.Contains("删除失败", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeHash, CoreStateHash(database.DatabasePath));
    }

    [Fact]
    public void FailedInsert_CanRetryWithoutDuplicateRows()
    {
        using var database = new TemporaryDatabase();
        TradeLogRecord record = Log("2026-07-16 09:00:00", "CASH", "CASH", cashBalance: 1000, principal: 1000);

        Assert.Throws<InjectedTradeLogAtomicSaveException>(() =>
            database.Repository.SaveTradeLogsAndReplayAtomically(
                Array.Empty<long>(),
                new[] { record },
                Array.Empty<MarketQuoteRecord>(),
                new DateTime(2026, 7, 16),
                new ThrowingFaultInjector(TradeLogAtomicSaveStage.BeforeCommit),
                commitAction: null));
        Assert.Equal(0, record.Id);
        Assert.Empty(database.Repository.ReadTradeLogs());

        TradeLogAtomicSaveResult retry = database.Repository.SaveTradeLogsAndReplayAtomically(
            Array.Empty<long>(),
            new[] { record },
            Array.Empty<MarketQuoteRecord>());

        Assert.Single(database.Repository.ReadTradeLogs());
        Assert.True(Assert.Single(retry.Identities).PersistedId > 0);
    }

    [Fact]
    public void FailedUpdate_CanRetryAsUpdateWithoutInsert()
    {
        using var database = new TemporaryDatabase();
        TradeLogRecord record = Log("2026-07-16 09:00:00", "CASH", "CASH", cashBalance: 1000, principal: 1000);
        database.Repository.SaveTradeLog(record);
        long id = record.Id;
        TradeLogRecord edited = Clone(record);
        edited.Memo = "updated";

        Assert.Throws<InjectedTradeLogAtomicSaveException>(() =>
            database.Repository.SaveTradeLogsAndReplayAtomically(
                Array.Empty<long>(),
                new[] { edited },
                Array.Empty<MarketQuoteRecord>(),
                new DateTime(2026, 7, 16),
                new ThrowingFaultInjector(TradeLogAtomicSaveStage.AfterTradeLogWrite),
                commitAction: null));
        Assert.Null(Assert.Single(database.Repository.ReadTradeLogs()).Memo);

        database.Repository.SaveTradeLogsAndReplayAtomically(
            Array.Empty<long>(),
            new[] { edited },
            Array.Empty<MarketQuoteRecord>());

        TradeLogRecord saved = Assert.Single(database.Repository.ReadTradeLogs());
        Assert.Equal(id, saved.Id);
        Assert.Equal("updated", saved.Memo);
    }

    [Fact]
    public void FailedDelete_CanRetryWithoutResidualDerivedState()
    {
        using SeededScenario scenario = CreateSeededScenario();
        string beforeHash = CoreStateHash(scenario.DatabasePath);

        Assert.Throws<InjectedTradeLogAtomicSaveException>(() =>
            scenario.Repository.SaveTradeLogsAndReplayAtomically(
                new[] { scenario.DeleteId },
                scenario.CandidateRecords,
                scenario.Quotes,
                new DateTime(2026, 7, 16),
                new ThrowingFaultInjector(TradeLogAtomicSaveStage.AfterTradeLogDelete),
                commitAction: null));
        Assert.Equal(beforeHash, CoreStateHash(scenario.DatabasePath));

        scenario.Repository.SaveTradeLogsAndReplayAtomically(
            new[] { scenario.DeleteId },
            scenario.CandidateRecords,
            scenario.Quotes);

        Assert.DoesNotContain(scenario.Repository.ReadTradeLogs(), record => record.Id == scenario.DeleteId);
    }

    [Fact]
    public void SameSecondNewRows_ReplayInFinalDatabaseIdOrder()
    {
        using var database = new TemporaryDatabase();
        var records = new[]
        {
            Log("2026-07-16 09:00:00", "CASH", "入金", amount: 100, netCashImpact: 100),
            Log("2026-07-16 09:00:00", "CASH", "出金", amount: 20, netCashImpact: -20)
        };

        TradeLogAtomicSaveResult result = database.Repository.SaveTradeLogsAndReplayAtomically(
            Array.Empty<long>(),
            records,
            Array.Empty<MarketQuoteRecord>());

        Assert.True(records[0].Id < records[1].Id);
        Assert.Equal(records[1].Id, result.ReplayResult.Account.LastTradeLogId);
        Assert.Equal(80, result.ReplayResult.Account.CashBalance!.Value, 2);
        Assert.Equal(80, result.ReplayResult.Account.Principal!.Value, 2);
    }

    [Fact]
    public void MixedCashEtfOtcFeeAndMultiStrategyRecords_ReplayInsideAtomicSave()
    {
        using var database = new TemporaryDatabase();
        IReadOnlyList<TradeLogRecord> records = BuildMixedBusinessRecords();
        IReadOnlyList<MarketQuoteRecord> quotes = Quotes();

        TradeLogAtomicSaveResult result = database.Repository.SaveTradeLogsAndReplayAtomically(
            Array.Empty<long>(),
            records,
            quotes);

        Assert.Equal("正常", result.ReplayResult.Account.ReplayStatus);
        Assert.Equal(9811, result.ReplayResult.Account.CashBalance!.Value, 2);
        Assert.Equal(2, result.ReplayResult.Positions.Count);
        Assert.Single(result.ReplayResult.OtcPositions);
        Assert.Equal(records.Count, database.Repository.ReadTradeLogs().Count);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void LargeMixedBatch_CompletesInOneAtomicSaveWithoutDeadlock(int count)
    {
        using var database = new TemporaryDatabase();
        IReadOnlyList<TradeLogRecord> records = BuildLargeMixedBatch(count);

        TradeLogAtomicSaveResult result = database.Repository.SaveTradeLogsAndReplayAtomically(
            Array.Empty<long>(),
            records,
            BuildLargeBatchQuotes());

        Assert.True(result.Committed);
        Assert.Equal(count, result.Identities.Count);
        Assert.Equal(count, database.Repository.ReadTradeLogs().Count);
        Assert.NotEqual("财务异常", result.ReplayResult.Account.ReplayStatus);
    }

    [Fact]
    public void AtomicRepositoryPath_UsesPassedQuoteSnapshotAndDoesNotCallNetworkStrategyOrDraftServices()
    {
        string source = ReadRepositoryFile("Infrastructure", "Persistence", "LocalDataRepository.cs");
        string method = Slice(
            source,
            "internal TradeLogAtomicSaveResult SaveTradeLogsAndReplayAtomically(",
            "private static void SaveTradeLogsSnapshot(");

        Assert.Contains("using var connection = _database.OpenConnection()", method, StringComparison.Ordinal);
        Assert.Contains("using var transaction = connection.BeginTransaction()", method, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(method, "transaction.Commit()"));
        Assert.DoesNotContain("ReadMarketQuoteCache", method, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketData", method, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", method, StringComparison.Ordinal);
        Assert.DoesNotContain("StrategyDecisionService", method, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderDraftService", method, StringComparison.Ordinal);
        Assert.Contains("writeRuntimeLog: false", method, StringComparison.Ordinal);
    }

    private static SeededScenario CreateSeededScenario()
    {
        var database = new TemporaryDatabase();
        var seed = new List<TradeLogRecord>
        {
            Log("2026-07-16 09:00:00", "CASH", "CASH", cashBalance: 10000, principal: 10000),
            Log("2026-07-16 09:30:00", "159941", "买入", "159941", 1, 100, 100, source: "场内ETF"),
            Log("2026-07-16 09:31:00", "159509", "买入", "017091", 1, 50, 50, source: "场外替代"),
            Log("2026-07-16 09:32:00", "159941", "分红", "159941", amount: 5, source: "场内ETF", memo: "delete-me")
        };
        IReadOnlyList<MarketQuoteRecord> quotes = Quotes();
        database.Repository.SaveTradeLogsAndReplayAtomically(Array.Empty<long>(), seed, quotes);

        TradeLogRecord[] persisted = database.Repository.ReadTradeLogs().Select(Clone).ToArray();
        long deleteId = persisted.Single(record => record.Memo == "delete-me").Id;
        var candidate = persisted
            .Where(record => record.Id != deleteId)
            .Select(Clone)
            .ToList();
        candidate.Add(Log("2026-07-16 09:33:00", "159509", "分红", "017091", amount: 7, source: "场外替代"));
        return new SeededScenario(database, candidate, quotes, deleteId);
    }

    private static IReadOnlyList<TradeLogRecord> BuildMixedBusinessRecords()
        => new[]
        {
            Log("2026-07-16 09:00:00", "CASH", "CASH", cashBalance: 10000, principal: 10000),
            Log("2026-07-16 09:30:00", "159941", "买入", "159941", 1, 100, 100, source: "场内ETF", fee: 1),
            Log("2026-07-16 09:30:00", "159941", "卖出", "159941", 1.25, 40, 50, source: "场内ETF", fee: 1),
            Log("2026-07-16 09:30:00", "159509", "买入", "017091", 1.2, 50, 60, source: "场外替代", fee: 1),
            Log("2026-07-16 09:30:00", "159509", "卖出", "017091", 1.25, 20, 25, source: "场外替代", fee: 1),
            Log("2026-07-16 10:00:00", "CASH", "出金", amount: 100, netCashImpact: -100)
        };

    private static IReadOnlyList<TradeLogRecord> BuildLargeMixedBatch(int count)
    {
        var records = new List<TradeLogRecord>(count)
        {
            Log("2026-07-16 09:00:00", "CASH", "CASH", cashBalance: 1000000, principal: 1000000)
        };

        for (int index = 1; index < count; index++)
        {
            int phase = (index - 1) % 5;
            int strategyIndex = ((index - 1) / 5) % 10;
            string strategy = $"S{strategyIndex:00}";
            string time = $"2026-07-16 10:{((index - 1) / 5) % 60:00}:00";
            records.Add(phase switch
            {
                0 => Log(time, strategy, "买入", $"E{strategyIndex:00}", 1, 1, 1, source: "场内ETF"),
                1 => Log(time, strategy, "卖出", $"E{strategyIndex:00}", 1, 1, 1, source: "场内ETF"),
                2 => Log(time, strategy, "买入", $"O{strategyIndex:00}", 1, 1, 1, source: "场外替代"),
                3 => Log(time, strategy, "卖出", $"O{strategyIndex:00}", 1, 1, 1, source: "场外替代"),
                _ => Log(time, strategy, "分红", $"E{strategyIndex:00}", amount: 1, source: "场内ETF")
            });
        }

        return records;
    }

    private static IReadOnlyList<MarketQuoteRecord> BuildLargeBatchQuotes()
    {
        var quotes = new List<MarketQuoteRecord>();
        for (int index = 0; index < 10; index++)
        {
            quotes.Add(Quote($"E{index:00}", "ETF", 1));
            quotes.Add(Quote($"O{index:00}", "OTC", 1));
        }

        return quotes;
    }

    private static IReadOnlyList<MarketQuoteRecord> Quotes()
        => new[]
        {
            Quote("159941", "ETF", 1.2),
            Quote("017091", "OTC", 1.1)
        };

    private static MarketQuoteRecord Quote(string symbol, string marketType, double price)
        => new()
        {
            Symbol = symbol,
            MarketType = marketType,
            Source = "TEST",
            Price = price,
            ReceivedAt = "2026-07-16 15:00:00",
            QuoteTime = "2026-07-16 15:00:00"
        };

    private static TradeLogRecord Log(
        string time,
        string strategyCode,
        string action,
        string? actualCode = null,
        double price = 0,
        double quantity = 0,
        double amount = 0,
        string? source = null,
        double fee = 0,
        double netCashImpact = 0,
        double principal = 0,
        double cashBalance = 0,
        string? memo = null)
        => new()
        {
            Time = time,
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Action = action,
            Price = price,
            Quantity = quantity,
            Amount = amount,
            Source = source,
            Fee = fee,
            NetCashImpact = netCashImpact,
            Principal = principal,
            CashBalance = cashBalance,
            Memo = memo
        };

    private static TradeLogRecord Clone(TradeLogRecord record)
        => new()
        {
            Id = record.Id,
            Time = record.Time,
            StrategyCode = record.StrategyCode,
            ActualCode = record.ActualCode,
            Action = record.Action,
            Price = record.Price,
            Quantity = record.Quantity,
            Amount = record.Amount,
            Tier = record.Tier,
            Source = record.Source,
            Fee = record.Fee,
            Memo = record.Memo,
            NetCashImpact = record.NetCashImpact,
            Principal = record.Principal,
            CashBalance = record.CashBalance,
            TotalAssets = record.TotalAssets
        };

    private static string CoreStateHash(string databasePath)
    {
        var builder = new StringBuilder();
        using var connection = OpenReadOnly(databasePath);
        foreach (string table in CoreTables)
        {
            builder.Append('[').Append(table).Append(']');
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY id;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                for (int index = 0; index < reader.FieldCount; index++)
                {
                    builder.Append('|').Append(CanonicalValue(reader.GetValue(index)));
                }

                builder.AppendLine();
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static int CountRows(string databasePath, string table)
    {
        using var connection = OpenReadOnly(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static SqliteConnection OpenReadOnly(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static string CanonicalValue(object value)
        => value switch
        {
            DBNull => "NULL",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    private static string ReadRepositoryFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray()));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CrossETF.Terminal.UiShell.Reference.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);
        int end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to slice {startMarker} -> {endMarker}");
        return text[start..end];
    }

    private static int CountOccurrences(string text, string value)
        => (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    private sealed class ThrowingFaultInjector(TradeLogAtomicSaveStage targetStage) : ITradeLogAtomicSaveFaultInjector
    {
        public void OnStage(TradeLogAtomicSaveStage stage, int itemIndex = -1)
        {
            if (stage == targetStage)
            {
                throw new InjectedTradeLogAtomicSaveException($"Injected at {stage}/{itemIndex}");
            }
        }
    }

    private sealed class InjectedTradeLogAtomicSaveException(string message) : Exception(message);

    private sealed class TemporaryDatabase : IDisposable
    {
        public TemporaryDatabase()
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_trade_log_atomic_{Guid.NewGuid():N}.db");
            Repository = new LocalDataRepository(new LocalDatabase(DatabasePath));
        }

        public string DatabasePath { get; }

        public LocalDataRepository Repository { get; }

        public void Dispose()
        {
            TryDelete(DatabasePath);
            TryDelete(DatabasePath + "-shm");
            TryDelete(DatabasePath + "-wal");
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class SeededScenario : IDisposable
    {
        private readonly TemporaryDatabase _database;

        public SeededScenario(
            TemporaryDatabase database,
            List<TradeLogRecord> candidateRecords,
            IReadOnlyList<MarketQuoteRecord> quotes,
            long deleteId)
        {
            _database = database;
            CandidateRecords = candidateRecords;
            Quotes = quotes;
            DeleteId = deleteId;
        }

        public string DatabasePath => _database.DatabasePath;

        public LocalDataRepository Repository => _database.Repository;

        public List<TradeLogRecord> CandidateRecords { get; }

        public IReadOnlyList<MarketQuoteRecord> Quotes { get; }

        public long DeleteId { get; }

        public void Dispose() => _database.Dispose();
    }
}
