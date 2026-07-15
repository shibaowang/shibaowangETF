using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

public sealed partial class LocalDataRepository
{
    private static readonly string[] IndicatorDrawdownSources =
    {
        MarketSources.Tencent,
        MarketSources.EastMoney,
        MarketSources.TencentHistory,
        MarketSources.EastMoneyHistory
    };

    public IndicatorDrawdownReadModel ReadIndicatorDrawdownReadModel()
    {
        try
        {
            using SqliteConnection connection = OpenIndicatorDrawdownReadOnlyConnection();
            EnableIndicatorDrawdownQueryOnly(connection);
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
            IReadOnlyList<StrategyConfigRecord> strategies = ReadIndicatorDrawdownStrategies(connection, transaction);
            IReadOnlyList<IndicatorDrawdownInstrument> instruments = IndicatorDrawdownSnapshotBuilder.BuildInstruments(strategies);
            IReadOnlyList<MarketQuoteRecord> quotes = ReadIndicatorDrawdownQuotes(connection, transaction, instruments);
            IReadOnlyList<MarketSourceStatusRecord> statuses = ReadIndicatorDrawdownStatuses(connection, transaction);
            IReadOnlyList<IndicatorDrawdownHistoryCandidate> history = ReadIndicatorDrawdownHistoryCandidates(connection, transaction, instruments);
            IReadOnlyList<IndicatorDrawdownHistoryMetadata> metadata = history.Select(ToMetadata).ToArray();
            transaction.Commit();
            return new IndicatorDrawdownReadModel
            {
                Strategies = strategies,
                Instruments = instruments,
                Quotes = quotes,
                SourceStatuses = statuses,
                HistoryCandidates = history,
                HistoryMetadata = metadata,
                ReadAt = DateTimeOffset.Now
            };
        }
        catch (Exception ex)
        {
            return new IndicatorDrawdownReadModel
            {
                ReadAt = DateTimeOffset.Now,
                ReadError = ex.Message
            };
        }
    }

    public IndicatorDrawdownRealtimeReadModel ReadIndicatorDrawdownRealtimeState(bool includeHistoryMetadata = false)
    {
        try
        {
            using SqliteConnection connection = OpenIndicatorDrawdownReadOnlyConnection();
            EnableIndicatorDrawdownQueryOnly(connection);
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
            IReadOnlyList<StrategyConfigRecord> strategies = ReadIndicatorDrawdownStrategies(connection, transaction);
            IReadOnlyList<IndicatorDrawdownInstrument> instruments = IndicatorDrawdownSnapshotBuilder.BuildInstruments(strategies);
            IReadOnlyList<MarketQuoteRecord> quotes = ReadIndicatorDrawdownQuotes(connection, transaction, instruments);
            IReadOnlyList<MarketSourceStatusRecord> statuses = ReadIndicatorDrawdownStatuses(connection, transaction);
            IReadOnlyList<IndicatorDrawdownHistoryMetadata> metadata = includeHistoryMetadata
                ? ReadIndicatorDrawdownHistoryMetadata(connection, transaction, instruments)
                : Array.Empty<IndicatorDrawdownHistoryMetadata>();
            transaction.Commit();
            return new IndicatorDrawdownRealtimeReadModel
            {
                Strategies = strategies,
                Instruments = instruments,
                Quotes = quotes,
                SourceStatuses = statuses,
                HistoryMetadata = metadata,
                ReadAt = DateTimeOffset.Now
            };
        }
        catch (Exception ex)
        {
            return new IndicatorDrawdownRealtimeReadModel
            {
                ReadAt = DateTimeOffset.Now,
                ReadError = ex.Message
            };
        }
    }

    public IReadOnlyList<IndicatorDrawdownHistoryCandidate> ReadIndicatorDrawdownHistoryCandidates(
        IReadOnlyCollection<IndicatorDrawdownInstrument> instruments)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        using SqliteConnection connection = OpenIndicatorDrawdownReadOnlyConnection();
        EnableIndicatorDrawdownQueryOnly(connection);
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        IReadOnlyList<IndicatorDrawdownHistoryCandidate> candidates = ReadIndicatorDrawdownHistoryCandidates(
            connection,
            transaction,
            instruments);
        transaction.Commit();
        return candidates;
    }

    private SqliteConnection OpenIndicatorDrawdownReadOnlyConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _database.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void EnableIndicatorDrawdownQueryOnly(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only = ON;";
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<MarketQuoteRecord> ReadIndicatorDrawdownQuotes(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<IndicatorDrawdownInstrument> instruments)
    {
        using SqliteCommand command = CreateIndicatorDrawdownCommand(connection, transaction);
        string targetPredicate = AddInstrumentTargetPredicate(command, instruments, "market_type", "symbol", "$quote");
        command.CommandText = $"""
            SELECT id, symbol, display_name, market_type, source, price, last_close, change_value, change_percent,
                   high_value, low_value, open_value, volume, amount, iopv, quote_time, received_at, raw_code, raw_payload
            FROM market_quote_cache
            WHERE {targetPredicate}
            ORDER BY market_type, symbol, received_at DESC, id DESC;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var records = new List<MarketQuoteRecord>();
        while (reader.Read())
        {
            records.Add(ReadMarketQuote(reader));
        }

        return records;
    }

    private static IReadOnlyList<StrategyConfigRecord> ReadIndicatorDrawdownStrategies(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = CreateIndicatorDrawdownCommand(connection, transaction);
        command.CommandText = """
            SELECT id, code, name, index_sec_id, enabled, created_at, updated_at
            FROM strategy_config
            WHERE enabled = 1
            ORDER BY code COLLATE NOCASE, id;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var records = new List<StrategyConfigRecord>();
        while (reader.Read())
        {
            records.Add(new StrategyConfigRecord
            {
                Id = reader.GetInt64(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                IndexSecId = OptionalString(reader, 3),
                Enabled = reader.GetInt64(4) != 0,
                CreatedAt = reader.GetString(5),
                UpdatedAt = reader.GetString(6)
            });
        }

        return records;
    }

    private static IReadOnlyList<MarketSourceStatusRecord> ReadIndicatorDrawdownStatuses(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = CreateIndicatorDrawdownCommand(connection, transaction);
        string[] parameters = IndicatorDrawdownSources.Select((source, index) =>
        {
            string name = "$source" + index.ToString(CultureInfo.InvariantCulture);
            command.Parameters.AddWithValue(name, source);
            return name;
        }).ToArray();
        command.CommandText = $"""
            SELECT id, source, status, last_success_at, last_failure_at, failure_count, cooldown_until, last_error, updated_at
            FROM market_source_status
            WHERE source IN ({string.Join(",", parameters)})
            ORDER BY source, updated_at DESC, id DESC;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var records = new List<MarketSourceStatusRecord>();
        while (reader.Read())
        {
            records.Add(new MarketSourceStatusRecord
            {
                Id = reader.GetInt64(0),
                Source = reader.GetString(1),
                Status = reader.GetString(2),
                LastSuccessAt = OptionalString(reader, 3),
                LastFailureAt = OptionalString(reader, 4),
                FailureCount = (int)reader.GetInt64(5),
                CooldownUntil = OptionalString(reader, 6),
                LastError = OptionalString(reader, 7),
                UpdatedAt = reader.GetString(8)
            });
        }

        return records;
    }

    private static IReadOnlyList<IndicatorDrawdownHistoryCandidate> ReadIndicatorDrawdownHistoryCandidates(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<IndicatorDrawdownInstrument> instruments)
    {
        using SqliteCommand command = CreateIndicatorDrawdownCommand(connection, transaction);
        string targetPredicate = AddInstrumentTargetPredicate(command, instruments, "market_type", "symbol", "$history");
        command.CommandText = $"""
            SELECT id, symbol, market_type, source, cache_date, updated_at, raw_payload, length(COALESCE(raw_payload, ''))
            FROM market_history_cache
            WHERE ({targetPredicate})
              AND source IN ($tencent_history, $eastmoney_history)
            ORDER BY market_type, symbol, updated_at DESC, id DESC;
            """;
        command.Parameters.AddWithValue("$tencent_history", MarketSources.TencentHistory);
        command.Parameters.AddWithValue("$eastmoney_history", MarketSources.EastMoneyHistory);
        using SqliteDataReader reader = command.ExecuteReader();
        var records = new List<IndicatorDrawdownHistoryCandidate>();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            string symbol = reader.GetString(1);
            string marketType = reader.GetString(2);
            string source = reader.GetString(3);
            string cacheDate = reader.GetString(4);
            string updatedAt = reader.GetString(5);
            string? payload = OptionalString(reader, 6);
            int payloadLength = reader.GetInt32(7);
            records.Add(new IndicatorDrawdownHistoryCandidate(
                id,
                symbol,
                marketType,
                source,
                cacheDate,
                updatedAt,
                payload,
                payloadLength,
                BuildIndicatorMetadataSignature(id, symbol, marketType, source, updatedAt, payloadLength)));
        }

        return records;
    }

    private static IReadOnlyList<IndicatorDrawdownHistoryMetadata> ReadIndicatorDrawdownHistoryMetadata(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<IndicatorDrawdownInstrument> instruments)
    {
        using SqliteCommand command = CreateIndicatorDrawdownCommand(connection, transaction);
        string targetPredicate = AddInstrumentTargetPredicate(command, instruments, "market_type", "symbol", "$metadata");
        command.CommandText = $"""
            SELECT id, symbol, market_type, source, cache_date, updated_at, length(COALESCE(raw_payload, ''))
            FROM market_history_cache
            WHERE ({targetPredicate})
              AND source IN ($tencent_history, $eastmoney_history)
            ORDER BY market_type, symbol, updated_at DESC, id DESC;
            """;
        command.Parameters.AddWithValue("$tencent_history", MarketSources.TencentHistory);
        command.Parameters.AddWithValue("$eastmoney_history", MarketSources.EastMoneyHistory);
        using SqliteDataReader reader = command.ExecuteReader();
        var records = new List<IndicatorDrawdownHistoryMetadata>();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            string symbol = reader.GetString(1);
            string marketType = reader.GetString(2);
            string source = reader.GetString(3);
            string cacheDate = reader.GetString(4);
            string updatedAt = reader.GetString(5);
            int payloadLength = reader.GetInt32(6);
            records.Add(new IndicatorDrawdownHistoryMetadata(
                id,
                symbol,
                marketType,
                source,
                cacheDate,
                updatedAt,
                payloadLength,
                BuildIndicatorMetadataSignature(id, symbol, marketType, source, updatedAt, payloadLength)));
        }

        return records;
    }

    private static string AddInstrumentTargetPredicate(
        SqliteCommand command,
        IEnumerable<IndicatorDrawdownInstrument> instruments,
        string marketTypeColumn,
        string symbolColumn,
        string parameterPrefix)
    {
        string[] predicates = instruments
            .GroupBy(instrument => instrument.MarketType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group, groupIndex) =>
            {
                string marketParameter = parameterPrefix + "_market_" + groupIndex.ToString(CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue(marketParameter, group.Key);
                string[] symbolParameters = group
                    .Select(instrument => instrument.Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                    .Select((code, symbolIndex) =>
                    {
                        string name = parameterPrefix + "_symbol_" + groupIndex.ToString(CultureInfo.InvariantCulture)
                                      + "_" + symbolIndex.ToString(CultureInfo.InvariantCulture);
                        command.Parameters.AddWithValue(name, code);
                        return name;
                    }).ToArray();
                return $"({marketTypeColumn} = {marketParameter} AND {symbolColumn} IN ({string.Join(",", symbolParameters)}))";
            }).ToArray();
        return predicates.Length == 0 ? "1 = 0" : string.Join(" OR ", predicates);
    }

    private static SqliteCommand CreateIndicatorDrawdownCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        return command;
    }

    private static IndicatorDrawdownHistoryMetadata ToMetadata(IndicatorDrawdownHistoryCandidate candidate)
        => new(
            candidate.Id,
            candidate.Symbol,
            candidate.MarketType,
            candidate.Source,
            candidate.CacheDate,
            candidate.UpdatedAt,
            candidate.PayloadLength,
            candidate.MetadataSignature);

    private static string BuildIndicatorMetadataSignature(
        long id,
        string symbol,
        string marketType,
        string source,
        string updatedAt,
        int payloadLength)
        => string.Join("|",
            source,
            marketType,
            symbol,
            id.ToString(CultureInfo.InvariantCulture),
            updatedAt,
            payloadLength.ToString(CultureInfo.InvariantCulture));
}
